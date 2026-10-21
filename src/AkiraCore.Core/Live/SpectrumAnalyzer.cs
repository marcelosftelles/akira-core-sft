namespace AkiraCore.Core.Live;

/// <summary>
/// Transforma o áudio que o PC está tocando em níveis por faixa de frequência (0 a 1), do grave ao
/// agudo, para o ritmo musical. FFT de 2048 pontos com janela de Hann, faixas em escala logarítmica
/// (como o ouvido percebe) e ganho automático: música baixa ou alta enche as barras do mesmo jeito.
/// Sem dependência de Windows: o app entrega as amostras (WASAPI) e pede os níveis a cada quadro.
/// </summary>
public sealed class SpectrumAnalyzer
{
    public const int DefaultBands = 16;
    private const double LowestHz = 40;
    private const double HighestHz = 16000;
    private const double RangeDb = 36;          // faixa entre a barra vazia e a cheia
    private const double SilenceDb = -60;       // abaixo disso é silêncio, não ruído a amplificar
    private const double ReferenceFallDb = 8;   // por segundo: o ganho automático se adapta devagar
    private const double LevelFall = 2.2;       // por segundo: as barras caem suaves

    private readonly int _sampleRate;
    private readonly int _fftSize;
    private readonly float[] _ring;
    private readonly double[] _window;
    private readonly double[] _re;
    private readonly double[] _im;
    private readonly (int From, int To, double TiltDb)[] _bands;
    private readonly float[] _levels;
    private readonly object _gate = new();
    private int _write;
    private long _received;
    private long _analyzed;
    private double _reference = SilenceDb;

    public SpectrumAnalyzer(int sampleRate, int bands = DefaultBands, int fftSize = 2048)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);
        ArgumentOutOfRangeException.ThrowIfLessThan(bands, 1);
        if (fftSize < 64 || (fftSize & (fftSize - 1)) != 0)
        {
            throw new ArgumentException("O tamanho da FFT precisa ser potência de 2 (mínimo 64).", nameof(fftSize));
        }
        _sampleRate = sampleRate;
        _fftSize = fftSize;
        _ring = new float[fftSize];
        _re = new double[fftSize];
        _im = new double[fftSize];
        _window = Enumerable.Range(0, fftSize).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (fftSize - 1))).ToArray();
        _levels = new float[bands];
        _bands = BuildBands(bands);
    }

    public int Bands => _levels.Length;

    /// <summary>Sensibilidade de 1 a 10 (5 é neutra): mais alta, barras mais altas.</summary>
    public int Sensitivity { get; set; } = 5;

    /// <summary>Amostras mono de -1 a 1, na taxa informada no construtor. Pode vir de outra thread.</summary>
    public void AddSamples(ReadOnlySpan<float> mono)
    {
        lock (_gate)
        {
            foreach (var sample in mono)
            {
                _ring[_write] = sample;
                _write = (_write + 1) % _fftSize;
            }
            _received += mono.Length;
        }
    }

    /// <summary>Níveis atuais. <paramref name="deltaSeconds"/> é o tempo desde a chamada anterior.</summary>
    public float[] Analyze(double deltaSeconds)
    {
        deltaSeconds = Math.Clamp(deltaSeconds, 0, 1);
        lock (_gate)
        {
            // O WASAPI não entrega nada enquanto nada toca: sem amostra nova, conta como silêncio
            if (_received == _analyzed)
            {
                var silence = (int)Math.Min(_fftSize, deltaSeconds * _sampleRate);
                for (var i = 0; i < silence; i++)
                {
                    _ring[_write] = 0;
                    _write = (_write + 1) % _fftSize;
                }
            }
            _analyzed = _received;
            for (var i = 0; i < _fftSize; i++)
            {
                _re[i] = _ring[(_write + i) % _fftSize] * _window[i];
                _im[i] = 0;
            }
        }

        Fft(_re, _im);

        // Amplitude relativa ao fundo de escala: 2/N, dividido pelo ganho da janela de Hann (0,5)
        var scale = 4.0 / _fftSize;
        var bandDb = new double[_bands.Length];
        var loudest = double.MinValue;
        for (var band = 0; band < _bands.Length; band++)
        {
            var (from, to, tilt) = _bands[band];
            double peak = 0;
            for (var bin = from; bin <= to; bin++)
            {
                peak = Math.Max(peak, Math.Sqrt(_re[bin] * _re[bin] + _im[bin] * _im[bin]));
            }
            var db = 20 * Math.Log10(peak * scale + 1e-12);
            bandDb[band] = db + tilt;
            loudest = Math.Max(loudest, db);
        }

        _reference = Math.Max(loudest, Math.Max(SilenceDb, _reference - ReferenceFallDb * deltaSeconds));
        var silent = loudest < SilenceDb;
        var gain = 0.6 + Math.Clamp(Sensitivity, 1, 10) * 0.08;
        for (var band = 0; band < _levels.Length; band++)
        {
            var target = silent ? 0 : Math.Clamp((bandDb[band] - (_reference - RangeDb)) / RangeDb * gain, 0, 1);
            var fallen = _levels[band] - LevelFall * deltaSeconds;
            _levels[band] = (float)Math.Max(target, Math.Max(0, fallen));
        }
        return (float[])_levels.Clone();
    }

    private (int, int, double)[] BuildBands(int count)
    {
        var top = Math.Min(HighestHz, _sampleRate / 2.0 * 0.95);
        var binHz = (double)_sampleRate / _fftSize;
        var bands = new (int, int, double)[count];
        for (var i = 0; i < count; i++)
        {
            var low = LowestHz * Math.Pow(top / LowestHz, (double)i / count);
            var high = LowestHz * Math.Pow(top / LowestHz, (double)(i + 1) / count);
            var from = Math.Max(1, (int)Math.Floor(low / binHz));
            var to = Math.Max(from, (int)Math.Ceiling(high / binHz) - 1);
            // Música tem menos energia no agudo: compensa uns 3 dB por oitava acima de 100 Hz
            var center = Math.Sqrt(low * high);
            var tilt = Math.Max(0, 3 * Math.Log2(center / 100));
            bands[i] = (from, Math.Min(to, _fftSize / 2 - 1), tilt);
        }
        return bands;
    }

    // FFT radix-2 iterativa, no lugar
    private static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var (wr, wi) = (Math.Cos(angle), Math.Sin(angle));
            for (var start = 0; start < n; start += length)
            {
                double cr = 1, ci = 0;
                for (var k = 0; k < length / 2; k++)
                {
                    var a = start + k;
                    var b = a + length / 2;
                    var tr = re[b] * cr - im[b] * ci;
                    var ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                    (cr, ci) = (cr * wr - ci * wi, cr * wi + ci * wr);
                }
            }
        }
    }
}
