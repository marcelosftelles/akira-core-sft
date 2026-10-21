using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using AkiraCore.App.Hid;
using AkiraCore.App.Web;
using AkiraCore.Core;
using AkiraCore.Core.Device;
using AkiraCore.Core.Diagnostics;
using AkiraCore.Core.Keys;
using AkiraCore.Core.Lighting;
using AkiraCore.Core.Links;
using AkiraCore.Core.Macros;
using AkiraCore.Core.Presentation;
using AkiraCore.Core.Protocol;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace AkiraCore.App;

/// <summary>
/// Janela principal: hospeda a interface HTML (pasta Ui) num WebView2 e traduz as
/// mensagens dela em comandos do AkiraCore.Core. A bandeja continua em WinForms.
/// </summary>
public sealed partial class MainForm : Form
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
    // Teclas do AKIRA (Raw Input): alimentam a prévia e o monitor do Laboratório
    private readonly RawKeyboardMonitor _rawKeyboard = new();
    private IDisposable? _previewKeys;
    // Bateria: o oficial pergunta ao dongle a cada 15 s (fcn.004357d0)
    private readonly System.Windows.Forms.Timer _batteryTimer = new()
    {
        Interval = (int)BatteryProtocol.PollInterval.TotalMilliseconds,
    };
    private int? _batteryLevel;
    private readonly BatteryAlert _batteryAlert = new();

    // Teclas e configurações: cada ajuste reenvia tudo, então só o pedido mais recente importa
    private readonly LatestOnly _keysSender;
    private readonly LatestOnly _deviceSender;

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
        SanitizeKeyboardSettings(_settings);
        EnsureProfiles();
        _keysSender = new LatestOnly(SendKeysAsync);
        _deviceSender = new LatestOnly(SendDeviceAsync);

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
            _ = RefreshBatteryAsync(); // dongle recém-conectado: o oficial lê na hora
        };
        _batteryTimer.Tick += (_, _) => _ = RefreshBatteryAsync();
        Activated += (_, _) => PostDevice();
        VisibleChanged += (_, _) => UpdatePreviewKeys();
        Resize += (_, _) => UpdatePreviewKeys();
        _rawKeyboard.KeyReceived += OnRawKey;
        SystemEvents.UserPreferenceChanged += OnThemeChanged;
    }

    // --- Ciclo de vida da janela -----------------------------------------------

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowChrome.Apply(Handle, WindowChrome.IsDarkTheme());
        _rawKeyboard.Attach(Handle);
        // Também com --hidden: a bandeja mostra a bateria mesmo sem a janela ter aberto
        _batteryTimer.Start();
        _ = RefreshBatteryAsync();
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
        else if (m.Msg == RawKeyboardMonitor.WindowMessage)
        {
            _rawKeyboard.Process(m.LParam);
        }
        base.WndProc(ref m); // DefWindowProc libera o buffer do WM_INPUT
    }

    /// <summary>
    /// A prévia só escuta as teclas enquanto está na tela: na bandeja ou minimizado,
    /// nada do que se digita passa pelo app (o Laboratório pede a própria escuta).
    /// </summary>
    private void UpdatePreviewKeys()
    {
        var wanted = _webReady && Visible && WindowState != FormWindowState.Minimized;
        if (wanted && _previewKeys is null)
        {
            _previewKeys = _rawKeyboard.Listen();
        }
        else if (!wanted && _previewKeys is not null)
        {
            _previewKeys.Dispose();
            _previewKeys = null;
        }
    }

    private void OnRawKey(RawKey key)
    {
        // Só a posição vai para a página, para acender a tecla na prévia; nada é guardado
        if (_previewKeys is not null)
        {
            Post(new { type = "key", scan = key.ScanCode, down = !key.Released });
        }
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
                    UpdatePreviewKeys();
                    break;
                case "apply":
                    if (AcceptLighting(message))
                    {
                        // Escolher ou ajustar um efeito sai das cores por tecla
                        _settings.LightOn = true;
                        _settings.CustomLightId = null;
                        QueueApply();
                        PostCustomLights();
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
                case "setKeys":
                    AcceptKeys(message);
                    break;
                case "saveMacros":
                    AcceptMacros(message);
                    break;
                case "setDevice":
                    AcceptDevice(message);
                    break;
                case "factoryReset":
                    _ = RestoreFactoryDefaultsAsync();
                    break;
                case "diagnose":
                    _ = DiagnoseAsync();
                    break;
                case "openLab":
                    OpenLab();
                    break;
                default:
                    HandleLibraryMessage(message);
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

    /// <summary>Teclas remapeadas: valida contra as macros atuais, guarda e envia as duas camadas.</summary>
    private void AcceptKeys(JsonElement message)
    {
        var assignments = message.GetProperty("assignments").Deserialize<List<KeyAssignmentState>>(Json) ?? [];
        try
        {
            KeyProfile.Build(assignments, _settings.Macros);
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException)
        {
            PostStatus("error", ex is ArgumentException ? ex.Message : "Tecla incompleta.");
            return;
        }
        _settings.KeyAssignments = assignments;
        SaveSettings();
        _keysSender.Request();
    }

    /// <summary>
    /// Macros chegam junto com as teclas: apagar ou reordenar uma macro muda a posição que as
    /// teclas usam, então as duas coisas são validadas e enviadas juntas.
    /// </summary>
    private void AcceptMacros(JsonElement message)
    {
        var macros = message.GetProperty("macros").Deserialize<List<MacroState>>(Json) ?? [];
        var assignments = message.GetProperty("assignments").Deserialize<List<KeyAssignmentState>>(Json) ?? [];
        IReadOnlyList<Macro> encoded;
        (KeyRemapTable Normal, KeyRemapTable Fn) tables;
        try
        {
            encoded = MacroState.ToMacros(macros);
            tables = KeyProfile.Build(assignments, macros);
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException)
        {
            PostStatus("error", ex is ArgumentException ? ex.Message : "Macro incompleta.");
            return;
        }
        _settings.Macros = macros;
        _settings.KeyAssignments = assignments;
        // Macro apagada ou movida muda as posições: teclas de outros perfis que apontam para
        // uma macro que sumiu voltam ao original
        DropMissingMacrosFromProfiles();
        SaveSettings();
        _ = SendMacrosAsync(encoded, tables);
    }

    private async Task SendMacrosAsync(IReadOnlyList<Macro> macros, (KeyRemapTable Normal, KeyRemapTable Fn) tables)
    {
        if (await SendAsync(client =>
            {
                client.ApplyMacros(macros);
                client.ApplyKeyRemap(tables.Normal, tables.Fn);
            }))
        {
            PostStatus("ok", "Macros gravadas no teclado");
        }
    }

    private void AcceptDevice(JsonElement message)
    {
        var candidate = message.GetProperty("settings").Deserialize<DeviceSettingsState>(Json);
        try
        {
            ArgumentNullException.ThrowIfNull(candidate);
            candidate.ToSettings();
        }
        catch (ArgumentException ex)
        {
            PostStatus("error", ex.Message);
            return;
        }
        _settings.Device = candidate;
        SaveSettings();
        _deviceSender.Request();
    }

    private async Task SendKeysAsync()
    {
        (KeyRemapTable Normal, KeyRemapTable Fn) tables;
        try
        {
            tables = KeyProfile.Build(_settings.KeyAssignments, _settings.Macros);
        }
        catch (ArgumentException ex)
        {
            PostStatus("error", ex.Message);
            return;
        }
        if (await SendAsync(client => client.ApplyKeyRemap(tables.Normal, tables.Fn)))
        {
            PostStatus("ok", "Teclas gravadas no teclado");
        }
    }

    private async Task SendDeviceAsync()
    {
        var settings = CurrentDeviceSettings();
        if (await SendAsync(client => client.ApplyDeviceSettings(settings)))
        {
            PostStatus("ok", "Configurações gravadas no teclado");
        }
    }

    private DeviceSettings CurrentDeviceSettings()
    {
        try
        {
            return _settings.Device?.ToSettings() ?? DeviceSettings.Default;
        }
        catch (ArgumentException)
        {
            return DeviceSettings.Default;
        }
    }

    /// <summary>
    /// Arquivo editado à mão ou de outra versão não pode virar envio inválido: o que não
    /// passa na validação é descartado ao abrir o app.
    /// </summary>
    private static void SanitizeKeyboardSettings(AppSettings settings)
    {
        try
        {
            MacroState.ToMacros(settings.Macros);
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException)
        {
            settings.Macros = [];
        }
        try
        {
            KeyProfile.Build(settings.KeyAssignments, settings.Macros);
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException)
        {
            settings.KeyAssignments = [];
        }
        try
        {
            settings.Device?.ToSettings();
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException)
        {
            settings.Device = null;
        }
        try
        {
            CustomLightState.ValidateAll(settings.CustomLights);
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException)
        {
            settings.CustomLights = [];
        }
        if (settings.CustomLights.All(s => s.Id != settings.CustomLightId))
        {
            settings.CustomLightId = null;
        }
        // Perfil com algo inválido é descartado inteiro; o ativo é refeito a partir do que está em uso
        settings.Profiles = settings.Profiles.Where(p =>
        {
            try
            {
                ProfileState.ValidateAll([p], settings.Macros, settings.CustomLights);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or NullReferenceException)
            {
                return false;
            }
        }).DistinctBy(p => p.Id).Take(ProfileState.MaxProfiles).ToList();
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
        options = new
        {
            swapRedBlue = _settings.SwapRedBlue,
            simulate = _simulate,
            startWithWindows = AutoStart.IsEnabled,
            batteryAlert = _settings.BatteryAlert,
        },
        device = DeviceInfo(),
        keyboard = new
        {
            catalog = KeysCatalogView.Build(),
            assignments = _settings.KeyAssignments,
            macros = _settings.Macros,
            settings = DeviceSettingsState.From(CurrentDeviceSettings()),
        },
        customLights = CustomLightsView(),
        profiles = ProfilesView(),
    });

    private void PostDevice() => Post(new { type = "device", device = DeviceInfo() });

    private void PostStatus(string kind, string text) => Post(new { type = "status", kind, text });

    private void PostLog(string title, IEnumerable<string> lines) => Post(new { type = "log", title, lines });

    private object DeviceInfo()
    {
        var collections = HidDeviceFinder.ListCollections();
        var device = HidDeviceFinder.FindCommandInterface(collections);
        // O protocolo não diz se está carregando (o oficial nunca mostra o raio):
        // cabo ligado no PC é o único sinal, e com bateria cheia ele só mantém a carga
        var cable = collections.Any(c => c.Kind == ChannelKind.Feature);
        return new
        {
            connected = device is not null,
            channel = device is null ? null : device.Kind == ChannelKind.Dongle ? "dongle" : "usb",
            name = device?.Profile.Name ?? "",
            battery = new { level = _batteryLevel, cable },
        };
    }

    // --- Bateria -------------------------------------------------------------------

    /// <summary>
    /// Lê o nível pelo dongle, mesmo com o cabo ligado, para mostrar a carga subindo.
    /// Se um envio estiver em andamento, fica para a próxima rodada; se o teclado estiver
    /// dormindo, o dongle não confirma e vale o último nível lido, como no oficial.
    /// </summary>
    private async Task RefreshBatteryAsync()
    {
        if (_simulate)
        {
            return;
        }
        var collections = HidDeviceFinder.ListCollections();
        var dongle = collections.FirstOrDefault(c => c.Kind == ChannelKind.Dongle);
        if (dongle is null)
        {
            _batteryLevel = null;
        }
        else if (await _keyboardLock.WaitAsync(0))
        {
            try
            {
                var level = await Task.Run(() =>
                {
                    using var link = new DongleLink(new HidReportPort(dongle.Path));
                    return new KeyboardClient(link).ReadBatteryLevel();
                });
                _batteryLevel = level ?? _batteryLevel;
            }
            catch (Exception ex) when (ex is KeyboardException or Win32Exception)
            {
                Debug.WriteLine($"Bateria sem resposta: {ex.Message}");
            }
            finally
            {
                _keyboardLock.Release();
            }
        }
        UpdateTrayText();
        PostDevice();

        var cable = collections.Any(c => c.Kind == ChannelKind.Feature);
        if (_batteryAlert.Update(dongle is null ? null : _batteryLevel, cable) && _settings.BatteryAlert)
        {
            _trayIcon.ShowBalloonTip(10000, "Bateria fraca no AKIRA",
                $"Restam {_batteryLevel}%. Ligue o cabo USB para carregar.", ToolTipIcon.Warning);
        }
    }

    private void UpdateTrayText()
    {
        _trayIcon.Text = _batteryLevel is { } level ? $"AKIRA CORE · bateria {level}%" : "AKIRA CORE";
    }

    /// <summary>
    /// Lista o que o Windows vê e, com dongle, a resposta crua da bateria: comparar a linha
    /// com e sem o cabo mostra se algum byte além do nível indica carga.
    /// </summary>
    private async Task DiagnoseAsync()
    {
        var lines = HidDeviceFinder.Diagnose().ToList();
        var dongle = HidDeviceFinder.ListCollections().FirstOrDefault(c => c.Kind == ChannelKind.Dongle);
        if (dongle is not null && !_simulate)
        {
            await _keyboardLock.WaitAsync();
            try
            {
                var response = await Task.Run(() =>
                {
                    using var link = new DongleLink(new HidReportPort(dongle.Path));
                    return link.QueryBattery();
                });
                lines.Add($"Bateria pelo dongle: {PacketBuilder.DongleBatteryLevel(response)?.ToString() ?? "?"}% | " +
                          $"resposta {HexText.Spaced(response)}");
            }
            catch (Exception ex) when (ex is KeyboardException or Win32Exception)
            {
                lines.Add($"Bateria pelo dongle: sem resposta ({ex.Message})");
            }
            finally
            {
                _keyboardLock.Release();
            }
        }
        PostLog($"Diagnóstico às {DateTime.Now:HH:mm:ss}", lines);
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
                Action<KeyboardClient> apply;
                try
                {
                    apply = CurrentLightingAction();
                }
                catch (Exception ex) when (ex is ArgumentException or FormatException)
                {
                    PostStatus("error", "Valor inválido: " + ex.Message);
                    return;
                }
                if (await SendAsync(apply))
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

    /// <summary>
    /// O que vai ao teclado para a iluminação em uso: desligada, esquema de cores por tecla
    /// (modo 0x80) ou o efeito escolhido.
    /// </summary>
    /// <exception cref="ArgumentException">Estado salvo inválido.</exception>
    /// <exception cref="FormatException">Cor que não é #RRGGBB.</exception>
    private Action<KeyboardClient> CurrentLightingAction()
    {
        if (!_settings.LightOn)
        {
            var off = new LightingConfig(EffectCatalog.Off, default);
            return client => client.ApplyLighting(off);
        }
        if (ActiveCustomLight() is { } scheme)
        {
            var frame = scheme.ToFrame(_settings.SwapRedBlue);
            var brightness = scheme.Brightness;
            return client => client.ApplyPerKeyColors(frame, brightness);
        }
        var config = CurrentConfig();
        return client => client.ApplyLighting(config);
    }

    private async Task RestoreFactoryDefaultsAsync()
    {
        var swapRedBlue = _settings.SwapRedBlue;
        if (!await SendAsync(client => client.RestoreFactoryDefaults(swapRedBlue)))
        {
            return;
        }
        // Nos dois canais o teclado fica sem teclas remapeadas, sem macros e com as
        // configurações padrão (dongle: o próprio firmware apaga; cabo: o app reenviou)
        _lighting = LightingState.From(FactoryDefaults.Lighting);
        _settings.LightOn = true;
        _settings.CustomLightId = null;
        _settings.KeyAssignments = [];
        _settings.Macros = [];
        _settings.Device = null;
        SaveSettings();
        PostState();
        PostStatus("ok", "Teclado restaurado ao padrão de fábrica");
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
            case "batteryAlert":
                _settings.BatteryAlert = value;
                SaveSettings();
                break;
            case "startWithWindows":
                try
                {
                    AutoStart.Set(value);
                    PostStatus("ok", value ? "Vai iniciar com o Windows, na bandeja" : "Não inicia mais com o Windows");
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                {
                    PostStatus("error", "Não foi possível mudar a inicialização: " + ex.Message);
                    PostState(); // a chave volta a mostrar o que está no registro
                }
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
        SyncActiveProfile();
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
        _labForm = new LabForm(OpenLink, CurrentConfig, _rawKeyboard);
        _labForm.Show(this);
    }

    // --- Bandeja -------------------------------------------------------------------

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => ShowFromTray());
        menu.Items.Add("Desligar luz", null, (_, _) => SetLightFromTray(false));
        menu.Items.Add("Reaplicar última configuração", null, (_, _) => SetLightFromTray(true));
        var profiles = new ToolStripMenuItem("Perfil");
        menu.Items.Add(profiles);
        // A lista muda pela janela: monta na hora de abrir o menu
        menu.Opening += (_, _) => FillTrayProfiles(profiles);
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
            _batteryTimer.Dispose();
            _keyboardLock.Dispose();
            _rawKeyboard.Dispose();
            _trayIcon.Dispose();
            _web.Dispose();
        }
        base.Dispose(disposing);
    }
}
