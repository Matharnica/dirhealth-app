using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using DirHealth.Desktop.Core.Storage;

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
            return list.ToList();
        }
    }

    partial void OnFilterTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredFindings));
        ExportCsvCommand.NotifyCanExecuteChanged();
        ExportPdfCommand.NotifyCanExecuteChanged();
    }

    partial void OnSeverityFilterChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredFindings));
        ExportCsvCommand.NotifyCanExecuteChanged();
        ExportPdfCommand.NotifyCanExecuteChanged();
    }

    partial void OnShowAcknowledgedChanged(bool value)
    {
        OnPropertyChanged(nameof(FilteredFindings));
        ExportCsvCommand.NotifyCanExecuteChanged();
        ExportPdfCommand.NotifyCanExecuteChanged();
    }

    partial void OnFindingsChanged(List<AdFinding> value)
    {
        OnPropertyChanged(nameof(FilteredFindings));
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
    public void SelectFinding(AdFinding? finding)
    {
        SelectedFinding = finding;
        AcknowledgeNote = finding?.AcknowledgeNote ?? "";
    }

    [RelayCommand]
    public void Acknowledge()
    {
        if (SelectedFinding is null) return;
        _store.Acknowledge(SelectedFinding.Category, AcknowledgeNote);
        SelectedFinding.IsAcknowledged  = true;
        SelectedFinding.AcknowledgeNote = AcknowledgeNote;
        OnPropertyChanged(nameof(FilteredFindings));
        SelectedFinding = null;
    }

    [RelayCommand]
    public void Unacknowledge()
    {
        if (SelectedFinding is null) return;
        _store.Unacknowledge(SelectedFinding.Category);
        SelectedFinding.IsAcknowledged  = false;
        SelectedFinding.AcknowledgeNote = "";
        OnPropertyChanged(nameof(FilteredFindings));
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
