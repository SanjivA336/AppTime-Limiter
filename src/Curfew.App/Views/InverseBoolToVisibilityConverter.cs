using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Curfew.App.Views;

/// <summary>The opposite of the built-in one: true hides the element, false shows it.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
