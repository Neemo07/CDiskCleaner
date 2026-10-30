using System;
using System.Globalization;
using System.Windows.Data;
using CDiskCleaner.Models;

namespace CDiskCleaner.Converters
{
    public sealed class KindGlyphConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ItemKind)
            {
                switch ((ItemKind)value)
                {
                    case ItemKind.Folder:
                        return "\uE8B7";
                    case ItemKind.Group:
                        return "\uE8F1";
                    default:
                        return "\uE8A5";
                }
            }

            return "\uE8A5";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
