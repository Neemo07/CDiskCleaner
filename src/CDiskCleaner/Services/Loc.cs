using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Windows;

namespace CDiskCleaner.Services
{
    /// <summary>
    /// Swaps the merged string dictionary at runtime and exposes the same strings to code.
    /// The string cache is replaced atomically so background deletion code can read it safely.
    /// </summary>
    public static class Loc
    {
        public const string Arabic = "ar";
        public const string English = "en";

        private static volatile Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public static string CurrentLanguage { get; private set; } = Arabic;

        public static FlowDirection FlowDirection
        {
            get { return CurrentLanguage == Arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight; }
        }

        public static event EventHandler LanguageChanged;

        public static void SetLanguage(string language)
        {
            if (!string.Equals(language, English, StringComparison.OrdinalIgnoreCase))
            {
                language = Arabic;
            }
            else
            {
                language = English;
            }

            Application app = Application.Current;
            if (app != null)
            {
                for (int i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
                {
                    ResourceDictionary dictionary = app.Resources.MergedDictionaries[i];
                    if (dictionary.Contains("_Locale"))
                    {
                        app.Resources.MergedDictionaries.RemoveAt(i);
                    }
                }

                var strings = new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/CDiskCleaner;component/Resources/Strings." + language + ".xaml",
                        UriKind.Absolute)
                };
                app.Resources.MergedDictionaries.Add(strings);

                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (object key in strings.Keys)
                {
                    string value = strings[key] as string;
                    if (value != null && key is string)
                    {
                        values[(string)key] = value;
                    }
                }

                _values = values;
            }

            CurrentLanguage = language;

            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(language == Arabic ? "ar-EG" : "en-US");
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
            }
            catch (CultureNotFoundException)
            {
                // The UI strings do not depend on the OS culture being installed.
            }

            EventHandler handler = LanguageChanged;
            if (handler != null)
            {
                handler(null, EventArgs.Empty);
            }
        }

        public static string T(string key)
        {
            return T(key, null);
        }

        public static string T(string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            Dictionary<string, string> values = _values;
            string text;
            if (values == null || !values.TryGetValue(key, out text) || text == null)
            {
                return key;
            }

            if (args == null || args.Length == 0)
            {
                return text;
            }

            try
            {
                return string.Format(CultureInfo.CurrentCulture, text, args);
            }
            catch (FormatException)
            {
                return text;
            }
        }
    }
}
