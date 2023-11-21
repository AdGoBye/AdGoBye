using Serilog;

// ReSharper disable FunctionNeverReturns

namespace AdGoBye;

public static class Live
{
    private static readonly EventWaitHandle Ewh = new(true, EventResetMode.ManualReset);
    private const string LoadStartIndicator = "[Behaviour] Preparing assets...";
    private const string LoadStopIndicator = "Loaded asset bundle";

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
        watcher.Error += (_, e) =>
        {
            Log.Error("{source}: {exception}", e.GetException().Message, e.GetException().Message);
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
        Log.Verbose("File creation: {directory}", path);
        var done = false;
        while (!done)
        {
            try
            {
                var content = Indexer.ParseFile(path);
                if (content is null)
                {
                    Log.Debug("{path} was null", path);
                    return;
                }

                Indexer.Index.Add(content);
                Log.Information("Adding to index: {id} ({type})", content.Id, content.Type);

                if (content.Type == ContentType.World && Blocklist.Blocks is not null)
                {
                    if (Blocklist.Blocks.ContainsKey(content.Id))
                    {
                        Log.Verbose("Live patching world after lock is released… ({id})", content.Id);
                        // Unity doesn't hold a lock on the file.
                        // If we attempt to patch the file during load, we may overwrite the file while
                        // the client loading the world, causing corruption and the client to crash.
                        Ewh.WaitOne();
                        Indexer.PatchContent(content);
                    }
                }

                done = true;
            }
            catch (EndOfStreamException)
            {
                await Task.Delay(500);
            }
        }
    }

    public static void ParseLogLock()
    {
        var currentLogFile = GetNewestLog();
        var sr = GetLogStream(currentLogFile);

        var timer = new System.Timers.Timer(60000);
        timer.Elapsed += (_, _) => Indexer.WriteIndexToDisk();
        timer.AutoReset = true;
        timer.Enabled = true;

        while (true)
        {
            if (GetNewestLog() != currentLogFile)
            {
                Log.Verbose("Switching file, new log file exists");
                currentLogFile = GetNewestLog();
                sr = GetLogStream(currentLogFile);

                // Assuming a new log file means a client restart, it's likely not loading any file.
                // Let's take initiative and free any tasks.
                Ewh.Set();
                continue;
            }

            var s = sr.ReadLine();
            if (s == null)
            {
                continue;
            }

            if (s.Contains(LoadStartIndicator))
            {
                Log.Verbose("Expecting world load: {msg}", s);
                Ewh.Reset();
            }
            else if (s.Contains(LoadStopIndicator))
            {
                Log.Verbose("Expecting world load finish: {msg}", s);
                Ewh.Set();
            }
        }
    }

    private static StreamReader GetLogStream(string loglocation)
    {
        var fs = new FileStream(loglocation, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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