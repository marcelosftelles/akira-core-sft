using System.ComponentModel;
using System.Diagnostics;
using AkiraCore.App.Hid;
using AkiraCore.Core;
using AkiraCore.Core.Links;
using AkiraCore.Core.Protocol;

namespace AkiraCore.App;

/// <summary>
/// Prévia da página de iluminação como a do oficial. Pelo cabo, com a página aberta e nenhum
/// envio em andamento, ele lê a cor que cada LED mostra agora (04 F5, fcn.0042adb0) e pinta o
/// desenho: é um espelho do teclado, então qualquer efeito aparece como ele é de verdade. No
/// dongle o oficial não lê nada e a prévia fica parada na cor escolhida (isso fica na página).
/// </summary>
public sealed partial class MainForm
{
    // Página que a interface mostra (mensagem "view"); o espelho só roda na de iluminação,
    // como o laço do oficial só chama fcn.0042adb0 na página 3
    private string _view = "lighting";
    private bool _mirrorRunning;
    // Caminho do cabo resolvido uma vez por conexão: varrer o HID a cada quadro custaria caro
    private string? _mirrorPath;
    private bool _mirrorPathKnown;

    // O Laboratório envia sem passar pelo semáforo desta janela: com ele aberto o espelho para,
    // para uma leitura nunca se misturar a um comando dele
    private bool MirrorWanted =>
        _webReady && Visible && WindowState != FormWindowState.Minimized && _view == "lighting" && !_simulate
        && _labForm is not { IsDisposed: false };

    /// <summary>Teclado conectado ou desconectado: o próximo quadro procura o cabo de novo.</summary>
    private void ForgetMirrorDevice()
    {
        _mirrorPathKnown = false;
        UpdateMirror();
    }

    /// <summary>Liga o laço quando as condições aparecem; ele mesmo para quando somem.</summary>
    private async void UpdateMirror()
    {
        if (_mirrorRunning || !MirrorWanted)
        {
            return;
        }
        _mirrorRunning = true;
        try
        {
            while (MirrorWanted && !IsDisposed)
            {
                if (!_mirrorPathKnown)
                {
                    var device = HidDeviceFinder.FindCommandInterface();
                    _mirrorPath = device?.Kind == ChannelKind.Feature ? device.Path : null;
                    _mirrorPathKnown = true;
                }
                if (_mirrorPath is null)
                {
                    break; // dongle ou nada conectado: a página mostra a prévia parada
                }
                var started = Stopwatch.StartNew();
                await ReadMirrorFrameAsync(_mirrorPath);
                // A thread do oficial roda no mínimo a cada 30 ms (fcn.004169b0)
                var rest = LedReadProtocol.MinFramePeriod - started.Elapsed;
                if (rest > TimeSpan.Zero)
                {
                    await Task.Delay(rest);
                }
            }
        }
        finally
        {
            _mirrorRunning = false;
            if (!IsDisposed)
            {
                Post(new { type = "mirror", colors = (int[]?)null }); // a página volta à prévia parada
            }
        }
    }

    private async Task ReadMirrorFrameAsync(string path)
    {
        // O oficial só lê com a fila de comandos vazia: envio em andamento tem prioridade
        if (!await _keyboardLock.WaitAsync(0))
        {
            return;
        }
        try
        {
            var swapRedBlue = _settings.SwapRedBlue;
            var colors = await Task.Run(() =>
            {
                Thread.Sleep(LedReadProtocol.FrameDelay); // Sleep(10) no começo de fcn.0042adb0
                using var link = new FeatureLink(new HidFeaturePort(path));
                return new KeyboardClient(link).ReadLedColors(swapRedBlue);
            });
            if (colors is not null && MirrorWanted)
            {
                Post(new { type = "mirror", colors = colors.Select(c => (c.R << 16) | (c.G << 8) | c.B) });
            }
        }
        catch (Exception ex) when (ex is KeyboardException or Win32Exception or IOException)
        {
            // Cabo desconectado no meio da leitura: procura de novo no próximo quadro
            Debug.WriteLine($"Espelho dos LEDs sem resposta: {ex.Message}");
            _mirrorPathKnown = false;
        }
        finally
        {
            // Saindo pela bandeja no meio de uma leitura: o semáforo já foi descartado
            if (!IsDisposed)
            {
                _keyboardLock.Release();
            }
        }
    }
}
