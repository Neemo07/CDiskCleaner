using CDiskCleaner.Models;
using CDiskCleaner.Services;

namespace CDiskCleaner.ViewModels
{
    public sealed class CategoryFilterOption : ObservableObject
    {
        private int _count;
        private long _sizeBytes;
        private readonly string _titleKey;

        public CategoryFilterOption(ScanCategory? category, string letter, string titleKey)
        {
            Category = category;
            Letter = letter;
            _titleKey = titleKey;
        }

        public ScanCategory? Category { get; private set; }
        public string Letter { get; private set; }

        public string Title
        {
            get { return Loc.T(_titleKey); }
        }

        public int Count
        {
            get { return _count; }
            set
            {
                if (SetProperty(ref _count, value))
                {
                    OnPropertyChanged("CountText");
                }
            }
        }

        public long SizeBytes
        {
            get { return _sizeBytes; }
            set
            {
                if (SetProperty(ref _sizeBytes, value))
                {
                    OnPropertyChanged("SizeText");
                }
            }
        }

        public string CountText
        {
            get { return Format.Count(Count); }
        }

        public string SizeText
        {
            get { return Format.Size(SizeBytes); }
        }

        public void RefreshLocalizedText()
        {
            OnPropertyChanged("Title");
        }
    }
}
