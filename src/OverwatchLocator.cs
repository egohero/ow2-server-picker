using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Ow2ServerPicker
{
    /// <summary>Best-effort discovery of Overwatch.exe so rules can be scoped to the game.</summary>
    internal static class OverwatchLocator
    {
        private static readonly string[] RelativePaths =
        {
            @"Overwatch\_retail_\Overwatch.exe",
            @"Overwatch\Overwatch.exe",
            @"Games\Overwatch\_retail_\Overwatch.exe",
        };

        public static string Find()
        {
            // A running game is the most reliable answer available.
            try
            {
                foreach (Process p in Process.GetProcessesByName("Overwatch"))
                {
                    try
                    {
                        string path = p.MainModule.FileName;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
                    }
                    catch { }
                }
            }
            catch { }

            string fromRegistry = FromUninstallKeys();
            if (fromRegistry != null) return fromRegistry;

            foreach (string root in ProgramRoots())
            {
                foreach (string rel in RelativePaths)
                {
                    try
                    {
                        string candidate = Path.Combine(root, rel);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch { }
                }
            }
            return null;
        }

        private static IEnumerable<string> ProgramRoots()
        {
            List<string> roots = new List<string>();
            string[] vars = { "ProgramFiles(x86)", "ProgramFiles", "ProgramW6432" };
            foreach (string v in vars)
            {
                string p = Environment.GetEnvironmentVariable(v);
                if (!string.IsNullOrEmpty(p) && !roots.Contains(p)) roots.Add(p);
            }

            // Large games often live off the system drive entirely.
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    string root = d.RootDirectory.FullName;
                    foreach (string sub in new[] { "Program Files (x86)", "Program Files", "" })
                    {
                        string p = string.IsNullOrEmpty(sub) ? root : Path.Combine(root, sub);
                        if (!roots.Contains(p)) roots.Add(p);
                    }
                }
            }
            catch { }
            return roots;
        }

        private static string FromUninstallKeys()
        {
            string[] hives =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Overwatch",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Overwatch",
            };

            foreach (string hive in hives)
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(hive))
                    {
                        if (k == null) continue;
                        string install = k.GetValue("InstallLocation") as string;
                        if (string.IsNullOrEmpty(install)) continue;

                        string[] candidates =
                        {
                            Path.Combine(install, @"_retail_\Overwatch.exe"),
                            Path.Combine(install, "Overwatch.exe"),
                        };
                        foreach (string c in candidates)
                            if (File.Exists(c)) return c;
                    }
                }
                catch { }
            }
            return null;
        }
    }
}
