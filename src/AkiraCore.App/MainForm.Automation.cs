using System.Text.Json;
using AkiraCore.App.Platform;
using AkiraCore.Core.Automation;
using AkiraCore.Core.Keys;
using AkiraCore.Core.Lighting;
using AkiraCore.Core.Presentation;

namespace AkiraCore.App;

/// <summary>
/// Automação do lado do PC: regras de luz (bloqueio, tela, horário), perfil automático pelo programa
/// em primeiro plano, atalhos globais, teclas com ação do app e a linha de comando.
/// </summary>
public sealed partial class MainForm
{
    private const int GlobalHotkeyBase = 1;
    private const int AppKeyBase = 1000;
    private static readonly string OwnProcessName = Path.GetFileName(Environment.ProcessPath ?? "AkiraCore.exe");

    private readonly GlobalHotkeys _hotkeys = new();
    private readonly ForegroundWatcher _foreground = new();
    private readonly SessionWatcher _session = new();
    private readonly System.Windows.Forms.Timer _rulesTimer = new() { Interval = 30_000 };
    // Alt + Tab rápido não deve regravar o teclado a cada janela que passa
    private readonly System.Windows.Forms.Timer _foregroundDebounce = new() { Interval = 1500 };
    private readonly CancellationTokenSource _commandCancel = new();
    private AppActionRunner _appActions = null!;
    private LightOverride _lightOverride;
    private string? _pendingForeground;
    private string _registeredAppKeys = "";
    private bool _automationStarted;

    private LightRulesState CurrentRules => _settings.LightRules ?? LightRulesState.Default;
    private AutoProfileState CurrentAutoProfile => _settings.AutoProfile ?? AutoProfileState.Default;

    private void InitializeAutomation()
    {
        _appActions = new AppActionRunner((title, text) => RunOnUi(() =>
            _trayIcon.ShowBalloonTip(6000, title, text, ToolTipIcon.Info)));
        _session.Changed += EvaluateLightRules;
        _session.Resumed += EvaluateLightRules;
        _rulesTimer.Tick += (_, _) => EvaluateLightRules();
        _foreground.Changed += name =>
        {
            _pendingForeground = name;
            _foregroundDebounce.Stop();
            _foregroundDebounce.Start();
        };
        _foregroundDebounce.Tick += (_, _) =>
        {
            _foregroundDebounce.Stop();
            ApplyAutoProfile(_pendingForeground);
        };
    }

    /// <summary>Chamado com a janela criada (também escondida na bandeja): tudo aqui precisa do handle.</summary>
    private void StartAutomation()
    {
        _hotkeys.Attach(Handle);
        _session.Attach(Handle);
        RegisterGlobalHotkeys(report: false);
        _registeredAppKeys = ""; // handle novo: os atalhos reservados precisam ser registrados de novo
        RegisterAppKeys();
        if (_automationStarted)
        {
            return;
        }
        _automationStarted = true;
        UpdateForegroundWatcher();
        _rulesTimer.Start();
        EvaluateLightRules();
        _ = CommandChannel.ServeAsync(HandleCommandAsync, _commandCancel.Token);
    }

    private bool HandleAutomationMessage(string? type, JsonElement message)
    {
        switch (type)
        {
            case "setRules":
                AcceptRules(message);
                return true;
            case "setHotkeys":
                AcceptHotkeys(message);
                return true;
            case "setAutoProfile":
                AcceptAutoProfile(message);
                return true;
            case "setProfileApps":
                SetProfileApps(message.GetProperty("id").GetString(), message.GetProperty("apps"));
                return true;
            case "pickFile":
                PickFile(message.GetProperty("purpose").GetString());
                return true;
            default:
                return false;
        }
    }

    private object AutomationView() => new
    {
        rules = CurrentRules,
        hotkeys = _settings.Hotkeys,
        hotkeyActions = Hotkeys.Actions.Select(a => new { key = a.Key, label = a.Label }),
    };

    // --- Regras de luz -------------------------------------------------------------------

    private void AcceptRules(JsonElement message)
    {
        var candidate = message.GetProperty("rules").Deserialize<LightRulesState>(Json);
        try
        {
            ArgumentNullException.ThrowIfNull(candidate);
            candidate.Validate();
        }
        catch (ArgumentException ex)
        {
            PostStatus("error", ex.Message);
            return;
        }
        _settings.LightRules = candidate;
        SaveSettings();
        EvaluateLightRules();
    }

    /// <summary>
    /// Recalcula o que as regras pedem. A luz configurada não muda: o app só apaga ou escurece por
    /// cima e volta ao normal quando a regra deixa de valer.
    /// </summary>
    private void EvaluateLightRules()
    {
        LightOverride next;
        try
        {
            next = LightRules.Evaluate(CurrentRules, _session.Locked, _session.DisplayOff, TimeOnly.FromDateTime(DateTime.Now));
        }
        catch (ArgumentException)
        {
            next = LightOverride.None;
        }
        if (next == _lightOverride)
        {
            return;
        }
        _lightOverride = next;
        ApplyLiveBrightness();
        UpdateLivePause();
        QueueApply();
        PostLiveStatus();
    }

    // --- Perfil automático ------------------------------------------------------------------

    private void AcceptAutoProfile(JsonElement message)
    {
        var candidate = message.GetProperty("auto").Deserialize<AutoProfileState>(Json);
        if (candidate is null)
        {
            return;
        }
        if (candidate.FallbackId is not null && _settings.Profiles.All(p => p.Id != candidate.FallbackId))
        {
            candidate = candidate with { FallbackId = null };
        }
        _settings.AutoProfile = candidate;
        SaveSettings();
        UpdateForegroundWatcher();
        PostProfiles();
    }

    private void UpdateForegroundWatcher()
    {
        if (CurrentAutoProfile.Enabled)
        {
            _foreground.Start();
        }
        else
        {
            _foreground.Stop();
        }
    }

    private void ApplyAutoProfile(string? processName)
    {
        // A janela do próprio AkiraCore não conta: é nela que os perfis são editados
        if (processName is null || string.Equals(processName, OwnProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var target = ProfileApps.Target(CurrentAutoProfile, _settings.Profiles, processName);
        if (target is not null && target != _settings.ActiveProfileId)
        {
            _ = SwitchProfileAsync(target);
        }
    }

    private void SetProfileApps(string? id, JsonElement appsElement)
    {
        var index = _settings.Profiles.FindIndex(p => p.Id == id);
        if (index < 0)
        {
            return;
        }
        try
        {
            var apps = ProfileApps.NormalizeAll(appsElement.Deserialize<List<string?>>(Json));
            var updated = _settings.Profiles.ToList();
            updated[index] = updated[index] with { Apps = apps };
            ProfileApps.ValidateAll(updated);
            _settings.Profiles = updated;
            SaveSettings();
        }
        catch (ArgumentException ex)
        {
            PostStatus("error", ex.Message);
        }
        PostProfiles();
    }

    // --- Atalhos globais ----------------------------------------------------------------------

    private void AcceptHotkeys(JsonElement message)
    {
        var candidate = message.GetProperty("hotkeys").Deserialize<List<HotkeyState>>(Json) ?? [];
        try
        {
            Hotkeys.ValidateAll(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException)
        {
            PostStatus("error", ex is ArgumentException ? ex.Message : "Atalho incompleto.");
            Post(new { type = "automation", automation = AutomationView() });
            return;
        }
        _settings.Hotkeys = candidate;
        SaveSettings();
        RegisterGlobalHotkeys(report: true);
    }

    private void RegisterGlobalHotkeys(bool report)
    {
        var list = _settings.Hotkeys.ToList();
        var refused = _hotkeys.Replace(GlobalHotkeyBase, list.Select((h, i) =>
            (GlobalHotkeyBase + i, (uint)h.Modifiers, Hotkeys.VirtualKey(h.Key) ?? 0, (Action)(() => RunHotkey(h.Action)))));
        if (!report)
        {
            return;
        }
        if (refused.Count > 0)
        {
            PostStatus("error", "Outro programa já usa " +
                                string.Join(", ", refused.Select(id => Hotkeys.Describe(list[id - GlobalHotkeyBase]))));
        }
        else
        {
            PostStatus("ok", list.Count == 0 ? "Nenhum atalho global" : "Atalhos globais prontos");
        }
    }

    private void RunHotkey(string action)
    {
        switch (action)
        {
            case "toggleLight":
                SetLightFromTray(!_settings.LightOn);
                break;
            case "nextEffect":
                StepEffect(1);
                break;
            case "previousEffect":
                StepEffect(-1);
                break;
            case "brightnessUp":
                StepBrightness(1);
                break;
            case "brightnessDown":
                StepBrightness(-1);
                break;
            case "nextProfile":
                StepProfile(1);
                break;
            case "previousProfile":
                StepProfile(-1);
                break;
            case "liveToggle":
                SetLiveMode(CurrentLive.Mode == LiveModes.Off ? _settings.LastLiveMode : LiveModes.Off);
                break;
            case "showWindow":
                ShowFromTray();
                break;
        }
    }

    private void StepEffect(int step)
    {
        var catalog = EffectView.Catalog;
        var index = 0;
        for (var i = 0; i < catalog.Count; i++)
        {
            if (catalog[i].Key == _lighting.Effect)
            {
                index = i;
            }
        }
        var next = catalog[(index + step + catalog.Count) % catalog.Count];
        ChooseEffect(next.Key);
    }

    /// <summary>Um efeito escolhido fora da janela (atalho, linha de comando) vale como clicar na lista.</summary>
    private void ChooseEffect(string effect)
    {
        _lighting = _lighting with { Effect = effect };
        _settings.LightOn = true;
        _settings.CustomLightId = null;
        StopLiveForLighting();
        QueueApply();
        PostState();
    }

    private void StepBrightness(int step)
    {
        static int Clamp(int value) => Math.Clamp(value, 0, Core.Protocol.ProtocolConstants.BrightnessMax);
        if (CurrentLive.Mode != LiveModes.Off)
        {
            _settings.Live = CurrentLive with { Brightness = Clamp(CurrentLive.Brightness + step) };
            SaveSettings();
            ApplyLive();
            PostState();
            return;
        }
        if (ActiveCustomLight() is { } scheme)
        {
            var index = _settings.CustomLights.IndexOf(scheme);
            _settings.CustomLights[index] = scheme with { Brightness = Clamp(scheme.Brightness + step) };
            PostCustomLights();
        }
        else
        {
            _lighting = _lighting with { Brightness = Clamp(_lighting.Brightness + step) };
            PostState();
        }
        QueueApply();
    }

    private void StepProfile(int step)
    {
        var list = _settings.Profiles;
        var index = list.FindIndex(p => p.Id == _settings.ActiveProfileId);
        if (list.Count > 1)
        {
            _ = SwitchProfileAsync(list[(Math.Max(index, 0) + step + list.Count) % list.Count].Id);
        }
    }

    // --- Teclas com ação do app ----------------------------------------------------------------

    /// <summary>
    /// Registra o atalho reservado de cada tecla com ação do app. Chamado ao salvar: só refaz quando
    /// as teclas mudaram de verdade.
    /// </summary>
    private void RegisterAppKeys()
    {
        IReadOnlyList<AppKeyBinding> bindings;
        try
        {
            bindings = AppKeyActions.Allocate(_settings.KeyAssignments);
        }
        catch (ArgumentException)
        {
            bindings = [];
        }
        var signature = JsonSerializer.Serialize(bindings.Select(b => b.Assignment), Json);
        if (signature == _registeredAppKeys || !IsHandleCreated)
        {
            return;
        }
        _registeredAppKeys = signature;
        var refused = _hotkeys.Replace(AppKeyBase, bindings.Select(b =>
            (AppKeyBase + b.Slot, (uint)b.HotkeyModifiers, b.VirtualKey,
                (Action)(() => _appActions.Run(b.Assignment.App, b.Assignment.Target)))));
        if (refused.Count > 0)
        {
            PostStatus("error", "Outro programa usa um dos atalhos reservados (F13 a F24): algumas teclas do app não vão funcionar.");
        }
    }

    private void PickFile(string? purpose)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Escolher programa ou arquivo",
            Filter = "Programas e atalhos (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|Todos os arquivos (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            Post(new { type = "filePicked", purpose, path = dialog.FileName });
        }
    }

    // --- Linha de comando -----------------------------------------------------------------------

    /// <summary>Executa o que outro AkiraCore.exe pediu (ver <see cref="CommandLine"/>) e responde em texto.</summary>
    private async Task<CommandChannel.Reply> HandleCommandAsync(string[] args)
    {
        CliRequest request;
        try
        {
            request = CommandLine.Parse(args);
        }
        catch (ArgumentException ex)
        {
            return new CommandChannel.Reply(false, ex.Message);
        }
        var lines = new List<string>();
        var ok = true;
        void Fail(string text)
        {
            ok = false;
            lines.Add(text);
        }

        if (request.Profile is { } profileName)
        {
            var profile = _settings.Profiles.FirstOrDefault(p =>
                string.Equals(p.Name, profileName, StringComparison.CurrentCultureIgnoreCase));
            if (profile is null)
            {
                Fail($"Perfil não encontrado: {profileName}. Perfis: {string.Join(", ", _settings.Profiles.Select(p => p.Name))}.");
            }
            else
            {
                await SwitchProfileAsync(profile.Id);
                lines.Add($"Perfil {profile.Name} em uso.");
            }
        }
        if (request.ChangesLighting)
        {
            var current = _lighting;
            var candidate = current with
            {
                Effect = request.Effect ?? current.Effect,
                Color = request.Color ?? current.Color,
                Brightness = request.Brightness ?? current.Brightness,
                Speed = request.Speed ?? current.Speed,
                // Uma cor pedida sem --arco-iris é para aparecer: tira o arco-íris
                Rainbow = request.Rainbow ?? (request.Color is null && current.Rainbow),
                Reverse = request.Reverse ?? current.Reverse,
            };
            try
            {
                candidate.ToConfig();
                _lighting = candidate;
                _settings.LightOn = true;
                _settings.CustomLightId = null;
                StopLiveForLighting();
                PostState();
                if (await ApplyNowAsync())
                {
                    lines.Add($"Iluminação aplicada: {EffectCatalog.ByKey(candidate.Effect).Label}.");
                }
                else
                {
                    Fail("O teclado não confirmou a iluminação.");
                }
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                Fail(ex.Message);
            }
        }
        if (request.Scheme is { } schemeName)
        {
            var scheme = _settings.CustomLights.FirstOrDefault(s =>
                string.Equals(s.Name, schemeName, StringComparison.CurrentCultureIgnoreCase));
            if (scheme is null)
            {
                Fail($"Esquema de cores não encontrado: {schemeName}.");
            }
            else
            {
                StopLiveForLighting();
                UseCustomLight(scheme.Id);
                lines.Add($"Esquema {scheme.Name} no teclado.");
            }
        }
        if (request.Light is not null || request.ToggleLight)
        {
            var target = request.Light ?? !_settings.LightOn;
            _settings.LightOn = target;
            UpdateLivePause();
            PostState();
            if (await ApplyNowAsync())
            {
                lines.Add(target ? "Luz ligada." : "Luz desligada.");
            }
            else
            {
                Fail("O teclado não confirmou.");
            }
        }
        if (request.Live is { } mode)
        {
            SetLiveMode(mode);
            var channel = CurrentChannel();
            lines.Add(mode == LiveModes.Off
                ? "Modo ao vivo desligado."
                : $"Ao vivo: {LiveModes.ByKey(mode).Label}." +
                  (channel is not null && !LiveModes.Supports(mode, channel) ? " Este modo só funciona pelo cabo USB." : ""));
        }
        if (request.TimerMinutes is { } minutes)
        {
            _settings.Live = CurrentLive with { Timer = CurrentLive.Timer with { Minutes = minutes } };
            _countdown = null;
            HandleTimer("start");
            lines.Add($"Cronômetro de {minutes} min iniciado na fileira F.");
        }
        if (request.TimerStop)
        {
            HandleTimer("stop");
            lines.Add("Cronômetro parado.");
        }
        if (request.AlertColor is { } color)
        {
            if (await ShowAlertAsync(color, request.AlertTimes) is { } problem)
            {
                Fail(problem);
            }
            else
            {
                lines.Add("Aviso enviado.");
            }
        }
        if (request.Status)
        {
            lines.Add(StatusText());
        }
        return new CommandChannel.Reply(ok, lines.Count == 0 ? "Nada a fazer." : string.Join(Environment.NewLine, lines));
    }

    /// <summary>Aplica a iluminação em uso e espera o teclado confirmar (a linha de comando precisa saber).</summary>
    private async Task<bool> ApplyNowAsync()
    {
        Action<Core.KeyboardClient> apply;
        try
        {
            apply = CurrentLightingAction();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return false;
        }
        var sent = await SendAsync(apply);
        if (sent)
        {
            SaveSettings();
        }
        return sent;
    }

    private string StatusText()
    {
        var channel = CurrentChannel();
        var profile = _settings.Profiles.FirstOrDefault(p => p.Id == _settings.ActiveProfileId)?.Name ?? "?";
        var light = !_settings.LightOn ? "luz desligada"
            : ActiveCustomLight() is { } scheme ? $"cores por tecla \"{scheme.Name}\""
            : $"{EffectCatalog.ByKey(_lighting.Effect).Label}, {(_lighting.Rainbow ? "arco-íris" : _lighting.Color)}, brilho {_lighting.Brightness}";
        var parts = new List<string>
        {
            channel switch { "usb" => "Conectado pelo cabo", "dongle" => "Conectado pelo dongle 2.4G", _ => "Teclado não encontrado" },
            $"perfil {profile}",
            light,
        };
        if (CurrentLive.Mode != LiveModes.Off)
        {
            parts.Add($"ao vivo: {LiveModes.ByKey(CurrentLive.Mode).Label}");
        }
        if (_lightOverride != LightOverride.None)
        {
            parts.Add(_lightOverride == LightOverride.Off ? "regra de luz apagando" : "regra de luz no mínimo");
        }
        if (_batteryLevel is { } level)
        {
            parts.Add($"bateria {level}%");
        }
        return string.Join(" | ", parts);
    }

    /// <summary>Configuração salva inválida volta ao padrão, como o resto do settings.json.</summary>
    private static void SanitizeAutomationSettings(AppSettings settings)
    {
        try
        {
            settings.LightRules?.Validate();
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException)
        {
            settings.LightRules = null;
        }
        try
        {
            Hotkeys.ValidateAll(settings.Hotkeys);
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException)
        {
            settings.Hotkeys = [];
        }
        try
        {
            ProfileApps.ValidateAll(settings.Profiles);
        }
        catch (Exception ex) when (ex is ArgumentException or NullReferenceException)
        {
            settings.Profiles = settings.Profiles.Select(p => p with { Apps = [] }).ToList();
        }
        if (settings.AutoProfile is { FallbackId: { } fallback } auto && settings.Profiles.All(p => p.Id != fallback))
        {
            settings.AutoProfile = auto with { FallbackId = null };
        }
    }
}
