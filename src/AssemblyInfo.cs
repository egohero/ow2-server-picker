using System.Reflection;
using System.Runtime.InteropServices;

// Single source of truth for the version. Shown in the title bar and the status line, and
// carried in the file's own properties so Explorer's Details tab agrees with the UI.
[assembly: AssemblyTitle("Overwatch 2 Server Picker")]
[assembly: AssemblyDescription("Choose which Overwatch 2 datacenters the game may connect to")]
[assembly: AssemblyProduct("Overwatch 2 Server Picker")]
[assembly: AssemblyCopyright("MIT licensed. Not affiliated with Blizzard Entertainment.")]

// Derived from git by tools/gen-version.ps1, not hand-maintained. const strings are
// compile-time constants, so they are legal in attributes.
[assembly: AssemblyVersion(Ow2ServerPicker.BuildInfo.Version)]
[assembly: AssemblyFileVersion(Ow2ServerPicker.BuildInfo.Version)]
[assembly: AssemblyInformationalVersion(
    Ow2ServerPicker.BuildInfo.Version + " (" + Ow2ServerPicker.BuildInfo.Commit + ")")]

[assembly: ComVisible(false)]
