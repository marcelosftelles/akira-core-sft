using AkiraCore.App.Hid;
using AkiraCore.Core;
using AkiraCore.Core.Lighting;
using AkiraCore.Core.Links;

namespace AkiraCore.App;

/// <summary>
/// Interface montada em código (sem Designer) para ficar legível no Git.
/// A tela só traduz cliques em LightingConfig; toda regra fica no AkiraCore.Core.
/// </summary>
public sealed class MainForm : Form
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly bool _startHidden;

    private readonly ComboBox _effectCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly Button _colorButton = new() { Width = 220, Height = 32, FlatStyle = FlatStyle.Flat };
    private readonly CheckBox _rainbowCheck = new() { Text = "Arco-íris", AutoSize = true };
    private readonly TrackBar _brightnessBar = new() { Minimum = 0, Maximum = 5, Width = 220, TickFrequency = 1 };
    private readonly TrackBar _speedBar = new() { Minimum = 0, Maximum = 5, Width = 220, TickFrequency = 1 };
    private readonly CheckBox _directionCheck = new() { Text = "Inverter direção", AutoSize = true };
    private readonly CheckBox _swapCheck = new() { Text = "Corrigir cores (trocar vermelho e azul)", AutoSize = true };
    private readonly CheckBox _simulateCheck = new() { Text = "Simular (mostrar bytes sem enviar)", AutoSize = true };
    private readonly Button _applyButton = new() { Text = "Aplicar", Width = 105, Height = 34 };
    private readonly Button _offButton = new() { Text = "Desligar luz", Width = 105, Height = 34 };
    private readonly Button _diagnoseButton = new() { Text = "Diagnóstico", AutoSize = true };
    private readonly Button _labButton = new() { Text = "Laboratório", AutoSize = true };
    private LabForm? _labForm;
    private readonly Label _statusLabel = new() { AutoSize = true, MaximumSize = new Size(240, 0) };
    private readonly TextBox _logBox = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
        Width = 760, Height = 160, Font = new Font("Consolas", 8.5f), Visible = false,
    };
    private readonly NotifyIcon _trayIcon = new() { Text = "AKIRA CORE", Icon = SystemIcons.Application };

    private Color _selectedColor;
    private bool _exiting;

    public MainForm(bool startHidden)
    {
        _startHidden = startHidden;
        Text = "AKIRA CORE";
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        BuildLayout();
        BuildTray();
        LoadSettingsIntoControls();
        RefreshDeviceStatus();
    }

    // --- Montagem da tela ----------------------------------------------------

    private void BuildLayout()
    {
        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };

        void AddRow(string label, Control control)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 16, 8) });
            grid.Controls.Add(control);
        }

        _effectCombo.Items.AddRange([.. EffectCatalog.All]);
        _effectCombo.SelectedIndexChanged += (_, _) => UpdateEnabledControls();
        _colorButton.Click += (_, _) => PickColor();
        _rainbowCheck.CheckedChanged += (_, _) => UpdateEnabledControls();
        _simulateCheck.CheckedChanged += (_, _) => _logBox.Visible = _simulateCheck.Checked;
        _applyButton.Click += async (_, _) => await ApplyLightingAsync(BuildConfig());
        _offButton.Click += async (_, _) => await ApplyLightingAsync(new LightingConfig(EffectCatalog.Off, default));

        AddRow("Efeito", _effectCombo);
        AddRow("Cor", _colorButton);
        AddRow("", _rainbowCheck);
        AddRow("Brilho", _brightnessBar);
        AddRow("Velocidade", _speedBar);
        AddRow("", _directionCheck);

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.AddRange([_applyButton, _offButton]);
        AddRow("", buttons);

        AddRow("", _statusLabel);
        AddRow("", _swapCheck);
        AddRow("", _simulateCheck);
        var tools = new FlowLayoutPanel { AutoSize = true };
        tools.Controls.AddRange([_diagnoseButton, _labButton]);
        AddRow("", tools);
        _labButton.Click += (_, _) => OpenLab();
        _diagnoseButton.Click += (_, _) => RunDiagnostics();

        var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
        root.Controls.AddRange([grid, _logBox]);
        Controls.Add(root);
        AcceptButton = _applyButton;
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => ShowFromTray());
        menu.Items.Add("Desligar luz", null, async (_, _) =>
            await ApplyLightingAsync(new LightingConfig(EffectCatalog.Off, default)));
        menu.Items.Add("Reaplicar última configuração", null, async (_, _) => await ApplyLightingAsync(BuildConfig()));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => { _exiting = true; Close(); });

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
        _trayIcon.Visible = true;
    }

    protected override void SetVisibleCore(bool value)
    {
        // Com --hidden o app nasce direto no tray
        base.SetVisibleCore(_startHidden && !IsHandleCreated ? false : value);
        if (_startHidden && !IsHandleCreated)
        {
            CreateHandle();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Fechar a janela só esconde; sair de verdade é pelo menu do tray
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _trayIcon.Visible = false;
        base.OnFormClosing(e);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        RefreshDeviceStatus();
    }

    // --- Estado da tela ------------------------------------------------------

    private void LoadSettingsIntoControls()
    {
        _effectCombo.SelectedItem = EffectCatalog.All.FirstOrDefault(e => e.Key == _settings.EffectKey)
                                    ?? EffectCatalog.All[0];
        SetColor(ColorTranslator.FromHtml("#" + _settings.ColorHex));
        _brightnessBar.Value = Math.Clamp(_settings.Brightness, 0, 5);
        _speedBar.Value = Math.Clamp(_settings.Speed, 0, 5);
        _directionCheck.Checked = _settings.ReverseDirection;
        _rainbowCheck.Checked = _settings.Rainbow;
        _swapCheck.Checked = _settings.SwapRedBlue;
        UpdateEnabledControls();
    }

    private Effect SelectedEffect => (Effect)_effectCombo.SelectedItem!;

    private void UpdateEnabledControls()
    {
        // Cada efeito só libera os controles que o próprio firmware aceita (config_func)
        var effect = SelectedEffect;
        _rainbowCheck.Enabled = effect.Supports(EffectCapabilities.Rainbow);
        _colorButton.Enabled = effect.Supports(EffectCapabilities.Color) && !_rainbowCheck.Checked;
        _brightnessBar.Enabled = effect.Supports(EffectCapabilities.Brightness);
        _speedBar.Enabled = effect.Supports(EffectCapabilities.Speed);
        _directionCheck.Enabled = effect.Supports(EffectCapabilities.Direction);
    }

    private void PickColor()
    {
        using var dialog = new ColorDialog { Color = _selectedColor, FullOpen = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetColor(dialog.Color);
        }
    }

    private void SetColor(Color color)
    {
        _selectedColor = color;
        _colorButton.BackColor = color;
        _colorButton.ForeColor = color.GetBrightness() > 0.5f ? Color.Black : Color.White;
        _colorButton.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private LightingConfig BuildConfig() =>
        new(SelectedEffect,
            new Rgb(_selectedColor.R, _selectedColor.G, _selectedColor.B),
            _brightnessBar.Value,
            _speedBar.Value,
            direction: _directionCheck.Checked ? 1 : 0, // faixa real de direção ainda não confirmada
            rainbow: _rainbowCheck.Checked)
        {
            SwapRedBlue = _swapCheck.Checked,
        };

    private void SaveSettings(LightingConfig config)
    {
        if (config.Effect.Mode != 0)
        {
            _settings.EffectKey = config.Effect.Key;
        }
        _settings.ColorHex = $"{_selectedColor.R:X2}{_selectedColor.G:X2}{_selectedColor.B:X2}";
        _settings.Brightness = _brightnessBar.Value;
        _settings.Speed = _speedBar.Value;
        _settings.ReverseDirection = _directionCheck.Checked;
        _settings.Rainbow = _rainbowCheck.Checked;
        _settings.SwapRedBlue = _swapCheck.Checked;
        _settings.Save();
    }

    // --- Comunicação ---------------------------------------------------------

    private void RefreshDeviceStatus()
    {
        var device = HidDeviceFinder.FindCommandInterface();
        _statusLabel.ForeColor = device is null ? Color.Firebrick : Color.ForestGreen;
        _statusLabel.Text = device is null
            ? "Teclado não encontrado. Clique em Diagnóstico e me envie o resultado."
            : device.Kind == ChannelKind.Dongle
                ? $"Conectado: {device.Profile.Name}. Se não responder, aperte uma tecla para acordar o teclado."
                : $"Conectado: {device.Profile.Name}";
    }

    /// <summary>Chamado quando alguém tenta abrir uma segunda instância.</summary>
    public void ActivateFromAnotherInstance()
    {
        if (InvokeRequired)
        {
            BeginInvoke(ActivateFromAnotherInstance);
            return;
        }
        ShowFromTray();
    }

    private void OpenLab()
    {
        if (_labForm is { IsDisposed: false })
        {
            _labForm.Activate();
            return;
        }
        _labForm = new LabForm(OpenLink);
        _labForm.Show(this);
    }

    private void RunDiagnostics()
    {
        // Mostra o relatório na mesma caixa de log; dá para selecionar e copiar
        _logBox.Visible = true;
        _logBox.Clear();
        _logBox.AppendText($"Diagnóstico em {DateTime.Now:HH:mm:ss}{Environment.NewLine}");
        foreach (var line in HidDeviceFinder.Diagnose())
        {
            _logBox.AppendText(line + Environment.NewLine);
        }
        RefreshDeviceStatus();
    }

    private IKeyboardLink OpenLink()
    {
        var device = HidDeviceFinder.FindCommandInterface();

        if (_simulateCheck.Checked)
        {
            // Simula o canal do dispositivo detectado; sem dispositivo, simula o cabo
            void Log(string line) => BeginInvoke(() => _logBox.AppendText(line + Environment.NewLine));
            if (device?.Kind == ChannelKind.Dongle)
            {
                var reportPort = new RecordingReportPort();
                reportPort.Logged += Log;
                return new DongleLink(reportPort);
            }
            var featurePort = new RecordingFeaturePort();
            featurePort.Logged += Log;
            return new FeatureLink(featurePort);
        }

        if (device is null)
        {
            throw new KeyboardException("Teclado não encontrado.");
        }
        return device.Kind == ChannelKind.Dongle
            ? new DongleLink(new HidReportPort(device.Path))
            : new FeatureLink(new HidFeaturePort(device.Path));
    }

    private Task ApplyLightingAsync(LightingConfig config) =>
        RunOnKeyboardAsync(client => client.ApplyLighting(config), () => SaveSettings(config));

    private async Task RunOnKeyboardAsync(Action<KeyboardClient> action, Action onSuccess)
    {
        Button[] buttons = [_applyButton, _offButton];
        foreach (var b in buttons) b.Enabled = false;
        _statusLabel.ForeColor = SystemColors.ControlText;
        _statusLabel.Text = "Enviando...";

        try
        {
            // Abre, envia e fecha a cada comando: não segura o dispositivo
            // e se recupera sozinho se o cabo ou o dongle for reconectado.
            await Task.Run(() =>
            {
                using var link = OpenLink();
                action(new KeyboardClient(link));
            });

            onSuccess();
            _statusLabel.ForeColor = Color.ForestGreen;
            _statusLabel.Text = _simulateCheck.Checked ? "Simulado. Veja os bytes abaixo." : "Aplicado.";
        }
        catch (Exception ex) when (ex is KeyboardException or System.ComponentModel.Win32Exception)
        {
            _statusLabel.ForeColor = Color.Firebrick;
            _statusLabel.Text = ex.Message;
        }
        finally
        {
            foreach (var b in buttons) b.Enabled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
