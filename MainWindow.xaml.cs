using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using LittlePinger.ViewModels;

namespace LittlePinger;

/// <summary>
/// Main application window. Acts as the composition root: creates <see cref="MainViewModel"/>,
/// wires dialog events, and starts pinging after the window is fully loaded.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        _vm.AddEntryRequested  += OnAddEntryRequested;
        _vm.EditEntryRequested += OnEditEntryRequested;

        // Auto-scroll traceroute output whenever new text is appended
        _vm.TracerouteVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TracerouteViewModel.Output))
                TracerouteScroll.ScrollToEnd();
        };

        // Auto-scroll DNS output
        _vm.DnsLookupVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DnsLookupViewModel.Output))
                DnsScroll.ScrollToEnd();
        };

        // Auto-scroll MTU output
        _vm.MtuDiscoveryVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MtuDiscoveryViewModel.Output))
                MtuScroll.ScrollToEnd();
        };

        // Auto-scroll WHOIS output
        _vm.ConnectivityVM.Whois.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WhoisLookupViewModel.Output))
                WhoisScroll.ScrollToEnd();
        };

        // Continuous Ping graph rendering is now handled by PingGraphControl instances
        // directly bound to each PingHostViewModel.LatencySamples — no code-behind wiring needed.

        // Wi-Fi signal graph: redraw on new sample
        _vm.WifiNetworkVM.WifiMonitorVM.SignalSamples.CollectionChanged += (_, _) => DrawWifiGraph();
        WifiGraphCanvas.SizeChanged += (_, _) => DrawWifiGraph();

        // Entries were loaded without starting; begin pinging once the window is ready.
        Loaded += (_, _) => _vm.StartAll();
    }

    /// <summary>
    /// Shows the Add Entry dialog. On confirmation, creates a new <see cref="PingEntryViewModel"/>,
    /// adds it to the collection, and starts its ping loop immediately.
    /// </summary>
    private void OnAddEntryRequested(object? sender, EventArgs e)
    {
        var dialog = new AddEditDialog(_vm.DefaultInterval, _vm.DefaultTimeout)
        {
            Owner = this,
            Title = "Add Ping Entry"
        };

        if (dialog.ShowDialog() != true) return;

        var entry = new PingEntryViewModel
        {
            Name      = dialog.EntryName,
            IpAddress = dialog.IpAddress,
            Interval  = dialog.Interval,
            Timeout   = dialog.Timeout
        };

        // New entries start pinging immediately by default
        _vm.AddEntry(entry, startImmediately: true);
    }

    /// <summary>
    /// Shows the Edit Entry dialog for <paramref name="entry"/>. Stops the ping loop while the
    /// dialog is open, applies changes and resets stats on confirmation, then restores the
    /// previous running state. Saves to disk on confirmation.
    /// </summary>
    private void OnEditEntryRequested(object? sender, PingEntryViewModel entry)
    {
        var wasRunning = entry.IsRunning;
        if (wasRunning) entry.Stop();

        var dialog = new AddEditDialog(entry.Name, entry.IpAddress, entry.Interval, entry.Timeout)
        {
            Owner = this,
            Title = "Edit Ping Entry"
        };

        if (dialog.ShowDialog() == true)
        {
            entry.Name      = dialog.EntryName;
            entry.IpAddress = dialog.IpAddress;
            entry.Interval  = dialog.Interval;
            entry.Timeout   = dialog.Timeout;
            entry.ResetStats();
            if (wasRunning) entry.Start();
            _vm.SaveEntries();
        }
        else if (wasRunning)
        {
            entry.Start(); // restore if user cancelled
        }
    }

    /// <summary>
    /// Opens the Edit dialog when the user double-clicks a DataGrid row.
    /// </summary>
    private void MainGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.SelectedEntry is not null && _vm.EditCommand.CanExecute(null))
            _vm.EditCommand.Execute(null);
    }

    // ── Graph rendering ───────────────────────────────────────────────────────/// <summary>Redraws the Wi-Fi signal polyline on <see cref="WifiGraphCanvas"/>.</summary>
    private void DrawWifiGraph()
    {
        var canvas  = WifiGraphCanvas;
        var line    = WifiGraphLine;
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        if (w < 2 || h < 2) { line.Points = new PointCollection(); return; }

        var samples = _vm.WifiNetworkVM.WifiMonitorVM.SignalSamples.ToList();
        if (samples.Count < 2) { line.Points = new PointCollection(); return; }

        int    n   = samples.Count;
        var    pts = new PointCollection();

        for (int i = 0; i < n; i++)
        {
            if (!samples[i].HasValue) continue;
            double x = w * i / (n - 1);
            double y = h - samples[i]!.Value / 100.0 * (h - 4) - 2;
            pts.Add(new System.Windows.Point(x, Math.Clamp(y, 0, h)));
        }
        line.Points = pts;
    }
}
