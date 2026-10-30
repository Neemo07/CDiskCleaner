namespace CDiskCleaner.Models
{
    public enum ScanCategory
    {
        A = 0,
        B = 1,
        C = 2,
        D = 3
    }

    public enum SafetyRating
    {
        Safe = 0,
        Caution = 1,
        Risky = 2,
        Unknown = 3,
        Protected = 4
    }

    public enum ItemKind
    {
        File = 0,
        Folder = 1,
        Group = 2
    }

    public enum ItemStatus
    {
        Pending = 0,
        Skipped = 1,
        Recycled = 2,
        Deleted = 3,
        Failed = 4
    }

    public enum OperationMethod
    {
        Recycle = 0,
        Delete = 1,
        Skip = 2
    }
}
