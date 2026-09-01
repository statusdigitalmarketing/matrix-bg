// MatrixBG - Matrix rain desktop wallpaper + idle screensaver with a tray icon.
// Build: build.cmd (uses the csc.exe that ships with .NET Framework 4.x)
// Flags: --fullscreen (start the screensaver immediately)  --window (test window)  --debug (log to %APPDATA%\MatrixBG\debug.log)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MatrixBG
{
    // ------------------------------------------------------------------ settings
    class Settings
    {
        public const string Version = "1.0";

        public Color Color = Color.FromArgb(0, 255, 70);   // classic matrix green
        public bool Rainbow = false;
        public Color Color2 = Color.FromArgb(0, 230, 255);  // second colour for blend modes
        public int Blend = 0;          // 0 off, 1 fade over time, 2 left->right, 3 top->bottom
        public int Speed = 2;          // 0 slow .. 4 fast
        public int Density = 3;        // 0 sparse .. 4 dense
        public int Tail = 4;           // 0 short .. 5 endless
        public int Flicker = 2;        // 0 off, 1 subtle, 2 normal, 3 lots
        public int FontSize = 2;       // 0 tiny .. 4 huge
        public int Charset = 0;        // 0 katakana, 1 binary, 2 hex, 3 latin
        public bool Paused = false;
        public bool Wallpaper = true;          // render behind the desktop icons
        public bool SaverOnIdle = true;        // pop a fullscreen saver after IdleSeconds of no input
        public int IdleSeconds = 120;
        public bool GitTrigger = true;         // show the background while a git command is running
        // Philips Hue
        public bool HueEnabled = false;
        public bool HueOnBackground = true;    // also run the light effect for the git-triggered background
        public Color HueColorGit = Color.FromArgb(150, 0, 255);    // lights while git/background trigger is active
        public Color HueColorIdle = Color.FromArgb(0, 90, 255);    // lights while the overlay is up
        public string HueIp = "";
        public string HueUser = "";
        public string HueLights = "";          // comma-separated light ids; empty = all colour lights
        // MIDI control
        public bool MidiEnabled = false;
        public string MidiPort = "";           // input device name; "" = none chosen yet
        public int MidiChannel = 0;            // 0 = omni, 1-16 = only that channel
        public bool PauseOnVideo = true;       // audio is playing -> pause wallpaper, don't start saver
        public bool PauseWhenFullscreen = true;
        public bool KeepAwake = false;
        // Off by default for the public download: an exe that adds itself to the Run key
        // on first launch is a pattern users and AV heuristics rightly distrust. The tray
        // toggle turns it on with one click.
        public bool Autostart = false;
        public bool Loaded = false;    // true when a settings file was read successfully (vs. first run / unreadable)

        public static readonly int[] IdleChoices = { 30, 60, 120, 300, 600, 1200, 1800 };

        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MatrixBG"); } }
        public static string File { get { return Path.Combine(Dir, "settings.txt"); } }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!System.IO.File.Exists(File)) return s;
                foreach (string line in System.IO.File.ReadAllLines(File))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    int n; bool isInt = int.TryParse(v, out n);   // a bad value only affects its own key
                    switch (k)
                    {
                        case "color": if (isInt) s.Color = Color.FromArgb(n); break;
                        case "rainbow": s.Rainbow = v == "1"; break;
                        case "color2": if (isInt) s.Color2 = Color.FromArgb(n); break;
                        case "blend": if (isInt) s.Blend = Clamp(n, 0, 5); break;
                        case "speed": if (isInt) s.Speed = Clamp(n, 0, 4); break;
                        case "density": if (isInt) s.Density = Clamp(n, 0, 4); break;
                        case "tail": if (isInt) s.Tail = Clamp(n, 0, 5); break;
                        case "flicker": if (isInt) s.Flicker = Clamp(n, 0, 3); break;
                        case "gittrigger": s.GitTrigger = v == "1"; break;
                        case "hueenabled": s.HueEnabled = v == "1"; break;
                        case "hueonbackground": s.HueOnBackground = v == "1"; break;
                        case "hueip": s.HueIp = v; break;
                        case "hueuser": s.HueUser = v; break;
                        case "huelights": s.HueLights = v; break;
                        case "huecolorgit": if (isInt) s.HueColorGit = n == 0 ? Color.Empty : Color.FromArgb(n); break;
                        case "huecoloridle": if (isInt) s.HueColorIdle = n == 0 ? Color.Empty : Color.FromArgb(n); break;
                        case "midienabled": s.MidiEnabled = v == "1"; break;
                        case "midiport": s.MidiPort = v; break;
                        case "midichannel": if (isInt) s.MidiChannel = Clamp(n, 0, 16); break;
                        case "fontsize": if (isInt) s.FontSize = Clamp(n, 0, 4); break;
                        case "charset": if (isInt) s.Charset = Clamp(n, 0, 3); break;
                        case "paused": s.Paused = v == "1"; break;
                        case "wallpaper": s.Wallpaper = v == "1"; break;
                        case "saveronidle": s.SaverOnIdle = v == "1"; break;
                        case "idleseconds": if (isInt) s.IdleSeconds = Clamp(n, 10, 86400); break;
                        case "pauseonvideo": s.PauseOnVideo = v == "1"; break;
                        case "pausefullscreen": s.PauseWhenFullscreen = v == "1"; break;
                        case "keepawake": s.KeepAwake = v == "1"; break;
                        case "autostart": s.Autostart = v == "1"; break;
                    }
                }
                s.Loaded = true;
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("color=" + Color.ToArgb());
                sb.AppendLine("rainbow=" + B(Rainbow));
                sb.AppendLine("color2=" + Color2.ToArgb());
                sb.AppendLine("blend=" + Blend);
                sb.AppendLine("speed=" + Speed);
                sb.AppendLine("density=" + Density);
                sb.AppendLine("tail=" + Tail);
                sb.AppendLine("flicker=" + Flicker);
                sb.AppendLine("gittrigger=" + B(GitTrigger));
                sb.AppendLine("hueenabled=" + B(HueEnabled));
                sb.AppendLine("hueonbackground=" + B(HueOnBackground));
                sb.AppendLine("hueip=" + HueIp);
                sb.AppendLine("hueuser=" + HueUser);
                sb.AppendLine("huelights=" + HueLights);
                sb.AppendLine("huecolorgit=" + HueColorGit.ToArgb());
                sb.AppendLine("huecoloridle=" + HueColorIdle.ToArgb());
                sb.AppendLine("midienabled=" + B(MidiEnabled));
                sb.AppendLine("midiport=" + MidiPort);
                sb.AppendLine("midichannel=" + MidiChannel);
                sb.AppendLine("fontsize=" + FontSize);
                sb.AppendLine("charset=" + Charset);
                sb.AppendLine("paused=" + B(Paused));
                sb.AppendLine("wallpaper=" + B(Wallpaper));
                sb.AppendLine("saveronidle=" + B(SaverOnIdle));
                sb.AppendLine("idleseconds=" + IdleSeconds);
                sb.AppendLine("pauseonvideo=" + B(PauseOnVideo));
                sb.AppendLine("pausefullscreen=" + B(PauseWhenFullscreen));
                sb.AppendLine("keepawake=" + B(KeepAwake));
                sb.AppendLine("autostart=" + B(Autostart));
                System.IO.File.WriteAllText(File, sb.ToString());
            }
            catch { }
        }

        static string B(bool b) { return b ? "1" : "0"; }
        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }

    // ------------------------------------------------------------------ native
    static class Native
    {
        [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int idx, int val);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll")] public static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT ps);
        [DllImport("user32.dll")] public static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT ps);
        [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr hWnd, IntPtr rect, bool erase);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr inst, IntPtr name);
        [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr cursor);
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);
        [DllImport("kernel32.dll")] public static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool Process32First(IntPtr snap, ref PROCESSENTRY32 pe);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool Process32Next(IntPtr snap, ref PROCESSENTRY32 pe);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PROCESSENTRY32
        {
            public uint dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID, cntThreads, th32ParentProcessID; public int pcPriClassBase; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }
        [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)] public struct PAINTSTRUCT { public IntPtr hdc; public bool fErase; public int L, T, R, B; public bool fRestore, fIncUpdate; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgb; }
        [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFOHEADER { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPels, YPels; public uint ClrUsed, ClrImportant; }
        [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFO { public BITMAPINFOHEADER H; public uint Color0; }
        [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020, SWP_SHOWWINDOW = 0x0040;
        public const uint GW_HWNDLAST = 1, GW_HWNDNEXT = 2, GA_PARENT = 1;
        public const int GWL_STYLE = -16, GWL_EXSTYLE = -20, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1), HWND_TOPMOST = new IntPtr(-1);
        public const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 1, ES_DISPLAY_REQUIRED = 2;

        // Milliseconds since the last keyboard/mouse input (system-wide).
        public static uint IdleMs()
        {
            LASTINPUTINFO li = new LASTINPUTINFO(); li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref li)) return 0;
            return unchecked((uint)Environment.TickCount - li.dwTime);
        }

        // Where the wallpaper window has to live on this Windows build.
        public class DesktopLayout
        {
            public IntPtr Progman, DefView, WorkerW;
            // Win11 24H2+ "raised desktop": Progman is WS_EX_NOREDIRECTIONBITMAP, DefView is a layered child, and
            // the wallpaper must be a WS_EX_LAYERED child of Progman z-ordered under DefView and above WorkerW.
            public bool Raised;
            public IntPtr Parent { get { return Raised ? Progman : WorkerW; } }
        }

        public static DesktopLayout Probe()
        {
            DesktopLayout d = new DesktopLayout();
            d.Progman = FindWindow("Progman", null);
            if (d.Progman == IntPtr.Zero) return d;
            IntPtr r;
            // Ask Progman to spawn the WorkerW that sits behind the icons (no-op if it already exists).
            SendMessageTimeout(d.Progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0, 1000, out r);
            d.Raised = (GetWindowLong(d.Progman, GWL_EXSTYLE) & WS_EX_NOREDIRECTIONBITMAP) != 0;
            d.DefView = FindWindowEx(d.Progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (d.Raised)
            {
                d.WorkerW = FindWindowEx(d.Progman, IntPtr.Zero, "WorkerW", null);
            }
            else
            {
                // Classic layout: top-level WorkerW that follows the one hosting SHELLDLL_DefView.
                IntPtr worker = IntPtr.Zero;
                EnumWindows(delegate(IntPtr top, IntPtr lp)
                {
                    IntPtr shell = FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (shell != IntPtr.Zero) { worker = FindWindowEx(IntPtr.Zero, top, "WorkerW", null); d.DefView = shell; }
                    return true;
                }, IntPtr.Zero);
                if (worker == IntPtr.Zero) worker = FindWindowEx(d.Progman, IntPtr.Zero, "WorkerW", null);
                d.WorkerW = worker != IntPtr.Zero ? worker : d.Progman;
            }
            return d;
        }

        public static bool IsFullscreenAppActive()
        {
            int st;
            if (SHQueryUserNotificationState(out st) != 0) return false;
            // QUNS_BUSY=2, QUNS_RUNNING_D3D_FULL_SCREEN=3, QUNS_PRESENTATION_MODE=4, QUNS_APP=7
            return st == 2 || st == 3 || st == 4 || st == 7;
        }
    }

    // ------------------------------------------------------------------ audio meter ("is something playing?")
    // Reads the default render endpoint's peak level through Core Audio (IAudioMeterInformation).
    class AudioMeter : IDisposable
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }
        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioMeterInformation
        {
            [PreserveSig] int GetPeakValue(out float peak);
        }

        IAudioMeterInformation meter;
        int failures = 0;

        public float Peak()
        {
            try
            {
                if (meter == null)
                {
                    if (failures > 5) return 0f;    // give up quietly (no audio device, etc.)
                    IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                    try
                    {
                        IMMDevice dev;
                        if (en.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out dev) != 0) { failures++; return 0f; }
                        try
                        {
                            Guid iid = typeof(IAudioMeterInformation).GUID; object o;
                            if (dev.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out o) != 0) { failures++; return 0f; }
                            meter = (IAudioMeterInformation)o;
                        }
                        finally { Marshal.ReleaseComObject(dev); }
                    }
                    finally { Marshal.ReleaseComObject(en); }
                }
                float p; meter.GetPeakValue(out p); return p;
            }
            catch (Exception ex) { Dispose(); failures++; if (failures <= 2) Program.Log("audio meter: " + ex.GetType().Name + " " + ex.Message); return 0f; }   // device changed -> release + re-acquire next time
        }

        public void Dispose() { if (meter != null) { try { Marshal.ReleaseComObject(meter); } catch { } meter = null; } }
    }

    // ------------------------------------------------------------------ rain simulation + renderer
    // Owns the GDI DIB section (shared by every window that shows the rain) and the simulation.
    unsafe class RainCore : IDisposable
    {
        struct Col { public float Y; public float Speed; public int Len; public int Wait; public float Mix; }

        static readonly string KATA = "ｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜﾝ0123456789Z:・\"=*+<>¦|ç";
        static readonly string BIN = "01";
        static readonly string HEX = "0123456789ABCDEF";
        static readonly string LATIN = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789@#$%&*<>";

        readonly Random rnd = new Random();
        readonly Settings s;
        Col[] cols = new Col[0];
        int cellW, cellH, rows;
        Font font;
        float hue = 120f;
        float blendPhase = 0f;
        static Color Lerp(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
        readonly byte[] lut = new byte[256];
        float lutKeep = -1f; float lutSpeed = -1f;

        // MIDI overrides: written on the UI thread each tick before Step(); -1 = none.
        public float MidiSpeedMul = -1f;   // 0.35..2.3, replaces the Speed preset
        public float MidiDensity = -1f;    // 0..1, starves or floods respawns live
        public float MidiHue = -1f;        // 0..360, replaces colour AND rainbow
        public float MidiBlendT = -1f;     // 0..1, pins the blend position for all streaks
        public float MidiTail = -1f;       // 0..1, retention between Short and Endless
        public float MidiDim = -1f;        // 0..1 master brightness
        public float BeatPulse = 0f;       // set to 1 on a MIDI clock beat, decays per frame

        // Spawn n drops near the top on random columns (MIDI note bursts). UI thread only.
        public void Burst(int n)
        {
            for (int j = 0; j < n && cols.Length > 0; j++)
            {
                int i = rnd.Next(cols.Length);
                cols[i].Wait = 0;
                cols[i].Y = -rnd.Next(0, 4);
                cols[i].Speed = 0.5f + (float)rnd.NextDouble() * 0.9f;
            }
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public IntPtr DC { get { return dibDC; } }
        Bitmap back;
        IntPtr dibDC = IntPtr.Zero, dib = IntPtr.Zero, dibOld = IntPtr.Zero, dibBits = IntPtr.Zero;

        public RainCore(int w, int h, Settings settings) { s = settings; Resize(w, h); }

        public void Resize(int w, int h)
        {
            DestroyBuffer();
            Width = w; Height = h;
            Native.BITMAPINFO bmi = new Native.BITMAPINFO();
            bmi.H.Size = (uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
            bmi.H.Width = w; bmi.H.Height = -h; bmi.H.Planes = 1; bmi.H.BitCount = 32;
            dibDC = Native.CreateCompatibleDC(IntPtr.Zero);
            if (dibDC != IntPtr.Zero) dib = Native.CreateDIBSection(IntPtr.Zero, ref bmi, 0, out dibBits, IntPtr.Zero, 0);
            if (dibDC == IntPtr.Zero || dib == IntPtr.Zero || dibBits == IntPtr.Zero)
            {
                DestroyBuffer();
                throw new InvalidOperationException("Could not allocate a " + w + "x" + h + " GDI backbuffer.");
            }
            dibOld = Native.SelectObject(dibDC, dib);
            back = new Bitmap(w, h, w * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, dibBits);
            Reconfigure();
        }

        void DestroyBuffer()
        {
            if (back != null) { back.Dispose(); back = null; }
            if (dibDC != IntPtr.Zero) { if (dibOld != IntPtr.Zero) Native.SelectObject(dibDC, dibOld); Native.DeleteDC(dibDC); }
            if (dib != IntPtr.Zero) Native.DeleteObject(dib);
            dibDC = dib = dibOld = dibBits = IntPtr.Zero;
        }

        public void Reconfigure()
        {
            int[] sizes = { 10, 13, 17, 22, 30 };
            int px = sizes[s.FontSize];
            if (font != null) font.Dispose();
            font = new Font(s.Charset == 0 ? "MS Gothic" : "Consolas", px, FontStyle.Bold, GraphicsUnit.Pixel);
            cellW = (int)(px * 0.95f); cellH = (int)(px * 1.05f);
            rows = Height / cellH + 2;
            nCols = Width / cellW + 1;
            cols = new Col[nCols * LANES];            // LANES drops can share a column (denser downpours)
            for (int i = 0; i < cols.Length; i++) Reset(ref cols[i], true);
            Clear();
        }

        const int LANES = 2;
        int nCols = 1;

        // A neighbouring column already has a fresh drop near the top -> spawn later so streaks spread out.
        bool CrowdedNear(int i)
        {
            int c = i % nCols;
            for (int lane = 0; lane < LANES; lane++)
                for (int dc = -2; dc <= 2; dc++)
                {
                    int cc = c + dc; if (cc < 0 || cc >= nCols) continue;
                    int j = lane * nCols + cc; if (j == i) continue;
                    if (cols[j].Wait == 0 && cols[j].Y > -5 && cols[j].Y < 12) return true;
                }
            return false;
        }

        public void Clear()
        {
            if (back == null) return;
            using (Graphics g = Graphics.FromImage(back)) g.Clear(Color.Black);
        }

        void Reset(ref Col c, bool initial) { Reset(ref c, initial, -1); }
        void Reset(ref Col c, bool initial, int index)
        {
            c.Y = initial ? rnd.Next(-rows, rows) : -rnd.Next(0, 20);
            c.Speed = 0.3f + (float)rnd.NextDouble() * 0.9f;
            c.Mix = (float)rnd.NextDouble();        // this streak's position between colour A and B (mixed blend modes)
            int[] lenMin = { 4, 8, 16, 28, 45, 70 }, lenMax = { 12, 24, 45, 75, 120, 200 };
            c.Len = rnd.Next(lenMin[s.Tail], lenMax[s.Tail]);
            // density = how long a drop idles before respawning (frames). Lane 0 carries the base density,
            // lane 1 only really comes alive at Heavy/Downpour.
            int[] idleMax = { 220, 120, 70, 30, 8 };
            int[] lane1Mul = { 6, 5, 4, 2, 1 };
            int lane = index < 0 ? 0 : index / nCols;
            int max = idleMax[s.Density] * (lane == 0 ? 1 : lane1Mul[s.Density]);
            c.Wait = rnd.Next(max / 4, max + 1);
            if (index >= 0 && !initial && CrowdedNear(index)) c.Wait += rnd.Next(15, 45);   // spread streaks out
        }

        string Chars()
        {
            switch (s.Charset) { case 1: return BIN; case 2: return HEX; case 3: return LATIN; default: return KATA; }
        }

        // A MIDI hue CC pins the colour, beating both the preset and rainbow, and the Hue
        // lights read the same effective base so bulbs and rain always agree.
        public Color CurrentColor() { if (MidiHue >= 0f) return FromHsv(MidiHue, 1f, 1f); return s.Rainbow ? FromHsv(hue, 1f, 1f) : s.Color; }

        // Colour the rain would use for "slot" i of n (used to give each Hue bulb its own place in a blend).
        public Color ColourFor(int i, int n)
        {
            // A MIDI hue replaces colour A but keeps its place in the blend, so bulbs
            // still spread across the ramp exactly like the rain does.
            if (MidiHue < 0f && s.Rainbow) return FromHsv(hue + (n > 1 ? 360f * i / n : 0f), 1f, 1f);
            Color baseA = MidiHue >= 0f ? FromHsv(MidiHue, 1f, 1f) : s.Color;
            if (s.Blend == 0) return baseA;
            float t;
            switch (s.Blend)
            {
                case 1: t = (float)(Math.Sin(blendPhase) * 0.5 + 0.5); break;
                case 2: case 3: t = n > 1 ? (float)i / (n - 1) : 0f; break;
                case 4: t = (float)new Random(i * 7919 + 13).NextDouble(); break;
                default: { float ph = (float)(((i / (float)Math.Max(1, n)) + blendPhase / (2 * Math.PI)) % 1.0); t = ph < 0.5f ? ph * 2f : 2f - ph * 2f; break; }
            }
            return Lerp(baseA, s.Color2, t);
        }

        static Color FromHsv(float h, float sat, float v)
        {
            h = (h % 360f + 360f) % 360f;
            float c = v * sat, x = c * (1 - Math.Abs((h / 60f) % 2 - 1)), m = v - c;
            float r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }

        // Multiplicative fade with a hard floor so trails really end at pure black
        // (GDI+ alpha fills round up and leave ghost outlines at ~12/255).
        void Fade(float sp, float tailScale)
        {
            float[] keep = { 0.80f, 0.88f, 0.93f, 0.955f, 0.972f, 0.985f };   // per-frame retention per tail setting
            // MIDI tail rides the same retention range; the LUT cache is keyed on the
            // EFFECTIVE retention so a live CC sweep rebuilds it (preset alone would not).
            float baseKeep = tailScale >= 0f ? 0.80f + (0.985f - 0.80f) * tailScale : keep[s.Tail];
            if (lutKeep != baseKeep || lutSpeed != sp)
            {
                float k = 1f - (1f - baseKeep) * (0.6f + 0.4f * sp);
                for (int v = 0; v < 256; v++)
                {
                    int nv = (int)(v * k);
                    if (nv == v && v > 0) nv = v - 1;        // always make progress
                    lut[v] = (byte)(nv < 5 ? 0 : nv);        // floor -> true black
                }
                lutKeep = baseKeep; lutSpeed = sp;
            }
            byte* basePtr = (byte*)dibBits; int stride = Width * 4; int w = Width;
            byte[] l = lut;
            Parallel.For(0, Height, delegate(int y)
            {
                byte* p = basePtr + y * stride;
                for (int x = 0; x < w; x++, p += 4) { p[0] = l[p[0]]; p[1] = l[p[1]]; p[2] = l[p[2]]; p[3] = 255; }
            });
        }

        // Advances the simulation one frame and paints onto the persistent backbuffer.
        public void Step()
        {
            if (back == null) return;
            // Snapshot the MIDI overrides once per frame so a driver-thread write mid-frame
            // can never produce an inconsistent palette/fade/speed combination.
            float mSpeed = MidiSpeedMul, mDensity = MidiDensity, mHue = MidiHue,
                  mBlendT = MidiBlendT, mTail = MidiTail, mDim = MidiDim, mBeat = BeatPulse;
            BeatPulse *= 0.80f; if (BeatPulse < 0.02f) BeatPulse = 0f;   // ~250 ms decay

            float[] speedMul = { 0.35f, 0.6f, 1f, 1.5f, 2.3f };
            float sp = mSpeed > 0f ? mSpeed : speedMul[s.Speed];
            if (s.Rainbow) hue += 0.4f * sp;
            Fade(sp, mTail);

            // Colour palette for this frame: a ramp of STEPS colours between colour A and B (blend modes),
            // or a single colour. Each drop picks its ramp index from time / x / y.
            const int STEPS = 24;
            // A hue CC replaces colour A only; the two-colour blend (and CC 23) stay live.
            Color a = mHue >= 0f ? FromHsv(mHue, 1f, 1f) : CurrentColor(), b = s.Color2;
            blendPhase += 0.004f * sp;
            bool blend = s.Blend != 0 && (!s.Rainbow || mHue >= 0f);   // a hue CC also overrides rainbow
            int steps = blend ? STEPS : 1;
            // Master brightness: dim CC scales down, a clock beat pushes up briefly.
            float lum = (mDim >= 0f ? 0.15f + 0.85f * mDim : 1f) * (1f + 0.35f * mBeat);
            int headAdd = (int)(180 * Math.Min(1f, lum));   // heads must dim with the master too
            SolidBrush[] bHeads = new SolidBrush[steps], bBodies = new SolidBrush[steps], bDims = new SolidBrush[steps];
            try
            {
            for (int k = 0; k < steps; k++)
            {
                float t = steps == 1 ? 0f : (float)k / (steps - 1);
                Color c = Lerp(a, b, t);
                if (lum != 1f)
                    c = Color.FromArgb(Math.Min(255, (int)(c.R * lum)), Math.Min(255, (int)(c.G * lum)), Math.Min(255, (int)(c.B * lum)));
                bBodies[k] = new SolidBrush(c);
                bHeads[k] = new SolidBrush(Color.FromArgb(Math.Min(255, c.R + headAdd), Math.Min(255, c.G + headAdd), Math.Min(255, c.B + headAdd)));
                bDims[k] = new SolidBrush(Color.FromArgb(150, c));
            }
            float timeT = (float)(Math.Sin(blendPhase) * 0.5 + 0.5);
            string chars = Chars();
            using (Graphics g = Graphics.FromImage(back))
            using (SolidBrush bk = new SolidBrush(Color.Black))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i].Wait > 0)
                    {
                        // Density CC reshapes respawn pacing live: high floods (waits burn
                        // faster), low starves (waits sometimes don't tick down at all).
                        int dec = 1;
                        if (mDensity >= 0f)
                        {
                            if (mDensity >= 0.5f) dec = 1 + (int)((mDensity - 0.5f) * 10f);          // 1..6
                            else if (rnd.NextDouble() < (0.5f - mDensity) * 1.6f) dec = 0;           // sparse
                        }
                        cols[i].Wait -= dec; if (cols[i].Wait < 0) cols[i].Wait = 0;
                        continue;
                    }
                    cols[i].Y += cols[i].Speed * sp;
                    int row = (int)cols[i].Y;
                    if (row - cols[i].Len > rows) { Reset(ref cols[i], false, i); continue; }

                    float x = (i % nCols) * cellW;
                    int k = 0;
                    if (mBlendT >= 0f && steps > 1) k = (int)(mBlendT * (STEPS - 1) + 0.5f);
                    else if (blend)
                    {
                        float t;
                        switch (s.Blend)
                        {
                            case 1: t = timeT; break;                                                            // fade A<->B over time
                            case 2: t = (float)(i % nCols) / Math.Max(1, nCols - 1); break;                      // side by side, left -> right
                            case 3: t = Math.Max(0f, Math.Min(1f, (float)row / Math.Max(1, rows - 1))); break;  // top -> bottom
                            case 4: t = cols[i].Mix; break;                                                      // mixed: each streak its own colour
                            default:                                                                             // mixed + fading: each streak drifts, out of phase
                            {
                                float ph = (float)((cols[i].Mix + blendPhase / (2 * Math.PI)) % 1.0);
                                t = ph < 0.5f ? ph * 2f : 2f - ph * 2f;                                          // triangle wave 0..1..0
                                break;
                            }
                        }
                        k = (int)(t * (STEPS - 1) + 0.5f);
                    }
                    SolidBrush bHead = bHeads[k], bBody = bBodies[k], bDim = bDims[k];
                    if (row >= 0 && row < rows)                          // bright head
                        g.DrawString(chars[rnd.Next(chars.Length)].ToString(), font, bHead, x, row * cellH);
                    int prev = row - 1;
                    if (prev >= 0 && prev < rows)                        // glyph behind head -> body colour
                    {
                        g.FillRectangle(bk, x, prev * cellH, cellW, cellH);
                        g.DrawString(chars[rnd.Next(chars.Length)].ToString(), font, bBody, x, prev * cellH);
                    }
                    int[] flickerOdds = { 0, 60, 14, 4 };                // Off / Subtle / Normal / Lots
                    if (flickerOdds[s.Flicker] > 0 && rnd.Next(flickerOdds[s.Flicker]) == 0)   // glyph blink inside the tail
                    {
                        int t = row - rnd.Next(2, Math.Max(3, Math.Min(cols[i].Len, 14)));
                        if (t >= 0 && t < rows)
                        {
                            g.FillRectangle(bk, x, t * cellH, cellW, cellH);
                            g.DrawString(chars[rnd.Next(chars.Length)].ToString(), font, bDim, x, t * cellH);
                        }
                    }
                }
            }
            }
            finally
            {
                // A tick exception is swallowed upstream; without this the frame's brushes leak GDI handles.
                for (int k = 0; k < steps; k++)
                {
                    if (bHeads[k] != null) bHeads[k].Dispose();
                    if (bBodies[k] != null) bBodies[k].Dispose();
                    if (bDims[k] != null) bDims[k].Dispose();
                }
            }
        }

        public void Dispose()
        {
            if (font != null) { font.Dispose(); font = null; }
            DestroyBuffer();
        }
    }

    // ------------------------------------------------------------------ windows that show the rain
    // Raw Win32 windows (no WinForms Form: Form.Show()/UpdateStyles kept resetting the styles and parent we need).
    abstract class RainWindow : NativeWindow
    {
        protected readonly RainCore core;
        protected RainWindow(RainCore c) { core = c; }

        public void Present() { if (Handle != IntPtr.Zero) Native.InvalidateRect(Handle, IntPtr.Zero, false); }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x000F: // WM_PAINT
                {
                    Native.PAINTSTRUCT ps;
                    IntPtr hdc = Native.BeginPaint(m.HWnd, out ps);
                    try { if (hdc != IntPtr.Zero && core.DC != IntPtr.Zero) Native.BitBlt(hdc, 0, 0, core.Width, core.Height, core.DC, 0, 0, 0x00CC0020 /*SRCCOPY*/); }
                    finally { Native.EndPaint(m.HWnd, ref ps); }
                    m.Result = IntPtr.Zero;
                    return;
                }
                case 0x0014: // WM_ERASEBKGND
                    m.Result = new IntPtr(1);
                    return;
                case 0x0020: // WM_SETCURSOR - our window class has no cursor, so set the arrow or the pointer vanishes
                    Native.SetCursor(Native.LoadCursor(IntPtr.Zero, new IntPtr(32512) /*IDC_ARROW*/));
                    m.Result = new IntPtr(1);
                    return;
            }
            base.WndProc(ref m);
        }

        public virtual void Destroy() { if (Handle != IntPtr.Zero) DestroyHandle(); }
    }

    // Behind the desktop icons.
    class WallpaperWindow : RainWindow
    {
        Native.DesktopLayout layout;
        IntPtr host = IntPtr.Zero;

        public WallpaperWindow(RainCore c) : base(c) { }

        public void Create()
        {
            layout = Native.Probe(); host = layout.Parent;
            Program.Log("layout raised=" + layout.Raised + " progman=" + layout.Progman + " defview=" + layout.DefView + " workerw=" + layout.WorkerW);
            Rectangle vs = SystemInformation.VirtualScreen;
            CreateParams cp = new CreateParams();
            cp.Caption = "MatrixBG";
            cp.X = vs.X; cp.Y = vs.Y; cp.Width = vs.Width; cp.Height = vs.Height;
            // Created as a hidden popup; Attach() converts it into a child of the desktop host.
            cp.Style = unchecked((int)0x80000000) /*WS_POPUP*/ | 0x04000000 /*WS_CLIPSIBLINGS*/;
            cp.ExStyle = 0x80 /*WS_EX_TOOLWINDOW*/;
            CreateHandle(cp);
            Program.Log("wallpaper handle created " + Handle);
            Attach();
        }

        public void Attach()
        {
            if (Handle == IntPtr.Zero) return;
            if (layout == null || host == IntPtr.Zero || !Native.IsWindow(host)) { layout = Native.Probe(); host = layout.Parent; }
            Program.Log("attach raised=" + layout.Raised + " host=" + host + " defview=" + layout.DefView + " workerw=" + layout.WorkerW);
            if (host == IntPtr.Zero) return;

            // Styles Windows 11 24H2+/25H2 actually composites for a foreign child of Progman (the same set Lively uses):
            // WS_CHILD|WS_VISIBLE|WS_CLIPSIBLINGS|WS_CLIPCHILDREN|WS_TABSTOP and
            // WS_EX_LAYERED|WS_EX_TOOLWINDOW|WS_EX_CONTROLPARENT|WS_EX_NOACTIVATE, made opaque via SetLayeredWindowAttributes.
            Native.SetWindowLong(Handle, Native.GWL_STYLE, unchecked((int)0x56010000));
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, layout.Raised ? unchecked((int)0x08090080) : 0x80);
            if (layout.Raised) Native.SetLayeredWindowAttributes(Handle, 0, 255, 2 /*LWA_ALPHA*/);
            if (Native.GetAncestor(Handle, Native.GA_PARENT) != host) Native.SetParent(Handle, host);

            Rectangle vs = SystemInformation.VirtualScreen;
            IntPtr after = layout.Raised && layout.DefView != IntPtr.Zero ? layout.DefView : IntPtr.Zero;
            uint zflag = after == IntPtr.Zero ? Native.SWP_NOZORDER : 0;
            // Size nudge: DWM only picks the window up after a size change (Lulu6432 write-up on build 26120+).
            Native.SetWindowPos(Handle, after, 0, 0, vs.Width - 1, vs.Height - 1, zflag | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_FRAMECHANGED);
            Native.SetWindowPos(Handle, after, 0, 0, vs.Width, vs.Height, zflag | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_FRAMECHANGED);
            EnsureZOrder();
            Program.Log("attached parent=" + Native.GetAncestor(Handle, Native.GA_PARENT) + " style=0x" + Native.GetWindowLong(Handle, Native.GWL_STYLE).ToString("X") + " ex=0x" + Native.GetWindowLong(Handle, Native.GWL_EXSTYLE).ToString("X"));
            Present();
        }

        void EnsureZOrder()
        {
            if (layout == null || !layout.Raised) return;
            if (layout.DefView != IntPtr.Zero && Native.GetWindow(layout.DefView, Native.GW_HWNDNEXT) != Handle)
                Native.SetWindowPos(Handle, layout.DefView, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            if (layout.WorkerW != IntPtr.Zero && Native.GetWindow(layout.WorkerW, Native.GW_HWNDLAST) != layout.WorkerW)
                Native.SetWindowPos(layout.WorkerW, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // Called every few seconds: re-create after an Explorer restart, re-attach if the desktop changed.
        public void Watch()
        {
            if (Handle == IntPtr.Zero)
            {
                Program.Log("wallpaper handle lost; recreating");
                layout = null; host = IntPtr.Zero;
                try { Create(); core.Clear(); } catch (Exception ex) { Program.Log("recreate failed: " + ex.Message); }
                return;
            }
            IntPtr p = Native.GetAncestor(Handle, Native.GA_PARENT);
            bool stale = layout != null && layout.Raised &&
                ((layout.DefView != IntPtr.Zero && !Native.IsWindow(layout.DefView)) || (layout.WorkerW != IntPtr.Zero && !Native.IsWindow(layout.WorkerW)));
            if (p == IntPtr.Zero || !Native.IsWindow(p) || p != host || stale) { Program.Log("reattach: parent=" + p + " host=" + host + " stale=" + stale); host = IntPtr.Zero; layout = null; Attach(); }
            else EnsureZOrder();
        }

        public void Reattach() { host = IntPtr.Zero; layout = null; Attach(); }

        protected override void OnHandleChange()
        {
            base.OnHandleChange();
            if (Handle == IntPtr.Zero) Program.Log("wallpaper handle destroyed");
        }
    }

    // Fullscreen topmost screensaver (created once, shown/hidden).
    class SaverWindow : RainWindow
    {
        public bool Visible { get; private set; }
        public SaverWindow(RainCore c) : base(c) { }

        void EnsureCreated()
        {
            if (Handle != IntPtr.Zero) return;
            Rectangle vs = SystemInformation.VirtualScreen;
            CreateParams cp = new CreateParams();
            cp.Caption = "MatrixBG Screensaver";
            cp.X = vs.X; cp.Y = vs.Y; cp.Width = vs.Width; cp.Height = vs.Height;
            cp.Style = unchecked((int)0x80000000) /*WS_POPUP*/ | 0x04000000 /*WS_CLIPSIBLINGS*/;
            cp.ExStyle = 0x80 /*WS_EX_TOOLWINDOW*/ | 0x8 /*WS_EX_TOPMOST*/;
            CreateHandle(cp);
        }

        public void Show()
        {
            EnsureCreated();
            Rectangle vs = SystemInformation.VirtualScreen;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, vs.X, vs.Y, vs.Width, vs.Height, Native.SWP_SHOWWINDOW);
            Visible = true;
            Present();
        }

        public void Hide()
        {
            if (Handle != IntPtr.Zero) Native.ShowWindow(Handle, 0 /*SW_HIDE*/);
            Visible = false;
        }

        public override void Destroy() { Visible = false; base.Destroy(); }
    }

    // Plain test window (--window).
    class TestWindow : RainWindow
    {
        public event EventHandler Closed;
        public TestWindow(RainCore c) : base(c)
        {
            CreateParams cp = new CreateParams();
            cp.Caption = "MatrixBG (window mode)"; cp.X = 100; cp.Y = 100; cp.Width = c.Width; cp.Height = c.Height;
            cp.Style = unchecked((int)0x10CF0000); // WS_OVERLAPPEDWINDOW | WS_VISIBLE
            CreateHandle(cp);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0010 /*WM_CLOSE*/) { if (Closed != null) Closed(this, EventArgs.Empty); return; }
            base.WndProc(ref m);
        }
    }

    // ------------------------------------------------------------------ Philips Hue (local bridge API v1, no cloud)
    // Captures the chosen lights, runs a "matrix" effect (rain colour, slow pulse, random flickers) while active,
    // restores the captured state when stopped. All HTTP runs on a worker thread.
    class Hue : IDisposable
    {
        public class Light { public string Id, Name; public bool On, Colour; public int Bri; public double X, Y; public int Ct; public string Mode; }

        readonly Settings s;
        readonly System.Web.Script.Serialization.JavaScriptSerializer json = new System.Web.Script.Serialization.JavaScriptSerializer();
        readonly Random rnd = new Random();
        readonly object gate = new object();
        List<Light> active = new List<Light>();
        Thread worker;
        volatile bool running = false;
        volatile int gen = 0;   // session stamp; written only on the UI thread
        Func<int, int, Color> colourSource;   // (bulb index, bulb count) -> colour

        public Hue(Settings settings) { s = settings; }
        public bool Paired { get { return s.HueIp.Length > 0 && s.HueUser.Length > 0; } }
        public bool Active { get { return running; } }
        public string LastError = "";

        string Url(string path) { return "http://" + s.HueIp + "/api/" + s.HueUser + path; }

        string Http(string method, string url, string body)
        {
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            req.Method = method; req.Timeout = 4000; req.Proxy = null;
            if (body != null)
            {
                byte[] data = Encoding.UTF8.GetBytes(body);
                req.ContentType = "application/json"; req.ContentLength = data.Length;
                using (Stream st = req.GetRequestStream()) st.Write(data, 0, data.Length);
            }
            using (System.Net.WebResponse resp = req.GetResponse())
            using (StreamReader rd = new StreamReader(resp.GetResponseStream()))
                return rd.ReadToEnd();
        }

        // Pair: POST /api with devicetype. Succeeds only while the bridge's link button was pressed (< 30 s ago).
        public bool TryPair(string ip, out string message)
        {
            try
            {
                string r = Http("POST", "http://" + ip + "/api", "{\"devicetype\":\"matrixbg#" + Environment.MachineName + "\"}");
                object[] arr = json.Deserialize<object[]>(r);
                Dictionary<string, object> first = arr.Length > 0 ? arr[0] as Dictionary<string, object> : null;
                if (first != null && first.ContainsKey("success"))
                {
                    Dictionary<string, object> ok = (Dictionary<string, object>)first["success"];
                    s.HueIp = ip; s.HueUser = (string)ok["username"]; s.Save();
                    message = "Paired with the bridge at " + ip + ".";
                    return true;
                }
                if (first != null && first.ContainsKey("error"))
                {
                    Dictionary<string, object> err = (Dictionary<string, object>)first["error"];
                    message = err.ContainsKey("description") ? (string)err["description"] : "bridge error";
                    return false;
                }
                message = "Unexpected reply: " + r; return false;
            }
            catch (Exception ex) { message = ex.Message; return false; }
        }

        public static string Discover()
        {
            try
            {
                System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("https://discovery.meethue.com/");
                req.Timeout = 6000;
                using (System.Net.WebResponse resp = req.GetResponse())
                using (StreamReader rd = new StreamReader(resp.GetResponseStream()))
                {
                    string r = rd.ReadToEnd();
                    System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(r, "\"internalipaddress\"\\s*:\\s*\"([^\"]+)\"");
                    return m.Success ? m.Groups[1].Value : "";
                }
            }
            catch { return ""; }
        }

        public List<Light> GetLights()
        {
            List<Light> list = new List<Light>();
            if (!Paired) return list;
            try
            {
                Dictionary<string, object> all = json.Deserialize<Dictionary<string, object>>(Http("GET", Url("/lights"), null));
                foreach (KeyValuePair<string, object> kv in all)
                {
                    // One malformed entry must not abort the enumeration (that would break
                    // capture/restore for every light after it), and ids go straight into the
                    // request path, so only accept the numeric ids the v1 API actually uses.
                    try
                    {
                        if (!System.Text.RegularExpressions.Regex.IsMatch(kv.Key, "^[0-9]+$")) continue;
                        Dictionary<string, object> l = kv.Value as Dictionary<string, object>;
                        if (l == null || !l.ContainsKey("state")) continue;
                        Dictionary<string, object> st = l["state"] as Dictionary<string, object>;
                        if (st == null) continue;
                        Light L = new Light(); L.Id = kv.Key; L.Name = l.ContainsKey("name") ? Convert.ToString(l["name"]) : kv.Key;
                        L.On = st.ContainsKey("on") && (bool)st["on"];
                        L.Bri = st.ContainsKey("bri") ? Convert.ToInt32(st["bri"]) : 254;
                        L.Mode = st.ContainsKey("colormode") ? Convert.ToString(st["colormode"]) : "";
                        L.Colour = st.ContainsKey("xy");
                        if (L.Colour) { System.Collections.IList xy = (System.Collections.IList)st["xy"]; L.X = Convert.ToDouble(xy[0]); L.Y = Convert.ToDouble(xy[1]); }
                        if (st.ContainsKey("ct")) L.Ct = Convert.ToInt32(st["ct"]);
                        list.Add(L);
                    }
                    catch (Exception ex) { Program.Log("hue: skipped malformed light entry: " + ex.Message); }
                }
                LastError = "";
            }
            catch (Exception ex) { LastError = ex.Message; }
            return list;
        }

        // RGB -> CIE xy (Philips' reference conversion).
        public static void ToXY(Color c, out double x, out double y)
        {
            double r = Gamma(c.R / 255.0), g = Gamma(c.G / 255.0), b = Gamma(c.B / 255.0);
            double X = r * 0.4124 + g * 0.3576 + b * 0.1805, Y = r * 0.2126 + g * 0.7152 + b * 0.0722, Z = r * 0.0193 + g * 0.1192 + b * 0.9505;
            double sum = X + Y + Z;
            if (sum <= 0) { x = 0.3127; y = 0.3290; return; }
            x = X / sum; y = Y / sum;
        }
        static double Gamma(double v) { return v > 0.04045 ? Math.Pow((v + 0.055) / 1.055, 2.4) : v / 12.92; }

        public void Start(Func<int, int, Color> colour)
        {
            if (!s.HueEnabled || !Paired) return;
            if (running) { colourSource = colour; return; }   // already running: just switch colour live
            // A previous worker may still be restoring bulb state; let it finish before the
            // new session captures "previous state", or it captures mid-restore effect colours.
            // If it is genuinely stuck (bridge timing out per light), do NOT start a rival
            // session: the old worker still owns the lights and restores when it unsticks.
            // Sessions are generation-stamped so a zombie can never resume its effect loop
            // after a newer session exists. gen is only written on the UI thread.
            if (worker != null && worker.IsAlive) { worker.Join(3000); if (worker.IsAlive) return; }
            colourSource = colour;
            running = true;
            gen++;
            int myGen = gen;
            worker = new Thread(delegate() { Run(myGen); }); worker.IsBackground = true; worker.Start();
        }

        public void Stop()
        {
            running = false;
        }

        void Run(int myGen)
        {
            List<Light> lights;
            try
            {
                lights = GetLights();
                HashSet<string> chosen = new HashSet<string>(s.HueLights.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                lights.RemoveAll(delegate(Light l) { return !l.Colour || (chosen.Count > 0 && !chosen.Contains(l.Id)); });
                lock (gate) active = lights;
                Program.Log("hue: effect on " + lights.Count + " lights");
                double phase = 0;
                while (running && gen == myGen)
                {
                    phase += 0.35;
                    int idx = 0;
                    foreach (Light l in lights)
                    {
                        if (!running || gen != myGen) break;
                        Color c = colourSource(idx++, lights.Count);
                        double x, y; ToXY(c, out x, out y);
                        int bri = (int)(150 + 100 * Math.Sin(phase + l.Id.GetHashCode() % 7));
                        bool flick = rnd.Next(6) == 0;
                        if (flick) bri = rnd.Next(20, 80);
                        string body = "{\"on\":true,\"xy\":[" + x.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "," + y.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "],\"bri\":" + Math.Max(1, Math.Min(254, bri)) + ",\"transitiontime\":" + (flick ? 0 : 4) + "}";
                        try { Http("PUT", Url("/lights/" + l.Id + "/state"), body); } catch (Exception ex) { LastError = ex.Message; }
                    }
                    Thread.Sleep(500);   // ~2 updates/s per light keeps well under the bridge's rate limit
                }
            }
            catch (Exception ex) { LastError = ex.Message; Program.Log("hue: " + ex.Message); lights = active; }
            // restore
            foreach (Light l in lights)
            {
                string body = l.On
                    ? "{\"on\":true,\"bri\":" + l.Bri + (l.Mode == "ct" && l.Ct > 0 ? ",\"ct\":" + l.Ct : ",\"xy\":[" + l.X.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "," + l.Y.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "]") + ",\"transitiontime\":10}"
                    : "{\"on\":false,\"transitiontime\":10}";
                try { Http("PUT", Url("/lights/" + l.Id + "/state"), body); } catch (Exception ex) { LastError = ex.Message; }
            }
            Program.Log("hue: restored " + lights.Count + " lights");
        }

        public void Dispose() { running = false; gen++; if (worker != null && worker.IsAlive) worker.Join(10000); }   // long join: quitting must not strand the lights mid-effect
    }

    // ------------------------------------------------------------------ MIDI control (winmm)
    // An APC / Traktor (optionally through a loopMIDI virtual port) drives the rain live.
    // The winmm callback runs on a DRIVER thread and only parses short messages into
    // primitive fields; the 33 ms UI tick consumes them. Events (note bursts, overlay
    // toggle, beats) are Interlocked counters so hits between ticks are never lost.
    // Default map (README): CC 20 speed, 21 density, 22 hue, 23 blend position, 24 tail,
    // 25 brightness; any Note On = drop burst (velocity-scaled); note 36 (C1) toggles the
    // overlay; MIDI clock 0xF8 pulses the field every 24 ticks (Start/Stop reset phase).
    class Midi : IDisposable
    {
        [DllImport("winmm.dll")] static extern uint midiInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern uint midiInGetDevCaps(IntPtr id, ref MIDIINCAPS caps, uint size);
        [DllImport("winmm.dll")] static extern uint midiInOpen(out IntPtr h, uint id, MidiInProc cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] static extern uint midiInStart(IntPtr h);
        [DllImport("winmm.dll")] static extern uint midiInStop(IntPtr h);
        [DllImport("winmm.dll")] static extern uint midiInReset(IntPtr h);
        [DllImport("winmm.dll")] static extern uint midiInClose(IntPtr h);
        delegate void MidiInProc(IntPtr h, uint msg, IntPtr inst, IntPtr p1, IntPtr p2);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MIDIINCAPS
        {
            public ushort Mid, Pid; public uint DriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
            public uint Support;
        }
        const uint CALLBACK_FUNCTION = 0x30000;
        const uint MIM_DATA = 0x3C3, MIM_MOREDATA = 0x3CC;

        readonly Settings s;
        readonly MidiInProc proc;   // held so the GC never collects the native callback
        IntPtr handle = IntPtr.Zero;
        volatile bool closing;
        public string OpenPortName = "";
        public string LastError = "";

        // Last-value CC overrides (-1 = untouched since enable); single writer (driver thread)
        public volatile int CcSpeed = -1, CcDensity = -1, CcHue = -1, CcBlend = -1, CcTail = -1, CcDim = -1;
        public volatile int ActivitySeq = 0;   // bumps on every accepted message (tray indicator)
        int burstPending, overlayToggles, beatSeq;
        volatile int clockTicks = -1;
        int lastBeatSeen;   // UI thread only

        public Midi(Settings settings) { s = settings; proc = InProc; }

        public string[] Ports()
        {
            uint n = midiInGetNumDevs();
            string[] names = new string[n];
            for (uint i = 0; i < n; i++)
            {
                MIDIINCAPS caps = new MIDIINCAPS();
                names[i] = midiInGetDevCaps(new IntPtr(i), ref caps, (uint)Marshal.SizeOf(typeof(MIDIINCAPS))) == 0 ? caps.Name : ("(device " + i + ")");
            }
            return names;
        }

        public bool Open(string portName)
        {
            Close();
            string[] ports = Ports();
            int idx = -1;
            for (int i = 0; i < ports.Length; i++) if (ports[i] == portName) { idx = i; break; }
            if (idx < 0) { LastError = "port not found: " + portName; return false; }
            IntPtr h;
            uint rc = midiInOpen(out h, (uint)idx, proc, IntPtr.Zero, CALLBACK_FUNCTION);
            if (rc != 0) { LastError = "midiInOpen failed (" + rc + ")"; return false; }
            handle = h;
            midiInStart(handle);
            OpenPortName = portName;
            LastError = "";
            Program.Log("midi: listening on " + portName);
            return true;
        }

        public void Close()
        {
            if (handle == IntPtr.Zero) return;
            closing = true;             // callback becomes a no-op before teardown
            midiInStop(handle);
            midiInReset(handle);
            midiInClose(handle);
            handle = IntPtr.Zero;
            OpenPortName = "";
            closing = false;
            ResetOverrides();
        }

        public void ResetOverrides()
        {
            CcSpeed = CcDensity = CcHue = CcBlend = CcTail = CcDim = -1;
            clockTicks = -1;
            Interlocked.Exchange(ref burstPending, 0);
            Interlocked.Exchange(ref overlayToggles, 0);
            lastBeatSeen = Thread.VolatileRead(ref beatSeq);   // no phantom beat on re-enable
        }

        // UI-tick consumers
        public int TakeBursts() { return Interlocked.Exchange(ref burstPending, 0); }
        public int TakeOverlayToggles() { return Interlocked.Exchange(ref overlayToggles, 0); }
        public bool TakeBeat() { int b = Thread.VolatileRead(ref beatSeq); if (b != lastBeatSeen) { lastBeatSeen = b; return true; } return false; }

        void InProc(IntPtr h, uint msg, IntPtr inst, IntPtr p1, IntPtr p2)
        {
            // Driver thread: no allocation, no locks, no logging, no UI. We never queue
            // sysex buffers, so MIM_LONGDATA needs no cleanup and is simply ignored.
            if (closing) return;
            if (msg != MIM_DATA && msg != MIM_MOREDATA) return;
            int packed = unchecked((int)(p1.ToInt64() & 0xFFFFFFFF));
            int status = packed & 0xFF, d1 = (packed >> 8) & 0x7F, d2 = (packed >> 16) & 0x7F;

            if (status == 0xF8)   // clock: beat every 24 ticks, phase-based (BPM would jitter)
            {
                int t = clockTicks + 1;
                if (t <= 0 || t >= 24) { t = 0; Interlocked.Increment(ref beatSeq); }
                clockTicks = t;
                ActivitySeq++;
                return;
            }
            if (status == 0xFA || status == 0xFC) { clockTicks = -1; return; }   // start/stop: next clock is beat 0

            int type = status & 0xF0, chan = status & 0x0F;
            if (type < 0x80 || type == 0xF0) return;
            if (s.MidiChannel != 0 && chan != s.MidiChannel - 1) return;

            if (type == 0xB0)
            {
                switch (d1)
                {
                    case 20: CcSpeed = d2; break;
                    case 21: CcDensity = d2; break;
                    case 22: CcHue = d2; break;
                    case 23: CcBlend = d2; break;
                    case 24: CcTail = d2; break;
                    case 25: CcDim = d2; break;
                    default: return;
                }
                ActivitySeq++;
            }
            else if (type == 0x90 && d2 > 0)   // note on (velocity 0 = note off)
            {
                if (d1 == 36) Interlocked.Increment(ref overlayToggles);
                else Interlocked.Add(ref burstPending, 1 + d2 / 8);   // 1..16 drops by velocity
                ActivitySeq++;
            }
        }

        public void Dispose() { Close(); }
    }

    // ------------------------------------------------------------------ engine: timers, idle/saver logic, pause rules
    class Engine : IDisposable
    {
        readonly Settings s;
        readonly RainCore core;
        readonly WallpaperWindow wall;
        readonly SaverWindow saver;
        readonly TestWindow test;
        readonly AudioMeter audio = new AudioMeter();
        public readonly Hue Lights;
        public readonly Midi MidiCtl;
        public Color RainColour() { return core.CurrentColor(); }
        public Color RainColourFor(int i, int n) { return core.ColourFor(i, n); }
        Color GitLight(int i, int n) { return s.HueColorGit.IsEmpty ? core.ColourFor(i, n) : s.HueColorGit; }
        Color IdleLight(int i, int n) { return s.HueColorIdle.IsEmpty ? core.ColourFor(i, n) : s.HueColorIdle; }
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
        int pollTicks = 0, audioHits = 0;
        float secPeak = 0f;
        // git trigger state
        bool gitRunning = false, gitWallpaper = false, gitLights = false;   // gitWallpaper: background exists only because git is running
        int gitQuietPolls = 99;     // starts "quiet" so the grace window doesn't fire at startup
        bool BackgroundOn { get { return s.Wallpaper || gitWallpaper; } }
        uint saverShownInput = 0;
        readonly uint startTick = unchecked((uint)Environment.TickCount);
        bool fullscreenApp = false;
        public bool VideoPlaying { get; private set; }
        public event EventHandler StateChanged;   // tray refreshes its menu/icon

        public bool SaverActive { get { return saver != null && saver.Visible; } }
        public bool EffectivelyPaused { get { return s.Paused || (s.PauseOnVideo && VideoPlaying && !SaverActive) || (fullscreenApp && !SaverActive); } }

        public Engine(Settings settings)
        {
            s = settings;
            Lights = new Hue(s);
            MidiCtl = new Midi(s);
            if (s.MidiEnabled && s.MidiPort.Length > 0 && !MidiCtl.Open(s.MidiPort))
                Program.Log("midi: " + MidiCtl.LastError);
            Rectangle vs = SystemInformation.VirtualScreen;
            core = new RainCore(Program.WindowMode ? 1280 : vs.Width, Program.WindowMode ? 720 : vs.Height, s);
            if (Program.WindowMode) { test = new TestWindow(core); test.Closed += delegate { Application.Exit(); }; }
            else
            {
                wall = new WallpaperWindow(core);
                saver = new SaverWindow(core);
                if (s.Wallpaper) wall.Create();
            }
            tick.Interval = 33; tick.Tick += OnTick; tick.Start();
            poll.Interval = 250; poll.Tick += OnPoll; poll.Start();   // idle/input/audio/fullscreen checks
            ApplyKeepAwake();
            if (s.Wallpaper && s.HueOnBackground) Lights.Start(GitLight);   // permanent background -> lights follow it
            if (Program.StartSaver) ShowSaver();
        }

        void OnTick(object sender, EventArgs e)
        {
            try
            {
                // MIDI: consume queued events and publish overrides on the UI thread.
                // Runs before the pause gate so an overlay toggle works while paused.
                if (s.MidiEnabled)
                {
                    if ((MidiCtl.TakeOverlayToggles() & 1) == 1) { if (SaverActive) HideSaver(); else ShowSaver(); }
                    int burst = MidiCtl.TakeBursts();
                    if (burst > 0) core.Burst(Math.Min(burst, 80));
                    if (MidiCtl.TakeBeat()) core.BeatPulse = 1f;
                    core.MidiSpeedMul = MidiCtl.CcSpeed >= 0 ? 0.35f + (2.3f - 0.35f) * MidiCtl.CcSpeed / 127f : -1f;
                    core.MidiDensity = MidiCtl.CcDensity >= 0 ? MidiCtl.CcDensity / 127f : -1f;
                    core.MidiHue = MidiCtl.CcHue >= 0 ? MidiCtl.CcHue * 360f / 127f : -1f;
                    core.MidiBlendT = MidiCtl.CcBlend >= 0 ? MidiCtl.CcBlend / 127f : -1f;
                    core.MidiTail = MidiCtl.CcTail >= 0 ? MidiCtl.CcTail / 127f : -1f;
                    core.MidiDim = MidiCtl.CcDim >= 0 ? MidiCtl.CcDim / 127f : -1f;
                }
                else if (core.MidiSpeedMul >= 0f || core.MidiHue >= 0f || core.MidiDensity >= 0f
                         || core.MidiBlendT >= 0f || core.MidiTail >= 0f || core.MidiDim >= 0f)
                {
                    core.MidiSpeedMul = core.MidiDensity = core.MidiHue = core.MidiBlendT = core.MidiTail = core.MidiDim = -1f;
                }

                if (EffectivelyPaused) return;
                // Nothing is looking at the buffer -> don't burn CPU.
                if (!Program.WindowMode && !BackgroundOn && !SaverActive) return;
                core.Step();
                Program.Frames++;
                if (test != null) test.Present();
                if (wall != null && BackgroundOn) wall.Present();
                if (saver != null && saver.Visible) saver.Present();
            }
            catch (Exception ex) { Program.Log("tick error: " + ex); }
        }

        void OnPoll(object sender, EventArgs e)
        {
            try
            {
                pollTicks++;
                uint rawIdle = Native.IdleMs();
                uint lastInput = unchecked((uint)Environment.TickCount - rawIdle);
                // Dismiss the saver on any input after it appeared.
                if (SaverActive && unchecked(lastInput - saverShownInput) > 400 && rawIdle < 1000)
                {
                    Program.Log("saver dismissed by input: rawIdle=" + rawIdle + "ms sinceShownInput=" + unchecked(lastInput - saverShownInput) + "ms");
                    HideSaver(); return;
                }

                // For triggering, idle counts from app start (otherwise launching on an already-idle machine pops the overlay instantly).
                uint idle = rawIdle;
                uint sinceStart = unchecked((uint)Environment.TickCount - startTick);
                if (idle > sinceStart) idle = sinceStart;

                // Peak meter sampled every poll (250 ms); decide once a second on the max of the last second.
                float peak = audio.Peak();
                if (peak > secPeak) secPeak = peak;
                if (pollTicks % 4 == 0)   // once a second
                {
                    if (secPeak > 0.01f) audioHits = Math.Min(audioHits + 1, 6); else audioHits = Math.Max(audioHits - 1, 0);
                    bool playing = VideoPlaying ? audioHits > 0 : audioHits >= 2;   // 2 s to start, ~3 s of silence to stop
                    if (playing != VideoPlaying) { VideoPlaying = playing; Program.Log("audio playing=" + playing + " secPeak=" + secPeak.ToString("0.000")); Notify(); }
                    secPeak = 0f;
                    fullscreenApp = s.PauseWhenFullscreen && Native.IsFullscreenAppActive();
                    OnDisplayChange();   // no-op unless the virtual screen size changed
                    if (wall != null && BackgroundOn && pollTicks % 12 == 0) wall.Watch();
                }

                if (!SaverActive && s.SaverOnIdle && !Program.WindowMode && idle >= (uint)s.IdleSeconds * 1000u
                    && !(s.PauseOnVideo && VideoPlaying) && !fullscreenApp)
                    ShowSaver(false);

                // --- git trigger: background rain (behind the icons) while any git.exe is running (checked every 500 ms).
                // Only matters when the background is otherwise off; it is created for the duration and removed afterwards.
                if (pollTicks % 2 == 0 && !Program.WindowMode && wall != null)
                {
                    bool running = s.GitTrigger && IsProcessRunning("git");
                    if (Program.Debug && (running || gitRunning)) Program.Log("git poll: running=" + running + " quiet=" + gitQuietPolls + " bg=" + gitWallpaper);
                    if (running) gitQuietPolls = 0; else gitQuietPolls++;
                    gitRunning = running || gitQuietPolls < 3;            // ~1.5 s grace so chained git calls don't flap
                    // lights: react to git whether or not the background is already on
                    if (gitRunning && !gitLights) { gitLights = true; if (s.HueOnBackground && !SaverActive) Lights.Start(GitLight); Program.Log("git: lights on"); }
                    else if (!gitRunning && gitLights) { gitLights = false; if (!SaverActive && !(s.Wallpaper && s.HueOnBackground)) Lights.Stop(); Program.Log("git: lights off"); }
                    // background: only needed when it is otherwise off
                    if (gitRunning && !s.Wallpaper && !gitWallpaper)
                    {
                        Program.Log("git: background on");
                        gitWallpaper = true;
                        try { wall.Create(); core.Clear(); } catch (Exception ex) { Program.Log("git wallpaper failed: " + ex.Message); gitWallpaper = false; }
                    }
                    else if (gitWallpaper && (!gitRunning || s.Wallpaper))
                    {
                        Program.Log("git: background off");
                        gitWallpaper = false;
                        if (!s.Wallpaper) wall.Destroy();
                    }
                }
            }
            catch (Exception ex) { Program.Log("poll error: " + ex); }
        }

        // Toolhelp snapshot walk. (Process.GetProcessesByName throws when a process disappears mid-enumeration,
        // which happens constantly while git spawns helpers, so it kept reporting "not running".)
        static bool IsProcessRunning(string exeName)
        {
            string target = exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exeName : exeName + ".exe";
            IntPtr snap = Native.CreateToolhelp32Snapshot(2 /*TH32CS_SNAPPROCESS*/, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return false;
            try
            {
                Native.PROCESSENTRY32 pe = new Native.PROCESSENTRY32();
                pe.dwSize = (uint)Marshal.SizeOf(typeof(Native.PROCESSENTRY32));
                if (!Native.Process32First(snap, ref pe)) return false;
                do { if (string.Equals(pe.szExeFile, target, StringComparison.OrdinalIgnoreCase)) return true; }
                while (Native.Process32Next(snap, ref pe));
                return false;
            }
            finally { Native.CloseHandle(snap); }
        }

        public void ShowSaver(bool unused = false)
        {
            if (saver == null || saver.Visible) return;
            Program.Log("saver show");
            saverShownInput = unchecked((uint)Environment.TickCount - Native.IdleMs());
            if (!s.Wallpaper) core.Clear();
            saver.Show();
            Lights.Start(IdleLight);   // overlay -> "idle" colour (blue by default); switches live if git already had them purple
            Notify();
        }

        public void HideSaver()
        {
            if (saver == null || !saver.Visible) return;
            Program.Log("saver hide");
            saver.Hide();
            if ((gitLights || s.Wallpaper) && s.HueOnBackground) Lights.Start(GitLight);   // back to the background/git colour
            else Lights.Stop();
            Notify();
        }

        public void ApplyWallpaper()
        {
            if (wall == null) return;
            if (s.Wallpaper) { if (wall.Handle == IntPtr.Zero) wall.Create(); else wall.Reattach(); core.Clear(); if (s.HueOnBackground && !SaverActive) Lights.Start(GitLight); }
            else { wall.Destroy(); core.Clear(); if (!SaverActive && !gitLights) Lights.Stop(); }
        }

        // "Lights follow the background" toggled from the tray.
        public void ApplyHueBackground()
        {
            if (SaverActive) return;
            if (s.HueOnBackground && (s.Wallpaper || gitLights)) Lights.Start(GitLight);
            else Lights.Stop();
        }

        public void ApplyKeepAwake()
        {
            Native.SetThreadExecutionState(Native.ES_CONTINUOUS | (s.KeepAwake ? Native.ES_SYSTEM_REQUIRED | Native.ES_DISPLAY_REQUIRED : 0));
        }

        public void Reconfigure() { core.Reconfigure(); }

        public void OnDisplayChange()
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            if (Program.WindowMode || (vs.Width == core.Width && vs.Height == core.Height)) return;
            Program.Log("display change -> " + vs.Width + "x" + vs.Height);
            try
            {
                core.Resize(vs.Width, vs.Height);
                if (wall != null && BackgroundOn) wall.Reattach();   // covers the git-triggered background too
                if (saver != null && saver.Visible) saver.Show();
            }
            catch (Exception ex) { Program.Log("display change failed: " + ex.Message); }
        }

        void Notify() { if (StateChanged != null) StateChanged(this, EventArgs.Empty); }

        public void Dispose()
        {
            tick.Stop(); poll.Stop(); tick.Dispose(); poll.Dispose();
            Native.SetThreadExecutionState(Native.ES_CONTINUOUS);
            if (saver != null) saver.Destroy();
            if (wall != null) wall.Destroy();
            if (test != null) test.Destroy();
            MidiCtl.Dispose();   // first: no late driver callback may publish into a dying engine
            Lights.Dispose();
            audio.Dispose();
            core.Dispose();
        }
    }

    // ------------------------------------------------------------------ tray
    class TrayContext : ApplicationContext
    {
        readonly Settings s;
        readonly Engine engine;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        ToolStripMenuItem miPause, miWallpaper, miSaverStart, miSaverStop, miSaverOnIdle, miGit, miRainbow, miVideo, miFullscreen, miKeepAwake, miAutostart;
        ToolStripMenuItem mIdle, mColor, mMore, mColor2, mBlend, mSpeed, mDensity, mTail, mFlicker, mSize, mChars;
        ToolStripMenuItem mHue, miHueEnabled, miHueBg, miHuePair, mHueLights, miHueTest;
        ToolStripMenuItem mMidi, miMidiEnabled, mMidiPort, miMidiStatus;

        // Green family first (all tuned around the classic matrix phosphor), other hues under "More colours".
        static readonly object[,] GREENS = {
            { "Matrix Green (classic)", Color.FromArgb(0, 255, 70) },
            { "Phosphor",               Color.FromArgb(60, 255, 110) },
            { "Neon Lime",              Color.FromArgb(120, 255, 40) },
            { "Emerald",                Color.FromArgb(0, 220, 100) },
            { "Jade",                   Color.FromArgb(0, 200, 140) },
            { "Deep Forest",            Color.FromArgb(0, 170, 50) },
            { "Mint",                   Color.FromArgb(150, 255, 190) },
            { "Sea Green",              Color.FromArgb(0, 230, 190) },
        };
        static readonly object[,] OTHERS = {
            { "Neon Cyan",     Color.FromArgb(0, 230, 255) },
            { "Electric Blue", Color.FromArgb(40, 110, 255) },
            { "Purple Haze",   Color.FromArgb(180, 60, 255) },
            { "Hot Pink",      Color.FromArgb(255, 50, 170) },
            { "Blood Red",     Color.FromArgb(255, 30, 30) },
            { "Amber",         Color.FromArgb(255, 176, 0) },
            { "Ice White",     Color.FromArgb(220, 230, 240) },
        };

        public TrayContext()
        {
            s = Settings.Load();
            // The Run key is only ever mutated from the explicit tray toggle. Touching it on
            // !s.Loaded would also fire when an EXISTING settings file is unreadable, deleting
            // a Run entry the user opted into.
            engine = new Engine(s);
            engine.StateChanged += delegate { SyncChecks(); RefreshIcon(); };

            tray = new NotifyIcon();
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object o, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowMenu(); };
            tray.DoubleClick += delegate { TogglePause(); };
            BuildMenu();
            RefreshIcon();
            tray.Visible = true;
            s.Save();
        }

        void ShowMenu()
        {
            // NotifyIcon only auto-opens the menu on right click; mirror it for left click.
            typeof(NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(tray, null);
        }

        void BuildMenu()
        {
            menu.Items.Clear();
            ToolStripMenuItem title = new ToolStripMenuItem("MatrixBG " + Settings.Version); title.Enabled = false; title.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add(title);
            menu.Items.Add(new ToolStripSeparator());

            // --- background mode (behind the desktop icons)
            ToolStripMenuItem hBg = new ToolStripMenuItem("BACKGROUND  (behind desktop icons)"); hBg.Enabled = false;
            miWallpaper = new ToolStripMenuItem("Enable background rain", null, delegate { s.Wallpaper = !s.Wallpaper; engine.ApplyWallpaper(); Changed(); });
            miPause = new ToolStripMenuItem("Pause animation", null, delegate { TogglePause(); });
            miGit = new ToolStripMenuItem("Trigger: show background while a git command runs", null, delegate { s.GitTrigger = !s.GitTrigger; Changed(); });
            menu.Items.Add(hBg); menu.Items.Add(miWallpaper); menu.Items.Add(miGit); menu.Items.Add(miPause);
            menu.Items.Add(new ToolStripSeparator());

            // --- overlay mode (fullscreen screensaver on top of everything)
            ToolStripMenuItem hOv = new ToolStripMenuItem("OVERLAY  (fullscreen screensaver)"); hOv.Enabled = false;
            miSaverStart = new ToolStripMenuItem("Start overlay now", null, delegate { engine.ShowSaver(); });
            miSaverStop = new ToolStripMenuItem("Stop overlay", null, delegate { engine.HideSaver(); });
            miSaverOnIdle = new ToolStripMenuItem("Trigger: auto-start when idle", null, delegate { s.SaverOnIdle = !s.SaverOnIdle; Changed(); });
            mIdle = new ToolStripMenuItem("Trigger: idle timeout");
            menu.Items.Add(hOv);
            foreach (int sec in Settings.IdleChoices)
            {
                int v = sec;
                ToolStripMenuItem mi = new ToolStripMenuItem(IdleLabel(sec), null, delegate { s.IdleSeconds = v; Changed(); });
                mi.Tag = v; mIdle.DropDownItems.Add(mi);
            }
            menu.Items.Add(miSaverStart); menu.Items.Add(miSaverStop); menu.Items.Add(miSaverOnIdle); menu.Items.Add(mIdle);
            menu.Items.Add(new ToolStripSeparator());

            // --- settings
            ToolStripMenuItem hSet = new ToolStripMenuItem("SETTINGS"); hSet.Enabled = false;
            menu.Items.Add(hSet);
            mColor = new ToolStripMenuItem("Colour");
            AddPresets(mColor, GREENS);
            mMore = new ToolStripMenuItem("More colours");
            AddPresets(mMore, OTHERS);
            mColor.DropDownItems.Add(new ToolStripSeparator());
            mColor.DropDownItems.Add(mMore);
            miRainbow = new ToolStripMenuItem("Rainbow cycle", null, delegate { s.Rainbow = !s.Rainbow; Changed(); });
            mColor.DropDownItems.Add(miRainbow);
            mColor.DropDownItems.Add(new ToolStripMenuItem("Custom...", null, delegate { PickColor(false); }));
            menu.Items.Add(mColor);

            // second colour + blend mode
            mColor2 = new ToolStripMenuItem("Second colour (for blends)");
            AddPresets(mColor2, GREENS, true);
            mColor2.DropDownItems.Add(new ToolStripSeparator());
            AddPresets(mColor2, OTHERS, true);
            mColor2.DropDownItems.Add(new ToolStripSeparator());
            mColor2.DropDownItems.Add(new ToolStripMenuItem("Custom...", null, delegate { PickColor(true); }));
            mBlend = Choice("Two-colour blend", new string[] { "Off (single colour)", "Fade between the two over time", "Side by side (left → right)", "Top → bottom", "Mixed (each streak its own colour)", "Mixed + fading (streaks drift between the two)" }, delegate { return s.Blend; }, delegate(int v) { s.Blend = v; });
            menu.Items.Add(mColor2); menu.Items.Add(mBlend);

            mSpeed = Choice("Speed", new string[] { "Glacial", "Slow", "Normal", "Fast", "Ludicrous" }, delegate { return s.Speed; }, delegate(int v) { s.Speed = v; });
            mDensity = Choice("Density", new string[] { "Sparse", "Light", "Normal", "Heavy", "Downpour" }, delegate { return s.Density; }, delegate(int v) { s.Density = v; });
            mTail = Choice("Tail length", new string[] { "Short", "Normal", "Long", "Very long", "Epic", "Endless" }, delegate { return s.Tail; }, delegate(int v) { s.Tail = v; });
            mFlicker = Choice("Glyph blinks", new string[] { "Off", "Subtle", "Normal", "Lots" }, delegate { return s.Flicker; }, delegate(int v) { s.Flicker = v; });
            mSize = Choice("Glyph size", new string[] { "Tiny", "Small", "Normal", "Large", "Huge" }, delegate { return s.FontSize; }, delegate(int v) { s.FontSize = v; });
            mChars = Choice("Characters", new string[] { "Katakana (classic)", "Binary 0/1", "Hex", "Latin" }, delegate { return s.Charset; }, delegate(int v) { s.Charset = v; });
            menu.Items.Add(mSpeed); menu.Items.Add(mDensity); menu.Items.Add(mTail); menu.Items.Add(mFlicker); menu.Items.Add(mSize); menu.Items.Add(mChars);
            menu.Items.Add(new ToolStripSeparator());

            // --- behaviour
            miVideo = new ToolStripMenuItem("Pause during video / audio playback", null, delegate { s.PauseOnVideo = !s.PauseOnVideo; Changed(); });
            miFullscreen = new ToolStripMenuItem("Pause when a fullscreen app is active", null, delegate { s.PauseWhenFullscreen = !s.PauseWhenFullscreen; Changed(); });
            miKeepAwake = new ToolStripMenuItem("Keep awake (no sleep / display off)", null, delegate { s.KeepAwake = !s.KeepAwake; engine.ApplyKeepAwake(); Changed(); });
            miAutostart = new ToolStripMenuItem("Launch at login", null, delegate { s.Autostart = !s.Autostart; ApplyAutostart(); Changed(); });
            menu.Items.Add(miVideo); menu.Items.Add(miFullscreen); menu.Items.Add(miKeepAwake); menu.Items.Add(miAutostart);

            // --- Philips Hue
            mHue = new ToolStripMenuItem("Philips Hue lights");
            miHueEnabled = new ToolStripMenuItem("Enable light effects", null, delegate { s.HueEnabled = !s.HueEnabled; if (!s.HueEnabled) engine.Lights.Stop(); Changed(); });
            miHueBg = new ToolStripMenuItem("Lights follow the background too (permanent or git-triggered)", null, delegate { s.HueOnBackground = !s.HueOnBackground; engine.ApplyHueBackground(); Changed(); });
            miHuePair = new ToolStripMenuItem("Pair with bridge...", null, delegate { PairHue(); });
            mHueLights = new ToolStripMenuItem("Lights to use");
            miHueTest = new ToolStripMenuItem("Test lights (5 s)", null, delegate { TestHue(); });
            ToolStripMenuItem mHueGit = new ToolStripMenuItem("Light colour: git / background");
            ToolStripMenuItem mHueIdle = new ToolStripMenuItem("Light colour: overlay (idle)");
            object[,] LIGHTCOLS = {
                { "Purple",  Color.FromArgb(150, 0, 255) }, { "Blue", Color.FromArgb(0, 90, 255) }, { "Matrix Green", Color.FromArgb(0, 255, 70) },
                { "Cyan", Color.FromArgb(0, 230, 255) }, { "Red", Color.FromArgb(255, 30, 30) }, { "Amber", Color.FromArgb(255, 176, 0) },
                { "Pink", Color.FromArgb(255, 50, 170) }, { "White", Color.FromArgb(255, 255, 255) } };
            for (int i = 0; i < LIGHTCOLS.GetLength(0); i++)
            {
                string nm = (string)LIGHTCOLS[i, 0]; Color c = (Color)LIGHTCOLS[i, 1];
                ToolStripMenuItem a = new ToolStripMenuItem(nm, Swatch(c), delegate { s.HueColorGit = c; Changed(); }); a.Tag = c; mHueGit.DropDownItems.Add(a);
                ToolStripMenuItem b = new ToolStripMenuItem(nm, Swatch(c), delegate { s.HueColorIdle = c; Changed(); }); b.Tag = c; mHueIdle.DropDownItems.Add(b);
            }
            mHueGit.DropDownItems.Add(new ToolStripMenuItem("Match the rain exactly (each bulb takes its place in the blend)", null, delegate { s.HueColorGit = Color.Empty; Changed(); }));
            mHueIdle.DropDownItems.Add(new ToolStripMenuItem("Match the rain exactly (each bulb takes its place in the blend)", null, delegate { s.HueColorIdle = Color.Empty; Changed(); }));
            mHueGit.DropDownOpening += delegate { foreach (ToolStripItem it in mHueGit.DropDownItems) { ToolStripMenuItem mi = it as ToolStripMenuItem; if (mi != null) mi.Checked = mi.Tag is Color ? ((Color)mi.Tag).ToArgb() == s.HueColorGit.ToArgb() : s.HueColorGit.IsEmpty; } };
            mHueIdle.DropDownOpening += delegate { foreach (ToolStripItem it in mHueIdle.DropDownItems) { ToolStripMenuItem mi = it as ToolStripMenuItem; if (mi != null) mi.Checked = mi.Tag is Color ? ((Color)mi.Tag).ToArgb() == s.HueColorIdle.ToArgb() : s.HueColorIdle.IsEmpty; } };
            mHue.DropDownItems.Add(miHueEnabled); mHue.DropDownItems.Add(miHueBg); mHue.DropDownItems.Add(mHueGit); mHue.DropDownItems.Add(mHueIdle); mHue.DropDownItems.Add(new ToolStripSeparator());
            mHue.DropDownItems.Add(miHuePair); mHue.DropDownItems.Add(mHueLights); mHue.DropDownItems.Add(miHueTest);
            mHueLights.DropDownOpening += delegate { LoadHueLights(); };
            menu.Items.Add(mHue);

            // --- MIDI control
            mMidi = new ToolStripMenuItem("MIDI control (APC / Traktor / loopMIDI)");
            miMidiEnabled = new ToolStripMenuItem("Enable MIDI control", null, delegate
            {
                s.MidiEnabled = !s.MidiEnabled;
                if (s.MidiEnabled) { if (s.MidiPort.Length > 0 && !engine.MidiCtl.Open(s.MidiPort)) { } }
                else engine.MidiCtl.Close();
                Changed();
            });
            mMidiPort = new ToolStripMenuItem("Input port");
            mMidiPort.DropDownOpening += delegate
            {
                mMidiPort.DropDownItems.Clear();
                string[] ports = engine.MidiCtl.Ports();
                if (ports.Length == 0)
                {
                    ToolStripMenuItem none = new ToolStripMenuItem("No MIDI input devices (install loopMIDI for a virtual port)");
                    none.Enabled = false;
                    mMidiPort.DropDownItems.Add(none);
                    return;
                }
                foreach (string p in ports)
                {
                    string name = p;
                    ToolStripMenuItem mi = new ToolStripMenuItem(name, null, delegate
                    {
                        s.MidiPort = name;
                        if (s.MidiEnabled) engine.MidiCtl.Open(name);
                        Changed();
                    });
                    mi.Checked = name == s.MidiPort;
                    mMidiPort.DropDownItems.Add(mi);
                }
            };
            miMidiStatus = new ToolStripMenuItem("");
            miMidiStatus.Enabled = false;
            mMidi.DropDownItems.Add(miMidiEnabled); mMidi.DropDownItems.Add(mMidiPort); mMidi.DropDownItems.Add(miMidiStatus);
            mMidi.DropDownItems.Add(new ToolStripMenuItem("Mapping...", null, delegate
            {
                MessageBox.Show(
                    "Default MIDI map (channel " + (s.MidiChannel == 0 ? "any" : s.MidiChannel.ToString()) + "; midichannel= in settings.txt):\n\n" +
                    "CC 20   Speed\nCC 21   Density\nCC 22   Hue (replaces colour A; overrides rainbow)\nCC 23   Blend position (with a two-colour blend)\nCC 24   Tail length\nCC 25   Brightness\n\n" +
                    "Any Note On   burst of drops (velocity = size)\nNote 36 (C1)   toggle the fullscreen overlay\nMIDI clock   beat pulse every quarter note\n\n" +
                    "Tip: give MatrixBG its own loopMIDI port so a Traktor/APC mapping never collides with another app.",
                    "MatrixBG MIDI", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }));
            menu.Items.Add(mMidi);
            menu.Items.Add(new ToolStripMenuItem("Open settings folder", null, delegate { try { Directory.CreateDirectory(Settings.Dir); Process.Start("explorer.exe", Settings.Dir); } catch { } }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("About MatrixBG...", null, delegate { About(); }));
            menu.Items.Add(new ToolStripMenuItem("Quit", null, delegate { Quit(); }));

            menu.Opening += delegate { SyncChecks(); };
            SyncChecks();
        }

        static string IdleLabel(int sec)
        {
            if (sec < 60) return sec + " seconds";
            if (sec % 60 == 0 && sec < 3600) return (sec / 60) + " minute" + (sec == 60 ? "" : "s");
            return (sec / 60f).ToString("0.#") + " minutes";
        }

        void AddPresets(ToolStripMenuItem parent, object[,] presets) { AddPresets(parent, presets, false); }
        void AddPresets(ToolStripMenuItem parent, object[,] presets, bool second)
        {
            for (int i = 0; i < presets.GetLength(0); i++)
            {
                string name = (string)presets[i, 0]; Color c = (Color)presets[i, 1];
                ToolStripMenuItem mi = new ToolStripMenuItem(name, Swatch(c), delegate { if (second) SetColor2(c); else SetColor(c); });
                mi.Tag = c;
                parent.DropDownItems.Add(mi);
            }
        }

        void SetColor2(Color c) { s.Color2 = c; Changed(); }

        delegate int Getter(); delegate void Setter(int v);
        ToolStripMenuItem Choice(string label, string[] names, Getter get, Setter set)
        {
            ToolStripMenuItem m = new ToolStripMenuItem(label);
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                ToolStripMenuItem mi = new ToolStripMenuItem(names[i], null, delegate { set(idx); Changed(true); });
                mi.Tag = idx;
                m.DropDownItems.Add(mi);
            }
            m.Tag = get;
            return m;
        }

        void SyncChecks()
        {
            miPause.Text = s.Paused ? "Resume animation" : "Pause animation";
            miPause.Checked = s.Paused;
            miWallpaper.Checked = s.Wallpaper;
            miSaverStart.Enabled = !engine.SaverActive;
            miSaverStop.Enabled = engine.SaverActive;
            miSaverOnIdle.Checked = s.SaverOnIdle;
            miGit.Checked = s.GitTrigger;
            miRainbow.Checked = s.Rainbow;
            miVideo.Checked = s.PauseOnVideo;
            miVideo.Text = "Pause during video / audio playback" + (engine.VideoPlaying ? "   (playing now)" : "");
            miFullscreen.Checked = s.PauseWhenFullscreen;
            miKeepAwake.Checked = s.KeepAwake;
            miAutostart.Checked = s.Autostart;
            miHueEnabled.Checked = s.HueEnabled;
            miHueBg.Checked = s.HueOnBackground;
            miHuePair.Text = engine.Lights.Paired ? "Re-pair with bridge...  (paired: " + s.HueIp + ")" : "Pair with bridge...";
            miMidiEnabled.Checked = s.MidiEnabled;
            miMidiStatus.Text = !s.MidiEnabled ? "MIDI off"
                : engine.MidiCtl.OpenPortName.Length > 0 ? "Listening on " + engine.MidiCtl.OpenPortName
                : engine.MidiCtl.LastError.Length > 0 ? "Error: " + engine.MidiCtl.LastError
                : "Pick an input port";
            mHueLights.Enabled = engine.Lights.Paired; miHueTest.Enabled = engine.Lights.Paired;
            foreach (ToolStripItem it in mIdle.DropDownItems)
            {
                ToolStripMenuItem mi = it as ToolStripMenuItem;
                if (mi != null) mi.Checked = (int)mi.Tag == s.IdleSeconds;
            }
            CheckColor(mColor); CheckColor(mMore);
            foreach (ToolStripItem it in mColor2.DropDownItems)
            {
                ToolStripMenuItem mi = it as ToolStripMenuItem;
                if (mi != null && mi.Tag is Color) mi.Checked = ((Color)mi.Tag).ToArgb() == s.Color2.ToArgb();
            }
            foreach (ToolStripMenuItem m in new ToolStripMenuItem[] { mBlend, mSpeed, mDensity, mTail, mFlicker, mSize, mChars })
            {
                int cur = ((Getter)m.Tag)();
                foreach (ToolStripItem it in m.DropDownItems)
                {
                    ToolStripMenuItem mi = it as ToolStripMenuItem;
                    if (mi != null) mi.Checked = (int)mi.Tag == cur;
                }
            }
        }

        void CheckColor(ToolStripMenuItem parent)
        {
            foreach (ToolStripItem it in parent.DropDownItems)
            {
                ToolStripMenuItem mi = it as ToolStripMenuItem;
                if (mi != null && mi.Tag is Color) mi.Checked = !s.Rainbow && ((Color)mi.Tag).ToArgb() == s.Color.ToArgb();
            }
        }

        static Image Swatch(Color c)
        {
            Bitmap b = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Black);
                using (SolidBrush br = new SolidBrush(c)) g.FillRectangle(br, 2, 2, 12, 12);
            }
            return b;
        }

        void SetColor(Color c) { s.Color = c; s.Rainbow = false; Changed(); }

        void PickColor(bool second)
        {
            using (ColorDialog d = new ColorDialog())
            {
                d.Color = second ? s.Color2 : s.Color; d.FullOpen = true; d.AnyColor = true;
                if (d.ShowDialog() == DialogResult.OK) { if (second) SetColor2(d.Color); else SetColor(d.Color); }
            }
        }

        void TogglePause() { s.Paused = !s.Paused; Changed(); }

        void Changed() { Changed(false); }
        void Changed(bool reconfigure)
        {
            s.Save();
            if (reconfigure) engine.Reconfigure();
            SyncChecks();
            RefreshIcon();
        }

        void RefreshIcon()
        {
            Color c = s.Rainbow ? Color.FromArgb(255, 0, 255) : s.Color;
            if (engine.EffectivelyPaused) c = Color.FromArgb(c.R / 3, c.G / 3, c.B / 3);
            Icon old = tray.Icon;
            tray.Icon = MakeIcon(c);
            if (old != null) old.Dispose();
            string state = s.Paused ? "paused" : (engine.VideoPlaying && s.PauseOnVideo ? "paused (audio playing)" : "running");
            string text = "MatrixBG - " + state;            // NotifyIcon.Text is limited to 63 chars
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        public static Icon MakeIcon(Color c)
        {
            using (Bitmap b = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Black);
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    using (Font f = new Font("Consolas", 13, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (SolidBrush br = new SolidBrush(c))
                    using (SolidBrush dim = new SolidBrush(Color.FromArgb(120, c)))
                    {
                        g.DrawString("1", f, br, 2, 1); g.DrawString("0", f, dim, 12, -2); g.DrawString("1", f, br, 21, 3);
                        g.DrawString("0", f, dim, 2, 14); g.DrawString("1", f, br, 12, 10); g.DrawString("0", f, dim, 21, 16);
                        g.DrawString("1", f, dim, 12, 20);
                    }
                }
                IntPtr h = b.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { DestroyIcon(h); }
            }
        }
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        void ApplyAutostart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (s.Autostart) k.SetValue("MatrixBG", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("MatrixBG", false);
                }
            }
            catch { }
        }

        // ---- Philips Hue helpers
        // The whole flow (discovery GET, pairing retries with sleeps) runs off the UI thread:
        // an unreachable bridge would otherwise freeze the tray for ~10 s of timeouts.
        // MessageBoxes are fine on a worker thread (no owner window in a tray app); the
        // settings/menu mutation on success is marshalled back via the captured UI context.
        volatile bool pairing = false;   // volatile: set on the UI thread, cleared on the worker
        void PairHue()
        {
            if (pairing) return;
            pairing = true;
            System.Threading.SynchronizationContext ui = System.Threading.SynchronizationContext.Current;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string ip = Hue.Discover();
                    if (ip.Length == 0) ip = s.HueIp;
                    if (ip.Length == 0)
                    {
                        MessageBox.Show("No Hue bridge found on this network (discovery.meethue.com returned nothing).\nMake sure the bridge is on the same network and try again.", "Philips Hue", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (MessageBox.Show("Bridge found at " + ip + ".\n\nPress the round link button on the bridge now, then click OK (within 30 seconds).", "Philips Hue", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
                    string msg = ""; bool ok = false;
                    for (int i = 0; i < 6 && !ok; i++) { ok = engine.Lights.TryPair(ip, out msg); if (!ok) Thread.Sleep(1500); }
                    if (ok && ui != null) ui.Post(delegate(object _) { s.HueEnabled = true; Changed(); }, null);
                    MessageBox.Show(ok ? msg + "\nLight effects are now enabled. Pick lights under \"Lights to use\" (default: all colour lights)." : "Pairing failed: " + msg, "Philips Hue", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                }
                finally { pairing = false; }
            });
        }

        void LoadHueLights()
        {
            mHueLights.DropDownItems.Clear();
            List<Hue.Light> lights = engine.Lights.GetLights();
            if (lights.Count == 0) { ToolStripMenuItem none = new ToolStripMenuItem(engine.Lights.LastError.Length > 0 ? "Bridge error: " + engine.Lights.LastError : "No lights found"); none.Enabled = false; mHueLights.DropDownItems.Add(none); return; }
            HashSet<string> chosen = new HashSet<string>(s.HueLights.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            ToolStripMenuItem all = new ToolStripMenuItem("All colour lights", null, delegate { s.HueLights = ""; Changed(); });
            all.Checked = chosen.Count == 0;
            mHueLights.DropDownItems.Add(all); mHueLights.DropDownItems.Add(new ToolStripSeparator());
            foreach (Hue.Light l in lights)
            {
                string id = l.Id;
                ToolStripMenuItem mi = new ToolStripMenuItem(l.Name + (l.Colour ? "" : "  (no colour)"), null, delegate
                {
                    HashSet<string> set = new HashSet<string>(s.HueLights.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                    if (!set.Remove(id)) set.Add(id);
                    s.HueLights = string.Join(",", new List<string>(set).ToArray()); Changed();
                });
                mi.Enabled = l.Colour; mi.Checked = chosen.Contains(id);
                mHueLights.DropDownItems.Add(mi);
            }
        }

        void TestHue()
        {
            bool was = s.HueEnabled; s.HueEnabled = true;
            engine.Lights.Start(engine.RainColourFor);
            s.HueEnabled = was;
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer(); t.Interval = 5000;
            t.Tick += delegate { t.Stop(); t.Dispose(); if (!engine.SaverActive) engine.Lights.Stop(); };
            t.Start();
        }

        void About()
        {
            MessageBox.Show(
                "MatrixBG " + Settings.Version + "\n\n" +
                "Matrix rain behind your desktop icons, plus an idle screensaver.\n" +
                "Windows 11 24H2 / 25H2 compatible (layered child of Progman).\n\n" +
                "Settings: " + Settings.File + "\n" +
                "Program:  " + Application.ExecutablePath,
                "About MatrixBG", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        bool quitting = false;
        void Quit()
        {
            if (quitting) return;
            quitting = true;
            tray.Visible = false; tray.ContextMenuStrip = null; tray.Dispose();
            menu.Dispose();
            engine.Dispose();
            ExitThread();
        }
    }

    // ------------------------------------------------------------------ entry
    static class Program
    {
        public static bool WindowMode = false;
        public static bool Debug = false;
        public static bool StartSaver = false;
        public static int Frames = 0;

        public static void Log(string m)
        {
            if (!Debug) return;
            try { Directory.CreateDirectory(Settings.Dir); File.AppendAllText(Path.Combine(Settings.Dir, "debug.log"), DateTime.Now.ToString("HH:mm:ss.fff ") + m + Environment.NewLine); } catch { }
        }

        [STAThread]
        static void Main(string[] args)
        {
            foreach (string a in args)
            {
                if (a == "--window") WindowMode = true;
                if (a == "--debug") Debug = true;
                if (a == "--fullscreen" || a == "--saver") StartSaver = true;
            }
            bool created;
            using (Mutex m = new Mutex(true, WindowMode ? "MatrixBG_Window" : "MatrixBG_SingleInstance", out created))
            {
                if (!created) return; // already running
                try { Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
        }
    }
}
