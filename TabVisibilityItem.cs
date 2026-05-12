using System.Collections.ObjectModel;

namespace LittlePinger.ViewModels;

/// <summary>
/// Represents a tab (or sub-tab) that can be shown or hidden by the user.
/// Root items have no parent; child items reflect their parent's visibility
/// through <see cref="IsEnabled"/> so they appear disabled when the parent is hidden.
/// </summary>
public class TabVisibilityItem : ViewModelBase
{
    private bool _isVisible = true;
    private TabVisibilityItem? _parent;

    /// <summary>Display name shown in the settings tree view.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary><c>true</c> when this tab should be visible.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    /// <summary>
    /// <c>false</c> when the parent tab is hidden, so the child checkbox appears disabled.
    /// Always <c>true</c> for root items.
    /// </summary>
    public bool IsEnabled => _parent?.IsVisible ?? true;

    /// <summary>Child items (sub-tabs). Empty for leaf tabs.</summary>
    public ObservableCollection<TabVisibilityItem> Children { get; } = new();

    /// <summary>Wires up <see cref="IsEnabled"/> change notifications from the parent.</summary>
    internal void SetParent(TabVisibilityItem parent)
    {
        _parent = parent;
        parent.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsVisible))
                OnPropertyChanged(nameof(IsEnabled));
        };
    }
}
