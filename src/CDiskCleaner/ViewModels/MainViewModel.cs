using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CDiskCleaner.Models;
using CDiskCleaner.Services;
using CDiskCleaner.Views;

namespace CDiskCleaner.ViewModels
{
    public sealed class MainViewModel : ObservableObject, IDisposable
    {
        private readonly ObservableCollection<ScanItem> _items = new ObservableCollection<ScanItem>();
        private readonly ICollectionView _itemsView;
        private readonly DeletionService _deletionService = new DeletionService();
        private readonly DispatcherTimer _driveTimer;

        private CancellationTokenSource _scanCancellation;
        private ScanResult _lastScan;
        private SessionState _session;

        private bool _isScanning;
        private bool _isBusy;
        private bool _hasScanned;
        private bool _fullScan = true;
        private bool _findDuplicates;
        private string _searchText = string.Empty;
        private bool _hideSkipped = true;
        private CategoryFilterOption _selectedCategoryOption;
        private ScanItem _selectedItem;
        private bool _bulkSelecting;

        private string _phaseKey = "Scan.Starting";
        private string _currentPath = string.Empty;
        private long _filesScanned;
        private long _directoriesScanned;
        private int _inaccessibleDirectories;
        private int _liveCandidateCount;
        private long _liveCandidateBytes;

        private string _statusKey = "Footer.Ready";
        private object[] _statusArgs;

        private long _freeBytes;
        private long _totalBytes;
        private string _driveName = "C:";
        private int _visibleItems;
        private int _pendingCount;
        private long _pendingBytes;
        private bool _isElevated;

        public bool HasSidecarConfig
        {
            get { return _hasSidecarConfig; }
        }

        public bool ShowConfigWarning
        {
            get { return !_hasSidecarConfig; }
        }

        public string ConfigWarningText
        {
            get { return Loc.T("Msg.NoConfigWarning"); }
        }

        private readonly bool _hasSidecarConfig;

        public MainViewModel()
        {
            _hasSidecarConfig = DetectSidecarConfig();
            _itemsView = CollectionViewSource.GetDefaultView(_items);
            _itemsView.Filter = FilterItem;
            _itemsView.SortDescriptions.Add(new SortDescription("SizeBytes", ListSortDirection.Descending));

            CategoryOptions = new ObservableCollection<CategoryFilterOption>
            {
                new CategoryFilterOption(null, "◎", "Cat.All"),
                new CategoryFilterOption(ScanCategory.A, "A", "Cat.A"),
                new CategoryFilterOption(ScanCategory.B, "B", "Cat.B"),
                new CategoryFilterOption(ScanCategory.C, "C", "Cat.C"),
                new CategoryFilterOption(ScanCategory.D, "D", "Cat.D")
            };
            _selectedCategoryOption = CategoryOptions[0];

            InitializeCommands();
            _isElevated = CheckElevated();
            RefreshDrive();

            _driveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _driveTimer.Tick += (sender, args) => RefreshDrive();
            _driveTimer.Start();

            Loc.LanguageChanged += OnLanguageChanged;
            RefreshCounters();
            RefreshList();
        }

        public ObservableCollection<ScanItem> Items
        {
            get { return _items; }
        }

        public ICollectionView ItemsView
        {
            get { return _itemsView; }
        }

        public ObservableCollection<CategoryFilterOption> CategoryOptions { get; private set; }

        public CategoryFilterOption SelectedCategoryOption
        {
            get { return _selectedCategoryOption; }
            set
            {
                if (value != null && SetProperty(ref _selectedCategoryOption, value))
                {
                    RefreshList();
                }
            }
        }

        public ScanItem SelectedItem
        {
            get { return _selectedItem; }
            set
            {
                if (SetProperty(ref _selectedItem, value))
                {
                    RaiseSelectionDetails();
                    RefreshCommands();
                }
            }
        }

        public bool IsScanning
        {
            get { return _isScanning; }
            private set
            {
                if (SetProperty(ref _isScanning, value))
                {
                    OnPropertyChanged("ShowWelcome");
                    OnPropertyChanged("ShowScanning");
                    OnPropertyChanged("ShowResults");
                    OnPropertyChanged("ShowEmpty");
                    OnPropertyChanged("CanDeleteSelected");
                    OnPropertyChanged("CanSkipSelected");
                    OnPropertyChanged("CanDeleteSelectedItems");
                    OnPropertyChanged("PhaseOrReadyText");
                    RefreshCommands();
                }
            }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged("CanDeleteSelected");
                    OnPropertyChanged("CanSkipSelected");
                    OnPropertyChanged("CanDeleteSelectedItems");
                    RefreshCommands();
                }
            }
        }

        public bool HasScanned
        {
            get { return _hasScanned; }
            private set
            {
                if (SetProperty(ref _hasScanned, value))
                {
                    OnPropertyChanged("ShowWelcome");
                    OnPropertyChanged("ShowResults");
                    OnPropertyChanged("ShowEmpty");
                    OnPropertyChanged("CanReport");
                    RefreshCommands();
                }
            }
        }

        public bool FullScan
        {
            get { return _fullScan; }
            set
            {
                if (SetProperty(ref _fullScan, value))
                {
                    OnPropertyChanged("ScanHintText");
                }
            }
        }

        public bool FindDuplicates
        {
            get { return _findDuplicates; }
            set { SetProperty(ref _findDuplicates, value); }
        }

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty))
                {
                    RefreshList();
                }
            }
        }

        public bool HideSkipped
        {
            get { return _hideSkipped; }
            set
            {
                if (SetProperty(ref _hideSkipped, value))
                {
                    RefreshList();
                }
            }
        }

        public bool IsElevated
        {
            get { return _isElevated; }
            private set
            {
                if (SetProperty(ref _isElevated, value))
                {
                    OnPropertyChanged("AdminStatusText");
                    OnPropertyChanged("ShowAdminNote");
                    RefreshCommands();
                }
            }
        }

        public string AdminStatusText
        {
            get { return Loc.T(IsElevated ? "Header.Elevated" : "Header.NotElevated"); }
        }

        public string LanguageButtonText
        {
            get { return Loc.CurrentLanguage == Loc.Arabic ? "English" : "العربية"; }
        }

        public string ScanHintText
        {
            get { return FullScan ? string.Empty : Loc.T("Sidebar.QuickModeHint"); }
        }

        public string PhaseText
        {
            get { return Loc.T(_phaseKey); }
        }

        /// <summary>
        /// The footer must keep the same height in every state, otherwise the content area
        /// shrinks and the side panel starts to scroll (which changes its width).
        /// </summary>
        public string PhaseOrReadyText
        {
            get { return IsScanning ? Loc.T(_phaseKey) : Loc.T("Footer.Ready"); }
        }

        public string StatusText
        {
            get { return Loc.T(_statusKey, _statusArgs); }
        }

        public string CurrentPath
        {
            get { return _currentPath; }
            private set { SetProperty(ref _currentPath, value); }
        }

        public long FilesScanned
        {
            get { return _filesScanned; }
            private set { SetProperty(ref _filesScanned, value); }
        }

        public long DirectoriesScanned
        {
            get { return _directoriesScanned; }
            private set { SetProperty(ref _directoriesScanned, value); }
        }

        public int InaccessibleDirectories
        {
            get { return _inaccessibleDirectories; }
            private set { SetProperty(ref _inaccessibleDirectories, value); }
        }

        public int LiveCandidateCount
        {
            get { return _liveCandidateCount; }
            private set { SetProperty(ref _liveCandidateCount, value); }
        }

        public long LiveCandidateBytes
        {
            get { return _liveCandidateBytes; }
            private set
            {
                if (SetProperty(ref _liveCandidateBytes, value))
                {
                    OnPropertyChanged("LiveCandidateBytesText");
                }
            }
        }

        public string LiveCandidateBytesText
        {
            get { return Format.Size(LiveCandidateBytes); }
        }

        public string FreeSpaceText
        {
            get { return Format.Size(_freeBytes); }
        }

        public string TotalSpaceText
        {
            get { return Format.Size(_totalBytes); }
        }

        public string UsedSpaceText
        {
            get { return Format.Size(Math.Max(0, _totalBytes - _freeBytes)); }
        }

        public string FreeOfText
        {
            get { return Loc.T("Header.FreeOf", Format.Size(_totalBytes)); }
        }

        public string DriveName
        {
            get { return _driveName; }
        }

        public double UsedPercent
        {
            get { return _totalBytes <= 0 ? 0 : (100.0 * (_totalBytes - _freeBytes) / _totalBytes); }
        }

        public double FreePercent
        {
            get { return _totalBytes <= 0 ? 0 : (100.0 * _freeBytes / _totalBytes); }
        }

        public int VisibleItems
        {
            get { return _visibleItems; }
            private set
            {
                if (SetProperty(ref _visibleItems, value))
                {
                    OnPropertyChanged("HasVisibleItems");
                    OnPropertyChanged("ShowEmpty");
                }
            }
        }

        public bool HasVisibleItems
        {
            get { return VisibleItems > 0; }
        }

        public bool ShowWelcome
        {
            get { return !HasScanned && !IsScanning; }
        }

        public bool ShowScanning
        {
            get { return IsScanning; }
        }

        public bool ShowResults
        {
            get { return HasScanned && !IsScanning; }
        }

        public bool ShowEmpty
        {
            get { return HasScanned && !IsScanning && !HasVisibleItems; }
        }

        public int PendingCount
        {
            get { return _pendingCount; }
            private set
            {
                if (SetProperty(ref _pendingCount, value))
                {
                    OnPropertyChanged("PendingSummaryText");
                }
            }
        }

        public long PendingBytes
        {
            get { return _pendingBytes; }
            private set
            {
                if (SetProperty(ref _pendingBytes, value))
                {
                    OnPropertyChanged("PendingSummaryText");
                }
            }
        }

        public string PendingSummaryText
        {
            get { return Loc.T("List.CountSummary", Format.Count(PendingCount), Format.Size(PendingBytes)); }
        }

        public int SessionDeletedCount
        {
            get { return _session == null ? 0 : _session.DeletedCount; }
        }

        public int SessionRecycledCount
        {
            get { return _session == null ? 0 : _session.RecycledCount; }
        }

        public int SessionSkippedCount
        {
            get { return _session == null ? 0 : _session.SkippedCount; }
        }

        public int SessionFailedCount
        {
            get { return _session == null ? 0 : _session.FailedCount; }
        }

        public string RecoveredText
        {
            get { return Format.Size(_session == null ? 0 : _session.DeletedBytes); }
        }

        public string RecycledText
        {
            get { return Format.Size(_session == null ? 0 : _session.RecycledBytes); }
        }

        public bool HasSelection
        {
            get { return SelectedItem != null; }
        }

        /// <summary>
        /// Only items that are currently visible (passed the category/search filters and
        /// "hide skipped") and may still be deleted count as selected, so the summary text
        /// always matches the exact list the bulk dialog will show.
        /// </summary>
        public int SelectedCount
        {
            get { return SelectedDeletableItems().Count; }
        }

        public long SelectedBytes
        {
            get { return SelectedDeletableItems().Where(i => i.SizeBytes > 0).Sum(i => i.SizeBytes); }
        }

        public bool HasSelectedItems
        {
            get { return SelectedCount > 0; }
        }

        public string SelectedSummaryText
        {
            get { return Loc.T("List.SelectionSummary", Format.Count(SelectedCount), Format.Size(SelectedBytes)); }
        }

        public bool CanDeleteSelectedItems
        {
            get { return !IsScanning && !IsBusy && SelectedDeletableItems().Count > 0; }
        }

        /// <summary>
        /// Header check box of the selection column. It ticks only the items the list is
        /// showing right now and only those that may be deleted (protected C: items and
        /// already processed rows stay out of it).
        /// </summary>
        public bool SelectAllVisible
        {
            get
            {
                List<ScanItem> visible = VisibleSelectableItems();
                return visible.Count > 0 && visible.All(i => i.IsSelected);
            }
            set
            {
                bool on = value;
                _bulkSelecting = true;
                try
                {
                    foreach (ScanItem item in VisibleSelectableItems())
                    {
                        item.IsSelected = on;
                    }
                }
                finally
                {
                    _bulkSelecting = false;
                }

                RefreshSelectionSummary();
            }
        }

        public bool CanReport
        {
            get { return _lastScan != null || _session != null; }
        }

        public bool CanDeleteSelected
        {
            get
            {
                return !IsScanning && !IsBusy && SelectedItem != null &&
                       SelectedItem.CanBeDeleted && PathSafety.IsDeletable(SelectedItem);
            }
        }

        public bool CanSkipSelected
        {
            get
            {
                return !IsScanning && !IsBusy && SelectedItem != null &&
                       (SelectedItem.Status == ItemStatus.Pending ||
                        SelectedItem.Status == ItemStatus.Failed);
            }
        }

        public bool ShowProtectedNote
        {
            get { return SelectedItem != null && SelectedItem.IsProtected; }
        }

        public bool ShowFolderNote
        {
            get { return SelectedItem != null && SelectedItem.Kind == ItemKind.Folder; }
        }

        public bool ShowGroupNote
        {
            get { return SelectedItem != null && SelectedItem.Kind == ItemKind.Group; }
        }

        public bool ShowAdminNote
        {
            get { return SelectedItem != null && SelectedItem.RequiresAdmin && !IsElevated; }
        }

        public bool ShowFailureDetail
        {
            get
            {
                return SelectedItem != null &&
                       SelectedItem.Status == ItemStatus.Failed &&
                       !string.IsNullOrWhiteSpace(SelectedItem.StatusDetail);
            }
        }

        public string GroupContentsText
        {
            get
            {
                return SelectedItem == null
                    ? string.Empty
                    : Loc.T("Details.GroupContents", Format.Count(SelectedItem.FileCount));
            }
        }

        public event EventHandler ScanCompleted;

        public RelayCommand StartScanCommand { get; private set; }
        public RelayCommand CancelScanCommand { get; private set; }
        public RelayCommand DeleteRecycleCommand { get; private set; }
        public RelayCommand DeletePermanentCommand { get; private set; }
        public RelayCommand DeleteSelectedCommand { get; private set; }
        public RelayCommand ClearSelectionCommand { get; private set; }
        public RelayCommand SkipCommand { get; private set; }
        public RelayCommand OpenLocationCommand { get; private set; }
        public RelayCommand CopyPathCommand { get; private set; }
        public RelayCommand ElevateCommand { get; private set; }
        public RelayCommand ShowReportCommand { get; private set; }
        public RelayCommand ToggleLanguageCommand { get; private set; }

        private void InitializeCommands()
        {
            StartScanCommand = new RelayCommand(
                parameter => StartScanAsync(),
                parameter => !IsScanning && !IsBusy);

            CancelScanCommand = new RelayCommand(
                parameter => CancelScan(),
                parameter => IsScanning);

            DeleteRecycleCommand = new RelayCommand(
                parameter => DeleteSelectedAsync(true),
                parameter => CanDeleteSelected);

            DeletePermanentCommand = new RelayCommand(
                parameter => DeleteSelectedAsync(false),
                parameter => CanDeleteSelected);

            DeleteSelectedCommand = new RelayCommand(
                parameter => ShowBulkDeleteDialog(),
                parameter => CanDeleteSelectedItems);

            ClearSelectionCommand = new RelayCommand(
                parameter => ClearSelection(),
                parameter => HasSelectedItems);

            SkipCommand = new RelayCommand(
                parameter => SkipSelected(),
                parameter => CanSkipSelected);

            OpenLocationCommand = new RelayCommand(
                parameter => OpenLocation(),
                parameter => SelectedItem != null);

            CopyPathCommand = new RelayCommand(
                parameter => CopyPath(),
                parameter => SelectedItem != null);

            ElevateCommand = new RelayCommand(
                parameter => Elevate(),
                parameter => !IsElevated);

            ShowReportCommand = new RelayCommand(
                parameter => ShowReport(),
                parameter => CanReport);

            ToggleLanguageCommand = new RelayCommand(
                parameter => ToggleLanguage(),
                parameter => true);
        }

        private async void StartScanAsync()
        {
            if (IsScanning || IsBusy)
            {
                return;
            }

            if (_session == null)
            {
                _session = new SessionState(DriveSnapshot.Capture(@"C:\"));
            }

            _scanCancellation = new CancellationTokenSource();
            IsScanning = true;
            HasScanned = false;
            _lastScan = null;
            foreach (ScanItem previous in _items)
            {
                UntrackItem(previous);
            }

            _items.Clear();
            SelectedItem = null;
            VisibleItems = 0;
            OnPropertyChanged("SelectAllVisible");
            OnPropertyChanged("SelectedCount");
            OnPropertyChanged("SelectedBytes");
            OnPropertyChanged("HasSelectedItems");
            OnPropertyChanged("SelectedSummaryText");

            _phaseKey = "Scan.Phase.Cleanup";
            _currentPath = string.Empty;
            _filesScanned = 0;
            _directoriesScanned = 0;
            _inaccessibleDirectories = 0;
            _liveCandidateCount = 0;
            _liveCandidateBytes = 0;
            _statusKey = "Scan.Starting";
            _statusArgs = null;
            RaiseProgressProperties();
            OnPropertyChanged("StatusText");
            SetStatus("Scan.Starting");

            var options = new ScanOptions
            {
                ScanRoot = @"C:\",
                IncludeSystemCleanupLocations = true,
                FullScan = FullScan,
                FindDuplicates = FullScan && FindDuplicates
            };

            IProgress<ScanProgress> progress = new Progress<ScanProgress>(OnProgress);
            CancellationToken token = _scanCancellation.Token;

            try
            {
                ScanResult result = await Task.Run(() =>
                {
                    var scanner = new FileSystemScanner(options, p => progress.Report(p), token);
                    return scanner.Scan();
                });

                _lastScan = result;
                foreach (ScanItem item in result.Items)
                {
                    TrackItem(item);
                    _items.Add(item);
                }

                HasScanned = true;
                RefreshCounters();
                RefreshList();
                SelectedItem = _items.FirstOrDefault(i => i.IsActionable);
                EventHandler completed = ScanCompleted;
                if (completed != null)
                {
                    completed(this, EventArgs.Empty);
                }

                RefreshDrive();

                if (!string.IsNullOrEmpty(result.UnexpectedError))
                {
                    SetStatus("Msg.ScanError", result.UnexpectedError);
                }
                else if (result.Cancelled)
                {
                    SetStatus("Scan.Canceled");
                }
                else if (_items.Count == 0)
                {
                    SetStatus("Scan.None");
                }
                else
                {
                    SetStatus("Scan.Done", Format.Count(_items.Count), Format.Size(result.CandidateBytes));
                }
            }
            catch (OperationCanceledException)
            {
                HasScanned = _items.Count > 0;
                SetStatus("Scan.Canceled");
            }
            catch (Exception ex)
            {
                HasScanned = _items.Count > 0;
                SetStatus("Msg.ScanError", ex.Message);
            }
            finally
            {
                IsScanning = false;
                if (_scanCancellation != null)
                {
                    _scanCancellation.Dispose();
                    _scanCancellation = null;
                }

                RefreshCounters();
                RefreshList();
                RefreshCommands();
            }
        }

        private void CancelScan()
        {
            if (_scanCancellation != null)
            {
                SetStatus("Msg.Cancelling");
                _scanCancellation.Cancel();
            }
        }

        private void OnProgress(ScanProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            _phaseKey = progress.PhaseKey;
            OnPropertyChanged("PhaseText");
            CurrentPath = progress.CurrentPath;
            FilesScanned = progress.FilesScanned;
            DirectoriesScanned = progress.DirectoriesScanned;
            InaccessibleDirectories = progress.InaccessibleDirectories;
            LiveCandidateCount = progress.Candidates;
            LiveCandidateBytes = progress.CandidateBytes;
        }

        private async void DeleteSelectedAsync(bool recycle)
        {
            ScanItem item = SelectedItem;
            if (item == null || !CanDeleteSelected)
            {
                SetStatus("Msg.NotDeletable");
                return;
            }

            var dialog = new ConfirmDeleteWindow(item, recycle, IsElevated)
            {
                Owner = Application.Current != null ? Application.Current.MainWindow : null
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            IsBusy = true;
            SetStatus("Msg.Processing");

            try
            {
                DeletionOutcome outcome = await Task.Run(() => _deletionService.Execute(item, recycle));
                if (outcome.Success)
                {
                    if (recycle && outcome.LikelyPermanentlyDeleted)
                    {
                        item.Status = ItemStatus.Deleted;
                        _session.Record(item, OperationMethod.Delete, true, null);
                        SetStatus("Msg.RecycleLikelyDeleted", item.Name, Format.Size(outcome.FreeDelta));
                    }
                    else
                    {
                        item.Status = recycle ? ItemStatus.Recycled : ItemStatus.Deleted;
                        _session.Record(item, recycle ? OperationMethod.Recycle : OperationMethod.Delete, true, null);
                        SetStatus(
                            recycle ? "Msg.RecycleDone" : "Msg.DeleteDone",
                            item.Name,
                            Format.Size(item.SizeBytes));
                    }

                    _items.Remove(item);
                    UntrackItem(item);
                    SelectedItem = _items.FirstOrDefault(i => i.IsActionable);
                }
                else
                {
                    item.Status = ItemStatus.Failed;
                    item.StatusDetail = outcome.Error;
                    _session.Record(item, recycle ? OperationMethod.Recycle : OperationMethod.Delete, false, outcome.Error);
                    SetStatus("Msg.OperationFailed", outcome.Error);
                }
            }
            catch (Exception ex)
            {
                item.Status = ItemStatus.Failed;
                item.StatusDetail = ex.Message;
                _session.Record(item, recycle ? OperationMethod.Recycle : OperationMethod.Delete, false, ex.Message);
                SetStatus("Msg.OperationFailed", ex.Message);
            }
            finally
            {
                IsBusy = false;
                RefreshDrive();
                RefreshCounters();
                RefreshList();
                RefreshCommands();
                RaiseSelectionDetails();
            }
        }

        /// <summary>
        /// One dialog decides the method (Recycle Bin or permanent) for every ticked item.
        /// Nothing is removed before the user confirms inside that dialog.
        /// </summary>
        private void ShowBulkDeleteDialog()
        {
            if (IsScanning || IsBusy)
            {
                return;
            }

            List<ScanItem> targets = SelectedDeletableItems();
            if (targets.Count == 0)
            {
                SetStatus("Msg.NotDeletable");
                return;
            }

            var dialog = new ConfirmBulkDeleteWindow(targets, IsElevated)
            {
                Owner = Application.Current != null ? Application.Current.MainWindow : null
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            DeleteManyAsync(targets, dialog.Recycle);
        }

        /// <summary>
        /// Deletes every ticked, visible, deletable item after ONE confirmation dialog.
        /// Each item is still processed on its own so a failure never stops the rest,
        /// and protected C: paths are rejected here even if they were ticked somehow.
        /// </summary>
        private async void DeleteManyAsync(List<ScanItem> targets, bool recycle)
        {
            IsBusy = true;
            SetStatus("Msg.Processing");

            int succeeded = 0;
            int failed = 0;
            long succeededBytes = 0;

            try
            {
                foreach (ScanItem item in targets)
                {
                    if (!item.CanBeDeleted || !PathSafety.IsDeletable(item))
                    {
                        continue;
                    }

                    try
                    {
                        DeletionOutcome outcome = await Task.Run(() => _deletionService.Execute(item, recycle));
                        if (outcome.Success)
                        {
                            bool permanently = !recycle || outcome.LikelyPermanentlyDeleted;
                            item.Status = permanently ? ItemStatus.Deleted : ItemStatus.Recycled;
                            item.StatusDetail = null;
                            item.IsSelected = false;
                            _session.Record(
                                item,
                                permanently ? OperationMethod.Delete : OperationMethod.Recycle,
                                true,
                                null);

                            succeeded++;
                            succeededBytes += item.SizeBytes;
                            UntrackItem(item);
                            _items.Remove(item);
                        }
                        else
                        {
                            failed++;
                            item.Status = ItemStatus.Failed;
                            item.StatusDetail = outcome.Error;
                            _session.Record(
                                item,
                                recycle ? OperationMethod.Recycle : OperationMethod.Delete,
                                false,
                                outcome.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        item.Status = ItemStatus.Failed;
                        item.StatusDetail = ex.Message;
                        _session.Record(
                            item,
                            recycle ? OperationMethod.Recycle : OperationMethod.Delete,
                            false,
                            ex.Message);
                    }
                }

                SetStatus(
                    recycle ? "Msg.BulkRecycleDone" : "Msg.BulkDeleteDone",
                    Format.Count(succeeded),
                    Format.Size(succeededBytes),
                    Format.Count(failed));
            }
            finally
            {
                IsBusy = false;
                if (SelectedItem != null && !_items.Contains(SelectedItem))
                {
                    SelectedItem = _items.FirstOrDefault(i => i.IsActionable);
                }

                RefreshDrive();
                RefreshCounters();
                RefreshList();
                RefreshCommands();
                RaiseSelectionDetails();
            }
        }

        private void ClearSelection()
        {
            _bulkSelecting = true;
            try
            {
                foreach (ScanItem item in _items)
                {
                    item.IsSelected = false;
                }
            }
            finally
            {
                _bulkSelecting = false;
            }

            RefreshSelectionSummary();
        }

        private void SkipSelected()
        {
            ScanItem item = SelectedItem;
            if (item == null || !CanSkipSelected)
            {
                return;
            }

            item.Status = ItemStatus.Skipped;
            _session.Record(item, OperationMethod.Skip, true, null);
            SetStatus("Msg.SkipDone", item.Name);
            SelectedItem = _items.FirstOrDefault(i => i.IsActionable);
            RefreshCounters();
            RefreshList();
            RefreshCommands();
            RaiseSelectionDetails();
        }

        private void OpenLocation()
        {
            ScanItem item = SelectedItem;
            if (item == null)
            {
                return;
            }

            try
            {
                string arguments = item.Kind == ItemKind.Group
                    ? "\"" + item.Path + "\""
                    : "/select,\"" + item.Path + "\"";
                Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
            }
            catch
            {
                // Explorer can refuse unusual paths; the details remain visible in the UI.
            }
        }

        private void CopyPath()
        {
            if (SelectedItem == null)
            {
                return;
            }

            try
            {
                Clipboard.SetText(SelectedItem.Path);
                SetStatus("Msg.PathCopied");
            }
            catch
            {
                SetStatus("Msg.CopyFailed");
            }
        }

        private void Elevate()
        {
            try
            {
                string executable = Assembly.GetEntryAssembly().Location;
                Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = true,
                    Verb = "runas"
                });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                SetStatus("Msg.ElevationFailed", ex.Message);
            }
        }

        private void ShowReport()
        {
            if (_lastScan == null && _session == null)
            {
                SetStatus("Msg.NoReport");
                return;
            }

            RefreshDrive();
            var pending = _items.Where(i => i.Status == ItemStatus.Pending).ToList();
            ReportData data = ReportBuilder.Build(
                _session,
                _lastScan,
                pending,
                DriveSnapshot.Capture(@"C:\"));

            var window = new ReportWindow(data)
            {
                Owner = Application.Current != null ? Application.Current.MainWindow : null
            };
            window.ShowDialog();
        }

        private void ToggleLanguage()
        {
            Loc.SetLanguage(Loc.CurrentLanguage == Loc.Arabic ? Loc.English : Loc.Arabic);
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            OnPropertyChanged("LanguageButtonText");
            OnPropertyChanged("AdminStatusText");
            OnPropertyChanged("ScanHintText");
            OnPropertyChanged("PhaseText");
            OnPropertyChanged("StatusText");
            OnPropertyChanged("PendingSummaryText");
            OnPropertyChanged("SelectedSummaryText");
            OnPropertyChanged("FreeOfText");
            OnPropertyChanged("GroupContentsText");
            OnPropertyChanged("LiveCandidateBytesText");
            OnPropertyChanged("RecoveredText");
            OnPropertyChanged("RecycledText");

            foreach (ScanItem item in _items)
            {
                item.RefreshLocalizedText();
            }

            foreach (CategoryFilterOption option in CategoryOptions)
            {
                option.RefreshLocalizedText();
            }
        }

        private bool FilterItem(object value)
        {
            var item = value as ScanItem;
            if (item == null)
            {
                return false;
            }

            if (HideSkipped && item.Status == ItemStatus.Skipped)
            {
                return false;
            }

            if (SelectedCategoryOption != null &&
                SelectedCategoryOption.Category.HasValue &&
                item.Category != SelectedCategoryOption.Category.Value)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string needle = SearchText.Trim();
                if (item.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0 &&
                    item.Path.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private void RefreshList()
        {
            _itemsView.Refresh();
            VisibleItems = _itemsView.Cast<object>().Count();
            OnPropertyChanged("HasVisibleItems");
            OnPropertyChanged("ShowEmpty");
            OnPropertyChanged("SelectAllVisible");
            OnPropertyChanged("SelectedCount");
            OnPropertyChanged("SelectedBytes");
            OnPropertyChanged("HasSelectedItems");
            OnPropertyChanged("SelectedSummaryText");
            OnPropertyChanged("CanDeleteSelectedItems");
            RefreshCommands();
        }

        private List<ScanItem> VisibleSelectableItems()
        {
            return _itemsView.Cast<ScanItem>().Where(i => i.CanBeDeleted).ToList();
        }

        private List<ScanItem> SelectedDeletableItems()
        {
            return _itemsView.Cast<ScanItem>()
                .Where(i => i.IsSelected && i.CanBeDeleted && PathSafety.IsDeletable(i))
                .ToList();
        }

        private void RefreshSelectionSummary()
        {
            OnPropertyChanged("SelectedCount");
            OnPropertyChanged("SelectedBytes");
            OnPropertyChanged("HasSelectedItems");
            OnPropertyChanged("SelectedSummaryText");
            OnPropertyChanged("CanDeleteSelectedItems");
            OnPropertyChanged("SelectAllVisible");
            RefreshCommands();
        }

        private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_bulkSelecting)
            {
                return;
            }

            if (e.PropertyName == "IsSelected" || e.PropertyName == "Status" || e.PropertyName == "CanBeDeleted")
            {
                RefreshSelectionSummary();
            }
        }

        private void TrackItem(ScanItem item)
        {
            item.PropertyChanged += OnItemPropertyChanged;
        }

        private void UntrackItem(ScanItem item)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        private void RefreshCounters()
        {
            var pending = _items.Where(i => i.Status == ItemStatus.Pending).ToList();
            PendingCount = pending.Count;
            PendingBytes = pending.Where(i => i.SizeBytes > 0).Sum(i => i.SizeBytes);

            foreach (CategoryFilterOption option in CategoryOptions)
            {
                if (!option.Category.HasValue)
                {
                    option.Count = pending.Count;
                    option.SizeBytes = pending.Where(i => i.SizeBytes > 0).Sum(i => i.SizeBytes);
                }
                else
                {
                    var categoryItems = pending.Where(i => i.Category == option.Category.Value).ToList();
                    option.Count = categoryItems.Count;
                    option.SizeBytes = categoryItems.Where(i => i.SizeBytes > 0).Sum(i => i.SizeBytes);
                }
            }

            OnPropertyChanged("PendingCount");
            OnPropertyChanged("PendingBytes");
            OnPropertyChanged("SessionDeletedCount");
            OnPropertyChanged("SessionRecycledCount");
            OnPropertyChanged("SessionSkippedCount");
            OnPropertyChanged("SessionFailedCount");
            OnPropertyChanged("RecoveredText");
            OnPropertyChanged("RecycledText");
            OnPropertyChanged("CanReport");
        }

        private void RaiseProgressProperties()
        {
            OnPropertyChanged("PhaseText");
            OnPropertyChanged("CurrentPath");
            OnPropertyChanged("FilesScanned");
            OnPropertyChanged("DirectoriesScanned");
            OnPropertyChanged("InaccessibleDirectories");
            OnPropertyChanged("LiveCandidateCount");
            OnPropertyChanged("LiveCandidateBytes");
            OnPropertyChanged("LiveCandidateBytesText");
            OnPropertyChanged("StatusText");
        }

        private void RaiseSelectionDetails()
        {
            OnPropertyChanged("HasSelection");
            OnPropertyChanged("CanDeleteSelected");
            OnPropertyChanged("CanSkipSelected");
            OnPropertyChanged("ShowProtectedNote");
            OnPropertyChanged("ShowFolderNote");
            OnPropertyChanged("ShowGroupNote");
            OnPropertyChanged("ShowAdminNote");
            OnPropertyChanged("ShowFailureDetail");
            OnPropertyChanged("GroupContentsText");
        }

        private void SetStatus(string key, params object[] args)
        {
            _statusKey = key;
            _statusArgs = args;
            OnPropertyChanged("StatusText");
        }

        private void RefreshDrive()
        {
            DriveSnapshot snapshot = DriveSnapshot.Capture(@"C:\");
            if (snapshot.TotalBytes > 0)
            {
                _freeBytes = snapshot.FreeBytes;
                _totalBytes = snapshot.TotalBytes;
                _driveName = snapshot.Name;
            }

            OnPropertyChanged("FreeSpaceText");
            OnPropertyChanged("TotalSpaceText");
            OnPropertyChanged("UsedSpaceText");
            OnPropertyChanged("FreeOfText");
            OnPropertyChanged("UsedPercent");
            OnPropertyChanged("FreePercent");
            OnPropertyChanged("DriveName");
        }

        private void RefreshCommands()
        {
            StartScanCommand.RaiseCanExecuteChanged();
            CancelScanCommand.RaiseCanExecuteChanged();
            DeleteRecycleCommand.RaiseCanExecuteChanged();
            DeletePermanentCommand.RaiseCanExecuteChanged();
            DeleteSelectedCommand.RaiseCanExecuteChanged();
            ClearSelectionCommand.RaiseCanExecuteChanged();
            SkipCommand.RaiseCanExecuteChanged();
            OpenLocationCommand.RaiseCanExecuteChanged();
            CopyPathCommand.RaiseCanExecuteChanged();
            ElevateCommand.RaiseCanExecuteChanged();
            ShowReportCommand.RaiseCanExecuteChanged();
        }

        private static bool CheckElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The .NET Framework reads its long-path switches from "&lt;app&gt;.exe.config" before any
        /// code of ours runs, so that file is the only place they can come from. Without it the
        /// framework keeps the legacy behaviour and paths longer than 260 characters are skipped.
        /// </summary>
        private static bool DetectSidecarConfig()
        {
            try
            {
                string location = Assembly.GetEntryAssembly().Location;
                return !string.IsNullOrEmpty(location) && File.Exists(location + ".config");
            }
            catch
            {
                return true;
            }
        }

        public void Dispose()
        {
            Loc.LanguageChanged -= OnLanguageChanged;
            _driveTimer.Stop();
            if (_scanCancellation != null)
            {
                _scanCancellation.Cancel();
                _scanCancellation.Dispose();
                _scanCancellation = null;
            }
        }
    }
}
