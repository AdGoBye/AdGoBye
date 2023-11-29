using AdGoBye;
using AdGoBye.Plugins;
using Serilog;
using Serilog.Core;
using Serilog.Events;

var levelSwitch = new LoggingLevelSwitch
{
    MinimumLevel = (LogEventLevel)Settings.Options.LogLevel
};

Log.Logger = new LoggerConfiguration().MinimumLevel.ControlledBy(levelSwitch)
    .WriteTo.Console().CreateLogger();

PluginLoader.LoadPlugins();
foreach (var plugin in PluginLoader.LoadedPlugins)
{
    Log.Information("Plugin {Name} ({Maintainer}) v{Version} is loaded.", plugin.Name, plugin.Maintainer,
        plugin.Version);
    Log.Information("Plugin type: {Type}", plugin.Instance.PluginType());

    if (plugin.Instance.PluginType() == EPluginType.ContentSpecific)
        Log.Information("Responsible for {IDs}", plugin.Instance.ResponsibleForContentIds());
}

if (Blocklist.Blocks.Count == 0) Log.Information("No blocklist has been loaded, is this intentional?");
Log.Information("Loaded blocks for {blockCount} worlds and indexed {indexCount} pieces of content",
    Blocklist.Blocks.Count, Indexer.Index.Count);

foreach (var content in Indexer.Index)
{
    if (content.Type != ContentType.World) continue;
    Indexer.PatchContent(content);
}

#pragma warning disable CS4014
if (Settings.Options.EnableLive)
{
    Task.Run(() => Live.WatchNewContent(Indexer.WorkingDirectory));
    Task.Run(Live.ParseLogLock);
    await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
}
#pragma warning restore CS4014