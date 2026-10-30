using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CDiskCleaner.Models;
using CDiskCleaner.Services;
using CDiskCleaner.ViewModels;

namespace CDiskCleaner
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            Loaded += (sender, args) =>
            {
                ApplyLanguage();
                ApplyInitialSize();
            };
            Loc.LanguageChanged += OnLanguageChanged;
            _viewModel.ScanCompleted += (sender, args) => RestoreActionableSelection();
        }

        /// <summary>
        /// The DataGrid grabs the first row when items are added, which is usually a protected
        /// item. Put the selection back on the first item that can actually be acted on.
        /// </summary>
        private void RestoreActionableSelection()
        {
            if (_viewModel.SelectedItem == null)
            {
                return;
            }

            if (!ReferenceEquals(ResultsGrid.SelectedItem, _viewModel.SelectedItem))
            {
                ResultsGrid.SelectedItem = _viewModel.SelectedItem;
                ResultsGrid.ScrollIntoView(_viewModel.SelectedItem);
            }
        }

        /// <summary>
        /// Fits the window into the work area of the screen while keeping a 16:9 aspect ratio,
        /// and shrinks the side columns on small screens.
        /// </summary>
        private void ApplyInitialSize()
        {
            Rect work = SystemParameters.WorkArea;
            if (work.Width <= 0 || work.Height <= 0)
            {
                return;
            }

            const double aspect = 16.0 / 9.0;
            double maxWidth = work.Width * 0.94;
            double maxHeight = work.Height * 0.94;

            double width = Math.Min(1440, maxWidth);
            double height = width / aspect;
            if (height > maxHeight)
            {
                height = maxHeight;
                width = height * aspect;
            }

            double minWidth = Math.Min(1024, work.Width);
            double minHeight = Math.Min(620, work.Height);
            width = Math.Max(width, minWidth);
            height = Math.Max(height, minHeight);

            if (width > work.Width)
            {
                width = work.Width;
                height = width / aspect;
            }

            if (height > work.Height)
            {
                height = work.Height;
                width = height * aspect;
            }

            Width = width;
            Height = height;
            MinWidth = minWidth;
            MinHeight = minHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = work.Left + (work.Width - width) / 2.0;
            Top = work.Top + (work.Height - height) / 2.0;

            bool compact = width < 1280;
            SidebarColumn.Width = new GridLength(compact ? 272 : 300);
            DetailsColumn.Width = new GridLength(compact ? 300 : 344);

            // the candidate list has to stay readable: give the item column a minimum width
            // and shrink the fixed columns on small screens
            ItemColumn.MinWidth = 170;
            ItemColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            SizeColumn.Width = new DataGridLength(compact ? 74 : 84);
            CategoryColumn.Width = new DataGridLength(compact ? 58 : 66);
            SafetyColumn.Width = new DataGridLength(compact ? 92 : 105);
            ModifiedColumn.Width = new DataGridLength(compact ? 104 : 118);
            StatusColumn.Width = new DataGridLength(compact ? 86 : 96);
            // on a narrow screen the status column is the least useful one, the details pane shows it too
            StatusColumn.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            ResultsGrid.HorizontalScrollBarVisibility = compact
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled;
        }

        private void ApplyLanguage()
        {
            FlowDirection = Loc.FlowDirection;
            Title = Loc.T("App.Title");
            ItemColumn.Header = Loc.T("Column.Item");
            SizeColumn.Header = Loc.T("Column.Size");
            CategoryColumn.Header = Loc.T("Column.Category");
            SafetyColumn.Header = Loc.T("Column.Safety");
            ModifiedColumn.Header = Loc.T("Column.Modified");
            StatusColumn.Header = Loc.T("Column.Status");
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(ApplyLanguage));
                return;
            }

            ApplyLanguage();
        }

        /// <summary>
        /// Tick/untick a single row. The template check box lives in a DataGrid cell, so the
        /// state change is pushed into the model here; the selection bar and the header box
        /// follow the model, not the visual state. The routed Checked/Unchecked events cover
        /// mouse, keyboard and UI automation; a no-op write is harmless.
        /// </summary>
        private void SelectionCheck_OnToggle(object sender, RoutedEventArgs e)
        {
            CheckBox box = sender as CheckBox;
            if (box == null)
            {
                return;
            }

            ScanItem item = box.DataContext as ScanItem;
            if (item == null)
            {
                return;
            }

            item.IsSelected = box.IsChecked == true;
        }

        private void ResultsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel == null || _viewModel.SelectedItem == null)
            {
                return;
            }

            DependencyObject source = e.OriginalSource as DependencyObject;
            while (source != null && !(source is DataGridRow))
            {
                source = VisualTreeHelper.GetParent(source);
            }

            if (source == null)
            {
                return;
            }

            if (_viewModel.OpenLocationCommand.CanExecute(null))
            {
                _viewModel.OpenLocationCommand.Execute(null);
            }
        }

        private void MainWindow_OnClosing(object sender, CancelEventArgs e)
        {
            if (_viewModel == null || !_viewModel.IsScanning)
            {
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                this,
                Loc.T("Msg.ExitWhileScanning"),
                Loc.T("Msg.ExitTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No,
                Loc.CurrentLanguage == Loc.Arabic ? MessageBoxOptions.RtlReading : MessageBoxOptions.None);

            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            if (_viewModel.CancelScanCommand.CanExecute(null))
            {
                _viewModel.CancelScanCommand.Execute(null);
            }
        }

        private void MainWindow_OnClosed(object sender, EventArgs e)
        {
            Loc.LanguageChanged -= OnLanguageChanged;
            _viewModel.Dispose();
        }
    }
}
