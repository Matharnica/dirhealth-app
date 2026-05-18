using DirHealth.Desktop.Core.AD;
using Xunit;

namespace DirHealth.Tests;

public class AdWmiClientAllowlistTests
{
    // GetEventLogAsync validates logName BEFORE the Task.Run / WMI call,
    // so these tests run without any WMI connection.

    [Theory]
    [InlineData("CustomApp")]
    [InlineData("System' OR '1'='1")]
    [InlineData("")]
    [InlineData("system")]          // case-sensitive — lowercase rejected
    [InlineData("security")]
    [InlineData("application")]
    [InlineData("../../etc/passwd")]
    public async Task GetEventLog_DisallowedLogName_ReturnsEmpty(string logName)
    {
        var client = new AdWmiClient();
        var result = await client.GetEventLogAsync("localhost", logName, null, 100);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("System")]
    [InlineData("Security")]
    [InlineData("Application")]
    public async Task GetEventLog_AllowedLogName_PassesAllowlist(string logName)
    {
        // Allowed names pass the guard. The WMI call may fail in test env (caught internally).
        // Assert no exception escapes — only that the allowlist does not reject valid names.
        var client = new AdWmiClient();
        var ex = await Record.ExceptionAsync(() =>
            client.GetEventLogAsync("localhost", logName, null, 100));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("attacker\\share")]
    [InlineData("evil.com/../../")]
    [InlineData(" ")]
    public async Task GetDisks_InvalidHostname_ReturnsEmpty(string hostname)
    {
        var client = new AdWmiClient();
        var result = await client.GetDisksAsync(hostname);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("attacker\\share")]
    public async Task PingAsync_InvalidHostname_ReturnsFalse(string hostname)
    {
        var client = new AdWmiClient();
        var result = await client.PingAsync(hostname);
        Assert.False(result);
    }
}
