using System.ComponentModel;
using AkiraCore.App.Live;
using AkiraCore.Core;
using AkiraCore.Core.Diagnostics;
using AkiraCore.Core.Keys;
using AkiraCore.Core.Lighting;
using AkiraCore.Core.Links;
using AkiraCore.Core.Live;
using AkiraCore.Core.Protocol;

namespace AkiraCore.App;

/// <summary>
/// Seção 6 do Laboratório: os comandos da Fase 2, para confirmar no teclado o que veio da
/// engenharia reversa sem teste em hardware (04 F5, 04 20, 0B no dongle e o 1F).
/// </summary>
public sealed partial class LabForm
{
    private readonly SemaphoreSlim _keyboardLock;
    private readonly Action<DongleMusicCalibration> _saveCalibration;
    private readonly Action _restoreLighting;
    private readonly NumericUpDown _musicColumns = new() { Minimum = 1, Maximum = MusicProtocol.MaxColumns, Width = 60 };
    private readonly NumericUpDown _musicMaxHeight = new() { Minimum = 1, Maximum = 255, Width = 60 };
    private readonly NumericUpDown _musicFront = new() { Minimum = 0, Maximum = 255, Width = 60 };
    private readonly NumericUpDown _musicBack = new() { Minimum = 0, Maximum = 255, Width = 60 };
    private IReadOnlyDictionary<int, Rgb>? _lastColors;

    private void LoadCalibration(DongleMusicCalibration calibration)
    {
        var c = calibration.Sanitized();
        _musicColumns.Value = c.Columns;
        _musicMaxHeight.Value = c.MaxHeight;
        _musicFront.Value = c.FrontMode;
        _musicBack.Value = c.BackMode;
    }

    private DongleMusicCalibration CurrentCalibration() => new()
    {
        Columns = (int)_musicColumns.Value,
        MaxHeight = (int)_musicMaxHeight.Value,
        FrontMode = (int)_musicFront.Value,
        BackMode = (int)_musicBack.Value,
    };

    private void BuildPhase2Section(Control parent)
    {
        var readColors = new Button { Text = "Ler cores do teclado (04 F5)", AutoSize = true };
        var testFrame = new Button { Text = "Quadro de teste (04 20, 4 s)", AutoSize = true };
        var testMusic = new Button { Text = "Testar barras (0B, 6 s)", AutoSize = true };
        var saveMusic = new Button { Text = "Usar esta calibração", AutoSize = true };
        var keyboardReset = new Button { Text = "Enviar 1F (Reset Keyboard)", AutoSize = true };
        readColors.Click += async (_, _) => await ReadColorsAsync();
        testFrame.Click += async (_, _) => await TestFrameAsync();
        testMusic.Click += async (_, _) => await TestMusicAsync();
        saveMusic.Click += (_, _) =>
        {
            _saveCalibration(CurrentCalibration());
            Log($"Calibração do ritmo no dongle salva: {CurrentCalibration().Columns} colunas, altura até " +
                $"{CurrentCalibration().MaxHeight}, frente {CurrentCalibration().FrontMode}, fundo {CurrentCalibration().BackMode}.");
        };
        keyboardReset.Click += async (_, _) => await SendKeyboardResetAsync();

        parent.Controls.Add(Help(
            "6. Fase 2 (sem teste em hardware). Desligue o modo Ao vivo da janela principal antes. " +
            "Ler cores (só cabo): mostra a cor que cada tecla tem agora e o que mudou desde a leitura " +
            "anterior; leia com um efeito animado e veja se as cores mudam entre leituras. Quadro de " +
            "teste (só cabo): as fileiras acendem do vermelho ao azul e a luz normal volta no fim."));
        parent.Controls.Add(Row(readColors, testFrame));
        parent.Controls.Add(Help(
            "Ritmo musical no dongle: o oficial manda 0B 1C 00 com as alturas das colunas, mas o número " +
            "de colunas e a escala da altura ainda não foram confirmados. O teste sobe uma rampa coluna a " +
            "coluna: se as barras ocuparem o teclado inteiro e subirem até o topo, a calibração está certa."));
        parent.Controls.Add(Row(Caption("Colunas"), _musicColumns, Caption("Altura máx."), _musicMaxHeight,
            Caption("Frente"), _musicFront, Caption("Fundo"), _musicBack, testMusic, saveMusic));
        parent.Controls.Add(Help(
            "1F no dongle: o \"Reset Keyboard\" do oficial (fcn.00414600). No cabo, o mesmo botão do " +
            "oficial regrava as tabelas de teclas; por isso a hipótese é que o 1F zere as teclas " +
            "remapeadas no firmware. Depois do teste, regrave as teclas em Teclas (ou troque de perfil)."));
        parent.Controls.Add(Row(keyboardReset));
    }

    /// <summary>Abre o canal e executa com o teclado reservado (o laço ao vivo e a bateria esperam).</summary>
    private async Task<T?> WithLinkAsync<T>(Func<IKeyboardLink, T> action)
    {
        await _keyboardLock.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                using var link = _openLink(null);
                return action(link);
            });
        }
        catch (Exception ex) when (ex is KeyboardException or Win32Exception or IOException or NotSupportedException)
        {
            Log("Erro: " + ex.Message);
            return default;
        }
        finally
        {
            _keyboardLock.Release();
        }
    }

    private async Task ReadColorsAsync()
    {
        var raw = await WithLinkAsync(link => link.HasColorReadback
            ? link.ReadLedColors()
            : throw new NotSupportedException("A leitura das cores (04 F5) só existe no cabo."));
        if (raw is null)
        {
            return;
        }
        var colors = PacketBuilder.ParseLedColors(raw);
        Log($"{DateTime.Now:HH:mm:ss.fff} 04 F5: {colors.Count} LEDs na resposta. Primeiros bytes: {HexText.Spaced(raw.AsSpan(0, 32).ToArray())}");
        var lines = KeyGeometry.All
            .Where(k => colors.ContainsKey(k.Led))
            .Select(k => $"{k.Name}={ColorMath.ToHex(colors[k.Led])}")
            .Chunk(8)
            .Select(chunk => "  " + string.Join("  ", chunk));
        foreach (var line in lines)
        {
            Log(line);
        }
        var extra = colors.Keys.Where(i => KeyboardLayout.ByIndex(i) is null && colors[i] != default).ToList();
        if (extra.Count > 0)
        {
            Log("  LEDs sem tecla acesos: " + string.Join(", ", extra.Select(i => $"{i}={ColorMath.ToHex(colors[i])}")));
        }
        if (_lastColors is not null)
        {
            var changed = colors.Where(c => _lastColors.TryGetValue(c.Key, out var before) && before != c.Value).Select(c => c.Key).ToList();
            Log(changed.Count == 0
                ? "  Nada mudou desde a leitura anterior."
                : $"  Mudaram desde a leitura anterior: {changed.Count} LEDs ({string.Join(", ", changed.Take(20))}{(changed.Count > 20 ? "…" : "")}).");
        }
        _lastColors = colors;
    }

    private async Task TestFrameAsync()
    {
        Rgb[] rows = [new(0xFF, 0, 0), new(0xFF, 0x6A, 0), new(0xFF, 0xD0, 0), new(0, 0xFF, 0x40), new(0, 0xE5, 0xFF), new(0, 0x40, 0xFF)];
        Log("Enviando quadros de teste pelo 04 20 por 4 s…");
        var sent = await WithLinkAsync(link =>
        {
            if (!link.HasStreaming)
            {
                throw new NotSupportedException("O quadro em tempo real (04 20) só existe no cabo.");
            }
            var client = new KeyboardClient(link);
            var frames = 0;
            var until = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < until)
            {
                var frame = new LiveFrame();
                // A cor desce uma fileira por segundo: mostra que os quadros seguidos chegam
                var shift = (int)(4 - (until - DateTime.UtcNow).TotalSeconds);
                foreach (var key in KeyGeometry.All)
                {
                    frame[key.Led] = rows[(KeyGeometry.RowOf(key) + shift) % rows.Length];
                }
                client.SendLiveFrame(frame);
                frames++;
            }
            return frames;
        });
        if (sent > 0)
        {
            Log($"Quadros enviados: {sent} ({sent / 4.0:0.#} por segundo). Voltando à luz normal.");
            _restoreLighting();
        }
    }

    private async Task TestMusicAsync()
    {
        var calibration = CurrentCalibration();
        Log($"Testando 0B 1C 00 por 6 s: {calibration.Columns} colunas, altura de 0 a {calibration.MaxHeight}…");
        var sent = await WithLinkAsync(link =>
        {
            if (!link.HasMusicReport)
            {
                throw new NotSupportedException("O ritmo musical pelo firmware (0B) só existe no dongle.");
            }
            var client = new KeyboardClient(link);
            var started = DateTime.UtcNow;
            var reports = 0;
            while (DateTime.UtcNow - started < TimeSpan.FromSeconds(6))
            {
                var t = (DateTime.UtcNow - started).TotalSeconds;
                var heights = new byte[calibration.Columns];
                for (var c = 0; c < heights.Length; c++)
                {
                    // Rampa que anda: cada coluna um degrau acima da anterior
                    var level = (c + (int)(t * 4)) % (calibration.MaxHeight + 1);
                    heights[c] = (byte)level;
                }
                client.SendMusicHeights(heights, ProtocolConstants.BrightnessMax, (byte)calibration.FrontMode, (byte)calibration.BackMode);
                reports++;
                Thread.Sleep(40);
            }
            return reports;
        });
        if (sent > 0)
        {
            Log($"Reports enviados: {sent}. Voltando à luz normal.");
            _restoreLighting();
        }
    }

    private async Task SendKeyboardResetAsync()
    {
        var answer = MessageBox.Show(this,
            "Enviar o 1F do oficial pelo dongle? Pela análise, ele deve zerar as teclas remapeadas guardadas " +
            "no teclado (não é o reset de fábrica 0F). Depois, regrave as teclas pelo app.",
            "Comando 1F", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (answer != DialogResult.OK)
        {
            return;
        }
        var done = await WithLinkAsync(link =>
        {
            if (!link.HasKeyboardReset)
            {
                throw new NotSupportedException("O 1F só existe no dongle; no cabo o oficial só regrava as teclas.");
            }
            link.SendKeyboardReset();
            return true;
        });
        if (done)
        {
            Log($"{DateTime.Now:HH:mm:ss} 1F confirmado pelo dongle. Teste as teclas remapeadas e anote o que mudou.");
        }
    }
}
