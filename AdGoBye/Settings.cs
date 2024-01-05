using Microsoft.Extensions.Configuration;

namespace AdGoBye;

public sealed class Settings
{
    public string[]? Allowlist { get; set; }
    public string? WorkingFolder { get; set; }
    public int LogLevel { get; set; }
    public bool EnableLive { get; set; }
    public bool DryRun { get; set; }
}