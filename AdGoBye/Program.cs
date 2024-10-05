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
            if (_isLogSet) Log.Information("Press [ENTER] to exit.");
            else Console.WriteLine("Press [ENTER] to exit.");


            Console.ReadLine();
#endif
        };

        Console.OutputEncoding = Encoding.UTF8;
        WorkingFolder.GetWorkingFolder();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddJsonFile(Path.Combine(WorkingFolder.MainDirectory, "appsettings.json")).Build();
        Settings.ConvertV1SettingsToV2(builder.Configuration);
        Settings.ConvertV2SettingsToV3(builder.Configuration);

        builder.Services.AddSerilog((_, configuration) =>
            configuration.ReadFrom.Configuration(builder.Configuration));
        _isLogSet = true;

        var configRoot = builder.Configuration.GetSection(nameof(Settings));
        builder.Services.Configure<Settings.SettingsOptionsV3>(configRoot);
        builder.Services.Configure<Settings.BlocklistOptions>(configRoot.GetSection("Blocklist"));
        builder.Services.Configure<Settings.IndexerOptions>(configRoot.GetSection("Indexer"));
        builder.Services.Configure<Settings.PatcherOptions>(configRoot.GetSection("Patcher"));

        builder.Services.Configure<HostOptions>(opts => opts.ShutdownTimeout = TimeSpan.Zero);

        builder.Services.AddDbContextFactory<AdGoByeContext>(optionsBuilder =>
        {
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.UseSqlite($"Data Source={Path.Combine(WorkingFolder.MainDirectory, "database.db")}");
        });

        builder.Services.RegisterLiveServices();
        builder.Services.AddSingleton<Indexer>();
        builder.Services.AddSingleton<Blocklist>();
        builder.Services.AddSingleton<Patcher>();
        builder.Services.AddSingleton<PluginLoader>();
        var host = builder.Build();
        SingleInstance.Attach();

        var db = await host.Services.GetRequiredService<IDbContextFactory<AdGoByeContext>>().CreateDbContextAsync();
        await db.Database.MigrateAsync();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var blocklists = host.Services.GetRequiredService<Blocklist>();
        var patcher = host.Services.GetRequiredService<Patcher>();
        var globalOptions = host.Services.GetRequiredService<IOptions<Settings.SettingsOptionsV3>>().Value;

        await host.StartAsync();

        if (globalOptions.EnableUpdateCheck) Updater.CheckUpdates();

        logger.LogInformation("Loaded blocks for {blockCount} worlds and indexed {indexCount} pieces of content",
            blocklists.Blocks.Count, db.Content.Count());

        Parallel.ForEach(db.Content.Include(content => content.VersionMeta),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = globalOptions.Patcher.MaxPatchThreads
            }, content => { patcher.PatchContent(content); });
        if (!globalOptions.EnableLive) await host.StopAsync();
        await host.WaitForShutdownAsync();
    }
}