using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LittlePinger.ViewModels;

/// <summary>
/// View-model for the Network Info tab. Reads all non-loopback network interfaces
/// and exposes them as <see cref="NetworkInterfaceInfo"/> objects.
/// </summary>
public class IPConfigViewModel : ViewModelBase
{
    public ObservableCollection<NetworkInterfaceInfo> Interfaces { get; } = new();

    public RelayCommand RefreshCommand { get; }

    public IPConfigViewModel()
    {
        RefreshCommand = new RelayCommand(Refresh);
        Refresh();
    }

    private void Refresh()
    {
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
    }
}
