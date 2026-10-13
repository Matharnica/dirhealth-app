using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using DirHealth.Desktop.ViewModels.Tables;

namespace DirHealth.Desktop.ViewModels;

/// <summary>
/// User detail (S15-4) and the single-user security dossier export (S15-5). Used by the user
/// browser and by the search (each with its own instance).
/// </summary>
public partial class UserDetailViewModel : BaseViewModel
{
    private readonly AdScanner? _scanner;

    /// <summary>PDF save dialog; replaced in tests.</summary>
    public static Func<string, string?> PickPdfPath { get; set; } = DefaultPickPdfPath;

    /// <summary>Current findings of the last scan (set by the app) — for "Affected by Findings".</summary>
    public Func<IReadOnlyList<AdFinding>>? FindingsProvider { get; set; }

    /// <summary>Last scan time for the dossier metadata (set by the app).</summary>
    public Func<string>? ScanTimeProvider { get; set; }

    [ObservableProperty] private AdUser?   _user;
    [ObservableProperty] private bool      _isLoading;
    /// <summary>Set when the group lookup failed — distinct from "no groups" (S13-2).</summary>
    [ObservableProperty] private string?   _groupsError;
    [ObservableProperty] private DateTime? _lastLogonThisDc;
    [ObservableProperty] private bool      _isTier0;
    [ObservableProperty] private List<DetailRow> _attributes = [];
    [ObservableProperty] private List<AdFinding> _affectedFindings = [];

    /// <summary>All groups (direct / nested / primary) — kept for the PDF and tests.</summary>
    public ObservableCollection<AdUserGroup> Groups { get; } = new();

    /// <summary>"Member Of" as the shared table (S15-4).</summary>
    public TableViewModel<AdUserGroup> GroupsTable { get; } = new("User Groups", ViewColumns.UserGroups)
    {
        Dn  = g => g.DistinguishedName,
        Sid = g => g.Sid,
    };

    public int GroupCount   => Groups.Count;
    public int FindingCount => AffectedFindings.Count;
    public bool HasGroups   => GroupCount > 0;
    public bool HasFindings => FindingCount > 0;

    public UserDetailViewModel(AdScanner? scanner)
    {
        _scanner = scanner;
        if (scanner is not null) GroupsTable.ContextProvider = scanner.GetExportContext;
    }

    partial void OnAffectedFindingsChanged(List<AdFinding> value)
    {
        OnPropertyChanged(nameof(FindingCount));
        OnPropertyChanged(nameof(HasFindings));
    }

    public async Task LoadAsync(AdUser user)
    {
        User            = user;
        IsLoading       = true;
        GroupsError     = null;
        LastLogonThisDc = null;
        IsTier0         = false;
        Groups.Clear();
        GroupsTable.Clear();
        Attributes       = UserDetailRows.Build(user, null);
        AffectedFindings = UserDetailRows.FindingsFor(user, FindingsProvider?.Invoke() ?? []);
        try
        {
            if (_scanner is null) return;
            try { LastLogonThisDc = await _scanner.GetLastLogonOnConnectedDcAsync(user.DistinguishedName); }
            catch { LastLogonThisDc = null; } // detail still useful without it; row says "no logon on this DC"
            Attributes = UserDetailRows.Build(user, LastLogonThisDc);

            var groups = await _scanner.GetUserGroupsAsync(user.DistinguishedName);
            GroupPaths.Assign(user.SamAccountName, groups);
            foreach (var g in groups) Groups.Add(g);
            GroupsTable.SetRows(groups);
            IsTier0 = groups.Any(g => g.IsTier0);
        }
        catch (Exception ex)
        {
            GroupsError   = $"Group memberships could not be loaded: {ex.Message}";
            StatusMessage = GroupsError;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(GroupCount));
            OnPropertyChanged(nameof(HasGroups));
            ExportPdfCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Dossier for the current user, or null when none is loaded.</summary>
    public UserDossier? BuildDossier() =>
        User is null ? null : new UserDossier(
            User, Attributes, Groups.ToList(), AffectedFindings,
            SafeContext(), ScanTimeProvider?.Invoke() ?? "unknown", DateTime.UtcNow, ExportMetadata.AppVersion);

    private ExportContext SafeContext()
    {
        try { return _scanner?.GetExportContext() ?? new ExportContext("unknown", "unknown", Environment.UserName); }
        catch { return new ExportContext("unknown", "unknown", Environment.UserName); }
    }

    /// <summary>"Export dossier…": PDF + groups/findings CSVs + .meta.txt files (S15-5).</summary>
    [RelayCommand(CanExecute = nameof(HasUser))]
    public void ExportPdf()
    {
        var dossier = BuildDossier();
        if (dossier is null) return;
        if (IsLoading) { StatusMessage = "Still loading — try again in a moment."; return; }
        var path = PickPdfPath($"DirHealth-User-{dossier.User.SamAccountName}-{DateTime.Now:yyyyMMdd}");
        if (path is null) return;
        try
        {
            var files = UserDossierExporter.Export(dossier, path);
            StatusMessage = $"Exported dossier: {string.Join(", ", files.Select(Path.GetFileName))} (+ .meta.txt)";
        }
        catch (Exception ex) { StatusMessage = $"Export failed: {ex.Message}"; }
    }

    private static string? DefaultPickPdfPath(string suggestedName)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName   = suggestedName,
            DefaultExt = ".pdf",
            Filter     = "PDF files|*.pdf",
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private bool HasUser() => User is not null;

    partial void OnUserChanged(AdUser? value) => ExportPdfCommand.NotifyCanExecuteChanged();
}
