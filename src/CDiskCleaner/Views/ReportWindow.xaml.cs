using System;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using CDiskCleaner.Models;
using CDiskCleaner.Services;

namespace CDiskCleaner.Views
{
    public partial class ReportWindow : Window
    {
        private readonly ReportData _data;

        public ReportWindow(ReportData data)
        {
            InitializeComponent();
            _data = data;
            DataContext = data;
            FlowDirection = Loc.FlowDirection;
            Title = Loc.T("Report.Title");
        }

        private void CopyButton_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_data.Text ?? string.Empty);
                StatusText.Text = Loc.T("Report.Copied");
            }
            catch (Exception ex)
            {
                StatusText.Text = Loc.T("Msg.CopyFailed") + " " + ex.Message;
            }
        }

        private void SaveButton_OnClick(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = Loc.T("Report.SaveDialogTitle"),
                FileName = "CDiskCleaner-Report-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                AddExtension = true,
                DefaultExt = ".txt"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                File.WriteAllText(dialog.FileName, _data.Text ?? string.Empty, new UTF8Encoding(true));
                StatusText.Text = Loc.T("Msg.ReportSaved", dialog.FileName);
            }
            catch (Exception ex)
            {
                StatusText.Text = Loc.T("Msg.SaveFailed", ex.Message);
            }
        }

        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
