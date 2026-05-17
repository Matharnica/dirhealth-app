using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace DirHealth.Desktop.Core.Services;

public record UpdateInfo(string Version, string DownloadUrl, bool HasDirectDownload, long FileSize, string? ExpectedSha256 = null);

public class UpdateChecker
{
    private const string ReleasesApi = "https://api.github.com/repos/Matharnica/dirhealth-app/releases";

    private readonly HttpClient _http;

    public UpdateChecker(HttpClient http)
    {
        _http = http;
    }

    public async Task<(UpdateInfo? Update, string Diagnostic)> CheckAsync()
    {
        var currentVersion = GetCurrentVersion();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("DirHealth", currentVersion));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var res = await _http.SendAsync(req);

            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (null, $"No releases published yet (installed={currentVersion})");

            if (!res.IsSuccessStatusCode)
                return (null, $"GitHub API returned {(int)res.StatusCode}");

            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            // releases endpoint returns array, newest first — skip drafts and pre-releases
            var release = doc.RootElement.EnumerateArray()
                .FirstOrDefault(r => !r.GetProperty("draft").GetBoolean()
                                  && !r.GetProperty("prerelease").GetBoolean());

            if (release.ValueKind == JsonValueKind.Undefined)
                return (null, $"No releases published yet (installed={currentVersion})");

            var tagName    = release.GetProperty("tag_name").GetString() ?? "";
            var latestVer  = tagName.TrimStart('v').Split('-')[0];
            var releaseUrl = release.GetProperty("html_url").GetString() ?? "";

            if (!IsNewer(latestVer, currentVersion))
                return (null, $"Up to date (installed={currentVersion}, latest={latestVer})");

            var asset = release.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(a =>
                    a.GetProperty("browser_download_url").GetString()
                     ?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);

            if (asset.ValueKind == JsonValueKind.Undefined)
                return (new UpdateInfo(latestVer, releaseUrl, false, 0),
                        $"Update found: {latestVer} (no installer attached yet — opens release page)");

            var downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
            var fileSize    = asset.GetProperty("size").GetInt64();

            string? expectedSha256 = null;
            var checksumAsset = release.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(a =>
                    a.GetProperty("browser_download_url").GetString()
                     ?.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) == true);
            if (checksumAsset.ValueKind != JsonValueKind.Undefined)
            {
                var checksumUrl = checksumAsset.GetProperty("browser_download_url").GetString() ?? "";
                try
                {
                    if (!Uri.TryCreate(checksumUrl, UriKind.Absolute, out var cUri) ||
                        (cUri.Host != "github.com" && cUri.Host != "objects.githubusercontent.com") ||
                        cUri.Scheme != Uri.UriSchemeHttps)
                        throw new InvalidOperationException("Unexpected checksum host.");
                    using var cReq = new HttpRequestMessage(HttpMethod.Get, checksumUrl);
                    cReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("DirHealth", currentVersion));
                    var cRes = await _http.SendAsync(cReq);
                    if (cRes.IsSuccessStatusCode)
                        expectedSha256 = (await cRes.Content.ReadAsStringAsync()).Trim().ToLowerInvariant();
                }
                catch { }
            }

            return (new UpdateInfo(latestVer, downloadUrl, true, fileSize, expectedSha256), $"Update found: {latestVer}");
        }
        catch (Exception ex)
        {
            return (null, $"Exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static string GetCurrentVersion() =>
        (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
            .GetName().Version?.ToString(3) ?? "1.0.0";

    private static bool IsNewer(string latest, string current) =>
        Version.TryParse(latest, out var l) &&
        Version.TryParse(current, out var c) &&
        l > c;
}
