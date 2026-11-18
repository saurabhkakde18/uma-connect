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
using Microsoft.Win32;

[assembly: AssemblyTitle("UMA Connect")]
[assembly: AssemblyDescription("UMA Connect - Enterprise Operations & Query Management Portal")]
[assembly: AssemblyCompany("UMA Steel Alloys and Power Limited")]
[assembly: AssemblyProduct("UMA Connect")]
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]

namespace UMAConnect
{
    internal static class Program
    {
        private const string AppDisplayName = "UMA Connect";
        private const string MutexName = "Global\\UMAConnect_App_SingleInstance_Mutex_v2";
        private const int PreferredPortStart = 45821;
        private const int PreferredPortEnd = 45850;

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
        private static long _lastRequestTicks = DateTime.UtcNow.Ticks;

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
                    if (!ActivateExistingWindow())
                    {
                        // Existing background instance had no visible window; open index directly on preferred port
                        string existingUrl = GetSavedActiveUrl();
                        if (!string.IsNullOrEmpty(existingUrl))
                        {
                            LaunchBrowserOnly(existingUrl);
                        }
                    }
                    return;
                }

                try
                {
                    RunApplication();
                }
                catch (Exception ex)
                {
                    // Last-resort fallback: open local index.html directly in default browser
                    try
                    {
                        string fallbackRoot = ResolveWebRoot(AppDomain.CurrentDomain.BaseDirectory);
                        if (!string.IsNullOrEmpty(fallbackRoot))
                        {
                            string indexFile = Path.Combine(fallbackRoot, "index.html");
                            if (File.Exists(indexFile))
                            {
                                Process.Start(new ProcessStartInfo(indexFile) { UseShellExecute = true });
                                return;
                            }
                        }
                    }
                    catch { }

                    MessageBox.Show(
                        "Failed to start UMA Connect:\n\n" + ex.Message,
                        AppDisplayName + " Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
        }

        private static string GetPortStateFile()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UMAConnect"
            );
            try { Directory.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, "active_url.txt");
        }

        private static void SaveActiveUrl(string url)
        {
            try { File.WriteAllText(GetPortStateFile(), url); } catch { }
        }

        private static string GetSavedActiveUrl()
        {
            try
            {
                string f = GetPortStateFile();
                if (File.Exists(f)) return File.ReadAllText(f).Trim();
            }
            catch { }
            return null;
        }

        private static bool ActivateExistingWindow()
        {
            bool found = false;
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
                    found = true;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static bool IsAppWindowOpen()
        {
            bool found = false;
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (!IsWindowVisible(hWnd)) return true;
                int len = GetWindowTextLength(hWnd);
                if (len <= 0) return true;

                var sb = new StringBuilder(len + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();
                if (title.IndexOf(AppDisplayName, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    title.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    found = true;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static TcpListener StartLocalServer(out int boundPort)
        {
            // Try fixed preferred ports first so localStorage persists across restarts
            for (int p = PreferredPortStart; p <= PreferredPortEnd; p++)
            {
                try
                {
                    var candidate = new TcpListener(IPAddress.Loopback, p);
                    candidate.Start();
                    boundPort = p;
                    return candidate;
                }
                catch { }
            }

            // Fallback to OS-assigned free port
            var dynamicListener = new TcpListener(IPAddress.Loopback, 0);
            dynamicListener.Start();
            boundPort = ((IPEndPoint)dynamicListener.LocalEndpoint).Port;
            return dynamicListener;
        }

        private static Process LaunchBrowserOnly(string appUrl)
        {
            string browserPath = FindChromiumBrowser();
            if (!string.IsNullOrEmpty(browserPath))
            {
                string userDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "UMAConnect",
                    "Data"
                );
                try { Directory.CreateDirectory(userDataDir); } catch { }

                var psi = new ProcessStartInfo
                {
                    FileName = browserPath,
                    Arguments = string.Format(
                        "--app=\"{0}\" --user-data-dir=\"{1}\" --new-window --disable-background-mode --window-size=1366,860 --no-first-run --no-default-browser-check",
                        appUrl,
                        userDataDir
                    ),
                    UseShellExecute = false
                };
                return Process.Start(psi);
            }
            else
            {
                Process.Start(new ProcessStartInfo(appUrl) { UseShellExecute = true });
                return null;
            }
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

            int port;
            TcpListener listener = null;
            try
            {
                listener = StartLocalServer(out port);
            }
            catch
            {
                // If local TCP port binding is blocked by strict endpoint security, open index.html directly
                string indexFile = Path.Combine(_webRoot, "index.html");
                Process.Start(new ProcessStartInfo(indexFile) { UseShellExecute = true });
                return;
            }

            string appUrl = string.Format("http://127.0.0.1:{0}/index.html", port);
            SaveActiveUrl(appUrl);
            Interlocked.Exchange(ref _lastRequestTicks, DateTime.UtcNow.Ticks);

            var serverThread = new Thread(delegate() { ServeLoop(listener); });
            serverThread.IsBackground = true;
            serverThread.Start();

            try
            {
                string browserPath = FindChromiumBrowser();
                if (!string.IsNullOrEmpty(browserPath))
                {
                    Process proc = LaunchBrowserOnly(appUrl);
                    DateTime launchedAt = DateTime.UtcNow;
                    bool sawWindow = false;

                    // Robust window lifecycle loop (survives Edge/Chrome Startup Boost early-exit)
                    while (true)
                    {
                        Thread.Sleep(600);

                        bool procAlive = false;
                        if (proc != null)
                        {
                            try { procAlive = !proc.HasExited; } catch { procAlive = false; }
                        }

                        bool windowOpen = IsAppWindowOpen();
                        if (windowOpen)
                        {
                            sawWindow = true;
                        }

                        double secsSinceLaunch = (DateTime.UtcNow - launchedAt).TotalSeconds;
                        long lastTicks = Interlocked.Read(ref _lastRequestTicks);
                        double secsSinceActivity = (DateTime.UtcNow - new DateTime(lastTicks, DateTimeKind.Utc)).TotalSeconds;

                        // During initial 15s startup grace period, never kill server
                        if (secsSinceLaunch < 15.0)
                        {
                            continue;
                        }

                        // If we saw the window open and now it is closed and process exited, shut down cleanly
                        if (!windowOpen && !procAlive && secsSinceActivity > 5.0)
                        {
                            break;
                        }

                        // If process never spawned a visible window after 35s and idle, shut down cleanly
                        if (!sawWindow && !windowOpen && !procAlive && secsSinceLaunch > 35.0)
                        {
                            break;
                        }
                    }

                    if (proc != null)
                    {
                        try { proc.Dispose(); } catch { }
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
            }
            finally
            {
                _running = false;
                try { listener.Stop(); } catch { }
            }
        }

        private static string ResolveWebRoot(string baseDir)
        {
            if (File.Exists(Path.Combine(baseDir, "index.html"))) return baseDir;
            DirectoryInfo parentInfo = Directory.GetParent(baseDir.TrimEnd('\\', '/'));
            string parent = parentInfo != null ? parentInfo.FullName : null;
            if (!string.IsNullOrEmpty(parent) && File.Exists(Path.Combine(parent, "index.html")))
            {
                return parent;
            }
            return null;
        }

        private static string FindBrowserFromRegistry(string exeName)
        {
            string[] regPaths = new string[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exeName,
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\" + exeName
            };
            RegistryKey[] roots = new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser };
            foreach (RegistryKey root in roots)
            {
                foreach (string subKey in regPaths)
                {
                    try
                    {
                        using (RegistryKey key = root.OpenSubKey(subKey))
                        {
                            if (key != null)
                            {
                                object val = key.GetValue("");
                                if (val != null)
                                {
                                    string path = val.ToString().Trim('"');
                                    if (File.Exists(path)) return path;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            return null;
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
                Path.Combine(localAppData, "Google\\Chrome\\Application\\chrome.exe"),
                Path.Combine(programFiles, "BraveSoftware\\Brave-Browser\\Application\\brave.exe"),
                Path.Combine(programFilesX86, "BraveSoftware\\Brave-Browser\\Application\\brave.exe"),
                Path.Combine(localAppData, "BraveSoftware\\Brave-Browser\\Application\\brave.exe")
            };

            foreach (string path in candidates)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return path;
                }
            }

            string fromReg = FindBrowserFromRegistry("msedge.exe");
            if (!string.IsNullOrEmpty(fromReg)) return fromReg;

            fromReg = FindBrowserFromRegistry("chrome.exe");
            if (!string.IsNullOrEmpty(fromReg)) return fromReg;

            return null;
        }

        private static void ServeLoop(TcpListener listener)
        {
            while (_running)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    Interlocked.Exchange(ref _lastRequestTicks, DateTime.UtcNow.Ticks);
                    ThreadPool.QueueUserWorkItem(delegate(object state)
                    {
                        HandleClient((TcpClient)state);
                    }, client);
                }
                catch (SocketException)
                {
                    if (!_running) break;
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

        private static void HandleClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 10000;
                    using (NetworkStream stream = client.GetStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true))
                    {
                        string requestLine = reader.ReadLine();
                        if (string.IsNullOrEmpty(requestLine)) return;

                        // Drain remaining HTTP headers
                        string headerLine;
                        while (!string.IsNullOrEmpty(headerLine = reader.ReadLine())) { }

                        string[] parts = requestLine.Split(' ');
                        if (parts.Length < 2) return;

                        string method = parts[0].ToUpperInvariant();
                        string rawUrl = parts[1];
                        int qIdx = rawUrl.IndexOf('?');
                        if (qIdx >= 0) rawUrl = rawUrl.Substring(0, qIdx);

                        string rawPath = Uri.UnescapeDataString(rawUrl);
                        if (string.IsNullOrEmpty(rawPath) || rawPath == "/")
                        {
                            rawPath = "/index.html";
                        }

                        string relPath = rawPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                        string rootFull = Path.GetFullPath(_webRoot);
                        string targetFull = Path.GetFullPath(Path.Combine(rootFull, relPath));

                        if (!targetFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !File.Exists(targetFull))
                        {
                            byte[] notFound = Encoding.UTF8.GetBytes("Not Found");
                            WriteResponse(stream, "404 Not Found", "text/plain; charset=utf-8", notFound, method == "HEAD");
                            return;
                        }

                        string ext = Path.GetExtension(targetFull);
                        string mime;
                        if (!MimeTypes.TryGetValue(ext, out mime))
                        {
                            mime = "application/octet-stream";
                        }

                        byte[] data = File.ReadAllBytes(targetFull);
                        WriteResponse(stream, "200 OK", mime, data, method == "HEAD");
                    }
                }
                catch { }
            }
        }

        private static void WriteResponse(NetworkStream stream, string status, string contentType, byte[] body, bool headOnly)
        {
            string headers =
                "HTTP/1.1 " + status + "\r\n" +
                "Content-Type: " + contentType + "\r\n" +
                "Content-Length: " + body.Length + "\r\n" +
                "Cache-Control: no-store, no-cache, must-revalidate\r\n" +
                "Connection: close\r\n\r\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            stream.Write(headerBytes, 0, headerBytes.Length);
            if (!headOnly && body.Length > 0)
            {
                stream.Write(body, 0, body.Length);
            }
            stream.Flush();
        }
    }
}
