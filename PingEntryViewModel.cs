using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Input;
using LittlePinger.Enums;

namespace LittlePinger.ViewModels;

/// <summary>
/// Represents a single ping target. Owns the async ping loop, live statistics,
/// and configuration (IP, interval, timeout).
/// Implements <see cref="IDisposable"/>: always dispose or call <see cref="Stop"/> before
/// discarding an instance to cancel any in-flight ping loop.
/// </summary>
public class PingEntryViewModel : ViewModelBase, IDisposable
{
    private string _name = string.Empty;
    private string _ipAddress = string.Empty;
    private string _netName = string.Empty;
    private int _interval = 1000;
    private int _timeout = 1000;
    private bool _isRunning;
    private PingEntryStatus _status = PingEntryStatus.Unknown;
    private long _lastRtt = -1;
    private int _sentCount;
    private int _successCount;
    private int _failCount;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <summary>User-defined display name. Falls back to <see cref="IpAddress"/> when left blank.</summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>Target IP address or hostname to ping.</summary>
    public string IpAddress
    {
        get => _ipAddress;
        set => SetField(ref _ipAddress, value);
    }

    /// <summary>Network name resolved via reverse-DNS (or forward-DNS if host is a name). Empty while unresolved.</summary>
    public string NetName
    {
        get => _netName;
        private set => SetField(ref _netName, value);
    }

    /// <summary>Milliseconds to wait between successive ping attempts.</summary>
    public int Interval
    {
        get => _interval;
        set => SetField(ref _interval, value);
    }

    /// <summary>Maximum milliseconds to wait for a reply before marking the attempt as timed out.</summary>
    public int Timeout
    {
        get => _timeout;
        set => SetField(ref _timeout, value);
    }

    /// <summary>Whether the ping loop is currently active.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            SetField(ref _isRunning, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Result of the most recent completed ping attempt.</summary>
    public PingEntryStatus Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>
    /// Round-trip time in milliseconds of the last successful reply.
    /// Returns <c>-1</c> when no successful reply has been received since the last reset.
    /// </summary>
    public long LastRtt
    {
        get => _lastRtt;
        private set
        {
            if (SetField(ref _lastRtt, value))
                OnPropertyChanged(nameof(LastRttDisplay));
        }
    }

    /// <summary>Human-readable RTT: <c>"X ms"</c> or <c>"—"</c> when unavailable.</summary>
    public string LastRttDisplay => _lastRtt >= 0 ? $"{_lastRtt} ms" : "—";

    /// <summary>Total ping attempts dispatched since the last <see cref="ResetStats"/> call.</summary>
    public int SentCount
    {
        get => _sentCount;
        private set => SetField(ref _sentCount, value);
    }

    /// <summary>Ping attempts that received a successful ICMP reply.</summary>
    public int SuccessCount
    {
        get => _successCount;
        private set => SetField(ref _successCount, value);
    }

    /// <summary>Ping attempts that timed out or resulted in an error.</summary>
    public int FailCount
    {
        get => _failCount;
        private set => SetField(ref _failCount, value);
    }

    /// <summary>Starts pinging when stopped, stops when running.</summary>
    public ICommand ToggleCommand { get; }

    public PingEntryViewModel()
    {
        ToggleCommand = new RelayCommand(Toggle);
    }

    /// <summary>Toggles the ping loop: starts if stopped, stops if running.</summary>
    private void Toggle()
    {
        if (IsRunning) Stop();
        else Start();
    }

    /// <summary>
    /// Starts the ping loop. No-op if already running or disposed.
    /// Cancels and disposes any previous loop's <see cref="CancellationTokenSource"/> before
    /// creating a fresh one, ensuring only one loop is active at a time.
    /// </summary>
    public void Start()
    {
        if (_disposed || IsRunning) return;

        var old = _cts;
        _cts = new CancellationTokenSource();
        old?.Cancel();
        old?.Dispose();

        IsRunning = true;
        _ = RunPingLoopAsync(_cts.Token);
        _ = ResolveNetNameAsync();
    }

    /// <summary>
    /// Stops the ping loop. Sets <see cref="IsRunning"/> to <c>false</c> before signalling
    /// cancellation so that any Dispatcher work already queued by the loop will observe the
    /// updated flag and skip its UI update.
    /// The in-flight <see cref="Ping.SendPingAsync"/> call may still complete but will not
    /// update statistics.
    /// </summary>
    public void Stop()
    {
        IsRunning = false; // set before Cancel so queued Dispatcher lambdas see the correct state
        _cts?.Cancel();
    }

    /// <summary>
    /// Resets all ping statistics and sets <see cref="Status"/> back to
    /// <see cref="PingEntryStatus.Unknown"/>. Safe to call while pinging.
    /// </summary>
    public void ResetStats()
    {
        SentCount = 0;
        SuccessCount = 0;
        FailCount = 0;
        LastRtt = -1;
        Status = PingEntryStatus.Unknown;
    }

    /// <summary>
    /// Core ping loop: repeatedly calls <see cref="DoPingAsync"/> then waits
    /// <see cref="Interval"/> ms, until <paramref name="token"/> is cancelled.
    /// </summary>
    private async Task RunPingLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await DoPingAsync(token);
                if (token.IsCancellationRequested) break;
                await Task.Delay(Interval, token);
            }
        }
        catch (OperationCanceledException) { /* expected when Stop() is called */ }
    }

    /// <summary>
    /// Resolves the network name for the current <see cref="IpAddress"/> via DNS
    /// and sets <see cref="NetName"/> if a different name is found.
    /// </summary>
    private async Task ResolveNetNameAsync()
    {
        if (string.IsNullOrWhiteSpace(IpAddress)) return;
        try
        {
            var entry = await Dns.GetHostEntryAsync(IpAddress);
            var resolved = entry.HostName;
            if (!string.IsNullOrWhiteSpace(resolved) &&
                !string.Equals(resolved, IpAddress, StringComparison.OrdinalIgnoreCase))
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null)
                    await dispatcher.InvokeAsync(() => NetName = resolved);
            }
        }
        catch { /* DNS resolution not always available */ }
    }

    /// <summary>
    /// Sends a single ICMP echo request and updates <see cref="Status"/>, <see cref="LastRtt"/>,
    /// and counters on the UI thread via the WPF Dispatcher.
    /// Silently skips the UI update if <paramref name="token"/> was cancelled before the
    /// Dispatcher callback executes.
    /// </summary>
    private async Task DoPingAsync(CancellationToken token)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(IpAddress, Timeout);
            if (token.IsCancellationRequested) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;

            await dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                SentCount++;
                if (reply.Status == IPStatus.Success)
                {
                    SuccessCount++;
                    LastRtt = reply.RoundtripTime;
                    Status = PingEntryStatus.Success;
                }
                else if (reply.Status == IPStatus.TimedOut)
                {
                    FailCount++;
                    LastRtt = -1;
                    Status = PingEntryStatus.Timeout;
                }
                else
                {
                    FailCount++;
                    LastRtt = -1;
                    Status = PingEntryStatus.Error;
                }
            });
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;

            await dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                SentCount++;
                FailCount++;
                LastRtt = -1;
                Status = PingEntryStatus.Error;
            });
        }
    }

    /// <summary>
    /// Stops the ping loop, cancels the <see cref="CancellationTokenSource"/>, and releases it.
    /// Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts?.Dispose();
        _cts = null;
    }
}
