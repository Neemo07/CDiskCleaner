using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using CDiskCleaner.Models;

namespace CDiskCleaner.Services
{
    public sealed class DeletionService
    {
        private const uint FoDelete = 3;
        private const ushort FofSilent = 0x0004;
        private const ushort FofNoConfirmation = 0x0010;
        private const ushort FofAllowUndo = 0x0040;
        private const ushort FofNoConfirmMkDir = 0x0200;
        private const ushort FofNoErrorUi = 0x0400;
        private const ushort FofWantNukeWarning = 0x4000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShFileOpStruct
        {
            public IntPtr hwnd;
            public uint wFunc;
            public IntPtr pFrom;
            public IntPtr pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public IntPtr lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref ShFileOpStruct fileOp);

        public DeletionOutcome Execute(ScanItem item, bool recycle)
        {
            var outcome = new DeletionOutcome
            {
                RecycleRequested = recycle,
                Bytes = item == null ? 0 : Math.Max(0, item.SizeBytes)
            };

            if (item == null)
            {
                outcome.Error = Loc.T("Error.NotFound");
                return outcome;
            }

            if (!PathSafety.IsDeletable(item))
            {
                outcome.Error = Loc.T("Error.NotAllowed");
                return outcome;
            }

            var targets = new List<string>();
            foreach (string path in item.GetTargetPaths())
            {
                string normalized = PathSafety.Normalize(path);
                if (!string.IsNullOrEmpty(normalized) &&
                    !targets.Contains(normalized, StringComparer.OrdinalIgnoreCase) &&
                    Exists(normalized))
                {
                    targets.Add(normalized);
                }
            }

            outcome.TotalTargets = targets.Count;
            if (targets.Count == 0)
            {
                outcome.Error = Loc.T("Error.NotFound");
                return outcome;
            }

            DriveSnapshot before = DriveSnapshot.Capture(item.Path);
            outcome.FreeBefore = before.FreeBytes;

            if (recycle)
            {
                if (targets.Any(p => p.Length >= 260))
                {
                    outcome.Error = Loc.T("Error.LongPathRecycle");
                    return outcome;
                }

                int code = RecyclePaths(targets);
                if (code != 0)
                {
                    outcome.Error = Loc.T("Error.RecycleFailed", "0x" + code.ToString("X"));
                    outcome.FreeAfter = DriveSnapshot.Capture(item.Path).FreeBytes;
                    return outcome;
                }

                outcome.Success = true;
                outcome.SucceededTargets = targets.Count;
            }
            else
            {
                var errors = new List<string>();
                int succeeded = 0;
                foreach (string target in targets)
                {
                    try
                    {
                        if (Directory.Exists(PathSafety.ToExtended(target)))
                        {
                            DeleteDirectoryTree(target);
                        }
                        else if (File.Exists(PathSafety.ToExtended(target)))
                        {
                            DeleteFile(target);
                        }
                        else
                        {
                            // It vanished after the scan; treat it as already handled.
                        }

                        succeeded++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(Describe(ex));
                    }
                }

                outcome.SucceededTargets = succeeded;
                if (errors.Count > 0)
                {
                    outcome.Error = errors.Count == 1
                        ? errors[0]
                        : Loc.T("Error.PartialFailure", succeeded, targets.Count, errors[0]);
                    outcome.FreeAfter = DriveSnapshot.Capture(item.Path).FreeBytes;
                    return outcome;
                }

                outcome.Success = true;
            }

            // Let Windows update the free-space counters before sampling them.
            Thread.Sleep(350);
            DriveSnapshot after = DriveSnapshot.Capture(item.Path);
            outcome.FreeAfter = after.FreeBytes;

            if (recycle && item.SizeBytes >= 1024L * 1024 &&
                outcome.FreeDelta >= item.SizeBytes / 2)
            {
                // Recycle Bin should not free space unless the item was too large for it.
                outcome.LikelyPermanentlyDeleted = true;
            }

            return outcome;
        }

        private static int RecyclePaths(IList<string> paths)
        {
            var buffer = new System.Text.StringBuilder();
            foreach (string path in paths)
            {
                buffer.Append(path);
                buffer.Append('\0');
            }

            buffer.Append('\0');

            IntPtr from = Marshal.StringToHGlobalUni(buffer.ToString());
            try
            {
                var op = new ShFileOpStruct
                {
                    hwnd = IntPtr.Zero,
                    wFunc = FoDelete,
                    pFrom = from,
                    pTo = IntPtr.Zero,
                    fFlags = (ushort)(FofSilent | FofNoConfirmation | FofAllowUndo |
                                      FofNoConfirmMkDir | FofNoErrorUi | FofWantNukeWarning),
                    fAnyOperationsAborted = false,
                    hNameMappings = IntPtr.Zero,
                    lpszProgressTitle = IntPtr.Zero
                };

                int result = SHFileOperation(ref op);
                if (result == 0 && op.fAnyOperationsAborted)
                {
                    return 1223; // ERROR_CANCELLED.
                }

                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(from);
            }
        }

        private static bool Exists(string path)
        {
            string extended = PathSafety.ToExtended(path);
            return File.Exists(extended) || Directory.Exists(extended);
        }

        private static void DeleteFile(string path)
        {
            string extended = PathSafety.ToExtended(path);
            ClearReadOnly(extended);
            File.Delete(extended);
        }

        private static void DeleteDirectoryTree(string path)
        {
            string extended = PathSafety.ToExtended(path);
            var directory = new DirectoryInfo(extended);
            FileAttributes rootAttributes = directory.Attributes;
            if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
            {
                ClearReadOnly(extended);
                Directory.Delete(extended, false);
                return;
            }

            foreach (FileSystemInfo entry in directory.GetFileSystemInfos())
            {
                string child = entry.FullName;
                FileAttributes attributes = entry.Attributes;
                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                bool isLink = (attributes & FileAttributes.ReparsePoint) != 0;

                if (isDirectory && !isLink)
                {
                    DeleteDirectoryTree(child);
                }
                else if (isDirectory)
                {
                    ClearReadOnly(child);
                    Directory.Delete(child, false);
                }
                else
                {
                    ClearReadOnly(child);
                    File.Delete(child);
                }
            }

            ClearReadOnly(extended);
            Directory.Delete(extended, false);
        }

        private static void ClearReadOnly(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                }
            }
            catch
            {
                // The delete call below will report the real problem.
            }
        }

        private static string Describe(Exception ex)
        {
            if (ex is UnauthorizedAccessException)
            {
                return Loc.T("Error.AccessDenied");
            }

            if (ex is PathTooLongException)
            {
                return Loc.T("Error.PathTooLong");
            }

            if (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                return Loc.T("Error.NotFound");
            }

            if (ex is IOException)
            {
                int win32 = ex.HResult & 0xFFFF;
                if (win32 == 32 || win32 == 33)
                {
                    return Loc.T("Error.FileInUse");
                }
            }

            return Loc.T("Error.DeleteFailed", ex.Message);
        }
    }
}
