using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CDiskCleaner.Services;

namespace CDiskCleaner
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Loc.SetLanguage("ar");

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                LogException(args.ExceptionObject as Exception);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogException(e.Exception);
            MessageBox.Show(
                Loc.T("Msg.UnexpectedError", e.Exception == null ? string.Empty : e.Exception.Message),
                Loc.T("App.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error,
                MessageBoxResult.OK,
                MessageBoxOptions.RtlReading);
            e.Handled = true;
        }

        private static void LogException(Exception ex)
        {
            if (ex == null)
            {
                return;
            }

            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CDiskCleaner");
                Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.AppendLine("------------------------------------------------------------");
                sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine(ex.ToString());
                File.AppendAllText(Path.Combine(dir, "crash.log"), sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Logging must never crash the application.
            }
        }
    }
}
