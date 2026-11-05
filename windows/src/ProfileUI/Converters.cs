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

    /// The folder for a new session. DefaultDirectory applies only until the user has chosen a
    /// folder: after that Windows opens the picker where they were last.
    public static string? ChooseFolder(string profileName)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = $"Choose a folder for the {profileName} session",
            DefaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        return WithModal(() => (owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true) ? dialog.FolderName : null;
    }
}
