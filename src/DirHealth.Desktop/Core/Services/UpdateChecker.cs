using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace DirHealth.Desktop.Core.Services;

public record UpdateInfo(string Version, string DownloadUrl);

public class UpdateChecker
{
    private readonly HttpClient _http;
    private readonly string _apiBase;

    public UpdateChecker(HttpClient http, string apiBase)
    {
        _http    = http;
        _apiBase = apiBase.TrimEnd('/');
    }

    public async Task<(UpdateInfo? Update, string Diagnostic)> CheckAsync()
    {
        var currentVersion = GetCurrentVersion();
        try
        {
            var res = await _http.GetAsync($"{_apiBase}/v1/version");

            if (!res.IsSuccessStatusCode)
                return (null, $"API returned {(int)res.StatusCode}");

            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var latestVer = doc.RootElement.GetProperty("version").GetString() ?? "";

            if (!IsNewer(latestVer, currentVersion))
                return (null, $"Up to date (installed={currentVersion}, latest={latestVer})");

            var downloadUrl = $"{_apiBase}/download/DirHealth-Setup-{latestVer}.exe";
            return (new UpdateInfo(latestVer, downloadUrl), $"Update found: {latestVer}");
        }
        catch (Exception ex)
        {
            return (null, $"Exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static string GetCurrentVersion() =>
        Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "1.0.0";

    private static bool IsNewer(string latest, string current) =>
        Version.TryParse(latest, out var l) &&
        Version.TryParse(current, out var c) &&
        l > c;
}
