using AkiraCore.App.Hid;
using AkiraCore.Core;
using AkiraCore.Core.Lighting;
using AkiraCore.Core.Links;
using AkiraCore.Core.Protocol;

namespace AkiraCore.App;

/// <summary>
/// Ferramentas para descobrir comandos que o app oficial não expõe:
/// monitor de avisos do firmware e envio de blocos experimentais.
/// Só mexe em iluminação: tudo é desfeito reaplicando um efeito normal.
/// </summary>
public sealed class LabForm : Form
{
    private readonly Func<IKeyboardLink> _openLink;
    private HidInputMonitor? _monitor;

    private readonly Button _monitorButton = new() { Text = "Iniciar monitor", AutoSize = true };
    private readonly TextBox _blockBox = new() { Width = 420, Font = new Font("Consolas", 10f) };
    private readonly ComboBox _targetCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly ComboBox _sidePresetCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    // Varredura: repete o bloco base trocando um byte por vez, com pausa para observar
    private readonly NumericUpDown _sweepIndex = new() { Minimum = 0, Maximum = 13, Value = 0, Width = 60 };
    private readonly NumericUpDown _sweepFrom = new() { Minimum = 0, Maximum = 255, Value = 0, Hexadecimal = true, Width = 60 };
    private readonly NumericUpDown _sweepTo = new() { Minimum = 0, Maximum = 255, Value = 0x0F, Hexadecimal = true, Width = 60 };
    private readonly NumericUpDown _sweepSeconds = new() { Minimum = 1, Maximum = 10, Value = 3, Width = 60 };
    private readonly Button _sweepButton = new() { Text = "Iniciar varredura", AutoSize = true };
    private readonly Label _sweepNow = new() { AutoSize = true, Font = new Font("Segoe UI", 14f, FontStyle.Bold) };
    private CancellationTokenSource? _sweepCts;

    // Localizador de LED: acende um índice por vez no modo personalizado
    private readonly NumericUpDown _ledIndex = new() { Minimum = 1, Maximum = PerKeyProtocol.MaxLedIndex, Value = 14, Width = 70 };
    private readonly ComboBox _ledColor = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly Button _ledSweepUnmapped = new() { Text = "Varrer LEDs sem tecla", AutoSize = true };
    private readonly Button _ledSweepAll = new() { Text = "Varrer todos (1 a 142)", AutoSize = true };

    private readonly TextBox _log = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
        Width = 720, Height = 300, Font = new Font("Consolas", 9f),
    };

    public LabForm(Func<IKeyboardLink> openLink)
    {
        _openLink = openLink;
        Text = "AKIRA CORE · Laboratório";
        Font = new Font("Segoe UI", 10f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        _blockBox.Text = "00 00 00 00 00 00 00 00 00 00 00 00 00 00 AA 55";
        _targetCombo.Items.AddRange(["Teclas (dongle 05 10 00)", "Fitas (dongle 05 01 00)"]);
        _targetCombo.SelectedIndex = 1;
        _sidePresetCombo.Items.AddRange([.. SideLightCatalog.All]);
        _sidePresetCombo.SelectedIndex = 0;

        var monitorHelp = new Label
        {
            AutoSize = true, MaximumSize = new Size(720, 0),
            Text = "1. Inicie o monitor, aperte cada atalho de luz do teclado algumas vezes, " +
                   "com uma pausa entre eles, e copie o log.",
        };
        var blockHelp = new Label
        {
            AutoSize = true, MaximumSize = new Size(720, 0),
            Text = "2. Bloco experimental (16 bytes em hex). A assinatura AA 55 é sempre forçada. " +
                   "No cabo o destino não muda nada: vai na transação 0x13.",
        };

        var sendBlock = new Button { Text = "Enviar bloco", AutoSize = true };
        var sendPreset = new Button { Text = "Enviar predefinição lateral oficial", AutoSize = true };
        var clear = new Button { Text = "Limpar log", AutoSize = true };
        var copy = new Button { Text = "Copiar log", AutoSize = true };

        _monitorButton.Click += (_, _) => ToggleMonitor();
        sendBlock.Click += async (_, _) => await SendAsync(BuildBlockFromText, SelectedTarget);
        sendPreset.Click += async (_, _) =>
            await SendAsync(() => PacketBuilder.SideLightBlock(((SideLightMode)_sidePresetCombo.SelectedItem!).Value),
                LightTarget.SideStrips);
        clear.Click += (_, _) => _log.Clear();
        copy.Click += (_, _) => { if (_log.TextLength > 0) Clipboard.SetText(_log.Text); };

        var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
        root.Controls.Add(monitorHelp);
        root.Controls.Add(Row(_monitorButton, clear, copy));
        root.Controls.Add(blockHelp);
        root.Controls.Add(Row(_blockBox, _targetCombo, sendBlock));
        root.Controls.Add(Row(_sidePresetCombo, sendPreset));

        var sweepHelp = new Label
        {
            AutoSize = true, MaximumSize = new Size(720, 0),
            Text = "3. Varredura: usa o bloco acima como base e troca o byte escolhido pelos valores " +
                   "da faixa, um por vez. Observe o teclado e anote o valor mostrado em destaque.",
        };
        _sweepButton.Click += async (_, _) => await ToggleSweepAsync();
        root.Controls.Add(sweepHelp);
        root.Controls.Add(Row(
            new Label { Text = "Byte", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, _sweepIndex,
            new Label { Text = "de 0x", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, _sweepFrom,
            new Label { Text = "até 0x", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, _sweepTo,
            new Label { Text = "segundos", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, _sweepSeconds,
            _sweepButton));
        root.Controls.Add(_sweepNow);

        var ledHelp = new Label
        {
            AutoSize = true, MaximumSize = new Size(720, 0),
            Text = "4. Localizador de LED: liga o modo personalizado e acende um único LED. " +
                   "As teclas apagam durante o teste; para voltar, aplique um efeito na janela principal.",
        };
        _ledColor.Items.AddRange(["Branco", "Vermelho", "Verde", "Azul"]);
        _ledColor.SelectedIndex = 0;
        var lightOne = new Button { Text = "Acender só este LED", AutoSize = true };
        lightOne.Click += async (_, _) => await LightSingleLedAsync((int)_ledIndex.Value);
        _ledSweepUnmapped.Click += async (_, _) => await ToggleLedSweepAsync(LedMap.UnmappedIndices);
        _ledSweepAll.Click += async (_, _) =>
            await ToggleLedSweepAsync(Enumerable.Range(1, PerKeyProtocol.MaxLedIndex).ToList());
        root.Controls.Add(ledHelp);
        root.Controls.Add(Row(
            new Label { Text = "LED", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, _ledIndex,
            _ledColor, lightOne, _ledSweepUnmapped, _ledSweepAll));
        root.Controls.Add(_log);
        Controls.Add(root);
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 8) };
        row.Controls.AddRange(controls);
        return row;
    }

    private LightTarget SelectedTarget => _targetCombo.SelectedIndex == 1 ? LightTarget.SideStrips : LightTarget.Keys;

    private byte[] BuildBlockFromText()
    {
        var hex = new string(_blockBox.Text.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length != ProtocolConstants.LightBlockLength * 2)
        {
            throw new KeyboardException("O bloco precisa ter exatamente 16 bytes em hexadecimal.");
        }
        return Convert.FromHexString(hex);
    }

    private void Log(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(line));
            return;
        }
        _log.AppendText(line + Environment.NewLine);
    }

    private void ToggleMonitor()
    {
        if (_monitor is not null)
        {
            _monitor.Dispose();
            _monitor = null;
            _monitorButton.Text = "Iniciar monitor";
            Log("Monitor parado.");
            return;
        }

        _monitor = new HidInputMonitor();
        _monitor.ReportReceived += Log;
        var opened = _monitor.Start();
        Log(opened.Count == 0
            ? "Nenhuma coleção de fabricante encontrada. O teclado está conectado?"
            : "Escutando: " + string.Join(", ", opened));
        _monitorButton.Text = "Parar monitor";
    }

    private async Task SendAsync(Func<byte[]> buildBlock, LightTarget target)
    {
        try
        {
            var block = buildBlock();
            Log($"Enviando {Convert.ToHexString(block)} para {target}");
            await Task.Run(() =>
            {
                using var link = _openLink();
                new KeyboardClient(link).SendExperimentalBlock(block, target);
            });
            Log("Confirmado pelo teclado.");
        }
        catch (Exception ex) when (ex is KeyboardException or ArgumentException
                                       or System.ComponentModel.Win32Exception)
        {
            Log("Erro: " + ex.Message);
        }
    }

    private Rgb SelectedLedColor => _ledColor.SelectedIndex switch
    {
        1 => new Rgb(255, 0, 0),
        2 => new Rgb(0, 255, 0),
        3 => new Rgb(0, 0, 255),
        _ => new Rgb(255, 255, 255),
    };

    private async Task<bool> LightSingleLedAsync(int index)
    {
        var frame = new PerKeyFrame().Set(index, SelectedLedColor);
        try
        {
            await Task.Run(() =>
            {
                using var link = _openLink();
                new KeyboardClient(link).ApplyPerKeyColors(frame);
            });
            Log($"LED {index} aceso.");
            return true;
        }
        catch (Exception ex) when (ex is KeyboardException or System.ComponentModel.Win32Exception)
        {
            Log("Erro: " + ex.Message);
            return false;
        }
    }

    private async Task ToggleLedSweepAsync(IReadOnlyList<int> indices)
    {
        if (_sweepCts is not null)
        {
            _sweepCts.Cancel();
            return;
        }

        _sweepCts = new CancellationTokenSource();
        var pause = TimeSpan.FromSeconds((double)_sweepSeconds.Value);
        _ledSweepUnmapped.Text = _ledSweepAll.Text = "Parar varredura de LED";
        Log($"Varrendo {indices.Count} LEDs, {pause.TotalSeconds:0} s cada.");

        try
        {
            foreach (var index in indices)
            {
                _sweepNow.Text = $"LED {index}";
                _ledIndex.Value = index;
                if (!await LightSingleLedAsync(index))
                {
                    break;
                }
                await Task.Delay(pause, _sweepCts.Token);
            }
            Log("Varredura de LED concluída.");
        }
        catch (OperationCanceledException)
        {
            Log("Varredura de LED interrompida.");
        }
        finally
        {
            _sweepCts.Dispose();
            _sweepCts = null;
            _ledSweepUnmapped.Text = "Varrer LEDs sem tecla";
            _ledSweepAll.Text = "Varrer todos (1 a 142)";
            _sweepNow.Text = "";
        }
    }

    private async Task ToggleSweepAsync()
    {
        if (_sweepCts is not null)
        {
            _sweepCts.Cancel();
            return;
        }

        byte[] baseBlock;
        try
        {
            baseBlock = BuildBlockFromText();
        }
        catch (KeyboardException ex)
        {
            Log("Erro: " + ex.Message);
            return;
        }

        var index = (int)_sweepIndex.Value;
        var from = (int)_sweepFrom.Value;
        var to = (int)_sweepTo.Value;
        var pause = TimeSpan.FromSeconds((double)_sweepSeconds.Value);
        var target = SelectedTarget;
        if (to < from)
        {
            Log("Erro: o valor final precisa ser maior ou igual ao inicial.");
            return;
        }

        _sweepCts = new CancellationTokenSource();
        _sweepButton.Text = "Parar varredura";
        Log($"Varredura do byte {index}, de 0x{from:X2} a 0x{to:X2}, destino {target}");

        try
        {
            for (var value = from; value <= to; value++)
            {
                var block = (byte[])baseBlock.Clone();
                block[index] = (byte)value;
                _sweepNow.Text = $"Valor atual: 0x{value:X2}";
                await SendAsync(() => block, target);
                await Task.Delay(pause, _sweepCts.Token);
            }
            Log("Varredura concluída.");
        }
        catch (OperationCanceledException)
        {
            Log("Varredura interrompida.");
        }
        finally
        {
            _sweepCts.Dispose();
            _sweepCts = null;
            _sweepButton.Text = "Iniciar varredura";
            _sweepNow.Text = "";
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _sweepCts?.Cancel();
        _monitor?.Dispose();
        base.OnFormClosed(e);
    }
}
