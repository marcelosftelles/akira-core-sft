using System.Runtime.InteropServices;
using AkiraCore.Core.Diagnostics;
using static AkiraCore.App.Hid.NativeMethods;

namespace AkiraCore.App.Hid;

/// <summary>
/// Mostra cada tecla que o AKIRA entrega ao PC, via Raw Input. Responde à pergunta
/// "o atalho das fitas chega ao computador ou o firmware o consome?".
/// Fn nunca aparece: é tratado dentro do teclado. Se a combinação não gerar
/// nenhuma linha TECLA, o firmware engoliu tudo e não há o que capturar no PC.
/// Filtra por VID/PID: teclas de outros teclados são ignoradas.
/// </summary>
public sealed class RawKeyboardMonitor : IDisposable
{
    public const int WindowMessage = WmInput;

    private readonly Dictionary<IntPtr, string?> _deviceNames = [];
    private bool _registered;

    public event Action<string>? KeyReceived;

    /// <summary>Registra a janela para receber WM_INPUT do teclado, mesmo sem foco.</summary>
    public bool Start(IntPtr windowHandle)
    {
        _registered = Register(RidevInputSink, windowHandle);
        return _registered;
    }

    /// <summary>Chamado pelo WndProc da janela registrada a cada WM_INPUT.</summary>
    public void Process(IntPtr rawInputHandle)
    {
        if (!_registered)
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
            KeyReceived?.Invoke(Describe(name, key));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string Describe(string deviceName, RawKeyboard key)
    {
        var released = (key.Flags & RiKeyBreak) != 0;
        var e0 = (key.Flags & RiKeyE0) != 0 ? " E0" : "";
        // Keys é enum de flags: um código não definido viraria uma soma enganosa de nomes
        var name = Enum.IsDefined((Keys)key.VKey) ? ((Keys)key.VKey).ToString() : "?";
        // VKey 0x10/0x11 é Shift/Ctrl genérico; o make code diz o lado (0x2A esq., 0x36 dir.)
        return $"{DateTime.Now:HH:mm:ss.fff} TECLA {HidPathInfo.InterfaceLabel(deviceName)}  " +
               $"{name} (vk=0x{key.VKey:X2} make=0x{key.MakeCode:X2}{e0}) " +
               (released ? "solta" : "apertada");
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
        if (_registered)
        {
            Register(RidevRemove, IntPtr.Zero);
            _registered = false;
        }
    }
}
