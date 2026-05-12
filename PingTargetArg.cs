namespace LittlePinger.ViewModels;

/// <summary>
/// Bundles an IP/host address together with a display name so context-menu
/// "Add to Ping List" commands can populate both fields in one binding.
/// </summary>
public record PingTargetArg(string Ip, string Name);
