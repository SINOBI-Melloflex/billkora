using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Management;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Billcora Setup")]
[assembly: AssemblyDescription("Billcora Setup 0.3.1")]
[assembly: AssemblyCompany("Billcora")]
[assembly: AssemblyProduct("Billcora")]
[assembly: AssemblyCopyright("Billcora")]
[assembly: AssemblyVersion("0.3.1.0")]
[assembly: AssemblyFileVersion("0.3.1.0")]

class Btn : Control
{
    public bool Primary;
    bool hover;
    public Btn(string t, bool primary)
    {
        Text = t; Primary = primary; Cursor = Cursors.Hand;
        Font = new Font("Segoe UI Semibold", 10.5f);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    public static GraphicsPath Round(Rectangle r, int rad)
    {
        GraphicsPath p = new GraphicsPath(); int d = rad * 2;
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Parent != null ? Parent.BackColor : Color.White);
        Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
        Color fill, border, txt;
        if (!Enabled) { fill = Color.FromArgb(226, 231, 242); border = fill; txt = Color.FromArgb(140, 150, 175); }
        else if (Primary) { fill = hover ? Color.FromArgb(29, 78, 216) : Color.FromArgb(37, 99, 235); border = fill; txt = Color.White; }
        else { fill = hover ? Color.FromArgb(243, 245, 250) : Color.White; border = Color.FromArgb(210, 216, 229); txt = Color.FromArgb(31, 42, 68); }
        using (GraphicsPath p = Round(r, Height / 7))
        {
            using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
            using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
        }
        StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
        using (SolidBrush tb = new SolidBrush(txt)) g.DrawString(Text, Font, tb, new RectangleF(0, 0, Width, Height), sf);
    }
}

class Bar : Control
{
    int v;
    public int Value { get { return v; } set { v = Math.Max(0, Math.Min(100, value)); Invalidate(); } }
    public Bar() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent != null ? Parent.BackColor : Color.White);
        Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (GraphicsPath p1 = Btn.Round(r, r.Height / 2)) using (SolidBrush b1 = new SolidBrush(Color.FromArgb(230, 234, 243))) g.FillPath(b1, p1);
        int w = (int)((Width - 1) * v / 100.0);
        if (w > r.Height)
        {
            Rectangle f = new Rectangle(0, 0, w, r.Height);
            using (GraphicsPath p2 = Btn.Round(f, f.Height / 2))
            using (LinearGradientBrush b2 = new LinearGradientBrush(f, Color.FromArgb(59, 130, 246), Color.FromArgb(29, 78, 216), 0f)) g.FillPath(b2, p2);
        }
    }
}

class MainForm : Form
{
    const string Ver = "0.3.1";
    const string CountJs = @"const path=require('path');const app=process.argv[2];const out=[];
try{const D=require(path.join(app,'node_modules','better-sqlite3'));const d=new D(path.join(app,'billing.db'),{fileMustExist:true});
for(const t of ['customers','packages','routers','zones','users','invoices','payments']){try{out.push(t+'='+d.prepare('select count(*) c from '+t).get().c);}catch(e){out.push(t+'=-1');}}
d.close();}catch(e){out.push('error='+String(e.message).replace(/[\r\n;]/g,' '));}
console.log(out.join(';'));";

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, int w, int l);

    float k = 1f; int W, H, Lw;
    Image logo;
    Label title, sub, lblLoc, lblHint, lblStatus, lblX;
    TextBox txt; Btn btnBrowse, btnCancel, btnMain; Bar bar;
    int state = 0; bool isUpdate = false;
    static int Port = 4010;
    static string LastApp = "";
    static bool Silent = false;

    int S(int v) { return (int)Math.Round(v * k); }

    [STAThread]
    static void Main(string[] args)
    {
        try { SetProcessDPIAware(); } catch { }
        try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
        bool created;
        Mutex mx = new Mutex(true, "Global\\BillcoraSetupMutex", out created);
        if (!created) { MessageBox.Show("Billcora Setup is already running.", "Billcora"); return; }
        try { File.Delete(Path.Combine(Path.GetTempPath(), "Billcora-Setup.log")); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Fatal(e.Exception); };
        AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Fatal(e.ExceptionObject as Exception); };
        foreach (string a in args) { if (a.Equals("/silent", StringComparison.OrdinalIgnoreCase)) Silent = true; }
        MainForm mf = new MainForm();
        if (Silent)
        {
            mf.ShowInTaskbar = false; mf.Opacity = 0;
            mf.Shown += delegate { mf.txt.Text = FindInstall(); mf.OnMain(null, EventArgs.Empty); };
        }
        Application.Run(mf);
        GC.KeepAlive(mx);
    }

    static void Fatal(Exception ex)
    {
        try { Log("FATAL: " + ex); WriteReport(LastApp, "crash: " + (ex == null ? "unknown" : ex.Message)); } catch { }
        try
        {
            MessageBox.Show("Setup hit a problem and could not continue.\r\n\r\n" + (ex == null ? "" : ex.Message) + "\r\n\r\nA report was saved on your Desktop: Billcora-Setup-Report.txt\r\nPlease send that file to support.", "Billcora Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
        Environment.Exit(1);
    }

    static void Log(string m)
    {
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "Billcora-Setup.log"), DateTime.Now.ToString("s") + " " + m + "\r\n"); } catch { }
    }

    static void WriteReport(string app, string reason)
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Billcora Setup report  " + DateTime.Now.ToString("s"));
            sb.AppendLine("Setup version: " + Ver + "    Reason: " + reason);
            sb.AppendLine("Folder: " + app + "    Port: " + Port);
            sb.AppendLine("OS: " + Environment.OSVersion + "  64bit=" + Environment.Is64BitOperatingSystem + "  CLR=" + Environment.Version);
            try { foreach (DriveInfo di in DriveInfo.GetDrives()) { if (di.DriveType == DriveType.Fixed && di.IsReady) sb.AppendLine("Drive " + di.Name + " free " + (di.AvailableFreeSpace / 1048576) + " MB"); } } catch { }
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName FROM AntiVirusProduct"))
                { foreach (ManagementObject mo in s.Get()) sb.AppendLine("Antivirus: " + mo["displayName"]); }
            }
            catch { sb.AppendLine("Antivirus: not readable"); }
            sb.AppendLine("WebView2 installed: " + HasWebView2());
            if (!string.IsNullOrEmpty(app))
            {
                foreach (string f in new string[] { "server.js", "runtime\\node.exe", "runtime\\vcruntime140.dll", "node_modules\\better-sqlite3\\build\\Release\\better_sqlite3.node", "billing.db", ".secret", "port.txt", "run.cmd" })
                    sb.AppendLine("File " + f + ": " + File.Exists(Path.Combine(app, f)));
            }
            sb.AppendLine("Task Billcora: " + TaskInfo("Billcora"));
            sb.AppendLine("Task BillcoraWatchdog: " + TaskInfo("BillcoraWatchdog"));
            sb.AppendLine("--- setup log ---");
            string lg = Path.Combine(Path.GetTempPath(), "Billcora-Setup.log");
            if (File.Exists(lg)) sb.AppendLine(File.ReadAllText(lg));
            if (!string.IsNullOrEmpty(app))
            {
                sb.AppendLine("--- app.log (last lines) ---");
                sb.AppendLine(LogTail(app));
            }
            string desk = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            File.WriteAllText(Path.Combine(desk, "Billcora-Setup-Report.txt"), sb.ToString());
        }
        catch { }
    }

    static string TaskInfo(string name)
    {
        try { string o; int rc = Exec("schtasks.exe", "/Query /TN " + name, out o); return rc == 0 ? "exists" : "missing"; } catch { return "unknown"; }
    }

    Label L(string text, string font, float size, Color c)
    {
        Label l = new Label(); l.Text = text; l.Font = new Font(font, size); l.ForeColor = c; l.BackColor = Color.White; l.AutoSize = false; return l;
    }

    public MainForm()
    {
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
        W = S(780); H = S(500); Lw = S(270);
        Text = "Billcora Setup"; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(W, H); BackColor = Color.White; DoubleBuffered = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        try
        {
            Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png");
            if (st != null) { Image tmp = Image.FromStream(st); logo = new Bitmap(tmp); tmp.Dispose(); }
        }
        catch { }

        int x0 = Lw + S(44), cw = W - x0 - S(44);
        Color dark = Color.FromArgb(11, 21, 48), grey = Color.FromArgb(107, 122, 153);

        title = L("Install Billcora", "Segoe UI Semibold", 23f, dark); title.SetBounds(x0, S(62), cw, S(48));
        sub = L("", "Segoe UI", 10.5f, grey); sub.SetBounds(x0, S(114), cw, S(86));
        lblLoc = L("Install location", "Segoe UI Semibold", 10.5f, dark); lblLoc.SetBounds(x0, S(214), cw, S(24));
        txt = new TextBox(); txt.Font = new Font("Segoe UI", 11f); txt.BorderStyle = BorderStyle.FixedSingle;
        txt.SetBounds(x0, S(242), cw - S(116), S(30));
        btnBrowse = new Btn("Browse...", false); btnBrowse.SetBounds(x0 + cw - S(104), S(240), S(104), S(34));
        lblHint = L("", "Segoe UI", 9.5f, grey); lblHint.SetBounds(x0, S(290), cw, S(70));
        bar = new Bar(); bar.SetBounds(x0, S(250), cw, S(10)); bar.Visible = false; bar.BackColor = Color.White;
        lblStatus = L("", "Segoe UI", 10.5f, grey); lblStatus.SetBounds(x0, S(272), cw, S(90)); lblStatus.Visible = false;
        btnMain = new Btn("Install", true); btnMain.SetBounds(W - S(44) - S(150), H - S(48) - S(46), S(150), S(46));
        btnCancel = new Btn("Cancel", false); btnCancel.SetBounds(btnMain.Left - S(14) - S(120), btnMain.Top, S(120), S(46));
        lblX = L("\u00D7", "Segoe UI", 20f, grey); lblX.SetBounds(W - S(46), S(10), S(36), S(36)); lblX.TextAlign = ContentAlignment.MiddleCenter; lblX.Cursor = Cursors.Hand;

        Controls.AddRange(new Control[] { title, sub, lblLoc, txt, btnBrowse, lblHint, bar, lblStatus, btnCancel, btnMain, lblX });
        txt.Text = FindInstall();
        RefreshMode();
        txt.TextChanged += delegate { RefreshMode(); };

        btnBrowse.Click += delegate
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Choose the folder for Billcora"; d.SelectedPath = txt.Text;
                if (d.ShowDialog(this) == DialogResult.OK) txt.Text = d.SelectedPath;
            }
        };
        btnCancel.Click += delegate { if (state != 1) Close(); };
        lblX.Click += delegate { if (state != 1) Close(); };
        btnMain.Click += OnMain;
        MouseDown += Drag;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (state == 1) e.Cancel = true;
        base.OnFormClosing(e);
    }

    void RefreshMode()
    {
        if (state != 0) return;
        string d = txt.Text.Trim().TrimEnd('\\');
        bool u = false;
        try { u = d.Length > 3 && File.Exists(d + "\\server.js"); } catch { }
        isUpdate = u;
        title.Text = u ? "Update Billcora" : "Install Billcora";
        sub.Text = u
            ? "An existing Billcora was found. Only the program is replaced. Your customers, bills and settings are kept, and a safety backup is taken first."
            : "Fast, reliable billing for your network. Everything Billcora needs is included. Just choose where to install it.";
        lblHint.Text = u
            ? "Found automatically. Change it only if you installed Billcora somewhere else."
            : "Needs about 500 MB of free space. Billcora starts automatically with Windows. No internet is needed.";
        btnMain.Text = u ? "Update" : "Install";
    }

    void Drag(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        Rectangle lr = new Rectangle(0, 0, Lw, H);
        using (LinearGradientBrush bg = new LinearGradientBrush(lr, Color.FromArgb(12, 43, 112), Color.FromArgb(4, 12, 38), 90f)) g.FillRectangle(bg, lr);
        using (SolidBrush glow = new SolidBrush(Color.FromArgb(26, 59, 130, 246))) g.FillEllipse(glow, -S(80), S(20), S(380), S(380));
        int ls = S(170), lx = (Lw - ls) / 2, ly = S(100);
        if (logo != null) g.DrawImage(logo, lx, ly, ls, ls);
        using (Font f1 = new Font("Segoe UI Semibold", 26f))
        using (Font f2 = new Font("Segoe UI", 11f))
        using (Font f3 = new Font("Segoe UI", 9.5f))
        {
            g.DrawString("Billcora", f1, Brushes.White, S(32), S(310));
            using (SolidBrush b = new SolidBrush(Color.FromArgb(169, 188, 230))) g.DrawString("Smart billing for ISPs", f2, b, S(35), S(366));
            using (SolidBrush b = new SolidBrush(Color.FromArgb(126, 147, 196))) g.DrawString("Version  " + Ver, f3, b, S(35), H - S(46));
        }
        using (Pen p = new Pen(Color.FromArgb(34, 53, 107))) g.DrawRectangle(p, 0, 0, W - 1, H - 1);
    }

    // ================= helpers =================
    static int Exec(string file, string args, out string output)
    {
        ProcessStartInfo psi = new ProcessStartInfo(file, args);
        psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
        using (Process p = Process.Start(psi))
        {
            p.ErrorDataReceived += delegate { };
            p.BeginErrorReadLine();
            output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode;
        }
    }

    static bool LooksLikeApp(string d)
    {
        try
        {
            if (string.IsNullOrEmpty(d) || !File.Exists(Path.Combine(d, "server.js"))) return false;
            string pj = Path.Combine(d, "package.json");
            if (!File.Exists(pj)) return false;
            return Regex.IsMatch(File.ReadAllText(pj), "billing|billcora|kkc", RegexOptions.IgnoreCase);
        }
        catch { return false; }
    }

    static string Scan()
    {
        string[] skip = new string[] { "Windows", "$Recycle.Bin", "System Volume Information", "Recovery", "PerfLogs", "ProgramData" };
        foreach (DriveInfo di in DriveInfo.GetDrives())
        {
            try
            {
                if (di.DriveType != DriveType.Fixed || !di.IsReady) continue;
                foreach (string d1 in Directory.GetDirectories(di.Name))
                {
                    string n1 = Path.GetFileName(d1);
                    if (Array.IndexOf(skip, n1) >= 0) continue;
                    if (LooksLikeApp(d1)) return d1;
                    string[] subs;
                    try { subs = Directory.GetDirectories(d1); } catch { continue; }
                    foreach (string d2 in subs) { if (LooksLikeApp(d2)) return d2; }
                }
            }
            catch { }
        }
        return "";
    }

    static string FindInstall()
    {
        List<string> c = new List<string>();
        try
        {
            string o; Exec("schtasks.exe", "/Query /TN Billcora /XML", out o);
            o = o.Replace("\0", "").Replace("&amp;", "&");
            Match m = Regex.Match(o, "<WorkingDirectory>(.*?)</WorkingDirectory>", RegexOptions.Singleline);
            if (m.Success) c.Add(m.Groups[1].Value.Trim().Trim('"').TrimEnd('\\'));
            m = Regex.Match(o, @"([A-Za-z]:\\[^""<]*?)\\run\.cmd");
            if (m.Success) c.Add(m.Groups[1].Value);
        }
        catch { }
        try
        {
            using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Billcora"))
            {
                if (rk != null)
                {
                    string il = rk.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(il)) c.Add(il.TrimEnd('\\'));
                    string us = rk.GetValue("UninstallString") as string;
                    if (!string.IsNullOrEmpty(us))
                    {
                        Match m3 = Regex.Match(us, @"([A-Za-z]:\\[^""]*?)\\(Billcora-Uninstall\.exe|uninstall\.ps1)", RegexOptions.IgnoreCase);
                        if (m3.Success) c.Add(m3.Groups[1].Value);
                    }
                }
            }
        }
        catch { }
        foreach (string d in c) { if (LooksLikeApp(d) || File.Exists(Path.Combine(d, "server.js"))) return d; }
        string s = Scan();
        if (s.Length > 0) return s;
        return Path.GetPathRoot(Environment.SystemDirectory) + "Billcora";
    }

    static bool Up()
    {
        try
        {
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Port + "/login");
            r.Timeout = 3000; r.Proxy = null;
            using (HttpWebResponse s = (HttpWebResponse)r.GetResponse()) return (int)s.StatusCode < 400;
        }
        catch { return false; }
    }

    static bool WaitUp(int tries) { for (int i = 0; i < tries; i++) { if (Up()) return true; Thread.Sleep(4000); } return false; }

    static int ReadPort(string app)
    {
        try { int p; if (int.TryParse(File.ReadAllText(Path.Combine(app, "port.txt")).Trim(), out p) && p > 0 && p < 65536) return p; } catch { }
        return 4010;
    }

    static bool PortBusy(int port)
    {
        try { foreach (System.Net.IPEndPoint ep in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()) { if (ep.Port == port) return true; } } catch { }
        return false;
    }

    static int PickPort()
    {
        for (int p = 4010; p < 4040; p++) if (!PortBusy(p)) return p;
        return 4010;
    }

    static void KillPort(int port)
    {
        try
        {
            string o; Exec("netstat.exe", "-ano -p tcp", out o);
            foreach (string line in o.Split('\n'))
            {
                string t = line.Trim();
                string[] c = Regex.Split(t, @"\s+");
                if (c.Length < 5 || !c[0].Equals("TCP", StringComparison.OrdinalIgnoreCase)) continue;
                if (!c[2].EndsWith(":0") || !c[1].EndsWith(":" + port)) continue;
                int pid; if (!int.TryParse(c[4], out pid)) continue;
                try
                {
                    Process pr = Process.GetProcessById(pid);
                    if (pr.ProcessName.Equals("node", StringComparison.OrdinalIgnoreCase)) { Log("killing node pid " + pid + " on port " + port); pr.Kill(); pr.WaitForExit(5000); }
                }
                catch { }
            }
        }
        catch (Exception ex) { Log("killport: " + ex.Message); }
    }

    static void KillNode(string app)
    {
        try
        {
            using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name='node.exe'"))
            {
                foreach (ManagementObject mo in s.Get())
                {
                    string ep = (mo["ExecutablePath"] as string) ?? "";
                    string cl = (mo["CommandLine"] as string) ?? "";
                    if (ep.StartsWith(app, StringComparison.OrdinalIgnoreCase) || cl.IndexOf(app, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try { Process.GetProcessById(Convert.ToInt32(mo["ProcessId"])).Kill(); } catch { }
                    }
                }
            }
        }
        catch (Exception ex) { Log("killnode: " + ex.Message); }
    }

    static void StopBill(string app)
    {
        string o;
        try { Exec("schtasks.exe", "/End /TN BillcoraWatchdog", out o); } catch { }
        try { File.WriteAllText(Path.Combine(app, "app.stopped"), DateTime.Now.ToString("s")); } catch { }
        try { Exec("schtasks.exe", "/End /TN Billcora", out o); } catch { }
        try { Exec("cmd.exe", "/c pm2 delete kkc-billing", out o); } catch { }
        Thread.Sleep(1500);
        KillNode(app);
        KillPort(Port);
        Thread.Sleep(1500);
    }

    static void StartBill(string app)
    {
        try { File.Delete(Path.Combine(app, "app.stopped")); } catch { }
        string o; Exec("schtasks.exe", "/Run /TN Billcora", out o);
    }

    static Dictionary<string, int> Counts(string app)
    {
        try
        {
            string node = Path.Combine(app, "runtime", "node.exe");
            if (!File.Exists(node)) node = "node.exe";
            string js = Path.Combine(Path.GetTempPath(), "bc_count.js");
            File.WriteAllText(js, CountJs);
            string o; Exec(node, "\"" + js + "\" \"" + app + "\"", out o);
            string line = "";
            foreach (string s in o.Split('\n')) { if (s.IndexOf('=') > 0) line = s.Trim(); }
            if (line.Length == 0 || line.StartsWith("error=")) { Log("counts: " + line); return null; }
            Dictionary<string, int> d = new Dictionary<string, int>();
            foreach (string part in line.Split(';'))
            {
                string[] kv = part.Split('='); int v;
                if (kv.Length == 2 && int.TryParse(kv[1], out v)) d[kv[0]] = v;
            }
            return d;
        }
        catch (Exception ex) { Log("counts failed: " + ex.Message); return null; }
    }

    static string Compare(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        if (a == null || b == null) return null;
        foreach (KeyValuePair<string, int> kv in a)
        {
            int v;
            if (kv.Value >= 0 && b.TryGetValue(kv.Key, out v) && v >= 0 && v < kv.Value) return kv.Key + ": " + kv.Value + " -> " + v;
        }
        return null;
    }

    static string LogTail(string app)
    {
        try
        {
            string f = Path.Combine(app, "logs", "app.log");
            if (!File.Exists(f)) return "";
            string[] ln = File.ReadAllLines(f);
            StringBuilder sb = new StringBuilder();
            for (int i = Math.Max(0, ln.Length - 12); i < ln.Length; i++) sb.AppendLine(ln[i].Length > 160 ? ln[i].Substring(0, 160) : ln[i]);
            return sb.ToString();
        }
        catch { return ""; }
    }

    static bool HasWebView2()
    {
        string g = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        string[] keys = new string[] { @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\" + g, @"SOFTWARE\Microsoft\EdgeUpdate\Clients\" + g };
        foreach (string kk in keys)
        {
            try
            {
                using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(kk))
                {
                    if (rk != null) { string pv = rk.GetValue("pv") as string; if (!string.IsNullOrEmpty(pv) && pv != "0.0.0.0") return true; }
                }
            }
            catch { }
        }
        try
        {
            using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\EdgeUpdate\Clients\" + g))
            {
                if (rk != null) { string pv = rk.GetValue("pv") as string; if (!string.IsNullOrEmpty(pv) && pv != "0.0.0.0") return true; }
            }
        }
        catch { }
        return false;
    }

    static bool SignedByMicrosoft(string f)
    {
        try { X509Certificate c = X509Certificate.CreateFromSignedFile(f); return c.Subject.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0; }
        catch { return false; }
    }

    void Step(int pct, string msg)
    {
        Log(msg);
        try { BeginInvoke(new Action(delegate { bar.Value = pct; lblStatus.Text = msg; })); } catch { }
    }

    bool Download(string url, string dest, string label, int p0, int p1)
    {
        try
        {
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.Timeout = 20000; r.ReadWriteTimeout = 20000; r.UserAgent = "Billcora-Setup/" + Ver;
            using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (FileStream fs = File.Create(dest))
            {
                long total = resp.ContentLength, got = 0; int last = -1; byte[] buf = new byte[65536]; int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    fs.Write(buf, 0, n); got += n;
                    if (total > 0) { int pc = (int)(got * 100 / total); if (pc != last) { last = pc; Step(p0 + (p1 - p0) * pc / 100, label + " (" + pc + "%)"); } }
                }
            }
            return new FileInfo(dest).Length > 100000;
        }
        catch (Exception ex) { Log("download failed " + url + " : " + ex.Message); return false; }
    }

    void EnsureWebView2(int p0, int p1)
    {
        string exe = Path.Combine(Application.StartupPath, "MicrosoftEdgeWebview2Setup.exe");
        if (!File.Exists(exe))
        {
            exe = Path.Combine(Path.GetTempPath(), "bc_MicrosoftEdgeWebview2Setup.exe");
            Step(p0, "Downloading Microsoft WebView2 (optional)...");
            if (!Download("https://go.microsoft.com/fwlink/p/?LinkId=2124703", exe, "Downloading Microsoft WebView2", p0, p1 - 3)) { Log("WebView2 download failed; Billcora will open in the browser"); return; }
        }
        if (!SignedByMicrosoft(exe)) { Log("WebView2 installer signature check failed, skipped"); return; }
        Step(p1 - 2, "Installing Microsoft WebView2...");
        string o; int rc = Exec(exe, "/silent /install", out o); Log("WebView2 rc=" + rc);
    }

    void ExtractPayload(string app)
    {
        using (Stream st = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
        using (ZipArchive za = new ZipArchive(st, ZipArchiveMode.Read))
        {
            int n = za.Entries.Count, i = 0;
            foreach (ZipArchiveEntry en in za.Entries)
            {
                i++;
                string rel = en.FullName.Replace('/', '\\'); while (rel.StartsWith(".\\")) rel = rel.Substring(2);
                if (rel.EndsWith("\\") || en.Name.Length == 0 || rel.Contains("..")) continue;
                string low = rel.ToLowerInvariant();
                if (low.StartsWith("billing.db") || low == ".secret" || low == "port.txt") continue;
                string dest = Path.Combine(app, rel);
                FileInfo fi = new FileInfo(dest);
                if (low == "config.js" && fi.Exists) continue;
                bool heavy = low.StartsWith("runtime\\") || low.StartsWith("node_modules\\") || low.StartsWith("webview2\\");
                if (heavy && fi.Exists && fi.Length == en.Length) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                for (int attempt = 1; ; attempt++)
                {
                    try { en.ExtractToFile(dest, true); break; }
                    catch (IOException)
                    {
                        if (attempt >= 4) throw;
                        KillNode(app); KillPort(Port); Thread.Sleep(1200);
                    }
                }
                if (i % 20 == 0) Step(30 + (int)(44.0 * i / n), "Installing files...");
            }
        }
    }

    void AuxFiles(string app)
    {
        Directory.CreateDirectory(Path.Combine(app, "logs"));
        Directory.CreateDirectory(Path.Combine(app, "backups"));
        string sec = Path.Combine(app, ".secret");
        if (!File.Exists(sec))
        {
            byte[] b = new byte[32];
            using (RNGCryptoServiceProvider r = new RNGCryptoServiceProvider()) r.GetBytes(b);
            File.WriteAllText(sec, BitConverter.ToString(b).Replace("-", "").ToLower());
        }
        File.WriteAllText(Path.Combine(app, "port.txt"), Port.ToString());
        string rc = Path.Combine(app, "run.cmd");
        if (File.Exists(rc))
        {
            string t = File.ReadAllText(rc);
            string t2 = Regex.Replace(t, @"set PORT=\d+", "set PORT=" + Port);
            if (t2 != t) File.WriteAllText(rc, t2);
        }
    }

    static void CreateTask(string name, string xml)
    {
        string p = Path.Combine(Path.GetTempPath(), "bc_task_" + name + ".xml");
        File.WriteAllText(p, xml, Encoding.Unicode);
        string o; int rc = Exec("schtasks.exe", "/Create /TN \"" + name + "\" /XML \"" + p + "\" /F", out o);
        try { File.Delete(p); } catch { }
        if (rc != 0) throw new Exception("Could not create the startup task " + name + ": " + o);
    }

    static void CreateTasks(string app)
    {
        string e = SecurityElement.Escape(app);
        string head = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">";
        string x1 = head +
            "<RegistrationInfo><Description>Billcora</Description><URI>\\Billcora</URI></RegistrationInfo>" +
            "<Principals><Principal id=\"A\"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
            "<Settings><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><RestartOnFailure><Count>999</Count><Interval>PT1M</Interval></RestartOnFailure><StartWhenAvailable>true</StartWhenAvailable><IdleSettings><StopOnIdleEnd>true</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings></Settings>" +
            "<Triggers><BootTrigger /></Triggers>" +
            "<Actions Context=\"A\"><Exec><Command>cmd.exe</Command><Arguments>/c \"" + e + "\\run.cmd\"</Arguments><WorkingDirectory>" + e + "</WorkingDirectory></Exec></Actions></Task>";
        CreateTask("Billcora", x1);
        string x2 = head +
            "<RegistrationInfo><Description>Billcora Watchdog</Description><URI>\\BillcoraWatchdog</URI></RegistrationInfo>" +
            "<Principals><Principal id=\"A\"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
            "<Settings><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT10M</ExecutionTimeLimit><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><StartWhenAvailable>true</StartWhenAvailable><Hidden>true</Hidden></Settings>" +
            "<Triggers><TimeTrigger><Repetition><Interval>PT5M</Interval></Repetition><StartBoundary>2026-01-01T00:00:00</StartBoundary><Enabled>true</Enabled></TimeTrigger></Triggers>" +
            "<Actions Context=\"A\"><Exec><Command>powershell.exe</Command><Arguments>-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + e + "\\Billcora-Watchdog.ps1\"</Arguments><WorkingDirectory>" + e + "</WorkingDirectory></Exec></Actions></Task>";
        try { CreateTask("BillcoraWatchdog", x2); } catch (Exception ex) { Log("watchdog task: " + ex.Message); }
    }

    static void Firewall()
    {
        string o;
        try { Exec("netsh.exe", "advfirewall firewall delete rule name=\"Billcora\"", out o); } catch { }
        try
        {
            int rc = Exec("netsh.exe", "advfirewall firewall add rule name=\"Billcora\" dir=in action=allow protocol=TCP localport=" + Port + " profile=private,domain", out o);
            Log("firewall rc=" + rc);
        }
        catch (Exception ex) { Log("firewall: " + ex.Message); }
    }

    static void MakeLink(string lnk, string target, string args, string wd, string icon)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lnk));
        Type t = Type.GetTypeFromProgID("WScript.Shell");
        object sh = Activator.CreateInstance(t);
        object l = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
        Type lt = l.GetType();
        lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, l, new object[] { target });
        lt.InvokeMember("Arguments", BindingFlags.SetProperty, null, l, new object[] { args });
        lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, l, new object[] { wd });
        lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, l, new object[] { icon });
        lt.InvokeMember("Description", BindingFlags.SetProperty, null, l, new object[] { "Billcora" });
        lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, l, null);
    }

    static void Shortcuts(string app)
    {
        string target = Path.Combine(Environment.SystemDirectory, "wscript.exe");
        string args = "\"" + Path.Combine(app, "Billcora-Open.vbs") + "\"";
        string icon = Path.Combine(app, "billcora.ico") + ",0";
        string[] places = new string[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Billcora.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Billcora.lnk") };
        foreach (string p in places)
        {
            try { MakeLink(p, target, args, app, icon); } catch (Exception ex) { Log("shortcut " + p + ": " + ex.Message); }
        }
    }

    static void Register(string app)
    {
        using (RegistryKey k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Billcora"))
        {
            k.SetValue("DisplayName", "Billcora");
            k.SetValue("DisplayVersion", Ver);
            k.SetValue("Publisher", "Billcora");
            k.SetValue("InstallLocation", app);
            k.SetValue("DisplayIcon", Path.Combine(app, "billcora.ico"));
            k.SetValue("UninstallString", "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(app, "uninstall.ps1") + "\"");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", 200000, RegistryValueKind.DWord);
        }
    }

    void SaveLog(string app)
    {
        try
        {
            string lg = Path.Combine(Path.GetTempPath(), "Billcora-Setup.log");
            string dst = Path.Combine(app, "logs");
            if (File.Exists(lg) && Directory.Exists(dst)) File.Copy(lg, Path.Combine(dst, "setup.log"), true);
        }
        catch { }
    }

    // ================= flow =================
    void OnMain(object s, EventArgs e)
    {
        if (state == 0)
        {
            string app = txt.Text.Trim().TrimEnd('\\');
            if (app.Length < 4 || !Path.IsPathRooted(app) || app.StartsWith("\\\\") || app.Length <= Path.GetPathRoot(app).Length)
            {
                MessageBox.Show(this, "Please choose a folder on a local drive, for example C:\\Billcora", "Billcora", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            bool upd = File.Exists(app + "\\server.js");
            if (!upd && Directory.Exists(app) && Directory.GetFileSystemEntries(app).Length > 0)
            {
                if (MessageBox.Show(this, "This folder is not empty and does not contain Billcora.\r\nInstall Billcora here anyway?", "Billcora", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }
            state = 1; LastApp = app;
            lblLoc.Visible = false; txt.Visible = false; btnBrowse.Visible = false; lblHint.Visible = false;
            title.Text = upd ? "Updating Billcora" : "Installing Billcora";
            sub.Text = "Please keep this window open. This takes a few minutes.";
            bar.Visible = true; lblStatus.Visible = true; btnMain.Enabled = false; btnCancel.Enabled = false; lblX.Visible = false;
            Thread t = new Thread(delegate()
            {
                try { Run(app, upd); }
                catch (Exception ex)
                {
                    Log("fatal: " + ex);
                    WriteReport(app, "exception: " + ex.Message);
                    Finish(3, "Something went wrong", ex.Message + "\r\n\r\nA report was saved on your Desktop: Billcora-Setup-Report.txt. Please send it to support.", "Close");
                }
            });
            t.IsBackground = true; t.Start();
        }
        else if (state == 2)
        {
            try { Process.Start("http://localhost:" + Port); } catch { }
            Close();
        }
        else Close();
    }

    void Finish(int newState, string head, string text, string mainText)
    {
        BeginInvoke(new Action(delegate
        {
            state = newState; bar.Value = 100; bar.Visible = false;
            title.Text = head; sub.Height = S(240); sub.Text = text; lblStatus.Visible = false;
            btnMain.Text = mainText; btnMain.Enabled = true; btnCancel.Enabled = true; lblX.Visible = true;
            btnCancel.Visible = false; if (Silent) { Environment.ExitCode = (state == 2 ? 0 : 1); Close(); }
        }));
    }

    void Run(string app, bool upd)
    {
        string o;
        Log("Setup " + Ver + " start. folder=" + app + " update=" + upd);
        if (!Environment.Is64BitOperatingSystem) { WriteReport(app, "32-bit Windows"); Finish(3, "Not supported", "Billcora needs 64-bit Windows 10 or newer.", "Close"); return; }
        Port = upd ? ReadPort(app) : PickPort();
        try
        {
            DriveInfo di = new DriveInfo(Path.GetPathRoot(app));
            if (di.AvailableFreeSpace < 700L * 1024 * 1024) { WriteReport(app, "low disk space"); Finish(3, "Not enough free space", "Billcora needs about 500 MB free on drive " + di.Name + ". Please free some space or choose another drive, then run Setup again.", "Close"); return; }
        }
        catch { }

        string backup = "";
        Dictionary<string, int> before = null;
        if (upd)
        {
            try
            {
                Step(4, "Stopping the old Billcora...");
                StopBill(app);
                if (PortBusy(Port)) { int np = PickPort(); Log("port " + Port + " still busy, using " + np); Port = np; }
                Step(10, "Taking a safety backup of your current version...");
                backup = Path.Combine(app, "backups", "update-before-" + Ver + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                int rc = Exec("robocopy.exe", "\"" + app + "\" \"" + backup + "\" /E /XD \"" + app + "\\node_modules\" \"" + app + "\\runtime\" \"" + app + "\\webview2\" \"" + app + "\\logs\" \"" + app + "\\backups\" /NFL /NDL /NJH /NJS /NP", out o);
                if (rc >= 8) throw new Exception("robocopy exit " + rc + ": " + (o.Length > 400 ? o.Substring(0, 400) : o));
                Step(16, "Counting your current data...");
                before = Counts(app);
            }
            catch (Exception ex)
            {
                try { StartBill(app); } catch { }
                Log("backup failed: " + ex); WriteReport(app, "backup failed: " + ex.Message);
                Finish(3, "Nothing was changed", "The safety backup could not be made (" + ex.Message + "). Check free disk space and permissions, then try again. Billcora is running as before.", "Close");
                return;
            }
        }

        bool ok = false; string err = "";
        Dictionary<string, int> after = null;
        try
        {
            if (!HasWebView2()) EnsureWebView2(20, 40);
            Step(44, "Installing files...");
            Directory.CreateDirectory(app);
            ExtractPayload(app);
            AuxFiles(app);
            if (!File.Exists(Path.Combine(app, "runtime", "vcruntime140.dll")) || !File.Exists(Path.Combine(app, "runtime", "node.exe"))) throw new Exception("Some program files were not written (antivirus may have blocked them).");
            Step(78, "Setting up automatic start with Windows...");
            CreateTasks(app);
            Step(82, "Opening the firewall for your network...");
            Firewall();
            Step(85, "Creating shortcuts...");
            Shortcuts(app);
            Register(app);
            Step(90, "Starting Billcora...");
            StartBill(app);
            ok = WaitUp(25);
            if (!ok) err = LogTail(app);
            if (ok && upd && before != null)
            {
                Step(96, "Checking that all your data is still there...");
                after = Counts(app);
                string diff = Compare(before, after);
                if (diff != null) { ok = false; err = "Data check failed (" + diff + ")."; }
            }
        }
        catch (Exception ex) { ok = false; err = ex.Message; Log("install error: " + ex); }

        if (ok)
        {
            try { string oldU = Path.Combine(app, "Billcora-Uninstall.exe"); if (File.Exists(oldU)) File.Delete(oldU); } catch { }
            SaveLog(app);
            string vt = "";
            if (after != null && after.ContainsKey("customers") && after.ContainsKey("packages")) vt = "\r\nChecked: " + after["customers"] + " customers and " + after["packages"] + " packages are all there.";
            if (upd) Finish(2, "Billcora is up to date", "Version " + Ver + " is installed and running. Your customers, bills and settings were kept." + vt + "\r\nBackup: " + backup, "Open Billcora");
            else Finish(2, "Billcora is installed", "Billcora is installed and running. It starts by itself with Windows.\r\n\r\nFirst login:  admin  /  admin123\r\nPlease change the password right after you log in.", "Open Billcora");
            return;
        }

        if (!upd)
        {
            WriteReport(app, "start failed: " + err);
            SaveLog(app);
            Finish(3, "Billcora could not start", "Installation finished but the program did not start.\r\n" + err + "\r\nA report was saved on your Desktop: Billcora-Setup-Report.txt. Please send it to support.", "Close");
            return;
        }

        Step(92, "The new version did not start properly. Restoring your old version...");
        bool back = false;
        try
        {
            StopBill(app);
            Exec("robocopy.exe", "\"" + backup + "\" \"" + app + "\" /E /XF billing.db billing.db-wal billing.db-shm /NFL /NDL /NJH /NJS /NP", out o);
            foreach (string f in new string[] { "billing.db", "billing.db-wal", "billing.db-shm" })
            {
                string tgt = Path.Combine(app, f);
                if (File.Exists(tgt)) File.Delete(tgt);
                string sf = Path.Combine(backup, f);
                if (File.Exists(sf)) File.Copy(sf, tgt, true);
            }
            StartBill(app);
            back = WaitUp(20);
        }
        catch (Exception ex) { Log("rollback error: " + ex); }
        WriteReport(app, "update failed: " + err);
        SaveLog(app);
        Finish(3, "Update could not be applied",
            (back ? "Your old version was restored and is running. Your data is safe.\r\n" : "Billcora did not start. Your data is safe in the backup folder.\r\n") + err + "\r\nBackup: " + backup + "\r\nA report was saved on your Desktop: Billcora-Setup-Report.txt. Please send it to support.", "Close");
    }
}