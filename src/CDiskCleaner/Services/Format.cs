using System;
using System.Globalization;

namespace CDiskCleaner.Services
{
    public static class Format
    {
        private static readonly string[] Units = { "KB", "MB", "GB", "TB", "PB" };

        public static string Size(long bytes)
        {
            if (bytes < 0)
            {
                return "—";
            }

            if (bytes < 1024)
            {
                return bytes.ToString("N0", CultureInfo.InvariantCulture) + " B";
            }

            double value = bytes;
            int unit = -1;
            do
            {
                value /= 1024d;
                unit++;
            }
            while (value >= 1024d && unit < Units.Length - 1);

            string format = value >= 100 ? "N0" : value >= 10 ? "N1" : "N2";
            return value.ToString(format, CultureInfo.InvariantCulture) + " " + Units[unit];
        }

        public static string SizeSigned(long bytes)
        {
            if (bytes > 0)
            {
                return "+" + Size(bytes);
            }

            if (bytes < 0)
            {
                return "-" + Size(-bytes);
            }

            return Size(0);
        }

        public static string Count(long count)
        {
            return count.ToString("N0", CultureInfo.InvariantCulture);
        }

        public static string Date(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : "—";
        }

        public static int AgeDays(DateTime value)
        {
            double days = (DateTime.Now - value).TotalDays;
            if (days < 0)
            {
                return 0;
            }

            return (int)Math.Floor(days);
        }

        public static string Percent(double value)
        {
            return value.ToString("N1", CultureInfo.InvariantCulture) + "%";
        }

        public static string Months(int days)
        {
            return Math.Max(1, days / 30).ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
