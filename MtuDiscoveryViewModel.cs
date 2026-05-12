using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>
/// View-model for the MTU Discovery tab.
/// Uses a binary search over <c>ping -f -l {size}</c> probes to find the maximum
/// payload that traverses the path without fragmentation.
/// Discovered MTU = max payload + 28 (20-byte IP header + 8-byte ICMP header).
/// </summary>
public class MtuDiscoveryViewModel : ViewModelBase
{
    private static readonly string DataDir     = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string HistoryFile = Path.Combine(DataDir, "mtu_history.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly ObservableCollection<PingEntryViewModel> _pingEntries;
    private CancellationTokenSource? _cts;

    private string _host         = "";
    private string _output       = "";
    private string _resultText   = "";
    private int    _discoveredMtu;
    private bool   _isRunning;

    public ObservableCollection<string> History  { get; } = new();
    public ObservableCollection<string> AllHosts { get; } = new();

    public string Host
    {
        get => _host;
        set { SetField(ref _host, value); CommandManager.InvalidateRequerySuggested(); }
    }

    public string Output
    {
        get => _output;
        set => SetField(ref _output, value);
    }

    /// <summary>Plain-text result summary shown below the log pane (empty until a run completes).</summary>
    public string ResultText
    {
        get => _resultText;
        set => SetField(ref _resultText, value);
    }

    public int DiscoveredMtu
    {
        get => _discoveredMtu;
        set => SetField(ref _discoveredMtu, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set { SetField(ref _isRunning, value); CommandManager.InvalidateRequerySuggested(); }
    }

    public RelayCommand RunCommand    { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ClearCommand  { get; }

    public MtuDiscoveryViewModel(ObservableCollection<PingEntryViewModel> pingEntries)
    {
        _pingEntries  = pingEntries;
        RunCommand    = new RelayCommand(OnRun,    () => !string.IsNullOrWhiteSpace(Host) && !IsRunning);
        CancelCommand = new RelayCommand(OnCancel, () => IsRunning);
        ClearCommand  = new RelayCommand(OnClear,  () => !IsRunning);

        pingEntries.CollectionChanged += (_, _) => RebuildAllHosts();
        LoadHistory();
        RebuildAllHosts();
    }

    // ── Address list ─────────────────────────────────────────────────────────

    private void RebuildAllHosts()
    {
        AllHosts.Clear();
        foreach (var h in History) AllHosts.Add(h);
        foreach (var e in _pingEntries)
            if (!string.IsNullOrWhiteSpace(e.IpAddress) && !AllHosts.Contains(e.IpAddress))
                AllHosts.Add(e.IpAddress);
    }

    // ── Command handlers ─────────────────────────────────────────────────────

    private void OnRun()
    {
        var host = Host.Trim();
        if (string.IsNullOrEmpty(host)) return;

        _cts         = new CancellationTokenSource();
        IsRunning    = true;
        DiscoveredMtu = 0;
        ResultText   = "";
        Output       = $"MTU Discovery  →  {host}\r\n" +
                       "Method: ICMP ping with DF bit (binary search 0–1472)\r\n\r\n";

        Task.Run(() => RunDiscovery(host, _cts.Token));
    }

    private void RunDiscovery(string host, CancellationToken ct)
    {
        try
        {
            // ── Connectivity check ────────────────────────────────────────
            Dispatch(() => Output += "Checking connectivity (0-byte probe)… ");
            if (!Probe(host, 0, ct))
            {
                Dispatch(() =>
                {
                    Output    += "FAILED\r\nICMP is filtered or host is unreachable.\r\n";
                    ResultText = "❌  Unable to determine MTU — ICMP blocked or host unreachable";
                    IsRunning  = false;
                });
                return;
            }
            Dispatch(() => Output += "OK\r\n\r\n");

            // ── Fast path: standard Ethernet (1472) ───────────────────────
            const int maxPayload = 1472; // 1500 – 28
            Dispatch(() => Output += $"  Probe 1472 bytes (1500 MTU)… ");
            if (Probe(host, maxPayload, ct))
            {
                int mtu = maxPayload + 28;
                Dispatch(() =>
                {
                    Output       += $"OK  →  MTU = {mtu}\r\n";
                    DiscoveredMtu = mtu;
                    ResultText    = $"✅  Discovered MTU: {mtu} bytes  (standard Ethernet)";
                    AppendToHistory(host);
                    IsRunning     = false;
                });
                return;
            }
            Dispatch(() => Output += "Too large — starting binary search…\r\n\r\n");

            // ── Binary search ─────────────────────────────────────────────
            int lo = 0, hi = maxPayload;
            while (hi - lo > 1 && !ct.IsCancellationRequested)
            {
                int mid = (lo + hi) / 2;
                Dispatch(() => Output += $"  Probe {mid,4} bytes… ");
                bool ok = Probe(host, mid, ct);
                Dispatch(() => Output += (ok ? "OK\r\n" : "Too large\r\n"));
                if (ok) lo = mid; else hi = mid;
            }

            if (ct.IsCancellationRequested)
            {
                Dispatch(() => { Output += "\r\n[Cancelled]\r\n"; IsRunning = false; });
                return;
            }

            int discovered = lo + 28;
            Dispatch(() =>
            {
                Output       += $"\r\nMax payload without fragmentation: {lo} bytes\r\n";
                Output       += $"Discovered MTU: {lo} + 28 (IP+ICMP) = {discovered} bytes\r\n";
                DiscoveredMtu = discovered;
                ResultText    = $"✅  Discovered MTU: {discovered} bytes";
                AppendToHistory(host);
                IsRunning     = false;
            });
        }
        catch (Exception ex)
        {
            Dispatch(() => { Output += $"\r\nError: {ex.Message}\r\n"; IsRunning = false; });
        }
    }

    /// <summary>
    /// Sends one ICMP echo with DF bit set and the given payload size.
    /// Returns <c>true</c> if a reply was received without fragmentation error.
    /// </summary>
    private static bool Probe(string host, int payloadBytes, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;

        var psi = new ProcessStartInfo("ping", $"-f -l {payloadBytes} -n 1 -w 2000 \"{host}\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        try
        {
            using var proc = Process.Start(psi)!;
            string output  = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(6000);
            return output.Contains("Reply from") && !output.Contains("needs to be fragmented");
        }
        catch { return false; }
    }

    private void AppendToHistory(string host)
    {
        if (!History.Contains(host))
        {
            History.Insert(0, host);
            if (History.Count > 50) History.RemoveAt(History.Count - 1);
            RebuildAllHosts();
            SaveHistory();
        }
    }

    private void OnCancel() => _cts?.Cancel();

    private void OnClear()
    {
        Output        = "";
        ResultText    = "";
        DiscoveredMtu = 0;
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    private void LoadHistory()
    {
        if (!File.Exists(HistoryFile)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(HistoryFile));
            if (list is null) return;
            foreach (var h in list) History.Add(h);
        }
        catch (Exception ex) { Debug.WriteLine($"[MtuVM] Load: {ex.Message}"); }
    }

    private void SaveHistory()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(HistoryFile, JsonSerializer.Serialize(History.ToList(), JsonOpts));
        }
        catch (Exception ex) { Debug.WriteLine($"[MtuVM] Save: {ex.Message}"); }
    }

    private static void Dispatch(Action a) => Application.Current.Dispatcher.Invoke(a);
}
