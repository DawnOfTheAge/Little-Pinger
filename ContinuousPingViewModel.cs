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

    public RelayCommand AddHostCommand { get; }

    public ContinuousPingViewModel(ObservableCollection<PingEntryViewModel> pingEntries)
    {
        _pingEntries   = pingEntries;
        AddHostCommand = new RelayCommand(OnAddHost);

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
