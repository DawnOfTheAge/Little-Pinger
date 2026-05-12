using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LittlePinger.Enums;

namespace LittlePinger.Converters;

/// <summary>
/// Converts a <see cref="PingEntryStatus"/> to a <see cref="SolidColorBrush"/> status indicator colour.
/// Static brushes are frozen so they can be safely shared across WPF rendering threads.
/// </summary>
[ValueConversion(typeof(PingEntryStatus), typeof(SolidColorBrush))]
public class PingStatusToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush SuccessBrush;
    private static readonly SolidColorBrush TimeoutBrush;
    private static readonly SolidColorBrush ErrorBrush;
    private static readonly SolidColorBrush UnknownBrush;

    static PingStatusToColorConverter()
    {
        SuccessBrush = new SolidColorBrush(Color.FromRgb( 67, 160,  71)); // green
        SuccessBrush.Freeze();
        TimeoutBrush = new SolidColorBrush(Color.FromRgb(251, 140,   0)); // amber
        TimeoutBrush.Freeze();
        ErrorBrush   = new SolidColorBrush(Color.FromRgb(229,  57,  53)); // red
        ErrorBrush.Freeze();
        UnknownBrush = new SolidColorBrush(Color.FromRgb(189, 189, 189)); // grey
        UnknownBrush.Freeze();
    }

    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is PingEntryStatus s ? s switch
        {
            PingEntryStatus.Success => SuccessBrush,
            PingEntryStatus.Timeout => TimeoutBrush,
            PingEntryStatus.Error   => ErrorBrush,
            _                       => UnknownBrush
        } : UnknownBrush;

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
