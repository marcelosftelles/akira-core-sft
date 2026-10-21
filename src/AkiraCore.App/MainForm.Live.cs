using System.Text.Json;
using AkiraCore.App.Hid;
using AkiraCore.App.Live;
using AkiraCore.Core.Automation;
using AkiraCore.Core.Keys;
using AkiraCore.Core.Lighting;
using AkiraCore.Core.Links;
using AkiraCore.Core.Live;
using AkiraCore.Core.Presentation;

namespace AkiraCore.App;

/// <summary>
/// Página Ao vivo: efeitos calculados no PC (luz personalizada, ritmo musical, cor da tela, CPU,
/// cronômetro), avisos piscando e o espelho dos LEDs. O laço roda numa thread própria
/// (<see cref="LiveRunner"/>); aqui ficam a escolha da fonte, as pausas e a volta à luz normal.
/// </summary>
public sealed partial class MainForm
{
    private readonly LiveEngine _liveEngine = new();
    private readonly System.Windows.Forms.Timer _countdownWatch = new() { Interval = 500 };
    private LiveRunner _liveRunner = null!;
    private LedMirror _mirror = null!;
    private CountdownTimer? _countdown;
    private IDisposable? _liveKeys;
    private bool _mirrorWanted;
    private LiveSendState _liveState;
    private string? _liveDetail;

    private LiveSettingsState CurrentLive => _settings.Live ?? LiveSettingsState.Default;

    private void InitializeLive()
    {
        _liveRunner = new LiveRunner(_liveEngine, OpenLiveLink, _keyboardLock);
        _liveRunner.Preview += frame => Post(new { type = "liveFrame", colors = frame.ToHexMap() });
        _liveRunner.StateChanged += (state, detail) => RunOnUi(() =>
        {
            _liveState = state;
            _liveDetail = detail;
            PostLiveStatus();
        });
        // O teclado ficou no último quadro: a luz normal (ou apagada, pelas regras) volta pelo comando de sempre
        _liveRunner.StreamReleased += () => RunOnUi(QueueApply);
        _liveRunner.Stopped += streamed => RunOnUi(() => OnLiveStopped(streamed));
        _mirror = new LedMirror(OpenLiveLink, _keyboardLock);
        _mirror.Colors += colors => Post(new
        {
            type = "mirror",
            colors = colors.ToDictionary(c => c.Key, c => ColorMath.ToHex(c.Value)),
            problem = (string?)null,
        });
        _mirror.Problem += text => Post(new { type = "mirror", colors = (object?)null, problem = text });
        _countdownWatch.Tick += (_, _) => CheckCountdown();
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }
        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    /// <summary>Canal próprio do laço, sem o registro de bytes (seriam dezenas de linhas por segundo).</summary>
    private IKeyboardLink? OpenLiveLink()
    {
        if (_simulate)
        {
            return null;
        }
        var device = HidDeviceFinder.FindCommandInterface();
        return device switch
        {
            null => null,
            { Kind: ChannelKind.Dongle } => new DongleLink(new HidReportPort(device.Path)),
            _ => new FeatureLink(new HidFeaturePort(device.Path)),
        };
    }

    /// <summary>Monta a fonte do modo escolhido e liga o laço se houver algo para desenhar.</summary>
    private void ApplyLive()
    {
        var live = CurrentLive;
        ApplyLiveBrightness();
        _liveRunner.Sensitivity = live.Music.Sensitivity;
        _liveRunner.SwapRedBlue = _settings.SwapRedBlue;
        _liveRunner.DongleMusic = _settings.DongleMusic;
        _mirror.SwapRedBlue = _settings.SwapRedBlue;
        if (live.Mode == LiveModes.Timer)
        {
            _countdown ??= new CountdownTimer(TimeSpan.FromMinutes(live.Timer.Minutes));
        }
        _liveEngine.Source = live.Mode switch
        {
            LiveModes.Scene => live.CreateScene(),
            LiveModes.Music => live.CreateMusic(),
            LiveModes.Screen => live.CreateScreen(),
            LiveModes.Cpu => live.CreateCpu(),
            LiveModes.Timer => new TimerBar(_countdown!, live.TimerColor, () => DateTime.Now),
            _ => null,
        };
        UpdateLivePause();
        UpdateLiveKeys();
        if (_liveEngine.IsActive)
        {
            _liveRunner.Start();
        }
        UpdateMirror();
        PostLiveStatus();
    }

    private void ApplyLiveBrightness()
    {
        var brightness = CurrentLive.Brightness;
        _liveEngine.Brightness = _lightOverride == LightOverride.Dim
            ? Math.Min(LightRules.DimBrightness, brightness)
            : brightness;
    }

    /// <summary>Não manda quadros com a luz desligada, numa regra que apaga ou em simulação (avisos passam).</summary>
    private void UpdateLivePause()
    {
        _liveRunner.Paused = _simulate || !_settings.LightOn || _lightOverride == LightOverride.Off;
        _liveRunner.AlertsIgnorePause = !_simulate;
    }

    /// <summary>
    /// Teclas "acender ao tocar" precisam das teclas do AKIRA mesmo com a janela fechada. Só a
    /// posição chega ao efeito; nada é guardado, e a escuta acaba quando o modo muda.
    /// </summary>
    private void UpdateLiveKeys()
    {
        var live = CurrentLive;
        var wanted = live.Mode == LiveModes.Scene && live.Scene.Keys.Values.Any(k => k.Effect == "reactive");
        if (wanted && _liveKeys is null)
        {
            _liveKeys = _rawKeyboard.Listen();
        }
        else if (!wanted && _liveKeys is not null)
        {
            _liveKeys.Dispose();
            _liveKeys = null;
        }
    }

    private void PressLiveKey(RawKey key)
    {
        if (!key.Released && _liveEngine.Source is AnimatedScene scene && KeyboardLayout.LedOfScanCode(key.ScanCode) is { } led)
        {
            scene.Press(led);
        }
    }

    private void OnLiveStopped(bool streamed)
    {
        // Um modo ou aviso novo pode ter chegado enquanto o laço fechava
        if (_liveEngine.IsActive)
        {
            _liveRunner.Start();
            return;
        }
        if (streamed)
        {
            QueueApply(); // tira o teclado do modo em tempo real e volta à luz normal
        }
        UpdateMirror();
        PostLiveStatus();
    }

    /// <summary>Escolher um efeito, um esquema ou ligar a luz pela iluminação sai do modo ao vivo.</summary>
    private void StopLiveForLighting()
    {
        if (CurrentLive.Mode == LiveModes.Off)
        {
            return;
        }
        _settings.Live = CurrentLive with { Mode = LiveModes.Off };
        ApplyLive();
    }

    private void SetLiveMode(string mode)
    {
        var live = CurrentLive;
        if (mode != LiveModes.Off)
        {
            _settings.LastLiveMode = mode;
        }
        if (mode != LiveModes.Timer)
        {
            _countdown = null;
            _countdownWatch.Stop();
        }
        _settings.Live = live with { Mode = mode };
        SaveSettings();
        ApplyLive();
        PostState();
    }

    private bool HandleLiveMessage(string? type, JsonElement message)
    {
        switch (type)
        {
            case "setLive":
                AcceptLive(message);
                return true;
            case "liveTimer":
                HandleTimer(message.GetProperty("action").GetString());
                return true;
            case "testAlert":
                _ = ShowAlertAsync(message.GetProperty("color").GetString(), message.GetProperty("times").GetInt32());
                return true;
            case "setMirror":
                _mirrorWanted = message.GetProperty("on").GetBoolean();
                UpdateMirror();
                return true;
            default:
                return false;
        }
    }

    private void AcceptLive(JsonElement message)
    {
        var candidate = message.GetProperty("live").Deserialize<LiveSettingsState>(Json);
        try
        {
            ArgumentNullException.ThrowIfNull(candidate);
            candidate.Validate();
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException)
        {
            PostStatus("error", ex is ArgumentException ? ex.Message : "Opções do modo ao vivo incompletas.");
            PostLiveStatus();
            return;
        }
        var before = CurrentLive;
        if (candidate.Mode != before.Mode)
        {
            if (candidate.Mode != LiveModes.Off)
            {
                _settings.LastLiveMode = candidate.Mode;
            }
            if (candidate.Mode != LiveModes.Timer)
            {
                _countdown = null;
                _countdownWatch.Stop();
            }
        }
        // Cronômetro ainda não iniciado acompanha os minutos escolhidos
        if (_countdown is { Running: false } countdown && countdown.Remaining(DateTime.Now) == countdown.Total
            && countdown.Total != TimeSpan.FromMinutes(candidate.Timer.Minutes))
        {
            _countdown = null;
        }
        _settings.Live = candidate;
        SaveSettings();
        ApplyLive();
        if (candidate.Mode != before.Mode && candidate.Mode != LiveModes.Off)
        {
            var channel = CurrentChannel();
            PostStatus(LiveModes.Supports(candidate.Mode, channel) || channel is null ? "ok" : "error",
                LiveModes.Supports(candidate.Mode, channel) || channel is null
                    ? $"Ao vivo: {LiveModes.ByKey(candidate.Mode).Label}"
                    : "Este modo só funciona pelo cabo USB");
        }
    }

    private string? CurrentChannel()
    {
        var device = HidDeviceFinder.FindCommandInterface();
        return device is null ? null : device.Kind == ChannelKind.Dongle ? "dongle" : "usb";
    }

    // --- Cronômetro -------------------------------------------------------------------

    private void HandleTimer(string? action)
    {
        var live = CurrentLive;
        switch (action)
        {
            case "start":
                if (_countdown is null || _countdown.IsFinished(DateTime.Now))
                {
                    _countdown = new CountdownTimer(TimeSpan.FromMinutes(live.Timer.Minutes));
                }
                _countdown.Start(DateTime.Now);
                _countdownWatch.Start();
                if (live.Mode != LiveModes.Timer)
                {
                    _settings.Live = live with { Mode = LiveModes.Timer };
                    _settings.LastLiveMode = LiveModes.Timer;
                    SaveSettings();
                    PostState();
                }
                ApplyLive();
                break;
            case "pause":
                _countdown?.Pause(DateTime.Now);
                break;
            case "stop":
                _countdown = null;
                _countdownWatch.Stop();
                ApplyLive();
                break;
        }
        PostLiveStatus();
    }

    private void CheckCountdown()
    {
        if (_countdown is not { } countdown)
        {
            _countdownWatch.Stop();
            return;
        }
        if (countdown.Running && countdown.IsFinished(DateTime.Now))
        {
            _countdownWatch.Stop();
            _countdown = null;
            _trayIcon.ShowBalloonTip(8000, "Cronômetro do AKIRA", "O tempo acabou.", ToolTipIcon.Info);
            ApplyLive(); // a barra volta cheia para a próxima vez
            _ = ShowAlertAsync(CurrentLive.Timer.Color, 6);
        }
        if (Visible)
        {
            PostLiveStatus();
        }
    }

    // --- Avisos ----------------------------------------------------------------------------

    /// <summary>
    /// Pisca o teclado. No cabo, pelo fluxo em tempo real (não grava nada no teclado, por mais avisos
    /// que cheguem). No dongle, que não tem fluxo, a cor fica fixa pelo tempo do aviso e a luz volta.
    /// Devolve o motivo quando não deu.
    /// </summary>
    private async Task<string?> ShowAlertAsync(string? hex, int times)
    {
        Rgb color;
        try
        {
            color = Rgb.FromHex(hex ?? "");
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }
        times = Math.Clamp(times, 1, AlertFlash.MaxTimes);
        // A prévia mostra o aviso mesmo sem teclado (e na simulação)
        _liveEngine.ShowAlert(color, times);
        _liveRunner.Start();

        var device = _simulate ? null : HidDeviceFinder.FindCommandInterface();
        if (device is null)
        {
            return _simulate ? null : "Teclado não encontrado.";
        }
        if (device.Kind == ChannelKind.Dongle)
        {
            var flash = new LightingConfig(EffectCatalog.ByKey("static"), color) { SwapRedBlue = _settings.SwapRedBlue };
            if (!await SendAsync(client => client.ApplyLighting(flash)))
            {
                return "O dongle não confirmou o aviso.";
            }
            await Task.Delay(TimeSpan.FromSeconds(times * AlertFlash.Period));
            QueueApply();
        }
        return null;
    }

    // --- Espelho dos LEDs ------------------------------------------------------------------

    /// <summary>
    /// Lê as cores de verdade (04 F5) só com a janela à vista, o espelho pedido e nada sendo
    /// transmitido: com o modo ao vivo, a prévia já mostra o quadro enviado.
    /// </summary>
    private void UpdateMirror()
    {
        var wanted = _mirrorWanted && _webReady && Visible && WindowState != FormWindowState.Minimized
                     && !_liveRunner.Running && !_simulate;
        if (wanted)
        {
            _mirror.Start();
        }
        else if (_mirror.Running)
        {
            _mirror.Stop();
            Post(new { type = "mirror", colors = (object?)null, problem = (string?)null });
        }
    }

    /// <summary>
    /// Ao sair com o modo ao vivo no cabo, o teclado ficaria parado no último quadro: espera o laço
    /// fechar e devolve a luz normal (ou apagada) pelo comando de sempre.
    /// </summary>
    private void ReleaseLiveBeforeExit()
    {
        _mirror.Stop();
        if (!_liveRunner.Running)
        {
            return;
        }
        _liveRunner.StopAndWait(TimeSpan.FromSeconds(1.5));
        if (_simulate || !_keyboardLock.Wait(TimeSpan.FromSeconds(2)))
        {
            return;
        }
        try
        {
            using var link = OpenLink();
            if (link.HasStreaming)
            {
                CurrentLightingAction()(new Core.KeyboardClient(link));
            }
        }
        catch (Exception ex) when (ex is KeyboardException or System.ComponentModel.Win32Exception
                                       or ArgumentException or FormatException)
        {
            System.Diagnostics.Debug.WriteLine($"Luz não restaurada ao sair: {ex.Message}");
        }
        finally
        {
            _keyboardLock.Release();
        }
    }

    // --- Estado para a página -------------------------------------------------------------

    private object LiveView() => new
    {
        settings = CurrentLive,
        catalog = LiveModes.CatalogView(),
        lastMode = _settings.LastLiveMode,
        status = LiveStatusView(),
    };

    private object LiveStatusView() => new
    {
        mode = CurrentLive.Mode,
        running = _liveRunner.Running,
        state = _liveState switch
        {
            LiveSendState.Sending => "sending",
            LiveSendState.NoKeyboard => "no-keyboard",
            LiveSendState.WrongChannel => "wrong-channel",
            LiveSendState.Failing => "failing",
            _ => "idle",
        },
        detail = _liveDetail,
        paused = _liveRunner.Paused,
        lightOverride = _lightOverride switch
        {
            LightOverride.Off => "off",
            LightOverride.Dim => "dim",
            _ => "none",
        },
        timer = _countdown is { } countdown
            ? new
            {
                remainingMs = (long)countdown.Remaining(DateTime.Now).TotalMilliseconds,
                totalMs = (long)countdown.Total.TotalMilliseconds,
                running = countdown.Running,
            }
            : null,
    };

    private void PostLiveStatus() => Post(new { type = "liveStatus", status = LiveStatusView() });

    /// <summary>Configuração salva inválida (arquivo editado à mão) volta ao padrão.</summary>
    private static void SanitizeLiveSettings(AppSettings settings)
    {
        try
        {
            settings.Live?.Validate();
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException)
        {
            settings.Live = null;
        }
        if (LiveModes.All.All(m => m.Key != settings.LastLiveMode) || settings.LastLiveMode == LiveModes.Off)
        {
            settings.LastLiveMode = LiveModes.Music;
        }
        settings.DongleMusic = (settings.DongleMusic ?? new DongleMusicCalibration()).Sanitized();
    }
}
