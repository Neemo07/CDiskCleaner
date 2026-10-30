using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CDiskCleaner.Models;

namespace CDiskCleaner.Converters
{
    public sealed class CategoryToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string key = "CategoryAll";
            if (value is ScanCategory)
            {
                key = "Category" + ((ScanCategory)value);
            }
            else if (value is SafetyRating)
            {
                key = "SafetyBrush" + ((SafetyRating)value);
            }

            return Application.Current != null && Application.Current.TryFindResource(key) is Brush
                ? Application.Current.TryFindResource(key)
                : Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
