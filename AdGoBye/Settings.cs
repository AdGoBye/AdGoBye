using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace AdGoBye;

public static class Settings
{
    static Settings()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        if (ConvertV1SettingsToV2(config))
            config.Reload();

        Options = config.GetRequiredSection("Settings").Get<SettingsOptionsV2>() ?? new SettingsOptionsV2();
    }

    public static SettingsOptionsV2 Options { get; set; }

    #region appsettings.json conversion methods

    private static bool ConvertV1SettingsToV2(IConfigurationRoot? config)
    {
        if (config == null)
            return false;

        var section = config.GetRequiredSection("Settings");
        if (section == null)
            return false;

        var configVersion = section.GetValue<int?>("ConfigVersion");
        if (configVersion == null)
        {
            var settingsV2 = new SettingsOptionsV2();
            var settingsV1 = section.Get<SettingsOptionsV1>();

            if (settingsV1 == null)
                return false;

            settingsV2.Blocklist.BlocklistUrls = settingsV1.BlocklistUrLs;
            settingsV2.Blocklist.SendUnmatchedObjectsToDevs = settingsV1.SendUnmatchedObjectsToDevs;
            settingsV2.Blocklist.BlocklistUnmatchedServer = settingsV1.BlocklistUnmatchedServer;
            settingsV2.Indexer.WorkingFolder = settingsV1.WorkingFolder;
            settingsV2.Indexer.Allowlist = settingsV1.Allowlist;
            settingsV2.Patcher.DryRun = settingsV1.DryRun;
            settingsV2.Patcher.DisableBackupFile = settingsV1.DisableBackupFile;
            settingsV2.Patcher.EnableRecompression = settingsV1.EnableRecompression;
            settingsV2.Patcher.RecompressionMemoryMaxMB = settingsV1.RecompressionMemoryMaxMB;
            settingsV2.Patcher.ZipBombSizeLimitMB = settingsV1.ZipBombSizeLimitMB;
            settingsV2.EnableUpdateCheck = settingsV1.EnableUpdateCheck;
            settingsV2.LogLevel = settingsV1.LogLevel;
            settingsV2.EnableLive = settingsV1.EnableLive;
            settingsV2.DisablePluginInstallWarning = settingsV1.DisablePluginInstallWarning;
            settingsV2.MaxIndexerThreads = settingsV1.MaxIndexerThreads;
            settingsV2.MaxPatchThreads = settingsV1.MaxPatchThreads;

            var jsonObject = JsonObject.Parse(File.ReadAllText("appsettings.json"));
            jsonObject["Settings"] = JsonNode.Parse(JsonSerializer.Serialize(settingsV2));
            File.WriteAllText("appsettings.json",
                jsonObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            return true;
        }

        return false;
    }

    #endregion appsettings.json conversion methods

    public class SettingsOptionsV2
    {
        public int ConfigVersion { get; set; } = 2;
        public BlocklistOptions Blocklist { get; set; } = new BlocklistOptions();
        public IndexerOptions Indexer { get; set; } = new IndexerOptions();
        public PatcherOptions Patcher { get; set; } = new PatcherOptions();
        public bool EnableUpdateCheck { get; set; }
        public int LogLevel { get; set; }
        public bool EnableLive { get; set; }
        public bool DisablePluginInstallWarning { get; set; }
        public int MaxIndexerThreads { get; set; }
        public int MaxPatchThreads { get; set; }
    }

    public class BlocklistOptions
    {
        public string[] BlocklistUrls { get; set; } = [];
        public bool SendUnmatchedObjectsToDevs { get; set; }
        public string? BlocklistUnmatchedServer { get; set; }
    }

    public class IndexerOptions
    {
        public string? WorkingFolder { get; set; }
        public string[]? Allowlist { get; set; }
    }

    public class PatcherOptions
    {
        public bool DryRun { get; set; }
        public bool DisableBackupFile { get; set; }
        public bool EnableRecompression { get; set; }
        public int RecompressionMemoryMaxMB { get; set; }
        public int ZipBombSizeLimitMB { get; set; }
    }

    #region Old Settings Versions

    public class SettingsOptionsV1
    {
        public string[]? Allowlist { get; set; }

        public bool SendUnmatchedObjectsToDevs { get; set; }

        public string? BlocklistUnmatchedServer { get; set; }
        public string? WorkingFolder { get; set; }
        public bool EnableUpdateCheck { get; set; }
        public int LogLevel { get; set; }
        public bool EnableLive { get; set; }
        public bool DryRun { get; set; }
        public string[] BlocklistUrLs { get; set; } = [];
        public bool DisablePluginInstallWarning { get; set; }
        public bool DisableBackupFile { get; set; }
        public bool EnableRecompression { get; set; }
        public int MaxIndexerThreads { get; set; }
        public int MaxPatchThreads { get; set; }
        public int RecompressionMemoryMaxMB { get; set; }
        public int ZipBombSizeLimitMB { get; set; }
    }

    #endregion Old Settings Versions
}