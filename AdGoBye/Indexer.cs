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
    public required string Path { get; init; }
}

public record struct IndexSerializationRoot
{
    public string Hash { get; init; }
    public List<Content> IndexContent { get; init; }
}

public class Indexer
{
    public static readonly string WorkingDirectory = GetWorkingDirectory();
    public static readonly List<Content> Index = PopulateIndex();
    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { IncludeFields = true };

    private static List<Content> PopulateIndex()
    {
        var directoryIndex = GetDirIndex(GetCacheDir());

        if (File.Exists("cache.json"))
        {
            var diskCache = JsonSerializer.Deserialize<IndexSerializationRoot>(File.ReadAllText("cache.json"));
            if (GetCurrentDirectoryHash() == diskCache.Hash)
            {
                if (Settings.Options.Allowlist is null) return diskCache.IndexContent;
                var sanitizedList = RemoveAllowlistItems(diskCache.IndexContent);
                WriteIndexToDisk(sanitizedList);
                return sanitizedList;
            }
        }

        Log.Information("Index is stale, regenerating...");
        return GenerateIndexContents(directoryIndex);

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

    private static List<Content> GenerateIndexContents(IEnumerable<string> directoryIndex)
    {
        var contentList = new List<Content>();
        Parallel.ForEach(directoryIndex, directory =>
        {
            Log.Verbose("Loading {directory}", directory);
            var content = ParseFile(directory + "/__data");
            if (content == null) return;
            if (Settings.Options.Allowlist is not null && Settings.Options.Allowlist.Contains(content.Id))
            {
                Log.Information("Skipped {id} for indexation in accordance with Allowlist", content.Id);
                return;
            }

            Log.Verbose("Adding to index: {id} ({type})", content.Id, content.Type);
            contentList.Add(content);
        });

        WriteIndexToDisk(contentList);
        return contentList;
    }

    public static void WriteIndexToDisk()
    {
        var currentHash = GetCurrentDirectoryHash();
        var serialized = JsonSerializer.Serialize(new IndexSerializationRoot
        {
            Hash = currentHash,
            IndexContent = Index
        }, JsonSerializerOptions);
        File.WriteAllText("cache.json", serialized);
        Log.Verbose("Wrote cache file to disk");
    }

    private static string GetCurrentDirectoryHash()
    {
        var directoryIndex = GetDirIndex(GetCacheDir());
        var currentHash = DirectoryIndexToHash(directoryIndex);
        return currentHash;
    }

    private static void WriteIndexToDisk(List<Content> contentList)
    {
        var currentHash = GetCurrentDirectoryHash();
        var serialized = JsonSerializer.Serialize(new IndexSerializationRoot
        {
            Hash = currentHash,
            IndexContent = contentList
        }, JsonSerializerOptions);
        File.WriteAllText("cache.json", serialized);
        Log.Verbose("Wrote cache file to disk");
    }

    private static string DirectoryIndexToHash(IEnumerable<string> directories)
    {
        var directoryString = System.Text.Encoding.UTF8.GetBytes(string.Join("", directories));
        return System.Text.Encoding.UTF8.GetString(SHA256.HashData(directoryString));
    }

    private static List<string> GetDirIndex(string vrcCacheDir)
    {
        // The file structure of Cache is roughly like this:
        //   - Folder (\w{16}) [We have this]
        //       - Folder (0{24}\w{8}) [We want path of this, don't know random name though]
        //          - __info
        //          - __data
        //          - __lock (if currently used, not actually filesystem file locked)

        var dirs = new List<string>();
        foreach (var dir in new DirectoryInfo(vrcCacheDir).GetDirectories("*", SearchOption.TopDirectoryOnly))
        {
            var subDirs = dir.GetDirectories();
            switch (subDirs.Length)
            {
                case 0:
                    continue;
                case 1:
                    dirs.Add(subDirs[0].FullName);
                    continue;
                default:
                    dirs.Add(subDirs.OrderByDescending(directory => directory.CreationTimeUtc).First().FullName);
                    break;
            }
        }

        return dirs;
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
            Log.Fatal("We're unable to find your game's working folder (the folder above the cache), " +
                      "please provide it manually in appsettings.json as 'WorkingFolder'.");
            throw e;
        }
    }

    private static string GetCacheDir()
    {
        return WorkingDirectory + "/Cache-WindowsPlayer/";
    }


    public static Content? ParseFile(string directory)
    {
        AssetsManager manager = new();
        BundleFileInstance bundleInstance;

        try
        {
            bundleInstance = manager.LoadBundleFile(directory);
        }
        catch (NotImplementedException e)
        {
            if (e.Message != "Cannot handle bundles with multiple block sizes yet.") throw;
            Log.Warning(
                "{directory} has multiple block sizes, AssetsTools can't handle this yet. Skipping... ",
                directory);
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
            Log.Warning(
                $"Indexing {directory} caused no loadable bundle directory to exist, is this bundle valid?",
                directory);
            return null;
        }

        var assetFile = assetInstance.file;
        var id = "";
        int type = 0;
        foreach (var gameObjectInfo in assetFile.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var gameObjectBase = manager.GetBaseField(assetInstance, gameObjectInfo);
            if (gameObjectBase["blueprintId"].IsDummy || gameObjectBase["contentType"].IsDummy) continue;
            id = gameObjectBase["blueprintId"].AsString;
            if (id == "") continue;

            type = gameObjectBase["contentType"].AsInt;
            break;
        }

        if (id == "")
        {
            Log.Warning("{directory} has no embedded ID for some reason, skipping this…", directory);
            return null;
        }

        if (type >= 3)
        {
            Log.Warning(
                "{directory} is neither Avatar nor World but another secret other thing ({type}), skipping this...",
                directory, type);
            return null;
        }


        return new Content
        {
            Id = id,
            Path = directory,
            Type = (ContentType)type
        };
    }

    public static void PatchContent(Content content)
    {
        Log.Information("Patching {ID} ({path})", content.Id, content.Path);
        foreach (var plugin in PluginLoader.LoadedPlugins)
        {
            var pluginApplies = plugin.Instance.PluginType() == EPluginType.Global;
            if (!pluginApplies && plugin.Instance.PluginType() == EPluginType.ContentSpecific)
            {
                var ctIds = plugin.Instance.ResponsibleForContentIds();
                if (ctIds != null) pluginApplies = ctIds.Contains(content.Id);
            }

            if (pluginApplies) plugin.Instance.Patch(content.Id, content.Path.Replace("__data", ""));
        }

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - False positive
        if (Blocklist.Blocks is null) return;
        foreach (var block in Blocklist.Blocks.Where(block => block.Key.Equals(content.Id)))
        { 
            Blocklist.Patch(content.Path, block.Value.ToArray());
        }
    }
}