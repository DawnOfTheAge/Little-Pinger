using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>
/// View-model for the Network Info tab. Reads all non-loopback network interfaces
/// and exposes them as <see cref="NetworkInterfaceInfo"/> objects. Also provides
/// adapter management commands (enable, disable, properties, delete).
/// </summary>
public class IPConfigViewModel : ViewModelBase
{
    private NetworkInterfaceInfo? _selectedInterface;
    private string _actionStatus = "Select an interface to manage it.";

    public ObservableCollection<NetworkInterfaceInfo> Interfaces { get; } = new();

    public NetworkInterfaceInfo? SelectedInterface
    {
        get => _selectedInterface;
        set { SetField(ref _selectedInterface, value); CommandManager.InvalidateRequerySuggested(); }
    }

    /// <summary>Status feedback for adapter management operations.</summary>
    public string ActionStatus { get => _actionStatus; set => SetField(ref _actionStatus, value); }

    public RelayCommand RefreshCommand    { get; }
    public RelayCommand EnableCommand     { get; }
    public RelayCommand DisableCommand    { get; }
    public RelayCommand ShowStatusCommand { get; }
    public RelayCommand PropertiesCommand { get; }
    public RelayCommand DeleteCommand     { get; }

    public IPConfigViewModel()
    {
        RefreshCommand    = new RelayCommand(Refresh);
        EnableCommand     = new RelayCommand(
            () => _ = RunAdapterCommandAsync("Enable-NetAdapter",  SelectedInterface!.Name),
            () => SelectedInterface is not null);
        DisableCommand    = new RelayCommand(
            () => _ = RunAdapterCommandAsync("Disable-NetAdapter", SelectedInterface!.Name),
            () => SelectedInterface is not null);
        ShowStatusCommand = new RelayCommand(ShowStatus,       () => SelectedInterface is not null);
        PropertiesCommand = new RelayCommand(OpenProperties,   () => SelectedInterface is not null);
        DeleteCommand     = new RelayCommand(ConfirmAndDelete, () => SelectedInterface is not null);
        Refresh();
    }

    private void Refresh()
    {
        var previousName = SelectedInterface?.Name;
        Interfaces.Clear();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            var props = nic.GetIPProperties();

            var ipv4 = props.UnicastAddresses
                           .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

            var ipv6 = props.UnicastAddresses
                           .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6
                                             && !a.Address.IsIPv6LinkLocal);

            var gateway = props.GatewayAddresses
                              .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);

            var dns = string.Join(", ", props.DnsAddresses
                          .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                          .Select(d => d.ToString()));

            var speed = nic.Speed switch
            {
                >= 1_000_000_000 => $"{nic.Speed / 1_000_000_000} Gbps",
                > 0              => $"{nic.Speed / 1_000_000} Mbps",
                _                => "N/A"
            };

            var macBytes = nic.GetPhysicalAddress().GetAddressBytes();
            var mac      = macBytes.Length > 0
                ? string.Join(":", macBytes.Select(b => b.ToString("X2")))
                : "";

            Interfaces.Add(new NetworkInterfaceInfo
            {
                Name        = nic.Name,
                Description = nic.Description,
                Type        = nic.NetworkInterfaceType.ToString(),
                Status      = nic.OperationalStatus.ToString(),
                MacAddress  = mac,
                IPv4Address = ipv4?.Address.ToString() ?? "",
                SubnetMask  = ipv4?.IPv4Mask.ToString() ?? "",
                Gateway     = gateway?.Address.ToString() ?? "",
                IPv6Address = ipv6?.Address.ToString() ?? "",
                DnsServers  = dns,
                Speed       = speed
            });
        }

        // Restore selection if the adapter still exists
        if (previousName is not null)
            SelectedInterface = Interfaces.FirstOrDefault(i => i.Name == previousName);
    }

    // ── Adapter management ────────────────────────────────────────────────────

    private async Task RunAdapterCommandAsync(string psCommand, string adapterName)
    {
        try
        {
            ActionStatus = $"Running {psCommand} on '{adapterName}'… (a UAC prompt may appear)";
            var psi = new ProcessStartInfo("powershell")
            {
                Arguments       = $"-NoProfile -Command \"{psCommand} -Name '{adapterName}' -Confirm:$false\"",
                UseShellExecute = true,
                Verb            = "runas",
                WindowStyle     = ProcessWindowStyle.Hidden
            };
            var proc = Process.Start(psi);
            if (proc != null)
                await Task.Run(() => proc.WaitForExit(15_000));
            await Task.Delay(800); // let the OS settle
            Refresh();
            ActionStatus = $"'{adapterName}': operation complete.";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            ActionStatus = "Cancelled — administrator access is required for this operation.";
        }
        catch (Exception ex)
        {
            ActionStatus = $"Error: {ex.Message}";
        }
    }

    private void ShowStatus()
    {
        var i = SelectedInterface!;
        MessageBox.Show(
            $"Name:        {i.Name}\n" +
            $"Status:      {i.Status}\n" +
            $"Type:        {i.Type}\n" +
            $"Description: {i.Description}\n\n" +
            $"IPv4:        {(string.IsNullOrEmpty(i.IPv4Address) ? "—" : i.IPv4Address)}\n" +
            $"Subnet Mask: {(string.IsNullOrEmpty(i.SubnetMask)  ? "—" : i.SubnetMask)}\n" +
            $"Gateway:     {(string.IsNullOrEmpty(i.Gateway)     ? "—" : i.Gateway)}\n" +
            $"DNS Servers: {(string.IsNullOrEmpty(i.DnsServers)  ? "—" : i.DnsServers)}\n" +
            $"IPv6:        {(string.IsNullOrEmpty(i.IPv6Address) ? "—" : i.IPv6Address)}\n\n" +
            $"MAC Address: {(string.IsNullOrEmpty(i.MacAddress)  ? "—" : i.MacAddress)}\n" +
            $"Speed:       {i.Speed}",
            $"Adapter Status — {i.Name}",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OpenProperties()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ncpa.cpl") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ActionStatus = $"Could not open Network Connections: {ex.Message}";
        }
    }

    private void ConfirmAndDelete()
    {
        var name = SelectedInterface!.Name;
        var result = MessageBox.Show(
            $"Remove (uninstall) the network adapter '{name}'?\n\n" +
            "This will permanently uninstall the adapter from Windows.\n" +
            "A UAC prompt will appear. This action may be irreversible for physical adapters.",
            "Confirm Adapter Removal",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            _ = RunAdapterCommandAsync("Remove-NetAdapter", name);
    }
}
