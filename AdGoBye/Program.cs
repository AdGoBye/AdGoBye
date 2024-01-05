using AdGoBye;
using AdGoBye.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Templates;
using Serilog.Templates.Themes;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog(new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new ExpressionTemplate(
        "[{@t:HH:mm:ss} {@l:u3} {Coalesce(SourceContext,'<none>')}] {@m}\n{@x}",
        theme: TemplateTheme.Literate))
    .CreateLogger());
builder.Services.Configure<Settings>(builder.Configuration.GetSection(key: nameof(Settings)));

builder.Services.AddDbContext<IndexContext>();
builder.Services.AddSingleton<Indexer>();
builder.Services.AddSingleton<Blocklist>();
builder.Services.AddSingleton<SharedStateService>();
builder.Services.AddHostedService<ContentWatcher>();
builder.Services.AddHostedService<LogWatcher>();
using var host = builder.Build();

var indexer = host.Services.GetRequiredService<Indexer>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var blocklist = host.Services.GetRequiredService<Blocklist>();
await using var db = host.Services.GetRequiredService<IndexContext>();
db.Database.Migrate();
indexer.ManageIndex();

PluginLoader.LoadPlugins();
foreach (var plugin in PluginLoader.LoadedPlugins)
{
    logger.LogInformation("Plugin {Name} ({Maintainer}) v{Version} is loaded.", plugin.Name, plugin.Maintainer,
        plugin.Version);
    logger.LogInformation("Plugin type: {Type}", plugin.Instance.PluginType());

    if (plugin.Instance.PluginType() == EPluginType.ContentSpecific)
        // ReSharper disable once CoVariantArrayConversion
        logger.Log(LogLevel.Information, "Responsible for {IDs}", plugin.Instance.ResponsibleForContentIds()!);
}

if (blocklist.Blocks.Count == 0) logger.LogInformation("No blocklist has been loaded, is this intentional?");
logger.LogInformation("Loaded blocks for {blockCount} worlds and indexed {indexCount} pieces of content",
    blocklist.Blocks.Count, db.Content.Count());

foreach (var content in db.Content.Include(content => content.VersionMeta))
{
    if (content.Type != ContentType.World) continue;
    logger.LogInformation("Processing {ID} ({director})", content.Id, content.VersionMeta.Path);
    indexer.PatchContent(content);
}

db.SaveChanges();
host.Run();