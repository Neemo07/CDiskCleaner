using System.Windows;
using System.Windows.Media;
using CDiskCleaner.Models;
using CDiskCleaner.Services;

namespace CDiskCleaner.Views
{
    public partial class ConfirmDeleteWindow : Window
    {
        private readonly ScanItem _item;
        private readonly bool _recycle;

        public ConfirmDeleteWindow(ScanItem item, bool recycle, bool elevated)
        {
            InitializeComponent();

            _item = item;
            _recycle = recycle;

            FlowDirection = Loc.FlowDirection;
            Title = Loc.T("Confirm.Title");
            HeaderText.Text = Loc.T(recycle ? "Confirm.RecycleHeader" : "Confirm.DeleteHeader");
            MessageText.Text = Loc.T(recycle ? "Confirm.RecycleMessage" : "Confirm.DeleteMessage");
            ItemName.Text = item.Name;
            PathBox.Text = item.Path;
            SizeLabel.Text = Loc.T("Details.Size");
            TypeLabel.Text = Loc.T("Details.Type");
            SizeText.Text = Format.Size(item.SizeBytes);
            TypeText.Text = item.TypeText;
            CancelButton.Content = Loc.T("Confirm.Cancel");
            ConfirmButton.Content = Loc.T("Confirm.Confirm");
            AcknowledgeCheck.Content = Loc.T("Confirm.Acknowledge");

            if (recycle)
            {
                MethodBadge.Background = (Brush)FindResource("BrushAccent");
                MethodGlyph.Text = "↺";
            }
            else
            {
                MethodBadge.Background = (Brush)FindResource("BrushDanger");
                MethodGlyph.Text = "!";
                PermanentWarningPanel.Visibility = Visibility.Visible;
                AcknowledgeCheck.Visibility = Visibility.Visible;
                ConfirmButton.Style = (Style)FindResource("DangerButton");
                ConfirmButton.IsEnabled = false;
            }

            if (item.Kind == ItemKind.Folder)
            {
                FolderNotePanel.Visibility = Visibility.Visible;
                FolderNoteText.Text = Loc.T("Confirm.FolderNote", Format.Count(item.FileCount));
            }
            else if (item.Kind == ItemKind.Group)
            {
                FolderNotePanel.Visibility = Visibility.Visible;
                FolderNoteText.Text = Loc.T("Confirm.GroupNote", Format.Count(item.FileCount));
            }

            if (item.RequiresAdmin && !elevated)
            {
                AdminWarningPanel.Visibility = Visibility.Visible;
            }

            if (recycle)
            {
                long total = DriveSnapshot.Capture(item.Path).TotalBytes;
                if (total > 0 && item.SizeBytes > total * 0.05)
                {
                    CapacityWarningPanel.Visibility = Visibility.Visible;
                    CapacityWarningText.Text = Loc.T("Confirm.RecycleCapacity", Format.Size(item.SizeBytes));
                }
            }

            if (!PathSafety.IsDeletable(item))
            {
                ConfirmButton.IsEnabled = false;
                MessageText.Text = Loc.T("Details.Protected");
            }
        }

        private void AcknowledgeCheck_OnChecked(object sender, RoutedEventArgs e)
        {
            ConfirmButton.IsEnabled = _recycle || AcknowledgeCheck.IsChecked == true;
        }

        private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (!_recycle && AcknowledgeCheck.IsChecked != true)
            {
                return;
            }

            DialogResult = true;
        }

        private void CancelButton_OnClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
