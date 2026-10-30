using System;
using System.Collections.Generic;
using System.Linq;

namespace CDiskCleaner.Models
{
    public sealed class ScanResult
    {
        public ScanResult()
        {
            Items = new List<ScanItem>();
            StartedUtc = DateTime.UtcNow;
            CompletedUtc = DateTime.UtcNow;
        }

        public List<ScanItem> Items { get; set; }
        public ScanOptions Options { get; set; }
        public DriveSnapshot Snapshot { get; set; }

        public DateTime StartedUtc { get; set; }
        public DateTime CompletedUtc { get; set; }
        public bool Cancelled { get; set; }

        public long FilesScanned { get; set; }
        public long DirectoriesScanned { get; set; }
        public int InaccessibleDirectories { get; set; }

        public long DuplicateHashedBytes { get; set; }
        public int DuplicateGroupCount { get; set; }
        public bool DuplicateCapReached { get; set; }
        public bool DuplicatePoolTruncated { get; set; }
        public string UnexpectedError { get; set; }

        public int CandidateCount
        {
            get { return Items == null ? 0 : Items.Count; }
        }

        public long CandidateBytes
        {
            get { return Items == null ? 0 : Items.Where(i => i.SizeBytes > 0).Sum(i => i.SizeBytes); }
        }

        public int PendingCount
        {
            get { return Items == null ? 0 : Items.Count(i => i.Status == ItemStatus.Pending); }
        }

        public TimeSpan Duration
        {
            get { return CompletedUtc - StartedUtc; }
        }
    }
}
