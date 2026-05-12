using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>
/// Root view-model for the application. Owns the <see cref="Entries"/> collection,
/// all toolbar commands, and JSON persistence of entries.
/// </summary>
public class MainViewModel : ViewModelBase
{
    private static readonly string DataDir  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string DataFile = Path.Combine(DataDir, "entries.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>When <c>true</c>, <see cref="SaveEntries"/> is a no-op (used during bulk load).</summary>
    private bool _suppressSave;
    private PingEntryViewModel? _selectedEntry;

    /// <summary>Live collection of all ping entries shown in the DataGrid.</summary>
    public ObservableCollection<PingEntryViewModel> Entries { get; } = new();

    /// <summary>View-model for the Traceroute tab.</summary>
    public TracerouteViewModel TracerouteVM { get; }

    /// <summary>View-model for the Network Info tab.</summary>
    public IPConfigViewModel IPConfigVM { get; }

    /// <summary>View-model for the DNS Lookup tab.</summary>
    public DnsLookupViewModel DnsLookupVM { get; }

    /// <summary>View-model for the Port Scanner tab.</summary>
    public PortScannerViewModel PortScannerVM { get; }

    /// <summary>View-model for the Netstat tab.</summary>
    public NetstatViewModel NetstatVM { get; }

    /// <summary>View-model for the Speed Test tab.</summary>
    public SpeedTestViewModel SpeedTestVM { get; }

    /// <summary>View-model for the MTU Discovery tab.</summary>
    public MtuDiscoveryViewModel MtuDiscoveryVM { get; }

    /// <summary>View-model for the Connectivity &amp; Reachability tab.</summary>
    public ConnectivityViewModel ConnectivityVM { get; }

    /// <summary>View-model for the Monitoring &amp; Analysis tab.</summary>
    public MonitoringViewModel MonitoringVM { get; }

    /// <summary>View-model for the Reporting tab.</summary>
    public ReportingViewModel ReportingVM { get; }

    /// <summary>View-model for the Wi-Fi &amp; Local Network tab.</summary>
    public WifiNetworkViewModel WifiNetworkVM { get; }

    /// <summary>View-model for the Settings tab (tab visibility configuration).</summary>
    public SettingsViewModel SettingsVM { get; }

    /// <summary>The entry currently selected in the DataGrid, or <c>null</c> when nothing is selected.</summary>
    public PingEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            SetField(ref _selectedEntry, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Summary line shown in the status bar (entry count, running, stopped).</summary>
    public string StatusText =>
        Entries.Count == 0
            ? "No entries — click Add to get started"
            : $"{Entries.Count} {(Entries.Count == 1 ? "entry" : "entries")}  |  " +
              $"{Entries.Count(e => e.IsRunning)} running  |  " +
              $"{Entries.Count(e => !e.IsRunning)} stopped";

    /// <summary>Default interval pre-filled in the Add dialog (ms).</summary>
    public int DefaultInterval { get; set; } = 1000;

    /// <summary>Default timeout pre-filled in the Add dialog (ms).</summary>
    public int DefaultTimeout { get; set; } = 1000;

    /// <summary>Opens the Add Entry dialog.</summary>
    public RelayCommand AddCommand { get; }

    /// <summary>Opens the Edit Entry dialog for <see cref="SelectedEntry"/>.</summary>
    public RelayCommand EditCommand { get; }

    /// <summary>Stops and removes <see cref="SelectedEntry"/>.</summary>
    public RelayCommand RemoveCommand { get; }

    /// <summary>Stops and removes all entries after user confirmation.</summary>
    public RelayCommand RemoveAllCommand { get; }

    /// <summary>Starts the ping loop for every entry.</summary>
    public RelayCommand StartAllCommand { get; }

    /// <summary>Stops the ping loop for every entry.</summary>
    public RelayCommand StopAllCommand { get; }

    /// <summary>Adds the given IP/host (passed as <c>string</c> parameter) to the ping list if not already present.</summary>
    public RelayCommand AddToPingListCommand { get; }

    /// <summary>Raised when the view should show the Add Entry dialog.</summary>
    public event EventHandler? AddEntryRequested;

    /// <summary>Raised when the view should show the Edit Entry dialog for the given entry.</summary>
    public event EventHandler<PingEntryViewModel>? EditEntryRequested;

    public MainViewModel()
    {
        AddCommand       = new RelayCommand(OnAdd);
        EditCommand      = new RelayCommand(OnEdit,      () => SelectedEntry is not null);
        RemoveCommand    = new RelayCommand(OnRemove,    () => SelectedEntry is not null);
        RemoveAllCommand = new RelayCommand(OnRemoveAll, () => Entries.Count > 0);
        StartAllCommand  = new RelayCommand(OnStartAll,  () => Entries.Count > 0);
        StopAllCommand   = new RelayCommand(OnStopAll,   () => Entries.Count > 0);
        AddToPingListCommand = new RelayCommand(
            param =>
            {
                var (ip, name) = ExtractPingTarget(param);
                if (string.IsNullOrWhiteSpace(ip)) return;
                var entry = new PingEntryViewModel { IpAddress = ip, Name = name, Interval = DefaultInterval, Timeout = DefaultTimeout };
                AddEntry(entry, startImmediately: true);
            },
            param =>
            {
                var (ip, _) = ExtractPingTarget(param);
                return !string.IsNullOrWhiteSpace(ip)
                    && !Entries.Any(e => string.Equals(e.IpAddress, ip, StringComparison.OrdinalIgnoreCase));
            });

        TracerouteVM  = new TracerouteViewModel(Entries);
        IPConfigVM    = new IPConfigViewModel();
        DnsLookupVM   = new DnsLookupViewModel(Entries);
        PortScannerVM = new PortScannerViewModel(Entries);
        NetstatVM     = new NetstatViewModel();
        SpeedTestVM   = new SpeedTestViewModel();
        MtuDiscoveryVM  = new MtuDiscoveryViewModel(Entries);
        ConnectivityVM  = new ConnectivityViewModel(Entries);
        MonitoringVM    = new MonitoringViewModel(Entries);
        ReportingVM     = new ReportingViewModel(Entries);
        WifiNetworkVM   = new WifiNetworkViewModel();
        SettingsVM      = new SettingsViewModel();

        Entries.CollectionChanged+= OnEntriesChanged;

        // Load with startImmediately:false — the view calls StartAll() once the window is Loaded
        // to avoid dispatching back to a not-yet-ready UI during construction.
        LoadEntries();
    }

    // ── Collection change bookkeeping ─────────────────────────────────────────

    private static (string ip, string name) ExtractPingTarget(object? param) => param switch
    {
        PingTargetArg arg => (arg.Ip, arg.Name),
        string s          => (s, s),
        _                 => ("", "")
    };

    /// <summary>
    /// Subscribes/unsubscribes <see cref="OnEntryPropertyChanged"/> as entries enter or leave
    /// the collection, and refreshes <see cref="StatusText"/>.
    /// </summary>
    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (PingEntryViewModel entry in e.NewItems)
                entry.PropertyChanged += OnEntryPropertyChanged;

        if (e.OldItems is not null)
            foreach (PingEntryViewModel entry in e.OldItems)
                entry.PropertyChanged -= OnEntryPropertyChanged;

        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>Refreshes <see cref="StatusText"/> whenever an entry's <c>IsRunning</c> changes.</summary>
    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PingEntryViewModel.IsRunning))
            OnPropertyChanged(nameof(StatusText));
    }

    // ── Public entry management ───────────────────────────────────────────────

    /// <summary>
    /// Adds <paramref name="entry"/> to <see cref="Entries"/>, optionally starts its ping loop,
    /// then persists the collection.
    /// </summary>
    public void AddEntry(PingEntryViewModel entry, bool startImmediately = true)
    {
        Entries.Add(entry);
        if (startImmediately)
            entry.Start();
        SaveEntries();
    }

    /// <summary>
    /// Starts the ping loop for every entry.
    /// Called by the view's <c>Loaded</c> event to begin pinging after entries are restored
    /// from disk, and also bound to <see cref="StartAllCommand"/>.
    /// </summary>
    public void StartAll()
    {
        foreach (var entry in Entries)
            entry.Start();
    }

    // ── Command handlers ──────────────────────────────────────────────────────

    /// <summary>Fires <see cref="AddEntryRequested"/> so the view can show the Add dialog.</summary>
    private void OnAdd() => AddEntryRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Fires <see cref="EditEntryRequested"/> so the view can show the Edit dialog.</summary>
    private void OnEdit()
    {
        if (SelectedEntry is null) return;
        EditEntryRequested?.Invoke(this, SelectedEntry);
    }

    /// <summary>Stops and removes the selected entry, then persists.</summary>
    private void OnRemove()
    {
        if (SelectedEntry is null) return;
        SelectedEntry.Stop();
        Entries.Remove(SelectedEntry);
        SelectedEntry = null;
        SaveEntries();
    }

    /// <summary>
    /// Prompts the user for confirmation, then stops and removes all entries, then persists.
    /// </summary>
    private void OnRemoveAll()
    {
        if (Entries.Count == 0) return;

        var result = MessageBox.Show(
            $"Remove all {Entries.Count} ping {(Entries.Count == 1 ? "entry" : "entries")}?",
            "Confirm Remove All",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        foreach (var entry in Entries)
            entry.Stop();

        Entries.Clear();
        SelectedEntry = null;
        SaveEntries();
    }

    /// <summary>Starts the ping loop on every entry.</summary>
    private void OnStartAll() => StartAll();

    /// <summary>Stops the ping loop on every entry.</summary>
    private void OnStopAll()
    {
        foreach (var entry in Entries)
            entry.Stop();
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    /// <summary>
    /// Serialises all entries to <c>Data\entries.json</c> next to the executable.
    /// Creates the <c>Data</c> directory if it does not exist.
    /// I/O errors are logged to the debug output and swallowed so the UI is never disrupted.
    /// No-op while <see cref="_suppressSave"/> is set (e.g. during initial load).
    /// </summary>
    public void SaveEntries()
    {
        if (_suppressSave) return;
        try
        {
            Directory.CreateDirectory(DataDir);
            var dtos = Entries.Select(e => new EntryData
            {
                Name      = e.Name,
                IpAddress = e.IpAddress,
                Interval  = e.Interval,
                Timeout   = e.Timeout
            });
            File.WriteAllText(DataFile, JsonSerializer.Serialize(dtos, JsonOpts));
        }
        catch (Exception ex) { Debug.WriteLine($"[LittlePinger] Save failed: {ex.Message}"); }
    }

    /// <summary>
    /// Deserialises entries from <c>Data\entries.json</c> and adds them to <see cref="Entries"/>
    /// without starting their ping loops (pings are started later via <see cref="StartAll"/>
    /// once the window is fully loaded).
    /// Suppresses <see cref="SaveEntries"/> during the bulk add to avoid redundant writes.
    /// </summary>
    private void LoadEntries()
    {
        if (!File.Exists(DataFile)) return;
        _suppressSave = true;
        try
        {
            var dtos = JsonSerializer.Deserialize<List<EntryData>>(File.ReadAllText(DataFile));
            if (dtos is null) return;
            foreach (var d in dtos)
                AddEntry(new PingEntryViewModel
                {
                    Name      = d.Name,
                    IpAddress = d.IpAddress,
                    Interval  = d.Interval,
                    Timeout   = d.Timeout
                }, startImmediately: false);
        }
        catch (Exception ex) { Debug.WriteLine($"[LittlePinger] Load failed: {ex.Message}"); }
        finally { _suppressSave = false; }
    }
}
