using Microsoft.Extensions.Configuration;

namespace AdGoBye;

public static class Settings
{
    static Settings()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();
        Options = config.GetRequiredSection("Settings").Get<SettingsOptions>() ?? new SettingsOptions();
    }

    public static SettingsOptions Options { get; set; }

    public class SettingsOptions
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
}