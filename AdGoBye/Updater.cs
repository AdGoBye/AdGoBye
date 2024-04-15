using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection;
using System.Text.Json;
using SemVersion;
using Serilog;

namespace AdGoBye;

public static class Updater
{
    private const string ConnectivityCheckUrl = "http://connectivity-check.ubuntu.com/";
    private const string GithubReleaseUrl = "https://api.github.com/repos/AdGoBye/AdGoBye/releases/latest";
    private const string ETagFile = "ReleaseETag";

    private static readonly HttpClient Client = CreateHttpClient();
    private static readonly ILogger Logger = Log.ForContext(typeof(Updater));

    public static void UpdateConditionally()
    {
        if (!CheckConnectivity())
        {
            Logger.Warning("We appear to be offline, skipping update check.");
            return;
        }

        if (GetVersionFromRemote() is not { } remoteVersion) return;

        var remoteVersionSemVer = SemanticVersion.Parse(remoteVersion.tag_name.Replace("v", ""));
        var localVersionSemVer = SemanticVersion.Parse(GetSelfVersion());
        Logger.Debug("Remote: {remote}, Local: {local} ", remoteVersion.tag_name, localVersionSemVer);
        if (remoteVersionSemVer > localVersionSemVer)
        {
            Logger.Information("Version {remoteVersion} is out, you are using {localVersion}, download it at {url}",
                remoteVersion.tag_name, localVersionSemVer, remoteVersion.html_url);
            return;
        }

        // If we know our version is the latest, conditional requests save on bandwidth and ratelimits
        // https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api#use-conditional-requests-if-appropriate
        SaveETag(remoteVersion.ETag);
    }

    private static bool CheckConnectivity()
    {
        var webRequest = new HttpRequestMessage(HttpMethod.Get, ConnectivityCheckUrl);
        // Captive portals might return 200, Ubuntu returns 204
        // If you're changing the provider, you may have to change this to IsSuccessStatusCode
        return Client.Send(webRequest).StatusCode is HttpStatusCode.NoContent;
    }

    private static GithubRelease? GetVersionFromRemote()
    {
        var webRequest = new HttpRequestMessage(HttpMethod.Get, GithubReleaseUrl);
        // ReSharper disable once InconsistentNaming
        var ETag = ReadETag();
        if (ETag is not null) webRequest.Headers.Add("If-None-Match", ETag);

        var response = Client.Send(webRequest);
        var body = response.Content.ReadAsStringAsync().Result; // Scary supposedly!

        if (response.StatusCode is HttpStatusCode.NotModified)
        {
            Logger.Verbose("No content difference from ETag");
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            Logger.Error("Github API request was not successful ({statusCode}: {response})", response.StatusCode, body);
            return null;
        }

        // ReSharper disable once InvertIf
        if (string.IsNullOrEmpty(body))
        {
            Logger.Error("Github API request was {statusCode} but has no contents?)",
                response.StatusCode);
            return null;
        }

        var release = JsonSerializer.Deserialize<GithubRelease>(body);
        if (release is null || response.Headers.ETag is null) return release;
        release.ETag = response.Headers.ETag.ToString();

        return release;
    }

    private static void SaveETag(string etag)
    {
        using var outputFile = new StreamWriter(ETagFile, false);
        outputFile.WriteLine(etag);
    }

    private static string? ReadETag()
    {
        if (!File.Exists(ETagFile)) return null;
        using var sr = new StreamReader(ETagFile);
        return sr.ReadToEnd().Trim();
    }

    private static string GetSelfVersion()
    {
        return typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AdGoBye");
        return client;
    }
}

#pragma warning disable CS8618
[SuppressMessage("ReSharper", "InconsistentNaming")]
public class GithubRelease
{
    public string html_url { get; init; }
    public string tag_name { get; init; }
    public string ETag { get; set; }
}
#pragma warning restore CS8618