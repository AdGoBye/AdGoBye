using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ReSharper disable FunctionNeverReturns

namespace AdGoBye;

public static class Live
{
    private const string LoadStartIndicator = "[Behaviour] Preparing assets...";
    private const string LoadStopIndicator = "Entering world";
    private static readonly EventWaitHandle Ewh = new(true, EventResetMode.ManualReset);
    private static CancellationTokenSource _logwatcherStoppingToken = new();

    public static void RegisterLiveServices(this IServiceCollection services)
    {
        services.AddHostedService<LogFileWatcher>();
        services.AddHostedService<Logwatcher>();
        services.AddHostedService<ContentWatcher>();
    }

    internal class ContentWatcher(ILogger<ContentWatcher> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Run(() =>
            {
                using var watcher = new FileSystemWatcher(Indexer.WorkingDirectory);

                watcher.NotifyFilter = NotifyFilters.Attributes
                                       | NotifyFilters.CreationTime
                                       | NotifyFilters.DirectoryName
                                       | NotifyFilters.FileName
                                       | NotifyFilters.LastAccess
                                       | NotifyFilters.LastWrite
                                       | NotifyFilters.Security
                                       | NotifyFilters.Size;

                // "Cancellation is cooperative and is not forced on the listener.
                // The listener determines how to gracefully terminate in response to a cancellation request."
                //
                // Nothing consumes this token, this suggestion is not useful.
                // ReSharper disable MethodSupportsCancellation
                watcher.Created += (_, e) => Task.Run(() => ParseFile(e.FullPath.Replace("__info", "__data")));
                watcher.Deleted += (_, e) => Task.Run(
                    () =>
                    {
                        logger.LogTrace("File removal: {directory}", e.FullPath);
                        Indexer.RemoveFromIndex(e.FullPath.Replace("__info", "__data"));
                    });
                // ReSharper restore MethodSupportsCancellation

                watcher.Error += (_, e) =>
                {
                    switch (e.GetException())
                    {
                        case InternalBufferOverflowException:
                            logger.LogError(
                                "FileSystemWatcher's internal buffer experienced an overflow, we may have missed some file events!\n" +
                                "Your Indexer state may not correct anymore, restart if something doesn't work.\n" +
                                "If you experience this often, please tell us at https://github.com/AdGoBye/AdGoBye/issues.");
                            break;
                        default:
                            logger.LogError("FileSystemWatcher threw an exception: {exception}", e);
                            break;
                    }
                };

                watcher.Filter = "__info";
                watcher.IncludeSubdirectories = true;
                watcher.EnableRaisingEvents = true;
                while (!stoppingToken.IsCancellationRequested)
                {
                    watcher.WaitForChanged(WatcherChangeTypes.Created, Timeout.Infinite);
                }
            });
        }

        private static async void ParseFile(string path)
        {
            var done = false;
            while (!done)
            {
                try
                {
                    Indexer.AddToIndex(path);
                    Ewh.WaitOne();
                    var newContent = Indexer.GetFromIndex(path);
                    if (newContent is not null) Patcher.PatchContent(newContent);
                    done = true;
                }
                catch (EndOfStreamException)
                {
                    await Task.Delay(500);
                }
            }
        }
    }

    internal class LogFileWatcher(ILogger<LogFileWatcher> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Run(() =>
            {
                using var watcher = new FileSystemWatcher(Indexer.WorkingDirectory);
                watcher.NotifyFilter = NotifyFilters.FileName;
                watcher.Filter = "*.txt";
                watcher.IncludeSubdirectories = false;
                watcher.Created += (_, e) =>
                {
                    _logwatcherStoppingToken.Cancel();
                    _logwatcherStoppingToken = new CancellationTokenSource();

                    // Assuming a new log file means a client restart, it's likely not loading any file.
                    // Let's take initiative and free any tasks.
                    Ewh.Set();

                    logger.LogTrace("Rotated log parsing to {file}", e.Name);
                };
                watcher.EnableRaisingEvents = true;

                while (true)
                {
                    watcher.WaitForChanged(WatcherChangeTypes.Created, Timeout.Infinite);
                }
            });
        }
    }

    internal class Logwatcher(ILogger<Logwatcher> logger) : BackgroundService
    {
        private static StreamReader GetLogStream(string logFile)
        {
            var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var sr = new StreamReader(fs);
            sr.BaseStream.Seek(0, SeekOrigin.End);
            return sr;
        }

        private static string GetNewestLog()
        {
            return new DirectoryInfo(Indexer.WorkingDirectory).GetFiles("*.txt")
                .OrderByDescending(file => file.CreationTimeUtc).First().FullName;
        }


        protected override async Task ExecuteAsync(CancellationToken applicationQuitToken)
        {
            var logPath = GetNewestLog();
            var sr = GetLogStream(logPath);
            logger.LogTrace("Now reading logfile {path}", logPath);
            while (!_logwatcherStoppingToken.IsCancellationRequested || !applicationQuitToken.IsCancellationRequested)
            {
                var output = await sr.ReadToEndAsync(applicationQuitToken);
                var lines = output.Split(Environment.NewLine);
                foreach (var line in lines)
                {
                    switch (line)
                    {
                        case not null when line.Contains(LoadStartIndicator):
                            logger.LogTrace("Expecting world load: {msg}", line);
                            Ewh.Reset();
                            break;
                        case not null when line.Contains(LoadStopIndicator):
                            logger.LogTrace("Expecting world load finish: {msg}", line);
                            Ewh.Set();
                            break;
                    }
                }

                await Task.Delay(300, applicationQuitToken);
            }
        }
    }
}