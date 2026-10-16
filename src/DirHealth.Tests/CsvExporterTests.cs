using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using Xunit;

namespace DirHealth.Tests;

public class CsvExporterTests
{
    // Fixed delimiter so header assertions don't depend on the test machine's region.
    private readonly CsvExporter _exporter = new(",");

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

    [Theory]
    [InlineData("=CMD+CALC")]
    [InlineData("+1234")]
    [InlineData("-9")]
    [InlineData("@SUM(A1)")]
    public void ExportPasswordReport_FormulaDisplayName_IsPrefixEscaped(string formulaName)
    {
        var users = new List<AdUser>
        {
            new() { DisplayName = formulaName, SamAccountName = "user",
                    DistinguishedName = "CN=user,DC=corp,DC=com" }
        };
        var path = Path.GetTempFileName();
        try
        {
            _exporter.ExportPasswordReport(users, path);
            var content = File.ReadAllText(path);
            Assert.Contains("'" + formulaName, content);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("=HYPERLINK(http://evil.com)")]
    [InlineData("+cmd|calc!A0")]
    public void ExportInactiveUsers_FormulaDisplayName_IsPrefixEscaped(string formulaName)
    {
        var users = new List<AdUser>
        {
            new() { DisplayName = formulaName, SamAccountName = "user",
                    DistinguishedName = "CN=user,DC=corp,DC=com" }
        };
        var path = Path.GetTempFileName();
        try
        {
            _exporter.ExportInactiveUsers(users, path);
            var content = File.ReadAllText(path);
            Assert.Contains("'" + formulaName, content);
        }
        finally { File.Delete(path); }
    }

    // ── S13-5: BOM, delimiter, extended formula protection ─────────────────────

    [Fact]
    public void Export_WritesUtf8Bom_AndUmlautsSurvive()
    {
        var users = new List<AdUser>
        {
            new() { DisplayName = "Jürgen Müßig", SamAccountName = "jmuessig",
                    DistinguishedName = "CN=Jürgen,OU=Büro,DC=corp,DC=com" }
        };
        var path = Path.GetTempFileName();
        try
        {
            _exporter.ExportPasswordReport(users, path);
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 3);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            Assert.Contains("Jürgen Müßig", System.Text.Encoding.UTF8.GetString(bytes));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_UsesGivenDelimiter()
    {
        var path = Path.GetTempFileName();
        try
        {
            new CsvExporter(";").ExportInactiveUsers([], path);
            Assert.Equal("DisplayName;SamAccountName;Email;LastLogon;OU", File.ReadAllLines(path)[0].TrimStart('\uFEFF'));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_DefaultDelimiter_IsRegionListSeparator()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal(";", new CsvExporter().Delimiter);
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Assert.Equal(",", new CsvExporter().Delimiter);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void Export_DelimiterInsideValue_IsQuoted()
    {
        var users = new List<AdUser>
        {
            new() { DisplayName = "Doe; John", SamAccountName = "jdoe", DistinguishedName = "CN=x,DC=corp,DC=com" }
        };
        var path = Path.GetTempFileName();
        try
        {
            new CsvExporter(";").ExportPasswordReport(users, path);
            Assert.Contains("\"Doe; John\"", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    // OWASP CSV Injection payloads + S13-5 additions (LF, full-width, leading blanks)
    [Theory]
    [InlineData("=1+2")]
    [InlineData("=cmd|' /C calc'!A0")]
    [InlineData("+cmd|' /C calc'!A0")]
    [InlineData("-2+3+cmd|' /C calc'!A0")]
    [InlineData("@SUM(1+1)*cmd|' /C calc'!A0")]
    [InlineData("=HYPERLINK(\"http://evil.example\",\"x\")")]
    [InlineData("\t=1+2")]
    [InlineData("\r=1+2")]
    [InlineData("\n=1+2")]
    [InlineData("＝1+2")]
    [InlineData("＋1")]
    [InlineData("－1")]
    [InlineData("＠SUM(A1)")]
    [InlineData(" =1+2")]
    [InlineData("   @SUM(A1)")]
    [InlineData("\u00A0=1+2")]
    public void SafeField_NeutralizesFormulaPayloads(string payload)
    {
        Assert.Equal("'" + payload, CsvExporter.SafeField(payload));
    }

    [Theory]
    [InlineData("John Doe")]
    [InlineData("jdoe@corp.com")]
    [InlineData("a=b")]
    [InlineData("OU=Finance,DC=corp")]
    [InlineData("   ")]
    public void SafeField_LeavesHarmlessValuesUnchanged(string value)
    {
        Assert.Equal(value, CsvExporter.SafeField(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SafeField_NullOrEmpty_ReturnsEmpty(string? value)
    {
        Assert.Equal("", CsvExporter.SafeField(value));
    }

    [Fact]
    public void ExportFindings_TitleFromAd_IsEscaped()
    {
        // PSO findings embed the AD object name in the title
        var findings = new List<AdFinding>
        {
            new() { Category = "FineGrainedPasswordPolicy", Title = "=HYPERLINK(\"http://x\")",
                    Severity = FindingSeverity.High, Count = 1, AffectedObjects = ["@evil"] }
        };
        var path = Path.GetTempFileName();
        try
        {
            _exporter.ExportFindings(findings, path);
            var content = File.ReadAllText(path);
            Assert.Contains("'=HYPERLINK", content);
            Assert.Contains("'@evil", content);
        }
        finally { File.Delete(path); }
    }
}
