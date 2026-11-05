using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ProfileUI;

/// "#RRGGBB" to a brush. Only validated colours reach here, so a bad value falls back to grey.
public sealed class HexToBrush : IValueConverter
{
    public static SolidColorBrush Brush(string? hex)
    {
        try
        {
            if (hex is { Length: 7 } && hex[0] == '#' && ColorConverter.ConvertFromString(hex) is Color c)
            {
                var b = new SolidColorBrush(c);
                b.Freeze();
                return b;
            }
        }
        catch (FormatException) { }
        return Brushes.Gray;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Brush(value as string);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// True to Visible; with ConverterParameter=Invert, true to Collapsed. Null and empty strings count as false.
public sealed class Shown : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var on = value switch { bool b => b, string s => s.Length > 0, int n => n > 0, null => false, _ => true };
        if (parameter as string == "Invert") on = !on;
        return on ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// While a dialog of the panel's own is open, the panel must not hide itself when it loses focus.
public static class UiState
{
    public static int ModalOpen { get; set; }

    public static T WithModal<T>(Func<T> show)
    {
        ModalOpen++;
        try { return show(); } finally { ModalOpen--; }
    }
}
