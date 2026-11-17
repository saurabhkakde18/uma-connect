using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("UMA Connect")]
[assembly: AssemblyDescription("UMA Connect - Enterprise Operations & Query Management Portal")]
[assembly: AssemblyCompany("UMA Steel Alloys and Power Limited")]
[assembly: AssemblyProduct("UMA Connect")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace UMAConnect
{
    internal static class Program
    {
        private const string AppDisplayName = "UMA Connect";
        private const string MutexName = "Global\\UMAConnect_App_SingleInstance_Mutex";

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxLength);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private static readonly Dictionary<string, string> MimeTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" },
            { ".htm", "text/html; charset=utf-8" },
            { ".css", "text/css; charset=utf-8" },
            { ".js", "application/javascript; charset=utf-8" },
            { ".json", "application/json; charset=utf-8" },
            { ".png", "image/png" },
            { ".jpg", "image/jpeg" },
            { ".jpeg", "image/jpeg" },
            { ".gif", "image/gif" },
            { ".svg", "image/svg+xml" },
            { ".ico", "image/x-icon" },
            { ".webp", "image/webp" },
            { ".woff", "font/woff" },
            { ".woff2", "font/woff2" },
            { ".ttf", "font/ttf" },
            { ".txt", "text/plain; charset=utf-8" }
        };

        private static string _webRoot;
        private static volatile bool _running = true;

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    ActivateExistingWindow();
                    return;
                }

                try
                {
                    RunApplication();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Failed to start UMA Connect:\n\n" + ex.Message,
                        AppDisplayName + " Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
        }

        private static void ActivateExistingWindow()
        {
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (!IsWindowVisible(hWnd)) return true;
                int len = GetWindowTextLength(hWnd);
                if (len <= 0) return true;

                var sb = new StringBuilder(len + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();
                if (title.IndexOf(AppDisplayName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ShowWindow(hWnd, 9); // SW_RESTORE
                    SetForegroundWindow(hWnd);
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }

        private static void RunApplication()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _webRoot = ResolveWebRoot(baseDir);
            if (_webRoot == null)
            {
                MessageBox.Show(
                    "Application entry file (index.html) was not found in:\n" + baseDir,
                    AppDisplayName + " Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            int port = GetFreePort();
            string prefix = string.Format("http://127.0.0.1:{0}/", port);
            string appUrl = prefix + "index.html";

            using (var listener = new HttpListener())
            {
                listener.Prefixes.Add(prefix);
                listener.Start();

                var serverThread = new Thread(delegate() { ServeLoop(listener); });
                serverThread.IsBackground = true;
                serverThread.Start();

                string browserPath = FindChromiumBrowser();
                if (!string.IsNullOrEmpty(browserPath))
                {
                    string userDataDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "UMAConnect",
                        "Data"
                    );
                    Directory.CreateDirectory(userDataDir);

                    var psi = new ProcessStartInfo
                    {
                        FileName = browserPath,
                        Arguments = string.Format(
                            "--app=\"{0}\" --user-data-dir=\"{1}\" --app-id=\"UMAConnect\" --window-size=1366,860 --no-first-run --no-default-browser-check",
                            appUrl,
                            userDataDir
                        ),
                        UseShellExecute = false
                    };

                    using (var proc = Process.Start(psi))
                    {
                        if (proc != null)
                        {
                            proc.WaitForExit();
                        }
                    }
                }
                else
                {
                    Process.Start(new ProcessStartInfo(appUrl) { UseShellExecute = true });
                    MessageBox.Show(
                        "UMA Connect opened in your default browser at:\n" + appUrl + "\n\nKeep this window open while using the app, then click OK to stop the local server.",
                        AppDisplayName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }

                _running = false;
                try { listener.Stop(); } catch { }
            }
        }

        private static string ResolveWebRoot(string baseDir)
        {
            if (File.Exists(Path.Combine(baseDir, "index.html"))) return baseDir;
            string parent = Directory.GetParent(baseDir.TrimEnd('\\', '/')) != null
                ? Directory.GetParent(baseDir.TrimEnd('\\', '/')).FullName
                : null;
            if (!string.IsNullOrEmpty(parent) && File.Exists(Path.Combine(parent, "index.html")))
            {
                return parent;
            }
            return null;
        }

        private static int GetFreePort()
        {
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            return port;
        }

        private static string FindChromiumBrowser()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string[] candidates = new string[]
            {
                Path.Combine(programFilesX86, "Microsoft\\Edge\\Application\\msedge.exe"),
                Path.Combine(programFiles, "Microsoft\\Edge\\Application\\msedge.exe"),
                Path.Combine(localAppData, "Microsoft\\Edge\\Application\\msedge.exe"),
                Path.Combine(programFiles, "Google\\Chrome\\Application\\chrome.exe"),
                Path.Combine(programFilesX86, "Google\\Chrome\\Application\\chrome.exe"),
                Path.Combine(localAppData, "Google\\Chrome\\Application\\chrome.exe")
            };

            foreach (string path in candidates)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return path;
                }
            }
            return null;
        }

        private static void ServeLoop(HttpListener listener)
        {
            while (_running && listener.IsListening)
            {
                try
                {
                    HttpListenerContext ctx = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(delegate(object state)
                    {
                        HandleRequest((HttpListenerContext)state);
                    }, ctx);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch
                {
                    if (!_running) break;
                }
            }
        }

        private static void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                string rawPath = Uri.UnescapeDataString(ctx.Request.Url.AbsolutePath ?? "/");
                if (string.IsNullOrEmpty(rawPath) || rawPath == "/")
                {
                    rawPath = "/index.html";
                }

                string relPath = rawPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                string rootFull = Path.GetFullPath(_webRoot);
                string targetFull = Path.GetFullPath(Path.Combine(rootFull, relPath));

                if (!targetFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !File.Exists(targetFull))
                {
                    ctx.Response.StatusCode = 404;
                    byte[] notFound = Encoding.UTF8.GetBytes("Not Found");
                    ctx.Response.OutputStream.Write(notFound, 0, notFound.Length);
                    ctx.Response.Close();
                    return;
                }

                string ext = Path.GetExtension(targetFull);
                string mime;
                if (!MimeTypes.TryGetValue(ext, out mime))
                {
                    mime = "application/octet-stream";
                }

                byte[] data = File.ReadAllBytes(targetFull);
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = mime;
                ctx.Response.ContentLength64 = data.Length;
                ctx.Response.AddHeader("Cache-Control", "no-store");
                ctx.Response.OutputStream.Write(data, 0, data.Length);
                ctx.Response.Close();
            }
            catch
            {
                try
                {
                    ctx.Response.StatusCode = 500;
                    ctx.Response.Close();
                }
                catch { }
            }
        }
    }
}

