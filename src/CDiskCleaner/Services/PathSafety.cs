using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CDiskCleaner.Models;

namespace CDiskCleaner.Services
{
    /// <summary>
    /// The last line of defence before anything is deleted. It is intentionally conservative.
    /// </summary>
    public static class PathSafety
    {
        private static readonly string[] BlockedExactPaths =
        {
            @"C:\",
            @"C:\Windows",
            @"C:\Windows\System32",
            @"C:\Windows\SysWOW64",
            @"C:\Windows\WinSxS",
            @"C:\Windows\Installer",
            @"C:\Windows.old",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            @"C:\ProgramData",
            @"C:\Users",
            @"C:\Users\Public",
            @"C:\System Volume Information",
            @"C:\$Recycle.Bin",
            @"C:\Recovery",
            @"C:\PerfLogs",
            @"C:\Config.Msi"
        };

        private static readonly string[] BlockedFileNames =
        {
            "pagefile.sys",
            "hiberfil.sys",
            "swapfile.sys",
            "bootmgr",
            "bootnxt",
            "ntldr",
            "boot.ini"
        };

        private static readonly string[] AllowedWindowsRoots =
        {
            @"C:\Windows\Temp",
            @"C:\Windows\SystemTemp",
            @"C:\Windows\Logs",
            @"C:\Windows\Panther",
            @"C:\Windows\Prefetch",
            @"C:\Windows\Minidump",
            @"C:\Windows\LiveKernelReports",
            @"C:\Windows\MEMORY.DMP",
            @"C:\Windows\SoftwareDistribution\Download",
            @"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization"
        };

        private static readonly string[] ProtectedUserFolders =
        {
            @"C:\Users\Public\Desktop",
            @"C:\Users\Public\Documents",
            @"C:\Users\Public\Downloads"
        };

        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                string full = Path.GetFullPath(path);
                string root = Path.GetPathRoot(full);
                if (!string.IsNullOrEmpty(root) &&
                    full.Length > root.Length &&
                    (full.EndsWith("\\", StringComparison.Ordinal) || full.EndsWith("/", StringComparison.Ordinal)))
                {
                    full = full.TrimEnd('\\', '/');
                }

                return full;
            }
            catch
            {
                return path.Trim();
            }
        }

        public static bool IsUnderOrEqual(string path, string root)
        {
            string p = Normalize(path);
            string r = Normalize(root);
            if (string.IsNullOrEmpty(p) || string.IsNullOrEmpty(r))
            {
                return false;
            }

            if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!r.EndsWith("\\", StringComparison.Ordinal))
            {
                r += "\\";
            }

            return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }

        public static string ToExtended(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return path;
            }

            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return @"\\?\UNC\" + path.Substring(2);
            }

            if (path.Length >= 240 && Path.IsPathRooted(path))
            {
                return @"\\?\" + path;
            }

            return path;
        }

        public static bool IsDeletable(ScanItem item)
        {
            if (item == null || item.Category == ScanCategory.C ||
                (item.Status != ItemStatus.Pending && item.Status != ItemStatus.Failed))
            {
                return false;
            }

            if (item.GroupPaths != null && item.GroupPaths.Count > 0)
            {
                foreach (string path in item.GroupPaths)
                {
                    if (!IsSafeTarget(path, item))
                    {
                        return false;
                    }
                }

                return item.GroupPaths.Count > 0;
            }

            return IsSafeTarget(item.Path, item);
        }

        private static bool IsSafeTarget(string path, ScanItem item)
        {
            string full = Normalize(path);
            if (string.IsNullOrEmpty(full) || !Path.IsPathRooted(full))
            {
                return false;
            }

            string root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root) || string.Equals(full, Normalize(root), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (string blocked in BlockedExactPaths)
            {
                if (string.Equals(full, Normalize(blocked), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            foreach (string protectedFolder in ProtectedUserFolders)
            {
                if (string.Equals(full, Normalize(protectedFolder), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            string fileName = Path.GetFileName(full);
            if (BlockedFileNames.Any(n => string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // A user profile root, and the generic profile containers, are never targets themselves.
            if (IsProfileContainer(full))
            {
                return false;
            }

            if (IsUnderOrEqual(full, @"C:\Windows"))
            {
                bool allowed = AllowedWindowsRoots.Any(r => IsUnderOrEqual(full, r));
                if (!allowed)
                {
                    return false;
                }
            }

            if (IsUnderOrEqual(full, @"C:\Program Files") || IsUnderOrEqual(full, @"C:\Program Files (x86)"))
            {
                if (!string.Equals(item.SourceTag, "ProgramLeftover", StringComparison.Ordinal))
                {
                    return false;
                }

                string parent = Path.GetDirectoryName(full);
                string programFiles = IsUnderOrEqual(full, @"C:\Program Files")
                    ? @"C:\Program Files"
                    : @"C:\Program Files (x86)";
                if (!string.Equals(Normalize(parent), Normalize(programFiles), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (IsUnderOrEqual(full, @"C:\ProgramData"))
            {
                if (string.Equals(full, Normalize(@"C:\ProgramData"), StringComparison.OrdinalIgnoreCase) ||
                    IsUnderOrEqual(full, @"C:\ProgramData\Package Cache"))
                {
                    return false;
                }
            }

            if (IsUnderOrEqual(full, @"C:\System Volume Information") ||
                IsUnderOrEqual(full, @"C:\$Recycle.Bin") ||
                IsUnderOrEqual(full, @"C:\Recovery") ||
                IsUnderOrEqual(full, @"C:\Config.Msi") ||
                IsUnderOrEqual(full, @"C:\Windows.old"))
            {
                return false;
            }

            // Reparse points are skipped by the scanner. If one reaches here, deleting the link is
            // acceptable, but never follow it. The deletion service handles that distinction.
            return true;
        }

        private static bool IsProfileContainer(string full)
        {
            if (IsUnderOrEqual(full, @"C:\Users"))
            {
                string relative = Normalize(full).Substring(Normalize(@"C:\Users").Length).TrimStart('\\');
                if (relative.Length == 0)
                {
                    return true;
                }

                string[] parts = relative.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1)
                {
                    return true;
                }

                if (parts.Length == 2 &&
                    (string.Equals(parts[1], "AppData", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(parts[1], "Desktop", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(parts[1], "Documents", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(parts[1], "Downloads", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(parts[1], "Pictures", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(parts[1], "Videos", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(parts[1], "Music", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                if (parts.Length == 3 &&
                    string.Equals(parts[1], "AppData", StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(parts[2], "Local", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(parts[2], "Roaming", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(parts[2], "LocalLow", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
