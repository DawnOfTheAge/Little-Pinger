using System.Collections.ObjectModel;

namespace LittlePinger.ViewModels;

/// <summary>
/// Container view-model for the "Monitoring &amp; Analysis" tab.
/// Owns the four sub-tool view-models so XAML can bind through
/// <c>MonitoringVM.ContinuousPingVM.*</c> etc.
/// </summary>
public class MonitoringViewModel
{
    public ContinuousPingViewModel          ContinuousPingVM   { get; }
    public NetworkInterfaceMonitorViewModel InterfaceMonitorVM { get; }
    public ArpTableViewModel               ArpTableVM         { get; }
    public RouteTableViewModel             RouteTableVM       { get; }

    public MonitoringViewModel(ObservableCollection<PingEntryViewModel> pingEntries)
    {
        ContinuousPingVM   = new ContinuousPingViewModel(pingEntries);
        InterfaceMonitorVM = new NetworkInterfaceMonitorViewModel();
        ArpTableVM         = new ArpTableViewModel(pingEntries);
        RouteTableVM       = new RouteTableViewModel();
    }
}
