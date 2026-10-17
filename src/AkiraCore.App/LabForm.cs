using System.ComponentModel;
using AkiraCore.App.Hid;
using AkiraCore.Core;
using AkiraCore.Core.Diagnostics;
using AkiraCore.Core.Lighting;
using AkiraCore.Core.Links;
using AkiraCore.Core.Protocol;

namespace AkiraCore.App;

/// <summary>
/// Ferramentas para descobrir comandos que o app oficial não expõe:
/// monitor de tudo que o teclado manda ao PC, foto do estado, blocos
/// experimentais e localizador de LED.
/// Só mexe em iluminação: tudo é desfeito reaplicando um efeito normal.
/// </summary>
public sealed class LabForm : Form
{
    private readonly Func<ExchangeLog?, IKeyboardLink> _openLink;
    private readonly Func<LightingConfig> _currentLighting;

    // 1. Monitor: reports HID de todas as coleções + teclas via Raw Input
    private HidInputMonitor? _monitor;
    private RawKeyboardMonitor? _rawKeyboard;
    private readonly Button _monitorButton = new() { Text = "Iniciar monitor", AutoSize = true };
    private readonly TextBox _markerText = new() { Width = 260, PlaceholderText = "ex.: Fn + Shift direito" };
    private int _markerCount;

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

    // 5. Foto do estado: lê o que o teclado devolve e compara com a foto anterior
    private readonly Button _snapshotButton = new() { Text = "Tirar foto do estado", AutoSize = true };
    private readonly CheckBox _snapshotWithAcks = new()
    {
        Text = "Incluir respostas completas (reaplica o efeito da janela principal)", AutoSize = true,
    };
    private StateSnapshot? _lastSnapshot;
    private int _snapshotCount;

    private readonly TextBox _log = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
        Dock = DockStyle.Fill, Font = new Font("Consolas", 9f),
    };

    public LabForm(Func<ExchangeLog?, IKeyboardLink> openLink, Func<LightingConfig> currentLighting)
    {
        _openLink = openLink;
        _currentLighting = currentLighting;
        Text = "AKIRA CORE · Laboratório";
        Font = new Font("Segoe UI", 10f);
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        Size = new Size(Math.Min(900, workArea.Width), Math.Min(860, workArea.Height - 40));
        MinimumSize = new Size(640, 480);

        _blockBox.Text = "00 00 00 00 00 00 00 00 00 00 00 00 00 00 AA 55";
        _targetCombo.Items.AddRange(["Teclas (dongle 05 10 00)", "Fitas (dongle 05 01 00)"]);
        _targetCombo.SelectedIndex = 1;
        _sidePresetCombo.Items.AddRange([.. SideLightCatalog.All]);
        _sidePresetCombo.SelectedIndex = 0;
        _ledColor.Items.AddRange(["Branco", "Vermelho", "Verde", "Azul"]);
        _ledColor.SelectedIndex = 0;

        // Seções roláveis em cima, log sempre visível embaixo
        var sections = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            Dock = DockStyle.Fill, Padding = new Padding(12),
        };
        BuildMonitorSection(sections);
        BuildBlockSection(sections);
        BuildSweepSection(sections);
        BuildLedSection(sections);
        BuildSnapshotSection(sections);

        var clear = new Button { Text = "Limpar log", AutoSize = true };
        var copy = new Button { Text = "Copiar log", AutoSize = true };
        var save = new Button { Text = "Salvar log...", AutoSize = true };
        clear.Click += (_, _) => _log.Clear();
        copy.Click += (_, _) => { if (_log.TextLength > 0) Clipboard.SetText(_log.Text); };
        save.Click += (_, _) => SaveLog();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.Controls.Add(sections, 0, 0);
        root.Controls.Add(Row(clear, copy, save), 0, 1);
        root.Controls.Add(_log, 0, 2);
        Controls.Add(root);
    }

    // --- Montagem das seções -------------------------------------------------

    private void BuildMonitorSection(Control parent)
    {
        var mark = new Button { Text = "Marcar", AutoSize = true };
        _monitorButton.Click += (_, _) => ToggleMonitor();
        mark.Click += (_, _) => AddMarker();
        _markerText.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                AddMarker();
                e.SuppressKeyPress = true;
            }
        };

        parent.Controls.Add(Help(
            "1. Monitor de atalhos: mostra tudo que o teclado manda ao PC (reports HID de todas as " +
            "coleções e as teclas, linhas TECLA). Inicie, escreva o atalho no campo, clique Marcar e " +
            "aperte o atalho. Fn nunca aparece: é tratado dentro do teclado. Se o atalho não gerar " +
            "nenhuma linha, ele não sai do teclado. Enquanto ligado, o que você digitar no AKIRA vai " +
            "para o log: não digite senhas."));
        parent.Controls.Add(Row(_monitorButton, _markerText, mark));
    }

    private void BuildBlockSection(Control parent)
    {
        var sendBlock = new Button { Text = "Enviar bloco", AutoSize = true };
        var sendPreset = new Button { Text = "Enviar predefinição lateral oficial", AutoSize = true };
        sendBlock.Click += async (_, _) => await SendAsync(BuildBlockFromText, SelectedTarget);
        sendPreset.Click += async (_, _) =>
            await SendAsync(() => PacketBuilder.SideLightBlock(((SideLightMode)_sidePresetCombo.SelectedItem!).Value),
                LightTarget.SideStrips);

        parent.Controls.Add(Help(
            "2. Bloco experimental (16 bytes em hex). A assinatura AA 55 é sempre forçada. " +
            "No cabo o destino não muda nada: vai na transação 0x13."));
        parent.Controls.Add(Row(_blockBox, _targetCombo, sendBlock));
        parent.Controls.Add(Row(_sidePresetCombo, sendPreset));
    }

    private void BuildSweepSection(Control parent)
    {
        _sweepButton.Click += async (_, _) => await ToggleSweepAsync();
        parent.Controls.Add(Help(
            "3. Varredura: usa o bloco acima como base e troca o byte escolhido pelos valores " +
            "da faixa, um por vez. Observe o teclado e anote o valor mostrado em destaque."));
        parent.Controls.Add(Row(
            Caption("Byte"), _sweepIndex, Caption("de 0x"), _sweepFrom, Caption("até 0x"), _sweepTo,
            Caption("segundos"), _sweepSeconds, _sweepButton));
        parent.Controls.Add(_sweepNow);
    }

    private void BuildLedSection(Control parent)
    {
        var lightOne = new Button { Text = "Acender só este LED", AutoSize = true };
        lightOne.Click += async (_, _) => await LightSingleLedAsync((int)_ledIndex.Value);
        _ledSweepUnmapped.Click += async (_, _) => await ToggleLedSweepAsync(LedMap.UnmappedIndices);
        _ledSweepAll.Click += async (_, _) =>
            await ToggleLedSweepAsync(Enumerable.Range(1, PerKeyProtocol.MaxLedIndex).ToList());

        parent.Controls.Add(Help(
            "4. Localizador de LED: liga o modo personalizado e acende um único LED. " +
            "As teclas apagam durante o teste; para voltar, aplique um efeito na janela principal."));
        parent.Controls.Add(Row(Caption("LED"), _ledIndex, _ledColor, lightOne, _ledSweepUnmapped, _ledSweepAll));
    }

    private void BuildSnapshotSection(Control parent)
    {
        _snapshotButton.Click += async (_, _) => await TakeSnapshotAsync();
        parent.Controls.Add(Help(
            "5. Foto do estado: lê o que o teclado devolve e compara com a foto anterior. Tire uma " +
            "foto, mude as fitas pelo atalho, tire outra. Se algum byte mudar, o firmware expõe o " +
            "estado das fitas. Sem a opção abaixo é só leitura (GET_REPORT), nada é enviado."));
        parent.Controls.Add(Row(_snapshotButton, _snapshotWithAcks));
    }

    private static Label Help(string text) => new()
    {
        AutoSize = true, MaximumSize = new Size(820, 0), Margin = new Padding(0, 10, 0, 0), Text = text,
    };

    private static Label Caption(string text) => new()
    {
        Text = text, AutoSize = true, Margin = new Padding(0, 6, 0, 0),
    };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 8) };
        row.Controls.AddRange(controls);
        return row;
    }

    // --- Log -----------------------------------------------------------------

    private void Log(string line)
    {
        if (IsDisposed)
        {
            return;
        }
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(line));
            return;
        }
        _log.AppendText(line + Environment.NewLine);
    }

    private void SaveLog()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Texto (*.txt)|*.txt",
            FileName = $"akira-laboratorio-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            File.WriteAllText(dialog.FileName, _log.Text);
            Log($"Log salvo em {dialog.FileName}");
        }
    }

    // --- 1. Monitor ----------------------------------------------------------

    private void ToggleMonitor()
    {
        if (_monitor is not null)
        {
            StopMonitor();
            _monitorButton.Text = "Iniciar monitor";
            Log("Monitor parado.");
            return;
        }

        _monitor = new HidInputMonitor();
        _monitor.ReportReceived += Log;
        var opened = _monitor.Start();
        Log(opened.Count == 0
            ? "Nenhuma coleção HID do teclado encontrada. Ele está conectado por cabo ou dongle?"
            : "Escutando HID:" + Environment.NewLine + string.Join(Environment.NewLine, opened.Select(o => "  " + o)));

        _rawKeyboard = new RawKeyboardMonitor();
        _rawKeyboard.KeyReceived += Log;
        Log(_rawKeyboard.Start(Handle)
            ? "Escutando as teclas do AKIRA (Raw Input)."
            : "Raw Input indisponível: as teclas não serão mostradas.");
        _monitorButton.Text = "Parar monitor";
    }

    private void StopMonitor()
    {
        _monitor?.Dispose();
        _monitor = null;
        _rawKeyboard?.Dispose();
        _rawKeyboard = null;
    }

    private void AddMarker()
    {
        _markerCount++;
        var note = _markerText.Text.Trim();
        Log($"{DateTime.Now:HH:mm:ss.fff} ===== MARCADOR {_markerCount}{(note.Length > 0 ? ": " + note : "")} =====");
        if (_monitor is null)
        {
            Log("(o monitor está parado: inicie-o para ver o que o atalho gera)");
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == RawKeyboardMonitor.WindowMessage)
        {
            _rawKeyboard?.Process(m.LParam);
        }
        base.WndProc(ref m); // DefWindowProc libera o buffer do WM_INPUT
    }

    // --- 2 e 3. Bloco experimental e varredura -------------------------------

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

    private async Task SendAsync(Func<byte[]> buildBlock, LightTarget target)
    {
        try
        {
            var block = buildBlock();
            Log($"Enviando {Convert.ToHexString(block)} para {target}");
            await Task.Run(() =>
            {
                using var link = _openLink(null);
                new KeyboardClient(link).SendExperimentalBlock(block, target);
            });
            Log("Confirmado pelo teclado.");
        }
        catch (Exception ex) when (ex is KeyboardException or ArgumentException or Win32Exception)
        {
            Log("Erro: " + ex.Message);
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

    // --- 4. Localizador de LED -----------------------------------------------

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
                using var link = _openLink(null);
                new KeyboardClient(link).ApplyPerKeyColors(frame);
            });
            Log($"LED {index} aceso.");
            return true;
        }
        catch (Exception ex) when (ex is KeyboardException or Win32Exception)
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

    // --- 5. Foto do estado ---------------------------------------------------

    private async Task TakeSnapshotAsync()
    {
        _snapshotButton.Enabled = false;
        try
        {
            // A configuração é lida da tela aqui, no thread de interface
            var lighting = _snapshotWithAcks.Checked ? _currentLighting() : null;
            var snapshot = await Task.Run(() =>
            {
                var result = HidStateReader.ReadFeatureReports(Log);
                if (lighting is not null)
                {
                    var exchanges = new ExchangeLog();
                    using (var link = _openLink(exchanges))
                    {
                        new KeyboardClient(link).ApplyLighting(lighting);
                    }
                    foreach (var (source, data) in exchanges.ToSnapshot().Sources)
                    {
                        result.Add(source, data);
                    }
                }
                return result;
            });

            _snapshotCount++;
            Log($"{snapshot.TakenAt:HH:mm:ss.fff} Foto {_snapshotCount} ({snapshot.Sources.Count} fontes):");
            foreach (var (source, data) in snapshot.Sources)
            {
                Log($"  {source}: {HexText.Spaced(data)}");
            }
            if (_lastSnapshot is not null)
            {
                Log($"Diferenças em relação à foto {_snapshotCount - 1}:");
                foreach (var line in StateDiff.Describe(StateDiff.Compare(_lastSnapshot, snapshot)))
                {
                    Log("  " + line);
                }
            }
            _lastSnapshot = snapshot;
        }
        catch (Exception ex) when (ex is KeyboardException or Win32Exception or ArgumentException)
        {
            Log("Erro: " + ex.Message);
        }
        finally
        {
            _snapshotButton.Enabled = true;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _sweepCts?.Cancel();
        StopMonitor();
        base.OnFormClosed(e);
    }
}
