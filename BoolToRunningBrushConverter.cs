using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LittlePinger.Converters;

/// <summary>
/// Converts a <see cref="bool"/> running state to a <see cref="SolidColorBrush"/>.
/// <c>true</c> (running) → red  |  <c>false</c> (stopped) → green.
/// Static brushes are frozen so they can be safely shared across WPF rendering threads.
/// </summary>
[ValueConversion(typeof(bool), typeof(SolidColorBrush))]
public class BoolToRunningBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush RunningBrush;
    private static readonly SolidColorBrush StoppedBrush;

    static BoolToRunningBrushConverter()
    {
        RunningBrush = new SolidColorBrush(Color.FromRgb(229,  57,  53)); // red
        RunningBrush.Freeze();
        StoppedBrush = new SolidColorBrush(Color.FromRgb( 67, 160,  71)); // green
        StoppedBrush.Freeze();
    }

    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? RunningBrush : StoppedBrush;

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
