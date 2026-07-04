using DirHealth.Desktop.Core.Storage;
using Xunit;

namespace DirHealth.Tests;

public class ScanLoggerTests
{
    [Fact]
    public void WriteRun_RecordsQueryDurationCountAndError()
    {
        var path = Path.GetTempFileName();
        try
        {
            new ScanLogger(path).WriteRun(new[]
            {
                new ScanLogEntry("InactiveUsers", 42, 5, null),
                new ScanLogEntry("EolComputers", 10, 0, "boom"),
            }, new DateTime(2026, 7, 4, 12, 0, 0));

            var text = File.ReadAllText(path);
            Assert.Contains("scan 2026-07-04 12:00:00", text);
            Assert.Contains("InactiveUsers", text);
            Assert.Contains("42 ms", text);
            Assert.Contains("5 result(s)", text);
            Assert.Contains("ERROR: boom", text);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WriteRun_RollsOver_KeepingFileBounded()
    {
        var path = Path.GetTempFileName();
        try
        {
            var many = Enumerable.Range(0, 2000)
                .Select(i => new ScanLogEntry($"q{i}", i, i, null))
                .ToArray();
            new ScanLogger(path).WriteRun(many, new DateTime(2026, 7, 4, 12, 0, 0));

            var lines = File.ReadAllLines(path);
            Assert.True(lines.Length <= 1500, $"expected <= 1500 lines, got {lines.Length}");
        }
        finally { File.Delete(path); }
    }
}
