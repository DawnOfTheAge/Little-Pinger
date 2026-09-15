using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>
/// Container view-model for the Continuous Ping tab.
/// Manages a collection of <see cref="PingHostViewModel"/> instances, each pinging their own host.
/// </summary>
public class ContinuousPingViewModel : ViewModelBase
{
    private readonly ObservableCollection<PingEntryViewModel> _pingEntries;

    public ObservableCollection<PingHostViewModel> Hosts { get; } = new();

    public RelayCommand AddHostCommand      { get; }
    public RelayCommand SyncFromPingCommand { get; }

    public ContinuousPingViewModel(ObservableCollection<PingEntryViewModel> pingEntries)
    {
        _pingEntries      = pingEntries;
        AddHostCommand    = new RelayCommand(OnAddHost);
        SyncFromPingCommand = new RelayCommand(OnSyncFromPing, () => _pingEntries.Count > 0);

        // Keep AllHosts suggestions in sync for all host VMs
        pingEntries.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (PingEntryViewModel entry in e.NewItems)
                    entry.PropertyChanged += (_, pe) =>
                    {
                        if (pe.PropertyName == nameof(PingEntryViewModel.IpAddress))
                            RebuildAllHosts();
                    };
            RebuildAllHosts();
        };

        foreach (var entry in pingEntries)
            entry.PropertyChanged += (_, pe) =>
            {
                if (pe.PropertyName == nameof(PingEntryViewModel.IpAddress))
                    RebuildAllHosts();
            };

        // Start with one default host panel
        AddHostInternal();
    }

    private void OnAddHost()
    {
        AddHostInternal();
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Imports every entry from the main Ping list as a continuous-ping host panel
    /// (skipping any already present) and starts pinging immediately.
    /// If the only existing panel is the default empty one, it is replaced.
    /// </summary>
    private void OnSyncFromPing()
    {
        // Remove the single default empty panel if it has never been started
        if (Hosts.Count == 1 && string.IsNullOrWhiteSpace(Hosts[0].Host) && !Hosts[0].IsRunning)
        {
            Hosts[0].Stop();
            Hosts.Clear();
        }

        foreach (var entry in _pingEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.IpAddress)) continue;

            // Skip if this host is already present
            if (Hosts.Any(h => string.Equals(h.Host, entry.IpAddress, StringComparison.OrdinalIgnoreCase)))
                continue;

            var vm = AddHostInternal();
            vm.Host = entry.IpAddress;
            CommandManager.InvalidateRequerySuggested();
            if (vm.StartCommand.CanExecute(null))
                vm.StartCommand.Execute(null);
        }

        // If nothing was added (e.g. ping list empty) restore a blank panel
        if (Hosts.Count == 0)
            AddHostInternal();

        CommandManager.InvalidateRequerySuggested();
    }

    private PingHostViewModel AddHostInternal()
    {
        var vm = new PingHostViewModel();
        RebuildAllHostsFor(vm);
        vm.RemoveCommand = new RelayCommand(
            () =>
            {
                vm.Stop();
                Hosts.Remove(vm);
                CommandManager.InvalidateRequerySuggested();
            },
            () => !vm.IsRunning && Hosts.Count > 1);
        Hosts.Add(vm);
        return vm;
    }

    private void RebuildAllHosts()
    {
        foreach (var vm in Hosts) RebuildAllHostsFor(vm);
    }

    private void RebuildAllHostsFor(PingHostViewModel vm)
    {
        vm.AllHosts.Clear();
        foreach (var e in _pingEntries)
            if (!string.IsNullOrWhiteSpace(e.IpAddress)) vm.AllHosts.Add(e.IpAddress);
    }
}
