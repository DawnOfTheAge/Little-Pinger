using System.Collections.ObjectModel;

namespace LittlePinger.ViewModels;

/// <summary>
/// Container view-model for the "Reporting" tab.
/// Owns Export, Scheduled Tests, and Historical Comparison sub-tool view-models.
/// </summary>
public class ReportingViewModel
{
    public ExportResultsViewModel       ExportVM    { get; }
    public ScheduledTestsViewModel      ScheduledVM { get; }
    public HistoricalComparisonViewModel HistoryVM  { get; }

    public ReportingViewModel(ObservableCollection<PingEntryViewModel> entries)
    {
        ExportVM    = new ExportResultsViewModel(entries);
        ScheduledVM = new ScheduledTestsViewModel(entries);
        HistoryVM   = new HistoricalComparisonViewModel(entries);
    }
}
