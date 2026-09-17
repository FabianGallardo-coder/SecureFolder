using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SecureFolder.App.Converters;

/// <summary>
/// Converters bool → Visibility for WPF bindings.
/// </summary>
public sealed class BoolToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue = value is bool b && b;
        if (parameter?.ToString() == "Inverted")
            boolValue = !boolValue;

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility v && v == Visibility.Visible;
    }
}

/// <summary>
/// Converter for showing/hiding UI based on collection count.
/// </summary>
public sealed class CountToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is System.Collections.ICollection collection)
        {
            bool nonZero = collection.Count > 0;
            if (parameter?.ToString() == "Inverted")
                nonZero = !nonZero;
            return nonZero ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}