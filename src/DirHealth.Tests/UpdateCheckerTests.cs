using System.Net;
using System.Net.Http;
using DirHealth.Desktop.Core.Services;
using Xunit;

namespace DirHealth.Tests;

public class UpdateCheckerTests
{
    private static HttpClient MakeClient(params (string url, string body)[] responses)
    {
        return new HttpClient(new SequencedMockHandler(responses));
    }

    [Fact]
    public async Task CheckAsync_WhenNoReleases_ReturnsNull()
    {
        var http    = MakeClient(("", "[]"));
        var checker = new UpdateChecker(http);
        var (update, diagnostic) = await checker.CheckAsync();
        Assert.Null(update);
        Assert.Contains("No releases", diagnostic);
    }

    [Fact]
    public async Task CheckAsync_WhenAllDraftReleases_ReturnsNull()
    {
        var json = """[{"tag_name":"v99.0.0","draft":true,"prerelease":false,"html_url":"https://github.com/x","assets":[]}]""";
        var http    = MakeClient(("", json));
        var checker = new UpdateChecker(http);
        var (update, _) = await checker.CheckAsync();
        Assert.Null(update);
    }

    [Fact]
    public async Task CheckAsync_WhenNoExeAsset_ReturnsUpdateWithReleasePageUrl()
    {
        var json = """[{"tag_name":"v99.0.0","draft":false,"prerelease":false,"html_url":"https://github.com/release","assets":[]}]""";
        var http    = MakeClient(("", json));
        var checker = new UpdateChecker(http);
        var (update, _) = await checker.CheckAsync();
        Assert.NotNull(update);
        Assert.False(update.HasDirectDownload);
        Assert.Equal("https://github.com/release", update.DownloadUrl);
    }

    [Fact]
    public async Task CheckAsync_ParsesExeAssetUrlAndFileSize()
    {
        var json = """
            [{"tag_name":"v99.0.0","draft":false,"prerelease":false,"html_url":"https://github.com/r",
              "assets":[{"browser_download_url":"https://objects.githubusercontent.com/DirHealth-Setup-99.0.0.exe","size":12345678}]}]
            """;
        var http    = MakeClient(("", json));
        var checker = new UpdateChecker(http);
        var (update, _) = await checker.CheckAsync();
        Assert.NotNull(update);
        Assert.True(update.HasDirectDownload);
        Assert.Equal("99.0.0", update.Version);
        Assert.Equal(12345678L, update.FileSize);
    }

    [Fact]
    public async Task CheckAsync_WhenInstalledVersionIsLatest_ReturnsNull()
    {
        // v0.0.1 is older than the test assembly's default version (1.0.0)
        var json = """[{"tag_name":"v0.0.1","draft":false,"prerelease":false,"html_url":"https://github.com/r","assets":[]}]""";
        var http    = MakeClient(("", json));
        var checker = new UpdateChecker(http);
        var (update, diagnostic) = await checker.CheckAsync();
        Assert.Null(update);
        Assert.Contains("Up to date", diagnostic);
    }

    [Fact]
    public async Task CheckAsync_FetchesSha256FromChecksumAsset()
    {
        const string expectedHash = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
        var releasesJson = """
            [{"tag_name":"v99.0.0","draft":false,"prerelease":false,"html_url":"https://github.com/r",
              "assets":[
                {"browser_download_url":"https://objects.githubusercontent.com/DirHealth-Setup-99.0.0.exe","size":1000},
                {"browser_download_url":"https://objects.githubusercontent.com/DirHealth-Setup-99.0.0.sha256","size":64}
              ]}]
            """;
        var http    = MakeClient(
            ("", releasesJson),
            ("DirHealth-Setup-99.0.0.sha256", expectedHash));
        var checker = new UpdateChecker(http);
        var (update, _) = await checker.CheckAsync();
        Assert.NotNull(update);
        Assert.Equal(expectedHash, update.ExpectedSha256);
    }
}

internal class SequencedMockHandler : HttpMessageHandler
{
    private readonly (string urlSubstring, string body)[] _responses;
    private int _callIndex;

    public SequencedMockHandler(params (string, string)[] responses)
        => _responses = responses;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body;
        if (_responses.Length == 1)
        {
            body = _responses[0].body;
        }
        else
        {
            // Match by URL substring, fall back to sequence
            var url    = request.RequestUri?.ToString() ?? "";
            var match  = _responses.FirstOrDefault(r => r.urlSubstring.Length > 0 && url.Contains(r.urlSubstring));
            body = match.body ?? (_callIndex < _responses.Length ? _responses[_callIndex].body : "{}");
        }
        _callIndex++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
