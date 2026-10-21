using System.Collections.Concurrent;
using AkiraCore.Core.Lighting;

namespace AkiraCore.Core.Live;

/// <summary>Instante do quadro: segundos desde o início do fluxo e tempo desde o quadro anterior.</summary>
public readonly record struct LiveTime(double Seconds, double Delta);

/// <summary>
/// Um efeito calculado no PC. Cada quadro, o motor pede a cor de cada LED. As entradas que vêm de
/// fora (áudio, tela, CPU, teclas) chegam por métodos próprios, de outra thread, e cada fonte
/// guarda só o valor mais recente.
/// </summary>
public interface ILiveSource
{
    void Render(LiveFrame frame, LiveTime time);
}

/// <summary>Animação de uma tecla na luz personalizada em tempo real.</summary>
public enum KeyAnimation
{
    Static,
    Breath,
    Blink,
    Spectrum,
    Wave,
    Reactive,
}

public sealed record AnimatedKey(KeyAnimation Animation, Rgb Color);

/// <summary>
/// "Luz personalizada" do oficial (modo contínuo 6, fcn.00427f60; t_customrgb_data com um
/// light_mode por tecla): cada tecla tem a própria animação, calculada aqui e enviada pelo 04 20.
/// </summary>
public sealed class AnimatedScene(IReadOnlyDictionary<int, AnimatedKey> keys, int speed) : ILiveSource
{
    private readonly ConcurrentQueue<int> _presses = new();
    private readonly Dictionary<int, double> _pressedAt = [];
    private readonly double _rate = 0.25 + Math.Clamp(speed, 0, 5) * 0.25;

    /// <summary>Tecla tocada no AKIRA (Raw Input). Vale para as teclas "ao tocar".</summary>
    public void Press(int led) => _presses.Enqueue(led);

    public void Render(LiveFrame frame, LiveTime time)
    {
        while (_presses.TryDequeue(out var led))
        {
            _pressedAt[led] = time.Seconds;
        }
        foreach (var shape in KeyGeometry.All)
        {
            if (keys.TryGetValue(shape.Led, out var key))
            {
                frame[shape.Led] = Color(shape, key, time.Seconds);
            }
        }
    }

    private Rgb Color(KeyShape shape, AnimatedKey key, double t) => key.Animation switch
    {
        KeyAnimation.Breath => ColorMath.Scale(key.Color, 0.5 - 0.5 * Math.Cos(t * _rate * 2 * Math.PI)),
        KeyAnimation.Blink => (t * _rate * 2 % 2) < 1 ? key.Color : default,
        // Matiz que gira a partir do matiz da cor escolhida
        KeyAnimation.Spectrum => ColorMath.Hsv(ColorMath.ToHsv(key.Color).Hue + t * 60 * _rate * 2),
        // Arco-íris correndo da esquerda para a direita
        KeyAnimation.Wave => ColorMath.Hsv(KeyGeometry.NormalizedX(shape) * 360 - t * 120 * _rate),
        KeyAnimation.Reactive => _pressedAt.TryGetValue(shape.Led, out var at)
            ? ColorMath.Scale(key.Color, 1 - (t - at) * _rate * 1.5)
            : default,
        _ => key.Color,
    };
}

public enum MusicStyle
{
    Rainbow,   // cor pela coluna
    Gradient,  // verde embaixo, amarelo no meio, vermelho em cima
    Solid,     // a cor escolhida
}

/// <summary>
/// Ritmo musical (modo contínuo 7, fcn.004332c0). No cabo as barras são desenhadas aqui e vão pelo
/// 04 20 (fcn.004328c0); no dongle o firmware desenha a partir das alturas (fcn.00432b00).
/// </summary>
public sealed class MusicVisualizer(MusicStyle style, Rgb color, bool background) : ILiveSource
{
    private readonly object _gate = new();
    private float[] _levels = [];

    /// <summary>Nível de cada faixa de frequência, de 0 a 1, do grave ao agudo.</summary>
    public void SetLevels(ReadOnlySpan<float> levels)
    {
        var copy = levels.ToArray();
        lock (_gate)
        {
            _levels = copy;
        }
    }

    public void Render(LiveFrame frame, LiveTime time)
    {
        float[] levels;
        lock (_gate)
        {
            levels = _levels;
        }
        foreach (var key in KeyGeometry.All)
        {
            var x = KeyGeometry.NormalizedX(key);
            var level = levels.Length == 0 ? 0 : levels[Math.Min(levels.Length - 1, (int)(x * levels.Length))];
            // Barras crescem de baixo (fileira 5) para cima (fileira 0)
            var fromBottom = KeyGeometry.Rows - 1 - KeyGeometry.RowOf(key);
            var lit = Math.Clamp(level * KeyGeometry.Rows - fromBottom, 0, 1);
            var tint = style switch
            {
                MusicStyle.Rainbow => ColorMath.Hsv(x * 300),
                MusicStyle.Gradient => ColorMath.Hsv(120 - fromBottom / (KeyGeometry.Rows - 1d) * 120),
                _ => color,
            };
            var floor = background ? 0.07 : 0;
            frame[key.Led] = ColorMath.Scale(tint, Math.Max(lit, floor));
        }
    }

    /// <summary>
    /// Alturas do report do dongle: a faixa de cada coluna, em degraus de 0 a <paramref name="maxHeight"/>.
    /// </summary>
    public static byte[] ColumnHeights(ReadOnlySpan<float> levels, int columns, int maxHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHeight, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxHeight, 255);
        var heights = new byte[columns];
        if (levels.Length == 0)
        {
            return heights;
        }
        for (var column = 0; column < columns; column++)
        {
            var band = Math.Min(levels.Length - 1, column * levels.Length / columns);
            heights[column] = (byte)Math.Round(Math.Clamp(levels[band], 0, 1) * maxHeight);
        }
        return heights;
    }
}

/// <summary>
/// Luz que acompanha a tela ("Ambilight", texto que existe no oficial sem uso): cada tecla pega a
/// cor da região da tela que fica na mesma posição, com transição suave.
/// </summary>
public sealed class ScreenAmbient(double smoothingSeconds, bool vivid) : ILiveSource
{
    private readonly object _gate = new();
    private Rgb[] _grid = [];
    private int _width;
    private int _height;
    private readonly double[] _r = new double[Protocol.PerKeyProtocol.MaxLedIndex + 1];
    private readonly double[] _g = new double[Protocol.PerKeyProtocol.MaxLedIndex + 1];
    private readonly double[] _b = new double[Protocol.PerKeyProtocol.MaxLedIndex + 1];

    /// <summary>Tela reduzida a uma grade de cores, linha a linha, de cima para baixo.</summary>
    public void SetSamples(Rgb[] grid, int width, int height)
    {
        if (grid.Length != width * height || width < 1 || height < 1)
        {
            throw new ArgumentException("A grade não bate com a largura e a altura.", nameof(grid));
        }
        lock (_gate)
        {
            (_grid, _width, _height) = (grid, width, height);
        }
    }

    public void Render(LiveFrame frame, LiveTime time)
    {
        Rgb[] grid;
        int width, height;
        lock (_gate)
        {
            (grid, width, height) = (_grid, _width, _height);
        }
        if (grid.Length == 0)
        {
            return;
        }
        var alpha = smoothingSeconds <= 0 ? 1 : 1 - Math.Exp(-Math.Max(time.Delta, 0) / smoothingSeconds);
        foreach (var key in KeyGeometry.All)
        {
            var column = (int)Math.Round(KeyGeometry.NormalizedX(key) * (width - 1));
            var row = (int)Math.Round(KeyGeometry.NormalizedY(key) * (height - 1));
            var target = grid[row * width + column];
            if (vivid)
            {
                target = ColorMath.Vivid(target);
            }
            var i = key.Led;
            _r[i] += (target.R - _r[i]) * alpha;
            _g[i] += (target.G - _g[i]) * alpha;
            _b[i] += (target.B - _b[i]) * alpha;
            frame[i] = new Rgb((byte)Math.Round(_r[i]), (byte)Math.Round(_g[i]), (byte)Math.Round(_b[i]));
        }
    }
}

public enum CpuStyle
{
    Bar,    // fileira dos números como barra de uso
    Whole,  // o teclado inteiro muda de cor
}

/// <summary>Uso da CPU no teclado: de <c>low</c> (ocioso) a <c>high</c> (no limite).</summary>
public sealed class CpuMeter(CpuStyle style, Rgb low, Rgb high) : ILiveSource
{
    private static readonly IReadOnlyList<KeyShape> NumberRow = KeyGeometry.Row(1).Where(k => k.Name != "Home").ToList();
    private double _target;
    private double _shown;

    /// <summary>Uso de 0 a 1.</summary>
    public void SetUsage(double usage) => Volatile.Write(ref _target, Math.Clamp(usage, 0, 1));

    public void Render(LiveFrame frame, LiveTime time)
    {
        // Suaviza: a leitura chega a cada segundo, o quadro a cada poucos milissegundos
        _shown += (Volatile.Read(ref _target) - _shown) * (1 - Math.Exp(-Math.Max(time.Delta, 0) / 0.4));
        var now = ColorMath.Lerp(low, high, _shown);
        if (style == CpuStyle.Whole)
        {
            frame.Fill(now);
            return;
        }
        frame.Fill(ColorMath.Scale(now, 0.08));
        var lit = _shown * NumberRow.Count;
        for (var i = 0; i < NumberRow.Count; i++)
        {
            var tint = ColorMath.Lerp(low, high, i / (NumberRow.Count - 1d));
            frame[NumberRow[i].Led] = ColorMath.Scale(tint, Math.Max(0.08, Math.Clamp(lit - i, 0, 1)));
        }
    }
}

/// <summary>
/// Cronômetro regressivo na fileira F: F1 a F12 acesas mostram o tempo que falta. O relógio é de
/// quem chama (o app passa a hora atual), para dar para testar sem esperar.
/// </summary>
public sealed class CountdownTimer
{
    private readonly object _gate = new();
    private DateTime? _endsAt;
    private TimeSpan _pausedRemaining;

    public CountdownTimer(TimeSpan total)
    {
        if (total <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(total), "O cronômetro precisa de um tempo maior que zero.");
        }
        Total = total;
        _pausedRemaining = total;
    }

    public TimeSpan Total { get; }

    public bool Running
    {
        get
        {
            lock (_gate)
            {
                return _endsAt is not null;
            }
        }
    }

    public void Start(DateTime now)
    {
        lock (_gate)
        {
            _endsAt ??= now + _pausedRemaining;
        }
    }

    public void Pause(DateTime now)
    {
        lock (_gate)
        {
            if (_endsAt is { } end)
            {
                _pausedRemaining = Max(end - now, TimeSpan.Zero);
                _endsAt = null;
            }
        }
    }

    public TimeSpan Remaining(DateTime now)
    {
        lock (_gate)
        {
            return _endsAt is { } end ? Max(end - now, TimeSpan.Zero) : _pausedRemaining;
        }
    }

    public bool IsFinished(DateTime now) => Remaining(now) == TimeSpan.Zero;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}

/// <summary>Desenho do cronômetro: F1 a F12 como barra do tempo restante; o resto do teclado bem fraco.</summary>
public sealed class TimerBar(CountdownTimer timer, Rgb color, Func<DateTime> clock) : ILiveSource
{
    private static readonly IReadOnlyList<KeyShape> FunctionRow =
        KeyGeometry.Row(0).Where(k => k.Name.StartsWith('F')).ToList();

    public void Render(LiveFrame frame, LiveTime time)
    {
        var fraction = timer.Remaining(clock()) / timer.Total;
        frame.Fill(ColorMath.Scale(color, 0.05));
        var lit = fraction * FunctionRow.Count;
        // No último décimo a barra pulsa, para chamar a atenção
        var pulse = fraction < 0.1 && timer.Running ? 0.55 + 0.45 * Math.Cos(time.Seconds * 2 * Math.PI) : 1;
        for (var i = 0; i < FunctionRow.Count; i++)
        {
            frame[FunctionRow[i].Led] = ColorMath.Scale(color, Math.Max(0.05, Math.Clamp(lit - i, 0, 1) * pulse));
        }
    }
}

/// <summary>Aviso: o teclado inteiro pisca numa cor algumas vezes, por cima do que estiver no fluxo.</summary>
public sealed class AlertFlash(Rgb color, int times)
{
    public const double Period = 0.5;
    public const int MaxTimes = 20;

    private readonly int _times = Math.Clamp(times, 1, MaxTimes);
    private double? _start;

    public double Duration => _times * Period;

    /// <summary>Pinta o aviso no quadro. Falso quando acabou (o quadro fica como estava).</summary>
    public bool Apply(LiveFrame frame, double seconds)
    {
        _start ??= seconds;
        var phase = (seconds - _start.Value) / Period;
        if (phase >= _times)
        {
            return false;
        }
        frame.Fill(phase % 1 < 0.55 ? color : default);
        return true;
    }
}

/// <summary>
/// Junta a fonte em uso, o aviso por cima e o brilho. O app chama <see cref="Render"/> na thread do
/// fluxo; trocar a fonte ou pedir um aviso pode vir de qualquer thread.
/// </summary>
public sealed class LiveEngine
{
    private readonly object _gate = new();
    private ILiveSource? _source;
    private AlertFlash? _alert;
    private double? _last;

    public int Brightness { get; set; } = Protocol.ProtocolConstants.BrightnessMax;

    public ILiveSource? Source
    {
        get
        {
            lock (_gate)
            {
                return _source;
            }
        }
        set
        {
            lock (_gate)
            {
                _source = value;
            }
        }
    }

    /// <summary>Há algo para desenhar: uma fonte ou um aviso em andamento.</summary>
    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return _source is not null || _alert is not null;
            }
        }
    }

    public void ShowAlert(Rgb color, int times)
    {
        lock (_gate)
        {
            _alert = new AlertFlash(color, times);
        }
    }

    public LiveFrame Render(double seconds)
    {
        var frame = new LiveFrame();
        lock (_gate)
        {
            var delta = _last is { } last ? Math.Max(0, seconds - last) : 0;
            _last = seconds;
            _source?.Render(frame, new LiveTime(seconds, delta));
            if (_alert is not null && !_alert.Apply(frame, seconds))
            {
                _alert = null;
                frame.Clear();
                _source?.Render(frame, new LiveTime(seconds, 0));
            }
        }
        frame.Scale(ColorMath.BrightnessFactor(Brightness));
        return frame;
    }
}
