// Lite XL ARM32 -- shared by LiteXLSetup.exe and LiteXLUninstall.exe.
//
// BATCH-LITEXL-17. Pattern carried from D:\repo\varan-release\installer\Setup.cs,
// which is field-proven on these exact devices. Two constraints come with it and
// are fatal if dropped:
//
//   1. /platform:anycpu ONLY -- never anycpu32bitpreferred, which sets
//      32BITREQUIRED and will not run on ARM32 at all. csc's DEFAULT for
//      /target:winexe is anycpu32bitpreferred, so it must be stated.
//   2. NOTHING from System.IO.Compression[.FileSystem]. Both are .NET 4.5-only
//      and absent from some Windows RT images -- that was a real Varan field
//      failure on a Spanish-locale RT device. Our payload is a DIRECTORY beside
//      the setup exe, not a zip, so the hazard cannot arise here; the build
//      refuses the reference anyway so it cannot creep back.
//
// Everything is per-user: %LOCALAPPDATA% and HKCU only, no elevation, ever.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace LiteXLRT
{
    internal static class App
    {
        // Unchanged from BATCH-LITEXL-14 §4.3. Generated for this project; it is
        // deliberately NOT upstream's {06761240-...} so an official x64 install
        // and this one never collide in Add/Remove Programs.
        internal const string AppId = "{FF848C65-3773-4E63-B209-B4FE47563810}";

        internal const string ProductName = "Lite XL";
        internal const string DisplayName = "Lite XL 2.1.8 (ARM32, Windows RT)";
        internal const string Version = "2.1.8";
        internal const string Publisher = "lite-xl-rt (unofficial ARM32 port)";
        internal const string AboutUrl = "https://github.com/hamed7ir/lite-xl-rt";

        internal const string ExeName = "lite-xl.exe";
        internal const string UninstallerName = "LiteXLUninstall.exe";
        internal const string RecordName = "install-record.txt";
        internal const string ShortcutName = "Lite XL.lnk";

        // The verb key name is deliberately NOT plain "Lite XL". HKCU wins the
        // HKCR merge, so reusing the name an official x64 install registers under
        // HKLM would silently SHADOW that entry on a machine with both.
        internal const string VerbKey = "Lite XL ARM32";
        internal const string VerbText = "Open with Lite XL (ARM32)";

        internal const string UninstallKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId;

        internal static string DefaultDir()
        {
            string bas = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(Path.Combine(bas, "Programs"), ProductName);
        }

        internal static string StartMenuLnk()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                                ShortcutName);
        }

        internal static string DesktopLnk()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                                ShortcutName);
        }
    }

    // ------------------------------------------------------------------------
    // Can we actually write there? §2.1 is explicit: probe by WRITING and
    // DELETING a file, not by pattern-matching the path string. "C:\Program
    // Files" is the obvious case but not the only one, and a string test would
    // both miss cases and invent them (a writable folder that merely looks
    // system-ish would be refused for no reason).
    // ------------------------------------------------------------------------
    internal static class Probe
    {
        internal static bool CanWrite(string dir, out string why)
        {
            why = null;
            string full;
            try { full = Path.GetFullPath(dir); }
            catch (Exception ex) { why = "That is not a usable path: " + ex.Message; return false; }

            // Probe the nearest EXISTING ancestor -- the target itself usually
            // does not exist yet, and a permission error on creating it is the
            // thing we are trying to find out about.
            string probeDir = full;
            while (!Directory.Exists(probeDir))
            {
                string parent = Path.GetDirectoryName(probeDir);
                if (string.IsNullOrEmpty(parent) || parent == probeDir) break;
                probeDir = parent;
            }
            if (!Directory.Exists(probeDir))
            {
                why = "That drive or folder does not exist.";
                return false;
            }

            string probe = Path.Combine(probeDir, "litexl-write-probe.tmp");
            try
            {
                using (FileStream fs = new FileStream(probe, FileMode.Create, FileAccess.Write,
                                                      FileShare.None, 64, FileOptions.DeleteOnClose))
                {
                    fs.WriteByte(0x2E);
                }
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                why = "Windows will not let this account write to:\r\n    " + probeDir +
                      "\r\n\r\nThis installer does not ask for administrator rights, so pick a " +
                      "folder inside your own profile instead.";
                return false;
            }
            catch (Exception ex)
            {
                why = "Cannot write to:\r\n    " + probeDir + "\r\n\r\n" + ex.Message;
                return false;
            }
            finally
            {
                try { if (File.Exists(probe)) File.Delete(probe); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    // ------------------------------------------------------------------------
    // Shortcuts via IShellLink + IPersistFile, declared as COM interop here.
    //
    // NOT WScript.Shell: that is a Windows Script Host feature and can be absent
    // or disabled on a locked-down tablet. The shell link COM object is part of
    // the shell itself.
    //
    // Resolve() is the other half and it is why this class is not just Create():
    // BATCH-LITEXL-17 §2.3 requires a shortcut be verified by READING ITS TARGET
    // BACK, not by File.Exists -- a zero-byte .lnk passes an existence check.
    // ------------------------------------------------------------------------
    internal static class Shortcut
    {
        internal static void Create(string lnk, string target, string workDir,
                                    string iconPath, string description)
        {
            // Deliberately NOT wrapped in catch{}: a shortcut that silently fails
            // to be created is exactly what the gate exists to catch, and the
            // caller records the failure.
            IShellLinkW link = (IShellLinkW)new CShellLink();
            link.SetPath(target);
            link.SetArguments("");
            link.SetWorkingDirectory(workDir ?? "");
            link.SetDescription(description ?? "");
            if (!string.IsNullOrEmpty(iconPath)) link.SetIconLocation(iconPath, 0);
            string dir = Path.GetDirectoryName(lnk);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            ((IPersistFile)link).Save(lnk, true);
        }

        // Reads a .lnk back and returns the path it points at, or null.
        internal static string Resolve(string lnk)
        {
            try
            {
                if (!File.Exists(lnk)) return null;
                IShellLinkW link = (IShellLinkW)new CShellLink();
                ((IPersistFile)link).Load(lnk, 0);
                StringBuilder sb = new StringBuilder(600);
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                string s = sb.ToString();
                return string.IsNullOrEmpty(s) ? null : s;
            }
            catch (Exception) { return null; }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink { }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder f, int c, IntPtr d, int fl);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder n, int c);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string n);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder d, int c);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string d);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder a, int c);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string a);
            void GetHotkey(out short h);
            void SetHotkey(short h);
            void GetShowCmd(out int c);
            void SetShowCmd(int c);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder p, int c, out int i);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string p, int i);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string p, int r);
            void Resolve(IntPtr hwnd, int fl);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string p);
        }

        [ComImport, Guid("0000010b-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid c);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string f, int m);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string f, [MarshalAs(UnmanagedType.Bool)] bool remember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string f);
            void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder f);
        }
    }

    // ------------------------------------------------------------------------
    // The install record. Uninstall replays THIS and nothing else -- there is no
    // wildcard delete anywhere in either assembly and there must never be one.
    // If the record is missing, uninstall removes nothing and says so, which is
    // the safe failure.
    // ------------------------------------------------------------------------
    internal sealed class Record
    {
        internal string InstallDir = "";
        internal readonly List<string> Files = new List<string>();      // relative
        internal readonly List<string> RegValues = new List<string>();  // key|name
        internal readonly List<string> RegKeys = new List<string>();    // key
        internal readonly List<string> Shortcuts = new List<string>();  // absolute .lnk

        internal void Write(string path)
        {
            List<string> l = new List<string>();
            l.Add("# Lite XL ARM32 install record, format 2");
            l.Add("# Uninstall removes EXACTLY these entries and nothing else.");
            l.Add("# format 2 adds SHORTCUT lines (BATCH-LITEXL-17 §2.3).");
            l.Add("APPID " + App.AppId);
            l.Add("PRODUCT " + App.ProductName);
            l.Add("VERSION " + App.Version);
            l.Add("INSTALLDIR " + InstallDir);
            l.Add("INSTALLED " + DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
            foreach (string f in Files) l.Add("FILE " + f);
            foreach (string s in Shortcuts) l.Add("SHORTCUT " + s);
            foreach (string v in RegValues) l.Add("REGVAL HKCU|" + v);
            // deepest first, so a key is only removed after everything under it
            List<string> keys = new List<string>(RegKeys);
            keys.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
            foreach (string k in keys) l.Add("REGKEY HKCU|" + k);
            File.WriteAllLines(path, l.ToArray(), Encoding.UTF8);
        }

        internal static Record Read(string path)
        {
            Record r = new Record();
            foreach (string raw in File.ReadAllLines(path))
            {
                string s = raw.Trim();
                if (s.Length == 0 || s[0] == '#') continue;
                if (s.StartsWith("INSTALLDIR ", StringComparison.Ordinal))
                    r.InstallDir = s.Substring(11).Trim();
                else if (s.StartsWith("FILE ", StringComparison.Ordinal))
                {
                    string v = s.Substring(5);
                    int sp = v.LastIndexOf(' ');       // "<rel> <sha256>"
                    r.Files.Add(sp > 0 ? v.Substring(0, sp) : v);
                }
                else if (s.StartsWith("SHORTCUT ", StringComparison.Ordinal))
                    r.Shortcuts.Add(s.Substring(9).Trim());
                else if (s.StartsWith("REGVAL HKCU|", StringComparison.Ordinal))
                    r.RegValues.Add(s.Substring(12));
                else if (s.StartsWith("REGKEY HKCU|", StringComparison.Ordinal))
                    r.RegKeys.Add(s.Substring(12));
            }
            return r;
        }
    }

    internal static class Util
    {
        internal static string Sha256(string path)
        {
            using (SHA256 h = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
            {
                byte[] d = h.ComputeHash(fs);
                StringBuilder sb = new StringBuilder(64);
                for (int i = 0; i < d.Length; i++)
                    sb.Append(d[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        // Refuse to touch anything outside the install root. Asserted at runtime,
        // not merely intended.
        internal static void GuardPath(string root, string full)
        {
            string r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(full).StartsWith(r, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "refusing to touch a path outside the install directory: " + full);
        }

        // Detached cmd.exe retry loop that removes the install directory once
        // this process has exited and released its own image.
        //
        // NOT MoveFileEx(..., DELAY_UNTIL_REBOOT): that writes to HKLM
        // PendingFileRenameOperations and needs elevation, and it can only be
        // verified after a reboot. BATCH-LITEXL-14-AMEND-1 §3 settled this and
        // BATCH-LITEXL-17 §2.2 says keep it. Writes no file anywhere -- the
        // command is passed inline.
        internal static bool ScheduleSelfDelete(string dir)
        {
            try
            {
                string q = "\"" + dir + "\"";
                string cmd = "/c for /l %n in (1,1,30) do @(if exist " + q +
                             " (ping -n 2 127.0.0.1 >nul & rd /s /q " + q + " 2>nul))";
                System.Diagnostics.ProcessStartInfo psi =
                    new System.Diagnostics.ProcessStartInfo("cmd.exe", cmd);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                // must not sit inside the folder it is about to delete
                psi.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
                return System.Diagnostics.Process.Start(psi) != null;
            }
            catch (Exception) { return false; }
        }

        // The VC++ runtime question, answered honestly.
        //
        // lite-xl.exe is linked with the STATIC CRT (/MT). Measured on the
        // shipped binary: 10 imported DLLs, all of them OS DLLs, and zero of
        // VCRUNTIME140 / MSVCP* / api-ms-win-crt-* / ucrtbase. It therefore does
        // NOT need the Visual C++ redistributable, and this is reported as
        // information only -- it never blocks an install. A hard prerequisite
        // check here would refuse to install on machines where the editor
        // demonstrably runs, including the box this was built on, which has no
        // VC++ 2015+ runtime registered at all.
        internal static string VcRuntimeStatus()
        {
            string[] keys = new string[] {
                @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\ARM",
                @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x86",
                @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64",
                @"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x86"
            };
            string found = null;
            foreach (string k in keys)
            {
                try
                {
                    using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(k))
                    {
                        if (rk == null) continue;
                        object v = rk.GetValue("Version");
                        if (v != null) { found = v.ToString(); break; }
                        found = "installed";
                        break;
                    }
                }
                catch (Exception) { }
            }
            if (found != null)
                return "Visual C++ 2015-2022 runtime: " + found + " (not required by this build)";
            return "Visual C++ runtime: not installed - and not required, "
                 + "this build is statically linked";
        }
    }
}
