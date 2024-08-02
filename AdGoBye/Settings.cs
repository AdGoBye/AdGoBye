using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Configuration;

namespace AdGoBye;

public class Settings
{
    public class SettingsOptionsV3
    {
        public int ConfigVersion { get; set; } = 3;
        public BlocklistOptions Blocklist { get; set; } = new();
        public IndexerOptions Indexer { get; set; } = new();
        public PatcherOptions Patcher { get; set; } = new();
        public bool EnableUpdateCheck { get; set; }
        public bool EnableLive { get; set; }
        public bool DisablePluginInstallWarning { get; set; }
    }

    public class SettingsOptionsV2
    {
        public int ConfigVersion { get; set; } = 2;
        public BlocklistOptions Blocklist { get; set; } = new();
        public IndexerOptions Indexer { get; set; } = new();
        public PatcherOptions Patcher { get; set; } = new();
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

        public int MaxIndexerThreads { get; set; }
    }

    public class PatcherOptions
    {
        public bool DryRun { get; set; }
        public bool DisableBackupFile { get; set; }
        public bool EnableRecompression { get; set; }
        public int RecompressionMemoryMaxMB { get; set; }
        public int ZipBombSizeLimitMB { get; set; }
        public int MaxPatchThreads { get; set; }
    }

    #region Old Settings Versions

    public class SettingsOptionsV1
    {
        public string[]? Allowlist { get; set; }
        public string? WorkingFolder { get; set; }
        public int LogLevel { get; set; }
        public bool EnableLive { get; set; }
        public bool DryRun { get; set; }
        public string[] BlocklistUrLs { get; set; } = [];

        // Null because v2.1.0 doesn't have these yet
        public string? BlocklistUnmatchedServer { get; set; }
        public bool? SendUnmatchedObjectsToDevs { get; set; }
        public bool? EnableUpdateCheck { get; set; }
        public bool? DisablePluginInstallWarning { get; set; }
        public bool? DisableBackupFile { get; set; }
        public bool? EnableRecompression { get; set; }
        public int? MaxIndexerThreads { get; set; }
        public int? MaxPatchThreads { get; set; }
        public int? RecompressionMemoryMaxMB { get; set; }
        public int? ZipBombSizeLimitMB { get; set; }
    }

    #endregion Old Settings Versions

    #region appsettings.json conversion methods

    internal static void ConvertV2SettingsToV3(IConfigurationRoot? config)
    {
        var section = config?.GetRequiredSection("Settings");

        var configVersion = section?.GetValue<int?>("ConfigVersion");
        if (configVersion is not 2) return;

        var configJsonObject = (JsonObject?)JsonNode.Parse(File.ReadAllText("appsettings.json"));
        if (configJsonObject is null) return;

        var modifiedSettingsSection = configJsonObject["Settings"]!.AsObject();
        modifiedSettingsSection.Remove("LogLevel");
        modifiedSettingsSection.Remove("MaxIndexerThreads");
        modifiedSettingsSection.Remove("MaxPatchThreads");
        modifiedSettingsSection["Patcher"]!["MaxPatchThreads"] = section?.GetValue<int?>("MaxPatchThreads") ?? 16;
        modifiedSettingsSection["Indexer"]!["MaxIndexerThreads"] = section?.GetValue<int?>("MaxIndexerThreads") ?? 16;
        modifiedSettingsSection["ConfigVersion"] = 3;
        configJsonObject["Settings"]!.ReplaceWith(modifiedSettingsSection);

        configJsonObject.Remove("Logging");
        configJsonObject["Serilog"] = new JsonObject
        {
            ["Using"] = new JsonArray
            {
                "Serilog.Sinks.Console"
            },
            ["MinimumLevel"] = new JsonObject
            {
                ["Default"] = "Verbose",
                ["Override"] = new JsonObject
                {
                    ["Microsoft"] = "Warning",
                    ["Microsoft.EntityFrameworkCore.Model.Validation"] = "Error"
                }
            },
            ["WriteTo"] = new JsonArray
            {
                new JsonObject
                {
                    ["Name"] = "Console",
                    ["Args"] = new JsonObject
                    {
                        ["formatter"] = new JsonObject
                        {
                            ["type"] = "Serilog.Templates.ExpressionTemplate, Serilog.Expressions",
                            ["template"] = "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}\n{@x}",
                            ["theme"] = "Serilog.Templates.Themes.TemplateTheme::Literate, Serilog.Expressions"
                        }

                    }
                }
            }
        };

        File.WriteAllText("appsettings.json",
            configJsonObject.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));
    }

    internal static bool ConvertV1SettingsToV2(IConfigurationRoot? config)
    {
        var section = config?.GetRequiredSection("Settings");
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
            settingsV2.Indexer.WorkingFolder = settingsV1.WorkingFolder;
            settingsV2.Indexer.Allowlist = settingsV1.Allowlist;
            settingsV2.Patcher.DryRun = settingsV1.DryRun;
            settingsV2.LogLevel = settingsV1.LogLevel;
            settingsV2.EnableLive = settingsV1.EnableLive;

            settingsV2.Blocklist.SendUnmatchedObjectsToDevs = settingsV1.SendUnmatchedObjectsToDevs ?? false;
            settingsV2.Blocklist.BlocklistUnmatchedServer = settingsV1.BlocklistUnmatchedServer ??
                                                            "https://blocklistsrv.dogworld.eu.org/v1/BlocklistCallback";
            settingsV2.DisablePluginInstallWarning = settingsV1.DisablePluginInstallWarning ?? false;
            settingsV2.EnableUpdateCheck = settingsV1.EnableUpdateCheck ?? true;
            settingsV2.Patcher.DisableBackupFile = settingsV1.DisableBackupFile ?? false;
            settingsV2.Patcher.EnableRecompression = settingsV1.EnableRecompression ?? false;
            settingsV2.Patcher.RecompressionMemoryMaxMB = settingsV1.RecompressionMemoryMaxMB ?? 250;
            settingsV2.Patcher.ZipBombSizeLimitMB = settingsV1.ZipBombSizeLimitMB ?? 8000;
            settingsV2.MaxIndexerThreads = settingsV1.MaxIndexerThreads ?? 16;
            settingsV2.MaxPatchThreads = settingsV1.MaxPatchThreads ?? 16;

            var jsonObject = JsonNode.Parse(File.ReadAllText("appsettings.json")) ?? new JsonObject();
            jsonObject["Settings"] = JsonNode.Parse(JsonSerializer.Serialize(settingsV2));
            File.WriteAllText("appsettings.json",
                jsonObject.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

            return true;
        }

        return false;
    }

    #endregion appsettings.json conversion methods
}