using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CDiskCleaner.Models;

namespace CDiskCleaner.Services
{
    public sealed partial class FileSystemScanner
    {
        private void AddProtectedWarnings()
        {
            string windows = Path.Combine(_root, "Windows");
            AddProtectedFolder(windows, "Reason.ProtectedWindows", "Type.SystemFolder", false);
            AddProtectedFolder(Path.Combine(windows, "WinSxS"), "Reason.ProtectedWinSxS", "Type.SystemFolder", false);
            AddProtectedFolder(Path.Combine(windows, "System32"), "Reason.ProtectedWindows", "Type.SystemFolder", false);
            AddProtectedFolder(Path.Combine(windows, "Installer"), "Reason.ProtectedInstallerCache", "Type.SystemFolder", false);

            AddProtectedFolder(Path.Combine(_root, "Program Files"), "Reason.ProtectedPrograms", "Type.SystemFolder", false);
            AddProtectedFolder(Path.Combine(_root, "Program Files (x86)"), "Reason.ProtectedPrograms", "Type.SystemFolder", false);
            AddProtectedFolder(Path.Combine(_root, "ProgramData"), "Reason.ProtectedProgramData", "Type.SystemFolder", false);
            AddProtectedFolder(Path.Combine(_root, "Users"), "Reason.ProtectedUserData", "Type.SystemFolder", false);
            AddProtectedFolder(Path.Combine(_root, "System Volume Information"), "Reason.ProtectedSystemVolume", "Type.SystemFolder", false);
            AddProtectedFolder(Path.Combine(_root, "Recovery"), "Reason.ProtectedRecovery", "Type.SystemFolder", false);
            AddProtectedFolder(Path.Combine(_root, "ProgramData", "Package Cache"), "Reason.ProtectedInstallerCache", "Type.SystemFolder", false);

            string windowsOld = Path.Combine(_root, "Windows.old");
            if (Directory.Exists(PathSafety.ToExtended(windowsOld)))
            {
                AddProtectedFolder(windowsOld, "Reason.WindowsOld", "Type.WindowsOld", false);
            }

            AddProtectedFile(Path.Combine(_root, "pagefile.sys"), "Reason.ProtectedPagefile", "Type.SystemFile");
            AddProtectedFile(Path.Combine(_root, "hiberfil.sys"), "Reason.ProtectedHiberfil", "Type.SystemFile");
            AddProtectedFile(Path.Combine(_root, "swapfile.sys"), "Reason.ProtectedSwapfile", "Type.SystemFile");
            AddProtectedFile(Path.Combine(_root, "bootmgr"), "Reason.ProtectedBoot", "Type.SystemFile");
        }

        private void AddProtectedFolder(string path, string reasonKey, string typeKey, bool requiresAdmin)
        {
            if (!Directory.Exists(PathSafety.ToExtended(path)))
            {
                return;
            }

            AddItem(new ScanItem
            {
                Path = path,
                Name = Path.GetFileName(path.TrimEnd('\\')),
                Kind = ItemKind.Folder,
                SizeBytes = -1,
                FileCount = 0,
                LastModified = TryGetLastWrite(path),
                Category = ScanCategory.C,
                Safety = SafetyRating.Protected,
                ReasonKey = reasonKey,
                TypeKey = typeKey,
                RecoverabilityKey = "Recover.Protected",
                RecommendDelete = false,
                RequiresAdmin = requiresAdmin,
                SourceTag = "Protected"
            });
        }

        private void AddProtectedFile(string path, string reasonKey, string typeKey)
        {
            if (!File.Exists(PathSafety.ToExtended(path)))
            {
                return;
            }

            long size = -1;
            DateTime? modified = null;
            try
            {
                var info = new FileInfo(PathSafety.ToExtended(path));
                size = info.Length;
                modified = info.LastWriteTime;
            }
            catch
            {
                // Keep the metadata unknown.
            }

            AddItem(new ScanItem
            {
                Path = path,
                Name = Path.GetFileName(path),
                Kind = ItemKind.File,
                SizeBytes = size,
                FileCount = 1,
                LastModified = modified,
                Category = ScanCategory.C,
                Safety = SafetyRating.Protected,
                ReasonKey = reasonKey,
                TypeKey = typeKey,
                RecoverabilityKey = "Recover.Protected",
                RecommendDelete = false,
                RequiresAdmin = true,
                SourceTag = "Protected"
            });
        }

        private void ScanCleanupLocations()
        {
            foreach (string profile in _userProfiles)
            {
                ScanTempRoot(Path.Combine(profile, "AppData", "Local", "Temp"), false);
            }

            ScanTempRoot(Path.Combine(_root, "Windows", "Temp"), true);
            ScanTempRoot(Path.Combine(_root, "Windows", "SystemTemp"), true);

            ScanBrowserCaches();
            ScanWindowsUpdateCache();
            ScanThumbnailCaches();
            ScanINetCache();
            ScanCrashDumpLocations();
            ScanWerReports();
            ScanPrefetch();
            ScanWindowsLogs();

            ReportProgress(true);
        }

        private void ScanTempRoot(string root, bool requiresAdmin)
        {
            if (!Directory.Exists(PathSafety.ToExtended(root)))
            {
                return;
            }

            FileSystemInfo[] entries = ListDirectory(root);
            if (entries == null)
            {
                return;
            }

            var smallPaths = new List<string>();
            long smallBytes = 0;
            int smallCount = 0;
            DateTime? groupModified = null;

            foreach (FileSystemInfo entry in entries)
            {
                CheckCancelled();
                string path = DisplayPath(entry);
                _currentPath = path;

                bool isDirectory = entry is DirectoryInfo;
                long size = 0;
                int fileCount = 0;
                DateTime? modified = null;

                if (isDirectory)
                {
                    FolderStats stats = ComputeStats(path);
                    size = stats.Bytes;
                    fileCount = stats.Files;
                    modified = stats.LastModified;
                }
                else
                {
                    try
                    {
                        var file = (FileInfo)entry;
                        size = file.Length;
                        fileCount = 1;
                        modified = file.LastWriteTime;
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (size <= 0)
                {
                    continue;
                }

                bool locked = !isDirectory && IsFileLocked(path);
                ScanCategory category;
                SafetyRating safety;
                string reasonKey;
                object[] reasonArgs = null;
                bool recommend;

                if (locked)
                {
                    category = ScanCategory.B;
                    safety = SafetyRating.Caution;
                    reasonKey = "Reason.TempInUse";
                    recommend = false;
                }
                else if (!modified.HasValue)
                {
                    category = ScanCategory.B;
                    safety = SafetyRating.Caution;
                    reasonKey = "Reason.TempUnknownDate";
                    recommend = false;
                }
                else
                {
                    int ageDays = Format.AgeDays(modified.Value);
                    if (ageDays >= _options.TempAgeDays)
                    {
                        category = ScanCategory.A;
                        safety = SafetyRating.Safe;
                        reasonKey = "Reason.TempOld";
                        reasonArgs = new object[] { ageDays };
                        recommend = true;
                    }
                    else
                    {
                        category = ScanCategory.B;
                        safety = SafetyRating.Caution;
                        reasonKey = "Reason.TempRecent";
                        reasonArgs = new object[] { Math.Max(0, ageDays) };
                        recommend = false;
                    }
                }

                if (size >= _options.MinItemSizeBytes)
                {
                    AddItem(new ScanItem
                    {
                        Path = path,
                        Name = entry.Name,
                        Kind = isDirectory ? ItemKind.Folder : ItemKind.File,
                        SizeBytes = size,
                        FileCount = fileCount,
                        LastModified = modified,
                        Category = category,
                        Safety = safety,
                        ReasonKey = reasonKey,
                        ReasonArgs = reasonArgs,
                        TypeKey = isDirectory ? "Type.TempFolder" : "Type.TempFile",
                        RecoverabilityKey = "Recover.RecycleOrPermanent",
                        RecommendDelete = recommend,
                        RequiresAdmin = requiresAdmin,
                        SourceTag = TempSource
                    });
                }
                else
                {
                    smallPaths.Add(path);
                    smallBytes += size;
                    smallCount += fileCount;
                    if (modified.HasValue && (!groupModified.HasValue || modified.Value > groupModified.Value))
                    {
                        groupModified = modified;
                    }
                }
            }

            if (smallPaths.Count > 0 && smallBytes >= _options.GroupMinSizeBytes)
            {
                AddGroup(
                    root,
                    "Name.TempGroup",
                    smallPaths,
                    smallBytes,
                    smallCount,
                    groupModified,
                    ScanCategory.B,
                    SafetyRating.Caution,
                    "Reason.TempGroup",
                    new object[] { smallCount, root },
                    "Type.TempGroup",
                    "Recover.RecycleOrPermanent",
                    false,
                    requiresAdmin,
                    TempSource);
            }
        }

        private void ScanBrowserCaches()
        {
            var running = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                { "chrome", IsProcessRunning("chrome") },
                { "msedge", IsProcessRunning("msedge") },
                { "brave", IsProcessRunning("brave") },
                { "vivaldi", IsProcessRunning("vivaldi") },
                { "opera", IsProcessRunning("opera") },
                { "firefox", IsProcessRunning("firefox") }
            };

            string[] cacheNames =
            {
                "Cache",
                "Code Cache",
                "GPUCache",
                "DawnCache",
                "GraphiteDawnCache",
                "ShaderCache",
                "GrShaderCache"
            };

            foreach (string profile in _userProfiles)
            {
                string local = Path.Combine(profile, "AppData", "Local");
                AddChromiumCaches(Path.Combine(local, "Google", "Chrome", "User Data"), "Chrome", "chrome", cacheNames, running);
                AddChromiumCaches(Path.Combine(local, "Microsoft", "Edge", "User Data"), "Edge", "msedge", cacheNames, running);
                AddChromiumCaches(Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"), "Brave", "brave", cacheNames, running);
                AddChromiumCaches(Path.Combine(local, "Vivaldi", "User Data"), "Vivaldi", "vivaldi", cacheNames, running);

                AddFirefoxCaches(Path.Combine(local, "Mozilla", "Firefox", "Profiles"), running);
                bool operaRunning;
                running.TryGetValue("opera", out operaRunning);
                AddBrowserCache(Path.Combine(local, "Opera Software", "Opera Stable", "Cache"), "Opera", operaRunning);
                AddBrowserCache(Path.Combine(local, "Opera Software", "Opera GX Stable", "Cache"), "Opera GX", operaRunning);
                AddBrowserCache(Path.Combine(profile, "AppData", "Roaming", "Opera Software", "Opera Stable", "Cache"), "Opera", operaRunning);
            }
        }

        private void AddChromiumCaches(
            string userData,
            string browser,
            string process,
            IEnumerable<string> cacheNames,
            IDictionary<string, bool> running)
        {
            if (!Directory.Exists(PathSafety.ToExtended(userData)))
            {
                return;
            }

            bool isRunning;
            running.TryGetValue(process, out isRunning);

            var profileDirectories = new List<string> { userData };
            foreach (string profile in SafeGetDirectories(userData))
            {
                string name = Path.GetFileName(profile);
                if (string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Guest Profile", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "System Profile", StringComparison.OrdinalIgnoreCase))
                {
                    profileDirectories.Add(profile);
                }
            }

            foreach (string profileDirectory in profileDirectories)
            {
                foreach (string cacheName in cacheNames)
                {
                    AddBrowserCache(Path.Combine(profileDirectory, cacheName), browser, isRunning);
                }
            }
        }

        private void AddFirefoxCaches(string profilesRoot, IDictionary<string, bool> running)
        {
            if (!Directory.Exists(PathSafety.ToExtended(profilesRoot)))
            {
                return;
            }

            bool isRunning;
            running.TryGetValue("firefox", out isRunning);

            foreach (string profile in SafeGetDirectories(profilesRoot))
            {
                AddBrowserCache(Path.Combine(profile, "cache2"), "Firefox", isRunning);
                AddBrowserCache(Path.Combine(profile, "startupCache"), "Firefox", isRunning);
                AddBrowserCache(Path.Combine(profile, "shader-cache"), "Firefox", isRunning);
            }
        }

        private void AddBrowserCache(string path, string browser, bool isRunning)
        {
            if (!Directory.Exists(PathSafety.ToExtended(path)))
            {
                return;
            }

            FolderStats stats = ComputeStats(path);
            if (stats.Bytes < MinCacheBytes)
            {
                return;
            }

            ScanCategory category = isRunning ? ScanCategory.B : ScanCategory.A;
            SafetyRating safety = isRunning ? SafetyRating.Caution : SafetyRating.Safe;
            string reasonKey = isRunning ? "Reason.BrowserCacheRunning" : "Reason.BrowserCache";

            AddItem(new ScanItem
            {
                Path = path,
                Name = browser + " — " + Path.GetFileName(path.TrimEnd('\\')),
                Kind = ItemKind.Folder,
                SizeBytes = stats.Bytes,
                FileCount = stats.Files,
                LastModified = stats.LastModified,
                Category = category,
                Safety = safety,
                ReasonKey = reasonKey,
                ReasonArgs = new object[] { browser },
                TypeKey = "Type.BrowserCache",
                RecoverabilityKey = "Recover.Regenerates",
                RecommendDelete = !isRunning,
                RequiresAdmin = false,
                SourceTag = BrowserCacheSource
            });
        }

        private void ScanWindowsUpdateCache()
        {
            string download = Path.Combine(_root, "Windows", "SoftwareDistribution", "Download");
            AddGroupFromDirectory(
                download,
                "Name.WindowsUpdateGroup",
                "Reason.UpdateCache",
                "Type.UpdateCache",
                true,
                "UpdateCache");

            string delivery = Path.Combine(
                _root,
                "Windows",
                "ServiceProfiles",
                "NetworkService",
                "AppData",
                "Local",
                "Microsoft",
                "Windows",
                "DeliveryOptimization",
                "Cache");
            AddGroupFromDirectory(
                delivery,
                "Name.DeliveryOptimizationGroup",
                "Reason.UpdateCache",
                "Type.UpdateCache",
                true,
                "UpdateCache");
        }

        private void AddGroupFromDirectory(
            string root,
            string nameKey,
            string reasonKey,
            string typeKey,
            bool requiresAdmin,
            string sourceTag)
        {
            if (!Directory.Exists(PathSafety.ToExtended(root)))
            {
                return;
            }

            FileSystemInfo[] entries = ListDirectory(root);
            if (entries == null || entries.Length == 0)
            {
                return;
            }

            var paths = new List<string>();
            long bytes = 0;
            int count = 0;
            DateTime? modified = null;

            foreach (FileSystemInfo entry in entries)
            {
                CheckCancelled();
                string path = DisplayPath(entry);
                long itemBytes;
                int itemCount;
                DateTime? itemModified;

                if (entry is DirectoryInfo)
                {
                    FolderStats stats = ComputeStats(path);
                    itemBytes = stats.Bytes;
                    itemCount = stats.Files;
                    itemModified = stats.LastModified;
                }
                else
                {
                    try
                    {
                        var file = (FileInfo)entry;
                        itemBytes = file.Length;
                        itemCount = 1;
                        itemModified = file.LastWriteTime;
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (itemBytes <= 0)
                {
                    continue;
                }

                paths.Add(path);
                bytes += itemBytes;
                count += itemCount;
                if (itemModified.HasValue && (!modified.HasValue || itemModified.Value > modified.Value))
                {
                    modified = itemModified;
                }
            }

            if (paths.Count == 0 || bytes < MinCacheBytes)
            {
                return;
            }

            AddGroup(
                root,
                nameKey,
                paths,
                bytes,
                count,
                modified,
                ScanCategory.B,
                SafetyRating.Caution,
                reasonKey,
                null,
                typeKey,
                "Recover.Regenerates",
                true,
                requiresAdmin,
                sourceTag);
        }

        private void ScanThumbnailCaches()
        {
            foreach (string profile in _userProfiles)
            {
                string explorer = Path.Combine(profile, "AppData", "Local", "Microsoft", "Windows", "Explorer");
                if (!Directory.Exists(PathSafety.ToExtended(explorer)))
                {
                    continue;
                }

                foreach (string file in SafeGetFiles(explorer))
                {
                    string name = Path.GetFileName(file);
                    if (!name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) &&
                        !name.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    long size;
                    DateTime modified;
                    try
                    {
                        var info = new FileInfo(PathSafety.ToExtended(file));
                        size = info.Length;
                        modified = info.LastWriteTime;
                    }
                    catch
                    {
                        continue;
                    }

                    if (size <= 0)
                    {
                        continue;
                    }

                    AddItem(new ScanItem
                    {
                        Path = file,
                        Name = name,
                        Kind = ItemKind.File,
                        SizeBytes = size,
                        FileCount = 1,
                        LastModified = modified,
                        Category = ScanCategory.A,
                        Safety = SafetyRating.Safe,
                        ReasonKey = "Reason.ThumbnailCache",
                        TypeKey = "Type.ThumbnailCache",
                        RecoverabilityKey = "Recover.Regenerates",
                        RecommendDelete = true,
                        RequiresAdmin = false,
                        SourceTag = ThumbnailSource
                    });
                }
            }
        }

        private void ScanINetCache()
        {
            foreach (string profile in _userProfiles)
            {
                string root = Path.Combine(profile, "AppData", "Local", "Microsoft", "Windows", "INetCache");
                AddGroupFromDirectory(
                    root,
                    "Name.INetCacheGroup",
                    "Reason.InternetCache",
                    "Type.InternetCache",
                    false,
                    "InternetCache");
            }
        }

        private void ScanCrashDumpLocations()
        {
            foreach (string profile in _userProfiles)
            {
                string root = Path.Combine(profile, "AppData", "Local", "CrashDumps");
                if (!Directory.Exists(PathSafety.ToExtended(root)))
                {
                    continue;
                }

                FileSystemInfo[] entries = ListDirectory(root);
                if (entries == null)
                {
                    continue;
                }

                foreach (FileSystemInfo entry in entries)
                {
                    CheckCancelled();
                    string path = DisplayPath(entry);
                    _currentPath = path;
                    long size;
                    int count;
                    DateTime? modified;

                    if (entry is DirectoryInfo)
                    {
                        FolderStats stats = ComputeStats(path);
                        size = stats.Bytes;
                        count = stats.Files;
                        modified = stats.LastModified;
                    }
                    else
                    {
                        try
                        {
                            var file = (FileInfo)entry;
                            size = file.Length;
                            count = 1;
                            modified = file.LastWriteTime;
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    if (size <= 0)
                    {
                        continue;
                    }

                    AddItem(new ScanItem
                    {
                        Path = path,
                        Name = entry.Name,
                        Kind = entry is DirectoryInfo ? ItemKind.Folder : ItemKind.File,
                        SizeBytes = size,
                        FileCount = count,
                        LastModified = modified,
                        Category = ScanCategory.A,
                        Safety = SafetyRating.Safe,
                        ReasonKey = "Reason.CrashDump",
                        TypeKey = "Type.CrashDump",
                        RecoverabilityKey = "Recover.RecycleOrPermanent",
                        RecommendDelete = true,
                        RequiresAdmin = false,
                        SourceTag = CrashDumpSource
                    });
                }
            }

            AddCrashDumpFiles(Path.Combine(_root, "Windows", "Minidump"), true);
            AddCrashDumpFiles(Path.Combine(_root, "Windows", "LiveKernelReports"), true);

            string memoryDump = Path.Combine(_root, "Windows", "MEMORY.DMP");
            if (File.Exists(PathSafety.ToExtended(memoryDump)))
            {
                long length = 0;
                DateTime modified = DateTime.Now;
                try
                {
                    var info = new FileInfo(PathSafety.ToExtended(memoryDump));
                    length = info.Length;
                    modified = info.LastWriteTime;
                }
                catch
                {
                    // Keep the known path even if metadata is unavailable.
                }

                AddItem(new ScanItem
                {
                    Path = memoryDump,
                    Name = "MEMORY.DMP",
                    Kind = ItemKind.File,
                    SizeBytes = length,
                    FileCount = 1,
                    LastModified = modified,
                    Category = ScanCategory.A,
                    Safety = SafetyRating.Safe,
                    ReasonKey = "Reason.CrashDump",
                    TypeKey = "Type.CrashDump",
                    RecoverabilityKey = "Recover.RecycleOrPermanent",
                    RecommendDelete = true,
                    RequiresAdmin = true,
                    SourceTag = CrashDumpSource
                });
            }
        }

        private void AddCrashDumpFiles(string root, bool requiresAdmin)
        {
            if (!Directory.Exists(PathSafety.ToExtended(root)))
            {
                return;
            }

            FileSystemInfo[] entries = ListDirectory(root);
            if (entries == null)
            {
                return;
            }

            foreach (FileSystemInfo entry in entries)
            {
                CheckCancelled();
                string path = DisplayPath(entry);
                long size;
                int count;
                DateTime? modified;

                if (entry is DirectoryInfo)
                {
                    FolderStats stats = ComputeStats(path);
                    size = stats.Bytes;
                    count = stats.Files;
                    modified = stats.LastModified;
                }
                else
                {
                    try
                    {
                        var file = (FileInfo)entry;
                        size = file.Length;
                        count = 1;
                        modified = file.LastWriteTime;
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (size <= 0)
                {
                    continue;
                }

                AddItem(new ScanItem
                {
                    Path = path,
                    Name = entry.Name,
                    Kind = entry is DirectoryInfo ? ItemKind.Folder : ItemKind.File,
                    SizeBytes = size,
                    FileCount = count,
                    LastModified = modified,
                    Category = ScanCategory.A,
                    Safety = SafetyRating.Safe,
                    ReasonKey = "Reason.CrashDump",
                    TypeKey = "Type.CrashDump",
                    RecoverabilityKey = "Recover.RecycleOrPermanent",
                    RecommendDelete = true,
                    RequiresAdmin = requiresAdmin,
                    SourceTag = CrashDumpSource
                });
            }
        }

        private void ScanWerReports()
        {
            string wer = Path.Combine(_root, "ProgramData", "Microsoft", "Windows", "WER");
            foreach (string name in new[] { "ReportArchive", "ReportQueue", "Temp" })
            {
                string root = Path.Combine(wer, name);
                FolderStats stats = ComputeStatsIfExists(root);
                if (stats == null || stats.Bytes < MinCacheBytes)
                {
                    continue;
                }

                FileSystemInfo[] entries = ListDirectory(root);
                if (entries == null)
                {
                    continue;
                }

                var paths = entries.Select(DisplayPath).ToList();
                if (paths.Count == 0)
                {
                    continue;
                }

                AddGroup(
                    root,
                    "Name.WerGroup",
                    paths,
                    stats.Bytes,
                    stats.Files,
                    stats.LastModified,
                    ScanCategory.A,
                    SafetyRating.Safe,
                    "Reason.WerReports",
                    null,
                    "Type.WerReports",
                    "Recover.RecycleOrPermanent",
                    true,
                    true,
                    "WerReports");
            }
        }

        private void ScanPrefetch()
        {
            string root = Path.Combine(_root, "Windows", "Prefetch");
            FolderStats stats = ComputeStatsIfExists(root);
            if (stats == null || stats.Bytes < MinCacheBytes)
            {
                return;
            }

            FileSystemInfo[] entries = ListDirectory(root);
            if (entries == null)
            {
                return;
            }

            var paths = entries.Select(DisplayPath).ToList();
            if (paths.Count == 0)
            {
                return;
            }

            AddGroup(
                root,
                "Name.PrefetchGroup",
                paths,
                stats.Bytes,
                stats.Files,
                stats.LastModified,
                ScanCategory.B,
                SafetyRating.Caution,
                "Reason.Prefetch",
                null,
                "Type.Prefetch",
                "Recover.Regenerates",
                true,
                true,
                "Prefetch");
        }

        private void ScanWindowsLogs()
        {
            string logs = Path.Combine(_root, "Windows", "Logs");
            if (Directory.Exists(PathSafety.ToExtended(logs)))
            {
                FileSystemInfo[] entries = ListDirectory(logs);
                if (entries != null)
                {
                    foreach (FileSystemInfo entry in entries)
                    {
                        CheckCancelled();
                        string path = DisplayPath(entry);
                        long size;
                        int count;
                        DateTime? modified;

                        if (entry is DirectoryInfo)
                        {
                            FolderStats stats = ComputeStats(path);
                            size = stats.Bytes;
                            count = stats.Files;
                            modified = stats.LastModified;
                        }
                        else
                        {
                            try
                            {
                                var file = (FileInfo)entry;
                                size = file.Length;
                                count = 1;
                                modified = file.LastWriteTime;
                            }
                            catch
                            {
                                continue;
                            }
                        }

                        if (size < MinCacheBytes)
                        {
                            continue;
                        }

                        AddItem(new ScanItem
                        {
                            Path = path,
                            Name = entry.Name,
                            Kind = entry is DirectoryInfo ? ItemKind.Folder : ItemKind.File,
                            SizeBytes = size,
                            FileCount = count,
                            LastModified = modified,
                            Category = ScanCategory.B,
                            Safety = SafetyRating.Caution,
                            ReasonKey = "Reason.WindowsLogs",
                            TypeKey = "Type.WindowsLogs",
                            RecoverabilityKey = "Recover.RecycleOrPermanent",
                            RecommendDelete = true,
                            RequiresAdmin = true,
                            SourceTag = "WindowsLogs"
                        });
                    }
                }
            }

            string panther = Path.Combine(_root, "Windows", "Panther");
            FolderStats pantherStats = ComputeStatsIfExists(panther);
            if (pantherStats != null && pantherStats.Bytes >= MinCacheBytes)
            {
                AddFolderCandidate(
                    panther,
                    "Panther",
                    pantherStats,
                    ScanCategory.B,
                    SafetyRating.Caution,
                    "Reason.WindowsLogs",
                    null,
                    "Type.WindowsLogs",
                    "Recover.RecycleOrPermanent",
                    true,
                    "WindowsLogs",
                    true);
            }
        }

        private FolderStats ComputeStatsIfExists(string path)
        {
            if (!Directory.Exists(PathSafety.ToExtended(path)))
            {
                return null;
            }

            return ComputeStats(path);
        }

        private void AddGroup(
            string path,
            string nameKey,
            IList<string> paths,
            long bytes,
            int count,
            DateTime? modified,
            ScanCategory category,
            SafetyRating safety,
            string reasonKey,
            object[] reasonArgs,
            string typeKey,
            string recoverabilityKey,
            bool recommendDelete,
            bool requiresAdmin,
            string sourceTag)
        {
            if (paths == null || paths.Count == 0)
            {
                return;
            }

            AddItem(new ScanItem
            {
                Path = path,
                NameKey = nameKey,
                Kind = ItemKind.Group,
                SizeBytes = bytes,
                FileCount = count,
                LastModified = modified,
                Category = category,
                Safety = safety,
                ReasonKey = reasonKey,
                ReasonArgs = reasonArgs,
                TypeKey = typeKey,
                RecoverabilityKey = recoverabilityKey,
                RecommendDelete = recommendDelete,
                RequiresAdmin = requiresAdmin,
                SourceTag = sourceTag,
                GroupPaths = paths.ToList()
            });
        }

        private static bool IsProcessRunning(string processName)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(processName);
                try
                {
                    return processes.Length > 0;
                }
                finally
                {
                    foreach (Process process in processes)
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
