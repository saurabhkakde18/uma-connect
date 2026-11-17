using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("UMA Connect Uninstaller")]
[assembly: AssemblyDescription("Uninstalls UMA Connect")]
[assembly: AssemblyCompany("UMA Steel Alloys and Power Limited")]
[assembly: AssemblyProduct("UMA Connect")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace UMAConnectUninstall
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            DialogResult confirm = MessageBox.Show(
                "Are you sure you want to uninstall UMA Connect from this computer?\n\nDesktop shortcuts, Start Menu entries, and program files will be removed.",
                "Uninstall UMA Connect",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            try
            {
                string desktopShortcut = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "UMA Connect.lnk"
                );
                if (File.Exists(desktopShortcut))
                {
                    File.Delete(desktopShortcut);
                }

                string startMenuDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    "UMA Connect"
                );
                if (Directory.Exists(startMenuDir))
                {
                    Directory.Delete(startMenuDir, true);
                }

                using (RegistryKey uninstallKey = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall", true))
                {
                    if (uninstallKey != null)
                    {
                        uninstallKey.DeleteSubKeyTree("UMAConnect", false);
                    }
                }

                string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
                string localPrograms = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs",
                    "UMA Connect"
                );

                if (string.Equals(Path.GetFullPath(appDir), Path.GetFullPath(localPrograms), StringComparison.OrdinalIgnoreCase))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = string.Format("/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{0}\"", appDir),
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    Process.Start(psi);
                }

                MessageBox.Show(
                    "UMA Connect has been uninstalled successfully.",
                    "UMA Connect Uninstalled",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "An error occurred during uninstall:\n\n" + ex.Message,
                    "UMA Connect Uninstall Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
    }
}

