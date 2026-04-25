using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace DirHealth.Desktop.Core.Services;

public record UpdateInfo(string Version, string DownloadUrl, bool HasDirectDownload);

public class UpdateChecker
{
    private const string ReleasesApi = "https://api.github.com/repos/matharnica/dirhealth/releases/latest";

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

            var tagName    = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var latestVer  = tagName.TrimStart('v').Split('-')[0]; // strip pre-release suffix
            var releaseUrl = doc.RootElement.GetProperty("html_url").GetString() ?? "";

            if (!IsNewer(latestVer, currentVersion))
                return (null, $"Up to date (installed={currentVersion}, latest={latestVer})");

            var downloadUrl = doc.RootElement
                .GetProperty("assets")
                .EnumerateArray()
                .Select(a => a.GetProperty("browser_download_url").GetString())
                .FirstOrDefault(u => u?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
                ?? "";

            if (string.IsNullOrEmpty(downloadUrl))
                return (new UpdateInfo(latestVer, releaseUrl, false),
                        $"Update found: {latestVer} (no installer attached yet — opens release page)");

            return (new UpdateInfo(latestVer, downloadUrl, true), $"Update found: {latestVer}");
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
