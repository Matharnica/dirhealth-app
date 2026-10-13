using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using DirHealth.Desktop.Core.Storage;

using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.ViewModels.Tables;

namespace DirHealth.Desktop.ViewModels;

public partial class FindingsViewModel : BaseViewModel
{
    private readonly AcknowledgeStore _store       = new();
    private readonly CsvExporter      _csvExporter = new();
    private readonly PdfExporter      _pdfExporter = new();

    [ObservableProperty] private List<AdFinding> _findings = [];
    [ObservableProperty] private AdFinding?      _selectedFinding;
    [ObservableProperty] private string          _filterText     = "";
    [ObservableProperty] private string          _severityFilter = "All";
    [ObservableProperty] private string          _acknowledgeNote = "";
    [ObservableProperty] private bool            _showAcknowledged = false;
    [ObservableProperty] private int             _score;

    public List<string> SeverityOptions { get; } = ["All", "High", "Medium", "Low"];

    /// <summary>Affected objects of the selected finding as the shared table (S16-2).</summary>
    public TableViewModel<AffectedObject> ObjectsTable { get; } = new("Affected Objects", ViewColumns.AffectedObjects)
    {
        Dn  = o => string.IsNullOrEmpty(o.DistinguishedName) ? null : o.DistinguishedName,
        Sid = o => string.IsNullOrEmpty(o.Sid) ? null : o.Sid,
        Sam = o => o.Type == AffectedObjectType.User ? o.Name : null,
    };

    /// <summary>Opens an object's detail (set by the main VM: switches to the search detail, S15-2).</summary>
    public Action<AffectedObject>? NavigateToObject { get; set; }

    public FindingRule? SelectedRule => SelectedFinding?.Rule;

    public FindingsViewModel()
    {
        ObjectsTable.Activated += row => { if (row is AffectedObject o) OpenObject(o); };
    }

    /// <summary>Findings not acknowledged — drives the sidebar badge (S9-8; A6 fix in S14-6).</summary>
    public int OpenFindingsCount => CountOpen(Findings);

    public static int CountOpen(IEnumerable<AdFinding> findings) => findings.Count(f => !f.IsAcknowledged);

    public List<AdFinding> FilteredFindings
    {
        get
        {
            var list = Findings.AsEnumerable();
            if (!ShowAcknowledged)
                list = list.Where(f => !f.IsAcknowledged);
            if (SeverityFilter != "All")
                list = list.Where(f => f.Severity.ToString() == SeverityFilter);
            if (!string.IsNullOrWhiteSpace(FilterText))
                list = list.Where(f =>
                    f.Title.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                    f.Category.Contains(FilterText, StringComparison.OrdinalIgnoreCase));
            // Critical → High → Medium → Low, then alphabetical (severity enum is Low..Critical ascending)
            return list.OrderByDescending(f => f.Severity).ThenBy(f => f.Title).ToList();
        }
    }

    partial void OnFilterTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredFindings));
        OnPropertyChanged(nameof(OpenFindingsCount));
        ExportCsvCommand.NotifyCanExecuteChanged();
        ExportPdfCommand.NotifyCanExecuteChanged();
    }

    partial void OnSeverityFilterChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredFindings));
        OnPropertyChanged(nameof(OpenFindingsCount));
        ExportCsvCommand.NotifyCanExecuteChanged();
        ExportPdfCommand.NotifyCanExecuteChanged();
    }

    partial void OnShowAcknowledgedChanged(bool value)
    {
        OnPropertyChanged(nameof(FilteredFindings));
        OnPropertyChanged(nameof(OpenFindingsCount));
        ExportCsvCommand.NotifyCanExecuteChanged();
        ExportPdfCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedFindingChanged(AdFinding? value)
    {
        ObjectsTable.SetRows(value?.Objects ?? []);
        ObjectsTable.FilterDescription = () => $"finding={value?.RuleId} {value?.Category}";
        OnPropertyChanged(nameof(SelectedRule));
        CopyFixCommand.NotifyCanExecuteChanged();
        CopyRevertCommand.NotifyCanExecuteChanged();
        // A8 (S14-6): the list binds SelectedItem straight to SelectedFinding, so the
        // SelectFinding command never runs on a click — load the saved note here instead.
        AcknowledgeNote = value?.AcknowledgeNote ?? "";
        OnPropertyChanged(nameof(SelectedFindingIsAcknowledged));
    }

    public bool SelectedFindingIsAcknowledged => SelectedFinding?.IsAcknowledged ?? false;

    partial void OnFindingsChanged(List<AdFinding> value)
    {
        OnPropertyChanged(nameof(FilteredFindings));
        OnPropertyChanged(nameof(OpenFindingsCount));
        ExportCsvCommand.NotifyCanExecuteChanged();
        ExportPdfCommand.NotifyCanExecuteChanged();
    }

    public void LoadFindings(List<AdFinding> findings, int score = 0)
    {
        foreach (var f in findings)
        {
            f.IsAcknowledged  = _store.IsAcknowledged(f.Category);
            f.AcknowledgeNote = _store.GetNote(f.Category);
        }
        Findings = findings;
        Score    = score;
        SelectedFinding = null;
    }

    [RelayCommand]
    public void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    [RelayCommand]
    public void ClearSelection() => SelectedFinding = null;

    // ── S16-3 fix block: display + copy only. DirHealth never runs these commands. ──
    private bool HasRule() => SelectedRule is not null;

    [RelayCommand(CanExecute = nameof(HasRule))]
    public void CopyFix() => CopyScript(SelectedRule?.Fix, "Fix");

    [RelayCommand(CanExecute = nameof(HasRule))]
    public void CopyRevert() => CopyScript(SelectedRule?.Revert, "Undo");

    private void CopyScript(string? script, string what)
    {
        if (string.IsNullOrEmpty(script)) return;
        try
        {
            TableViewModel.SetClipboardText(script);
            StatusMessage = $"{what} command copied — review it, then run it yourself (it starts with -WhatIf).";
        }
        catch (Exception ex) { StatusMessage = $"Copy failed: {ex.Message}"; }
    }

    /// <summary>Double-click on an affected object opens its detail (S16-2 → S15-2).</summary>
    public void OpenObject(AffectedObject obj)
    {
        if (obj.Type is AffectedObjectType.User or AffectedObjectType.Computer or AffectedObjectType.Group
            && !string.IsNullOrEmpty(obj.DistinguishedName) && NavigateToObject is not null)
            NavigateToObject(obj);
        else
            StatusMessage = string.IsNullOrEmpty(obj.DistinguishedName)
                ? $"No directory path stored for {obj.Name} (scan predates S16) — run a new scan or use Copy."
                : $"{obj.Name} has no detail view.";
    }

    [RelayCommand]
    public void SelectFinding(AdFinding? finding)
    {
        SelectedFinding = finding;
        AcknowledgeNote = finding?.AcknowledgeNote ?? "";
    }

    [RelayCommand]
    public void Acknowledge()
    {
        if (SelectedFinding is null) return;
        try
        {
            _store.Acknowledge(SelectedFinding.Category, AcknowledgeNote);
            SelectedFinding.IsAcknowledged  = true;
            SelectedFinding.AcknowledgeNote = AcknowledgeNote;
            OnPropertyChanged(nameof(FilteredFindings));
        OnPropertyChanged(nameof(OpenFindingsCount));
            OnPropertyChanged(nameof(SelectedFindingIsAcknowledged));
            StatusMessage   = "Finding acknowledged.";
            AcknowledgeNote = "";
        }
        catch (Exception ex) { StatusMessage = $"Failed to acknowledge: {ex.Message}"; }
        SelectedFinding = null;
    }

    [RelayCommand]
    public void Unacknowledge()
    {
        if (SelectedFinding is null) return;
        try
        {
            _store.Unacknowledge(SelectedFinding.Category);
            SelectedFinding.IsAcknowledged  = false;
            SelectedFinding.AcknowledgeNote = "";
            OnPropertyChanged(nameof(FilteredFindings));
        OnPropertyChanged(nameof(OpenFindingsCount));
            OnPropertyChanged(nameof(SelectedFindingIsAcknowledged));
            StatusMessage   = "Acknowledgement removed.";
            AcknowledgeNote = "";
        }
        catch (Exception ex) { StatusMessage = $"Failed to unacknowledge: {ex.Message}"; }
        SelectedFinding = null;
    }

    [RelayCommand(CanExecute = nameof(HasFilteredFindings))]
    public void ExportCsv()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName   = $"DirHealth-Findings-{DateTime.Now:yyyyMMdd}",
            DefaultExt = ".csv",
            Filter     = "CSV files|*.csv"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                _csvExporter.ExportFindings(FilteredFindings, dlg.FileName);
                StatusMessage = $"Exported: {System.IO.Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex) { StatusMessage = $"Export failed: {ex.Message}"; }
        }
    }

    [RelayCommand(CanExecute = nameof(HasFilteredFindings))]
    public void ExportPdf()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName   = $"DirHealth-Findings-{DateTime.Now:yyyyMMdd}",
            DefaultExt = ".pdf",
            Filter     = "PDF files|*.pdf"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                _pdfExporter.ExportReport(FilteredFindings, Score, dlg.FileName);
                StatusMessage = $"Exported: {System.IO.Path.GetFileName(dlg.FileName)}";
            }
            catch (Exception ex) { StatusMessage = $"Export failed: {ex.Message}"; }
        }
    }

    private bool HasFilteredFindings() => FilteredFindings.Count > 0;
}
