using System.Collections.ObjectModel;

namespace LittlePinger.ViewModels;

/// <summary>
/// Container view-model for the "Connectivity &amp; Reachability" tab.
/// Owns and exposes the three sub-tools so the XAML can bind through
/// <c>ConnectivityVM.HttpCheck.*</c>, <c>.SslInspector.*</c>, and <c>.Whois.*</c>.
/// </summary>
public class ConnectivityViewModel
{
    public HttpCheckViewModel   HttpCheck   { get; }
    public SslInspectorViewModel SslInspector { get; }
    public WhoisLookupViewModel  Whois        { get; }

    public ConnectivityViewModel(ObservableCollection<PingEntryViewModel> pingEntries)
    {
        HttpCheck    = new HttpCheckViewModel(pingEntries);
        SslInspector = new SslInspectorViewModel(pingEntries);
        Whois        = new WhoisLookupViewModel(pingEntries);
    }
}
