using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// LangFix: Ctrl+Shift+Space converts text typed with the wrong keyboard language
// (Thai <-> English, Kedmanee layout). A keyboard hook remembers only the current
// word in memory (the physical keys + which layout was active), so the fix is
// Backspace + retype: no clipboard round trip. If nothing was typed since the last
// click/Enter/shortcut, the selected text is converted instead.
static class Converter
{
    const string EngBase = "`1234567890-=qwertyuiop[]\\asdfghjkl;'zxcvbnm,./";
    const string ThaiBase = "_ๅ/-ภถุึคตจขชๆไำพะัีรนยบลฃฟหกดเ้่าสวงผปแอิืทมใฝ";
    const string EngShift = "~!@#$%^&*()_+QWERTYUIOP{}|ASDFGHJKL:\"ZXCVBNM<>?";
    const string ThaiShift = "%+๑๒๓๔ู฿๕๖๗๘๙๐\"ฎฑธํ๊ณฯญฐ,ฅฤฆฏโฌ็๋ษศซ.()ฉฮฺ์?ฒฬฦ";

    static readonly Dictionary<char, char> ToThai = new Dictionary<char, char>();
    static readonly Dictionary<char, char> ToEng = new Dictionary<char, char>();

    static Converter()
    {
        Add(EngBase, ThaiBase);
        Add(EngShift, ThaiShift);
    }

    static void Add(string eng, string thai)
    {
        for (int i = 0; i < eng.Length; i++)
        {
            ToThai[eng[i]] = thai[i];
            if (IsThai(thai[i])) ToEng[thai[i]] = eng[i];
        }
    }

    public static bool IsThai(char c) { return c >= '฀' && c <= '๿'; }

    public static bool HasThai(string s)
    {
        foreach (char c in s) if (IsThai(c)) return true;
        return false;
    }

    public static char EngToThai(char c)
    {
        char r;
        return ToThai.TryGetValue(c, out r) ? r : c;
    }

    public static string Convert(string s)
    {
        var map = HasThai(s) ? ToEng : ToThai;
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            char r;
            sb.Append(map.TryGetValue(c, out r) ? r : c);
        }
        return sb.ToString();
    }

    // Character a US-QWERTY key produces; Thai layouts use the same virtual keys.
    public static char KeyChar(int vk, bool shift)
    {
        if (vk >= 0x41 && vk <= 0x5A) return (char)(shift ? vk : vk + 32);
        if (vk >= 0x30 && vk <= 0x39) return shift ? ")!@#$%^&*("[vk - 0x30] : (char)vk;
        switch (vk)
        {
            case 0xC0: return shift ? '~' : '`';
            case 0xBD: return shift ? '_' : '-';
            case 0xBB: return shift ? '+' : '=';
            case 0xDB: return shift ? '{' : '[';
            case 0xDD: return shift ? '}' : ']';
            case 0xDC: return shift ? '|' : '\\';
            case 0xBA: return shift ? ':' : ';';
            case 0xDE: return shift ? '"' : '\'';
            case 0xBC: return shift ? '<' : ',';
            case 0xBE: return shift ? '>' : '.';
            case 0xBF: return shift ? '?' : '/';
        }
        return '\0';
    }
}

static class Native
{
    public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    public const byte VK_BACK = 0x08, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_CAPITAL = 0x14, VK_SPACE = 0x20;
    public const ushort LANG_THAI = 0x041E, LANG_ENGLISH = 0x0409;
    public static readonly IntPtr OurTag = (IntPtr)0x4C46;

    public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetKeyboardLayoutList(int n, IntPtr[] list);
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public ushort vk;
        [FieldOffset(10)] public ushort scan;
        [FieldOffset(12)] public uint flags;
        [FieldOffset(16)] public uint time;
        [FieldOffset(24)] public IntPtr extra;
    }

    const uint KEYUP = 2, UNICODE = 4, EXTENDED = 1;

    public static INPUT Vk(byte vk, bool up, bool ext = false)
    {
        return new INPUT { type = 1, vk = vk, flags = (up ? KEYUP : 0) | (ext ? EXTENDED : 0), extra = OurTag };
    }

    public static void AddUnicode(List<INPUT> l, char c)
    {
        l.Add(new INPUT { type = 1, scan = c, flags = UNICODE, extra = OurTag });
        l.Add(new INPUT { type = 1, scan = c, flags = UNICODE | KEYUP, extra = OurTag });
    }

    public static void AddKey(List<INPUT> l, byte vk, bool ext = false)
    {
        l.Add(Vk(vk, false, ext));
        l.Add(Vk(vk, true, ext));
    }

    // Lift modifiers the user is still holding for the hotkey so they don't combine with what we send.
    public static void AddReleaseModifiers(List<INPUT> l)
    {
        foreach (byte v in new byte[] { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3 }) l.Add(Vk(v, true));
    }

    public static void Send(List<INPUT> l)
    {
        if (l.Count > 0) SendInput((uint)l.Count, l.ToArray(), Marshal.SizeOf(typeof(INPUT)));
    }

    [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr h);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint fmt);
    [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint fmt, IntPtr mem);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);

    const uint CF_UNICODETEXT = 13;

    static bool OpenClipboardRetry()
    {
        for (int i = 0; i < 10; i++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    public static string ReadClipboardText()
    {
        if (!OpenClipboardRetry()) return null;
        try
        {
            IntPtr h = GetClipboardData(CF_UNICODETEXT);
            if (h == IntPtr.Zero) return null;
            IntPtr p = GlobalLock(h);
            if (p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(p); }
            finally { GlobalUnlock(h); }
        }
        finally { CloseClipboard(); }
    }

    public static void WriteClipboardText(string s)
    {
        if (!OpenClipboardRetry()) return;
        try
        {
            EmptyClipboard();
            byte[] bytes = Encoding.Unicode.GetBytes(s + "\0");
            IntPtr h = GlobalAlloc(0x0002, (UIntPtr)bytes.Length);   // GMEM_MOVEABLE
            IntPtr p = GlobalLock(h);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            GlobalUnlock(h);
            SetClipboardData(CF_UNICODETEXT, h);
        }
        finally { CloseClipboard(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GUITHREADINFO
    {
        public int cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public int left, top, right, bottom;
    }
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);

    // Layout of the control that really has the keyboard focus (more exact than the top-level window's thread).
    public static ushort ForegroundLang()
    {
        IntPtr h = GetForegroundWindow();
        uint thread = GetWindowThreadProcessId(h, IntPtr.Zero);
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
        if (GetGUIThreadInfo(thread, ref info) && info.hwndFocus != IntPtr.Zero)
            thread = GetWindowThreadProcessId(info.hwndFocus, IntPtr.Zero);
        return (ushort)((long)GetKeyboardLayout(thread) & 0xFFFF);
    }

    // Content-free diagnostics (counts and layout ids only, never the typed characters).
    public static void Log(string line)
    {
        try
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LangFix");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "log.txt"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + "\r\n");
        }
        catch { }
    }

    public static void SwitchLayout(ushort langId)
    {
        var list = new IntPtr[32];
        int n = GetKeyboardLayoutList(list.Length, list);
        for (int i = 0; i < n; i++)
        {
            if (((long)list[i] & 0xFFFF) == langId)
            {
                PostMessage(GetForegroundWindow(), 0x0050, IntPtr.Zero, list[i]);
                return;
            }
        }
    }
}

// Remembers the current word (and spaces after it) as typed on the keyboard.
class WordTracker
{
    readonly object gate = new object();
    readonly List<char> keys = new List<char>();   // US-QWERTY character of each key
    readonly List<bool> thai = new List<bool>();   // true if the Thai layout was active for that key
    int trailing;                                  // spaces typed after the word
    IntPtr window;

    public void Clear()
    {
        lock (gate) { keys.Clear(); thai.Clear(); trailing = 0; }
    }

    public void OnKey(int vk, IntPtr hwnd)
    {
        lock (gate)
        {
            if (hwnd != window) { keys.Clear(); thai.Clear(); trailing = 0; window = hwnd; }

            bool ctrlAlt = (Native.GetAsyncKeyState(0x11) & 0x8000) != 0
                        || (Native.GetAsyncKeyState(0x12) & 0x8000) != 0
                        || (Native.GetAsyncKeyState(0x5B) & 0x8000) != 0
                        || (Native.GetAsyncKeyState(0x5C) & 0x8000) != 0;
            if (ctrlAlt) { keys.Clear(); thai.Clear(); trailing = 0; return; }

            if (vk == Native.VK_SPACE) { if (keys.Count > 0) trailing++; return; }
            if (vk == Native.VK_BACK)
            {
                if (trailing > 0) trailing--;
                else if (keys.Count > 0) { keys.RemoveAt(keys.Count - 1); thai.RemoveAt(thai.Count - 1); }
                return;
            }

            bool shift = (Native.GetAsyncKeyState(0x10) & 0x8000) != 0;
            if (vk >= 0x41 && vk <= 0x5A && (Native.GetKeyState(Native.VK_CAPITAL) & 1) != 0) shift = !shift;
            char c = Converter.KeyChar(vk, shift);
            if (c == '\0') { keys.Clear(); thai.Clear(); trailing = 0; return; }

            if (trailing > 0) { keys.Clear(); thai.Clear(); trailing = 0; }
            keys.Add(c);
            thai.Add(Native.ForegroundLang() == Native.LANG_THAI);
        }
    }

    public string Describe()
    {
        lock (gate)
        {
            var sb = new StringBuilder();
            foreach (bool t in thai) sb.Append(t ? 'T' : 'E');
            return "keys=" + sb + " spaces=" + trailing;
        }
    }

    // Replacement for the last run typed in one layout: how many chars to erase and what to type.
    public bool Take(out int erase, out string replacement, out bool toThai)
    {
        lock (gate)
        {
            erase = 0; replacement = null; toThai = false;
            if (keys.Count == 0) return false;
            bool lastThai = thai[thai.Count - 1];
            int start = keys.Count;
            while (start > 0 && thai[start - 1] == lastThai) start--;

            var sb = new StringBuilder();
            for (int i = start; i < keys.Count; i++)
                sb.Append(lastThai ? keys[i] : Converter.EngToThai(keys[i]));
            sb.Append(' ', trailing);

            erase = keys.Count - start + trailing;
            replacement = sb.ToString();
            toThai = !lastThai;
            keys.Clear(); thai.Clear(); trailing = 0;
            return true;
        }
    }
}

class MainForm : Form
{
    readonly WordTracker tracker = new WordTracker();
    readonly Native.HookProc keyProc, mouseProc;
    IntPtr keyHook, mouseHook;
    NotifyIcon tray;
    bool swallowSpaceUp;
    int busy;
    int keysSeen;

    public MainForm()
    {
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        Opacity = 0;
        keyProc = KeyHook;
        mouseProc = MouseHook;

        var menu = new ContextMenuStrip();
        menu.Items.Add("LangFix: Ctrl+Shift+Space").Enabled = false;
        menu.Items.Add("Exit", null, (s, e) => Application.Exit());
        Icon appIcon;
        try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { appIcon = SystemIcons.Application; }
        tray = new NotifyIcon { Icon = appIcon, Text = "LangFix (Ctrl+Shift+Space)", ContextMenuStrip = menu, Visible = true };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Visible = false;
        IntPtr mod = Native.GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName);
        keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyProc, mod, 0);
        mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, mod, 0);
        if (keyHook == IntPtr.Zero)
        {
            MessageBox.Show("Could not start the keyboard hook.", "LangFix");
            Application.Exit();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyHook);
        if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
        if (tray != null) { tray.Visible = false; tray.Dispose(); }
        base.Dispose(disposing);
    }

    IntPtr KeyHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
            int msg = wParam.ToInt32();
            bool down = msg == 0x100 || msg == 0x104;
            bool up = msg == 0x101 || msg == 0x105;

            if (k.extra != Native.OurTag)
            {
                if (up && k.vkCode == Native.VK_SPACE && swallowSpaceUp)
                {
                    swallowSpaceUp = false;
                    return (IntPtr)1;
                }
                if (down)
                {
                    bool mods = (Native.GetAsyncKeyState(0x11) & 0x8000) != 0 && (Native.GetAsyncKeyState(0x10) & 0x8000) != 0;
                    if (k.vkCode == Native.VK_SPACE && mods)
                    {
                        Native.Log("hotkey; keysSeen=" + keysSeen + " " + tracker.Describe() + " lang=0x" + Native.ForegroundLang().ToString("X4"));
                        swallowSpaceUp = true;
                        if (Interlocked.CompareExchange(ref busy, 1, 0) == 0)
                        {
                            var t = new Thread(Fix);
                            t.SetApartmentState(ApartmentState.STA);
                            t.IsBackground = true;
                            t.Start();
                        }
                        return (IntPtr)1;
                    }
                    int vk = (int)k.vkCode;
                    // Shift/Ctrl/Alt/Win/CapsLock presses on their own say nothing about the word.
                    bool modifier = vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x14
                        || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C;
                    if (!modifier) { keysSeen++; tracker.OnKey(vk, Native.GetForegroundWindow()); }
                }
            }
        }
        return Native.CallNextHookEx(keyHook, code, wParam, lParam);
    }

    IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int m = wParam.ToInt32();
            if (m == 0x201 || m == 0x204 || m == 0x207) tracker.Clear();
        }
        return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    void Fix()
    {
        try
        {
            int erase; string text; bool toThai;
            var batch = new List<Native.INPUT>();
            Native.AddReleaseModifiers(batch);

            if (tracker.Take(out erase, out text, out toThai))
            {
                Native.Log("word mode: erase=" + erase + " toThai=" + toThai);
                for (int i = 0; i < erase; i++) Native.AddKey(batch, Native.VK_BACK);
                foreach (char c in text) AddChar(batch, c);
                Native.Send(batch);
                Native.SwitchLayout(toThai ? Native.LANG_THAI : Native.LANG_ENGLISH);
                return;
            }

            // Nothing typed since the last click/shortcut: convert the selected text, if any.
            Native.Send(batch);
            string selected = CopySelection();
            Native.Log("selection mode: copied " + (selected == null ? 0 : selected.Length) + " chars");
            if (string.IsNullOrEmpty(selected)) return;
            string converted = Converter.Convert(selected);
            bool selToThai = !Converter.HasThai(selected);

            batch.Clear();
            foreach (char c in converted) AddChar(batch, c);
            Native.Send(batch);
            Native.SwitchLayout(selToThai ? Native.LANG_THAI : Native.LANG_ENGLISH);
        }
        catch (Exception ex) { Native.Log("error: " + ex.Message); }
        finally { Interlocked.Exchange(ref busy, 0); }
    }

    static void AddChar(List<Native.INPUT> batch, char c)
    {
        if (c == '\r') return;
        if (c == '\n') Native.AddKey(batch, 0x0D);
        else Native.AddUnicode(batch, c);
    }

    // Ctrl+C, then see whether the clipboard changed (sequence number): "nothing selected" copies nothing.
    // Plain Win32 clipboard calls only: the OLE clipboard can stall the app we are copying from.
    static string CopySelection()
    {
        string saved = Native.ReadClipboardText();
        uint seq = Native.GetClipboardSequenceNumber();

        var l = new List<Native.INPUT>();
        l.Add(Native.Vk(Native.VK_CONTROL, false));
        Native.AddKey(l, (byte)'C');
        l.Add(Native.Vk(Native.VK_CONTROL, true));
        Native.Send(l);

        for (int i = 0; i < 25; i++)
        {
            Thread.Sleep(10);
            if (Native.GetClipboardSequenceNumber() == seq) continue;
            string t = Native.ReadClipboardText();
            if (t == null) continue;
            if (saved != null) Native.WriteClipboardText(saved);
            return t;
        }
        return null;
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--selftest")
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine(Converter.Convert(args[1]));
            return;
        }
        bool created;
        using (var mutex = new Mutex(true, "LangFix-SingleInstance", out created))
        {
            if (!created) return;
            Application.EnableVisualStyles();
            Application.Run(new MainForm());
        }
    }
}
