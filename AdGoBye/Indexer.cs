using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using AdGoBye.Plugins;
using AssetsTools.NET.Extra;
using Microsoft.Win32;
using Serilog;

namespace AdGoBye;

public enum ContentType
{
    Avatar,
    World
}

public record Content
{
    public required string Id { get; init; }
    public required ContentType Type { get; init; }
    public required ContentVersionMeta VersionMeta { get; set; }
    public required string StableContentName { get; init; }

    public struct ContentVersionMeta
    {
        public required int Version { get; set; }
        public required string Path { get; set; }
        public List<string> PatchedBy { get; set; }
    }
}

public class Indexer
{
    private static readonly ILogger Logger = Log.ForContext(typeof(Indexer));
    public static readonly string WorkingDirectory = GetWorkingDirectory();
    public static readonly List<Content> Index = PopulateIndex();

    public static void AddToIndex(string path)
    {
        //   - Folder (StableContentName) [singleton, we want this]
        //       - Folder (version) [may exist multiple times] 
        //          - __info
        //          - __data 
        //          - __lock (if currently used)
        var directory = new DirectoryInfo(path);
        if (directory.Name == "__data") directory = directory.Parent;

        // If we already have an item in Index that has the same StableContentName, then this is a newer version of something Indexed
        var indexMatch = Index.Find(content => content.StableContentName == directory!.Parent!.Parent!.Name);
        if (indexMatch is not null)
        {
            var version = GetVersion(directory!.Parent!.Name);
            if (version > indexMatch.VersionMeta.Version)
            {
                indexMatch.VersionMeta = new Content.ContentVersionMeta
                    { Version = version, Path = path, PatchedBy = new List<string>() };
            }
            else
            {
                Logger.Verbose(
                    "Skipped Indexation of {directory} since it isn't an upgrade (Index: {greaterVersion}, Parsed: {lesserVersion})",
                    directory.FullName, indexMatch.VersionMeta.Version, version);
                return;
            }
        }
        else
        {
            indexMatch = FileToContent(directory!);
            if (indexMatch is not null) Index.Add(indexMatch);
            else return;
        }

        PatchContent(indexMatch);
    }

    public static Content? GetFromIndex(string path)
    {
        var directory = new DirectoryInfo(path);
        return Index.FirstOrDefault(content => content.StableContentName == directory.Parent!.Parent!.Name);
    }

    public static void RemoveFromIndex(string path)
    {
        var indexMatch = GetFromIndex(path);
        if (indexMatch is null) return;

        Index.Remove(indexMatch);
        Logger.Information("Removed {id} from Index", indexMatch.Id);
    }

    private static List<Content> PopulateIndex()
    {
        var directoryIndex = GetDirIndex(GetCacheDir());

        // if (File.Exists("cache.json"))
        // {
        //     var diskCache = JsonSerializer.Deserialize<IndexSerializationRoot>(File.ReadAllText("cache.json"));
        //     if (GetCurrentDirectoryHash() == diskCache.Hash)
        //     {
        //         if (Settings.Options.Allowlist is null) return diskCache.IndexContent;
        //         var sanitizedList = RemoveAllowlistItems(diskCache.IndexContent);
        //         WriteIndexToDisk(sanitizedList);
        //         return sanitizedList;
        //     }
        // }

        Logger.Information("Index does not exist, generating");
        return DirIndexToContent(directoryIndex);

        List<Content> RemoveAllowlistItems(List<Content> diskCache)
        {
            foreach (var removedContent in Settings.Options.Allowlist!)
            {
                if (diskCache.RemoveAll(c => c.Id == removedContent) is not 0)
                {
                    Log.Information("Removed {id} from cached Index in accordance with Allowlist", removedContent);
                }
            }

            return diskCache;
        }
    }

    public static void PatchContent(Content content)
    {
        if (content.Type is not ContentType.World) return;

        var pluginOverridesBlocklist = false;
        foreach (var plugin in PluginLoader.LoadedPlugins)
        {
            if (content.VersionMeta.PatchedBy.Contains(plugin.Name)) continue;

            var pluginApplies = plugin.Instance.PluginType() == EPluginType.Global;
            if (!pluginApplies && plugin.Instance.PluginType() == EPluginType.ContentSpecific)
            {
                var ctIds = plugin.Instance.ResponsibleForContentIds();
                if (ctIds is not null) pluginApplies = ctIds.Contains(content.Id);
            }

            pluginOverridesBlocklist = plugin.Instance.OverrideBlocklist(content.Id);

            if (plugin.Instance.Verify(content.Id, content.VersionMeta.Path) is not EVerifyResult.Success)
                pluginApplies = false;


            if (pluginApplies) plugin.Instance.Patch(content.Id, content.VersionMeta.Path);
            content.VersionMeta.PatchedBy.Add(plugin.Name);
        }

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - False positive
        if (Blocklist.Blocks is null) return;
        if (pluginOverridesBlocklist) return;
        if (content.VersionMeta.PatchedBy.Contains("Blocklist")) return;
        foreach (var block in Blocklist.Blocks.Where(block => block.Key.Equals(content.Id)))
        {
            Blocklist.Patch(content.VersionMeta.Path + "/__data", block.Value.ToArray());
            content.VersionMeta.PatchedBy.Add("Blocklist");
        }
    }

    private static Dictionary<string, List<DirectoryInfo>> GetDirIndex(string vrcCacheDir)
    {
        var dirs = new DirectoryInfo(vrcCacheDir).GetDirectories("*", SearchOption.AllDirectories)
            .Where(info => info.Parent is { Name: not "Cache-WindowsPlayer" });

        return dirs.GroupBy(info => info.Parent!.Name).ToDictionary(info => info.Key, info => info.ToList());
    }

    private static List<Content> DirIndexToContent(Dictionary<string, List<DirectoryInfo>> dirIndex)
    {
        var contentList = new List<Content>();
        Parallel.ForEach(dirIndex, file =>
            {
                var highestVersionDir = GetLatestFileVersion(file);

                if (highestVersionDir is null) // something is horribly wrong if this happens
                {
                    Logger.Verbose(
                        "highestVersionDir was null for after parsing, hell might have frozen over");
                    return;
                }

                var parsed = FileToContent(highestVersionDir);
                if (parsed is null) return;

                contentList.Add(parsed);
            }
        );

        return contentList;
    }

    private static DirectoryInfo? GetLatestFileVersion(KeyValuePair<string, List<DirectoryInfo>> file)
    {
        var highestVersion = 0;
        DirectoryInfo? highestVersionDir = null;
        foreach (var directory in file.Value)
        {
            var version = GetVersion(directory.Name);
            if (version < highestVersion) continue;
            highestVersion = version;
            highestVersionDir = directory;
        }

        return highestVersionDir;
    }

    private static Content? FileToContent(DirectoryInfo pathToFile)
    {
        string id;
        int type;
        try
        {
            (id, type) = ParseFileMeta(pathToFile.FullName)!;
        }
        catch (NullReferenceException) // null is a parsing issue from ParseFileMeta's side
        {
            return null;
        }

        return new Content
        {
            Id = id,
            Type = (ContentType)type,
            VersionMeta = new Content.ContentVersionMeta
            {
                Version = GetVersion(pathToFile.Name),
                Path = pathToFile.FullName
            },
            StableContentName = pathToFile.Parent!.Name
        };
    }

    public static Tuple<string, int>? ParseFileMeta(string path)
    {
        AssetsManager manager = new();
        BundleFileInstance bundleInstance;

        try
        {
            bundleInstance = manager.LoadBundleFile(path + "/__data");
        }
        catch (NotImplementedException e)
        {
            if (e.Message != "Cannot handle bundles with multiple block sizes yet.") throw;
            Logger.Warning(
                "{directory} has multiple block sizes, AssetsTools can't handle this yet. Skipping... ",
                path);
            return null;
        }


        var bundle = bundleInstance!.file;

        var index = 0;
        AssetsFileInstance? assetInstance = null;
        foreach (var bundleDirectoryInfo in bundle.BlockAndDirInfo.DirectoryInfos)
        {
            if (bundleDirectoryInfo.Name.EndsWith(".sharedAssets"))
            {
                index++;
                continue;
            }

            assetInstance = manager.LoadAssetsFileFromBundle(bundleInstance, index);
        }

        if (assetInstance is null)
        {
            Logger.Warning(
                "Indexing {directory} caused no loadable bundle directory to exist, is this bundle valid?",
                path);
            return null;
        }

        var assetFile = assetInstance.file;

        foreach (var gameObjectBase in assetFile.GetAssetsOfType(AssetClassID.MonoBehaviour)
                     .Select(gameObjectInfo => manager.GetBaseField(assetInstance, gameObjectInfo)).Where(
                         gameObjectBase =>
                             !gameObjectBase["blueprintId"].IsDummy && !gameObjectBase["contentType"].IsDummy))
        {
            if (gameObjectBase["blueprintId"].AsString == "")
            {
                Log.Warning("{directory} has no embedded ID for some reason, skipping this…", path);
                return null;
            }

            if (gameObjectBase["contentType"].AsInt >= 3)
            {
                Logger.Warning(
                    "{directory} is neither Avatar nor World but another secret other thing ({type}), skipping this...",
                    path, gameObjectBase["contentType"].AsInt);
                return null;
            }

            return new Tuple<string, int>(gameObjectBase["blueprintId"].AsString, gameObjectBase["contentType"].AsInt);
        }

        return null;
    }

    private static int GetVersion(string hexVersion)
    {
        var hex = hexVersion.TrimStart('0');
        if (hex.Length % 2 != 0) hex = '0' + hex;
        var bytes = Convert.FromHexString(hex);
        return BitConverter.ToInt32(bytes);
    }


    [SuppressMessage("ReSharper", "StringLiteralTypo")]
    private static string GetWorkingDirectory()
    {
        if (!string.IsNullOrEmpty(Settings.Options.WorkingFolder)) return Settings.Options.WorkingFolder;
        var appName = GetApplicationName();
        var pathToCache = "/" + appName + "/" + appName + "/";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return
                $"/home/{Environment.UserName}/.steam/steam/steamapps/compatdata/438100/pfx/drive_c/users/steamuser/AppData/LocalLow" +
                pathToCache;
        }

        var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            .Replace("Roaming", "LocalLow");
        return appDataFolder + pathToCache;
    }

    private static string GetApplicationName()
    {
        const string appid = "438100";

        string? pathToSteamApps = null;

        try
        {
            return ExtractAppName();
        }
        catch (Exception e)
        {
            DieFatally(e);
        }

        throw new InvalidOperationException();

        string ExtractAppName()
        {
            if (OperatingSystem.IsLinux())
            {
                pathToSteamApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) +
                                               "/.steam/steam/steamapps/");
            }
            else if (OperatingSystem.IsWindows())
            {
                var registryKey = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Valve\Steam",
                    "InstallPath",
                    null);

                pathToSteamApps = registryKey!.ToString()!.Replace("steam.exe", "") + @"\steamapps\";
            }

            if (pathToSteamApps is null) throw new InvalidOperationException("couldn't determine pathToSteamApps");
            var line = File.ReadLines(pathToSteamApps + $"appmanifest_{appid}.acf")
                .First(line => line.Contains("name"));
            var words = line.Split("\t");
            return words[3].Replace("\"", "");
        }

        void DieFatally(Exception e)
        {
            Logger.Fatal("We're unable to find your game's working folder (the folder above the cache), " +
                         "please provide it manually in appsettings.json as 'WorkingFolder'.");
            throw e;
        }
    }

    private static string GetCacheDir()
    {
        return WorkingDirectory + "/Cache-WindowsPlayer/";
    }
}