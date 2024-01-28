using Serilog;
using System.Diagnostics.CodeAnalysis;

// ReSharper disable FunctionNeverReturns

namespace AdGoBye;

public static class Live
{
    private static readonly ILogger Logger = Log.ForContext(typeof(Live));
    private static readonly EventWaitHandle Ewh = new(true, EventResetMode.ManualReset);
    private const string LoadStartIndicator = "[Behaviour] Preparing assets...";
    private const string LoadStopIndicator = "Entering world";

    public static void WatchNewContent(string path)
    {
        using var watcher = new FileSystemWatcher(path);

        watcher.NotifyFilter = NotifyFilters.Attributes
                               | NotifyFilters.CreationTime
                               | NotifyFilters.DirectoryName
                               | NotifyFilters.FileName
                               | NotifyFilters.LastAccess
                               | NotifyFilters.LastWrite
                               | NotifyFilters.Security
                               | NotifyFilters.Size;

        /* HACK: [Regalia 2023-11-18T19:43:50Z] FileSystemWatcher doesn't detect __data on Windows only
          Instead, we track for __info and replace it with __data.
          This has the implication that info is always created alongside data.
          This might also break if the detection failure is caused intentionally by adversarial motive.
 */
        watcher.Created += (_, e) => Task.Run(() => ParseFile(e.FullPath.Replace("__info", "__data")));
        watcher.Deleted += (_, e) => Task.Run(
            () =>
            {
                Logger.Verbose("File removal: {directory}", e.FullPath);
                Indexer.RemoveFromIndex(e.FullPath.Replace("__info", "__data"));
            });

        watcher.Error += (_, e) =>
        {
            Logger.Error("{source}: {exception}", e.GetException().Message, e.GetException().Message);
        };

        watcher.Filter = "__info";
        watcher.IncludeSubdirectories = true;
        watcher.EnableRaisingEvents = true;
        while (true)
        {
            watcher.WaitForChanged(WatcherChangeTypes.Created, Timeout.Infinite);
        }
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
                Indexer.PatchContent(Indexer.GetFromIndex(path)!);
                done = true;
            }
            catch (EndOfStreamException)
            {
                await Task.Delay(500);
            }
        }
    }


    [SuppressMessage("ReSharper", "MethodSupportsCancellation")]
    public static void WatchLogFile(string path)
    {
        CancellationTokenSource ct = new();
        var currentTask = Task.Run(() => HandleFileLock(GetNewestLog(), ct.Token));

        using var watcher = new FileSystemWatcher(path);
        watcher.NotifyFilter = NotifyFilters.FileName;
        watcher.Created += (_, e) =>
        {
            ct.Cancel();
            currentTask.Wait();
            ct = new CancellationTokenSource();

            // Assuming a new log file means a client restart, it's likely not loading any file.
            // Let's take initiative and free any tasks.
            Ewh.Set();

            currentTask = Task.Run(() => HandleFileLock(e.FullPath, ct.Token));
            Logger.Verbose("Rotated log parsing to {file}", e.Name);
        };

        watcher.Filter = "*.txt";
        watcher.IncludeSubdirectories = false;
        watcher.EnableRaisingEvents = true;
        while (true)
        {
            watcher.WaitForChanged(WatcherChangeTypes.Created, Timeout.Infinite);
        }
    }

    private static void HandleFileLock(string logFile, CancellationToken cancellationToken)
    {
        var sr = GetLogStream(logFile);
        while (!cancellationToken.IsCancellationRequested)
        {
            var output = sr.ReadToEnd();
            var lines = output.Split(Environment.NewLine);
            foreach (var line in lines)
            {
                switch (line)
                {
                    case not null when line.Contains(LoadStartIndicator):
                        Logger.Verbose("Expecting world load: {msg}", line);
                        Ewh.Reset();
                        break;
                    case not null when line.Contains(LoadStopIndicator):
                        Logger.Verbose("Expecting world load finish: {msg}", line);
                        Ewh.Set();
                        break;
                }
            }
            Thread.Sleep(300);
        }
    }

    private static StreamReader GetLogStream(string logFile)
    {
        var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var sr = new StreamReader(fs);
        sr.BaseStream.Seek(0, SeekOrigin.End);
        return sr;
    }

    private static string GetNewestLog()
    {
        return new DirectoryInfo(Indexer.WorkingDirectory).GetFiles("*.txt")
            .OrderByDescending(file => file.CreationTimeUtc).First().FullName;
    }
}