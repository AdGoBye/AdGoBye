using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdGoBye.Database;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdGoBye;

public class Indexer
{
    private readonly ILogger<Indexer> _logger;
    private readonly Settings.IndexerOptions _options;
    private readonly Settings.SettingsOptionsV2 _optionsGlobal;
    public readonly string WorkingDirectory;

    public Indexer(ILogger<Indexer> logger, IOptions<Settings.IndexerOptions> options, IOptions<Settings.SettingsOptionsV2> optionsGlobal)
    {
        _logger = logger;

        _options = options.Value;
        _optionsGlobal = optionsGlobal.Value;
        WorkingDirectory = GetWorkingDirectory();
        ManageIndex();
    }

    private void ManageIndex()
    {
        using var db = new AdGoByeContext();
        var container = new DatabaseOperationsContainer();
        if (db.Content.Any()) VerifyDbTruth(ref container);
        CommitToDatabase(container);

        const int maxRetries = 3;
        const int delayMilliseconds = 5000;

        DirectoryInfo[] contentFolders = [];
        for (var retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                contentFolders = new DirectoryInfo(GetCacheDir()).GetDirectories();
                break;
            }
            catch (DirectoryNotFoundException ex)
            {
                // print exception with exception message if it's the last retry
                if (retry == maxRetries - 1)
                {
                    _logger.LogCritical(ex,
                        "Max retries reached. Unable to find your game's Cache directory, please define the folder above manually in appsettings.json as 'WorkingFolder'.");
                    Environment.Exit(1);
                }
                else
                {
                    _logger.LogCritical($"Directory not found attempting retry: {retry + 1} of {maxRetries}");
                }

                Thread.Sleep(delayMilliseconds);
            }
        }

        if (contentFolders.Length == db.Content.Count() - SafeAllowlistCount()) return;

        var content = contentFolders
            .ExceptBy(db.Content.Select(content => content.StableContentName), info => info.Name)
            .Select(newContent => GetLatestFileVersion(newContent).HighestVersionDirectory)
            // If we do a conditional check against null, the type checker thinks it's still null  
            .OfType<DirectoryInfo>().ToList();

        if (content.Count != 0) AddToIndex(content);

        _logger.LogInformation("Finished Index processing");
        return;

        int SafeAllowlistCount()
        {
            return _options.Allowlist?.Length ?? 0;
        }
    }

    private void VerifyDbTruth(ref DatabaseOperationsContainer container)
    {
        using var db = new AdGoByeContext();
        foreach (var content in db.Content.Include(content => content.VersionMeta))
        {
            var directoryMeta = new DirectoryInfo(content.VersionMeta.Path);
            if (!directoryMeta.Parent!.Exists) // This content doesn't have a StableContentId folder anymore
            {
                container.RemoveContent.Add(content);
                continue;
            }

            // We know the content is still being tracked but we don't know if its actually relevant
            // so we'll resolve every version to determine the highest and mutate based on that
            var (highestVersionDir, highestVersion) = GetLatestFileVersion(directoryMeta.Parent);
            if (highestVersionDir is null)
            {
                _logger.LogWarning(
                    "{parentDir} ({id}) is lingering with no content versions and should be deleted, Skipping for now",
                    directoryMeta.Parent, content.Id);
                continue;
            }


            if (!File.Exists(highestVersionDir.FullName + "/__data"))
            {
                container.RemoveContent.Add(content);
                _logger.LogWarning(
                    "{directory} is highest version but doesn't have __data, hell might have frozen over. Removed from Index",
                    highestVersionDir.FullName);
                continue;
            }

            if (highestVersion <= content.VersionMeta.Version && Directory.Exists(content.VersionMeta.Path)) continue;
            content.VersionMeta.Version = highestVersion;
            content.VersionMeta.Path = highestVersionDir.FullName;
            content.VersionMeta.PatchedBy = [];
            container.EditContent.Add(content);
        }
    }

    public void AddToIndex(string path)
    {
        var container = new DatabaseOperationsContainer();
        AddToIndex(path, ref container);
        CommitToDatabase(container);
    }

    public void AddToIndex(IEnumerable<DirectoryInfo> paths)
    {
        var dbActionsContainer = new DatabaseOperationsContainer();
        Parallel.ForEach(paths, new ParallelOptions
            {
                MaxDegreeOfParallelism = _optionsGlobal.MaxIndexerThreads
            },
            path => AddToIndex(path.FullName, ref dbActionsContainer));
        CommitToDatabase(dbActionsContainer);
    }

    private void AddToIndex(string path, ref DatabaseOperationsContainer container)
    {
        using var db = new AdGoByeContext();
        //   - Folder (StableContentName) [singleton, we want this]
        //       - Folder (version) [may exist multiple times] 
        //          - __info
        //          - __data 
        //          - __lock (if currently used)
        var directory = new DirectoryInfo(path);
        if (directory.Name == "__data") directory = directory.Parent;

        // If we already have an item in Index that has the same StableContentName, then this is a newer version of something Indexed
        var content = db.Content.Include(content => content.VersionMeta)
            .FirstOrDefault(content => content.StableContentName == directory!.Parent!.Name);
        if (content is not null)
        {
            var version = GetVersion(directory!.Name);
            if (version < content.VersionMeta.Version)
            {
                _logger.LogTrace(
                    "Skipped Indexation of {directory} since it isn't an upgrade (Index: {greaterVersion}, Parsed: {lesserVersion})",
                    directory.FullName, content.VersionMeta.Version, version);
                return;
            }

            content.VersionMeta.Version = version;
            content.VersionMeta.Path = directory.FullName;
            content.VersionMeta.PatchedBy = [];
            container.EditContent.Add(content);
            return;
        }

        if (!File.Exists(directory!.FullName + "/__data")) return;
        content = FileToContent(directory);
        if (content is null) return;

        var indexCopy = db.Content.Include(existingFile => existingFile.VersionMeta)
            .FirstOrDefault(existingFile => existingFile.Id == content.Id);

        if (indexCopy is null)
        {
            if (content.Type is ContentType.Avatar && IsAvatarImposter(content.VersionMeta.Path))
            {
                _logger.LogInformation("Skipping {path} because it's an Imposter avatar", content.VersionMeta.Path);
                return;
            }

            container.AddContent.Add(content);
            _logger.LogInformation("Added {id} [{type}] to Index", content.Id, content.Type);
            return;
        }

        switch (indexCopy.Type)
        {
            // There are two unique cases where content may share a Content ID with a different StableContentName
            // The first is a world Unity version upgrade, in that case we simply use the newer version
            case ContentType.World:
                if (!IsWorldHigherUnityVersion(indexCopy, content)) return;
                _logger.LogInformation("Upgrading {id} since it bumped Unity version ({directory})",
                    indexCopy.Id, content.VersionMeta.Path);
                indexCopy.VersionMeta.Version = content.VersionMeta.Version;
                indexCopy.VersionMeta.Path = content.VersionMeta.Path;
                indexCopy.VersionMeta.PatchedBy = [];
                container.EditContent.Add(indexCopy);
                return;
            // The second is an Imposter avatar, which we don't want to index.
            case ContentType.Avatar:
                if (IsAvatarImposter(content.VersionMeta.Path))
                {
                    _logger.LogInformation("Skipping {path} because it's an Imposter avatar", content.VersionMeta.Path);
                    return;
                }

                break;
            default:
                throw new NotImplementedException();
        }

        return;

        bool IsAvatarImposter(string contentPath)
        {
            AssetsManager manager = new();
            var bundleInstance = manager.LoadBundleFile(contentPath + "/__data");
            var assetInstance = manager.LoadAssetsFileFromBundle(bundleInstance, 0);

            foreach (var monoScript in assetInstance.file.GetAssetsOfType(AssetClassID.MonoScript))
            {
                try
                {
                    var monoScriptBase = manager.GetBaseField(assetInstance, monoScript);
                    if (monoScriptBase["m_ClassName"].IsDummy ||
                        monoScriptBase["m_ClassName"].AsString != "Impostor") continue;
                    return true;
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "AssetsTools likely failed to deserialize MonoBehaviour (PathId: {id}): ",
                        monoScript.PathId);
                }
            }

            return false;
        }

        bool IsWorldHigherUnityVersion(Content indexedContent, Content newContent)
        {
            // Hack: [Regalia 2023-12-25T03:33:18Z] I'm not paid enough to parse Unity version strings reliably
            // This assumes the Unity versions always contains the major version at the start, is seperated by a dot and 
            // the major versions will always be greater to each other. Upcoming Unity 6 will violate this assumption
            // but I'm betting that service provider won't upgrade anytime soon
            var indexedContentVersion = ResolveUnityVersion(indexedContent.VersionMeta.Path).Split(".")[0];
            var newContentVersion = ResolveUnityVersion(newContent.VersionMeta.Path).Split(".")[0];
            return int.Parse(newContentVersion) > int.Parse(indexedContentVersion);
        }

        string ResolveUnityVersion(string contentPath)
        {
            AssetsManager manager = new();
            var bundleInstance = manager.LoadBundleFile(contentPath + "/__data");
            var assetInstance = manager.LoadAssetsFileFromBundle(bundleInstance, 1);

            foreach (var monoScript in assetInstance.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
            {
                try
                {
                    var monoScriptBase = manager.GetBaseField(assetInstance, monoScript);
                    if (monoScriptBase["unityVersion"].IsDummy) continue;
                    return monoScriptBase["unityVersion"].AsString;
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "AssetsTools likely failed to deserialize MonoBehaviour (PathId: {id}): ",
                        monoScript.PathId);
                }
            }

            _logger.LogCritical("ResolveUnityVersion: Unable to parse unityVersion out for {path}", contentPath);
            throw new InvalidOperationException();
        }
    }

    private static void CommitToDatabase(DatabaseOperationsContainer container)
    {
        using var writeDbContext = new AdGoByeContext();
        writeDbContext.Content.AddRange(container.AddContent);
        writeDbContext.Content.UpdateRange(container.EditContent);
        writeDbContext.Content.RemoveRange(container.RemoveContent);
        writeDbContext.SaveChanges();
    }


    public static Content? GetFromIndex(string path)
    {
        using var db = new AdGoByeContext();
        var directory = new DirectoryInfo(path);
        return db.Content.Include(content => content.VersionMeta)
            .FirstOrDefault(content => content.StableContentName == directory.Parent!.Parent!.Name);
    }

    public void RemoveFromIndex(string path)
    {
        using var db = new AdGoByeContext();
        var indexMatch = GetFromIndex(path);
        if (indexMatch is null) return;


        db.Content.Remove(indexMatch);
        db.SaveChanges();
        _logger.LogInformation("Removed {id} from Index", indexMatch.Id);
    }

    private static (DirectoryInfo? HighestVersionDirectory, int HighestVersion) GetLatestFileVersion(
        DirectoryInfo stableNameFolder)
    {
        var highestVersion = 0;
        DirectoryInfo? highestVersionDir = null;
        foreach (var directory in stableNameFolder.GetDirectories())
        {
            var version = GetVersion(directory.Name);
            if (version < highestVersion) continue;
            highestVersion = version;
            highestVersionDir = directory;
        }

        return (highestVersionDir, highestVersion);
    }

    private Content? FileToContent(DirectoryInfo pathToFile)
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
                Path = pathToFile.FullName,
                PatchedBy = []
            },
            StableContentName = pathToFile.Parent!.Name
        };
    }

    public Tuple<string, int>? ParseFileMeta(string path)
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
            _logger.LogWarning(
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
            _logger.LogWarning(
                "Indexing {directory} caused no loadable bundle directory to exist, is this bundle valid?",
                path);
            return null;
        }

        foreach (var monoScript in assetInstance.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            AssetTypeValueField gameObjectBase;

            try
            {
                gameObjectBase = manager.GetBaseField(assetInstance, monoScript);
                if (gameObjectBase["blueprintId"].IsDummy || gameObjectBase["contentType"].IsDummy) continue;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "AssetsTools likely failed to deserialize MonoBehaviour (PathId: {id}): ",
                    monoScript.PathId);
                continue;
            }

            if (gameObjectBase["blueprintId"].AsString == "")
            {
                _logger.LogWarning("{directory} has no embedded ID for some reason, skipping this…", path);
                return null;
            }

            if (gameObjectBase["contentType"].AsInt >= 3)
            {
                _logger.LogWarning(
                    "{directory} is neither Avatar nor World but another secret other thing ({type}), skipping this...",
                    path, gameObjectBase["contentType"].AsInt);
                return null;
            }

            return new Tuple<string, int>(gameObjectBase["blueprintId"].AsString,
                gameObjectBase["contentType"].AsInt);
        }

        return null;
    }

    private static int GetVersion(string hexVersion)
    {
        var hex = hexVersion.TrimStart('0');
        if (hex.Length % 2 != 0) hex = '0' + hex;
        var bytes = Convert.FromHexString(hex);
        while (bytes.Length < 4)
        {
            var newValues = new byte[bytes.Length + 1];
            newValues[0] = 0x00;
            Array.Copy(bytes, 0, newValues, 1, bytes.Length);
            bytes = newValues;
        }

        return BitConverter.ToInt32(bytes);
    }


    [SuppressMessage("ReSharper", "StringLiteralTypo")]
    private string GetWorkingDirectory()
    {
        if (!string.IsNullOrEmpty(_options.WorkingFolder))
            return _options.WorkingFolder;
        var appName = SteamParser.GetApplicationName();
        var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            .Replace("Roaming", "LocalLow");
        var pathToWorkingDir = $@"{appName}\{appName}\";
        var defaultWorkingDir = Path.Combine(appDataFolder, pathToWorkingDir);
        var customWorkingDir = ReadConfigFile(defaultWorkingDir);
        if (!string.IsNullOrEmpty(customWorkingDir))
            return customWorkingDir;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return ConstructLinuxWorkingPath();

        return defaultWorkingDir;

        [SupportedOSPlatform("linux")]
        string ConstructLinuxWorkingPath()
        {
            var protonWorkingPath =
                $"/steamapps/compatdata/{SteamParser.Appid}/pfx/drive_c/users/steamuser/AppData/LocalLow/{pathToWorkingDir}";

            if (string.IsNullOrEmpty(SteamParser.AlternativeLibraryPath))
                return SteamParser.GetPathToSteamRoot() + protonWorkingPath;

            return SteamParser.AlternativeLibraryPath + protonWorkingPath;
        }
    }

    private string? ReadConfigFile(string path)
    {
        var configFile = Path.Combine(path, "config.json");
        if (!File.Exists(configFile))
            return null;

        var json = File.ReadAllText(configFile);
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            var jsonObject = JsonSerializer.Deserialize<JsonObject>(json);
            return jsonObject?["cache_directory"]?.ToString();
        }
        catch (JsonException e)
        {
            _logger.LogError(e, "Failed to parse config.json");
            return null;
        }
    }

    private string GetCacheDir()
    {
        return Path.Combine(WorkingDirectory, "Cache-WindowsPlayer");
    }

    public record DatabaseOperationsContainer
    {
        public ConcurrentBag<Content> AddContent = [];
        public ConcurrentBag<Content> EditContent = [];
        public ConcurrentBag<Content> RemoveContent = [];
    }
}