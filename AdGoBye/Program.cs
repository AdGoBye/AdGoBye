global using AdGoBye.Types;
using AdGoBye;
using AdGoBye.Database;
using AdGoBye.Plugins;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Templates;
using Serilog.Templates.Themes;

internal class Program
{
    private static bool _isLoggerSet;

    private static async Task Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (_isLoggerSet)
                Log.Logger.Error(e.ExceptionObject as Exception, "Unhandled Error occured. Please report this.");
            else
                Console.Error.WriteLine(
                    $"Unhandled Error occured. Please report this.{Environment.NewLine}{e.ExceptionObject as Exception}");

            if (!e.IsTerminating) return;
            if (_isLoggerSet) Log.Logger.Information("Press [ENTER] to exit.");
            else Console.WriteLine("Press [ENTER] to exit.");

#if !DEBUG // Only block terminating unhandled exceptions in release mode, this can be annoying in debug mode.
            Console.ReadLine();
#endif
        };

        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var levelSwitch = new LoggingLevelSwitch
        {
            MinimumLevel = (LogEventLevel)Settings.Options.LogLevel
        };

        Log.Logger = new LoggerConfiguration().MinimumLevel.ControlledBy(levelSwitch)
            .WriteTo.Console(new ExpressionTemplate(
                "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}\n{@x}",
                theme: TemplateTheme.Literate))
            .CreateLogger();

        _isLoggerSet = true;

        var logger = Log.ForContext(typeof(Program));

        SingleInstance.Attach();

        if (Settings.Options.EnableUpdateCheck) Updater.CheckUpdates();

        await using var db = new AdGoByeContext();
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

        Parallel.ForEach(db.Content.Include(content => content.VersionMeta),
            new ParallelOptions { MaxDegreeOfParallelism = Settings.Options.MaxPatchThreads }, content =>
            {
                if (content.Type != ContentType.World) return;
                Patcher.PatchContent(content);
            });

        db.SaveChanges();

        if (Settings.Options.EnableLive)
        {
            _ = Task.Run(() => Live.WatchNewContent(Indexer.WorkingDirectory));
            _ = Task.Run(() => Live.WatchLogFile(Indexer.WorkingDirectory));
            await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
        }
    }
}