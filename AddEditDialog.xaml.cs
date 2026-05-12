using System.Windows;

namespace LittlePinger;

/// <summary>
/// Modal dialog for adding a new ping entry or editing an existing one.
/// Exposes the validated form values through read-only properties after the user clicks OK.
/// </summary>
public partial class AddEditDialog : Window
{
    /// <summary>Validated name chosen by the user (defaults to IP if left blank).</summary>
    public string EntryName { get; private set; } = string.Empty;

    /// <summary>Validated IP address or hostname.</summary>
    public string IpAddress { get; private set; } = string.Empty;

    /// <summary>Validated ping interval in milliseconds.</summary>
    public int Interval { get; private set; } = 1000;

    /// <summary>Validated ping timeout in milliseconds.</summary>
    public int Timeout { get; private set; } = 1000;

    /// <summary>
    /// <c>true</c> once the user has manually edited the Name field.
    /// Prevents the auto-sync from overwriting a user-supplied name.
    /// </summary>
    private bool _nameDirty;

    /// <summary>Opens the dialog pre-filled with default values (Add mode).</summary>
    public AddEditDialog(int defaultInterval = 1000, int defaultTimeout = 1000)
    {
        InitializeComponent();
        TxtInterval.Text = defaultInterval.ToString();
        TxtTimeout.Text  = defaultTimeout.ToString();
    }

    /// <summary>Opens the dialog pre-filled with an existing entry's values (Edit mode).</summary>
    public AddEditDialog(string name, string ipAddress, int interval, int timeout)
        : this(interval, timeout)
    {
        _nameDirty   = true; // name is already set; never auto-sync in edit mode
        TxtName.Text = name;
        TxtIp.Text   = ipAddress;
        Loaded += (_, _) => { TxtName.Focus(); TxtName.SelectAll(); };
    }

    /// <summary>
    /// Wires the Name TextChanged handler after the window initialises so it fires only on real
    /// user input, not on programmatic population of the field.
    /// </summary>
    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += (_, _) => TxtName.TextChanged += (_, _) => _nameDirty = true;
    }

    /// <summary>
    /// Keeps the Name field in sync with the IP field while the user has not yet manually
    /// edited the Name (i.e. while <see cref="_nameDirty"/> is <c>false</c>).
    /// </summary>
    private void TxtIp_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_nameDirty)
            TxtName.Text = TxtIp.Text;
    }

    /// <summary>
    /// Validates all fields. On success, populates the result properties and closes the dialog
    /// with <see cref="Window.DialogResult"/> = <c>true</c>.
    /// </summary>
    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        var ip = TxtIp.Text.Trim();
        if (string.IsNullOrEmpty(ip))
        {
            ShowError("Please enter an IP address or hostname.", TxtIp);
            return;
        }

        if (!int.TryParse(TxtInterval.Text.Trim(), out var interval) || interval < 100)
        {
            ShowError("Interval must be a whole number ≥ 100 ms.", TxtInterval);
            return;
        }

        if (!int.TryParse(TxtTimeout.Text.Trim(), out var timeout) || timeout < 100)
        {
            ShowError("Timeout must be a whole number ≥ 100 ms.", TxtTimeout);
            return;
        }

        EntryName    = string.IsNullOrWhiteSpace(TxtName.Text) ? ip : TxtName.Text.Trim();
        IpAddress    = ip;
        Interval     = interval;
        Timeout      = timeout;
        DialogResult = true;
    }

    /// <summary>Shows a warning message box and sets focus to <paramref name="focusTarget"/>.</summary>
    private void ShowError(string message, System.Windows.Controls.Control focusTarget)
    {
        MessageBox.Show(this, message, "Validation Error",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        focusTarget.Focus();
    }
}
