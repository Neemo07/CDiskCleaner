using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CDiskCleaner.Converters
{
    /// <summary>
    /// Visible when the value is not null. Pass "invert" to show when it is null.
    /// </summary>
    public sealed class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
            bool hasValue = value != null;
            if (invert)
            {
                hasValue = !hasValue;
            }

            return hasValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
