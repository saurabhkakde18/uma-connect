using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("UMA Connect Setup")]
[assembly: AssemblyDescription("Installer for UMA Connect")]
[assembly: AssemblyCompany("UMA Steel Alloys and Power Limited")]
[assembly: AssemblyProduct("UMA Connect")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace UMAConnectInstaller
{
    public class InstallerForm : Form
    {
        private TextBox _pathBox;
        private Button _browseBtn;
        private CheckBox _desktopCheck;
        private CheckBox _startMenuCheck;
        private CheckBox _launchCheck;
        private ProgressBar _progressBar;
        private Label _statusLabel;
        private Button _installBtn;
        private Button _cancelBtn;

        public InstallerForm()
        {
            Text = "UMA Connect Setup";
            ClientSize = new Size(520, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(244, 247, 252);
            Font = new Font("Segoe UI", 9.5f);

            LoadEmbeddedIcon();
            InitializeUi();
        }

        private void LoadEmbeddedIcon()
        {
            try
            {
                using (Stream iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app_icon.ico"))
                {
                    if (iconStream != null)
                    {
                        Icon = new Icon(iconStream);
                    }
                }
            }
            catch { }
        }

        private void InitializeUi()
        {
            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 82,
                BackColor = Color.FromArgb(15, 23, 42)
            };

            var titleLabel = new Label
            {
                Text = "UMA Connect",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(22, 16)
            };

            var subtitleLabel = new Label
            {
                Text = "Enterprise Operations & Query Portal (4 Glass Themes) — Setup Wizard",
                ForeColor = Color.FromArgb(186, 210, 245),
                Font = new Font("Segoe UI", 9.5f),
                AutoSize = true,
                Location = new Point(24, 48)
            };

            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(subtitleLabel);
            Controls.Add(headerPanel);

            var dirLabel = new Label
            {
                Text = "Installation Folder:",
                Location = new Point(24, 102),
                AutoSize = true
            };
            Controls.Add(dirLabel);

            string defaultInstallDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "UMA Connect"
            );

            _pathBox = new TextBox
            {
                Text = defaultInstallDir,
                Location = new Point(24, 125),
                Width = 382
            };
            Controls.Add(_pathBox);

            _browseBtn = new Button
            {
                Text = "Browse...",
                Location = new Point(414, 123),
                Width = 82,
                Height = 27
            };
            _browseBtn.Click += BrowseClicked;
            Controls.Add(_browseBtn);

            _desktopCheck = new CheckBox
            {
                Text = "Create a Desktop shortcut (UMA Connect)",
                Checked = true,
                AutoSize = true,
                Location = new Point(24, 168)
            };
            Controls.Add(_desktopCheck);

            _startMenuCheck = new CheckBox
            {
                Text = "Create Start Menu entry",
                Checked = true,
                AutoSize = true,
                Location = new Point(24, 195)
            };
            Controls.Add(_startMenuCheck);

            _launchCheck = new CheckBox
            {
                Text = "Launch UMA Connect after installation",
                Checked = true,
                AutoSize = true,
                Location = new Point(24, 222)
            };
            Controls.Add(_launchCheck);

            _statusLabel = new Label
            {
                Text = "Ready to install UMA Connect.",
                Location = new Point(24, 258),
                Width = 472,
                ForeColor = Color.FromArgb(51, 65, 85)
            };
            Controls.Add(_statusLabel);

            _progressBar = new ProgressBar
            {
                Location = new Point(24, 282),
                Width = 472,
                Height = 18,
                Minimum = 0,
                Maximum = 100
            };
            Controls.Add(_progressBar);

            _installBtn = new Button
            {
                Text = "Install",
                Location = new Point(316, 314),
                Width = 88,
                Height = 30,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _installBtn.FlatAppearance.BorderSize = 0;
            _installBtn.Click += InstallClicked;
            Controls.Add(_installBtn);

            _cancelBtn = new Button
            {
                Text = "Cancel",
                Location = new Point(412, 314),
                Width = 84,
                Height = 30
            };
            _cancelBtn.Click += delegate { Close(); };
            Controls.Add(_cancelBtn);
        }

        private void BrowseClicked(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select folder where UMA Connect should be installed";
                dlg.SelectedPath = _pathBox.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    string selected = dlg.SelectedPath.TrimEnd('\\', '/');
                    if (!selected.EndsWith("UMA Connect", StringComparison.OrdinalIgnoreCase))
                    {
                        selected = Path.Combine(selected, "UMA Connect");
                    }
                    _pathBox.Text = selected;
                }
            }
        }

        private void InstallClicked(object sender, EventArgs e)
        {
            string installDir = _pathBox.Text.Trim();
            if (string.IsNullOrEmpty(installDir))
            {
                MessageBox.Show(this, "Please enter a valid installation folder.", "UMA Connect Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _installBtn.Enabled = false;
            _browseBtn.Enabled = false;
            _pathBox.Enabled = false;
            _cancelBtn.Enabled = false;

            bool createDesktop = _desktopCheck.Checked;
            bool createStartMenu = _startMenuCheck.Checked;
            bool launchAfter = _launchCheck.Checked;

            var worker = new Thread(delegate()
            {
                RunInstall(installDir, createDesktop, createStartMenu, launchAfter);
            });
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }

        private void SetProgress(int value, string status)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, string>(SetProgress), value, status);
                return;
            }
            _progressBar.Value = Math.Max(0, Math.Min(100, value));
            _statusLabel.Text = status;
        }

        private void RunInstall(string installDir, bool createDesktop, bool createStartMenu, bool launchAfter)
        {
            string tempZip = Path.Combine(Path.GetTempPath(), "umaconnect_setup_" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                SetProgress(15, "Preparing installation directory...");
                Directory.CreateDirectory(installDir);

                SetProgress(35, "Extracting application files...");
                using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("app_payload.zip"))
                {
                    if (payload == null)
                    {
                        throw new InvalidOperationException("Embedded payload resource (app_payload.zip) is missing.");
                    }
                    using (FileStream fs = File.Create(tempZip))
                    {
                        payload.CopyTo(fs);
                    }
                }

                using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        string destPath = Path.Combine(installDir, entry.FullName);
                        string destFolder = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(destFolder))
                        {
                            Directory.CreateDirectory(destFolder);
                        }
                        entry.ExtractToFile(destPath, true);
                    }
                }

                string targetExe = Path.Combine(installDir, "UMA Connect.exe");
                string uninstallExe = Path.Combine(installDir, "Uninstall.exe");
                string iconPath = Path.Combine(installDir, "app_icon.ico");

                SetProgress(70, "Creating shortcuts...");
                if (createDesktop)
                {
                    string desktopLnk = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                        "UMA Connect.lnk"
                    );
                    CreateShortcut(desktopLnk, targetExe, installDir, iconPath, "UMA Connect - Enterprise Operations Portal");
                }

                if (createStartMenu)
                {
                    string startDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                        "UMA Connect"
                    );
                    Directory.CreateDirectory(startDir);
                    string startLnk = Path.Combine(startDir, "UMA Connect.lnk");
                    CreateShortcut(startLnk, targetExe, installDir, iconPath, "UMA Connect - Enterprise Operations Portal");
                }

                SetProgress(88, "Registering application...");
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\UMAConnect"))
                {
                    if (key != null)
                    {
                        key.SetValue("DisplayName", "UMA Connect");
                        key.SetValue("DisplayVersion", "2.0.0");
                        key.SetValue("Publisher", "UMA Steel Alloys and Power Limited");
                        key.SetValue("InstallLocation", installDir);
                        key.SetValue("DisplayIcon", iconPath);
                        key.SetValue("UninstallString", "\"" + uninstallExe + "\"");
                        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }

                SetProgress(100, "Installation completed successfully.");

                BeginInvoke(new Action(delegate()
                {
                    MessageBox.Show(
                        this,
                        "UMA Connect has been installed successfully to:\n" + installDir,
                        "UMA Connect Setup",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );

                    if (launchAfter && File.Exists(targetExe))
                    {
                        Process.Start(new ProcessStartInfo(targetExe) { WorkingDirectory = installDir });
                    }
                    Close();
                }));
            }
            catch (Exception ex)
            {
                BeginInvoke(new Action(delegate()
                {
                    _statusLabel.Text = "Installation failed.";
                    _installBtn.Enabled = true;
                    _browseBtn.Enabled = true;
                    _pathBox.Enabled = true;
                    _cancelBtn.Enabled = true;
                    MessageBox.Show(
                        this,
                        "Installation failed:\n\n" + ex.Message,
                        "UMA Connect Setup Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }));
            }
            finally
            {
                try
                {
                    if (File.Exists(tempZip)) File.Delete(tempZip);
                }
                catch { }
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string iconPath, string description)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            Type scType = shortcut.GetType();
            scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
            scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconPath + ",0" });
            scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
            scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerForm());
        }
    }
}

