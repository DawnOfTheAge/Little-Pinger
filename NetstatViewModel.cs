using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>One row in the active-connections list.</summary>
public class NetstatEntry
{
    public string Protocol       { get; init; } = "";
    public string LocalAddress   { get; init; } = "";
    public string ForeignAddress { get; init; } = "";
    public string State          { get; init; } = "";
    public int    ProcessId      { get; init; }
    public string Executable     { get; init; } = "";

    /// <summary>IP-only portion of <see cref="LocalAddress"/> (no port), for context-menu binding.</summary>
    public string LocalIp   => ExtractIp(LocalAddress);

    /// <summary>IP-only portion of <see cref="ForeignAddress"/> (no port), for context-menu binding.</summary>
    public string ForeignIp => ExtractIp(ForeignAddress);

    /// <summary>Bundles local IP for the "Add to Ping List" command.</summary>
    public PingTargetArg LocalPingTarget   => new(LocalIp,   LocalIp);

    /// <summary>Bundles foreign IP for the "Add to Ping List" command.</summary>
    public PingTargetArg ForeignPingTarget => new(ForeignIp, ForeignIp);

    private static string ExtractIp(string addr)
    {
        if (string.IsNullOrEmpty(addr) || addr == "*:*") return "";
        // IPv6 notation: [::1]:80 → strip brackets → ::1
        if (addr.StartsWith("["))
        {
            var close = addr.IndexOf(']');
            return close > 1 ? addr.Substring(1, close - 1) : addr;
        }
        var lastColon = addr.LastIndexOf(':');
        return lastColon > 0 ? addr.Substring(0, lastColon) : addr;
    }
}

/// <summary>
/// View-model for the Netstat tab. Runs <c>netstat -ano</c> and surfaces active
/// TCP/UDP connections with protocol, addresses, state, PID, and executable name.
/// Supports filtering by protocol, state, and a free-text search across addresses,
/// ports, PID and executable name.
/// </summary>
public class NetstatViewModel : ViewModelBase
{
    private NetstatEntry? _selectedConnection;
    private string        _statusText      = "Click Refresh to load connections";
    private string        _protocolFilter  = "All";
    private string        _stateFilter     = "All";
    private string        _searchText      = "";
    private List<string>  _availableStates = new() { "All" };
    private int           _totalCount;

    // ── Raw data collection ──────────────────────────────────────────────────
    public ObservableCollection<NetstatEntry> Connections { get; } = new();

    // ── Filtered view (DataGrid binds to this) ───────────────────────────────
    public ICollectionView ConnectionsView { get; }

    // ── Filter sources ───────────────────────────────────────────────────────
    public static IReadOnlyList<string> AvailableProtocols { get; } = new[] { "All", "TCP", "UDP" };

    public List<string> AvailableStates
    {
        get => _availableStates;
        private set => SetField(ref _availableStates, value);
    }

    // ── Filter properties ────────────────────────────────────────────────────
    public string ProtocolFilter
    {
        get => _protocolFilter;
        set { SetField(ref _protocolFilter, value); RefreshView(); }
    }

    public string StateFilter
    {
        get => _stateFilter;
        set { SetField(ref _stateFilter, value); RefreshView(); }
    }

    public string SearchText
    {
        get => _searchText;
        set { SetField(ref _searchText, value); RefreshView(); }
    }

    // ── Selection & status ───────────────────────────────────────────────────
    public NetstatEntry? SelectedConnection
    {
        get => _selectedConnection;
        set
        {
            SetField(ref _selectedConnection, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    // ── Commands ─────────────────────────────────────────────────────────────
    public RelayCommand RefreshCommand      { get; }
    public RelayCommand KillProcessCommand  { get; }
    public RelayCommand ClearFiltersCommand { get; }

    public NetstatViewModel()
    {
        ConnectionsView        = CollectionViewSource.GetDefaultView(Connections);
        ConnectionsView.Filter = FilterEntry;

        RefreshCommand      = new RelayCommand(OnRefresh);
        KillProcessCommand  = new RelayCommand(OnKillProcess, () => SelectedConnection is not null);
        ClearFiltersCommand = new RelayCommand(OnClearFilters);
    }

    // ── Filter logic ─────────────────────────────────────────────────────────
    private bool FilterEntry(object obj)
    {
        if (obj is not NetstatEntry e) return false;

        if (_protocolFilter != "All" && e.Protocol != _protocolFilter) return false;
        if (_stateFilter    != "All" && e.State    != _stateFilter)    return false;

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var s = _searchText.Trim();
            if (e.LocalAddress.IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0 &&
                e.ForeignAddress.IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0 &&
                e.ProcessId.ToString().IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0 &&
                e.Executable.IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }

        return true;
    }

    private void RefreshView()
    {
        ConnectionsView.Refresh();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_totalCount == 0) { StatusText = "Click Refresh to load connections"; return; }
        var visible = ConnectionsView.Cast<NetstatEntry>().Count();
        StatusText = visible == _totalCount
            ? $"{_totalCount} connections"
            : $"{visible} of {_totalCount} connections (filtered)";
    }

    private void OnClearFilters()
    {
        _protocolFilter = "All";
        _stateFilter    = "All";
        _searchText     = "";
        OnPropertyChanged(nameof(ProtocolFilter));
        OnPropertyChanged(nameof(StateFilter));
        OnPropertyChanged(nameof(SearchText));
        RefreshView();
    }

    // ── Refresh / load ───────────────────────────────────────────────────────
    private void OnRefresh()
    {
        StatusText = "Loading…";
        _ = Task.Run(LoadConnectionsAsync);
    }

    private async Task LoadConnectionsAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("netstat", "-ano")
            {
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            var list = new List<NetstatEntry>();
            using var proc = Process.Start(psi)!;
            var output = await proc.StandardOutput.ReadToEndAsync();
#if NET472
            await Task.Run(() => proc.WaitForExit());
#else
            await proc.WaitForExitAsync();
#endif

            foreach (var rawLine in output.Split('\n'))
            {
                var line  = rawLine.Trim();
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;

                string proto = parts[0].ToUpperInvariant();
                if (proto != "TCP" && proto != "UDP") continue;

                NetstatEntry entry;

                if (proto == "TCP" && parts.Length >= 5)
                {
                    // TCP: Proto  LocalAddr  ForeignAddr  State  PID
                    if (!int.TryParse(parts[parts.Length - 1], out int pid)) continue;
                    entry = new NetstatEntry
                    {
                        Protocol       = proto,
                        LocalAddress   = parts[1],
                        ForeignAddress = parts[2],
                        State          = parts[3],
                        ProcessId      = pid,
                        Executable     = GetExecutableName(pid)
                    };
                }
                else if (proto == "UDP" && parts.Length >= 4)
                {
                    // UDP: Proto  LocalAddr  ForeignAddr  PID
                    if (!int.TryParse(parts[parts.Length - 1], out int pid)) continue;
                    entry = new NetstatEntry
                    {
                        Protocol       = proto,
                        LocalAddress   = parts[1],
                        ForeignAddress = parts[2],
                        State          = "",
                        ProcessId      = pid,
                        Executable     = GetExecutableName(pid)
                    };
                }
                else continue;

                list.Add(entry);
            }

            // Build sorted distinct state list for the State filter dropdown
            var states = new[] { "All" }
                .Concat(list.Select(e => e.State)
                            .Where(s => !string.IsNullOrEmpty(s))
                            .Distinct()
                            .OrderBy(s => s))
                .ToList();

            Dispatch(() =>
            {
                Connections.Clear();
                foreach (var e in list)
                    Connections.Add(e);

                // If the current state filter no longer exists in the fresh data, reset it
                if (!states.Contains(_stateFilter))
                {
                    _stateFilter = "All";
                    OnPropertyChanged(nameof(StateFilter));
                }

                AvailableStates = states;
                _totalCount     = list.Count;
                ConnectionsView.Refresh();
                UpdateStatus();
            });
        }
        catch (Exception ex)
        {
            Dispatch(() => StatusText = $"Error: {ex.Message}");
        }
    }

    private static string GetExecutableName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            try   { return p.MainModule?.FileName ?? p.ProcessName; }
            catch { return p.ProcessName; }
        }
        catch { return "(unknown)"; }
    }

    private void OnKillProcess()
    {
        if (SelectedConnection is null) return;
        var conn = SelectedConnection;

        var result = MessageBox.Show(
            $"Kill process '{conn.Executable}' (PID {conn.ProcessId})?\n\nThis will terminate the process immediately.",
            "Kill Process",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            using var p = Process.GetProcessById(conn.ProcessId);
            p.Kill();
            OnRefresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to kill process: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void Dispatch(Action a) => Application.Current.Dispatcher.Invoke(a);
}
