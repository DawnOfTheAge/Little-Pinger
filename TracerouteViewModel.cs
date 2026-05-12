using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>
/// View-model for the Traceroute tab. Streams tracert output line-by-line and
/// persists a history of traced addresses to disk.
/// </summary>
public class TracerouteViewModel : ViewModelBase
{
    private static readonly string DataDir     = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string HistoryFile = Path.Combine(DataDir, "traceroute_history.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly ObservableCollection<PingEntryViewModel> _pingEntries;
    private CancellationTokenSource? _cts;

    private string _selectedAddress = "";
    private string _output          = "";
    private bool   _isRunning;

    /// <summary>Addresses that have been successfully traced and were saved to disk.</summary>
    public ObservableCollection<string> History { get; } = new();

    /// <summary>Combined list for the ComboBox: history addresses + all current ping tab IPs.</summary>
    public ObservableCollection<string> AllAddresses { get; } = new();

    public string SelectedAddress
    {
        get => _selectedAddress;
        set { SetField(ref _selectedAddress, value); CommandManager.InvalidateRequerySuggested(); }
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

    public RelayCommand RunCommand         { get; }
    public RelayCommand CancelCommand      { get; }
    public RelayCommand ClearOutputCommand { get; }

    public TracerouteViewModel(ObservableCollection<PingEntryViewModel> pingEntries)
    {
        _pingEntries = pingEntries;

        RunCommand         = new RelayCommand(OnRun,    () => !string.IsNullOrWhiteSpace(SelectedAddress) && !IsRunning);
        CancelCommand      = new RelayCommand(OnCancel, () => IsRunning);
        ClearOutputCommand = new RelayCommand(() => Output = "");

        pingEntries.CollectionChanged += (_, _) => RebuildAllAddresses();
        LoadHistory();
        RebuildAllAddresses();
    }

    // ── Address list ─────────────────────────────────────────────────────────

    private void RebuildAllAddresses()
    {
        AllAddresses.Clear();
        foreach (var addr in History)
            AllAddresses.Add(addr);
        foreach (var entry in _pingEntries)
            if (!string.IsNullOrWhiteSpace(entry.IpAddress) && !AllAddresses.Contains(entry.IpAddress))
                AllAddresses.Add(entry.IpAddress);
    }

    // ── Command handlers ─────────────────────────────────────────────────────

    private void OnRun()
    {
        var address = SelectedAddress.Trim();
        if (string.IsNullOrEmpty(address)) return;

        _cts      = new CancellationTokenSource();
        IsRunning = true;
        Output    = $"Tracing route to {address} ...\r\n\r\n";

        Task.Run(() => RunTraceroute(address, _cts.Token));
    }

    private void RunTraceroute(string address, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("tracert", $"\"{address}\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        try
        {
            using var process = Process.Start(psi)!;

            while (!process.StandardOutput.EndOfStream)
            {
                if (ct.IsCancellationRequested)
                {
                    process.Kill();
                    Dispatch(() => Output += "\r\n[Cancelled]\r\n");
                    return;
                }
                var line = process.StandardOutput.ReadLine();
                if (line is not null)
                    Dispatch(() => Output += line + "\r\n");
            }

            // Completed normally — save address to history
            Dispatch(() =>
            {
                if (!History.Contains(address))
                {
                    History.Insert(0, address);
                    RebuildAllAddresses();
                    SaveHistory();
                }
            });
        }
        catch (Exception ex)
        {
            Dispatch(() => Output += $"\r\nError: {ex.Message}\r\n");
        }
        finally
        {
            Dispatch(() => IsRunning = false);
        }
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
            foreach (var addr in list)
                History.Add(addr);
        }
        catch (Exception ex) { Debug.WriteLine($"[TracerouteVM] Load failed: {ex.Message}"); }
    }

    private void SaveHistory()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(HistoryFile, JsonSerializer.Serialize(History.ToList(), JsonOpts));
        }
        catch (Exception ex) { Debug.WriteLine($"[TracerouteVM] Save failed: {ex.Message}"); }
    }

    private static void Dispatch(Action action) =>
        Application.Current.Dispatcher.Invoke(action);
}
