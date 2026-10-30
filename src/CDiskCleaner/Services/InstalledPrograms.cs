using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace CDiskCleaner.Services
{
    /// <summary>
    /// Reads installed-program names from the registry. This class never writes to the registry.
    /// </summary>
    public sealed class InstalledPrograms
    {
        private sealed class Entry
        {
            public string Name;
            public string NormalizedName;
            public string Location;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private bool _loaded;

        public int Count
        {
            get { return _entries.Count; }
        }

        public static InstalledPrograms Load()
        {
            var result = new InstalledPrograms();
            result.Read(RegistryHive.LocalMachine, RegistryView.Registry64);
            result.Read(RegistryHive.LocalMachine, RegistryView.Registry32);
            result.Read(RegistryHive.CurrentUser, RegistryView.Registry64);
            result.Read(RegistryHive.CurrentUser, RegistryView.Registry32);
            return result;
        }

        public bool MatchesFolder(string folderPath, string folderName)
        {
            // If the registry could not be read, never claim that a folder is orphaned.
            if (!_loaded)
            {
                return true;
            }

            string normalizedFolder = NormalizeName(folderName);
            if (normalizedFolder.Length < 3)
            {
                return true;
            }

            foreach (Entry entry in _entries)
            {
                if (!string.IsNullOrEmpty(entry.Location) &&
                    (PathSafety.IsUnderOrEqual(folderPath, entry.Location) ||
                     PathSafety.IsUnderOrEqual(entry.Location, folderPath)))
                {
                    return true;
                }

                if (entry.NormalizedName.Length >= 3 &&
                    (entry.NormalizedName.IndexOf(normalizedFolder, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     normalizedFolder.IndexOf(entry.NormalizedName, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }
            }

            return false;
        }

        private void Read(RegistryHive hive, RegistryView view)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (uninstall == null)
                    {
                        return;
                    }

                    _loaded = true;
                    foreach (string subKeyName in uninstall.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey app = uninstall.OpenSubKey(subKeyName))
                            {
                                if (app == null)
                                {
                                    continue;
                                }

                                string displayName = app.GetValue("DisplayName") as string;
                                if (string.IsNullOrWhiteSpace(displayName))
                                {
                                    continue;
                                }

                                object systemComponent = app.GetValue("SystemComponent");
                                if (systemComponent is int && (int)systemComponent == 1)
                                {
                                    continue;
                                }

                                string location = (app.GetValue("InstallLocation") as string ?? string.Empty).Trim().Trim('"');
                                _entries.Add(new Entry
                                {
                                    Name = displayName.Trim(),
                                    NormalizedName = NormalizeName(displayName),
                                    Location = string.IsNullOrWhiteSpace(location) ? string.Empty : PathSafety.Normalize(location)
                                });
                            }
                        }
                        catch
                        {
                            // One broken registry key must not abort the scan.
                        }
                    }
                }
            }
            catch
            {
                // Reading HKLM/HKCU can fail under restricted accounts.
            }
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            bool lastWasSpace = true;
            foreach (char c in value.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                    lastWasSpace = false;
                }
                else if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
            }

            return builder.ToString().Trim();
        }
    }
}
