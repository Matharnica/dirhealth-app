using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using DirHealth.Desktop.ViewModels;
using DirHealth.Desktop.ViewModels.Tables;
using Xunit;

namespace DirHealth.Tests;

// S14-1 shared table, S14-5 export metadata.
[Collection(ExportHookCollection.Name)]
public class TableViewModelTests : IDisposable
{
    private readonly Func<string, string?> _savedPicker = TableViewModel.PickSavePath;
    private readonly Action<string>        _savedClip   = TableViewModel.SetClipboardText;
    private readonly List<string>          _files       = [];

    public void Dispose()
    {
        TableViewModel.PickSavePath     = _savedPicker;
        TableViewModel.SetClipboardText = _savedClip;
        foreach (var f in _files)
        {
            if (File.Exists(f)) File.Delete(f);
            var meta = ExportMetadata.MetaPathFor(f);
            if (File.Exists(meta)) File.Delete(meta);
        }
    }

    private string TempCsv()
    {
        var p = Path.Combine(Path.GetTempPath(), $"dh-test-{Guid.NewGuid():N}.csv");
        _files.Add(p);
        return p;
    }

    private static List<AdUser> Users(int n) =>
        Enumerable.Range(1, n).Select(i => new AdUser
        {
            SamAccountName    = $"user{i:D4}",
            DisplayName       = $"User {i}",
            IsEnabled         = i % 2 == 0,
            DistinguishedName = $"CN=User {i},OU=Staff,DC=corp,DC=com",
            Sid               = $"S-1-5-21-1-2-3-{1000 + i}",
        }).ToList();

    private static TableViewModel<AdUser> NewTable() =>
        new("Users", UserColumns.Grid)
        {
            Dn  = u => u.DistinguishedName,
            Sid = u => u.Sid,
            Sam = u => u.SamAccountName,
        };

    [Fact]
    public void Counter_FollowsLocalFilter()
    {
        var t = NewTable();
        t.SetRows(Users(1204));
        Assert.Equal("1,204 items", t.CountLabel);

        t.SetFilter(u => u.IsEnabled);
        Assert.Equal(602, t.FilteredCount);
        Assert.Equal(1204, t.TotalCount);
        Assert.Equal("602 of 1,204", t.CountLabel);
        Assert.Equal("CSV (602 filtered)", t.ExportFilteredLabel);
        Assert.Equal("CSV (all 1,204)", t.ExportAllLabel);

        t.SetFilter(null);
        Assert.Equal("1,204 items", t.CountLabel);
    }

    [Fact]
    public void FilteredVsAll_ExportRowSets()
    {
        var t = NewTable();
        t.SetRows(Users(10));
        t.SetFilter(u => u.SamAccountName.EndsWith('1') || u.SamAccountName.EndsWith('2'));
        Assert.Equal(2, t.RowsForFilteredExport().Count);
        Assert.Equal(10, t.RowsForFullExport().Count);
    }

    [Fact]
    public void FilteredExport_UsesDisplayOrderWhenProvided()
    {
        var t = NewTable();
        t.SetRows(Users(3));
        t.DisplayOrder = () => t.FilteredRows.Reverse().Cast<object>();
        Assert.Equal("user0003", ((AdUser)t.RowsForFilteredExport()[0]).SamAccountName);
    }

    [Fact]
    public void ExportCommands_DisabledWhenEmpty()
    {
        var t = NewTable();
        Assert.False(t.ExportAllCommand.CanExecute(null));
        Assert.False(t.ExportFilteredCommand.CanExecute(null));
        t.SetRows(Users(2));
        Assert.True(t.ExportAllCommand.CanExecute(null));
        t.SetFilter(_ => false);
        Assert.False(t.ExportFilteredCommand.CanExecute(null));
        Assert.True(t.ExportAllCommand.CanExecute(null));
    }

    [Fact]
    public void Export_WritesCsvAndMetaWithMatchingHash()
    {
        var path = TempCsv();
        TableViewModel.PickSavePath = _ => path;
        var t = NewTable();
        t.ContextProvider   = () => new ExportContext("corp.com", "DC01.corp.com", "CORP\\admin");
        t.FilterDescription = () => "status=Active";
        t.SetRows(Users(5));
        t.SetFilter(u => u.IsEnabled);

        t.ExportFilteredCommand.Execute(null);

        var lines = File.ReadAllLines(path);
        Assert.Equal(1 + 2, lines.Length);                       // header + 2 enabled users
        Assert.StartsWith("Display Name", lines[0]);

        var meta = File.ReadAllText(ExportMetadata.MetaPathFor(path));
        Assert.Contains(ExportMetadata.Sha256OfFile(path), meta);
        foreach (var key in new[] { "File", "SHA-256", "Rows", "View", "Filter", "Domain", "Domain controller", "Run by", "Created (UTC)", "Tool" })
            Assert.Contains(key, meta);
        Assert.Contains("DC01.corp.com", meta);
        Assert.Contains("status=Active", meta);
        Assert.Matches(@"Rows\s+: 2\r?\n", meta);
        Assert.StartsWith("Exported 2 rows", t.StatusMessage);
    }

    [Fact]
    public void Export_Cancelled_WritesNothing()
    {
        TableViewModel.PickSavePath = _ => null;
        var t = NewTable();
        t.SetRows(Users(1));
        Assert.Null(t.Export(t.RowsForFullExport(), "", "all"));
    }

    [Fact]
    public void Export_ContextProviderThrows_StillExportsWithUnknown()
    {
        var path = TempCsv();
        TableViewModel.PickSavePath = _ => path;
        var t = NewTable();
        t.ContextProvider = () => throw new InvalidOperationException("no AD");
        t.SetRows(Users(1));
        t.ExportAllCommand.Execute(null);
        Assert.Contains("unknown", File.ReadAllText(ExportMetadata.MetaPathFor(path)));
    }

    [Fact]
    public void Copy_UsesSelectedRow()
    {
        string? copied = null;
        TableViewModel.SetClipboardText = s => copied = s;
        var t = NewTable();
        t.SetRows(Users(3));
        Assert.False(t.CopyDnCommand.CanExecute(null));

        t.SelectedItem = t.FilteredRows[1];
        t.CopyDnCommand.Execute(null);
        Assert.Equal("CN=User 2,OU=Staff,DC=corp,DC=com", copied);
        t.CopySidCommand.Execute(null);
        Assert.Equal("S-1-5-21-1-2-3-1002", copied);
        t.CopySamCommand.Execute(null);
        Assert.Equal("user0002", copied);
    }

    [Fact]
    public void Refresh_DropsSelectionThatIsFilteredOut()
    {
        var t = NewTable();
        t.SetRows(Users(4));
        t.SelectedItem = t.FilteredRows[0];          // user0001, disabled
        t.SetFilter(u => u.IsEnabled);
        Assert.Null(t.SelectedItem);
    }

    [Fact]
    public void Activate_RaisesEvent()
    {
        var t = NewTable();
        object? got = null;
        t.Activated += r => got = r;
        var u = new AdUser();
        t.Activate(u);
        Assert.Same(u, got);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(true, "TRUE")]
    [InlineData(false, "FALSE")]
    [InlineData(42, "42")]
    public void FormatValue_IsImportFriendly(object? value, string expected)
    {
        Assert.Equal(expected, TableColumn<AdUser>.FormatValue(value));
    }

    [Fact]
    public void FormatValue_DateIsIsoInvariant()
    {
        Assert.Equal("2026-03-04 05:06", TableColumn<AdUser>.FormatValue(new DateTime(2026, 3, 4, 5, 6, 7)));
        Assert.Equal("2026-03-04", TableColumn<AdUser>.FormatValue(new DateTime(2026, 3, 4), "yyyy-MM-dd"));
    }

    [Fact]
    public void ExportTable_EscapesHeaderAndCells()
    {
        var path = TempCsv();
        var cols = new List<ITableColumn> { new TableColumn<AdUser>("=Name", u => u.DisplayName) };
        new CsvExporter(",").ExportTable([new AdUser { DisplayName = "@evil" }], cols, path);
        var lines = File.ReadAllLines(path);
        Assert.Equal("'=Name", lines[0]);
        Assert.Equal("'@evil", lines[1]);
    }

    [Fact]
    public void Meta_Build_ContainsAllFields()
    {
        var text = ExportMetadata.Build(new ExportContext("corp.com", "DC1", "CORP\\a"), "Users", "",
            7, "x.csv", "abc", new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc), "2.10.0");
        Assert.Contains("Filter            : (none)", text);
        Assert.Contains("Created (UTC)     : 2026-10-13 12:00:00Z", text);
        Assert.Contains("Tool              : DirHealth 2.10.0", text);
    }

    [Fact]
    public void MetaPathFor_ReplacesExtension()
    {
        Assert.Equal(Path.Combine("dir", "export.meta.txt"), ExportMetadata.MetaPathFor(Path.Combine("dir", "export.csv")));
    }

    [Fact]
    public void UserBrowser_Filter_MatchesTextAndStatus()
    {
        var u = new AdUser { DisplayName = "Jane Doe", SamAccountName = "jdoe", IsEnabled = false };
        Assert.True(UserBrowserViewModel.MatchesFilter(u, "jane", "All"));
        Assert.True(UserBrowserViewModel.MatchesFilter(u, "", "Disabled"));
        Assert.False(UserBrowserViewModel.MatchesFilter(u, "", "Active"));
        Assert.False(UserBrowserViewModel.MatchesFilter(u, "bob", "All"));
    }
}
