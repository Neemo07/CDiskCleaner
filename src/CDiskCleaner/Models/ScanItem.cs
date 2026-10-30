using System;
using System.Collections.Generic;
using CDiskCleaner.Services;
using CDiskCleaner.ViewModels;

namespace CDiskCleaner.Models
{
    public sealed class ScanItem : ObservableObject
    {
        private string _path = string.Empty;
        private string _name = string.Empty;
        private string _nameKey;
        private long _sizeBytes;
        private int _fileCount;
        private DateTime? _lastModified;
        private ScanCategory _category;
        private SafetyRating _safety;
        private string _reasonKey = string.Empty;
        private object[] _reasonArgs;
        private string _typeKey = string.Empty;
        private string _recoverabilityKey = string.Empty;
        private bool _recommendDelete;
        private bool _requiresAdmin;
        private ItemStatus _status = ItemStatus.Pending;
        private string _statusDetail;
        private ItemKind _kind;
        private string _sourceTag = string.Empty;
        private bool _isSelected;

        public ScanItem()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; private set; }

        public string Path
        {
            get { return _path; }
            set { if (SetProperty(ref _path, value)) { OnPropertyChanged("DisplayPath"); } }
        }

        public string Name
        {
            get { return string.IsNullOrEmpty(_nameKey) ? _name : Loc.T(_nameKey); }
            set { SetProperty(ref _name, value); }
        }

        public string NameKey
        {
            get { return _nameKey; }
            set { if (SetProperty(ref _nameKey, value)) { OnPropertyChanged("Name"); } }
        }

        public ItemKind Kind
        {
            get { return _kind; }
            set { if (SetProperty(ref _kind, value)) { OnPropertyChanged("KindText"); } }
        }

        public long SizeBytes
        {
            get { return _sizeBytes; }
            set { if (SetProperty(ref _sizeBytes, value)) { OnPropertyChanged("SizeText"); } }
        }

        public int FileCount
        {
            get { return _fileCount; }
            set { if (SetProperty(ref _fileCount, value)) { OnPropertyChanged("FileCountText"); } }
        }

        public DateTime? LastModified
        {
            get { return _lastModified; }
            set { if (SetProperty(ref _lastModified, value)) { OnPropertyChanged("LastModifiedText"); } }
        }

        public ScanCategory Category
        {
            get { return _category; }
            set
            {
                if (SetProperty(ref _category, value))
                {
                    OnPropertyChanged("CategoryText");
                    OnPropertyChanged("IsProtected");
                }
            }
        }

        public SafetyRating Safety
        {
            get { return _safety; }
            set { if (SetProperty(ref _safety, value)) { OnPropertyChanged("SafetyText"); } }
        }

        public string ReasonKey
        {
            get { return _reasonKey; }
            set { if (SetProperty(ref _reasonKey, value)) { OnPropertyChanged("Reason"); } }
        }

        public object[] ReasonArgs
        {
            get { return _reasonArgs; }
            set { if (SetProperty(ref _reasonArgs, value)) { OnPropertyChanged("Reason"); } }
        }

        public string TypeKey
        {
            get { return _typeKey; }
            set { if (SetProperty(ref _typeKey, value)) { OnPropertyChanged("TypeText"); } }
        }

        public string RecoverabilityKey
        {
            get { return _recoverabilityKey; }
            set { if (SetProperty(ref _recoverabilityKey, value)) { OnPropertyChanged("RecoverabilityText"); } }
        }

        public bool RecommendDelete
        {
            get { return _recommendDelete; }
            set { if (SetProperty(ref _recommendDelete, value)) { OnPropertyChanged("RecommendText"); } }
        }

        public bool RequiresAdmin
        {
            get { return _requiresAdmin; }
            set { if (SetProperty(ref _requiresAdmin, value)) { OnPropertyChanged("AdminText"); } }
        }

        public ItemStatus Status
        {
            get { return _status; }
            set
            {
                if (SetProperty(ref _status, value))
                {
                    OnPropertyChanged("StatusText");
                    OnPropertyChanged("IsActionable");
                    OnPropertyChanged("CanBeDeleted");
                }
            }
        }

        public string StatusDetail
        {
            get { return _statusDetail; }
            set { SetProperty(ref _statusDetail, value); }
        }

        public string SourceTag
        {
            get { return _sourceTag; }
            set { SetProperty(ref _sourceTag, value); }
        }

        /// <summary>
        /// Ticked in the candidate list. Selection is a UI-only state; nothing is deleted
        /// until the user confirms the bulk dialog.
        /// </summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

        public List<string> GroupPaths { get; set; }

        public bool IsProtected
        {
            get { return Category == ScanCategory.C; }
        }

        public bool IsActionable
        {
            get { return Status == ItemStatus.Pending && Category != ScanCategory.C; }
        }

        public bool CanBeDeleted
        {
            get
            {
                return (Status == ItemStatus.Pending || Status == ItemStatus.Failed) &&
                       Category != ScanCategory.C;
            }
        }

        public string DisplayPath
        {
            get { return Path; }
        }

        public string SizeText
        {
            get { return Format.Size(SizeBytes); }
        }

        public string LastModifiedText
        {
            get { return Format.Date(LastModified); }
        }

        public string FileCountText
        {
            get
            {
                return FileCount > 0
                    ? Loc.T("Details.FileCountValue", Format.Count(FileCount))
                    : "—";
            }
        }

        public string CategoryText
        {
            get { return Category.ToString(); }
        }

        public string SafetyText
        {
            get { return Loc.T("Safety." + Safety); }
        }

        public string TypeText
        {
            get { return Loc.T(TypeKey); }
        }

        public string Reason
        {
            get { return string.IsNullOrEmpty(ReasonKey) ? string.Empty : Loc.T(ReasonKey, ReasonArgs); }
        }

        public string RecoverabilityText
        {
            get { return Loc.T(RecoverabilityKey); }
        }

        public string RecommendText
        {
            get { return Loc.T(RecommendDelete ? "Details.RecommendYes" : "Details.RecommendNo"); }
        }

        public string AdminText
        {
            get { return RequiresAdmin ? Loc.T("Details.AdminRequired") : Loc.T("Details.AdminNotRequired"); }
        }

        public string StatusText
        {
            get { return Loc.T("Status." + Status); }
        }

        public IList<string> GetTargetPaths()
        {
            if (GroupPaths != null && GroupPaths.Count > 0)
            {
                return GroupPaths;
            }

            return new List<string> { Path };
        }

        public void RefreshLocalizedText()
        {
            RaiseAllPropertiesChanged();
        }
    }
}
