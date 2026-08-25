using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ow2ServerPicker
{
    /// <summary>Where a game path came from, so the UI can show how much to trust it.</summary>
    internal sealed class Located
    {
        public string Path;
        public string Source;
    }

    /// <summary>
    /// Finds the Overwatch client so firewall rules can be scoped to it.
    ///
    /// The target is the GAME, not the launcher. "Overwatch Launcher.exe" is a ~5 MB stub
    /// that reads a product code and starts the matching flavour; the client that actually
    /// talks to the datacenters is "&lt;install&gt;\_retail_\Overwatch.exe". Blizzard's own
    /// uninstall entry points DisplayIcon at exactly that file.
    ///
    /// Every candidate is confirmed with File.Exists before it is returned, so a stale
    /// registry value or a path for a different Blizzard game can never be handed back.
    /// </summary>
    internal static class OverwatchLocator
    {
        /// <summary>Client paths relative to an install root, best flavour first.</summary>
        private static readonly string[] RelativeExes =
        {
            @"_retail_\Overwatch.exe",
            @"_ptr_\Overwatch.exe",
            @"_beta_\Overwatch.exe",
            @"Overwatch.exe",
        };

        public static string Find()
        {
            Located l = Locate();
            return l == null ? null : l.Path;
        }

        public static Located Locate()
        {
            Located l = FromRunningGame();       if (l != null) return l;
            l = FromBattleNetAgent();            if (l != null) return l;
            l = FromRegistry();                  if (l != null) return l;
            return FromProgramRoots();
        }

        private static Located Accept(string path, string source)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                if (!File.Exists(path)) return null;
                // The launcher is never the right answer - it hands off and exits.
                if (System.IO.Path.GetFileName(path)
                        .IndexOf("Launcher", StringComparison.OrdinalIgnoreCase) >= 0) return null;
                return new Located { Path = System.IO.Path.GetFullPath(path), Source = source };
            }
            catch { return null; }
        }

        private static Located FromRoot(string root, string source)
        {
            if (string.IsNullOrEmpty(root)) return null;
            root = root.Replace('/', '\\').Trim().Trim('"');
            foreach (string rel in RelativeExes)
            {
                try
                {
                    Located l = Accept(System.IO.Path.Combine(root, rel), source);
                    if (l != null) return l;
                }
                catch { }
            }
            return null;
        }

        /// <summary>A running client is the most authoritative answer available.</summary>
        private static Located FromRunningGame()
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName("Overwatch"))
                {
                    try
                    {
                        Located l = Accept(p.MainModule.FileName, "running game");
                        if (l != null) return l;
                    }
                    catch { /* 32-bit vs 64-bit or access denied; try the next */ }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Battle.net's Agent keeps its own record of every installed product, including the
        /// install root and the flavour subfolder. It is a protobuf file, but the paths are
        /// stored as plain strings, so they are pulled out by pattern rather than by parsing
        /// the schema - and each candidate still has to exist on disk to be accepted.
        /// </summary>
        private static Located FromBattleNetAgent()
        {
            string[] candidates =
            {
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"Battle.net\Agent\product.db"),
            };

            foreach (string db in candidates)
            {
                try
                {
                    if (!File.Exists(db)) continue;
                    string text = Encoding.ASCII.GetString(File.ReadAllBytes(db));

                    // Drive-rooted paths, forward or back slashes, as Agent writes them.
                    foreach (Match m in Regex.Matches(text, @"[A-Za-z]:[\\/][ -~]{2,180}"))
                    {
                        string root = m.Value;

                        // Trim at the first character Agent would not have put in a path.
                        int cut = root.IndexOfAny(new[] { '\0', '"', '*', '?', '<', '>', '|' });
                        if (cut > 0) root = root.Substring(0, cut);

                        Located l = FromRoot(root, "Battle.net");
                        if (l != null) return l;

                        // The record may already point at the flavour folder itself.
                        l = Accept(System.IO.Path.Combine(root.Replace('/', '\\'), "Overwatch.exe"),
                                   "Battle.net");
                        if (l != null) return l;
                    }
                }
                catch { }
            }
            return null;
        }

        private static Located FromRegistry()
        {
            string[] keys =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Overwatch",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Overwatch",
            };

            foreach (string key in keys)
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(key))
                    {
                        if (k == null) continue;

                        // DisplayIcon is the client executable itself - Blizzard writes the
                        // full _retail_ path there, which beats guessing from InstallLocation.
                        string icon = k.GetValue("DisplayIcon") as string;
                        if (!string.IsNullOrEmpty(icon))
                        {
                            int comma = icon.LastIndexOf(',');
                            if (comma > 2) icon = icon.Substring(0, comma);   // strip ",0"
                            Located l = Accept(icon.Trim().Trim('"'), "registry (DisplayIcon)");
                            if (l != null) return l;
                        }

                        Located r = FromRoot(k.GetValue("InstallLocation") as string,
                                             "registry (InstallLocation)");
                        if (r != null) return r;
                    }
                }
                catch { }
            }
            return null;
        }

        private static Located FromProgramRoots()
        {
            foreach (string root in ProgramRoots())
            {
                Located l = FromRoot(System.IO.Path.Combine(root, "Overwatch"), "folder scan");
                if (l != null) return l;
                l = FromRoot(System.IO.Path.Combine(root, @"Games\Overwatch"), "folder scan");
                if (l != null) return l;
            }
            return null;
        }

        private static IEnumerable<string> ProgramRoots()
        {
            List<string> roots = new List<string>();
            foreach (string v in new[] { "ProgramFiles(x86)", "ProgramFiles", "ProgramW6432" })
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
                        string p = string.IsNullOrEmpty(sub) ? root : System.IO.Path.Combine(root, sub);
                        if (!roots.Contains(p)) roots.Add(p);
                    }
                }
            }
            catch { }
            return roots;
        }
    }
}
