namespace LittlePinger;

/// <summary>Represents a single port-scan result entry (open ports only).</summary>
public class PortResult
{
    public int    Port        { get; init; }
    public string ServiceName { get; init; } = "";
    public string Status      { get; init; } = "Open";
}
