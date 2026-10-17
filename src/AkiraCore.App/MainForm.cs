using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using AkiraCore.App.Hid;
using AkiraCore.App.Web;
using AkiraCore.Core;
using AkiraCore.Core.Diagnostics;
using AkiraCore.Core.Lighting;
using AkiraCore.Core.Links;
using AkiraCore.Core.Presentation;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace AkiraCore.App;

/// <summary>
/// Janela principal: hospeda a interface HTML (pasta Ui) num WebView2 e traduz as
/// mensagens dela em comandos do AkiraCore.Core. A bandeja continua em WinForms.
/// </summary>
public sealed class MainForm : Form
{
    private const int WmDeviceChange = 0x0219;
    private const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly bool _startHidden;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly NotifyIcon _trayIcon = new() { Text = "AKIRA CORE", Icon = SystemIcons.Application };
    // WM_DEVICECHANGE chega em rajadas: espera assentar antes de procurar o teclado
    private readonly System.Windows.Forms.Timer _deviceTimer = new() { Interval = 700 };
    // Um envio por vez: duas transações abertas juntas se misturariam no teclado
    private readonly SemaphoreSlim _keyboardLock = new(1, 1);

    private LightingState _lighting;
    private bool _simulate;
    private bool _webReady;
    private bool _sending;
    private bool _dirty;
    private bool _exiting;
    private LabForm? _labForm;

    public MainForm(bool startHidden)
    {
        _startHidden = startHidden;
        _lighting = LoadLighting(_settings);

        var dark = WindowChrome.IsDarkTheme();
        Text = "AKIRA CORE";
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        Size = new Size(Math.Min(1000, workArea.Width), Math.Min(740, workArea.Height));
        MinimumSize = new Size(820, 600);
        BackColor = WindowChrome.Ground(dark);
        // Fundo da página antes dela pintar: evita um clarão branco no tema escuro
        _web.DefaultBackgroundColor = WindowChrome.Ground(dark);
        Controls.Add(_web);

        BuildTray();
        _deviceTimer.Tick += (_, _) =>
        {
            _deviceTimer.Stop();
            PostDevice();
        };
        Activated += (_, _) => PostDevice();
        SystemEvents.UserPreferenceChanged += OnThemeChanged;
    }

    // --- Ciclo de vida da janela -----------------------------------------------

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowChrome.Apply(Handle, WindowChrome.IsDarkTheme());
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await InitializeWebAsync();
    }

    protected override void SetVisibleCore(bool value)
    {
        // Com --hidden o app nasce direto na bandeja; a interface carrega quando abrir
        base.SetVisibleCore(_startHidden && !IsHandleCreated ? false : value);
        if (_startHidden && !IsHandleCreated)
        {
            CreateHandle();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Fechar a janela só esconde; sair de verdade é pelo menu da bandeja
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _trayIcon.Visible = false;
        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmDeviceChange)
        {
            _deviceTimer.Stop();
            _deviceTimer.Start();
        }
        base.WndProc(ref m);
    }

    private void OnThemeChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || !IsHandleCreated)
        {
            return;
        }
        BeginInvoke(() =>
        {
            var dark = WindowChrome.IsDarkTheme();
            WindowChrome.Apply(Handle, dark);
            BackColor = WindowChrome.Ground(dark);
            _web.DefaultBackgroundColor = WindowChrome.Ground(dark);
        });
    }

    // --- WebView2 ----------------------------------------------------------------

    private async Task InitializeWebAsync()
    {
        try
        {
            // Pasta de dados em %LocalAppData%: a pasta do .exe pode não aceitar escrita
            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AkiraCore", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, dataFolder);
            await _web.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowWebUnavailable("A interface precisa do Microsoft Edge WebView2 Runtime, que não está instalado. " +
                               "Instale e abra o AkiraCore de novo.");
            return;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                                       or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            ShowWebUnavailable($"A interface não pôde iniciar ({ex.Message}). Reinstalar o WebView2 Runtime costuma resolver.");
            return;
        }

        var core = _web.CoreWebView2;
        var settings = core.Settings;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDevToolsEnabled = Debugger.IsAttached;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;

        core.AddWebResourceRequestedFilter(EmbeddedUi.Origin + "/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            var file = EmbeddedUi.Open(e.Request.Uri);
            e.Response = file is { } f
                ? core.Environment.CreateWebResourceResponse(f.Content, 200, "OK", $"Content-Type: {f.ContentType}")
                : core.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
        };
        // A página nunca sai do próprio app; links externos abrem no navegador padrão
        core.NavigationStarting += (_, e) =>
        {
            if (!EmbeddedUi.IsOwnUrl(e.Uri))
            {
                e.Cancel = true;
            }
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.WebMessageReceived += OnWebMessage;
        core.Navigate(EmbeddedUi.StartPage);
    }

    private void ShowWebUnavailable(string reason)
    {
        Controls.Remove(_web);
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(40),
        };
        panel.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(560, 0), Font = new Font("Segoe UI", 11f),
            Text = reason + " Enquanto isso, a bandeja continua funcionando.",
        });
        var link = new LinkLabel { AutoSize = true, Text = "Baixar o WebView2 Runtime", Margin = new Padding(0, 16, 0, 0) };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(RuntimeDownloadUrl) { UseShellExecute = true });
        panel.Controls.Add(link);
        Controls.Add(panel);
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!EmbeddedUi.IsOwnUrl(e.Source))
        {
            return;
        }
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var message = document.RootElement;
            switch (message.GetProperty("type").GetString())
            {
                case "ready":
                    _webReady = true;
                    PostState();
                    break;
                case "apply":
                    if (AcceptLighting(message))
                    {
                        _settings.LightOn = true;
                        QueueApply();
                    }
                    break;
                case "setLight":
                    if (AcceptLighting(message))
                    {
                        _settings.LightOn = message.GetProperty("on").GetBoolean();
                        QueueApply();
                    }
                    break;
                case "setOption":
                    SetOption(message.GetProperty("key").GetString(), message.GetProperty("value").GetBoolean());
                    break;
                case "factoryReset":
                    _ = RestoreFactoryDefaultsAsync();
                    break;
                case "diagnose":
                    PostLog($"Diagnóstico às {DateTime.Now:HH:mm:ss}", HidDeviceFinder.Diagnose());
                    break;
                case "openLab":
                    OpenLab();
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Mensagem malformada vinda da página: ignora, nada é enviado ao teclado
            Debug.WriteLine($"Mensagem da interface ignorada: {ex.Message}");
        }
    }

    /// <summary>Só troca o estado se ele for válido; senão avisa a tela e mantém o anterior.</summary>
    private bool AcceptLighting(JsonElement message)
    {
        var candidate = message.GetProperty("lighting").Deserialize<LightingState>(Json);
        try
        {
            candidate?.ToConfig();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            PostStatus("error", "Valor inválido: " + ex.Message);
            return false;
        }
        if (candidate is null)
        {
            return false;
        }
        _lighting = candidate;
        return true;
    }

    private void Post(object message)
    {
        if (!_webReady || _web.CoreWebView2 is null)
        {
            return;
        }
        if (InvokeRequired)
        {
            BeginInvoke(() => Post(message));
            return;
        }
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));
    }

    private void PostState() => Post(new
    {
        type = "state",
        effects = EffectView.Catalog,
        lighting = _lighting,
        lightOn = _settings.LightOn,
        options = new { swapRedBlue = _settings.SwapRedBlue, simulate = _simulate },
        device = DeviceInfo(),
    });

    private void PostDevice() => Post(new { type = "device", device = DeviceInfo() });

    private void PostStatus(string kind, string text) => Post(new { type = "status", kind, text });

    private void PostLog(string title, IEnumerable<string> lines) => Post(new { type = "log", title, lines });

    private static object DeviceInfo()
    {
        var device = HidDeviceFinder.FindCommandInterface();
        return new
        {
            connected = device is not null,
            channel = device is null ? null : device.Kind == ChannelKind.Dongle ? "dongle" : "usb",
            name = device?.Profile.Name ?? "",
        };
    }

    // --- Comandos ------------------------------------------------------------------

    private static LightingState LoadLighting(AppSettings settings)
    {
        var saved = new LightingState(settings.EffectKey, "#" + settings.ColorHex, settings.Brightness,
            settings.Speed, settings.ReverseDirection, settings.Rainbow);
        try
        {
            saved.ToConfig(); // valida: arquivo editado à mão não pode quebrar o app
            return saved;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return LightingState.From(FactoryDefaults.Lighting);
        }
    }

    private LightingConfig CurrentConfig() => _lighting.ToConfig(_settings.SwapRedBlue);

    /// <summary>
    /// A interface pode mandar vários ajustes seguidos (arrastar, clicar rápido): só o
    /// último importa. Um envio por vez, e ao terminar manda o estado mais recente.
    /// </summary>
    private async void QueueApply()
    {
        _dirty = true;
        if (_sending)
        {
            return;
        }
        _sending = true;
        try
        {
            while (_dirty)
            {
                _dirty = false;
                LightingConfig config;
                try
                {
                    config = _settings.LightOn ? CurrentConfig() : new LightingConfig(EffectCatalog.Off, default);
                }
                catch (Exception ex) when (ex is ArgumentException or FormatException)
                {
                    PostStatus("error", "Valor inválido: " + ex.Message);
                    return;
                }
                if (await SendAsync(client => client.ApplyLighting(config)))
                {
                    SaveSettings();
                    PostStatus("ok", _settings.LightOn ? "Aplicado" : "Luz desligada");
                }
            }
        }
        finally
        {
            _sending = false;
        }
    }

    private async Task RestoreFactoryDefaultsAsync()
    {
        var swapRedBlue = _settings.SwapRedBlue;
        var scope = FactoryResetScope.LightingOnly;
        if (!await SendAsync(client => scope = client.RestoreFactoryDefaults(swapRedBlue)))
        {
            return;
        }
        _lighting = LightingState.From(FactoryDefaults.Lighting);
        _settings.LightOn = true;
        SaveSettings();
        PostState();
        PostStatus("ok", scope == FactoryResetScope.DeviceAndLighting
            ? "Teclado restaurado ao padrão de fábrica"
            : "Iluminação de fábrica reaplicada");
    }

    /// <summary>Abre o canal, executa e fecha: não segura o dispositivo entre comandos.</summary>
    private async Task<bool> SendAsync(Action<KeyboardClient> action)
    {
        PostStatus("busy", _simulate ? "Simulando…" : "Enviando…");
        await _keyboardLock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                using var link = OpenLink();
                action(new KeyboardClient(link));
            });
            return true;
        }
        catch (Exception ex) when (ex is KeyboardException or Win32Exception)
        {
            PostStatus("error", ex.Message);
            return false;
        }
        finally
        {
            _keyboardLock.Release();
        }
    }

    private void SetOption(string? key, bool value)
    {
        switch (key)
        {
            case "swapRedBlue":
                _settings.SwapRedBlue = value;
                SaveSettings();
                if (_settings.LightOn)
                {
                    QueueApply(); // a correção muda as cores no teclado na hora
                }
                break;
            case "simulate":
                _simulate = value;
                PostDevice();
                break;
        }
    }

    private void SaveSettings()
    {
        _settings.EffectKey = _lighting.Effect;
        _settings.ColorHex = _lighting.Color.TrimStart('#');
        _settings.Brightness = _lighting.Brightness;
        _settings.Speed = _lighting.Speed;
        _settings.ReverseDirection = _lighting.Reverse;
        _settings.Rainbow = _lighting.Rainbow;
        _settings.Save();
    }

    /// <param name="trace">Se informado, guarda as respostas completas do teclado (Laboratório).</param>
    private IKeyboardLink OpenLink(ExchangeLog? trace = null)
    {
        var device = HidDeviceFinder.FindCommandInterface();
        IFeaturePort Feature(IFeaturePort port) => trace?.Wrap(port) ?? port;
        IReportPort Report(IReportPort port) => trace?.Wrap(port) ?? port;

        if (_simulate)
        {
            // Simula o canal do dispositivo detectado; sem dispositivo, simula o cabo
            void Log(string line) => PostLog("Bytes enviados", [line]);
            if (device?.Kind == ChannelKind.Dongle)
            {
                var reportPort = new RecordingReportPort();
                reportPort.Logged += Log;
                return new DongleLink(Report(reportPort));
            }
            var featurePort = new RecordingFeaturePort();
            featurePort.Logged += Log;
            return new FeatureLink(Feature(featurePort));
        }

        if (device is null)
        {
            throw new KeyboardException("Teclado não encontrado. Confira o cabo ou o dongle e tente de novo.");
        }
        return device.Kind == ChannelKind.Dongle
            ? new DongleLink(Report(new HidReportPort(device.Path)))
            : new FeatureLink(Feature(new HidFeaturePort(device.Path)));
    }

    private void OpenLab()
    {
        if (_labForm is { IsDisposed: false })
        {
            _labForm.Activate();
            return;
        }
        _labForm = new LabForm(OpenLink, CurrentConfig);
        _labForm.Show(this);
    }

    // --- Bandeja -------------------------------------------------------------------

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => ShowFromTray());
        menu.Items.Add("Desligar luz", null, (_, _) => SetLightFromTray(false));
        menu.Items.Add("Reaplicar última configuração", null, (_, _) => SetLightFromTray(true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) =>
        {
            _exiting = true;
            Close();
        });

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
        _trayIcon.Visible = true;
    }

    private void SetLightFromTray(bool on)
    {
        _settings.LightOn = on;
        PostState();
        QueueApply();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnThemeChanged;
            _deviceTimer.Dispose();
            _keyboardLock.Dispose();
            _trayIcon.Dispose();
            _web.Dispose();
        }
        base.Dispose(disposing);
    }
}
