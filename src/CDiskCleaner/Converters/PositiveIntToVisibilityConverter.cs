using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CDiskCleaner.Converters
{
    public sealed class PositiveIntToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int number = value is int ? (int)value : 0;
            return number > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
