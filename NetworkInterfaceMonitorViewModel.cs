using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Windows.Input;
using System.Windows.Threading;

namespace LittlePinger.ViewModels;

/// <summary>Snapshot of one network interface's bandwidth statistics.</summary>
public class InterfaceMonitorEntry : ViewModelBase
{
    private string _name = "", _status = "", _rxPerSec = "—", _txPerSec = "—", _totalRx = "—", _totalTx = "—";

    public string Name     { get => _name;     set => SetField(ref _name, value); }
    public string Status   { get => _status;   set => SetField(ref _status, value); }
    public string RxPerSec { get => _rxPerSec; set => SetField(ref _rxPerSec, value); }
    public string TxPerSec { get => _txPerSec; set => SetField(ref _txPerSec, value); }
    public string TotalRx  { get => _totalRx;  set => SetField(ref _totalRx, value); }
    public string TotalTx  { get => _totalTx;  set => SetField(ref _totalTx, value); }
}

/// <summary>
/// View-model for the Network Interface Monitor sub-tab.
/// Uses a <see cref="DispatcherTimer"/> (UI thread) so that <see cref="Interfaces"/>
/// can be mutated directly without Dispatcher.Invoke.
/// </summary>
public class NetworkInterfaceMonitorViewModel : ViewModelBase
{
    private readonly DispatcherTimer          _timer;
    private readonly Dictionary<string, long> _prevRx = new();
    private readonly Dictionary<string, long> _prevTx = new();
    private bool _isRunning;

    public ObservableCollection<InterfaceMonitorEntry> Interfaces { get; } = new();
    public bool IsRunning { get => _isRunning; set { SetField(ref _isRunning, value); CommandManager.InvalidateRequerySuggested(); } }

    public RelayCommand StartCommand   { get; }
    public RelayCommand StopCommand    { get; }
    public RelayCommand RefreshCommand { get; }

    public NetworkInterfaceMonitorViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Poll();

        StartCommand   = new RelayCommand(() => { IsRunning = true;  _timer.Start(); Poll(); }, () => !IsRunning);
        StopCommand    = new RelayCommand(() => { IsRunning = false; _timer.Stop();          }, () => IsRunning);
        RefreshCommand = new RelayCommand(Poll);

        Poll(); // initial snapshot (no rates yet, just totals)
    }

    private void Poll()
    {
        try
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            foreach (var nic in nics)
            {
                IPv4InterfaceStatistics stats;
                try { stats = nic.GetIPv4Statistics(); }
                catch { continue; }

                long rx = stats.BytesReceived, tx = stats.BytesSent;

                string rxRate = "—", txRate = "—";
                if (_prevRx.TryGetValue(nic.Id, out long prx) &&
                    _prevTx.TryGetValue(nic.Id, out long ptx))
                {
                    rxRate = FormatBytes(Math.Max(0, rx - prx)) + "/s";
                    txRate = FormatBytes(Math.Max(0, tx - ptx)) + "/s";
                }
                _prevRx[nic.Id] = rx;
                _prevTx[nic.Id] = tx;

                var entry = Interfaces.FirstOrDefault(e => e.Name == nic.Name);
                if (entry is null) { entry = new InterfaceMonitorEntry { Name = nic.Name }; Interfaces.Add(entry); }

                entry.Status   = nic.OperationalStatus.ToString();
                entry.RxPerSec = rxRate;
                entry.TxPerSec = txRate;
                entry.TotalRx  = FormatBytes(rx);
                entry.TotalTx  = FormatBytes(tx);
            }

            // Remove stale entries
            var current = nics.Select(n => n.Name).ToHashSet();
            for (int i = Interfaces.Count - 1; i >= 0; i--)
                if (!current.Contains(Interfaces[i].Name)) Interfaces.RemoveAt(i);
        }
        catch { /* swallow transient errors */ }
    }

    private static string FormatBytes(long b)
    {
        if (b < 0) b = 0;
        if (b < 1_024)           return $"{b} B";
        if (b < 1_048_576)       return $"{b / 1_024.0:F1} KB";
        if (b < 1_073_741_824)   return $"{b / 1_048_576.0:F1} MB";
        return                          $"{b / 1_073_741_824.0:F2} GB";
    }
}
