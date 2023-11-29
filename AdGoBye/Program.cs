using AdGoBye;
using AdGoBye.Plugins;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Templates;
using Serilog.Templates.Themes;

var levelSwitch = new LoggingLevelSwitch
{
    MinimumLevel = (LogEventLevel)Settings.Options.LogLevel
};

Log.Logger = new LoggerConfiguration().MinimumLevel.ControlledBy(levelSwitch)
    .WriteTo.Console(new ExpressionTemplate(
        "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}\n{@x}",
        theme: TemplateTheme.Literate))
    .CreateLogger();
var logger = Log.ForContext(typeof(Program));

PluginLoader.LoadPlugins();
foreach (var plugin in PluginLoader.LoadedPlugins)
{
    logger.Information("Plugin {Name} ({Maintainer}) v{Version} is loaded.", plugin.Name, plugin.Maintainer,
        plugin.Version);
    logger.Information("Plugin type: {Type}", plugin.Instance.PluginType());

    if (plugin.Instance.PluginType() == EPluginType.ContentSpecific)
        logger.Information("Responsible for {IDs}", plugin.Instance.ResponsibleForContentIds());
}

if (Blocklist.Blocks.Count == 0) Log.Information("No blocklist has been loaded, is this intentional?");
logger.Information("Loaded blocks for {blockCount} worlds and indexed {indexCount} pieces of content",
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