using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace LittlePinger.ViewModels;

/// <summary>Possible test types for a scheduled task.</summary>
public enum ScheduledTestType { Ping, HTTP, Port }

/// <summary>Model for a single scheduled diagnostic test.</summary>
public class ScheduledTest : ViewModelBase
{
    private string            _name            = "New Test";
    private string            _host            = "";
    private string            _resolvedHost    = "";
    private ScheduledTestType _testType        = ScheduledTestType.Ping;
    private int               _port            = 80;
    private int               _intervalMinutes = 5;
    private bool              _isEnabled       = true;
    private string            _lastStatus      = "—";
    private DateTime?         _lastRunAt;
    private bool              _alertOnFailure  = true;

    public Guid              Id              { get; init; } = Guid.NewGuid();
    public string            Name            { get => _name;            set => SetField(ref _name, value); }
    public string            Host            { get => _host;            set { SetField(ref _host, value); ResolvedHost = ""; } }
    public string            ResolvedHost    { get => _resolvedHost;    set => SetField(ref _resolvedHost, value); }
    public ScheduledTestType TestType        { get => _testType;        set => SetField(ref _testType, value); }
    public int               Port            { get => _port;            set => SetField(ref _port, value); }
    public int               IntervalMinutes { get => _intervalMinutes; set => SetField(ref _intervalMinutes, value); }
    public bool              IsEnabled       { get => _isEnabled;       set => SetField(ref _isEnabled, value); }
    public string            LastStatus      { get => _lastStatus;      set { SetField(ref _lastStatus, value); OnPropertyChanged(nameof(IsLastFailed)); } }
    public DateTime?         LastRunAt       { get => _lastRunAt;       set { SetField(ref _lastRunAt, value); OnPropertyChanged(nameof(LastRunDisplay)); } }
    public bool              AlertOnFailure  { get => _alertOnFailure;  set => SetField(ref _alertOnFailure, value); }

    public string LastRunDisplay => LastRunAt.HasValue ? LastRunAt.Value.ToString("HH:mm:ss") : "Never";
    public bool   IsLastFailed   => LastStatus.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)
                                 || LastStatus.StartsWith("ERR",  StringComparison.OrdinalIgnoreCase);

    // Serialization DTO
    public ScheduledTestDto ToDto() => new(Id, Name, Host, (int)TestType, Port, IntervalMinutes, IsEnabled, AlertOnFailure, LastRunAt, LastStatus);
    public static ScheduledTest FromDto(ScheduledTestDto d) => new()
    {
        Id              = d.Id,
        Name            = d.Name,
        Host            = d.Host,
        TestType        = (ScheduledTestType)d.TestType,
        Port            = d.Port,
        IntervalMinutes = d.IntervalMinutes,
        IsEnabled       = d.IsEnabled,
        AlertOnFailure  = d.AlertOnFailure,
        LastRunAt       = d.LastRunAt,
        LastStatus      = d.LastStatus ?? "—"
    };
}

public record ScheduledTestDto(Guid Id, string Name, string Host, int TestType, int Port, int IntervalMinutes, bool IsEnabled, bool AlertOnFailure, DateTime? LastRunAt, string? LastStatus);

/// <summary>
/// View-model for the Scheduled Tests sub-tab.
/// A <see cref="DispatcherTimer"/> fires every 30 seconds and runs any tests whose
/// interval has elapsed since their <see cref="ScheduledTest.LastRunAt"/>.
/// </summary>
public class ScheduledTestsViewModel : ViewModelBase
{
    private static readonly string DataDir  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string DataFile = Path.Combine(DataDir, "scheduled_tests.json");
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    private readonly DispatcherTimer _timer;
    private ScheduledTest? _selected;
    private string _alerts = "";

    // ── Edit-form fields (bound to the inline "add/edit" form) ───────────────
    private string            _editName            = "";
    private string            _editHost            = "";
    private ScheduledTestType _editType            = ScheduledTestType.Ping;
    private int               _editPort            = 80;
    private int               _editIntervalMinutes = 5;
    private bool              _editAlertOnFailure  = true;

    private readonly ObservableCollection<PingEntryViewModel> _pingEntries;

    public ObservableCollection<ScheduledTest>  Tests    { get; } = new();
    public ObservableCollection<string>         AllHosts { get; } = new();
    public static IReadOnlyList<string>         TestTypes => Enum.GetNames(typeof(ScheduledTestType));

    public ScheduledTest? Selected
    {
        get => _selected;
        set
        {
            if (!SetField(ref _selected, value)) return;
            if (value is null) return;
            EditName            = value.Name;
            EditHost            = value.Host;
            EditType            = value.TestType;
            EditPort            = value.Port;
            EditIntervalMinutes = value.IntervalMinutes;
            EditAlertOnFailure  = value.AlertOnFailure;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string            Alerts             { get => _alerts;             set => SetField(ref _alerts, value); }
    public string            EditName           { get => _editName;           set => SetField(ref _editName, value); }
    public string            EditHost
    {
        get => _editHost;
        set
        {
            SetField(ref _editHost, value);
            CommandManager.InvalidateRequerySuggested();
            AutoPopulateEditName();
        }
    }
    public ScheduledTestType EditType           { get => _editType;           set => SetField(ref _editType, value); }
    public int               EditPort           { get => _editPort;           set => SetField(ref _editPort, value); }
    public int               EditIntervalMinutes{ get => _editIntervalMinutes; set => SetField(ref _editIntervalMinutes, value); }
    public bool              EditAlertOnFailure { get => _editAlertOnFailure; set => SetField(ref _editAlertOnFailure, value); }

    public RelayCommand AddCommand    { get; }
    public RelayCommand SaveCommand   { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand RunNowCommand { get; }
    public RelayCommand ClearAlerts  { get; }

    public ScheduledTestsViewModel(ObservableCollection<PingEntryViewModel> pingEntries)
    {
        _pingEntries = pingEntries;

        AddCommand    = new RelayCommand(OnAdd,    () => !string.IsNullOrWhiteSpace(EditHost));
        SaveCommand   = new RelayCommand(OnSave,   () => Selected is not null);
        RemoveCommand = new RelayCommand(OnRemove, () => Selected is not null);
        RunNowCommand = new RelayCommand(OnRunNow, () => Selected is not null);
        ClearAlerts   = new RelayCommand(() => Alerts = "");

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += async (_, _) => await RunDueTestsAsync();
        _timer.Start();

        // Keep AllHosts in sync with ping entries
        pingEntries.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (PingEntryViewModel entry in e.NewItems)
                    entry.PropertyChanged += (_, pe) =>
                    {
                        if (pe.PropertyName == nameof(PingEntryViewModel.IpAddress))
                            RebuildAllHosts();
                    };
            RebuildAllHosts();
        };
        foreach (var entry in pingEntries)
            entry.PropertyChanged += (_, pe) =>
            {
                if (pe.PropertyName == nameof(PingEntryViewModel.IpAddress))
                    RebuildAllHosts();
            };
        RebuildAllHosts();

        Load();
    }

    private void RebuildAllHosts()
    {
        AllHosts.Clear();
        foreach (var e in _pingEntries)
            if (!string.IsNullOrWhiteSpace(e.IpAddress))
                AllHosts.Add(e.IpAddress);
    }

    /// <summary>
    /// When <see cref="EditHost"/> matches a ping-list entry that has a user-defined name,
    /// pre-fills <see cref="EditName"/> with that name (only when EditName is blank).
    /// </summary>
    private void AutoPopulateEditName()
    {
        if (string.IsNullOrWhiteSpace(_editHost)) return;
        var match = _pingEntries.FirstOrDefault(p =>
            string.Equals(p.IpAddress, _editHost, StringComparison.OrdinalIgnoreCase));
        if (match is not null && !string.IsNullOrWhiteSpace(match.Name))
            EditName = match.Name;
    }

    // ── CRUD ─────────────────────────────────────────────────────────────────

    private void OnAdd()
    {
        var t = new ScheduledTest
        {
            Name            = string.IsNullOrWhiteSpace(EditName) ? EditHost : EditName,
            Host            = EditHost,
            TestType        = EditType,
            Port            = EditPort,
            IntervalMinutes = EditIntervalMinutes,
            AlertOnFailure  = EditAlertOnFailure
        };
        Tests.Add(t);
        Selected = t;
        _ = ResolveHostNameAsync(t);
        Save();
    }

    private void OnSave()
    {
        if (Selected is null) return;
        Selected.Name            = EditName;
        Selected.Host            = EditHost;
        Selected.TestType        = EditType;
        Selected.Port            = EditPort;
        Selected.IntervalMinutes = EditIntervalMinutes;
        Selected.AlertOnFailure  = EditAlertOnFailure;
        _ = ResolveHostNameAsync(Selected);
        Save();
    }

    private void OnRemove()
    {
        if (Selected is null) return;
        Tests.Remove(Selected);
        Selected = null;
        Save();
    }

    private void OnRunNow()
    {
        if (Selected is null) return;
        _ = Task.Run(() => RunTestAsync(Selected));
    }

    // ── Scheduler ────────────────────────────────────────────────────────────

    private async Task RunDueTestsAsync()
    {
        var now = DateTime.Now;
        var due = Tests.Where(t =>
            t.IsEnabled &&
            (!t.LastRunAt.HasValue || (now - t.LastRunAt.Value).TotalMinutes >= t.IntervalMinutes))
            .ToList();

        foreach (var t in due)
            await Task.Run(() => RunTestAsync(t));
    }

    private async Task RunTestAsync(ScheduledTest test)
    {
        string status;
        try
        {
            status = test.TestType switch
            {
                ScheduledTestType.HTTP => await RunHttpTestAsync(test.Host),
                ScheduledTestType.Port => await RunPortTestAsync(test.Host, test.Port),
                _                     => await RunPingTestAsync(test.Host)
            };
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            status = $"ERROR: {message.Substring(0, Math.Min(60, message.Length))}";
        }

        Dispatch(() =>
        {
            test.LastStatus = status;
            test.LastRunAt  = DateTime.Now;
            if (test.AlertOnFailure && test.IsLastFailed)
                Alerts = $"[{test.LastRunAt:HH:mm:ss}] {test.Name} on {test.Host}: {status}\n" + Alerts;
            Save();
        });
    }

    private static async Task<string> RunPingTestAsync(string host)
    {
        using var p = new Ping();
        var reply = await p.SendPingAsync(host, 3000);
        return reply.Status == IPStatus.Success ? $"OK  {reply.RoundtripTime} ms" : $"FAIL  {reply.Status}";
    }

    private static async Task<string> RunHttpTestAsync(string host)
    {
        var url = host.Contains("://") ? host : "https://" + host;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var sw   = System.Diagnostics.Stopwatch.StartNew();
        var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        sw.Stop();
        return $"{(int)resp.StatusCode} {resp.ReasonPhrase}  {sw.ElapsedMilliseconds} ms";
    }

    private static async Task<string> RunPortTestAsync(string host, int port)
    {
        using var tcp  = new TcpClient();
        using var cts  = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var connectTask = tcp.ConnectAsync(host, port);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
        var completed = await Task.WhenAny(connectTask, timeoutTask);
        if (completed != connectTask)
        {
            cts.Token.ThrowIfCancellationRequested();
            throw new TimeoutException("Connection timed out.");
        }
        await connectTask;
        sw.Stop();
        return $"OPEN  {sw.ElapsedMilliseconds} ms";
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    private void Load()
    {
        if (!File.Exists(DataFile)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<ScheduledTestDto>>(File.ReadAllText(DataFile));
            if (list is null) return;
            foreach (var dto in list)
            {
                var t = ScheduledTest.FromDto(dto);
                Tests.Add(t);
                _ = ResolveHostNameAsync(t);
            }
        }
        catch { /* corrupt file — start fresh */ }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(DataFile, JsonSerializer.Serialize(Tests.Select(t => t.ToDto()).ToList(), Opts));
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Attempts DNS resolution for <paramref name="test"/> and sets
    /// <see cref="ScheduledTest.ResolvedHost"/> if a different name is found.
    /// </summary>
    private static async Task ResolveHostNameAsync(ScheduledTest test)
    {
        if (string.IsNullOrWhiteSpace(test.Host)) return;
        try
        {
            var entry    = await System.Net.Dns.GetHostEntryAsync(test.Host);
            var resolved = entry.HostName;
            if (!string.IsNullOrWhiteSpace(resolved) &&
                !string.Equals(resolved, test.Host, StringComparison.OrdinalIgnoreCase))
            {
                Dispatch(() => test.ResolvedHost = resolved);
            }
        }
        catch { /* DNS resolution not always available */ }
    }

    private static void Dispatch(Action a) => Application.Current.Dispatcher.Invoke(a);
}
