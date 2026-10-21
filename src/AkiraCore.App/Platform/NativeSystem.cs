using System.Runtime.InteropServices;
using System.Text;

namespace AkiraCore.App.Platform;

/// <summary>
/// P/Invoke do Windows usado pela Fase 2 e 3: atalhos globais, programa em primeiro plano,
/// bloqueio e tela, entrada simulada (texto e multimídia das teclas com ação do app),
/// captura de tela reduzida e uso da CPU.
/// </summary>
internal static class NativeSystem
{
    // --- Atalhos globais -------------------------------------------------------------------

    public const int WmHotkey = 0x0312;
    public const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr window, int id);

    // --- Programa em primeiro plano -----------------------------------------------------------

    public const uint EventSystemForeground = 0x0003;
    public const uint WinEventOutOfContext = 0x0000;
    public const uint ProcessQueryLimitedInformation = 0x1000;

    public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId,
        uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback,
        uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);

    // --- Bloqueio e tela -----------------------------------------------------------------------

    public const int WmWtsSessionChange = 0x02B1;
    public const int WtsSessionLock = 0x7;
    public const int WtsSessionUnlock = 0x8;
    public const int NotifyForThisSession = 0;
    public const int WmPowerBroadcast = 0x0218;
    public const int PbtPowerSettingChange = 0x8013;
    public const int PbtApmResumeAutomatic = 0x0012;
    public const int DeviceNotifyWindowHandle = 0;
    // GUID_CONSOLE_DISPLAY_STATE: 0 tela desligada, 1 ligada, 2 escurecida
    public static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    [StructLayout(LayoutKind.Sequential)]
    public struct PowerBroadcastSetting
    {
        public Guid PowerSetting;
        public uint DataLength;
        public uint Data;
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSRegisterSessionNotification(IntPtr window, int flags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSUnRegisterSessionNotification(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LockWorkStation();

    // --- Entrada simulada ------------------------------------------------------------------------

    public const uint InputKeyboard = 1;
    public const uint KeyEventExtendedKey = 0x0001;
    public const uint KeyEventKeyUp = 0x0002;
    public const uint KeyEventUnicode = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    // INPUT tem uma união; o maior membro (MOUSEINPUT) define o tamanho, por isso o preenchimento
    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public uint Type;
        public KeyboardInput Keyboard;
        private readonly long _padding;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    // --- Captura de tela reduzida ------------------------------------------------------------------

    public const int HalfTone = 4;
    public const uint SrcCopy = 0x00CC0020;
    public const int SmXVirtualScreen = 76;
    public const int SmYVirtualScreen = 77;
    public const int SmCxVirtualScreen = 78;
    public const int SmCyVirtualScreen = 79;
    public const int SmCxScreen = 0;
    public const int SmCyScreen = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    public static extern int SetStretchBltMode(IntPtr dc, int mode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool StretchBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source,
        int sourceX, int sourceY, int sourceWidth, int sourceHeight, uint operation);

    [DllImport("gdi32.dll")]
    public static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits,
        ref BitmapInfoHeader info, uint usage);

    // --- CPU -----------------------------------------------------------------------------------------

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    // --- Console do terminal que chamou (linha de comando) ---------------------------------------------

    public const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachConsole(int processId);
}
