using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>
/// View-model for the WHOIS Lookup tool.
/// Performs a two-step WHOIS query: first asks whois.iana.org for the authoritative
/// server, then queries that server for the actual registration data.
/// For IP addresses it queries whois.iana.org directly (IANA provides referrals
/// to the correct RIR — ARIN, RIPE, APNIC, etc.).
/// </summary>
public class WhoisLookupViewModel : ViewModelBase
{
    private static readonly string DataDir     = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string HistoryFile = Path.Combine(DataDir, "whois_history.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly ObservableCollection<PingEntryViewModel> _pingEntries;
    private CancellationTokenSource? _cts;

    private string _target    = "";
    private string _output    = "";
    private bool   _isRunning;

    public ObservableCollection<string> History  { get; } = new();
    public ObservableCollection<string> AllHosts { get; } = new();

    public string Target
    {
        get => _target;
        set { SetField(ref _target, value); CommandManager.InvalidateRequerySuggested(); }
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

    public WhoisLookupViewModel(ObservableCollection<PingEntryViewModel> pingEntries)
    {
        _pingEntries  = pingEntries;
        LookupCommand = new RelayCommand(OnLookup, () => !string.IsNullOrWhiteSpace(Target) && !IsRunning);
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
        var target = Target.Trim();
        if (string.IsNullOrEmpty(target)) return;

        _cts      = new CancellationTokenSource();
        IsRunning = true;
        Output    = $"WHOIS Lookup: {target}\r\n" + new string('─', 60) + "\r\n\r\n";

        Task.Run(() => RunLookupAsync(target, _cts.Token));
    }

    private async Task RunLookupAsync(string target, CancellationToken ct)
    {
        try
        {
            // ── Step 1: query IANA for the authoritative WHOIS server ─────────
            Dispatch(() => Output += "Querying IANA (whois.iana.org)…\r\n");
            string ianaResponse = await QueryWhoisAsync("whois.iana.org", target, ct);

            string? authServer = ParseReferServer(ianaResponse);

            if (authServer is null)
            {
                // IANA itself has the info (e.g., for some ccTLDs or directly resolves IPs)
                Dispatch(() => Output += ianaResponse + "\r\n");
            }
            else
            {
                // ── Step 2: query the authoritative server ────────────────────
                Dispatch(() => Output += $"Authoritative server: {authServer}\r\n\r\n" +
                                         new string('─', 60) + "\r\n\r\n");
                string result = await QueryWhoisAsync(authServer, target, ct);
                Dispatch(() => Output += result + "\r\n");
            }

            Dispatch(() =>
            {
                if (!History.Contains(target))
                {
                    History.Insert(0, target);
                    if (History.Count > 50) History.RemoveAt(History.Count - 1);
                    RebuildAllHosts();
                    SaveHistory();
                }
            });
        }
        catch (OperationCanceledException) { Dispatch(() => Output += "\r\n[Cancelled]\r\n"); }
        catch (Exception ex)              { Dispatch(() => Output += $"\r\nError: {ex.Message}\r\n"); }
        finally                           { Dispatch(() => IsRunning = false); }
    }

    private static async Task<string> QueryWhoisAsync(string server, string query, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(15));

        using var tcp = new TcpClient();
        var connectTask = tcp.ConnectAsync(server, 43);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15), linked.Token);
        var completed = await Task.WhenAny(connectTask, timeoutTask);
        if (completed != connectTask)
        {
            linked.Token.ThrowIfCancellationRequested();
            throw new TimeoutException("WHOIS connection timed out.");
        }
        tcp.ReceiveTimeout = 15_000;

        using var stream = tcp.GetStream();
        var queryBytes = Encoding.ASCII.GetBytes(query + "\r\n");
        await stream.WriteAsync(queryBytes, 0, queryBytes.Length, linked.Token);

        using var reader = new StreamReader(stream, Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, bufferSize: 65536, leaveOpen: true);
        var readTask = reader.ReadToEndAsync();
        completed = await Task.WhenAny(readTask, timeoutTask);
        if (completed != readTask)
        {
            linked.Token.ThrowIfCancellationRequested();
            throw new TimeoutException("WHOIS query timed out.");
        }
        return await readTask;
    }

    private static string? ParseReferServer(string response)
    {
        foreach (var raw in response.Split('\n'))
        {
            var line = raw.Trim();
            foreach (var prefix in new[] { "refer:", "whois:" })
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var val = line.Substring(prefix.Length).Trim();
                    if (!string.IsNullOrEmpty(val)) return val;
                }
        }
        return null;
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
        catch (Exception ex) { Debug.WriteLine($"[WhoisVM] Load: {ex.Message}"); }
    }

    private void SaveHistory()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(HistoryFile, JsonSerializer.Serialize(History.ToList(), JsonOpts));
        }
        catch (Exception ex) { Debug.WriteLine($"[WhoisVM] Save: {ex.Message}"); }
    }

    private static void Dispatch(Action a) => Application.Current.Dispatcher.Invoke(a);
}
