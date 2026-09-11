// Lite XL ARM32 -- LiteXLSetup.exe. WinForms, MSIL/AnyCPU, .NET Framework 4.0.
//
// BATCH-LITEXL-17 §2.1: ONE window, not a wizard. A tablet is a bad place for
// five Next buttons.
//
// Why WinForms and not WPF: WPF is not dependable on these RT images, and the
// Varan installer that already runs on all three of these devices is WinForms.
// Why not Inno/NSIS: both produce NATIVE x86 executables, and Windows RT is ARM
// with no x86 emulation -- such an installer cannot run on the machine this
// targets.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LiteXLRT
{
    internal static class SetupProgram
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string dir = null;
            bool silent = false, startMenu = true, desktop = true, verbs = true;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("/S", StringComparison.OrdinalIgnoreCase)) silent = true;
                else if (a.Equals("/nostartmenu", StringComparison.OrdinalIgnoreCase)) startMenu = false;
                else if (a.Equals("/nodesktop", StringComparison.OrdinalIgnoreCase)) desktop = false;
                else if (a.Equals("/noverbs", StringComparison.OrdinalIgnoreCase)) verbs = false;
                else if (a.Equals("/dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    dir = args[++i];
            }

            if (silent)
            {
                // Exists so the gates can drive a NON-DEFAULT path end to end
                // (G-I4) without a human clicking. Also useful for scripted
                // deployment. Recorded as a decision in INSTALLER-RESULTS.md.
                // /target:winexe has no console, so Console.Out goes nowhere.
                // Write a log the gates can actually read.
                try
                {
                    string exe = Installer.Run(dir ?? App.DefaultDir(), startMenu, desktop, verbs, null);
                    SilentLog("OK installed " + exe);
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
            Application.Run(new SetupForm(dir));
            return 0;
        }

        internal static void SilentLog(string s)
        {
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                           "litexl-setup-silent.log"), s);
            }
            catch (Exception) { }
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly TextBox _dir;
        private readonly Button _browse, _install, _launch, _close;
        private readonly CheckBox _startMenu, _desktop, _verbs;
        private readonly Label _status, _note;
        private readonly ProgressBar _bar;
        private string _installedExe;

        internal SetupForm(string initialDir)
        {
            // §2.4: RT scaling is a known trap in this fleet. Scale by font and
            // state a generous base size rather than relying on defaults.
            Font = new Font("Segoe UI", 10F);
            AutoScaleMode = AutoScaleMode.Font;
            Text = "Install " + App.DisplayName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(600, 340);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { }

            Label head = new Label();
            head.Text = App.DisplayName;
            head.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            head.SetBounds(18, 14, 564, 30);
            Controls.Add(head);

            Label sub = new Label();
            sub.Text = "Installs for you only. No administrator rights are needed.";
            sub.SetBounds(18, 44, 564, 22);
            Controls.Add(sub);

            Label lbl = new Label();
            lbl.Text = "Install to:";
            lbl.SetBounds(18, 84, 100, 22);
            Controls.Add(lbl);

            _dir = new TextBox();
            _dir.SetBounds(18, 108, 460, 26);
            _dir.Text = string.IsNullOrEmpty(initialDir) ? App.DefaultDir() : initialDir;
            Controls.Add(_dir);

            _browse = new Button();
            _browse.Text = "Browse...";
            _browse.SetBounds(488, 106, 94, 30);
            _browse.Click += OnBrowse;
            Controls.Add(_browse);

            _startMenu = new CheckBox();
            _startMenu.Text = "Start menu shortcut";
            _startMenu.Checked = true;
            _startMenu.SetBounds(18, 148, 250, 24);
            Controls.Add(_startMenu);

            _desktop = new CheckBox();
            _desktop.Text = "Desktop shortcut";
            _desktop.Checked = true;
            _desktop.SetBounds(18, 176, 250, 24);
            Controls.Add(_desktop);

            _verbs = new CheckBox();
            _verbs.Text = "Right-click \"Open with Lite XL\"";
            _verbs.Checked = true;
            _verbs.SetBounds(18, 204, 330, 24);
            Controls.Add(_verbs);

            _note = new Label();
            _note.ForeColor = SystemColors.GrayText;
            _note.SetBounds(18, 234, 564, 22);
            _note.Text = Util.VcRuntimeStatus();
            Controls.Add(_note);

            _bar = new ProgressBar();
            _bar.SetBounds(18, 262, 564, 16);
            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Visible = false;
            Controls.Add(_bar);

            _status = new Label();
            _status.SetBounds(18, 284, 460, 44);
            Controls.Add(_status);

            _install = new Button();
            _install.Text = "Install";
            _install.SetBounds(488, 294, 94, 32);
            _install.Click += OnInstall;
            Controls.Add(_install);
            AcceptButton = _install;

            _launch = new Button();
            _launch.Text = "Launch Lite XL";
            _launch.SetBounds(360, 294, 120, 32);
            _launch.Visible = false;
            _launch.Click += OnLaunch;
            Controls.Add(_launch);

            _close = new Button();
            _close.Text = "Close";
            _close.SetBounds(488, 294, 94, 32);
            _close.Visible = false;
            _close.Click += delegate { Close(); };
            Controls.Add(_close);
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Choose a folder to install Lite XL into";
                d.ShowNewFolderButton = true;
                try { if (Directory.Exists(_dir.Text)) d.SelectedPath = _dir.Text; }
                catch (Exception) { }
                if (d.ShowDialog(this) == DialogResult.OK)
                    _dir.Text = Path.Combine(d.SelectedPath, App.ProductName);
            }
        }

        private void OnInstall(object sender, EventArgs e)
        {
            string target = _dir.Text.Trim();
            if (target.Length == 0) { Say("Choose a folder first."); return; }

            // §2.1: say so BEFORE starting and refuse -- do not begin copying and
            // fail halfway. Tested by writing a probe file, not by matching the
            // path string.
            string why;
            if (!Probe.CanWrite(target, out why))
            {
                MessageBox.Show(this, why, "Cannot install there",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Say("Nothing was installed. Choose a different folder.");
                return;
            }

            _install.Enabled = false; _browse.Enabled = false; _dir.Enabled = false;
            _startMenu.Enabled = false; _desktop.Enabled = false; _verbs.Enabled = false;
            _bar.Visible = true; _bar.Style = ProgressBarStyle.Marquee;
            Say("Installing...");

            bool sm = _startMenu.Checked, dt = _desktop.Checked, vb = _verbs.Checked;
            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += delegate(object s2, DoWorkEventArgs a2)
            {
                a2.Result = Installer.Run(target, sm, dt, vb, null);
            };
            bw.RunWorkerCompleted += delegate(object s2, RunWorkerCompletedEventArgs a2)
            {
                _bar.Style = ProgressBarStyle.Continuous;
                _bar.Value = _bar.Maximum;
                if (a2.Error != null)
                {
                    _bar.Visible = false;
                    _install.Enabled = true; _browse.Enabled = true; _dir.Enabled = true;
                    _startMenu.Enabled = true; _desktop.Enabled = true; _verbs.Enabled = true;
                    Say("Install failed. Nothing was left behind.\r\n" + a2.Error.Message);
                    MessageBox.Show(this, a2.Error.Message, "Install failed",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                _installedExe = (string)a2.Result;
                _install.Visible = false;
                _launch.Visible = true;
                _close.Visible = true;
                Say("Installed to:\r\n" + target);
            };
            bw.RunWorkerAsync();
        }

        private void OnLaunch(object sender, EventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_installedExe) && File.Exists(_installedExe))
                {
                    ProcessStartInfo psi = new ProcessStartInfo(_installedExe);
                    psi.WorkingDirectory = Path.GetDirectoryName(_installedExe);
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not start Lite XL",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            Close();
        }

        private void Say(string s) { _status.Text = s; }
    }

    internal static class Installer
    {
        // Returns the path of the installed lite-xl.exe.
        internal static string Run(string targetDir, bool startMenu, bool desktop,
                                   bool verbs, Action<string> progress)
        {
            string here = Path.GetDirectoryName(Application.ExecutablePath);
            string payload = Path.Combine(here, "payload");
            if (!Directory.Exists(payload))
                throw new DirectoryNotFoundException(
                    "The 'payload' folder must sit beside " +
                    Path.GetFileName(Application.ExecutablePath) +
                    ".\r\nExtract the whole download to one folder and run it from there.");

            Record rec = new Record();
            rec.InstallDir = targetDir;
            Directory.CreateDirectory(targetDir);

            // ---- files -------------------------------------------------------
            string[] all = Directory.GetFiles(payload, "*", SearchOption.AllDirectories);
            Array.Sort(all, StringComparer.OrdinalIgnoreCase);
            long bytes = 0;
            foreach (string src in all)
            {
                string rel = src.Substring(payload.Length).TrimStart(Path.DirectorySeparatorChar);
                string tgt = Path.Combine(targetDir, rel);
                Util.GuardPath(targetDir, tgt);
                string d = Path.GetDirectoryName(tgt);
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                File.Copy(src, tgt, true);
                bytes += new FileInfo(tgt).Length;
                rec.Files.Add(rel + " " + Util.Sha256(tgt));
            }

            string exe = Path.Combine(targetDir, App.ExeName);
            if (!File.Exists(exe))
                throw new FileNotFoundException(
                    "The payload does not contain " + App.ExeName +
                    " - the download is incomplete.", exe);

            // ---- the uninstaller, into the install directory -----------------
            // §2.2: it SHIPS, and it is not a script. It must be beside the setup
            // exe in the package, and it must end up in the install directory.
            string uninstSrc = Path.Combine(here, App.UninstallerName);
            if (!File.Exists(uninstSrc))
                throw new FileNotFoundException(
                    App.UninstallerName + " must sit beside " +
                    Path.GetFileName(Application.ExecutablePath) +
                    ". The download is incomplete.", uninstSrc);
            string uninstDst = Path.Combine(targetDir, App.UninstallerName);
            Util.GuardPath(targetDir, uninstDst);
            File.Copy(uninstSrc, uninstDst, true);
            rec.Files.Add(App.UninstallerName + " " + Util.Sha256(uninstDst));

            // ---- shortcuts ---------------------------------------------------
            if (startMenu)
            {
                string lnk = App.StartMenuLnk();
                Shortcut.Create(lnk, exe, targetDir, exe, App.DisplayName);
                rec.Shortcuts.Add(lnk);
            }
            if (desktop)
            {
                string lnk = App.DesktopLnk();
                Shortcut.Create(lnk, exe, targetDir, exe, App.DisplayName);
                rec.Shortcuts.Add(lnk);
            }

            // ---- registry: HKCU only ----------------------------------------
            string icon = exe + ", 0";
            string cmd1 = "\"" + exe + "\"  \"%1\"";
            string cmdV = "\"" + exe + "\"  \"%V\"";
            if (verbs)
            {
                string[] roots = new string[] {
                    @"Software\Classes\*\shell\" + App.VerbKey,
                    @"Software\Classes\Directory\shell\" + App.VerbKey,
                    @"Software\Classes\Directory\Background\shell\" + App.VerbKey
                };
                foreach (string root in roots)
                {
                    bool background = root.IndexOf(@"Directory\Background",
                                                   StringComparison.OrdinalIgnoreCase) >= 0;
                    Set(rec, root, "", App.VerbText);
                    Set(rec, root, "Icon", icon);
                    // Directory\Background takes %V, the other two take %1. That
                    // is measured from the official install, not remembered.
                    Set(rec, root + @"\command", "", background ? cmdV : cmd1);
                }
            }

            string k = App.UninstallKeyPath;
            Set(rec, k, "DisplayName", App.DisplayName);
            Set(rec, k, "DisplayVersion", App.Version);
            Set(rec, k, "Publisher", App.Publisher);
            Set(rec, k, "DisplayIcon", icon);
            Set(rec, k, "InstallLocation", targetDir + Path.DirectorySeparatorChar);
            Set(rec, k, "UninstallString", "\"" + uninstDst + "\"");
            Set(rec, k, "QuietUninstallString", "\"" + uninstDst + "\" /silent");
            Set(rec, k, "URLInfoAbout", App.AboutUrl);
            Set(rec, k, "NoModify", "1");
            Set(rec, k, "NoRepair", "1");
            Set(rec, k, "InstallDate",
                DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
            Set(rec, k, "EstimatedSize",
                (bytes / 1024).ToString(System.Globalization.CultureInfo.InvariantCulture));

            rec.Write(Path.Combine(targetDir, App.RecordName));
            return exe;
        }

        // Writes one HKCU value and records both the value and its key.
        private static void Set(Record rec, string key, string name, string value)
        {
            using (RegistryKey rk = Registry.CurrentUser.CreateSubKey(key))
            {
                if (rk == null) throw new InvalidOperationException("could not create HKCU\\" + key);
                rk.SetValue(name, value, RegistryValueKind.String);
            }
            rec.RegValues.Add(key + "|" + (name.Length == 0 ? "(default)" : name));
            if (!rec.RegKeys.Contains(key)) rec.RegKeys.Add(key);
        }
    }
}
