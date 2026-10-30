using System.Collections.Generic;
using System.Windows;
using CDiskCleaner.Models;
using CDiskCleaner.Services;

namespace CDiskCleaner.Views
{
    /// <summary>
    /// One confirmation for every ticked candidate. The method is chosen here, so the
    /// caller never deletes anything before this dialog returns true.
    /// </summary>
    public partial class ConfirmBulkDeleteWindow : Window
    {
        public ConfirmBulkDeleteWindow(IList<ScanItem> items, bool elevated)
        {
            InitializeComponent();

            FlowDirection = Loc.FlowDirection;
            Title = Loc.T("Confirm.Title");
            HeaderText.Text = Loc.T("Confirm.BulkHeader");
            MessageText.Text = Loc.T("Confirm.BulkMessage");

            long totalBytes = 0;
            bool needsAdmin = false;
            foreach (ScanItem item in items)
            {
                if (item.SizeBytes > 0)
                {
                    totalBytes += item.SizeBytes;
                }

                if (item.RequiresAdmin)
                {
                    needsAdmin = true;
                }
            }

            CountLabel.Text = Loc.T("Confirm.BulkCountLabel");
            CountText.Text = Format.Count(items.Count);
            SizeLabel.Text = Loc.T("Details.Size");
            SizeText.Text = Format.Size(totalBytes);
            ItemsList.ItemsSource = items;

            CancelButton.Content = Loc.T("Confirm.Cancel");
            RecycleButton.Content = Loc.T("Confirm.BulkRecycle");
            PermanentButton.Content = Loc.T("Confirm.BulkPermanent");

            if (needsAdmin && !elevated)
            {
                AdminWarningPanel.Visibility = Visibility.Visible;
            }
        }

        /// <summary>True when the user picked "move to Recycle Bin".</summary>
        public bool Recycle { get; private set; }

        private void AcknowledgeCheck_OnChecked(object sender, RoutedEventArgs e)
        {
            PermanentButton.IsEnabled = AcknowledgeCheck.IsChecked == true;
        }

        private void RecycleButton_OnClick(object sender, RoutedEventArgs e)
        {
            Recycle = true;
            DialogResult = true;
        }

        private void PermanentButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (AcknowledgeCheck.IsChecked != true)
            {
                return;
            }

            Recycle = false;
            DialogResult = true;
        }

        private void CancelButton_OnClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}