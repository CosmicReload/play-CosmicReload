// Play CosmicReload with the Cosmic Client.
//
// Starts the official Cosmic Client with the CosmicReload agent loaded (via JAVA_TOOL_OPTIONS, set only for the
// Cosmic Client and the Java processes it starts). The agent turns on the Cosmic Client's features when, and only when, you are
// connected to CosmicReload. Nothing is installed and no files of the Cosmic Client are changed.
//
// Build: Build.bat (Windows' own C# compiler, a JDK for the agent, NSIS for the installer).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Play CosmicReload")]
[assembly: AssemblyProduct("Play CosmicReload")]
[assembly: AssemblyCompany("CosmicReload")]
[assembly: AssemblyCopyright("Copyright (c) 2026 CosmicReload. MIT licence.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace CosmicReload.Play
{
    static class Config
    {
        public const string Version = "1.0.4";
        public const string ServerAddress = "play.cosmicreload.com";
        public const int ServerPort = 25565;
        // servers the agent turns Cosmic features on for (localhost: a server on your own PC)
        public const string AgentHosts = "play.cosmicreload.com,localhost";
        public const string CosmicClientSite = "https://cosmicclient.com";
        public const string ReloadClientSite = "https://cosmicreload.com/download/";
        public const string SourceSite = "https://github.com/CosmicReload/play-CosmicReload";
        // Updates come only from this repository's public GitHub releases, which GitHub Actions builds from the tagged
        // source (see README, "Updates"). The manifest is {"version": "x.y.z", "url": "<installer>", "sha256": "<hex>"},
        // written by build.ps1, and its url must be exactly ReleaseDownload + "v<version>/" + InstallerName.
        public const string UpdateManifest = "https://github.com/CosmicReload/play-CosmicReload/releases/latest/download/play-cosmicreload.json";
        public const string ReleaseDownload = "https://github.com/CosmicReload/play-CosmicReload/releases/download/";
        public const string InstallerName = "PlayCosmicReloadSetup.exe";
    }

    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            try { SetProcessDPIAware(); } catch (Exception) { }
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    static class CosmicClient
    {
        /// <summary>The Cosmic Client exe, or null when it is not installed.</summary>
        public static string FindLauncher()
        {
            string[] dirs = {
                Environment.GetEnvironmentVariable("ProgramFiles"),
                Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                Environment.GetEnvironmentVariable("LOCALAPPDATA") + "\\Programs"
            };
            foreach (string dir in dirs)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string exe = Path.Combine(dir, "Cosmic Client\\Cosmic Client.exe");
                if (File.Exists(exe)) return exe;
            }
            return FromUninstallKeys(Registry.LocalMachine) ?? FromUninstallKeys(Registry.CurrentUser);
        }

        static string FromUninstallKeys(RegistryKey root)
        {
            string[] paths = {
                "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
                "SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall"
            };
            foreach (string path in paths)
            {
                using (RegistryKey key = root.OpenSubKey(path))
                {
                    if (key == null) continue;
                    foreach (string name in key.GetSubKeyNames())
                    {
                        using (RegistryKey app = key.OpenSubKey(name))
                        {
                            if (app == null || !"Cosmic Client".Equals(app.GetValue("DisplayName") as string)) continue;
                            string dir = app.GetValue("InstallLocation") as string;
                            if (string.IsNullOrEmpty(dir)) continue;
                            string exe = Path.Combine(dir.Trim('"'), "Cosmic Client.exe");
                            if (File.Exists(exe)) return exe;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>True while the Cosmic Client is open (a running one would not pick the agent up).</summary>
        public static bool IsRunning()
        {
            if (Process.GetProcessesByName("Cosmic Client").Length > 0) return true;
            string cosmicJava = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft\\cosmic\\bootstrap\\java").ToLowerInvariant();
            foreach (string name in new[] { "java", "javaw" })
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (p.MainModule.FileName.ToLowerInvariant().StartsWith(cosmicJava)) return true;
                    }
                    catch (Exception)
                    {
                        // another user's or a protected process: not ours
                    }
                }
            }
            return false;
        }

        /// <summary>Writes the bundled agent into the user's app data and returns its path.</summary>
        public static string ExtractAgent()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CosmicReload");
            Directory.CreateDirectory(dir);
            string jar = Path.Combine(dir, "cosmic-agent.jar");
            byte[] bytes = Resource("cosmic-agent.jar");
            if (bytes == null) throw new InvalidOperationException("The agent is missing from this build.");
            if (!File.Exists(jar) || !SameBytes(File.ReadAllBytes(jar), bytes)) File.WriteAllBytes(jar, bytes);
            return jar;
        }

        public static byte[] Resource(string name)
        {
            using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (src == null) return null;
                byte[] bytes = new byte[src.Length];
                int read = 0;
                while (read < bytes.Length) read += src.Read(bytes, read, bytes.Length - read);
                return bytes;
            }
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        public static void Start(string launcher, string agentJar)
        {
            ProcessStartInfo info = new ProcessStartInfo(launcher);
            info.UseShellExecute = false;
            info.WorkingDirectory = Path.GetDirectoryName(launcher);
            string option = "-javaagent:" + agentJar + "=" + Config.AgentHosts;
            if (option.Contains(" ")) option = "\"" + option + "\"";
            // only the Cosmic Client (and the Java processes it starts) see the variable
            info.EnvironmentVariables["JAVA_TOOL_OPTIONS"] = option;
            Process.Start(info);
        }
    }

    /// <summary>The player's choices, in %LOCALAPPDATA%\CosmicReload\settings.txt (key=value lines).</summary>
    static class Settings
    {
        static string Location()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CosmicReload\\settings.txt");
        }

        /// <summary>Install updates without asking. Off by default: the app asks first.</summary>
        public static bool AutoUpdate
        {
            get
            {
                try
                {
                    foreach (string line in File.ReadAllLines(Location()))
                        if (line.Trim() == "autoUpdate=true") return true;
                }
                catch (Exception)
                {
                    // no settings yet
                }
                return false;
            }
            set
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Location()));
                    File.WriteAllText(Location(), "autoUpdate=" + (value ? "true" : "false") + "\r\n");
                }
                catch (Exception)
                {
                    // read-only profile: the choice lasts until the app closes
                }
            }
        }
    }

    /// <summary>
    /// Self-update: reads the manifest of this repository's latest GitHub release, offers a newer version (or installs
    /// it straight away when the player turned auto-update on),
    /// downloads the installer, checks its SHA-256 and runs it silently (per-user, so no admin prompt). The installer
    /// closes this app and starts the new version.
    /// </summary>
    static class Updater
    {
        public sealed class Release
        {
            public Version Version;
            public string Url;
            public string Sha256;
            public string Notes;
        }

        /// <summary>Only an installed copy updates itself (not one run from a build folder).</summary>
        public static bool Installed()
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            return File.Exists(Path.Combine(dir, "Uninstall.exe"));
        }

        /// <summary>The newer release, or null (none, or the check failed).</summary>
        public static Release Check()
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Config.UpdateManifest + "?t=" + DateTime.UtcNow.Ticks);
                request.Timeout = 8000;
                request.ReadWriteTimeout = 8000;
                request.UserAgent = "PlayCosmicReload/" + Config.Version;
                string json;
                using (WebResponse response = request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    json = reader.ReadToEnd();
                Match version = Regex.Match(json, "\"version\"\\s*:\\s*\"([0-9.]+)\"");
                Match url = Regex.Match(json, "\"url\"\\s*:\\s*\"(https://[^\"]+)\"");
                Match sha = Regex.Match(json, "\"sha256\"\\s*:\\s*\"([0-9a-fA-F]{64})\"");
                if (!version.Success || !url.Success || !sha.Success) return null;
                Release release = new Release();
                release.Version = new Version(version.Groups[1].Value);
                release.Url = url.Groups[1].Value;
                release.Sha256 = sha.Groups[1].Value.ToLowerInvariant();
                // only this repository's release for that exact version, never an address the manifest picks
                if (release.Url != Config.ReleaseDownload + "v" + version.Groups[1].Value + "/" + Config.InstallerName) return null;
                release.Notes = Config.SourceSite + "/releases/tag/v" + version.Groups[1].Value;
                return release.Version > new Version(Config.Version) ? release : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Downloads and verifies the installer; returns its path, or null when anything is off.</summary>
        public static string Download(Release release, Action<int> progress)
        {
            string file = Path.Combine(Path.GetTempPath(), "PlayCosmicReloadSetup-" + release.Version + ".exe");
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(release.Url);
                request.Timeout = 15000;
                request.ReadWriteTimeout = 15000;
                request.UserAgent = "PlayCosmicReload/" + Config.Version;
                using (WebResponse response = request.GetResponse())
                using (Stream input = response.GetResponseStream())
                using (FileStream output = File.Create(file))
                {
                    long total = response.ContentLength;
                    long done = 0;
                    byte[] buffer = new byte[65536];
                    for (int n; (n = input.Read(buffer, 0, buffer.Length)) > 0; )
                    {
                        output.Write(buffer, 0, n);
                        done += n;
                        if (total > 0) progress((int)(done * 100 / total));
                    }
                }
                string hash;
                using (FileStream input = File.OpenRead(file))
                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                if (hash == release.Sha256) return file;
            }
            catch (Exception)
            {
                // fall through: keep the current version
            }
            try { File.Delete(file); } catch (Exception) { }
            return null;
        }

        public static void Install(string installer)
        {
            ProcessStartInfo info = new ProcessStartInfo(installer, "/S /UPDATE");
            info.UseShellExecute = false;
            Process.Start(info);
        }
    }

    /// <summary>Minecraft server list ping (protocol 47), for the online / player count line.</summary>
    static class ServerStatus
    {
        public sealed class Result
        {
            public bool Online;
            public int Players;
            public int Max;
        }

        public static Result Ping(string host, int port, int timeoutMs)
        {
            Result result = new Result();
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult connect = client.BeginConnect(host, port, null, null);
                    if (!connect.AsyncWaitHandle.WaitOne(timeoutMs)) return result;
                    client.EndConnect(connect);
                    NetworkStream stream = client.GetStream();
                    stream.ReadTimeout = timeoutMs;
                    stream.WriteTimeout = timeoutMs;

                    List<byte> body = new List<byte>();
                    WriteVarInt(body, 0);
                    WriteVarInt(body, 47);
                    byte[] hostBytes = Encoding.UTF8.GetBytes(host);
                    WriteVarInt(body, hostBytes.Length);
                    body.AddRange(hostBytes);
                    body.Add((byte)(port >> 8));
                    body.Add((byte)port);
                    WriteVarInt(body, 1);
                    List<byte> packet = new List<byte>();
                    WriteVarInt(packet, body.Count);
                    packet.AddRange(body);
                    packet.Add(1);
                    packet.Add(0);
                    byte[] bytes = packet.ToArray();
                    stream.Write(bytes, 0, bytes.Length);

                    ReadVarInt(stream);
                    if (ReadVarInt(stream) != 0) return result;
                    byte[] json = new byte[ReadVarInt(stream)];
                    int read = 0;
                    while (read < json.Length)
                    {
                        int n = stream.Read(json, read, json.Length - read);
                        if (n <= 0) return result;
                        read += n;
                    }
                    string text = Encoding.UTF8.GetString(json);
                    Match online = Regex.Match(text, "\"online\"\\s*:\\s*(\\d+)");
                    Match max = Regex.Match(text, "\"max\"\\s*:\\s*(\\d+)");
                    result.Online = true;
                    if (online.Success) result.Players = int.Parse(online.Groups[1].Value);
                    if (max.Success) result.Max = int.Parse(max.Groups[1].Value);
                }
            }
            catch (Exception)
            {
                result.Online = false;
            }
            return result;
        }

        static void WriteVarInt(List<byte> output, int value)
        {
            uint v = (uint)value;
            do
            {
                byte b = (byte)(v & 0x7F);
                v >>= 7;
                if (v != 0) b |= 0x80;
                output.Add(b);
            } while (v != 0);
        }

        static int ReadVarInt(Stream stream)
        {
            int value = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                int b = stream.ReadByte();
                if (b < 0) throw new EndOfStreamException();
                value |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) return value;
            }
            throw new IOException("bad varint");
        }
    }

    /// <summary>The whole window is drawn by hand (Reload Client launcher look), in 96-dpi units scaled to the screen.</summary>
    sealed class MainForm : Form
    {
        const int W = 440, H = 580, TitleH = 38;

        static readonly Color Bg = Color.FromArgb(11, 11, 18);
        static readonly Color PanelC = Color.FromArgb(18, 18, 28);
        static readonly Color Panel2 = Color.FromArgb(25, 25, 38);
        static readonly Color Edge = Color.FromArgb(38, 38, 54);
        static readonly Color TextC = Color.FromArgb(242, 242, 247);
        static readonly Color Muted = Color.FromArgb(154, 154, 174);
        static readonly Color Faint = Color.FromArgb(98, 98, 120);
        static readonly Color Cyan = Color.FromArgb(85, 255, 255);
        static readonly Color Pink = Color.FromArgb(255, 85, 255);
        static readonly Color Good = Color.FromArgb(61, 220, 132);
        static readonly Color Bad = Color.FromArgb(255, 92, 108);
        static readonly Color Warn = Color.FromArgb(255, 196, 87);

        enum ClientState { Missing, Running, Ready }
        enum Phase { Idle, Updating, Launched }

        readonly float scale;
        readonly Image logo;
        readonly PointF[] stars;
        readonly float[] starSize;
        readonly int[] starAlpha;
        readonly Dictionary<string, RectangleF> targets = new Dictionary<string, RectangleF>();
        readonly StringFormat typo;
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();

        readonly Font fBrand = Px("Segoe UI", 11f, FontStyle.Bold);
        readonly Font fWord = Px("Segoe UI Black", 30f, FontStyle.Regular);
        readonly Font fEyebrow = Px("Segoe UI", 11f, FontStyle.Bold);
        readonly Font fLabel = Px("Segoe UI Semibold", 14.5f, FontStyle.Regular);
        readonly Font fSub = Px("Segoe UI", 12f, FontStyle.Regular);
        readonly Font fState = Px("Segoe UI Semibold", 13f, FontStyle.Regular);
        readonly Font fPlay = Px("Segoe UI Black", 19f, FontStyle.Regular);
        readonly Font fPlaySmall = Px("Segoe UI Black", 14f, FontStyle.Regular);
        readonly Font fHint = Px("Segoe UI", 12.5f, FontStyle.Regular);
        readonly Font fFoot = Px("Segoe UI", 12f, FontStyle.Regular);

        string launcher;
        ClientState client = ClientState.Ready;
        Phase phase = Phase.Idle;
        DateTime launchedAt;
        bool sawRunning;
        ServerStatus.Result server;
        bool pinging;
        DateTime nextPing = DateTime.MinValue;
        DateTime copiedUntil = DateTime.MinValue;
        string error;
        Updater.Release available; // a newer version the player has not answered yet (shown as a banner)
        readonly bool installed = Updater.Installed(); // a build-folder copy never updates
        bool autoUpdate = Settings.AutoUpdate;
        Version updateVersion;
        int updateProgress;
        string hover;
        string pressed;

        static Font Px(string family, float size, FontStyle style)
        {
            return new Font(family, size, style, GraphicsUnit.Pixel);
        }

        public MainForm()
        {
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
            Text = "Play CosmicReload";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size((int)Math.Round(W * scale), (int)Math.Round(H * scale));
            BackColor = Bg;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            byte[] png = CosmicClient.Resource("logo.png");
            if (png != null) logo = Image.FromStream(new MemoryStream(png));

            typo = (StringFormat)StringFormat.GenericTypographic.Clone();
            typo.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;

            Random random = new Random(7);
            stars = new PointF[70];
            starSize = new float[stars.Length];
            starAlpha = new int[stars.Length];
            for (int i = 0; i < stars.Length; i++)
            {
                stars[i] = new PointF((float)random.NextDouble() * W, TitleH + (float)random.NextDouble() * (H - TitleH));
                starSize[i] = random.NextDouble() < 0.15 ? 2f : 1.2f;
                starAlpha[i] = 40 + random.Next(120);
            }

            poll.Interval = 2000;
            poll.Tick += delegate { UpdateState(); };
            poll.Start();
            Activated += delegate { UpdateState(); };
            UpdateState();
            if (installed) CheckForUpdate();
        }

        void CheckForUpdate()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                Updater.Release release = Updater.Check();
                if (release == null) return;
                UI(delegate
                {
                    // ask first, unless the player chose auto-update
                    if (autoUpdate) StartUpdate(release);
                    else available = release;
                });
            });
        }

        void StartUpdate(Updater.Release release)
        {
            if (phase != Phase.Idle) return; // never interrupt a launch
            available = null;
            phase = Phase.Updating;
            updateVersion = release.Version;
            updateProgress = 0;
            error = null;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string installer = Updater.Download(release, delegate(int percent)
                {
                    UI(delegate { updateProgress = percent; });
                });
                UI(delegate
                {
                    if (installer == null)
                    {
                        phase = Phase.Idle; // download or check failed: carry on with this version
                        error = "The update could not be downloaded. You can keep playing on this version.";
                        return;
                    }
                    try
                    {
                        Updater.Install(installer);
                        Application.Exit();
                    }
                    catch (Exception)
                    {
                        phase = Phase.Idle;
                    }
                });
            });
        }

        /// <summary>Runs on the window's thread and repaints (no-op once the window is gone).</summary>
        void UI(MethodInvoker action)
        {
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    action();
                    Invalidate();
                });
            }
            catch (InvalidOperationException)
            {
                // window already closed
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                cp.Style |= 0x20000;      // WS_MINIMIZEBOX: minimise from the taskbar
                return cp;
            }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2; // Windows 11: rounded corners on a borderless window
            try { DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch (Exception) { }
        }

        // ---------------------------------------------------------------- state

        void UpdateState()
        {
            launcher = CosmicClient.FindLauncher();
            bool running = launcher != null && CosmicClient.IsRunning();
            client = launcher == null ? ClientState.Missing : running ? ClientState.Running : ClientState.Ready;
            if (phase == Phase.Launched)
            {
                if (running) sawRunning = true;
                // back to ready once the game is closed again (or the launcher never came up)
                if ((sawRunning && !running) || (!sawRunning && DateTime.Now - launchedAt > TimeSpan.FromSeconds(60)))
                    phase = Phase.Idle;
            }
            if (!pinging && DateTime.Now >= nextPing) PingServer();
            Invalidate();
        }

        void PingServer()
        {
            pinging = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                ServerStatus.Result result = ServerStatus.Ping(Config.ServerAddress, Config.ServerPort, 4000);
                UI(delegate
                {
                    server = result;
                    pinging = false;
                    nextPing = DateTime.Now.AddSeconds(30);
                });
            });
        }

        void Play()
        {
            if (client == ClientState.Missing)
            {
                Open(Config.CosmicClientSite);
                return;
            }
            if (client != ClientState.Ready || phase != Phase.Idle) return;
            try
            {
                CosmicClient.Start(launcher, CosmicClient.ExtractAgent());
                CopyAddress();
                phase = Phase.Launched;
                launchedAt = DateTime.Now;
                sawRunning = false;
                error = null;
            }
            catch (Exception ex)
            {
                error = "Could not start the Cosmic Client: " + ex.Message;
            }
            Invalidate();
        }

        void CopyAddress()
        {
            try { Clipboard.SetText(Config.ServerAddress); } catch (Exception) { }
            copiedUntil = DateTime.Now.AddSeconds(2.5);
            System.Windows.Forms.Timer clear = new System.Windows.Forms.Timer();
            clear.Interval = 2600;
            clear.Tick += delegate { clear.Dispose(); Invalidate(); };
            clear.Start();
            Invalidate();
        }

        static void Open(string url)
        {
            try { Process.Start(url); } catch (Exception) { }
        }

        // ---------------------------------------------------------------- input

        string HitTest(Point p)
        {
            PointF q = new PointF(p.X / scale, p.Y / scale);
            foreach (KeyValuePair<string, RectangleF> t in targets)
                if (t.Value.Contains(q)) return t.Key;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string hit = HitTest(e.Location);
            if (hit != hover)
            {
                hover = hit;
                Cursor = hit != null && hit != "min" && hit != "close" ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = null;
            pressed = null;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            string hit = HitTest(e.Location);
            if (hit == null && e.Y < TitleH * scale)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // drag by the title bar
                return;
            }
            pressed = hit;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            string hit = HitTest(e.Location);
            string was = pressed;
            pressed = null;
            Invalidate();
            if (hit == null || hit != was) return;
            switch (hit)
            {
                case "close": Close(); break;
                case "min": WindowState = FormWindowState.Minimized; break;
                case "play": Play(); break;
                case "client": if (client == ClientState.Missing) Open(Config.CosmicClientSite); break;
                case "server": CopyAddress(); break;
                case "source": Open(Config.SourceSite); break;
                case "update": if (available != null) StartUpdate(available); break;
                case "later": available = null; break; // asked again next time the app opens
                case "notes": if (available != null) Open(available.Notes); break;
                case "auto": autoUpdate = !autoUpdate; Settings.AutoUpdate = autoUpdate; break;
                case "reload": Open(Config.ReloadClientSite); break;
            }
        }

        // ---------------------------------------------------------------- drawing

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.ScaleTransform(scale, scale);
            targets.Clear();

            DrawBackdrop(g);
            DrawTitleBar(g);
            DrawHero(g);
            DrawStatusCard(g, 26, 262);
            DrawPlay(g, new RectangleF(26, 386, W - 52, 60));
            if (available != null && phase == Phase.Idle) DrawUpdateBanner(g, new RectangleF(26, 458, W - 52, 58));
            else DrawHint(g, new RectangleF(30, 458, W - 60, 48));
            DrawFooter(g);

            using (Pen border = new Pen(Color.FromArgb(46, 46, 66), 1f))
                g.DrawRectangle(border, 0, 0, W - 1, H - 1);
        }

        void DrawBackdrop(Graphics g)
        {
            g.Clear(Bg);
            Glow(g, new PointF(W * 0.92f, -30), 300, Color.FromArgb(52, Pink));
            Glow(g, new PointF(W * 0.04f, H + 30), 290, Color.FromArgb(38, Cyan));
            for (int i = 0; i < stars.Length; i++)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(starAlpha[i], 220, 220, 255)))
                    g.FillEllipse(b, stars[i].X, stars[i].Y, starSize[i], starSize[i]);
            }
        }

        static void Glow(Graphics g, PointF center, float radius, Color color)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(center.X - radius, center.Y - radius, radius * 2, radius * 2);
                using (PathGradientBrush brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = color;
                    brush.SurroundColors = new[] { Color.FromArgb(0, color) };
                    g.FillPath(brush, path);
                }
            }
        }

        void DrawTitleBar(Graphics g)
        {
            if (logo != null) g.DrawImage(logo, 14, 10, 18, 18);
            float x = DrawSpaced(g, "COSMIC", fBrand, Cyan, 40, 12, 2f);
            DrawSpaced(g, "RELOAD", fBrand, Pink, x + 4, 12, 2f);

            RectangleF min = new RectangleF(W - 92, 0, 46, TitleH);
            RectangleF close = new RectangleF(W - 46, 0, 46, TitleH);
            targets["min"] = min;
            targets["close"] = close;
            if (hover == "min") Fill(g, min, Color.FromArgb(18, 255, 255, 255));
            if (hover == "close") Fill(g, close, Color.FromArgb(196, 43, 28));
            using (Pen pen = new Pen(hover == "min" ? TextC : Muted, 1.1f))
                g.DrawLine(pen, min.X + 18, min.Y + 19.5f, min.X + 28, min.Y + 19.5f);
            using (Pen pen = new Pen(hover == "close" ? Color.White : Muted, 1.1f))
            {
                g.DrawLine(pen, close.X + 18, close.Y + 14, close.X + 28, close.Y + 24);
                g.DrawLine(pen, close.X + 28, close.Y + 14, close.X + 18, close.Y + 24);
            }
        }

        void DrawHero(Graphics g)
        {
            Glow(g, new PointF(W / 2f, 112), 92, Color.FromArgb(70, Pink));
            Glow(g, new PointF(W / 2f - 20, 104), 64, Color.FromArgb(40, Cyan));
            if (logo != null) g.DrawImage(logo, W / 2f - 50, 62, 100, 100);

            const string word = "COSMICRELOAD";
            float width = SpacedWidth(g, word, fWord, 3f);
            float x = (W - width) / 2f;
            RectangleF bounds = new RectangleF(x, 176, width, 40);
            using (LinearGradientBrush brush = new LinearGradientBrush(bounds, Cyan, Pink, LinearGradientMode.Horizontal))
                DrawSpaced(g, word, fWord, brush, x, 176, 3f);

            const string eyebrow = "PLAY WITH THE COSMIC CLIENT";
            float ew = SpacedWidth(g, eyebrow, fEyebrow, 2.4f);
            DrawSpaced(g, eyebrow, fEyebrow, Muted, (W - ew) / 2f, 222, 2.4f);
        }

        void DrawStatusCard(Graphics g, float x, float y)
        {
            RectangleF card = new RectangleF(x, y, W - 2 * x, 104);
            using (GraphicsPath path = Round(card, 12))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(225, PanelC))) g.FillPath(b, path);
                using (Pen p = new Pen(Edge)) g.DrawPath(p, path);
            }
            using (Pen p = new Pen(Edge)) g.DrawLine(p, card.X + 16, card.Y + 52, card.Right - 16, card.Y + 52);

            // Cosmic Client row
            string label, sub, state;
            Color color;
            switch (client)
            {
                case ClientState.Missing:
                    label = "Cosmic Client"; sub = "Not found on this PC. Click to download";
                    state = "Not installed"; color = Bad; break;
                case ClientState.Running:
                    label = "Cosmic Client"; sub = phase == Phase.Launched ? "Running with CosmicReload support" : "Close it so it can restart with support";
                    state = phase == Phase.Launched ? "Running" : "Open"; color = phase == Phase.Launched ? Good : Warn; break;
                default:
                    label = "Cosmic Client"; sub = "Installed";
                    state = "Ready"; color = Good; break;
            }
            RectangleF row1 = new RectangleF(card.X, card.Y, card.Width, 52);
            if (client == ClientState.Missing)
            {
                targets["client"] = row1;
                if (hover == "client") FillRound(g, Inset(row1, 4), 9, Color.FromArgb(10, 255, 255, 255));
            }
            DrawRow(g, row1, label, sub, state, color);

            // server row
            RectangleF row2 = new RectangleF(card.X, card.Y + 52, card.Width, 52);
            targets["server"] = row2;
            if (hover == "server") FillRound(g, Inset(row2, 4), 9, Color.FromArgb(10, 255, 255, 255));
            string serverSub = DateTime.Now < copiedUntil ? "Address copied" : "Click to copy the address";
            string serverState;
            Color serverColor;
            if (server == null) { serverState = "Checking"; serverColor = Muted; }
            else if (server.Online)
            {
                serverState = server.Max > 0 ? "Online  " + server.Players + "/" + server.Max : "Online";
                serverColor = Good;
            }
            else { serverState = "Offline"; serverColor = Bad; }
            DrawRow(g, row2, Config.ServerAddress, serverSub, serverState, serverColor);
        }

        void DrawRow(Graphics g, RectangleF row, string label, string sub, string state, Color color)
        {
            Glow(g, new PointF(row.X + 24, row.Y + 26), 9, Color.FromArgb(90, color));
            using (SolidBrush b = new SolidBrush(color)) g.FillEllipse(b, row.X + 20, row.Y + 22, 8, 8);
            using (SolidBrush b = new SolidBrush(TextC)) g.DrawString(label, fLabel, b, row.X + 40, row.Y + 9, typo);
            using (SolidBrush b = new SolidBrush(Muted)) g.DrawString(sub, fSub, b, row.X + 40, row.Y + 29, typo);
            SizeF size = g.MeasureString(state, fState, PointF.Empty, typo);
            using (SolidBrush b = new SolidBrush(color))
                g.DrawString(state, fState, b, row.Right - 18 - size.Width, row.Y + (row.Height - size.Height) / 2f, typo);
        }

        void DrawPlay(Graphics g, RectangleF r)
        {
            string text;
            bool enabled;
            if (phase == Phase.Updating) { text = "UPDATING " + updateProgress + "%"; enabled = false; }
            else if (client == ClientState.Missing) { text = "GET COSMIC CLIENT"; enabled = true; }
            else if (phase == Phase.Launched) { text = "LAUNCHED"; enabled = false; }
            else if (client == ClientState.Running) { text = "CLOSE COSMIC CLIENT"; enabled = false; }
            else { text = "PLAY"; enabled = true; }

            if (enabled) targets["play"] = r;
            bool hot = enabled && hover == "play";
            bool down = hot && pressed == "play";
            if (hot && !down) r.Offset(0, -1);

            using (GraphicsPath path = Round(r, 12))
            {
                if (enabled)
                {
                    // soft pink glow under the button
                    for (int i = 1; i <= 6; i++)
                    {
                        RectangleF halo = new RectangleF(r.X + 10 - i * 1.5f, r.Y + 10 + i, r.Width - 20 + i * 3, r.Height - 6);
                        using (GraphicsPath hp = Round(halo, 14))
                        using (SolidBrush hb = new SolidBrush(Color.FromArgb(hot ? 14 : 10, Pink)))
                            g.FillPath(hb, hp);
                    }
                    using (LinearGradientBrush brush = new LinearGradientBrush(r, Cyan, Pink, LinearGradientMode.ForwardDiagonal))
                        g.FillPath(brush, path);
                    if (hot) using (SolidBrush b = new SolidBrush(Color.FromArgb(down ? 0 : 26, 255, 255, 255))) g.FillPath(b, path);
                    if (down) using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 0, 0, 0))) g.FillPath(b, path);
                    // top highlight
                    using (Pen p = new Pen(Color.FromArgb(70, 255, 255, 255)))
                        g.DrawLine(p, r.X + 12, r.Y + 1, r.Right - 12, r.Y + 1);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(Panel2)) g.FillPath(b, path);
                    using (Pen p = new Pen(Edge)) g.DrawPath(p, path);
                }
            }

            Font font = text.Length > 8 ? fPlaySmall : fPlay;
            float spacing = text.Length > 8 ? 2.6f : 5f;
            float tw = SpacedWidth(g, text, font, spacing);
            SizeF th = g.MeasureString("A", font, PointF.Empty, typo);
            DrawSpaced(g, text, font, enabled ? Bg : Faint, r.X + (r.Width - tw) / 2f, r.Y + (r.Height - th.Height) / 2f, spacing);
        }

        void DrawHint(Graphics g, RectangleF r)
        {
            string text;
            Color color = Muted;
            if (error != null) { text = error; color = Bad; }
            else if (phase == Phase.Updating)
                text = "Updating Play CosmicReload to version " + updateVersion + ". It reopens by itself in a moment.";
            else if (client == ClientState.Missing)
                text = "Install the Cosmic Client from cosmicclient.com, then come back here.";
            else if (phase == Phase.Launched)
                text = "The Cosmic Client is starting. Join " + Config.ServerAddress
                       + " (the address is on your clipboard).";
            else if (client == ClientState.Running)
                text = "The Cosmic Client is already open. Close it, then press Play here.";
            else
                text = "Opens the Cosmic Client with CosmicReload support. Then join " + Config.ServerAddress + ".";
            using (StringFormat center = (StringFormat)StringFormat.GenericTypographic.Clone())
            using (SolidBrush b = new SolidBrush(color))
            {
                center.Alignment = StringAlignment.Center;
                center.LineAlignment = StringAlignment.Near;
                center.Trimming = StringTrimming.Word;
                g.DrawString(text, fHint, b, r, center);
            }
        }

        void DrawUpdateBanner(Graphics g, RectangleF r)
        {
            using (GraphicsPath path = Round(r, 12))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(235, PanelC))) g.FillPath(b, path);
                using (LinearGradientBrush edge = new LinearGradientBrush(r, Color.FromArgb(150, Cyan), Color.FromArgb(150, Pink), 0f))
                using (Pen p = new Pen(edge, 1f))
                    g.DrawPath(p, path);
            }
            Glow(g, new PointF(r.X + 24, r.Y + r.Height / 2f), 9, Color.FromArgb(110, Pink));
            using (SolidBrush b = new SolidBrush(Pink)) g.FillEllipse(b, r.X + 20, r.Y + r.Height / 2f - 4, 8, 8);
            using (SolidBrush b = new SolidBrush(TextC)) g.DrawString("Update available", fLabel, b, r.X + 40, r.Y + 11, typo);

            string version = "Version " + available.Version + "  ·  ";
            using (SolidBrush b = new SolidBrush(Muted)) g.DrawString(version, fSub, b, r.X + 40, r.Y + 31, typo);
            float nx = r.X + 40 + g.MeasureString(version, fSub, PointF.Empty, typo).Width;
            SizeF ns = g.MeasureString("What's new", fSub, PointF.Empty, typo);
            targets["notes"] = new RectangleF(nx - 3, r.Y + 28, ns.Width + 6, ns.Height + 6);
            using (SolidBrush b = new SolidBrush(hover == "notes" ? TextC : Cyan)) g.DrawString("What's new", fSub, b, nx, r.Y + 31, typo);
            if (hover == "notes")
                using (SolidBrush b = new SolidBrush(TextC)) g.FillRectangle(b, nx, r.Y + 31 + ns.Height + 1, ns.Width, 1f);

            RectangleF update = new RectangleF(r.Right - 14 - 76, r.Y + (r.Height - 30) / 2f, 76, 30);
            RectangleF later = new RectangleF(update.X - 8 - 64, update.Y, 64, 30);
            SmallButton(g, "later", "Later", later, false);
            SmallButton(g, "update", "Update", update, true);
        }

        void SmallButton(Graphics g, string id, string text, RectangleF r, bool primary)
        {
            targets[id] = r;
            bool hot = hover == id;
            using (GraphicsPath path = Round(r, 8))
            {
                if (primary)
                {
                    using (LinearGradientBrush brush = new LinearGradientBrush(r, Cyan, Pink, LinearGradientMode.ForwardDiagonal))
                        g.FillPath(brush, path);
                    if (hot) using (SolidBrush b = new SolidBrush(Color.FromArgb(pressed == id ? 0 : 30, 255, 255, 255))) g.FillPath(b, path);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(hot ? Color.FromArgb(34, 34, 50) : Panel2)) g.FillPath(b, path);
                    using (Pen p = new Pen(Edge)) g.DrawPath(p, path);
                }
            }
            SizeF size = g.MeasureString(text, fState, PointF.Empty, typo);
            using (SolidBrush b = new SolidBrush(primary ? Bg : (hot ? TextC : Muted)))
                g.DrawString(text, fState, b, r.X + (r.Width - size.Width) / 2f, r.Y + (r.Height - size.Height) / 2f, typo);
        }

        void DrawFooter(Graphics g)
        {
            float y = H - 44;
            using (Pen p = new Pen(Edge)) g.DrawLine(p, 0, y, W, y);
            y += 15;

            float x = 22;
            using (SolidBrush b = new SolidBrush(Faint)) g.DrawString("v" + Config.Version, fFoot, b, x, y, typo);
            x += g.MeasureString("v" + Config.Version, fFoot, PointF.Empty, typo).Width + 14;
            x = Link(g, "source", "Open source", x, y) + 14;
            if (installed) Link(g, "auto", autoUpdate ? "Auto-update: on" : "Auto-update: off", x, y);

            const string reload = "Get the Reload Client  →";
            float rw = g.MeasureString(reload, fFoot, PointF.Empty, typo).Width;
            Link(g, "reload", reload, W - 22 - rw, y);
        }

        float Link(Graphics g, string id, string text, float x, float y)
        {
            SizeF size = g.MeasureString(text, fFoot, PointF.Empty, typo);
            RectangleF r = new RectangleF(x - 4, y - 4, size.Width + 8, size.Height + 8);
            targets[id] = r;
            Color color = hover == id ? TextC : Muted;
            using (SolidBrush b = new SolidBrush(color)) g.DrawString(text, fFoot, b, x, y, typo);
            if (hover == id)
                using (LinearGradientBrush line = new LinearGradientBrush(new RectangleF(x, 0, size.Width + 1, 1), Cyan, Pink, 0f))
                    g.FillRectangle(line, x, y + size.Height + 1, size.Width, 1.2f);
            return x + size.Width;
        }

        // ---------------------------------------------------------------- helpers

        float SpacedWidth(Graphics g, string text, Font font, float spacing)
        {
            float w = 0;
            foreach (char c in text) w += g.MeasureString(c.ToString(), font, PointF.Empty, typo).Width + spacing;
            return w - spacing;
        }

        float DrawSpaced(Graphics g, string text, Font font, Color color, float x, float y, float spacing)
        {
            using (SolidBrush b = new SolidBrush(color)) return DrawSpaced(g, text, font, b, x, y, spacing);
        }

        float DrawSpaced(Graphics g, string text, Font font, Brush brush, float x, float y, float spacing)
        {
            foreach (char c in text)
            {
                string s = c.ToString();
                g.DrawString(s, font, brush, x, y, typo);
                x += g.MeasureString(s, font, PointF.Empty, typo).Width + spacing;
            }
            return x - spacing;
        }

        static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        static void Fill(Graphics g, RectangleF r, Color color)
        {
            using (SolidBrush b = new SolidBrush(color)) g.FillRectangle(b, r);
        }

        static void FillRound(Graphics g, RectangleF r, float radius, Color color)
        {
            using (GraphicsPath path = Round(r, radius))
            using (SolidBrush b = new SolidBrush(color))
                g.FillPath(b, path);
        }

        static RectangleF Inset(RectangleF r, float by)
        {
            return new RectangleF(r.X + by, r.Y + by, r.Width - 2 * by, r.Height - 2 * by);
        }
    }
}
