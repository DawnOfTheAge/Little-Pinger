using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>One discovered device on the local subnet.</summary>
public class LanDevice : ViewModelBase
{
    private string _ip = "", _mac = "—", _hostname = "—", _responseMs = "—", _status = "Scanning…";

    public string IP         { get => _ip;         set => SetField(ref _ip, value); }
    public string MAC        { get => _mac;         set => SetField(ref _mac, value); }
    public string Hostname   { get => _hostname;    set => SetField(ref _hostname, value); }
    public string ResponseMs { get => _responseMs;  set => SetField(ref _responseMs, value); }
    public string Status     { get => _status;      set => SetField(ref _status, value); }
    public bool   IsOnline   => Status == "Online";

    /// <summary>Bundles IP + resolved hostname for the "Add to Ping List" context-menu command.</summary>
    public PingTargetArg PingTarget => new(IP, string.IsNullOrWhiteSpace(Hostname) || Hostname == "—" ? IP : Hostname);
}

/// <summary>
/// View-model for the LAN Device Scanner sub-tab.
/// Performs a parallel ping-sweep of the local /24 subnet, then enriches each
/// online host with ARP-derived MAC and optional reverse-DNS hostname.
/// </summary>
public class LanScannerViewModel : ViewModelBase
{
    private CancellationTokenSource? _cts;
    private bool   _isRunning;
    private int    _progressPercent;
    private string _statusText   = "Ready — press Scan to discover devices";
    private string _subnetHint   = "";
    private bool   _resolveNames = true;
    private int    _maskBits     = 24;
    private string _searchText   = "";

    public int MaskBits { get => _maskBits; set => SetField(ref _maskBits, value); }

    public ObservableCollection<LanDevice> Devices         { get; } = new();
    public ICollectionView                 FilteredDevices { get; }

    public bool   IsRunning       { get => _isRunning;       set { SetField(ref _isRunning, value); CommandManager.InvalidateRequerySuggested(); } }
    public int    ProgressPercent { get => _progressPercent; set => SetField(ref _progressPercent, value); }
    public string StatusText      { get => _statusText;      set => SetField(ref _statusText, value); }
    public string SubnetHint      { get => _subnetHint;      set => SetField(ref _subnetHint, value); }
    public bool   ResolveNames    { get => _resolveNames;    set => SetField(ref _resolveNames, value); }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value)) return;
            FilteredDevices.Refresh();
            OnPropertyChanged(nameof(FilteredCount));
        }
    }

    public int FilteredCount => FilteredDevices.Cast<LanDevice>().Count();

    public RelayCommand ScanCommand   { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ClearSearchCommand { get; }

    public LanScannerViewModel()
    {
        FilteredDevices = CollectionViewSource.GetDefaultView(Devices);
        FilteredDevices.Filter = obj =>
        {
            if (string.IsNullOrWhiteSpace(_searchText)) return true;
            if (obj is not LanDevice d) return false;
            var q = _searchText.Trim();
            return d.IP.Contains(q, StringComparison.OrdinalIgnoreCase)
                || d.MAC.Contains(q, StringComparison.OrdinalIgnoreCase)
                || d.Hostname.Contains(q, StringComparison.OrdinalIgnoreCase);
        };

        // Re-evaluate FilteredCount whenever devices are added/removed
        Devices.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FilteredCount));

        ScanCommand        = new RelayCommand(OnScan,   () => !IsRunning);
        CancelCommand      = new RelayCommand(OnCancel, () => IsRunning);
        ClearSearchCommand = new RelayCommand(() => SearchText = "");

        SubnetHint = DetectLocalSubnet() ?? "192.168.1";
    }

    private void OnScan()
    {
        _cts          = new CancellationTokenSource();
        IsRunning     = true;
        ProgressPercent = 0;
        Dispatch(() => Devices.Clear());
        _ = Task.Run(() => RunScanAsync(_cts.Token));
    }

    private async Task RunScanAsync(CancellationToken ct)
    {
        string subnet = SubnetHint.TrimEnd('.');
        if (string.IsNullOrWhiteSpace(subnet)) subnet = "192.168.1";

        // Build network base address (append .0 if only 3 octets given)
        string baseIpStr = subnet.Split('.').Length >= 3 ? subnet + ".0" : subnet + ".0.0";
        if (!IPAddress.TryParse(baseIpStr, out var baseIp))
            baseIp = IPAddress.Parse("192.168.1.0");

        uint networkInt = IpToUint(baseIp);
        uint mask       = MaskBits == 0 ? 0u : 0xFFFFFFFFu << (32 - MaskBits);
        networkInt      = networkInt & mask;          // snap to network boundary
        uint broadcast  = networkInt | ~mask;
        uint totalHosts = broadcast - networkInt - 1; // usable host count

        // Cap at 4096 to keep the UI responsive
        uint maxHosts = Math.Min(totalHosts, 4096);

        Dispatch(() => StatusText = $"Scanning {UintToIp(networkInt)}/{MaskBits}  ({maxHosts} addresses)…");

        const int concurrency = 50;
        var sem    = new SemaphoreSlim(concurrency);
        var tasks  = new List<Task>();
        int done   = 0;

        for (uint i = 1; i <= maxHosts; i++)
        {
            if (ct.IsCancellationRequested) break;
            string ip = UintToIp(networkInt + i);

            tasks.Add(Task.Run(async () =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(ip, 600);
                    if (reply.Status == IPStatus.Success)
                    {
                        var dev = new LanDevice { IP = ip, ResponseMs = $"{reply.RoundtripTime} ms", Status = "Online" };
                        Dispatch(() => Devices.Add(dev));
                    }
                }
                catch { /* unreachable — ignore */ }
                finally
                {
                    sem.Release();
                    int d = Interlocked.Increment(ref done);
                    Dispatch(() => ProgressPercent = (int)(d * 100 / maxHosts));
                }
            }, ct));
        }

        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }

        if (!ct.IsCancellationRequested)
        {
            // Enrich with MACs from ARP table
            var arps = await ReadArpTableAsync();
            Dispatch(() =>
            {
                foreach (var dev in Devices)
                {
                    if (arps.TryGetValue(dev.IP, out var mac)) dev.MAC = mac;
                }
                StatusText = $"Found {Devices.Count} online device(s)  ·  Scan complete";
            });

            // Resolve hostnames if requested
            if (ResolveNames)
            {
                var snapshot = Dispatch(() => Devices.ToList());
                foreach (var dev in snapshot)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        var entry = await Dns.GetHostEntryAsync(dev.IP);
                        Dispatch(() => dev.Hostname = entry.HostName);
                    }
                    catch { /* no PTR record — leave as "—" */ }
                }
            }
        }

        Dispatch(() => { IsRunning = false; ProgressPercent = 100; });
    }

    private static async Task<Dictionary<string, string>> ReadArpTableAsync()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var psi = new ProcessStartInfo("arp", "-a")
            {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
            };
            await using var stream = Process.Start(psi)!.StandardOutput.BaseStream;
            using var reader = new System.IO.StreamReader(stream);
            var output = await reader.ReadToEndAsync();
            var rx = new Regex(@"^\s*([\d.]+)\s+([0-9a-fA-F-]{17})", RegexOptions.Multiline);
            foreach (Match m in rx.Matches(output))
                dict[m.Groups[1].Value] = m.Groups[2].Value;
        }
        catch { /* best-effort */ }
        return dict;
    }

    private static string? DetectLocalSubnet()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var parts = ua.Address.ToString().Split('.');
                if (parts.Length == 4 && parts[0] != "127")
                    return $"{parts[0]}.{parts[1]}.{parts[2]}";
            }
        }
        return null;
    }

    private void OnCancel() => _cts?.Cancel();

    private static uint IpToUint(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    private static string UintToIp(uint ip) =>
        $"{(ip >> 24) & 0xFF}.{(ip >> 16) & 0xFF}.{(ip >> 8) & 0xFF}.{ip & 0xFF}";

    private static void Dispatch(Action a) => Application.Current.Dispatcher.Invoke(a);
    private static T    Dispatch<T>(Func<T> f) => Application.Current.Dispatcher.Invoke(f);
}
