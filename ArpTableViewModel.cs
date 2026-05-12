using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>One row in the ARP cache.</summary>
public class ArpEntry : ViewModelBase
{
    private string _name = "";

    public string IPAddress       { get; }
    public string PhysicalAddress { get; }
    public string Type            { get; }

    /// <summary>Hostname resolved via reverse DNS lookup; empty while resolving.</summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public ArpEntry(string ipAddress, string physicalAddress, string type)
    {
        IPAddress       = ipAddress;
        PhysicalAddress = physicalAddress;
        Type            = type;
    }

    /// <summary>Bundles IP + resolved hostname for the "Add to Ping List" context-menu command.</summary>
    public PingTargetArg PingTarget => new(IPAddress, string.IsNullOrWhiteSpace(Name) ? IPAddress : Name);
}

/// <summary>
/// View-model for the ARP Table Viewer. Runs <c>arp -a</c> and parses the output
/// into an <see cref="Entries"/> collection. Multicast and broadcast addresses are
/// filtered out automatically.
/// </summary>
public class ArpTableViewModel : ViewModelBase
{
    private bool   _isRunning;
    private string _statusText = "";

    public ObservableCollection<ArpEntry> Entries    { get; } = new();
    public bool   IsRunning  { get => _isRunning;  set { SetField(ref _isRunning, value); CommandManager.InvalidateRequerySuggested(); } }
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand ClearCommand   { get; }

    public ArpTableViewModel()
    {
        RefreshCommand = new RelayCommand(OnRefresh, () => !IsRunning);
        ClearCommand   = new RelayCommand(() => { Entries.Clear(); StatusText = ""; });
        OnRefresh();
    }

    private void OnRefresh()
    {
        IsRunning  = true;
        StatusText = "Loading ARP table…";

        _ = Task.Run(async () =>
        {
            try
            {
                var psi = new ProcessStartInfo("arp", "-a")
                {
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow         = true
                };
                using var proc = Process.Start(psi)!;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                var entries = ParseArp(output);
                Dispatch(() =>
                {
                    Entries.Clear();
                    foreach (var e in entries) Entries.Add(e);
                    StatusText = $"{Entries.Count} entries — resolving names…";
                });

                // Async reverse-DNS resolution for each entry
                var tasks = entries.Select(async entry =>
                {
                    try
                    {
                        var host = await Dns.GetHostEntryAsync(entry.IPAddress);
                        if (host.HostName != entry.IPAddress)
                            Dispatch(() => entry.Name = host.HostName);
                    }
                    catch { /* DNS resolution not always available */ }
                });
                await Task.WhenAll(tasks);
            }
            catch (Exception ex) { Dispatch(() => StatusText = $"Error: {ex.Message}"); }
            finally               { Dispatch(() => { IsRunning = false; StatusText = $"{Entries.Count} entries"; }); }
        });
    }

    private static List<ArpEntry> ParseArp(string output)
    {
        var list = new List<ArpEntry>();
        // Match lines where MAC is exactly 17 chars: xx-xx-xx-xx-xx-xx
        var rx = new Regex(@"^\s*([\d.]+)\s+([0-9a-fA-F-]{17})\s+(\w+)", RegexOptions.Multiline);
        foreach (Match m in rx.Matches(output))
        {
            var ip = m.Groups[1].Value;
            if (ip.StartsWith("224.") || ip.StartsWith("239.") || ip == "255.255.255.255") continue;
            list.Add(new ArpEntry(ip, m.Groups[2].Value, m.Groups[3].Value));
        }
        return list;
    }

    private static void Dispatch(Action a) => Application.Current.Dispatcher.Invoke(a);
}
