using System;
using System.Collections.Generic;
using System.Linq;

namespace CDiskCleaner.Models
{
    public sealed class SessionState
    {
        public SessionState(DriveSnapshot startSnapshot)
        {
            StartSnapshot = startSnapshot ?? new DriveSnapshot(string.Empty, 0, 0);
            StartedUtc = DateTime.UtcNow;
            Operations = new List<OperationRecord>();
        }

        public DateTime StartedUtc { get; private set; }
        public DriveSnapshot StartSnapshot { get; private set; }
        public List<OperationRecord> Operations { get; private set; }

        public void Record(ScanItem item, OperationMethod method, bool success, string error)
        {
            if (item == null)
            {
                return;
            }

            Operations.Add(new OperationRecord
            {
                ItemId = item.Id,
                Name = item.Name,
                Path = item.Path,
                SizeBytes = item.SizeBytes,
                Method = method,
                Success = success,
                Error = error,
                TimestampUtc = DateTime.UtcNow
            });
        }

        public List<OperationRecord> GetLatestRecords()
        {
            var latest = new Dictionary<Guid, OperationRecord>();
            foreach (OperationRecord record in Operations)
            {
                latest[record.ItemId] = record;
            }

            return latest.Values
                .OrderBy(r => r.TimestampUtc)
                .ToList();
        }

        public int DeletedCount
        {
            get { return GetLatestRecords().Count(r => r.Success && r.Method == OperationMethod.Delete); }
        }

        public long DeletedBytes
        {
            get
            {
                return GetLatestRecords()
                    .Where(r => r.Success && r.Method == OperationMethod.Delete)
                    .Sum(r => Math.Max(0, r.SizeBytes));
            }
        }

        public int RecycledCount
        {
            get { return GetLatestRecords().Count(r => r.Success && r.Method == OperationMethod.Recycle); }
        }

        public long RecycledBytes
        {
            get
            {
                return GetLatestRecords()
                    .Where(r => r.Success && r.Method == OperationMethod.Recycle)
                    .Sum(r => Math.Max(0, r.SizeBytes));
            }
        }

        public int SkippedCount
        {
            get { return GetLatestRecords().Count(r => r.Success && r.Method == OperationMethod.Skip); }
        }

        public long SkippedBytes
        {
            get
            {
                return GetLatestRecords()
                    .Where(r => r.Success && r.Method == OperationMethod.Skip)
                    .Sum(r => Math.Max(0, r.SizeBytes));
            }
        }

        public int FailedCount
        {
            get { return GetLatestRecords().Count(r => !r.Success); }
        }

        public long FailedBytes
        {
            get
            {
                return GetLatestRecords()
                    .Where(r => !r.Success)
                    .Sum(r => Math.Max(0, r.SizeBytes));
            }
        }
    }
}
