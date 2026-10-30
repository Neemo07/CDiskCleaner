using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CDiskCleaner.Converters
{
    public sealed class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool empty = string.IsNullOrWhiteSpace(value as string);
            bool invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
            if (invert)
            {
                empty = !empty;
            }

            return empty ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
