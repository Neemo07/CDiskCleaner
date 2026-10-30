using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using CDiskCleaner.Models;

namespace CDiskCleaner.Services
{
    /// <summary>
    /// Read-only disk scanner. It never deletes or modifies files; it only builds candidates.
    /// </summary>
    public sealed partial class FileSystemScanner
    {
        private const string ProgramLeftoverSource = "ProgramLeftover";
        private const string TempSource = "Temp";
        private const string BrowserCacheSource = "BrowserCache";
        private const string ThumbnailSource = "Thumbnail";
        private const string CrashDumpSource = "CrashDump";
        private const string LinuxSource = "Other";
        private const long MinCacheBytes = 5L * 1024 * 1024;
        private const long MinNodeModulesBytes = 5L * 1024 * 1024;

        private sealed class FileCandidate
        {
            public string Path;
            public long Size;
            public DateTime Modified;
        }

        private sealed class FolderStats
        {
            public long Bytes;
            public int Files;
            public DateTime? LastModified;
        }

        private readonly ScanOptions _options;
        private readonly Action<ScanProgress> _progress;
        private readonly CancellationToken _cancellationToken;
        private readonly string _root;
        private readonly bool _systemScan;
        private readonly InstalledPrograms _installedPrograms;

        private readonly List<ScanItem> _items = new List<ScanItem>();
        private readonly Dictionary<string, ScanItem> _itemsByPath =
            new Dictionary<string, ScanItem>(StringComparer.OrdinalIgnoreCase);
        private readonly List<FileCandidate> _duplicatePool = new List<FileCandidate>();
        private readonly HashSet<string> _cleanupRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _userProfiles = new List<string>();

        private readonly Stopwatch _progressClock = Stopwatch.StartNew();
        private long _filesScanned;
        private long _directoriesScanned;
        private int _inaccessibleDirectories;
        private long _candidateBytes;
        private string _phaseKey = "Scan.Phase.Cleanup";
        private string _currentPath = string.Empty;
        private bool _cancelled;

        private long _hashedBytes;
        private int _duplicateGroups;
        private bool _duplicateCapReached;
        private bool _duplicatePoolTruncated;

        public FileSystemScanner(
            ScanOptions options,
            Action<ScanProgress> progress,
            CancellationToken cancellationToken)
        {
            _options = options ?? new ScanOptions();
            _progress = progress;
            _cancellationToken = cancellationToken;
            _root = PathSafety.Normalize(_options.ScanRoot);
            _systemScan = _options.IsSystemDriveScan;
            _installedPrograms = _systemScan ? InstalledPrograms.Load() : null;

            if (_systemScan)
            {
                CollectUserProfiles();
                BuildCleanupRoots();
            }
            else
            {
                // A non-system scan root behaves like a user profile: its Documents/Downloads folders
                // are document areas and everything below it counts as user data.
                _userProfiles.Add(_root);
            }
        }

        public ScanResult Scan()
        {
            var result = new ScanResult
            {
                Options = _options,
                StartedUtc = DateTime.UtcNow,
                Snapshot = DriveSnapshot.Capture(_root)
            };

            try
            {
                if (_systemScan)
                {
                    _phaseKey = "Scan.Phase.Cleanup";
                    AddProtectedWarnings();

                    if (_options.IncludeSystemCleanupLocations)
                    {
                        ScanCleanupLocations();
                    }
                }

                if (_options.FullScan)
                {
                    _phaseKey = "Scan.Phase.Users";
                    ScanGenericRoots();
                }

                if (_systemScan)
                {
                    _phaseKey = "Scan.Phase.Programs";
                    ScanProgramLeftovers();
                }

                if (_options.FindDuplicates && _options.FullScan)
                {
                    _phaseKey = "Scan.Phase.Duplicates";
                    FindDuplicates();
                }

                _phaseKey = "Scan.Phase.Finalizing";
            }
            catch (OperationCanceledException)
            {
                _cancelled = true;
            }
            catch (Exception ex)
            {
                result.UnexpectedError = ex.Message;
            }

            result.Items = _items
                .OrderBy(i => (int)i.Category)
                .ThenByDescending(i => i.SizeBytes)
                .ToList();
            result.CompletedUtc = DateTime.UtcNow;
            result.Cancelled = _cancelled;
            result.FilesScanned = _filesScanned;
            result.DirectoriesScanned = _directoriesScanned;
            result.InaccessibleDirectories = _inaccessibleDirectories;
            result.DuplicateHashedBytes = _hashedBytes;
            result.DuplicateGroupCount = _duplicateGroups;
            result.DuplicateCapReached = _duplicateCapReached;
            result.DuplicatePoolTruncated = _duplicatePoolTruncated;

            ReportProgress(true);
            return result;
        }

        private void CollectUserProfiles()
        {
            string usersRoot = Path.Combine(_root, "Users");
            if (!Directory.Exists(usersRoot))
            {
                return;
            }

            try
            {
                foreach (string profile in Directory.GetDirectories(usersRoot))
                {
                    string name = Path.GetFileName(profile);
                    if (string.Equals(name, "Public", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "Default User", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "All Users", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!IsReparsePoint(profile))
                    {
                        _userProfiles.Add(PathSafety.Normalize(profile));
                    }
                }
            }
            catch
            {
                _inaccessibleDirectories++;
            }
        }

        private void BuildCleanupRoots()
        {
            void Add(params string[] parts)
            {
                _cleanupRoots.Add(PathSafety.Normalize(Path.Combine(parts)));
            }

            foreach (string profile in _userProfiles)
            {
                Add(profile, "AppData", "Local", "Temp");
                Add(profile, "AppData", "Local", "Packages");
                Add(profile, "AppData", "Local", "Microsoft", "WindowsApps");
                Add(profile, "AppData", "Local", "Microsoft", "Windows", "Explorer");
                Add(profile, "AppData", "Local", "Microsoft", "Windows", "INetCache");
                Add(profile, "AppData", "Local", "Microsoft", "Windows", "WER");
                Add(profile, "AppData", "Local", "CrashDumps");

                Add(profile, "AppData", "Local", "Google", "Chrome", "User Data");
                Add(profile, "AppData", "Local", "Microsoft", "Edge", "User Data");
                Add(profile, "AppData", "Local", "Microsoft", "Edge");
                Add(profile, "AppData", "Local", "BraveSoftware", "Brave-Browser", "User Data");
                Add(profile, "AppData", "Local", "Vivaldi", "User Data");
                Add(profile, "AppData", "Local", "Opera Software");
                Add(profile, "AppData", "Local", "Mozilla", "Firefox", "Profiles");
                Add(profile, "AppData", "Roaming", "Opera Software");
            }

            Add(_root, "Windows", "Temp");
            Add(_root, "Windows", "SystemTemp");
            Add(_root, "Windows", "Logs");
            Add(_root, "Windows", "Panther");
            Add(_root, "Windows", "Prefetch");
            Add(_root, "Windows", "Minidump");
            Add(_root, "Windows", "LiveKernelReports");
            Add(_root, "Windows", "SoftwareDistribution", "Download");
            Add(_root, "Windows", "SoftwareDistribution", "DeliveryOptimization");
            Add(_root, "Windows", "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization");
            Add(_root, "ProgramData", "Microsoft", "Windows", "WER");
            Add(_root, "ProgramData", "Package Cache");
        }

        private void ScanGenericRoots()
        {
            var roots = new List<string>();
            if (!_systemScan)
            {
                if (Directory.Exists(_root))
                {
                    roots.Add(_root);
                }
            }
            else
            {
                string users = Path.Combine(_root, "Users");
                string programData = Path.Combine(_root, "ProgramData");
                if (Directory.Exists(users))
                {
                    roots.Add(users);
                }

                if (Directory.Exists(programData))
                {
                    roots.Add(programData);
                }

                foreach (string topLevel in SafeGetDirectories(_root))
                {
                    string name = Path.GetFileName(topLevel);
                    if (IsReservedRootDirectory(name))
                    {
                        continue;
                    }

                    roots.Add(topLevel);
                }

                foreach (string file in SafeGetFiles(_root))
                {
                    try
                    {
                        ProcessFile(new FileInfo(PathSafety.ToExtended(file)), true);
                    }
                    catch
                    {
                        // A root-level file disappeared or is inaccessible.
                    }
                }
            }

            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                WalkTree(root);
            }
        }

        private static bool IsReservedRootDirectory(string name)
        {
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "windows":
                case "windows.old":
                case "program files":
                case "program files (x86)":
                case "programdata":
                case "users":
                case "$recycle.bin":
                case "system volume information":
                case "recovery":
                case "perflogs":
                case "config.msi":
                case "$winreagent":
                case "documents and settings":
                case "msocache":
                case "boot":
                case "efi":
                    return true;
                default:
                    return false;
            }
        }

        private void WalkTree(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                CheckCancelled();
                string directory = stack.Pop();
                if (ShouldSkipDirectory(directory))
                {
                    continue;
                }

                FileSystemInfo[] entries = ListDirectory(directory);
                if (entries == null)
                {
                    continue;
                }

                bool currentIsProject = entries.Any(IsProjectMarker) || entries.Any(e =>
                    e is DirectoryInfo && string.Equals(e.Name, ".git", StringComparison.OrdinalIgnoreCase));

                foreach (FileSystemInfo entry in entries)
                {
                    CheckCancelled();
                    _currentPath = DisplayPath(entry);

                    var directoryInfo = entry as DirectoryInfo;
                    if (directoryInfo != null)
                    {
                        if (IsReparsePoint(directoryInfo.FullName))
                        {
                            continue;
                        }

                        string fullPath = DisplayPath(directoryInfo);
                        string name = directoryInfo.Name;

                        if (string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase))
                        {
                            AddNodeModules(fullPath);
                            continue;
                        }

                        if (IsBuildOutputFolder(name, fullPath))
                        {
                            AddBuildOutput(fullPath, name);
                            continue;
                        }

                        if (IsCopyFolderName(name) && LooksLikeProject(fullPath))
                        {
                            if (AddOldProjectCopy(fullPath, name))
                            {
                                continue;
                            }
                        }

                        if (ShouldSkipDirectory(fullPath))
                        {
                            continue;
                        }

                        stack.Push(fullPath);
                    }
                    else
                    {
                        var fileInfo = entry as FileInfo;
                        if (fileInfo != null)
                        {
                            ProcessFile(fileInfo, false);
                        }
                    }
                }

                ReportProgress();
            }
        }

        private void ProcessFile(FileInfo file, bool installerAreaOverride)
        {
            string path = DisplayPath(file);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            _filesScanned++;
            _currentPath = path;

            long size;
            DateTime modified;
            try
            {
                size = file.Length;
                modified = file.LastWriteTime;
            }
            catch
            {
                return;
            }

            if (size <= 0)
            {
                return;
            }

            if (_options.FindDuplicates && size >= _options.DuplicateMinBytes &&
                !IsUnderProgramFiles(path))
            {
                _duplicatePool.Add(new FileCandidate { Path = path, Size = size, Modified = modified });
            }

            string extension = Path.GetExtension(path) ?? string.Empty;
            int ageDays = Format.AgeDays(modified);

            bool installerArea = installerAreaOverride || IsUnderUserDocumentArea(path);
            if (ageDays >= _options.InstallerAgeDays &&
                size >= _options.InstallerMinSizeBytes &&
                installerArea &&
                IsInstallerExtension(extension))
            {
                bool exe = string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase);
                if (!exe || LooksLikeInstallerName(file.Name) || size >= 100L * 1024 * 1024)
                {
                    AddItem(CreateFileItem(
                        path,
                        size,
                        modified,
                        ScanCategory.B,
                        SafetyRating.Caution,
                        exe ? "Reason.InstallerExe" : "Reason.Installer",
                        new object[] { file.Name, ageDays },
                        "Type.Installer",
                        "Recover.RecycleOrPermanent",
                        false,
                        "Installer"));
                    return;
                }
            }

            if (ageDays >= _options.LogAgeDays &&
                size >= _options.LogMinSizeBytes &&
                IsLogExtension(extension))
            {
                AddItem(CreateFileItem(
                    path,
                    size,
                    modified,
                    ScanCategory.B,
                    SafetyRating.Caution,
                    "Reason.LogOld",
                    new object[] { file.Name, ageDays },
                    "Type.LogFile",
                    "Recover.RecycleOrPermanent",
                    true,
                    "Log"));
                return;
            }

            if (size >= _options.LargeFileMinBytes && IsLargeFileArea(path))
            {
                bool veryOld = ageDays >= 365;
                AddItem(CreateFileItem(
                    path,
                    size,
                    modified,
                    ScanCategory.D,
                    SafetyRating.Unknown,
                    veryOld ? "Reason.LargeOldFile" : "Reason.LargeFile",
                    new object[] { Format.Size(size), ageDays },
                    "Type.LargeFile",
                    "Recover.UserData",
                    false,
                    "Large"));
            }
        }

        private ScanItem CreateFileItem(
            string path,
            long size,
            DateTime modified,
            ScanCategory category,
            SafetyRating safety,
            string reasonKey,
            object[] reasonArgs,
            string typeKey,
            string recoverabilityKey,
            bool recommendDelete,
            string sourceTag)
        {
            return new ScanItem
            {
                Path = path,
                Name = Path.GetFileName(path),
                Kind = ItemKind.File,
                SizeBytes = size,
                FileCount = 1,
                LastModified = modified,
                Category = category,
                Safety = safety,
                ReasonKey = reasonKey,
                ReasonArgs = reasonArgs,
                TypeKey = typeKey,
                RecoverabilityKey = recoverabilityKey,
                RecommendDelete = recommendDelete,
                RequiresAdmin = IsUnderProgramData(path),
                SourceTag = sourceTag
            };
        }

        private bool AddItem(ScanItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Path))
            {
                return false;
            }

            string key = PathSafety.Normalize(item.Path);
            if (string.IsNullOrEmpty(key) || _itemsByPath.ContainsKey(key))
            {
                return false;
            }

            _itemsByPath[key] = item;
            _items.Add(item);
            if (item.SizeBytes > 0)
            {
                _candidateBytes += item.SizeBytes;
            }

            return true;
        }

        private void AddNodeModules(string path)
        {
            FolderStats stats = ComputeStats(path);
            if (stats.Bytes < MinNodeModulesBytes)
            {
                return;
            }

            AddFolderCandidate(
                path,
                "node_modules",
                stats,
                ScanCategory.B,
                SafetyRating.Caution,
                "Reason.NodeModules",
                null,
                "Type.NodeModules",
                "Recover.Rebuild",
                true,
                "NodeModules",
                false);
        }

        private bool AddOldProjectCopy(string path, string name)
        {
            FolderStats stats = ComputeStats(path);
            if (stats.Bytes < _options.MinItemSizeBytes)
            {
                return false;
            }

            return AddFolderCandidate(
                path,
                name,
                stats,
                ScanCategory.D,
                SafetyRating.Unknown,
                "Reason.OldProjectCopy",
                new object[] { name },
                "Type.OldProjectCopy",
                "Recover.UserData",
                false,
                "OldCopy",
                false);
        }

        private void AddBuildOutput(string path, string name)
        {
            FolderStats stats = ComputeStats(path);
            if (stats.Bytes < _options.MinItemSizeBytes)
            {
                return;
            }

            AddFolderCandidate(
                path,
                name,
                stats,
                ScanCategory.B,
                SafetyRating.Caution,
                "Reason.BuildOutput",
                new object[] { name },
                "Type.BuildOutput",
                "Recover.Rebuild",
                true,
                "BuildOutput",
                false);
        }

        private bool AddFolderCandidate(
            string path,
            string name,
            FolderStats stats,
            ScanCategory category,
            SafetyRating safety,
            string reasonKey,
            object[] reasonArgs,
            string typeKey,
            string recoverabilityKey,
            bool recommendDelete,
            string sourceTag,
            bool requiresAdmin)
        {
            return AddItem(new ScanItem
            {
                Path = path,
                Name = name,
                Kind = ItemKind.Folder,
                SizeBytes = stats.Bytes,
                FileCount = stats.Files,
                LastModified = stats.LastModified,
                Category = category,
                Safety = safety,
                ReasonKey = reasonKey,
                ReasonArgs = reasonArgs,
                TypeKey = typeKey,
                RecoverabilityKey = recoverabilityKey,
                RecommendDelete = recommendDelete,
                RequiresAdmin = requiresAdmin,
                SourceTag = sourceTag
            });
        }

        private void ScanProgramLeftovers()
        {
            var roots = new List<string>();
            string programFiles = Path.Combine(_root, "Program Files");
            string programFilesX86 = Path.Combine(_root, "Program Files (x86)");
            if (Directory.Exists(programFiles))
            {
                roots.Add(programFiles);
            }

            if (Directory.Exists(programFilesX86))
            {
                roots.Add(programFilesX86);
            }

            foreach (string profile in _userProfiles)
            {
                string localPrograms = Path.Combine(profile, "AppData", "Local", "Programs");
                if (Directory.Exists(localPrograms))
                {
                    roots.Add(localPrograms);
                }
            }

            foreach (string root in roots)
            {
                CheckCancelled();
                foreach (string folder in SafeGetDirectories(root))
                {
                    CheckCancelled();
                    string name = Path.GetFileName(folder);
                    if (ShouldSkipProgramFolder(name) || IsReparsePoint(folder))
                    {
                        continue;
                    }

                    if (_installedPrograms != null && _installedPrograms.MatchesFolder(folder, name))
                    {
                        continue;
                    }

                    FolderStats stats = ComputeStats(folder);
                    if (stats.Bytes < 50L * 1024 * 1024)
                    {
                        continue;
                    }

                    bool underProgramFiles = IsUnderProgramFiles(folder);
                    AddFolderCandidate(
                        folder,
                        name,
                        stats,
                        ScanCategory.D,
                        SafetyRating.Unknown,
                        "Reason.ProgramLeftover",
                        null,
                        "Type.ProgramLeftover",
                        "Recover.ProgramFolder",
                        false,
                        ProgramLeftoverSource,
                        underProgramFiles);
                }
            }
        }

        private static bool ShouldSkipProgramFolder(string name)
        {
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "common files":
                case "internet explorer":
                case "windowsapps":
                case "modifiablewindowsapps":
                case "windows defender":
                case "windows defender advanced threat protection":
                case "windows mail":
                case "windows media player":
                case "windows multimedia platform":
                case "windows nt":
                case "windows photo viewer":
                case "windows portable devices":
                case "windows security":
                case "windows sidebar":
                case "microsoft":
                case "microsoft office":
                case "microsoft.net":
                case "msbuild":
                case "dotnet":
                case "reference assemblies":
                case "uninstall information":
                case "removable storage access":
                case "windowspowershell":
                case "windows kits":
                    return true;
                default:
                    return false;
            }
        }

        private void FindDuplicates()
        {
            if (_duplicatePool.Count < 2)
            {
                return;
            }

            var candidates = _duplicatePool
                .Where(c => c.Size >= _options.DuplicateMinBytes)
                .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (candidates.Count > _options.DuplicateMaxFiles)
            {
                candidates = candidates
                    .OrderByDescending(c => c.Size)
                    .Take(_options.DuplicateMaxFiles)
                    .ToList();
                _duplicatePoolTruncated = true;
            }

            var sizeGroups = candidates
                .GroupBy(c => c.Size)
                .Where(g => g.Count() > 1)
                .OrderByDescending(g => g.Key * (g.Count() - 1))
                .ToList();

            foreach (var sizeGroup in sizeGroups)
            {
                CheckCancelled();
                long groupBytes = sizeGroup.Key * sizeGroup.Count();
                if (_hashedBytes + groupBytes > _options.DuplicateHashCapBytes)
                {
                    _duplicateCapReached = true;
                    break;
                }

                var hashGroups = new Dictionary<string, List<FileCandidate>>(StringComparer.OrdinalIgnoreCase);
                foreach (FileCandidate candidate in sizeGroup)
                {
                    CheckCancelled();
                    _currentPath = candidate.Path;
                    ReportProgress();
                    string hash = TryHashFile(candidate.Path, candidate.Size);
                    if (hash == null)
                    {
                        continue;
                    }

                    _hashedBytes += candidate.Size;
                    List<FileCandidate> list;
                    if (!hashGroups.TryGetValue(hash, out list))
                    {
                        list = new List<FileCandidate>();
                        hashGroups[hash] = list;
                    }

                    list.Add(candidate);
                }

                foreach (var hashGroup in hashGroups.Values.Where(g => g.Count > 1))
                {
                    FileCandidate keeper = hashGroup
                        .OrderBy(c => c.Modified)
                        .ThenBy(c => c.Path.Length)
                        .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                        .First();

                    foreach (FileCandidate duplicate in hashGroup)
                    {
                        if (ReferenceEquals(duplicate, keeper))
                        {
                            continue;
                        }

                        AddDuplicateItem(duplicate, keeper.Path);
                    }

                    _duplicateGroups++;
                }
            }

            ReportProgress(true);
        }

        private void AddDuplicateItem(FileCandidate candidate, string keeperPath)
        {
            ScanItem existing;
            if (_itemsByPath.TryGetValue(PathSafety.Normalize(candidate.Path), out existing))
            {
                if (existing.Category == ScanCategory.D || string.Equals(existing.SourceTag, "Large", StringComparison.Ordinal))
                {
                    existing.Category = ScanCategory.B;
                    existing.Safety = SafetyRating.Caution;
                    existing.ReasonKey = "Reason.Duplicate";
                    existing.ReasonArgs = new object[] { keeperPath };
                    existing.TypeKey = "Type.Duplicate";
                    existing.RecoverabilityKey = "Recover.UserData";
                    existing.RecommendDelete = true;
                    existing.SourceTag = "Duplicate";
                }

                return;
            }

            AddItem(new ScanItem
            {
                Path = candidate.Path,
                Name = Path.GetFileName(candidate.Path),
                Kind = ItemKind.File,
                SizeBytes = candidate.Size,
                FileCount = 1,
                LastModified = candidate.Modified,
                Category = ScanCategory.B,
                Safety = SafetyRating.Caution,
                ReasonKey = "Reason.Duplicate",
                ReasonArgs = new object[] { keeperPath },
                TypeKey = "Type.Duplicate",
                RecoverabilityKey = "Recover.UserData",
                RecommendDelete = true,
                RequiresAdmin = IsUnderProgramData(candidate.Path),
                SourceTag = "Duplicate"
            });
        }

        private string TryHashFile(string path, long expectedSize)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var stream = new FileStream(
                    PathSafety.ToExtended(path),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    1024 * 1024,
                    FileOptions.SequentialScan))
                {
                    if (stream.Length != expectedSize)
                    {
                        return null;
                    }

                    var buffer = new byte[1024 * 1024];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        CheckCancelled();
                        sha.TransformBlock(buffer, 0, read, null, 0);
                    }

                    sha.TransformFinalBlock(buffer, 0, 0);
                    if (sha.Hash == null)
                    {
                        return null;
                    }

                    var text = new StringBuilder(sha.Hash.Length * 2);
                    foreach (byte b in sha.Hash)
                    {
                        text.Append(b.ToString("x2"));
                    }

                    return text.ToString();
                }
            }
            catch
            {
                return null;
            }
        }

        private FolderStats ComputeStats(string path)
        {
            var stats = new FolderStats { LastModified = TryGetLastWrite(path) };
            var stack = new Stack<string>();
            stack.Push(path);

            while (stack.Count > 0)
            {
                CheckCancelled();
                string directory = stack.Pop();
                FileSystemInfo[] entries = ListDirectory(directory);
                if (entries == null)
                {
                    continue;
                }

                foreach (FileSystemInfo entry in entries)
                {
                    CheckCancelled();
                    if (entry is DirectoryInfo)
                    {
                        if (!IsReparsePoint(entry.FullName))
                        {
                            stack.Push(DisplayPath(entry));
                        }
                    }
                    else
                    {
                        try
                        {
                            stats.Bytes += ((FileInfo)entry).Length;
                            stats.Files++;
                        }
                        catch
                        {
                            // The file disappeared or is inaccessible.
                        }
                    }
                }

                _currentPath = directory;
                ReportProgress();
            }

            _filesScanned += stats.Files;
            return stats;
        }

        private FileSystemInfo[] ListDirectory(string path)
        {
            try
            {
                var info = new DirectoryInfo(PathSafety.ToExtended(path));
                FileSystemInfo[] entries = info.GetFileSystemInfos();
                _directoriesScanned++;
                return entries;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                _inaccessibleDirectories++;
                return null;
            }
            catch (IOException)
            {
                _inaccessibleDirectories++;
                return null;
            }
            catch (Exception)
            {
                _inaccessibleDirectories++;
                return null;
            }
        }

        private IEnumerable<string> SafeGetDirectories(string path)
        {
            try
            {
                return Directory.GetDirectories(PathSafety.ToExtended(path)).Select(StripExtendedPrefix);
            }
            catch
            {
                _inaccessibleDirectories++;
                return Enumerable.Empty<string>();
            }
        }

        private IEnumerable<string> SafeGetFiles(string path)
        {
            try
            {
                return Directory.GetFiles(PathSafety.ToExtended(path)).Select(StripExtendedPrefix);
            }
            catch
            {
                _inaccessibleDirectories++;
                return Enumerable.Empty<string>();
            }
        }

        private static string StripExtendedPrefix(string path)
        {
            if (!string.IsNullOrEmpty(path) && path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return path.Substring(4);
            }

            return path;
        }

        private static string DisplayPath(FileSystemInfo info)
        {
            return StripExtendedPrefix(info.FullName);
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(PathSafety.ToExtended(path)) & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return false;
            }
        }

        private bool ShouldSkipDirectory(string path)
        {
            string name = Path.GetFileName(path);
            if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, ".svn", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, ".hg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "$recycle.bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "system volume information", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "config.msi", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "windowsapps", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Default User", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "All Users", StringComparison.OrdinalIgnoreCase))
            {
                string parent = Path.GetDirectoryName(path);
                if (string.Equals(PathSafety.Normalize(parent), PathSafety.Normalize(Path.Combine(_root, "Users")), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            foreach (string root in _cleanupRoots)
            {
                if (PathSafety.IsUnderOrEqual(path, root))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsProjectMarker(FileSystemInfo entry)
        {
            string extension = Path.GetExtension(entry.Name) ?? string.Empty;
            return string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".vbproj", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".fsproj", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".vcxproj", StringComparison.OrdinalIgnoreCase);
        }

        private bool AllowsInstallerAtRoot(string path)
        {
            return string.Equals(PathSafety.Normalize(path), PathSafety.Normalize(_root), StringComparison.OrdinalIgnoreCase);
        }

        private bool IsBuildOutputFolder(string name, string fullPath)
        {
            if (string.Equals(name, ".vs", StringComparison.OrdinalIgnoreCase))
            {
                return HasProjectMarker(Path.GetDirectoryName(fullPath), true);
            }

            if (string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase))
            {
                return HasProjectMarker(Path.GetDirectoryName(fullPath), false);
            }

            return false;
        }

        private static bool HasProjectMarker(string directory, bool solutionOnly)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }

            try
            {
                foreach (string file in Directory.GetFiles(PathSafety.ToExtended(directory)))
                {
                    string extension = Path.GetExtension(file) ?? string.Empty;
                    if (solutionOnly)
                    {
                        if (string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    else if (string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(extension, ".vbproj", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(extension, ".fsproj", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(extension, ".vcxproj", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Treat unreadable project folders as non-projects.
            }

            return false;
        }

        private bool LooksLikeProject(string directory)
        {
            if (HasProjectMarker(directory, false))
            {
                return true;
            }

            try
            {
                return Directory.Exists(Path.Combine(directory, ".git"));
            }
            catch
            {
                return false;
            }
        }

        private static bool IsCopyFolderName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string lower = name.ToLowerInvariant();
            return lower.Contains("copy") ||
                   lower.Contains("backup") ||
                   lower.Contains("back-up") ||
                   lower.Contains("_old") ||
                   lower.Contains("-old") ||
                   lower.Contains(" old") ||
                   lower.Contains("نسخة") ||
                   lower.Contains("نسخه") ||
                   lower.Contains("قديم") ||
                   lower.Contains("احتياطي");
        }

        private static bool IsInstallerExtension(string extension)
        {
            switch ((extension ?? string.Empty).ToLowerInvariant())
            {
                case ".msi":
                case ".msp":
                case ".msix":
                case ".appx":
                case ".iso":
                case ".img":
                case ".exe":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsLogExtension(string extension)
        {
            return string.Equals(extension, ".log", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".etl", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeInstallerName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string lower = name.ToLowerInvariant();
            return lower.Contains("setup") ||
                   lower.Contains("install") ||
                   lower.Contains("installer") ||
                   lower.Contains("update");
        }

        private bool IsUnderUserDocumentArea(string path)
        {
            foreach (string profile in _userProfiles)
            {
                if (PathSafety.IsUnderOrEqual(path, Path.Combine(profile, "Downloads")) ||
                    PathSafety.IsUnderOrEqual(path, Path.Combine(profile, "Desktop")) ||
                    PathSafety.IsUnderOrEqual(path, Path.Combine(profile, "Documents")))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsLargeFileArea(string path)
        {
            if (IsUnderProgramFiles(path))
            {
                return false;
            }

            foreach (string profile in _userProfiles)
            {
                if (PathSafety.IsUnderOrEqual(path, profile))
                {
                    return true;
                }
            }

            return PathSafety.IsUnderOrEqual(path, Path.Combine(_root, "ProgramData")) ||
                   string.Equals(Path.GetDirectoryName(PathSafety.Normalize(path)), PathSafety.Normalize(_root), StringComparison.OrdinalIgnoreCase);
        }

        private bool IsUnderProgramFiles(string path)
        {
            return PathSafety.IsUnderOrEqual(path, Path.Combine(_root, "Program Files")) ||
                   PathSafety.IsUnderOrEqual(path, Path.Combine(_root, "Program Files (x86)"));
        }

        private bool IsUnderProgramData(string path)
        {
            return PathSafety.IsUnderOrEqual(path, Path.Combine(_root, "ProgramData"));
        }

        private bool IsFileLocked(string path)
        {
            try
            {
                using (new FileStream(
                    PathSafety.ToExtended(path),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    return false;
                }
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static DateTime? TryGetLastWrite(string path)
        {
            try
            {
                DateTime value = Directory.GetLastWriteTime(PathSafety.ToExtended(path));
                return value.Year < 1980 ? (DateTime?)null : value;
            }
            catch
            {
                return null;
            }
        }

        private void CheckCancelled()
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(_cancellationToken);
            }
        }

        private void ReportProgress(bool force = false)
        {
            if (_progress == null)
            {
                return;
            }

            if (!force && _progressClock.ElapsedMilliseconds < 180)
            {
                return;
            }

            _progressClock.Restart();
            _progress(new ScanProgress(
                _phaseKey,
                _currentPath,
                _filesScanned,
                _directoriesScanned,
                _inaccessibleDirectories,
                _items.Count,
                _candidateBytes));
        }
    }
}
