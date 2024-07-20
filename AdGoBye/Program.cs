global using AdGoBye.Types;
using System.Text;
using AdGoBye;
using AdGoBye.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

        var builder = Host.CreateApplicationBuilder();

        var configRoot = builder.Configuration.GetSection(nameof(Settings));
        builder.Services.Configure<Settings.SettingsOptionsV2>(configRoot);
        builder.Services.Configure<Settings.BlocklistOptions>(configRoot.GetSection("Blocklists"));
        builder.Services.Configure<Settings.IndexerOptions>(configRoot.GetSection("Indexer"));
        builder.Services.Configure<Settings.PatcherOptions>(configRoot.GetSection("Patcher"));


        // HACK: We can't access SettingsOptionsV2 before we initialize Host per .Build(), but we can't configure Serilog after we do that
        // Hence we'll have to read it out directly from configRoot and convert it to the appropriate value
        var levelSwitch = new LoggingLevelSwitch
        {
            MinimumLevel = string.IsNullOrEmpty(configRoot["LogLevel"]) ? LogEventLevel.Verbose : (LogEventLevel)int.Parse(configRoot["LogLevel"]!)
        };
        builder.Services.AddSerilog((_, configuration) =>
            configuration.Enrich.FromLogContext().MinimumLevel.ControlledBy(levelSwitch)
                .WriteTo.Console(new ExpressionTemplate(
                    "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}\n{@x}",
                    theme: TemplateTheme.Literate)
                )
        );
        _isLogSet = true;

        builder.Configuration.AddJsonFile("appsettings.json").Build();
        // TODO: I'm not sure if this is valid like this, I doubt it 
        Settings.ConvertV1SettingsToV2(builder.Configuration);
        ((IConfigurationRoot)builder.Configuration).Reload();

        builder.Services.AddSerilog((_, configuration) =>
            configuration.Enrich.FromLogContext().MinimumLevel.ControlledBy(levelSwitch)
                .WriteTo.Console(new ExpressionTemplate(
                    "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}\n{@x}",
                    theme: TemplateTheme.Literate)
                )
        );
        _isLogSet = true;


        builder.Services.RegisterLiveServices();
        builder.Services.AddSingleton<Indexer>();
        builder.Services.AddSingleton<Blocklist>();
        builder.Services.AddSingleton<Patcher>();
        builder.Services.AddSingleton<PluginLoader>();
        var host = builder.Build();



        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var blocklists = host.Services.GetRequiredService<Blocklist>();
        var patcher = host.Services.GetRequiredService<Patcher>();
        var globalOptions = host.Services.GetRequiredService<IOptions<Settings.SettingsOptionsV2>>().Value;
        SingleInstance.Attach();

        if (globalOptions.EnableUpdateCheck) Updater.CheckUpdates();

        await using var db = new AdGoByeContext();
        await db.Database.MigrateAsync();

        logger.LogInformation("Loaded blocks for {blockCount} worlds and indexed {indexCount} pieces of content",
            blocklists.Blocks.Count, db.Content.Count());

        Parallel.ForEach(db.Content.Include(content => content.VersionMeta),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = globalOptions.MaxPatchThreads
            }, content =>
            {
                if (content.Type != ContentType.World) return;
                patcher.PatchContent(content);
            });

        await db.SaveChangesAsync();

        if (globalOptions.EnableLive) host.Run();
    }
}