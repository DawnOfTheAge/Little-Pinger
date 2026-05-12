using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>
/// View-model for the DNS Lookup tab. Wraps nslookup and streams output line-by-line.
/// Supports forward and reverse lookups with a selectable record type.
/// </summary>
public class DnsLookupViewModel : ViewModelBase
{
    private static readonly string DataDir     = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string HistoryFile = Path.Combine(DataDir, "dns_history.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly ObservableCollection<PingEntryViewModel> _pingEntries;
    private CancellationTokenSource? _cts;

    private string _host       = "";
    private string _recordType = "AUTO";
    private string _dnsServer  = "";
    private string _output     = "";
    private bool   _isRunning;

    public ObservableCollection<string> History  { get; } = new();
    public ObservableCollection<string> AllHosts { get; } = new();

    public static IReadOnlyList<string> RecordTypes { get; } =
        ["AUTO", "A", "AAAA", "PTR", "MX", "NS", "TXT", "CNAME", "SOA"];

    public string Host
    {
        get => _host;
        set { SetField(ref _host, value); CommandManager.InvalidateRequerySuggested(); }
    }

    public string RecordType
    {
        get => _recordType;
        set => SetField(ref _recordType, value);
    }

    /// <summary>Optional: override DNS server (leave empty to use system default).</summary>
    public string DnsServer
    {
        get => _dnsServer;
        set => SetField(ref _dnsServer, value);
    }

    public string Output
    {
        get => _output;
        set => SetField(ref _output, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set { SetField(ref _isRunning, value); CommandManager.InvalidateRequerySuggested(); }
    }

    public RelayCommand LookupCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ClearCommand  { get; }

    public DnsLookupViewModel(ObservableCollection<PingEntryViewModel> pingEntries)
    {
        _pingEntries  = pingEntries;
        LookupCommand = new RelayCommand(OnLookup, () => !string.IsNullOrWhiteSpace(Host) && !IsRunning);
        CancelCommand = new RelayCommand(OnCancel, () => IsRunning);
        ClearCommand  = new RelayCommand(() => Output = "");

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

    private void OnLookup()
    {
        var host = Host.Trim();
        if (string.IsNullOrEmpty(host)) return;

        _cts      = new CancellationTokenSource();
        IsRunning = true;

        var effectiveType = RecordType == "AUTO"
            ? (IPAddress.TryParse(host, out _) ? "PTR" : "A")
            : RecordType;

        Output  = $"DNS Lookup: {host}  [type={effectiveType}]";
        if (!string.IsNullOrWhiteSpace(DnsServer)) Output += $"  [server={DnsServer.Trim()}]";
        Output += "\r\n\r\n";

        Task.Run(() => RunLookup(host, effectiveType, DnsServer.Trim(), _cts.Token));
    }

    private void RunLookup(string host, string recordType, string dnsServer, CancellationToken ct)
    {
        var args = string.IsNullOrEmpty(dnsServer)
            ? $"-type={recordType} \"{host}\""
            : $"-type={recordType} \"{host}\" {dnsServer}";

        var psi = new ProcessStartInfo("nslookup", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        try
        {
            using var process = Process.Start(psi)!;

            while (!process.StandardOutput.EndOfStream)
            {
                if (ct.IsCancellationRequested) { process.Kill(); Dispatch(() => Output += "\r\n[Cancelled]\r\n"); return; }
                var line = process.StandardOutput.ReadLine();
                if (line is not null) Dispatch(() => Output += line + "\r\n");
            }

            var err = process.StandardError.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(err))
                Dispatch(() => Output += "\r\n" + err.Trim() + "\r\n");

            Dispatch(() =>
            {
                if (!History.Contains(host))
                {
                    History.Insert(0, host);
                    if (History.Count > 50) History.RemoveAt(History.Count - 1);
                    RebuildAllHosts();
                    SaveHistory();
                }
            });
        }
        catch (Exception ex) { Dispatch(() => Output += $"\r\nError: {ex.Message}\r\n"); }
        finally  { Dispatch(() => IsRunning = false); }
    }

    private void OnCancel() => _cts?.Cancel();

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
        catch (Exception ex) { Debug.WriteLine($"[DnsVM] Load: {ex.Message}"); }
    }

    private void SaveHistory()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(HistoryFile, JsonSerializer.Serialize(History.ToList(), JsonOpts));
        }
        catch (Exception ex) { Debug.WriteLine($"[DnsVM] Save: {ex.Message}"); }
    }

    private static void Dispatch(Action a) => Application.Current.Dispatcher.Invoke(a);
}
