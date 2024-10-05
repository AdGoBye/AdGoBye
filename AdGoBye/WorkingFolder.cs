using Serilog;

namespace AdGoBye;

public static class WorkingFolder
{
    public static string MainDirectory = "";
    private static readonly ILogger Logger = Log.ForContext(typeof(SingleInstance));

    public static void GetWorkingFolder()
    {
        if (File.Exists("appsettings.json") || !OperatingSystem.IsLinux())
        {
            Console.WriteLine("Using local directory as working folder");
            return;
        }

        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ??
                       Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

        MainDirectory = Path.Combine(dataHome, "AdGoBye");
        Directory.CreateDirectory(MainDirectory);

        if (File.Exists(Path.Combine(MainDirectory, "appsettings.json"))) return;

        Console.WriteLine($"Could not find appsettings.json in {MainDirectory}, please move appsettings.json into the directory");
        Environment.Exit(1);
    }
}