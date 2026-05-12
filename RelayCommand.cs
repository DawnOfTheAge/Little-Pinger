using System.Windows.Input;

namespace LittlePinger.ViewModels;

/// <summary>
/// Lightweight <see cref="ICommand"/> implementation that delegates execution and
/// can-execute logic to caller-supplied delegates.
/// <see cref="CanExecuteChanged"/> is wired to <see cref="CommandManager.RequerySuggested"/>
/// so WPF re-evaluates commands automatically after UI interactions.
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>Creates a command with an optional parameter-aware can-execute predicate.</summary>
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute    = execute;
        _canExecute = canExecute;
    }

    /// <summary>Convenience constructor for parameter-less execute and can-execute delegates.</summary>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute()) { }

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged
    {
        add    => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <inheritdoc/>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <inheritdoc/>
    public void Execute(object? parameter) => _execute(parameter);
}
