global using AdGoBye.Types;
using System.Text;
using AdGoBye;
using AdGoBye.Database;
using AdGoBye.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Templates;
using Serilog.Templates.Themes;

internal class Program
{
    private static bool _isLogSet;

    private static async Task Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (_isLogSet)
                Log.Error(e.ExceptionObject as Exception,
                    "Unhandled Error occured (isTerminating: {isTerminating}). Please report this.",
                    e.IsTerminating);
            else
                Console.Error.WriteLine(
                    $"Unhandled Error occured (isTerminating: {e.IsTerminating})." +
                    $" Please report this.{Environment.NewLine}{e.ExceptionObject as Exception}");

            if (!e.IsTerminating) return;
#if !DEBUG // Only block terminating unhandled exceptions in release mode, this can be annoying in debug mode.
            if (_isLogSet) Log.Log.Information("Press [ENTER] to exit.");
            else Console.WriteLine("Press [ENTER] to exit.");


            Console.ReadLine();
#endif
        };

        Console.OutputEncoding = Encoding.UTF8;

        var levelSwitch = new LoggingLevelSwitch
        {
            MinimumLevel = (LogEventLevel)Settings.Options.LogLevel
        };

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSerilog((_, configuration) =>
            configuration.Enrich.FromLogContext().MinimumLevel.ControlledBy(levelSwitch)
                .WriteTo.Console(new ExpressionTemplate(
                    "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}\n{@x}",
                    theme: TemplateTheme.Literate)
                )
        );
        builder.Services.RegisterLiveServices();
        builder.Services.AddSingleton<Indexer>();
        builder.Services.AddSingleton<Blocklist>();
        builder.Services.AddSingleton<Patcher>();

        _isLogSet = true;
        var host = builder.Build();



        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var blocklists = host.Services.GetRequiredService<Blocklist>();
        var patcher = host.Services.GetRequiredService<Patcher>();

        SingleInstance.Attach();

        if (Settings.Options.EnableUpdateCheck) Updater.CheckUpdates();

        await using var db = new AdGoByeContext();
        await db.Database.MigrateAsync();

        PluginLoader.LoadPlugins();
        foreach (var plugin in PluginLoader.LoadedPlugins)
        {
            logger.LogInformation("Plugin {Name} ({Maintainer}) v{Version} is loaded.", plugin.Name, plugin.Maintainer,
                plugin.Version);
            logger.LogInformation("Plugin type: {Type}", plugin.Instance.PluginType());

            if (plugin.Instance.PluginType() == EPluginType.ContentSpecific && plugin.Instance.ResponsibleForContentIds() is not null)
                logger.LogInformation("Responsible for {IDs}", plugin.Instance.ResponsibleForContentIds());
        }

        if (blocklists.Blocks == null || blocklists.Blocks.Count == 0)
            logger.LogInformation("No blocklist has been loaded, is this intentional?");

        logger.LogInformation("Loaded blocks for {blockCount} worlds and indexed {indexCount} pieces of content",
            blocklists.Blocks?.Count, db.Content.Count());

        Parallel.ForEach(db.Content.Include(content => content.VersionMeta),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Settings.Options.MaxPatchThreads
            }, content =>
            {
                if (content.Type != ContentType.World) return;
                patcher.PatchContent(content);
            });

        await db.SaveChangesAsync();

        if (Settings.Options.EnableLive) host.Run();
    }
}