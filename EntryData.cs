namespace LittlePinger;

/// <summary>
/// Plain data-transfer object used to serialise/deserialise ping entries to and from JSON.
/// Contains no runtime state — only the configuration values that must be persisted.
/// A parameterless constructor is required by <see cref="System.Text.Json.JsonSerializer"/>.
/// </summary>
public class EntryData
{
    /// <summary>Display name for the entry (defaults to the IP address when first added).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>IP address or hostname to ping.</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>Time between successive pings, in milliseconds.</summary>
    public int Interval { get; set; } = 1000;

    /// <summary>Maximum time to wait for a ping reply before reporting a timeout, in milliseconds.</summary>
    public int Timeout { get; set; } = 1000;
}
