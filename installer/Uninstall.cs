// Lite XL ARM32 -- LiteXLUninstall.exe. WinForms, MSIL/AnyCPU, .NET Framework 4.0.
//
// BATCH-LITEXL-17 §2.2. A SEPARATE assembly rather than a switch on the setup
// exe, for one reason: it is written into the install directory and registered
// as the ARP UninstallString, and the setup exe carries an install UI and a
// payload dependency it would never use there. Recorded as a §2.2 decision.
//
// It removes EXACTLY what the install record lists -- files, shortcuts, registry
// values and keys. There is no wildcard delete in this file and there must never
// be one. If the record is missing it removes nothing and says so.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LiteXLRT
{
    internal static class UninstallProgram
    {
        [STAThread]
        private static int Main(string[] args)
        {
            bool silent = false;
            foreach (string a in args)
                if (a.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("/S", StringComparison.OrdinalIgnoreCase)) silent = true;

            if (silent)
            {
                try
                {
                    SilentLog(Uninstaller.Run());
                    return 0;
                }
                catch (Exception ex)
                {
                    SilentLog("ERROR " + ex.GetType().Name + ": " + ex.Message);
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UninstallForm());
            return 0;
        }

        internal static void SilentLog(string s)
        {
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                           "litexl-uninstall-silent.log"), s);
            }
            catch (Exception) { }
        }
    }

    internal sealed class UninstallForm : Form
    {
        private readonly Label _text;
        private readonly Button _yes, _no, _close;

        internal UninstallForm()
        {
            Font = new Font("Segoe UI", 10F);
            AutoScaleMode = AutoScaleMode.Font;
            Text = "Uninstall " + App.DisplayName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 230);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { }

            Label head = new Label();
            head.Text = "Remove " + App.DisplayName + "?";
            head.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            head.SetBounds(18, 16, 524, 30);
            Controls.Add(head);

            _text = new Label();
            _text.SetBounds(18, 52, 524, 110);
            _text.Text = "This removes the program files, its Start menu and Desktop shortcuts, "
                       + "and its right-click menu entries.\r\n\r\n"
                       + "Your settings in %USERPROFILE%\\.config\\lite-xl are NOT touched.";
            Controls.Add(_text);

            _yes = new Button();
            _yes.Text = "Uninstall";
            _yes.SetBounds(330, 176, 100, 34);
            _yes.Click += OnYes;
            Controls.Add(_yes);

            _no = new Button();
            _no.Text = "Cancel";
            _no.SetBounds(440, 176, 100, 34);
            _no.Click += delegate { Close(); };
            Controls.Add(_no);
            CancelButton = _no;

            _close = new Button();
            _close.Text = "Close";
            _close.SetBounds(440, 176, 100, 34);
            _close.Visible = false;
            _close.Click += delegate { Close(); };
            Controls.Add(_close);
        }

        private void OnYes(object sender, EventArgs e)
        {
            _yes.Enabled = false; _no.Enabled = false;
            string result;
            try { result = Uninstaller.Run(); }
            catch (Exception ex) { result = "Uninstall failed:\r\n" + ex.Message; }
            _text.Text = result;
            _yes.Visible = false; _no.Visible = false;
            _close.Visible = true;
            AcceptButton = _close;
        }
    }

    internal static class Uninstaller
    {
        internal static string Run()
        {
            // The record sits beside this executable -- that is where the setup
            // put both. Falling back to the default directory would be a guess,
            // and guessing what to delete is exactly what this must not do.
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            string rec = Path.Combine(dir, App.RecordName);
            if (!File.Exists(rec))
                return "No install record was found next to this uninstaller:\r\n    " + rec +
                       "\r\n\r\nNothing was removed. This uninstaller does not guess what to delete.";

            Record r = Record.Read(rec);
            if (!string.IsNullOrEmpty(r.InstallDir)) dir = r.InstallDir;

            int nv = 0, nk = 0, nf = 0, ns = 0;

            // ---- registry values --------------------------------------------
            foreach (string entry in r.RegValues)
            {
                string[] p = entry.Split('|');
                if (p.Length != 2) continue;
                string name = p[1] == "(default)" ? "" : p[1];
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(p[0], true))
                {
                    if (k == null) continue;
                    foreach (string existing in k.GetValueNames())
                        if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                        { k.DeleteValue(name, false); nv++; break; }
                }
            }

            // ---- registry keys, but only if already empty --------------------
            foreach (string key in r.RegKeys)
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(key, false))
                {
                    if (k == null) continue;
                    // A key still holding something we did not record belongs to
                    // somebody else. Leave it.
                    if (k.SubKeyCount != 0 || k.ValueCount != 0) continue;
                }
                try { Registry.CurrentUser.DeleteSubKey(key, false); nk++; }
                catch (ArgumentException) { }
            }

            // ---- shortcuts ---------------------------------------------------
            // Only the exact paths the record lists, and only if the .lnk still
            // points at this install -- a shortcut the user re-pointed somewhere
            // else is theirs now.
            foreach (string lnk in r.Shortcuts)
            {
                try
                {
                    if (!File.Exists(lnk)) continue;
                    string target = Shortcut.Resolve(lnk);
                    if (target != null && !target.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                        continue;
                    File.Delete(lnk);
                    ns++;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            // ---- files -------------------------------------------------------
            foreach (string rel in r.Files)
            {
                string full = Path.Combine(dir, rel);
                try { Util.GuardPath(dir, full); }
                catch (InvalidOperationException) { continue; }
                if (!File.Exists(full)) continue;
                try { File.Delete(full); nf++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }   // this running image
            }

            try { if (File.Exists(rec)) { File.Delete(rec); nf++; } }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            PruneEmpty(dir, dir);

            string tail;
            if (Directory.Exists(dir))
            {
                List<string> left = new List<string>(
                    Directory.GetFiles(dir, "*", SearchOption.AllDirectories));
                string self = Path.GetFullPath(Application.ExecutablePath);
                bool onlyUs = true;
                foreach (string f in left)
                    if (!string.Equals(Path.GetFullPath(f), self, StringComparison.OrdinalIgnoreCase))
                    { onlyUs = false; break; }

                if (onlyUs && Util.ScheduleSelfDelete(dir))
                    tail = "The folder is being removed now:\r\n    " + dir;
                else if (onlyUs)
                    tail = "Could not start the cleanup step. Delete this folder by hand:\r\n    " + dir;
                else
                    tail = left.Count + " file(s) in the folder were not part of this install, "
                         + "so it was left in place:\r\n    " + dir;
            }
            else tail = "The folder is gone.";

            return "Removed " + nf + " files, " + ns + " shortcuts, "
                 + nv + " registry values and " + nk + " registry keys.\r\n\r\n" + tail
                 + "\r\n\r\nYour settings in %USERPROFILE%\\.config\\lite-xl were not touched.";
        }

        private static void PruneEmpty(string root, string dir)
        {
            if (!Directory.Exists(dir)) return;
            try { Util.GuardPath(root, Path.Combine(dir, "x")); }
            catch (InvalidOperationException) { return; }
            foreach (string sub in Directory.GetDirectories(dir)) PruneEmpty(root, sub);
            try
            {
                if (Directory.GetFiles(dir).Length == 0 && Directory.GetDirectories(dir).Length == 0)
                    Directory.Delete(dir);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
