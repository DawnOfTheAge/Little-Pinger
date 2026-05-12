using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LittlePinger.ViewModels;

/// <summary>
/// Base class for all view-models. Implements <see cref="INotifyPropertyChanged"/> and
/// provides <see cref="SetField{T}"/> to reduce boilerplate in property setters.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises <see cref="PropertyChanged"/> for the given property name.</summary>
    /// <param name="name">
    /// Property name. Automatically resolved from the calling member via
    /// <see cref="CallerMemberNameAttribute"/>.
    /// </param>
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// Sets <paramref name="field"/> to <paramref name="value"/> and raises
    /// <see cref="PropertyChanged"/> if the value actually changed.
    /// </summary>
    /// <typeparam name="T">Property type.</typeparam>
    /// <param name="field">Backing field reference.</param>
    /// <param name="value">New value to assign.</param>
    /// <param name="name">Property name (automatically resolved).</param>
    /// <returns><c>true</c> if the value changed; <c>false</c> if it was already equal.</returns>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
