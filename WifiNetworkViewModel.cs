using System.Collections.ObjectModel;

namespace LittlePinger.ViewModels;

/// <summary>
/// Container view-model for the "Wi-Fi &amp; Local Network" tab.
/// Owns Wi-Fi monitor, LAN scanner, and DHCP inspector sub-tool view-models.
/// </summary>
public class WifiNetworkViewModel
{
    public WifiMonitorViewModel    WifiMonitorVM { get; }
    public LanScannerViewModel     LanScannerVM  { get; }
    public DhcpInspectorViewModel  DhcpVM        { get; }

    public WifiNetworkViewModel()
    {
        WifiMonitorVM = new WifiMonitorViewModel();
        LanScannerVM  = new LanScannerViewModel();
        DhcpVM        = new DhcpInspectorViewModel();
    }
}
