namespace CDiskCleaner.Models
{
    public sealed class ScanOptions
    {
        public string ScanRoot { get; set; } = @"C:\";
        public bool IncludeSystemCleanupLocations { get; set; } = true;
        public bool FullScan { get; set; } = true;
        public bool FindDuplicates { get; set; }

        public int TempAgeDays { get; set; } = 7;
        public int LogAgeDays { get; set; } = 30;
        public int InstallerAgeDays { get; set; } = 30;

        public long MinItemSizeBytes { get; set; } = 1024L * 1024;
        public long GroupMinSizeBytes { get; set; } = 20L * 1024 * 1024;
        public long LogMinSizeBytes { get; set; } = 10L * 1024 * 1024;
        public long InstallerMinSizeBytes { get; set; } = 50L * 1024 * 1024;
        public long LargeFileMinBytes { get; set; } = 1024L * 1024 * 1024;
        public long DuplicateMinBytes { get; set; } = 50L * 1024 * 1024;
        public long DuplicateHashCapBytes { get; set; } = 20L * 1024 * 1024 * 1024;
        public int DuplicateMaxFiles { get; set; } = 6000;

        /// <summary>
        /// True when the scan root is a drive root (C:\), which is when the Windows specific
        /// locations, protected items and installed-program leftovers have to be scanned.
        /// A folder scan (any sub folder, even on C:) uses the ordinary user-data rules.
        /// </summary>
        public bool IsSystemDriveScan
        {
            get
            {
                try
                {
                    string full = System.IO.Path.GetFullPath(ScanRoot);
                    string root = System.IO.Path.GetPathRoot(full);
                    return !string.IsNullOrEmpty(root) &&
                           string.Equals(full, root, System.StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
