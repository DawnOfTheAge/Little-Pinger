using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace LittlePinger.ViewModels;

/// <summary>
/// View-model for the Wi-Fi Signal Strength Monitor sub-tab.
/// Polls <c>netsh wlan show interfaces</c> on a <see cref="DispatcherTimer"/> and
/// accumulates signal-percentage samples for graph rendering in code-behind.
/// </summary>
public class WifiMonitorViewModel : ViewModelBase
{
    private const int MaxSamples = 300;

    private readonly DispatcherTimer _timer;

    private bool   _isRunning;
    private int    _intervalSec  = 2;
    private int    _signalPct;
    private string _ssid         = "—";
    private string _bssid        = "—";
    private string _radioType    = "—";
    private string _channel      = "—";
    private string _rxMbps       = "—";
    private string _txMbps       = "—";
    private string _auth         = "—";
    private string _statusText   = "Not monitoring";
    private string _noWifiMsg    = "";

    /// <summary>
    /// Signal percentage samples (0–100). <c>null</c> means the interface was not
    /// available at that sample point. Used by code-behind for graph rendering.
    /// </summary>
    public ObservableCollection<int?> SignalSamples { get; } = new();

    public bool   IsRunning   { get => _isRunning;   set { SetField(ref _isRunning, value); CommandManager.InvalidateRequerySuggested(); } }
    public int    IntervalSec { get => _intervalSec; set { SetField(ref _intervalSec, value); _timer.Interval = TimeSpan.FromSeconds(Math.Max(1, value)); } }
    public int    SignalPct   { get => _signalPct;   set => SetField(ref _signalPct, value); }
    public string Ssid        { get => _ssid;        set => SetField(ref _ssid, value); }
    public string Bssid       { get => _bssid;       set => SetField(ref _bssid, value); }
    public string RadioType   { get => _radioType;   set => SetField(ref _radioType, value); }
    public string Channel     { get => _channel;     set => SetField(ref _channel, value); }
    public string RxMbps      { get => _rxMbps;      set => SetField(ref _rxMbps, value); }
    public string TxMbps      { get => _txMbps;      set => SetField(ref _txMbps, value); }
    public string Auth        { get => _auth;        set => SetField(ref _auth, value); }
    public string StatusText  { get => _statusText;  set => SetField(ref _statusText, value); }
    public string NoWifiMsg   { get => _noWifiMsg;   set => SetField(ref _noWifiMsg, value); }

    public string SignalBar => _signalPct switch
    {
        >= 80 => "▓▓▓▓▓ Excellent",
        >= 60 => "▓▓▓▓░ Good",
        >= 40 => "▓▓▓░░ Fair",
        >= 20 => "▓▓░░░ Weak",
        _     => "▓░░░░ Very Weak"
    };

    public RelayCommand StartCommand   { get; }
    public RelayCommand StopCommand    { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ClearCommand   { get; }

    public WifiMonitorViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_intervalSec) };
        _timer.Tick += async (_, _) => await PollAsync();

        StartCommand   = new RelayCommand(() => { IsRunning = true;  _timer.Start(); _ = PollAsync(); }, () => !IsRunning);
        StopCommand    = new RelayCommand(() => { IsRunning = false; _timer.Stop();                   }, () => IsRunning);
        RefreshCommand = new RelayCommand(() => _ = PollAsync());
        ClearCommand   = new RelayCommand(() => { SignalSamples.Clear(); StatusText = "Cleared"; });

        _ = PollAsync(); // initial read
    }

    private async Task PollAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                CreateNoWindow         = true
            };

            var output = await Task.Run(() =>
            {
                using var proc = Process.Start(psi)!;
                var result = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                return result;
            });

            // output captured on background thread; parse and update on UI thread
            var fields = ParseNetsh(output);

            if (!fields.TryGetValue("SSID", out var ssid) || string.IsNullOrWhiteSpace(ssid))
            {
                NoWifiMsg = "No Wi-Fi interface connected.";
                if (SignalSamples.Count >= MaxSamples) SignalSamples.RemoveAt(0);
                SignalSamples.Add(null);
                StatusText = "No Wi-Fi connection";
                return;
            }

            NoWifiMsg = "";
            Ssid      = ssid;
            Bssid     = GetValueOrDefault(fields, "BSSID", "—");
            RadioType = GetValueOrDefault(fields, "Radio type", "—");
            Channel   = GetValueOrDefault(fields, "Channel", "—");
            RxMbps    = GetValueOrDefault(fields, "Receive rate (Mbps)", "—");
            TxMbps    = GetValueOrDefault(fields, "Transmit rate (Mbps)", "—");
            Auth      = GetValueOrDefault(fields, "Authentication", "—");

            if (fields.TryGetValue("Signal", out var sigStr))
            {
                var m = Regex.Match(sigStr, @"(\d+)");
                if (m.Success) SignalPct = int.Parse(m.Groups[1].Value);
            }

            OnPropertyChanged(nameof(SignalBar));

            if (SignalSamples.Count >= MaxSamples) SignalSamples.RemoveAt(0);
            SignalSamples.Add(SignalPct);

            StatusText = IsRunning
                ? $"Monitoring  ·  {Ssid}  ·  Ch {Channel}  ·  {SignalPct}%"
                : $"{Ssid}  ·  Ch {Channel}  ·  {SignalPct}%";
        }
        catch (Exception ex)
        {
            NoWifiMsg = $"Error reading Wi-Fi info: {ex.Message}";
        }
    }

    private static Dictionary<string, string> ParseNetsh(string output)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd();
            var idx  = line.IndexOf(':');
            if (idx < 1) continue;
            var key = line.Substring(0, idx).Trim();
            var val = line.Substring(idx + 1).Trim();
            if (!string.IsNullOrEmpty(key)) dict[key] = val;
        }
        return dict;
    }

    private static string GetValueOrDefault(Dictionary<string, string> dict, string key, string fallback)
        => dict.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : fallback;
}
