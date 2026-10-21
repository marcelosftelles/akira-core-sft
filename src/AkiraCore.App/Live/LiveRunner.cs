using System.ComponentModel;
using System.Diagnostics;
using AkiraCore.Core;
using AkiraCore.Core.Lighting;
using AkiraCore.Core.Links;
using AkiraCore.Core.Live;

namespace AkiraCore.App.Live;

/// <summary>Calibração do ritmo musical no dongle (Laboratório), enquanto o formato não é confirmado.</summary>
public sealed class DongleMusicCalibration
{
    public int Columns { get; set; } = Core.Protocol.MusicProtocol.DefaultColumns;
    public int MaxHeight { get; set; } = Core.Protocol.MusicProtocol.DefaultMaxHeight;
    public int FrontMode { get; set; }
    public int BackMode { get; set; }

    public DongleMusicCalibration Sanitized() => new()
    {
        Columns = Math.Clamp(Columns, 1, Core.Protocol.MusicProtocol.MaxColumns),
        MaxHeight = Math.Clamp(MaxHeight, 1, 255),
        FrontMode = Math.Clamp(FrontMode, 0, 255),
        BackMode = Math.Clamp(BackMode, 0, 255),
    };
}

/// <summary>O que o laço está conseguindo fazer, para a tela.</summary>
public enum LiveSendState
{
    Idle,          // nada para mandar (pausado, simulação ou sem fonte)
    Sending,       // quadros chegando ao teclado
    NoKeyboard,    // teclado não encontrado
    WrongChannel,  // o modo precisa do cabo e o teclado está no dongle
    Failing,       // o teclado parou de confirmar
}

/// <summary>
/// Laço dos efeitos ao vivo: a cada quadro alimenta a fonte (áudio, tela, CPU), pede o desenho ao
/// <see cref="LiveEngine"/> e manda ao teclado. Cabo: quadro inteiro pelo 04 20, sem gravar nada.
/// Dongle: só o ritmo musical, com as alturas das colunas. Divide o teclado com os outros envios
/// pelo mesmo semáforo, um quadro por vez, e pula o quadro quando outro envio está no meio.
/// </summary>
public sealed class LiveRunner(LiveEngine engine, Func<IKeyboardLink?> openLink, SemaphoreSlim keyboardLock) : IDisposable
{
    private static readonly TimeSpan CablePeriod = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan DonglePeriod = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan IdlePeriod = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan PreviewPeriod = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan ReopenDelay = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly ScreenSampler _screen = new();
    private readonly CpuSampler _cpu = new();
    private SpectrumAnalyzer _analyzer = new(48000);
    private AudioCapture? _audio;
    private CancellationTokenSource? _run;
    private Thread? _thread;
    private LiveSendState _state = LiveSendState.Idle;

    /// <summary>Não envia (luz desligada, regra de luz, simulação); a prévia continua.</summary>
    public volatile bool Paused;

    /// <summary>Avisos piscam mesmo com a luz pausada (um aviso pedido é para ser visto).</summary>
    public volatile bool AlertsIgnorePause;

    /// <summary>Janela visível: vale a pena desenhar a prévia.</summary>
    public volatile bool PreviewWanted;

    public volatile bool SwapRedBlue;

    public volatile int Sensitivity = 5;

    public DongleMusicCalibration DongleMusic { get; set; } = new();

    public bool Running
    {
        get
        {
            lock (_gate)
            {
                return _run is not null;
            }
        }
    }

    public event Action<LiveFrame>? Preview;
    public event Action<LiveSendState, string?>? StateChanged;

    /// <summary>Parou de mandar quadros ao cabo sem fechar o laço (pausa): a luz normal deve voltar.</summary>
    public event Action? StreamReleased;

    /// <summary>O laço terminou (nada mais para desenhar). True se algo foi para o cabo.</summary>
    public event Action<bool>? Stopped;

    public void Start()
    {
        lock (_gate)
        {
            if (_run is not null)
            {
                return;
            }
            var run = new CancellationTokenSource();
            _run = run;
            _thread = new Thread(() => Loop(run)) { IsBackground = true, Name = "AkiraCore ao vivo" };
            _thread.Start();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _run?.Cancel();
        }
    }

    /// <summary>Para e espera o laço terminar (ao fechar o app, antes de devolver a luz normal).</summary>
    public void StopAndWait(TimeSpan timeout)
    {
        Thread? thread;
        lock (_gate)
        {
            _run?.Cancel();
            thread = _thread;
        }
        if (thread is not null && thread != Thread.CurrentThread)
        {
            thread.Join(timeout);
        }
    }

    private void Loop(CancellationTokenSource run)
    {
        var token = run.Token;
        var clock = Stopwatch.StartNew();
        var lastPreview = TimeSpan.MinValue;
        var lastCpu = TimeSpan.MinValue;
        var lastTick = 0.0;
        var nextOpen = TimeSpan.Zero;
        var failures = 0;
        var streaming = false;
        var streamedToCable = false;
        IKeyboardLink? link = null;
        float[] levels = [];
        try
        {
            while (!token.IsCancellationRequested && engine.IsActive)
            {
                var started = clock.Elapsed;
                var seconds = started.TotalSeconds;
                var source = engine.Source;

                // Entradas da fonte em uso
                if (source is MusicVisualizer music)
                {
                    EnsureAudio();
                    var analyzer = Volatile.Read(ref _analyzer);
                    analyzer.Sensitivity = Sensitivity;
                    levels = analyzer.Analyze(seconds - lastTick);
                    music.SetLevels(levels);
                }
                else
                {
                    StopAudio();
                }
                if (source is ScreenAmbient ambient && _screen.Sample() is { } grid)
                {
                    ambient.SetSamples(grid, ScreenSampler.Width, ScreenSampler.Height);
                }
                if (source is CpuMeter cpu && started - lastCpu >= TimeSpan.FromSeconds(1))
                {
                    lastCpu = started;
                    if (_cpu.Sample() is { } usage)
                    {
                        cpu.SetUsage(usage);
                    }
                }
                lastTick = seconds;

                var alert = engine.HasAlert;
                var frame = engine.Render(seconds);
                if (PreviewWanted && started - lastPreview >= PreviewPeriod)
                {
                    lastPreview = started;
                    Preview?.Invoke(frame);
                }

                var period = IdlePeriod;
                if (Paused && !(alert && AlertsIgnorePause))
                {
                    Report(LiveSendState.Idle, null);
                    link?.Dispose();
                    link = null;
                    if (streaming)
                    {
                        streaming = false;
                        StreamReleased?.Invoke();
                    }
                }
                else
                {
                    if (link is null && started >= nextOpen)
                    {
                        link = TryOpen();
                        nextOpen = started + ReopenDelay;
                        if (link is null)
                        {
                            Report(LiveSendState.NoKeyboard, "Teclado não encontrado");
                        }
                    }
                    if (link is not null)
                    {
                        period = link.HasStreaming ? CablePeriod : DonglePeriod;
                        var result = Send(link, frame, source, levels, ref failures);
                        if (result == SendResult.Streamed)
                        {
                            streaming = streamedToCable = true;
                        }
                        else if (result == SendResult.Reopen)
                        {
                            link.Dispose();
                            link = null;
                            nextOpen = started + ReopenDelay;
                        }
                    }
                }

                var rest = period - (clock.Elapsed - started);
                if (rest > TimeSpan.Zero && token.WaitHandle.WaitOne(rest))
                {
                    break;
                }
            }
        }
        finally
        {
            link?.Dispose();
            StopAudio();
            Report(LiveSendState.Idle, null);
            lock (_gate)
            {
                if (_run == run)
                {
                    _run = null;
                }
            }
            run.Dispose();
            Stopped?.Invoke(streamedToCable);
        }
    }

    private enum SendResult
    {
        Sent,
        Streamed,
        Skipped,
        Reopen,
    }

    private SendResult Send(IKeyboardLink link, LiveFrame frame, ILiveSource? source, float[] levels, ref int failures)
    {
        var musicOnDongle = !link.HasStreaming && link.HasMusicReport && source is MusicVisualizer;
        if (!link.HasStreaming && !musicOnDongle)
        {
            Report(LiveSendState.WrongChannel, "Este modo só funciona pelo cabo USB");
            return SendResult.Skipped;
        }
        // Outro envio (luz, teclas, bateria) no meio: este quadro fica de fora
        if (!TryEnter(keyboardLock))
        {
            return SendResult.Skipped;
        }
        try
        {
            var client = new KeyboardClient(link);
            if (link.HasStreaming)
            {
                client.SendLiveFrame(frame, SwapRedBlue);
            }
            else
            {
                var calibration = DongleMusic.Sanitized();
                var heights = MusicVisualizer.ColumnHeights(levels, calibration.Columns, calibration.MaxHeight);
                client.SendMusicHeights(heights, Math.Clamp(engine.Brightness, 0, Core.Protocol.ProtocolConstants.BrightnessMax),
                    (byte)calibration.FrontMode, (byte)calibration.BackMode);
            }
            failures = 0;
            Report(LiveSendState.Sending, null);
            return link.HasStreaming ? SendResult.Streamed : SendResult.Sent;
        }
        catch (Exception ex) when (ex is KeyboardException or Win32Exception or IOException)
        {
            // Um quadro perdido é normal (teclado ocupado); três seguidos, reabre o canal
            if (++failures < 3)
            {
                return SendResult.Skipped;
            }
            failures = 0;
            Report(LiveSendState.Failing, ex.Message);
            return SendResult.Reopen;
        }
        finally
        {
            keyboardLock.Release();
        }
    }

    /// <summary>Reserva o teclado sem esperar; false também se o app estiver fechando.</summary>
    internal static bool TryEnter(SemaphoreSlim keyboardLock)
    {
        try
        {
            return keyboardLock.Wait(0);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private IKeyboardLink? TryOpen()
    {
        try
        {
            return openLink();
        }
        catch (Exception ex) when (ex is KeyboardException or Win32Exception or IOException)
        {
            return null;
        }
    }

    private void EnsureAudio()
    {
        if (_audio is not null)
        {
            return;
        }
        _audio = new AudioCapture(samples => Volatile.Read(ref _analyzer).AddSamples(samples));
        _audio.SampleRateChanged += rate => Volatile.Write(ref _analyzer, new SpectrumAnalyzer(rate));
        _audio.Start();
    }

    private void StopAudio()
    {
        _audio?.Dispose();
        _audio = null;
    }

    private void Report(LiveSendState state, string? detail)
    {
        if (state == _state && state != LiveSendState.Failing)
        {
            return;
        }
        _state = state;
        StateChanged?.Invoke(state, detail);
    }

    public void Dispose() => StopAndWait(TimeSpan.FromSeconds(1));
}

/// <summary>
/// Espelho dos LEDs (modo contínuo 3 do oficial, fcn.0042adb0): lê as cores pelo 04 F5 algumas
/// vezes por segundo, para a prévia mostrar o que o teclado acende de verdade. Só no cabo.
/// </summary>
public sealed class LedMirror(Func<IKeyboardLink?> openLink, SemaphoreSlim keyboardLock) : IDisposable
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private readonly object _gate = new();
    private CancellationTokenSource? _run;
    private Thread? _thread;

    public volatile bool SwapRedBlue;

    public bool Running
    {
        get
        {
            lock (_gate)
            {
                return _run is not null;
            }
        }
    }

    public event Action<IReadOnlyDictionary<int, Rgb>>? Colors;

    /// <summary>Motivo de não conseguir ler (dongle, sem teclado, sem resposta).</summary>
    public event Action<string>? Problem;

    public void Start()
    {
        lock (_gate)
        {
            if (_run is not null)
            {
                return;
            }
            var run = new CancellationTokenSource();
            _run = run;
            _thread = new Thread(() => Loop(run)) { IsBackground = true, Name = "AkiraCore espelho" };
            _thread.Start();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _run?.Cancel();
            _run = null;
        }
    }

    private void Loop(CancellationTokenSource run)
    {
        var token = run.Token;
        IKeyboardLink? link = null;
        var failures = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var started = DateTime.UtcNow;
                var wait = Period;
                try
                {
                    link ??= openLink();
                    if (link is null)
                    {
                        Problem?.Invoke("Teclado não encontrado");
                        wait = RetryDelay;
                    }
                    else if (!link.HasColorReadback)
                    {
                        Problem?.Invoke("O teclado de verdade só aparece pelo cabo USB");
                        link.Dispose();
                        link = null;
                        wait = RetryDelay;
                    }
                    else if (LiveRunner.TryEnter(keyboardLock))
                    {
                        try
                        {
                            if (new KeyboardClient(link).ReadLedColors(SwapRedBlue) is { } colors && !token.IsCancellationRequested)
                            {
                                Colors?.Invoke(colors);
                            }
                            failures = 0;
                        }
                        finally
                        {
                            keyboardLock.Release();
                        }
                    }
                }
                catch (Exception ex) when (ex is KeyboardException or Win32Exception or IOException)
                {
                    if (++failures >= 3)
                    {
                        Problem?.Invoke("O teclado não devolveu as cores: " + ex.Message);
                        link?.Dispose();
                        link = null;
                        failures = 0;
                        wait = RetryDelay;
                    }
                }
                var rest = wait - (DateTime.UtcNow - started);
                if (rest > TimeSpan.Zero && token.WaitHandle.WaitOne(rest))
                {
                    break;
                }
            }
        }
        finally
        {
            link?.Dispose();
            run.Dispose();
        }
    }

    public void Dispose()
    {
        Stop();
        _thread?.Join(TimeSpan.FromSeconds(1));
    }
}
