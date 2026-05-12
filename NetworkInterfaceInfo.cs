namespace LittlePinger;

/// <summary>Plain model that holds a snapshot of one network adapter's configuration.</summary>
public class NetworkInterfaceInfo
{
    public string Name        { get; init; } = "";
    public string Description { get; init; } = "";
    public string Type        { get; init; } = "";
    public string Status      { get; init; } = "";
    public string MacAddress  { get; init; } = "";
    public string IPv4Address { get; init; } = "";
    public string SubnetMask  { get; init; } = "";
    public string Gateway     { get; init; } = "";
    public string IPv6Address { get; init; } = "";
    public string DnsServers  { get; init; } = "";
    public string Speed       { get; init; } = "";
}
