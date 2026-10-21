using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AkiraCore.Core.Keys;
using static AkiraCore.App.Platform.NativeSystem;

namespace AkiraCore.App.Platform;

/// <summary>
/// Executa a ação das teclas "do app" quando o atalho reservado (F13 a F24 com modificadores) chega.
/// O teclado ainda segura Ctrl, Alt e Shift nesse instante: texto e multimídia esperam soltar,
/// senão o Windows leria um atalho (Ctrl + Alt + letra) em vez da letra.
/// </summary>
public sealed class AppActionRunner(Action<string, string> notify)
{
    private static readonly TimeSpan ShutdownDelay = TimeSpan.FromSeconds(30);
    private DateTime? _shutdownUntil;

    /// <param name="kind">Tipo de <see cref="AppKeyActions"/>.</param>
    /// <param name="target">Programa, site, texto ou tecla multimídia.</param>
    public void Run(string? kind, string? target)
    {
        try
        {
            switch (kind)
            {
                case AppKeyActions.Open:
                case AppKeyActions.Site:
                    Process.Start(new ProcessStartInfo(target!.Trim()) { UseShellExecute = true })?.Dispose();
                    break;
                case AppKeyActions.Text:
                    var text = target ?? "";
                    Task.Run(() => TypeText(text));
                    break;
                case AppKeyActions.Media:
                    var vk = AppKeyActions.MediaKeys.FirstOrDefault(m => m.Key == target)?.Value;
                    if (vk is { } key)
                    {
                        Task.Run(() => PressVirtualKey(key));
                    }
                    break;
                case AppKeyActions.Lock:
                    LockWorkStation();
                    break;
                case AppKeyActions.Sleep:
                    Application.SetSuspendState(PowerState.Suspend, force: false, disableWakeEvent: false);
                    break;
                case AppKeyActions.Shutdown:
                    ToggleShutdown();
                    break;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            notify("Ação da tecla falhou", ex.Message);
        }
    }

    /// <summary>
    /// Desligar tem 30 s de margem e cancela se a tecla for apertada de novo: uma tecla esbarrada
    /// não pode apagar o trabalho aberto.
    /// </summary>
    private void ToggleShutdown()
    {
        if (_shutdownUntil is { } until && DateTime.Now < until)
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", "/a") { CreateNoWindow = true, UseShellExecute = false })?.Dispose();
            _shutdownUntil = null;
            notify("Desligamento cancelado", "O computador continua ligado.");
            return;
        }
        Process.Start(new ProcessStartInfo("shutdown.exe", $"/s /t {(int)ShutdownDelay.TotalSeconds}")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })?.Dispose();
        _shutdownUntil = DateTime.Now + ShutdownDelay;
        notify("O computador vai desligar em 30 s", "Aperte a mesma tecla de novo para cancelar.");
    }

    private static void TypeText(string text)
    {
        WaitModifiersReleased();
        var inputs = new List<Input>();
        foreach (var c in text.Replace("\r\n", "\n"))
        {
            if (c == '\n')
            {
                inputs.Add(Key(0x0D, 0, 0));
                inputs.Add(Key(0x0D, 0, KeyEventKeyUp));
                continue;
            }
            // KEYEVENTF_UNICODE digita o caractere direto, em qualquer layout do Windows
            inputs.Add(Key(0, c, KeyEventUnicode));
            inputs.Add(Key(0, c, KeyEventUnicode | KeyEventKeyUp));
        }
        Send(inputs);
    }

    private static void PressVirtualKey(int virtualKey)
    {
        WaitModifiersReleased();
        Send([Key((ushort)virtualKey, 0, KeyEventExtendedKey), Key((ushort)virtualKey, 0, KeyEventExtendedKey | KeyEventKeyUp)]);
    }

    private static void WaitModifiersReleased()
    {
        int[] modifiers = [0x10, 0x11, 0x12, 0x5B, 0x5C]; // Shift, Ctrl, Alt, Win esquerdo e direito
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && modifiers.Any(vk => (GetAsyncKeyState(vk) & 0x8000) != 0))
        {
            Thread.Sleep(15);
        }
    }

    private static Input Key(ushort virtualKey, ushort scan, uint flags) => new()
    {
        Type = InputKeyboard,
        Keyboard = new KeyboardInput { VirtualKey = virtualKey, ScanCode = scan, Flags = flags },
    };

    private static void Send(List<Input> inputs)
    {
        // Em blocos: SendInput com milhares de eventos pode ser cortado por outra entrada no meio
        foreach (var chunk in inputs.Chunk(64))
        {
            SendInput((uint)chunk.Length, chunk, Marshal.SizeOf<Input>());
            Thread.Sleep(5);
        }
    }
}
