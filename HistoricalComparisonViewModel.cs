using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>Snapshot of one ping-entry at the time the baseline was taken.</summary>
public record BaselineEntry(
    string   Name,
    string   IpAddress,
    int      SentCount,
    int      SuccessCount,
    int      FailCount,
    string   LastRtt,
    string   Status,
    DateTime TakenAt);

/// <summary>
/// View-model for the Historical Comparison sub-tab.
/// Captures the current ping-entries state as a "baseline" snapshot saved to
/// <c>Data/baseline.json</c>, then lets the user compare the live stats against it.
/// </summary>
public class HistoricalComparisonViewModel : ViewModelBase
{
    private static readonly string DataDir  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string DataFile = Path.Combine(DataDir, "baseline.json");
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    private readonly ObservableCollection<PingEntryViewModel> _entries;

    private bool   _hasBaseline;
    private string _baselineTaken = "";
    private string _statusText    = "";

    public ObservableCollection<ComparisonRow> Rows { get; } = new();

    public bool   HasBaseline   { get => _hasBaseline;   set => SetField(ref _hasBaseline, value); }
    public string BaselineTaken { get => _baselineTaken; set => SetField(ref _baselineTaken, value); }
    public string StatusText    { get => _statusText;    set => SetField(ref _statusText, value); }

    public RelayCommand TakeBaselineCommand { get; }
    public RelayCommand CompareCommand      { get; }
    public RelayCommand ClearBaselineCommand{ get; }

    public HistoricalComparisonViewModel(ObservableCollection<PingEntryViewModel> entries)
    {
        _entries              = entries;
        TakeBaselineCommand   = new RelayCommand(OnTakeBaseline);
        CompareCommand        = new RelayCommand(OnCompare, () => HasBaseline);
        ClearBaselineCommand  = new RelayCommand(OnClearBaseline, () => HasBaseline);

        LoadBaseline();
    }

    private void OnTakeBaseline()
    {
        var snapshot = _entries.Select(e => new BaselineEntry(
            e.Name, e.IpAddress, e.SentCount, e.SuccessCount, e.FailCount,
            e.LastRttDisplay, e.Status.ToString(), DateTime.Now)).ToList();

        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(DataFile, JsonSerializer.Serialize(snapshot, Opts));
            LoadBaseline();
            StatusText = $"Baseline saved — {snapshot.Count} entries at {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { StatusText = $"Save failed: {ex.Message}"; }
    }

    private void OnCompare()
    {
        if (!File.Exists(DataFile)) return;
        try
        {
            var baseline = JsonSerializer.Deserialize<List<BaselineEntry>>(File.ReadAllText(DataFile));
            if (baseline is null) return;

            Rows.Clear();
            foreach (var cur in _entries)
            {
                var b = baseline.FirstOrDefault(x => x.IpAddress == cur.IpAddress);
                var row = b is null
                    ? new ComparisonRow
                    {
                        Name           = cur.Name,
                        IpAddress      = cur.IpAddress,
                        BaselineSent   = "—",
                        CurrentSent    = cur.SentCount.ToString(),
                        BaselineLoss   = "—",
                        CurrentLoss    = FormatLoss(cur),
                        LossDelta      = "no baseline",
                        BaselineLastRtt= "—",
                        CurrentLastRtt = cur.LastRttDisplay,
                        ChangeClass    = "neutral"
                    }
                    : BuildRow(b, cur);
                Rows.Add(row);
            }
            StatusText = $"Compared {Rows.Count} entries against baseline from {BaselineTaken}";
        }
        catch (Exception ex) { StatusText = $"Compare failed: {ex.Message}"; }
    }

    private static ComparisonRow BuildRow(BaselineEntry b, PingEntryViewModel cur)
    {
        double bLoss = b.SentCount > 0 ? (double)b.FailCount / b.SentCount * 100 : 0;
        double cLoss = cur.SentCount > 0 ? (double)cur.FailCount / cur.SentCount * 100 : 0;
        double delta = cLoss - bLoss;

        return new ComparisonRow
        {
            Name            = cur.Name,
            IpAddress       = cur.IpAddress,
            BaselineSent    = b.SentCount.ToString(),
            CurrentSent     = cur.SentCount.ToString(),
            BaselineLoss    = $"{bLoss:F1} %",
            CurrentLoss     = $"{cLoss:F1} %",
            LossDelta       = delta == 0 ? "±0" : delta > 0 ? $"+{delta:F1} %" : $"{delta:F1} %",
            BaselineLastRtt = b.LastRtt,
            CurrentLastRtt  = cur.LastRttDisplay,
            ChangeClass     = delta > 2 ? "worse" : delta < -2 ? "better" : "neutral"
        };
    }

    private void OnClearBaseline()
    {
        try { if (File.Exists(DataFile)) File.Delete(DataFile); }
        catch { /* best-effort */ }
        HasBaseline   = false;
        BaselineTaken = "";
        Rows.Clear();
        StatusText    = "Baseline cleared";
        CommandManager.InvalidateRequerySuggested();
    }

    private void LoadBaseline()
    {
        if (!File.Exists(DataFile)) { HasBaseline = false; return; }
        try
        {
            var list = JsonSerializer.Deserialize<List<BaselineEntry>>(File.ReadAllText(DataFile));
            if (list?.Count > 0)
            {
                HasBaseline   = true;
                BaselineTaken = list[0].TakenAt.ToString("yyyy-MM-dd HH:mm:ss");
                CommandManager.InvalidateRequerySuggested();
            }
        }
        catch { HasBaseline = false; }
    }

    private static string FormatLoss(PingEntryViewModel e) =>
        e.SentCount > 0 ? $"{(double)e.FailCount / e.SentCount * 100:F1} %" : "—";
}

/// <summary>One display row in the comparison DataGrid.</summary>
public class ComparisonRow
{
    public string Name            { get; init; } = "";
    public string IpAddress       { get; init; } = "";
    public string BaselineSent    { get; init; } = "";
    public string CurrentSent     { get; init; } = "";
    public string BaselineLoss    { get; init; } = "";
    public string CurrentLoss     { get; init; } = "";
    public string LossDelta       { get; init; } = "";
    public string BaselineLastRtt { get; init; } = "";
    public string CurrentLastRtt  { get; init; } = "";
    /// <summary>"better" | "worse" | "neutral" — drives DataTrigger coloring in XAML.</summary>
    public string ChangeClass     { get; init; } = "neutral";
}
