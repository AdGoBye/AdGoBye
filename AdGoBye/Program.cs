using AdGoBye;
using AdGoBye.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Templates;
using Serilog.Templates.Themes;

var levelSwitch = new LoggingLevelSwitch
{
    MinimumLevel = (LogEventLevel)Settings.Options.LogLevel
};

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.ControlledBy(levelSwitch)
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new ExpressionTemplate(
        "[{@t:HH:mm:ss} {@l:u3} {Coalesce(SourceContext,'<none>')}] {@m}\n{@x}",
        theme: TemplateTheme.Literate))
    .CreateLogger();

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices(collection =>
    {
        collection.AddDbContext<IndexContext>();
        collection.AddSerilog();
        collection.AddSingleton<Indexer>();
        collection.AddSingleton<SharedStateService>();
    });


using var host = builder.Build();
var awa = host.Services.GetRequiredService<Indexer>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();
await using var db = host.Services.GetRequiredService<IndexContext>();
db.Database.Migrate();
awa.ManageIndex();

PluginLoader.LoadPlugins();
foreach (var plugin in PluginLoader.LoadedPlugins)
{
    logger.LogInformation("Plugin {Name} ({Maintainer}) v{Version} is loaded.", plugin.Name, plugin.Maintainer,
        plugin.Version);
    logger.LogInformation("Plugin type: {Type}", plugin.Instance.PluginType());

    if (plugin.Instance.PluginType() == EPluginType.ContentSpecific)
        logger.Log(LogLevel.Information,"Responsible for {IDs}", plugin.Instance.ResponsibleForContentIds());
}

if (Blocklist.Blocks.Count == 0) logger.LogInformation("No blocklist has been loaded, is this intentional?");
logger.LogInformation("Loaded blocks for {blockCount} worlds and indexed {indexCount} pieces of content",
    Blocklist.Blocks.Count, db.Content.Count());

foreach (var content in db.Content.Include(content => content.VersionMeta))
{
    if (content.Type != ContentType.World) continue;
    logger.LogInformation("Processing {ID} ({director})", content.Id, content.VersionMeta.Path);
    awa.PatchContent(content);
}

db.SaveChanges();
host.Run();