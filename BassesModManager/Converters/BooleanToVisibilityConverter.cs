using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BassesModManager.Converters
{
    /// <summary>
    /// Bool to Visibility, collapsing rather than hiding. Pass "invert" as the converter
    /// parameter to show on false instead of true.
    /// </summary>
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool b && b;
            if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
                flag = !flag;
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
