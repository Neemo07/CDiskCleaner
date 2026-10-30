using System;
using System.IO;

namespace CDiskCleaner.Models
{
    public sealed class DriveSnapshot
    {
        public DriveSnapshot(string name, long totalBytes, long freeBytes)
        {
            Name = name ?? string.Empty;
            TotalBytes = totalBytes;
            FreeBytes = freeBytes;
        }

        public string Name { get; private set; }
        public long TotalBytes { get; private set; }
        public long FreeBytes { get; private set; }

        public long UsedBytes
        {
            get { return Math.Max(0, TotalBytes - FreeBytes); }
        }

        public double FreePercent
        {
            get { return TotalBytes <= 0 ? 0 : (100.0 * FreeBytes / TotalBytes); }
        }

        public long FreeDelta(DriveSnapshot later)
        {
            return later == null ? 0 : later.FreeBytes - FreeBytes;
        }

        public static DriveSnapshot Capture(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string root = Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(root))
                {
                    return new DriveSnapshot(full, 0, 0);
                }

                var info = new DriveInfo(root);
                string name = info.Name;
                try
                {
                    if (!string.IsNullOrWhiteSpace(info.VolumeLabel))
                    {
                        name = info.Name + " " + info.VolumeLabel;
                    }
                }
                catch
                {
                    // Volume label is optional.
                }

                return new DriveSnapshot(name, info.TotalSize, info.AvailableFreeSpace);
            }
            catch
            {
                return new DriveSnapshot(path, 0, 0);
            }
        }
    }
}
