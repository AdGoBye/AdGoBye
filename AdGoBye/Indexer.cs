using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
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
    public required ContentVersionMeta VersionMeta { get; init; }
    public required string StableContentName;

    public struct ContentVersionMeta
    {
        public required int Version;
        public required string Path;
        public List<string> PatchedBy;
    }
}

public record struct IndexSerializationRoot
{
    public string Hash { get; init; }
    public List<Content> IndexContent { get; init; }
}

public class Indexer
{
    private static readonly ILogger Logger = Log.ForContext(typeof(Indexer));
    public static readonly string WorkingDirectory = GetWorkingDirectory();
    public static readonly List<Content> Index = PopulateIndex();

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

        Logger.Information("Index is stale, regenerating...");
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

    private static int GetVersion(string hexVersion)
    {
        var hex = hexVersion.TrimStart('0');
        if (hex.Length % 2 != 0) hex = '0' + hex;
        var bytes = Convert.FromHexString(hex);
        return BitConverter.ToInt32(bytes);
    }

    /*private static List<Content> GenerateIndexContents(Dictionary<string, List<DirectoryInfo>> directoryIndex)
    {
        var contentList = new List<Content>();
        Parallel.ForEach(directoryIndex, directory =>
        {
            // Hack: Using the shared ILogger Logger causes the Parallel.ForEach to never complete
            //       As workaround, we're giving it its own ILogger which should be identical for the user
            //       And preserves the same context. Isn't ILogger thread safe?
            var threadLogger = Log.ForContext(typeof(Indexer));

            threadLogger.Verbose("Loading {directory}", directory);
            var content = ParseFileMeta(directory.Value);
            if (content == null) return;
            if (Settings.Options.Allowlist is not null && Settings.Options.Allowlist.Contains(content.Id))
            {
                threadLogger.Information("Skipped {id} for indexation in accordance with Allowlist", content.Id);
                return;
            }

            threadLogger.Verbose("Adding to index: {id} ({type})", content.Id, content.Type);
            contentList.Add(content);
        });

        WriteIndexToDisk(contentList);
        return contentList;
    }*/

    private static Dictionary<string, List<DirectoryInfo>> GetDirIndex(string vrcCacheDir)
    {
        // The file structure of Cache is roughly like this:
        //   - Folder (\w{16}) [We have this]
        //       - Folder (0{24}\w{8}) [We want path of this, don't know random name though]
        //          - __info
        //          - __data
        //          - __lock (if currently used)

        var dirs = new DirectoryInfo(vrcCacheDir).GetDirectories("*", SearchOption.AllDirectories)
            .Where(info => info.Parent is { Name: not "Cache-WindowsPlayer" });

        return dirs.GroupBy(info => info.Parent!.Name).ToDictionary(info => info.Key, info => info.ToList());
    }

    private static List<Content> DirIndexToContent(Dictionary<string, List<DirectoryInfo>> DirIndex)
    {
        var ContentList = new List<Content>();
        foreach (var file in DirIndex)
        {
            var highestVersion = 0;
            var highestVersionDir = "";
            foreach (var directory in file.Value)
            {
                var version = GetVersion(directory.Name);
                if (version < highestVersion) continue;
                highestVersion = version;
                highestVersionDir = directory.FullName;
            }


            string id;
            int type;
            try
            {
                (id, type) = ParseFileMeta(highestVersionDir)!;
            }
            catch (NullReferenceException) // null is a parsing issue from ParseFileMeta's side
            {
                continue;
            }

            ContentList.Add(new Content
            {
                Id = id,
                Type = (ContentType)type,
                VersionMeta = new Content.ContentVersionMeta
                {
                    Version = highestVersion,
                    Path = highestVersionDir
                },
                StableContentName = file.Value[0].Parent!.Name
            });
        }

        return ContentList;
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

        foreach (var gameObjectInfo in assetFile.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var gameObjectBase = manager.GetBaseField(assetInstance, gameObjectInfo);
            if (gameObjectBase["blueprintId"].IsDummy || gameObjectBase["contentType"].IsDummy) continue;

            if (gameObjectBase["blueprintId"].AsString == "")
            {
                Logger.Warning("{directory} has no embedded ID for some reason, skipping this…", path);
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