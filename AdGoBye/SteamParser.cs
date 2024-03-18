using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32;
using Serilog;

namespace AdGoBye;

[SuppressMessage("ReSharper", "StringLiteralTypo")]
public static class SteamParser
{
    private const string Appid = "438100";
    private static readonly ILogger Logger = Log.ForContext(typeof(SteamParser));

    public static string GetApplicationName()
    {
        try
        {
            return ExtractAppName();
        }
        catch (Exception e)
        {
            DieFatally(e);
        }

        throw new InvalidOperationException();

        void DieFatally(Exception e)
        {
            Logger.Fatal("We're unable to find your game's working folder (the folder above the cache), " +
                         "please provide it manually in appsettings.json as 'WorkingFolder'.");
            throw e;
        }
    }

    private static string ExtractAppName()
    {
        var steamRootPath = GetPathToSteamRoot();

        var appName = GetAppNameFromAppmanifest(steamRootPath) ?? GetAppNameFromVrManifest();
        if (appName is not null) return appName;

        Logger.Debug("Auto detection failed to get app from default path and vrmanifest");
        var libraryWithAppId = GetLibraryWithAppId(steamRootPath) ??
                               throw new InvalidOperationException(
                                   $"GetLibraryWithAppId failed to find the AppId {Appid} in libraryfolders.vdf");

        appName = GetAppNameFromAppmanifest(libraryWithAppId) ??
                  throw new InvalidOperationException(
                      $"GetLibraryWithAppId believes {libraryWithAppId} contains app but appmanifest is missing");

        return appName;

        // ReSharper disable once IdentifierTypo
        string? GetAppNameFromAppmanifest(string path)
        {
            string line;
            try
            {
                line = File.ReadLines(path + "/steamapps/" + $"appmanifest_{Appid}.acf")
                    .First(readLine => readLine.Contains("name"));
            }
            catch (FileNotFoundException)
            {
                return null;
            }

            var words = line.Split("\t");
            return words[3].Replace("\"", "");
        }

        string? GetAppNameFromVrManifest()
        {
            var expectingAppName = false;
            IEnumerable<string>? vrAppManifest;
            try
            {
                vrAppManifest = File.ReadLines(Path.Combine(steamRootPath, "config/", "steamapps.vrmanifest"));
            }
            catch (FileNotFoundException)
            {
                return null;
            }

            foreach (var line in vrAppManifest)
            {
                switch (expectingAppName)
                {
                    case false when line.Contains($"\"steam.app.{Appid}\""):
                        expectingAppName = true;
                        break;
                    case true when line.Contains("name"):
                        return line.Split(":")[1].Replace("\"", "").Trim();
                    case true when line.Contains("steam.app."):
                        Logger.Warning("VRManifest parser expected app name but got another appkey?");
                        expectingAppName = false;
                        break;
                }
            }

            return null;
        }
    }


    private static string? GetLibraryWithAppId(string pathToSteamRoot)
    {
        string? libraryPath = null;
        foreach (var line in File.ReadLines(Path.Combine(pathToSteamRoot, "config/", "libraryfolders.vdf")))
        {
            // Assumes line will be \t\t"path"\t\t"pathToLibrary"
            if (line.Contains("\"path\"")) libraryPath = line.Split("\t")[4].Replace("\"", "");

            if (line.Contains($"\"{Appid}\"")) return libraryPath;
        }

        return null;
    }


    private static string GetPathToSteamRoot()
    {
        if (OperatingSystem.IsLinux())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) +
                                "/.steam/steam/");
        }

        if (OperatingSystem.IsWindows())
        {
            var registryKey = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Valve\Steam",
                "InstallPath",
                null);

            // ReSharper disable once StringLiteralTypo
            return registryKey!.ToString()!.Replace("steam.exe", "");
        }

        throw new InvalidOperationException("couldn't determine pathToSteamApps");
    }
}