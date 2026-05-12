using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace LittlePinger.ViewModels;

/// <summary>
/// Manages which tabs and sub-tabs are visible. Persists the user's choices to
/// <c>Data/settings.json</c> automatically whenever any selection changes.
/// The Ping tab is always visible and is not represented here.
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private static readonly string DataDir      = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static readonly string SettingsFile = Path.Combine(DataDir, "settings.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>All configurable top-level tabs in display order (Ping excluded).</summary>
    public ObservableCollection<TabVisibilityItem> Tabs { get; } = new();

    // ── Outer tab properties ─────────────────────────────────────────────────
    public bool IsMonitoringVisible   => Tabs[0].IsVisible;
    public bool IsWifiVisible         => Tabs[1].IsVisible;
    public bool IsCoreNetworkVisible  => Tabs[2].IsVisible;
    public bool IsConnectivityVisible => Tabs[3].IsVisible;
    public bool IsReportingVisible    => Tabs[4].IsVisible;

    // ── Monitoring & Analysis sub-tabs ───────────────────────────────────────
    public bool IsContinuousPingVisible   => Tabs[0].Children[0].IsVisible;
    public bool IsInterfaceMonitorVisible => Tabs[0].Children[1].IsVisible;
    public bool IsArpTableVisible         => Tabs[0].Children[2].IsVisible;
    public bool IsRouteTableVisible       => Tabs[0].Children[3].IsVisible;

    // ── Wi-Fi & Local sub-tabs ───────────────────────────────────────────────
    public bool IsWifiSignalVisible  => Tabs[1].Children[0].IsVisible;
    public bool IsLanScannerVisible  => Tabs[1].Children[1].IsVisible;
    public bool IsDhcpLeasesVisible  => Tabs[1].Children[2].IsVisible;

    // ── Core Network Diagnostics sub-tabs ────────────────────────────────────
    public bool IsTracerouteVisible  => Tabs[2].Children[0].IsVisible;
    public bool IsNetworkInfoVisible => Tabs[2].Children[1].IsVisible;
    public bool IsDnsLookupVisible   => Tabs[2].Children[2].IsVisible;
    public bool IsPortScannerVisible => Tabs[2].Children[3].IsVisible;
    public bool IsNetstatVisible     => Tabs[2].Children[4].IsVisible;
    public bool IsSpeedTestVisible   => Tabs[2].Children[5].IsVisible;
    public bool IsMtuVisible         => Tabs[2].Children[6].IsVisible;

    // ── Connectivity sub-tabs ────────────────────────────────────────────────
    public bool IsHttpCheckVisible => Tabs[3].Children[0].IsVisible;
    public bool IsSslVisible       => Tabs[3].Children[1].IsVisible;
    public bool IsWhoisVisible     => Tabs[3].Children[2].IsVisible;

    // ── Reporting sub-tabs ───────────────────────────────────────────────────
    public bool IsExportVisible               => Tabs[4].Children[0].IsVisible;
    public bool IsScheduledTestsVisible       => Tabs[4].Children[1].IsVisible;
    public bool IsHistoricalComparisonVisible => Tabs[4].Children[2].IsVisible;

    public SettingsViewModel()
    {
        // Build the hierarchy — order must match TabItem order in MainWindow.xaml
        var monitoring = MakeParent("📊  Monitoring & Analysis", nameof(IsMonitoringVisible),
            ("📡  Continuous Ping",   nameof(IsContinuousPingVisible)),
            ("🖧  Interface Monitor", nameof(IsInterfaceMonitorVisible)),
            ("📋  ARP Table",         nameof(IsArpTableVisible)),
            ("🗺  Route Table",        nameof(IsRouteTableVisible)));

        var wifi = MakeParent("📶  Wi-Fi & Local", nameof(IsWifiVisible),
            ("📡  Wi-Fi Signal",  nameof(IsWifiSignalVisible)),
            ("🔍  LAN Scanner",   nameof(IsLanScannerVisible)),
            ("🏠  DHCP Leases",   nameof(IsDhcpLeasesVisible)));

        var core = MakeParent("🔧  Core Network Diagnostics", nameof(IsCoreNetworkVisible),
            ("🔍  Traceroute",   nameof(IsTracerouteVisible)),
            ("🌐  Network Info", nameof(IsNetworkInfoVisible)),
            ("🔎  DNS Lookup",   nameof(IsDnsLookupVisible)),
            ("🔌  Port Scanner", nameof(IsPortScannerVisible)),
            ("🖧  Netstat",      nameof(IsNetstatVisible)),
            ("📶  Speed Test",   nameof(IsSpeedTestVisible)),
            ("📏  MTU",          nameof(IsMtuVisible)));

        var connectivity = MakeParent("🔗  Connectivity", nameof(IsConnectivityVisible),
            ("🌐  HTTP Check", nameof(IsHttpCheckVisible)),
            ("🔐  SSL / TLS",  nameof(IsSslVisible)),
            ("📋  WHOIS",      nameof(IsWhoisVisible)));

        var reporting = MakeParent("📤  Reporting", nameof(IsReportingVisible),
            ("💾  Export",                 nameof(IsExportVisible)),
            ("⏱  Scheduled Tests",        nameof(IsScheduledTestsVisible)),
            ("📊  Historical Comparison",  nameof(IsHistoricalComparisonVisible)));

        Tabs.Add(monitoring);
        Tabs.Add(wifi);
        Tabs.Add(core);
        Tabs.Add(connectivity);
        Tabs.Add(reporting);

        Load();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a parent <see cref="TabVisibilityItem"/> with child items, wiring up
    /// PropertyChanged notifications to the named properties on this view-model.
    /// </summary>
    private TabVisibilityItem MakeParent(string name, string parentProp,
        params (string childName, string childProp)[] children)
    {
        var parent = new TabVisibilityItem { Name = name };
        Wire(parent, parentProp);

        foreach (var (childName, childProp) in children)
        {
            var child = new TabVisibilityItem { Name = childName };
            child.SetParent(parent);
            Wire(child, childProp);
            parent.Children.Add(child);
        }

        return parent;
    }

    private void Wire(TabVisibilityItem item, string propName)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TabVisibilityItem.IsVisible))
            {
                OnPropertyChanged(propName);
                Save();
            }
        };
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void Load()
    {
        if (!File.Exists(SettingsFile)) return;
        try
        {
            var json  = File.ReadAllText(SettingsFile);
            var saved = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
            if (saved is null) return;
            foreach (var parent in Tabs)
            {
                if (saved.TryGetValue(parent.Name, out var pv)) parent.IsVisible = pv;
                foreach (var child in parent.Children)
                    if (saved.TryGetValue(child.Name, out var cv)) child.IsVisible = cv;
            }
        }
        catch { /* ignore corrupt settings file */ }
    }

    internal void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var data = new Dictionary<string, bool>();
            foreach (var parent in Tabs)
            {
                data[parent.Name] = parent.IsVisible;
                foreach (var child in parent.Children)
                    data[child.Name] = child.IsVisible;
            }
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(data, JsonOpts));
        }
        catch { /* best-effort save */ }
    }
}
