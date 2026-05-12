using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>
/// View-model for a single continuously-pinged host. Owns the async ping loop,
/// latency samples for graph rendering, and per-host statistics.
/// </summary>
public class PingHostViewModel : ViewModelBase
{
    private const int MaxSamples = 300;

    private CancellationTokenSource? _cts;

    private string _host       = "";
    private int    _intervalMs = 1000;
    private int    _timeoutMs  = 2000;
    private bool   _isRunning;
    private int    _sentCount;
    private int    _receivedCount;
    private double _minMs      = double.MaxValue;
    private double _maxMs;
    private double _avgMs;
    private double _lossPercent;
    private double _jitterMs;
    private string _statusLine = "Enter a host and press ▶ Start";

    /// <summary>Host suggestions populated by the container from the main ping list.</summary>
    public ObservableCollection<string>  AllHosts       { get; } = new();
    public ObservableCollection<double?> LatencySamples { get; } = new();

    public string Host
    {
        get => _host;
        set { SetField(ref _host, value); CommandManager.InvalidateRequerySuggested(); }
    }

    public int    IntervalMs    { get => _intervalMs;    set => SetField(ref _intervalMs, value); }
    public int    TimeoutMs     { get => _timeoutMs;     set => SetField(ref _timeoutMs, value); }
    public bool   IsRunning     { get => _isRunning;     set { SetField(ref _isRunning, value); CommandManager.InvalidateRequerySuggested(); } }
    public int    SentCount     { get => _sentCount;     set { SetField(ref _sentCount, value);     OnPropertyChanged(nameof(SentRecvDisplay)); } }
    public int    ReceivedCount { get => _receivedCount; set { SetField(ref _receivedCount, value); OnPropertyChanged(nameof(SentRecvDisplay)); } }
    public double MinMs         { get => _minMs;         set { SetField(ref _minMs, value); OnPropertyChanged(nameof(MinMsDisplay)); } }
    public double MaxMs         { get => _maxMs;         set { SetField(ref _maxMs, value); OnPropertyChanged(nameof(MaxMsDisplay)); } }
    public double AvgMs         { get => _avgMs;         set => SetField(ref _avgMs, value); }
    public double LossPercent   { get => _lossPercent;   set { SetField(ref _lossPercent, value); OnPropertyChanged(nameof(HasPacketLoss)); } }
    public double JitterMs      { get => _jitterMs;      set => SetField(ref _jitterMs, value); }
    public string StatusLine    { get => _statusLine;    set => SetField(ref _statusLine, value); }

    public string MinMsDisplay  => _minMs >= double.MaxValue ? "—" : $"{_minMs:F1}";
    public string MaxMsDisplay  => _sentCount == 0 ? "—" : $"{_maxMs:F1}";
    public bool   HasPacketLoss => _lossPercent > 0;
    public string SentRecvDisplay => $"{_sentCount} / {_receivedCount}";

    public RelayCommand  StartCommand  { get; }
    public RelayCommand  StopCommand   { get; }
    public RelayCommand  ClearCommand  { get; }
    /// <summary>Set by the container view-model.</summary>
    public RelayCommand? RemoveCommand { get; set; }

    public PingHostViewModel()
    {
        StartCommand = new RelayCommand(OnStart,  () => !string.IsNullOrWhiteSpace(Host) && !IsRunning);
        StopCommand  = new RelayCommand(OnStop,   () => IsRunning);
        ClearCommand = new RelayCommand(OnClear,  () => !IsRunning);
    }

    /// <summary>Stops the ping loop (called by the container before removing this host).</summary>
    internal void Stop() => OnStop();

    private void OnStart()
    {
        _cts      = new CancellationTokenSource();
        IsRunning = true;
        StatusLine = $"Pinging {Host}…";
        _ = Task.Run(() => RunLoopAsync(Host.Trim(), _cts.Token));
    }

    private async Task RunLoopAsync(string host, CancellationToken ct)
    {
        double? prevMs      = null;
        double  sumMs       = 0;
        double  sumJitter   = 0;
        int     jitterCount = 0;

        using var pinger = new Ping();

        while (!ct.IsCancellationRequested)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double? latencyMs = null;

            try
            {
                var reply = await pinger.SendPingAsync(host, TimeoutMs);
                sw.Stop();
                if (reply.Status == IPStatus.Success)
                    latencyMs = reply.RoundtripTime > 0
                        ? (double)reply.RoundtripTime
                        : sw.Elapsed.TotalMilliseconds;
            }
            catch { sw.Stop(); }

            Dispatch(() =>
            {
                SentCount++;
                if (latencyMs.HasValue)
                {
                    ReceivedCount++;
                    sumMs += latencyMs.Value;
                    if (latencyMs.Value < MinMs) MinMs = latencyMs.Value;
                    if (latencyMs.Value > MaxMs) MaxMs = latencyMs.Value;
                    AvgMs = sumMs / ReceivedCount;

                    if (prevMs.HasValue)
                    {
                        sumJitter   += Math.Abs(latencyMs.Value - prevMs.Value);
                        jitterCount++;
                        JitterMs = sumJitter / jitterCount;
                    }
                    prevMs = latencyMs.Value;
                }
                else { prevMs = null; }

                LossPercent = SentCount > 0 ? (1.0 - (double)ReceivedCount / SentCount) * 100.0 : 0;

                if (LatencySamples.Count >= MaxSamples) LatencySamples.RemoveAt(0);
                LatencySamples.Add(latencyMs);

                StatusLine = SentCount > 0
                    ? $"Sent: {SentCount}  ·  Recv: {ReceivedCount}  ·  Loss: {LossPercent:F1}%  ·  " +
                      $"Min: {MinMsDisplay} ms  ·  Avg: {AvgMs:F1} ms  ·  Max: {MaxMsDisplay} ms  ·  Jitter: {JitterMs:F1} ms"
                    : $"Pinging {host}…";
            });

            int elapsed = (int)sw.Elapsed.TotalMilliseconds;
            int delay   = Math.Max(0, IntervalMs - elapsed);
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
        }

        Dispatch(() =>
        {
            IsRunning  = false;
            StatusLine = $"Stopped  ·  Sent: {SentCount}  ·  Recv: {ReceivedCount}  ·  Loss: {LossPercent:F1}%";
        });
    }

    private void OnStop() => _cts?.Cancel();

    private void OnClear()
    {
        LatencySamples.Clear();
        SentCount = ReceivedCount = 0;
        MinMs = double.MaxValue;
        MaxMs = AvgMs = LossPercent = JitterMs = 0;
        StatusLine = "Cleared — enter a host and press ▶ Start";
    }

    private static void Dispatch(Action a) => Application.Current.Dispatcher.Invoke(a);
}
