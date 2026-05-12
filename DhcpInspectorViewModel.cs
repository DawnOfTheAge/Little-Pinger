using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>DHCP lease information for one network adapter.</summary>
public record DhcpLeaseEntry(
    string  AdapterName,
    string  IPAddress,
    string  SubnetMask,
    string  DefaultGateway,
    string  DhcpServer,
    string  LeaseObtained,
    string  LeaseExpires,
    string  DnsServers,
    int     DaysLeft,
    bool    DhcpEnabled)
{
    /// <summary>Bundles IP + adapter name for the "Add to Ping List" context-menu command.</summary>
    public PingTargetArg PingTarget => new(IPAddress, string.IsNullOrWhiteSpace(AdapterName) ? IPAddress : AdapterName);
}

/// <summary>
/// View-model for the DHCP Lease Inspector sub-tab.
/// Parses <c>ipconfig /all</c> to surface DHCP lease information for each adapter.
/// </summary>
public class DhcpInspectorViewModel : ViewModelBase
{
    private bool   _isRunning;
    private string _statusText = "";

    public ObservableCollection<DhcpLeaseEntry> Leases   { get; } = new();
    public bool   IsRunning  { get => _isRunning;  set { SetField(ref _isRunning, value); CommandManager.InvalidateRequerySuggested(); } }
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    public RelayCommand RefreshCommand { get; }

    public DhcpInspectorViewModel()
    {
        RefreshCommand = new RelayCommand(OnRefresh, () => !IsRunning);
        OnRefresh();
    }

    private void OnRefresh()
    {
        IsRunning  = true;
        StatusText = "Reading DHCP leases…";

        _ = Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("ipconfig", "/all")
                {
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow         = true
                };
                using var proc = Process.Start(psi)!;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                var leases = ParseIpconfig(output);
                Dispatch(() =>
                {
                    Leases.Clear();
                    foreach (var l in leases) Leases.Add(l);
                    StatusText = $"{Leases.Count} adapter(s)  ·  {Leases.Count(l => l.DhcpEnabled)} with DHCP";
                });
            }
            catch (Exception ex) { Dispatch(() => StatusText = $"Error: {ex.Message}"); }
            finally               { Dispatch(() => IsRunning = false); }
        });
    }

    // ── Parser ────────────────────────────────────────────────────────────────

    private static List<DhcpLeaseEntry> ParseIpconfig(string output)
    {
        var result = new List<DhcpLeaseEntry>();

        // Split into adapter sections (lines that don't start with whitespace and end with ':')
        var sections = Regex.Split(output, @"(?m)^(?!\s).+:").Skip(1).ToList();
        var headers  = Regex.Matches(output, @"(?m)^(?!\s)(.+):").Cast<Match>()
                            .Select(m => m.Groups[1].Value.Trim()).ToList();

        for (int i = 0; i < Math.Min(sections.Count, headers.Count); i++)
        {
            var block = sections[i];
            string Get(string key)
            {
                var m = Regex.Match(block, $@"{Regex.Escape(key)}\s*[.:]+\s*(.+)", RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value.Trim().TrimEnd('.') : "";
            }

            var dhcpEnabled = Get("DHCP Enabled").Equals("Yes", StringComparison.OrdinalIgnoreCase);
            var ip          = Get("IPv4 Address").Replace("(Preferred)", "").Trim();
            if (string.IsNullOrWhiteSpace(ip)) ip = Get("IP Address").Replace("(Preferred)", "").Trim();

            // Skip non-relevant adapters (no IP, Tunnel, Loopback)
            if (string.IsNullOrWhiteSpace(ip)) continue;
            var header = headers[i];
            if (header.Contains("Loopback") || header.Contains("Tunnel") || header.Contains("isatap")) continue;

            string leaseObtained = Get("Lease Obtained");
            string leaseExpires  = Get("Lease Expires");
            int daysLeft = 0;

            if (!string.IsNullOrWhiteSpace(leaseExpires))
            {
                string[] fmts = { "dddd, MMMM dd, yyyy HH:mm:ss", "dddd, dd MMMM yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "M/d/yyyy h:mm:ss tt" };
                if (DateTime.TryParseExact(leaseExpires, fmts, null, System.Globalization.DateTimeStyles.None, out var exp))
                    daysLeft = (int)(exp - DateTime.Now).TotalDays;
            }

            // Collect all DNS server lines (may be multi-line)
            var dnsMatch = Regex.Matches(block, @"DNS Servers\s*[.:]+\s*(.+?)(?=\r?\n\s*\w|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var dns      = dnsMatch.Count > 0
                ? string.Join(", ", dnsMatch[0].Groups[1].Value.Split('\n').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)))
                : "";

            result.Add(new DhcpLeaseEntry(
                AdapterName:    header,
                IPAddress:      ip,
                SubnetMask:     Get("Subnet Mask"),
                DefaultGateway: Get("Default Gateway"),
                DhcpServer:     Get("DHCP Server"),
                LeaseObtained:  leaseObtained,
                LeaseExpires:   leaseExpires,
                DnsServers:     dns,
                DaysLeft:       daysLeft,
                DhcpEnabled:    dhcpEnabled));
        }

        return result;
    }

    private static void Dispatch(Action a) => Application.Current.Dispatcher.Invoke(a);
}
