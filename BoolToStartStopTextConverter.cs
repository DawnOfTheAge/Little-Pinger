using System.Globalization;
using System.Windows.Data;

namespace LittlePinger.Converters;

/// <summary>true → "⏹  Stop"  |  false → "▶  Start"</summary>
[ValueConversion(typeof(bool), typeof(string))]
public class BoolToStartStopTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "⏹  Stop" : "▶  Start";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
