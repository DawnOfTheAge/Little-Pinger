using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>
/// View-model for the Speed Test tab.
/// Runs three phases: ping latency, download, and (if supported) upload.
/// UI is updated live from a background thread via Dispatcher.Invoke.
/// </summary>
public class SpeedTestViewModel : ViewModelBase
{
    private record ServerConfig(string DownloadUrl, string UploadUrl, string PingHost);

    private static readonly Dictionary<string, ServerConfig> ServerConfigs = new()
    {
        ["Cloudflare"] = new(
            "https://speed.cloudflare.com/__down?bytes=52428800",   // 50 MB
            "https://speed.cloudflare.com/__up",
            "speed.cloudflare.com"),
        ["Tele2 (Europe)"] = new(
            "https://speedtest.tele2.net/25MB.zip",
            "",
            "speedtest.tele2.net"),
        ["OVH (Europe)"] = new(
            "https://proof.ovh.net/files/25Mb.dat",
            "",
            "proof.ovh.net"),
    };

    private CancellationTokenSource? _cts;

    private string _selectedServer  = "Cloudflare";
    private bool   _isRunning;
    private int    _progressPercent;
    private string _statusText      = "Select a server and click  ▶  Run";
    private double _pingMs;
    private double _jitterMs;
    private double _downloadMbps;
    private double _uploadMbps;
    private bool   _hasUpload;

    public IReadOnlyList<string> Servers { get; } = [.. ServerConfigs.Keys];

    public string SelectedServer  { get => _selectedServer;  set => SetField(ref _selectedServer, value); }
    public bool   IsRunning       { get => _isRunning;       set { SetField(ref _isRunning, value); CommandManager.InvalidateRequerySuggested(); } }
    public int    ProgressPercent { get => _progressPercent; set => SetField(ref _progressPercent, value); }
    public string StatusText      { get => _statusText;      set => SetField(ref _statusText, value); }
    public double PingMs          { get => _pingMs;          set => SetField(ref _pingMs, value); }
    public double JitterMs        { get => _jitterMs;        set => SetField(ref _jitterMs, value); }
    public double DownloadMbps    { get => _downloadMbps;    set => SetField(ref _downloadMbps, value); }
    public double UploadMbps      { get => _uploadMbps;      set => SetField(ref _uploadMbps, value); }
    public bool   HasUpload       { get => _hasUpload;       set => SetField(ref _hasUpload, value); }

    public RelayCommand StartCommand  { get; }
    public RelayCommand CancelCommand { get; }

    public SpeedTestViewModel()
    {
        StartCommand  = new RelayCommand(OnStart,  () => !IsRunning);
        CancelCommand = new RelayCommand(OnCancel, () => IsRunning);
    }

    // ── Command handlers ─────────────────────────────────────────────────────

    private void OnStart()
    {
        var server = SelectedServer;
        _cts = new CancellationTokenSource();

        IsRunning       = true;
        HasUpload       = false;
        PingMs          = 0;
        JitterMs        = 0;
        DownloadMbps    = 0;
        UploadMbps      = 0;
        ProgressPercent = 0;
        StatusText      = "Starting…";

        _ = Task.Run(() => RunTestAsync(server, _cts.Token));
    }

    private async Task RunTestAsync(string serverName, CancellationToken ct)
    {
        try
        {
            var cfg = ServerConfigs[serverName];

            // ── Phase 1: Latency (0–20 %) ────────────────────────────────
            Dispatch(() => { StatusText = $"Pinging {cfg.PingHost}…"; ProgressPercent = 0; });
            var (ping, jitter) = await MeasureLatencyAsync(cfg.PingHost, ct);
            Dispatch(() => { PingMs = ping; JitterMs = jitter; ProgressPercent = 20; StatusText = $"Ping: {ping:F1} ms  ·  Jitter: {jitter:F1} ms"; });

            if (ct.IsCancellationRequested) return;

            // ── Phase 2: Download (20–80 %) ──────────────────────────────
            Dispatch(() => StatusText = "Downloading…");
            double dlMbps = await MeasureDownloadAsync(cfg.DownloadUrl, ct, (mbps, frac) =>
                Dispatch(() => { DownloadMbps = mbps; ProgressPercent = 20 + (int)(frac * 60); }));
            Dispatch(() => { DownloadMbps = dlMbps; ProgressPercent = 80; StatusText = $"Download: {dlMbps:F1} Mbps"; });

            if (ct.IsCancellationRequested) return;

            // ── Phase 3: Upload (80–100 %, Cloudflare only) ──────────────
            double ulMbps = 0;
            if (!string.IsNullOrEmpty(cfg.UploadUrl))
            {
                Dispatch(() => StatusText = "Uploading…");
                ulMbps = await MeasureUploadAsync(cfg.UploadUrl, ct, (mbps, frac) =>
                    Dispatch(() => { UploadMbps = mbps; ProgressPercent = 80 + (int)(frac * 20); }));
                Dispatch(() => { UploadMbps = ulMbps; HasUpload = true; ProgressPercent = 100; });
            }
            else
            {
                Dispatch(() => ProgressPercent = 100);
            }

            Dispatch(() =>
            {
                var ul = HasUpload ? $"↑ {ulMbps:F1} Mbps  ·  " : "";
                StatusText = $"Complete  ·  {ul}↓ {dlMbps:F1} Mbps  ·  Ping {PingMs:F1} ms";
            });
        }
        catch (OperationCanceledException) { Dispatch(() => StatusText = "Cancelled"); }
        catch (Exception ex)              { Dispatch(() => StatusText = $"Error: {ex.Message}"); }
        finally                           { Dispatch(() => IsRunning = false); }
    }

    // ── Measurement helpers ───────────────────────────────────────────────────

    private static async Task<(double avgMs, double jitterMs)> MeasureLatencyAsync(string host, CancellationToken ct)
    {
        var times = new List<double>();
        using var ping = new Ping();
        for (int i = 0; i < 5; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(host, 2000);
                if (reply.Status == IPStatus.Success)
                    times.Add(reply.RoundtripTime);
            }
            catch { /* swallow individual failures */ }
            if (i < 4) await Task.Delay(100, ct);
        }
        if (times.Count == 0) return (0, 0);
        double avg    = times.Average();
        double jitter = times.Count > 1 ? times.Zip(times.Skip(1), (a, b) => Math.Abs(a - b)).Average() : 0;
        return (avg, jitter);
    }

    private static async Task<double> MeasureDownloadAsync(
        string url, CancellationToken ct, Action<double, double>? onProgress = null)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        http.Timeout = TimeSpan.FromSeconds(120);

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength ?? 0;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        var    sw         = Stopwatch.StartNew();
        var    lastUpdate = sw.Elapsed;
        long   total      = 0;
        var    buffer     = new byte[131072]; // 128 KB
        int    read;

        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            var now     = sw.Elapsed;
            if ((now - lastUpdate).TotalSeconds >= 0.25 && contentLength > 0)
            {
                onProgress?.Invoke(
                    total * 8.0 / 1_000_000 / sw.Elapsed.TotalSeconds,
                    (double)total / contentLength);
                lastUpdate = now;
            }
        }

        return total > 0 ? total * 8.0 / 1_000_000 / sw.Elapsed.TotalSeconds : 0;
    }

    private static async Task<double> MeasureUploadAsync(
        string url, CancellationToken ct, Action<double, double>? onProgress = null)
    {
        const int uploadBytes = 10 * 1024 * 1024; // 10 MB
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        http.Timeout = TimeSpan.FromSeconds(60);

        var data = new byte[uploadBytes];
        Random.Shared.NextBytes(data);

        onProgress?.Invoke(0, 0);
        var sw = Stopwatch.StartNew();
        await http.PostAsync(url, new ByteArrayContent(data), ct);
        double elapsed = sw.Elapsed.TotalSeconds;

        double mbps = uploadBytes * 8.0 / 1_000_000 / (elapsed > 0 ? elapsed : 1);
        onProgress?.Invoke(mbps, 1.0);
        return mbps;
    }

    private void OnCancel() => _cts?.Cancel();

    private static void Dispatch(Action a) => Application.Current.Dispatcher.Invoke(a);
}
