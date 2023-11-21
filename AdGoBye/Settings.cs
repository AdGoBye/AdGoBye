using Microsoft.Extensions.Configuration;

namespace AdGoBye;

public class SettingsOptions
{
    public string[]? Allowlist { get; set; }
    public string? WorkingFolder { get; set; }
    public int LogLevel { get; set; }
    public bool EnableLive { get; set; }
    public bool DryRun { get; set; }
}

public static class Settings
{
    public static SettingsOptions Options { get; set; }

    static Settings()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();
        Options = config.GetRequiredSection("Settings").Get<SettingsOptions>() ?? new SettingsOptions();
    }
}