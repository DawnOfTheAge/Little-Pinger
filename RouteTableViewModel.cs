using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>One row in the IPv4 or IPv6 routing table.</summary>
public record RouteEntry(string Network, string Netmask, string Gateway, string Interface, string Metric)
{
    /// <summary>Bundles Gateway IP for the "Add to Ping List" context-menu command.</summary>
    public PingTargetArg PingTarget => new(Gateway, Gateway);
}

/// <summary>
/// View-model for the Route Table Viewer. Runs <c>route print</c> and parses both
/// the IPv4 and IPv6 sections into separate observable collections.
/// </summary>
public class RouteTableViewModel : ViewModelBase
{
    private bool   _isRunning;
    private string _statusText = "";

    public ObservableCollection<RouteEntry> IPv4Routes { get; } = new();
    public ObservableCollection<RouteEntry> IPv6Routes { get; } = new();

    public bool   IsRunning  { get => _isRunning;  set { SetField(ref _isRunning, value); CommandManager.InvalidateRequerySuggested(); } }
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    public RelayCommand RefreshCommand { get; }

    public RouteTableViewModel()
    {
        RefreshCommand = new RelayCommand(OnRefresh, () => !IsRunning);
        OnRefresh();
    }

    private void OnRefresh()
    {
        IsRunning  = true;
        StatusText = "Loading route table…";

        _ = Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("route", "print")
                {
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow         = true
                };
                using var proc = Process.Start(psi)!;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                var (v4, v6) = ParseRoutes(output);
                Dispatch(() =>
                {
                    IPv4Routes.Clear(); foreach (var r in v4) IPv4Routes.Add(r);
                    IPv6Routes.Clear(); foreach (var r in v6) IPv6Routes.Add(r);
                    StatusText = $"{IPv4Routes.Count} IPv4  ·  {IPv6Routes.Count} IPv6 routes";
                });
            }
            catch (Exception ex) { Dispatch(() => StatusText = $"Error: {ex.Message}"); }
            finally               { Dispatch(() => IsRunning = false); }
        });
    }

    private static (List<RouteEntry>, List<RouteEntry>) ParseRoutes(string output)
    {
        var v4 = new List<RouteEntry>();
        var v6 = new List<RouteEntry>();
        bool inV4 = false, inV6 = false, v4Active = false, v6Active = false;

        // IPv4: Network   Netmask   Gateway   Interface   Metric
        var v4rx = new Regex(@"^\s*([\d.]+)\s+([\d.]+)\s+([\d.a-zA-Z-]+)\s+([\d.]+)\s+(\d+)");
        // IPv6: If#  Metric  Network/Prefix  Gateway
        var v6rx = new Regex(@"^\s*(\d+)\s+(\d+)\s+([0-9a-fA-F:/%]+)\s+(.+)");

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd();

            if (line.Contains("IPv4 Route Table")) { inV4 = true;  inV6 = false; v4Active = false; continue; }
            if (line.Contains("IPv6 Route Table")) { inV4 = false; inV6 = true;  v6Active = false; continue; }

            if (line.TrimStart().StartsWith("Active Routes:"))
            {
                if (inV4) v4Active = true;
                if (inV6) v6Active = true;
                continue;
            }
            if (line.Contains("=====") || line.TrimStart().StartsWith("Persistent")) { v4Active = false; v6Active = false; }

            if (v4Active) { var m = v4rx.Match(line); if (m.Success) v4.Add(new RouteEntry(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value, m.Groups[5].Value)); }
            if (v6Active) { var m = v6rx.Match(line); if (m.Success) v6.Add(new RouteEntry(m.Groups[3].Value, "", m.Groups[4].Value.Trim(), m.Groups[1].Value, m.Groups[2].Value)); }
        }
        return (v4, v6);
    }

    private static void Dispatch(Action a) => Application.Current.Dispatcher.Invoke(a);
}
