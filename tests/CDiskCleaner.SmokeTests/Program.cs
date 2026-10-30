using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CDiskCleaner.Models;
using CDiskCleaner.Services;

namespace CDiskCleaner.SmokeTests
{
    /// <summary>
    /// Console harness that proves the read-only scanner, the deletion guard and both delete paths.
    /// It only touches files it creates itself inside a throw-away sandbox folder.
    /// </summary>
    internal static class Program
    {
        private static int _checks;
        private static int _failures;

        private static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            string sandbox = null;

            try
            {
                TestFormat();
                TestPathSafety();
                sandbox = CreateSandbox();
                TestScanner(sandbox);
                TestDeletionService(sandbox);
                TestReportBuilder();
                TestRealQuickScan();
            }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex);
                _failures++;
            }
            finally
            {
                if (sandbox != null)
                {
                    TryDeleteSandbox(sandbox);
                }
            }

            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? "ALL PASS (" + _checks + " checks)"
                : _failures + " FAILURE(S) out of " + _checks + " checks");
            return _failures == 0 ? 0 : 1;
        }

        // ---------- helpers ----------

        private static void Check(bool condition, string description)
        {
            _checks++;
            if (condition)
            {
                Console.WriteLine("  PASS  " + description);
            }
            else
            {
                _failures++;
                Console.WriteLine("  FAIL  " + description);
            }
        }

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("== " + title);
        }

        private static string CreateSandbox()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "CDiskCleanerSmoke_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void TryDeleteSandbox(string sandbox)
        {
            try
            {
                if (Directory.Exists(sandbox))
                {
                    Directory.Delete(sandbox, true);
                }
            }
            catch
            {
                // The sandbox is disposable; leftovers are harmless.
            }
        }

        private static void WriteFile(string path, int sizeBytes, int ageDays, byte[] content)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (content == null)
            {
                content = new byte[sizeBytes];
                var random = new Random(1234);
                random.NextBytes(content);
            }

            File.WriteAllBytes(path, content);
            if (ageDays > 0)
            {
                File.SetLastWriteTime(path, DateTime.Now.AddDays(-ageDays));
            }
        }

        private static ScanItem MakeItem(string path, long size, ScanCategory category, SafetyRating safety)
        {
            return new ScanItem
            {
                Path = path,
                Name = Path.GetFileName(path),
                Kind = path.EndsWith("\\", StringComparison.Ordinal) ? ItemKind.Folder : ItemKind.File,
                SizeBytes = size,
                FileCount = 1,
                LastModified = DateTime.Now,
                Category = category,
                Safety = safety,
                ReasonKey = "Reason.Test",
                TypeKey = "Type.Test",
                RecoverabilityKey = "Recover.RecycleOrPermanent"
            };
        }

        // ---------- tests ----------

        private static void TestFormat()
        {
            Section("Format");
            Check(Format.Size(0) == "0 B", "Format.Size(0)");
            Check(Format.Size(1024) == "1.00 KB", "Format.Size(1024)");
            Check(Format.Size(1536 * 1024) == "1.50 MB", "Format.Size(1.5 MB)");
            Check(Format.Size(-1) == "—", "Format.Size(unknown) is a dash");
        }

        private static void TestPathSafety()
        {
            Section("PathSafety guard");

            Check(!PathSafety.IsDeletable(MakeItem(@"C:\Windows\System32\drivers", 0, ScanCategory.A, SafetyRating.Safe)),
                "Windows system folder is protected");
            Check(!PathSafety.IsDeletable(MakeItem(@"C:\", 0, ScanCategory.A, SafetyRating.Safe)),
                "Drive root is protected");
            Check(!PathSafety.IsDeletable(MakeItem(@"C:\pagefile.sys", 0, ScanCategory.A, SafetyRating.Safe)),
                "pagefile.sys is protected");
            Check(PathSafety.IsDeletable(MakeItem(@"C:\Windows\Temp\thing.tmp", 0, ScanCategory.A, SafetyRating.Safe)),
                "Windows\\Temp is an allowed cleanup location");
            Check(!PathSafety.IsDeletable(MakeItem(@"C:\Program Files\SomeApp", 0, ScanCategory.A, SafetyRating.Safe)),
                "Program Files is blocked unless it is tagged as a program leftover");
        }

        private static void TestScanner(string sandbox)
        {
            Section("Scanner classification");

            // Project with a build output.
            string project = Path.Combine(sandbox, "Project");
            Directory.CreateDirectory(project);
            File.WriteAllText(Path.Combine(project, "Project.csproj"), "<Project />");
            WriteFile(Path.Combine(project, "bin", "app.bin"), 2 * 1024 * 1024, 0, null);

            // node_modules.
            WriteFile(Path.Combine(sandbox, "node_modules", "pkg", "index.js"), 6 * 1024 * 1024, 0, null);

            // Old installer in Downloads.
            WriteFile(Path.Combine(sandbox, "Downloads", "old-installer.msi"), 2 * 1024 * 1024, 200, null);

            // Old log.
            WriteFile(Path.Combine(sandbox, "Logs", "app.log"), 2 * 1024 * 1024, 200, null);

            // Big user file.
            WriteFile(Path.Combine(sandbox, "Media", "movie.mkv"), 3 * 1024 * 1024, 10, null);

            // Two identical copies for the duplicate check.
            byte[] payload = new byte[2 * 1024 * 1024];
            new Random(99).NextBytes(payload);
            string copyA = Path.Combine(sandbox, "Dup", "a.bin");
            string copyB = Path.Combine(sandbox, "Dup", "b.bin");
            WriteFile(copyA, payload.Length, 200, payload);
            WriteFile(copyB, payload.Length, 0, payload);

            var options = new ScanOptions
            {
                ScanRoot = sandbox,
                IncludeSystemCleanupLocations = false,
                FullScan = true,
                FindDuplicates = true,
                MinItemSizeBytes = 1024,
                GroupMinSizeBytes = 1024,
                LogMinSizeBytes = 1024,
                InstallerMinSizeBytes = 1024,
                LargeFileMinBytes = 1024L * 1024,
                DuplicateMinBytes = 1024L * 1024,
                DuplicateHashCapBytes = 1024L * 1024 * 1024,
                TempAgeDays = 7,
                LogAgeDays = 30,
                InstallerAgeDays = 30
            };

            ScanResult result = new FileSystemScanner(options, null, System.Threading.CancellationToken.None).Scan();

            Check(result.Items.Count > 0, "scanner produced candidates (" + result.Items.Count + ")");
            Check(result.UnexpectedError == null, "scanner finished without an unexpected error");
            Console.WriteLine("  .. files scanned=" + result.FilesScanned +
                              " dirs=" + result.DirectoriesScanned +
                              " inaccessible=" + result.InaccessibleDirectories);
            foreach (ScanItem item in result.Items)
            {
                Console.WriteLine("  .. [" + item.TypeKey + "] " + item.Category + " " +
                                  Format.Size(item.SizeBytes) + " " + item.Path);
            }

            ScanItem build = result.Items.FirstOrDefault(i => i.TypeKey == "Type.BuildOutput");
            Check(build != null && build.Category == ScanCategory.B, "bin folder detected as build output (category B)");
            Check(build != null && build.Path.EndsWith("Project\\bin", StringComparison.OrdinalIgnoreCase),
                "build output path is the bin folder itself");

            ScanItem node = result.Items.FirstOrDefault(i => i.TypeKey == "Type.NodeModules");
            Check(node != null && node.Category == ScanCategory.B, "node_modules detected (category B)");

            ScanItem installer = result.Items.FirstOrDefault(i => i.TypeKey == "Type.Installer");
            Check(installer != null && installer.Category == ScanCategory.B, "old installer detected (category B)");
            Check(installer != null && installer.Path.EndsWith("old-installer.msi", StringComparison.OrdinalIgnoreCase),
                "installer path is the .msi file");

            ScanItem log = result.Items.FirstOrDefault(i => i.TypeKey == "Type.LogFile");
            Check(log != null && log.Category == ScanCategory.B, "old log detected (category B)");

            ScanItem large = result.Items.FirstOrDefault(i => i.TypeKey == "Type.LargeFile");
            Check(large != null && large.Category == ScanCategory.D, "large file detected (category D, needs your decision)");

            var duplicates = result.Items.Where(i => i.TypeKey == "Type.Duplicate").ToList();
            Check(duplicates.Count == 1, "exactly one duplicate copy is flagged (" + duplicates.Count + ")");
            Check(duplicates.Count == 1 &&
                  string.Equals(duplicates[0].Path, copyB, StringComparison.OrdinalIgnoreCase),
                "the newer copy is flagged, the older one is kept");
            Check(duplicates.Count == 1 && duplicates[0].Category == ScanCategory.B,
                "duplicate is category B (review first)");
        }

        private static void TestDeletionService(string sandbox)
        {
            Section("DeletionService");

            var service = new DeletionService();

            // Permanent delete of a single generated file.
            string target = Path.Combine(sandbox, "delete-me.bin");
            WriteFile(target, 4096, 0, null);
            DeletionOutcome permanent = service.Execute(MakeItem(target, 4096, ScanCategory.A, SafetyRating.Safe), false);
            Check(permanent.Success, "permanent delete succeeded");
            Check(!File.Exists(target), "permanently deleted file is gone");

            // Group delete (several generated files through one item).
            string groupDir = Path.Combine(sandbox, "group");
            string groupA = Path.Combine(groupDir, "a.bin");
            string groupB = Path.Combine(groupDir, "b.bin");
            WriteFile(groupA, 2048, 0, null);
            WriteFile(groupB, 2048, 0, null);
            var groupItem = MakeItem(groupDir, 4096, ScanCategory.B, SafetyRating.Caution);
            groupItem.Kind = ItemKind.Group;
            groupItem.GroupPaths = new List<string> { groupA, groupB };
            DeletionOutcome group = service.Execute(groupItem, false);
            Check(group.Success && !File.Exists(groupA) && !File.Exists(groupB), "group delete removed both files");

            // Recycle Bin path (goes to the Recycle Bin, which the user can restore).
            string recycleTarget = Path.Combine(sandbox, "recycle-me.bin");
            WriteFile(recycleTarget, 4096, 0, null);
            DeletionOutcome recycled = service.Execute(MakeItem(recycleTarget, 4096, ScanCategory.A, SafetyRating.Safe), true);
            Check(recycled.Success, "recycle operation reported success");
            Check(!File.Exists(recycleTarget), "recycled file is no longer in place");

            // Protected path must be refused before anything is touched.
            DeletionOutcome refused = service.Execute(
                MakeItem(@"C:\Windows\System32\drivers", 0, ScanCategory.A, SafetyRating.Safe), false);
            Check(!refused.Success && !string.IsNullOrEmpty(refused.Error), "protected path is refused with an error");
            Check(Directory.Exists(@"C:\Windows\System32\drivers"), "protected path still exists");
        }

        /// <summary>
        /// Runs the real quick scan (read-only) and prints the numbers. Used to compare a build
        /// with and without the long-path switches: the candidate list must stay the same.
        /// </summary>
        private static void TestRealQuickScan()
        {
            Section("Real quick scan of C: (read only)");
            var options = new ScanOptions
            {
                ScanRoot = @"C:\",
                IncludeSystemCleanupLocations = true,
                FullScan = false,
                FindDuplicates = false
            };

            ScanResult result = new FileSystemScanner(options, null, System.Threading.CancellationToken.None).Scan();
            long bytes = result.Items.Where(i => i.SizeBytes > 0).Sum(i => i.SizeBytes);
            Console.WriteLine("  .. candidates      : " + result.CandidateCount);
            Console.WriteLine("  .. candidate bytes : " + Format.Size(bytes));
            Console.WriteLine("  .. files touched   : " + Format.Count(result.FilesScanned));
            Console.WriteLine("  .. folders touched : " + Format.Count(result.DirectoriesScanned));
            Console.WriteLine("  .. inaccessible    : " + result.InaccessibleDirectories);
            Console.WriteLine("  .. by category     : A=" + result.Items.Count(i => i.Category == ScanCategory.A) +
                              " B=" + result.Items.Count(i => i.Category == ScanCategory.B) +
                              " C=" + result.Items.Count(i => i.Category == ScanCategory.C) +
                              " D=" + result.Items.Count(i => i.Category == ScanCategory.D));
            Check(result.CandidateCount > 0, "the real quick scan found candidates");
        }

        private static void TestReportBuilder()
        {
            Section("ReportBuilder");

            var session = new SessionState(DriveSnapshot.Capture(@"C:\"));
            var deleted = MakeItem(@"C:\Temp\a.bin", 1024, ScanCategory.A, SafetyRating.Safe);
            session.Record(deleted, OperationMethod.Delete, true, null);
            var skipped = MakeItem(@"C:\Temp\b.bin", 2048, ScanCategory.B, SafetyRating.Caution);
            session.Record(skipped, OperationMethod.Skip, true, null);

            var scan = new ScanResult { Snapshot = DriveSnapshot.Capture(@"C:\") };
            var pending = new List<ScanItem> { skipped };

            ReportData data = ReportBuilder.Build(session, scan, pending, DriveSnapshot.Capture(@"C:\"));
            Check(!string.IsNullOrEmpty(data.Text), "report text is not empty");
            Check(data.Text.Contains("Report.SectionSpace"), "report contains the space section");
            Check(data.Text.Contains("Report.SectionRemaining"), "report contains the remaining-items section");
            Check(data.DeletedCount == "1", "report counts the permanent deletion");
            Check(data.SkippedCount == "1", "report counts the skipped item");
            Check(data.Recovered == "1.00 KB", "report shows the recovered size");
        }
    }
}
