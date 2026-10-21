using System.Runtime.InteropServices;
using AkiraCore.Core.Diagnostics;
using static AkiraCore.App.Hid.NativeMethods;

namespace AkiraCore.App.Hid;

/// <summary>Uma tecla do AKIRA vista pelo Raw Input.</summary>
/// <param name="ScanCode">Make code do conjunto 1, com 0xE000 somado quando vem com prefixo E0.</param>
public sealed record RawKey(string DeviceName, int ScanCode, ushort VirtualKey, bool Released)
{
    /// <summary>Linha do monitor do Laboratório.</summary>
    public string Describe()
    {
        var e0 = ScanCode >= 0xE000 ? " E0" : "";
        // Keys é enum de flags: um código não definido viraria uma soma enganosa de nomes
        var name = Enum.IsDefined((Keys)VirtualKey) ? ((Keys)VirtualKey).ToString() : "?";
        // VKey 0x10/0x11 é Shift/Ctrl genérico; o make code diz o lado (0x2A esq., 0x36 dir.)
        return $"{DateTime.Now:HH:mm:ss.fff} TECLA {HidPathInfo.InterfaceLabel(DeviceName)}  " +
               $"{name} (vk=0x{VirtualKey:X2} make=0x{ScanCode & 0xFF:X2}{e0}) " +
               (Released ? "solta" : "apertada");
    }
}

/// <summary>
/// Entrega cada tecla que o AKIRA manda ao PC, via Raw Input. Usado pela prévia (as teclas
/// tocadas acendem na tela) e pelo monitor do Laboratório, que responde à pergunta
/// "o atalho das fitas chega ao computador ou o firmware o consome?".
/// Fn nunca aparece: é tratado dentro do teclado. Filtra por VID/PID: teclas de outros
/// teclados são ignoradas.
/// O Windows aceita um único destino de Raw Input de teclado por processo, por isso há
/// uma instância só, presa à janela principal, e cada interessado pede uma escuta com
/// <see cref="Listen"/>: o registro fica ativo enquanto houver alguma.
/// </summary>
public sealed class RawKeyboardMonitor : IDisposable
{
    public const int WindowMessage = WmInput;

    private readonly Dictionary<IntPtr, string?> _deviceNames = [];
    private IntPtr _window;
    private int _listeners;

    public event Action<RawKey>? KeyReceived;

    /// <summary>Janela que recebe o WM_INPUT; o WndProc dela chama <see cref="Process"/>.</summary>
    public void Attach(IntPtr windowHandle) => _window = windowHandle;

    /// <summary>
    /// Liga a escuta (mesmo com a janela em segundo plano) até o retorno ser descartado.
    /// Null se o Windows recusar o registro.
    /// </summary>
    public IDisposable? Listen()
    {
        if (_listeners == 0 && (_window == IntPtr.Zero || !Register(RidevInputSink, _window)))
        {
            return null;
        }
        _listeners++;
        return new Subscription(this);
    }

    private void Release()
    {
        if (_listeners > 0 && --_listeners == 0)
        {
            Register(RidevRemove, IntPtr.Zero);
        }
    }

    /// <summary>Chamado pelo WndProc da janela registrada a cada WM_INPUT.</summary>
    public void Process(IntPtr rawInputHandle)
    {
        if (_listeners == 0)
        {
            return;
        }

        var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        uint size = 0;
        GetRawInputData(rawInputHandle, RidInput, IntPtr.Zero, ref size, headerSize);
        if (size == 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            var copied = GetRawInputData(rawInputHandle, RidInput, buffer, ref size, headerSize);
            if (copied == 0 || copied == uint.MaxValue)
            {
                return;
            }

            var header = Marshal.PtrToStructure<RawInputHeader>(buffer);
            // Device zero = tecla injetada por software (SendInput), não veio do teclado
            if (header.Type != RimTypeKeyboard || header.Device == IntPtr.Zero)
            {
                return;
            }

            var name = DeviceName(header.Device);
            if (name is null || !HidDeviceFinder.KnownDevices.Any(p => p.Matches(name)))
            {
                return;
            }

            var key = Marshal.PtrToStructure<RawKeyboard>(buffer + (int)headerSize);
            var scanCode = key.MakeCode | ((key.Flags & RiKeyE0) != 0 ? 0xE000 : 0);
            KeyReceived?.Invoke(new RawKey(name, scanCode, key.VKey, (key.Flags & RiKeyBreak) != 0));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private string? DeviceName(IntPtr device)
    {
        if (_deviceNames.TryGetValue(device, out var cached))
        {
            return cached;
        }

        uint chars = 0;
        GetRawInputDeviceInfo(device, RidiDeviceName, IntPtr.Zero, ref chars);
        string? name = null;
        if (chars > 0)
        {
            var buffer = Marshal.AllocHGlobal((int)chars * sizeof(char));
            try
            {
                if (GetRawInputDeviceInfo(device, RidiDeviceName, buffer, ref chars) is > 0 and < uint.MaxValue)
                {
                    name = Marshal.PtrToStringUni(buffer);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        _deviceNames[device] = name;
        return name;
    }

    private static bool Register(uint flags, IntPtr target)
    {
        RawInputDevice[] devices =
        [
            new() { UsagePage = 0x01, Usage = 0x06, Flags = flags, Target = target }, // teclado
        ];
        return RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    public void Dispose()
    {
        if (_listeners > 0)
        {
            Register(RidevRemove, IntPtr.Zero);
            _listeners = 0;
        }
    }

    private sealed class Subscription(RawKeyboardMonitor owner) : IDisposable
    {
        private RawKeyboardMonitor? _owner = owner;

        public void Dispose()
        {
            _owner?.Release();
            _owner = null;
        }
    }
}
