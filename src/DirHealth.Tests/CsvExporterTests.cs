using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using Xunit;

namespace DirHealth.Tests;

public class CsvExporterTests
{
    private readonly CsvExporter _exporter = new();

    [Fact]
    public void ExportPasswordReport_WritesCorrectHeadersAndRow()
    {
        var users = new List<AdUser>
        {
            new() { DisplayName = "Alice", SamAccountName = "alice",
                    Email = "alice@corp.com",
                    PasswordExpires = new DateTime(2026, 5, 10),
                    DaysUntilPasswordExpiry = 16,
                    DistinguishedName = "CN=Alice,OU=Finance,DC=corp,DC=com" }
        };
        var path = Path.GetTempFileName();
        try
        {
            _exporter.ExportPasswordReport(users, path);
            var lines = File.ReadAllLines(path);
            Assert.Equal("DisplayName,SamAccountName,Email,PasswordExpires,DaysRemaining,OU", lines[0]);
            Assert.Contains("Alice", lines[1]);
            Assert.Contains("2026-05-10", lines[1]);
            Assert.Contains("Finance", lines[1]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExportPasswordReport_EmptyList_WritesHeaderOnly()
    {
        var path = Path.GetTempFileName();
        try
        {
            _exporter.ExportPasswordReport([], path);
            var lines = File.ReadAllLines(path);
            Assert.Equal(1, lines.Count(l => l.Length > 0));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExportInactiveUsers_WritesCorrectHeadersAndRow()
    {
        var users = new List<AdUser>
        {
            new() { DisplayName = "Bob", SamAccountName = "bob",
                    Email = "bob@corp.com",
                    LastLogon = new DateTime(2025, 11, 1),
                    DistinguishedName = "CN=Bob,OU=IT,DC=corp,DC=com" }
        };
        var path = Path.GetTempFileName();
        try
        {
            _exporter.ExportInactiveUsers(users, path);
            var lines = File.ReadAllLines(path);
            Assert.Equal("DisplayName,SamAccountName,Email,LastLogon,OU", lines[0]);
            Assert.Contains("Bob", lines[1]);
            Assert.Contains("2025-11-01", lines[1]);
            Assert.Contains("IT", lines[1]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExportPasswordReport_UserWithNoDnOu_WritesEmptyOu()
    {
        var users = new List<AdUser>
        {
            new() { DisplayName = "Svc", SamAccountName = "svc",
                    Email = "", PasswordExpires = null,
                    DaysUntilPasswordExpiry = null,
                    DistinguishedName = "CN=Svc,DC=corp,DC=com" }
        };
        var path = Path.GetTempFileName();
        try
        {
            _exporter.ExportPasswordReport(users, path);
            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Count(l => l.Length > 0));
            Assert.DoesNotContain("OU=", lines[1]);
        }
        finally { File.Delete(path); }
    }
}
