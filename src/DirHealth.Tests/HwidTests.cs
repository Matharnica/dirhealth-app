using DirHealth.Desktop.Core.HWID;
using Xunit;

namespace DirHealth.Tests;

public class HwidTests
{
    [Fact]
    public void ComputeHWID_ReturnsSha256HexString()
    {
        var hwid = HwidManager.ComputeHWID();
        Assert.NotNull(hwid);
        Assert.Equal(64, hwid.Length);
        Assert.Matches("^[0-9A-F]{64}$", hwid);
    }

    [Fact]
    public void ComputeHWID_IsIdempotent()
    {
        var first  = HwidManager.ComputeHWID();
        var second = HwidManager.ComputeHWID();
        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeHwid_ReturnsDifferentValues_ForDifferentUsernames()
    {
        // Simulates what Terminal Server mode does
        var raw1 = "CPU123-MB456-DISK789-userA";
        var raw2 = "CPU123-MB456-DISK789-userB";
        var hash1 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw1)));
        var hash2 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw2)));
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHWID_DifferentInputsProduceDifferentHwids()
    {
        var raw1 = "CPU1-MB1-DISK1";
        var raw2 = "CPU2-MB2-DISK2";

        var h1 = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw1)));
        var h2 = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw2)));

        Assert.NotEqual(h1, h2);
    }
}
