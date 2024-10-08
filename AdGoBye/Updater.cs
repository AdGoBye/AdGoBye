using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Semver;
using Serilog;

namespace AdGoBye;

public static class Updater
{
    private const string ConnectivityCheckUrl = "http://connectivity-check.ubuntu.com/";
    private const string GithubReleaseUrl = "https://api.github.com/repos/AdGoBye/AdGoBye/releases/latest";

    private static readonly string EtagFile = Path.Combine(WorkingFolder.MainDirectory, "ReleaseETag");
    private static readonly HttpClient Client = CreateHttpClient();
    private static readonly ILogger Logger = Log.ForContext(typeof(Updater));

    public static void CheckUpdates()
    {
        if (!HasConnectivity())
        {
            Logger.Warning("We appear to be offline, skipping update check.");
            return;
        }

        if (GetVersionFromRemote() is not { } remoteVersion) return;

        var remoteVersionSemVer = SemVersion.Parse(remoteVersion.TagName.Replace("v", ""));
        var localVersionSemVer = SemVersion.Parse(GetSelfVersion());
        Logger.Debug("Remote: {remote}, Local: {local} ", remoteVersion.TagName, localVersionSemVer);
        if (!localVersionSemVer.IsPrerelease && remoteVersionSemVer.ComparePrecedenceTo(localVersionSemVer) > 0)
        {
            Logger.Information(
                "New version {remoteVersion} is out, you are using {localVersion}, download the new version at {url}",
                remoteVersion.TagName, localVersionSemVer.WithoutMetadata(), remoteVersion.HtmlUrl);
            return;
        }

        // If we know our version is the latest, conditional requests save on bandwidth and ratelimits
        // https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api#use-conditional-requests-if-appropriate
        SaveETag(remoteVersion.ETag);
    }

    private static bool HasConnectivity()
    {
        var webRequest = new HttpRequestMessage(HttpMethod.Get, ConnectivityCheckUrl);
        // Captive portals might return 200, Ubuntu returns 204
        // If you're changing the provider, you may have to change this to IsSuccessStatusCode
        try
        {
            return Client.Send(webRequest).StatusCode is HttpStatusCode.NoContent;
        }
        catch
        {
            return false;
        }
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
        using var outputFile = new StreamWriter(EtagFile, false);
        outputFile.WriteLine(etag);
    }

    private static string? ReadETag()
    {
        if (!File.Exists(EtagFile)) return null;
        using var sr = new StreamReader(EtagFile);
        return sr.ReadToEnd().Trim();
    }

    private static string GetSelfVersion()
    {
        return typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AdGoBye");
        return client;
    }
}

public class GithubRelease
{
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; init; } = null!;

    [JsonPropertyName("tag_name")]
    public string TagName { get; init; } = null!;

    [JsonPropertyName("ETag")]
    public string ETag { get; set; } = null!;
}