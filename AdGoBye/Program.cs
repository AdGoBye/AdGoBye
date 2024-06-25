global using AdGoBye.Types;
using AdGoBye;
using AdGoBye.Plugins;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Templates;
using Serilog.Templates.Themes;

var levelSwitch = new LoggingLevelSwitch
{
    MinimumLevel = (LogEventLevel)Settings.Options.LogLevel
};

Console.OutputEncoding = System.Text.Encoding.UTF8;

Log.Logger = new LoggerConfiguration().MinimumLevel.ControlledBy(levelSwitch)
    .WriteTo.Console(new ExpressionTemplate(
        "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}\n{@x}",
        theme: TemplateTheme.Literate))
    .CreateLogger();
var logger = Log.ForContext(typeof(Program));
SingleInstance.Attach();
if (Settings.Options.EnableUpdateCheck) Updater.CheckUpdates();

await using var db = new State.IndexContext();
db.Database.Migrate();
Blocklist.UpdateNetworkBlocklists();
Blocklist.ParseAllBlocklists();
Indexer.ManageIndex();

PluginLoader.LoadPlugins();
foreach (var plugin in PluginLoader.LoadedPlugins)
{
    logger.Information("Plugin {Name} ({Maintainer}) v{Version} is loaded.", plugin.Name, plugin.Maintainer,
        plugin.Version);
    logger.Information("Plugin type: {Type}", plugin.Instance.PluginType());

    if (plugin.Instance.PluginType() == EPluginType.ContentSpecific)
        logger.Information("Responsible for {IDs}", plugin.Instance.ResponsibleForContentIds());
}

if (Blocklist.Blocks == null || Blocklist.Blocks.Count == 0)
    logger.Information("No blocklist has been loaded, is this intentional?");
logger.Information("Loaded blocks for {blockCount} worlds and indexed {indexCount} pieces of content",
    Blocklist.Blocks?.Count, db.Content.Count());

Parallel.ForEach(db.Content.Include(content => content.VersionMeta), content =>
{
    if (content.Type != ContentType.World) return;
    Indexer.PatchContent(content);
});

db.SaveChangesSafe();

#pragma warning disable CS4014
if (Settings.Options.EnableLive)
{
    Task.Run(() => Live.WatchNewContent(Indexer.WorkingDirectory));
    Task.Run(() => Live.WatchLogFile(Indexer.WorkingDirectory));
    await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
}
#pragma warning restore CS4014