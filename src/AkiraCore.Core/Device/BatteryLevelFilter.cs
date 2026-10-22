namespace AkiraCore.Core.Device;

/// <summary>
/// Suaviza o nível de bateria lido pelo dongle. O teclado calcula a porcentagem pela tensão da
/// bateria, que cai enquanto os LEDs puxam corrente, então duas leituras seguidas (15 s) podem
/// diferir alguns pontos (relato do usuário em 21/10/2026: 91 e depois 87). O oficial mostra o
/// valor cru; aqui a tela mostra a mediana das leituras recentes, muda só com pelo menos
/// <see cref="MinSamples"/> leituras e, sem o cabo, só desce: subir exige a janela cheia e folga,
/// que é o caso de carregar fora do PC (o cabo não aparece no Windows).
/// </summary>
public sealed class BatteryLevelFilter
{
    /// <summary>Leituras usadas na mediana (cerca de 1 minuto, com uma a cada 15 s).</summary>
    public const int WindowSize = 5;

    /// <summary>
    /// Leituras na janela antes de mudar um nível já mostrado: depois de um intervalo (teclado
    /// dormindo, cabo ligado ou tirado) a janela recomeça, e uma leitura sozinha não decide nada.
    /// </summary>
    public const int MinSamples = 3;

    /// <summary>
    /// Sem o cabo, a mediana da janela cheia precisa passar o nível mostrado em tanto para subir
    /// (a tensão em repouso, logo depois de o teclado acordar, também lê mais alto).
    /// </summary>
    public const int RiseWithoutCable = 5;

    /// <summary>Leituras mais velhas que isso saem da mediana (teclado dormindo, sem leitura).</summary>
    public static readonly TimeSpan MaxSampleAge = TimeSpan.FromMinutes(2);

    private readonly List<(DateTimeOffset At, int Level)> _samples = [];
    private bool? _charging;
    // Ao abrir o app (ou reconectar) não há nível confiável: a tela segue a mediana até juntar
    // MinSamples leituras, para uma primeira leitura fora da curva não ficar presa
    private bool _settled;

    /// <summary>Nível para mostrar; null antes da primeira leitura.</summary>
    public int? Level { get; private set; }

    /// <param name="reading">Nível cru do dongle; null quando não houve resposta.</param>
    /// <param name="charging">Cabo ligado no PC (o único sinal de carga que o teclado dá).</param>
    /// <param name="now">Hora da leitura.</param>
    /// <returns>O nível para mostrar.</returns>
    public int? Update(int? reading, bool charging, DateTimeOffset now)
    {
        // Cabo entrou ou saiu: a tensão muda de patamar, as leituras antigas não servem mais
        if (_charging != charging)
        {
            _samples.Clear();
            _charging = charging;
        }
        _samples.RemoveAll(s => now - s.At > MaxSampleAge);
        if (reading is not { } value)
        {
            return Level;
        }

        _samples.Add((now, value));
        if (_samples.Count > WindowSize)
        {
            _samples.RemoveAt(0);
        }

        var median = Median(_samples.Select(s => s.Level));
        if (Level is not { } shown || !_settled)
        {
            Level = median;
            _settled = _samples.Count >= MinSamples;
        }
        else if (_samples.Count < MinSamples)
        {
            // Janela recomeçando: mantém o que já estava na tela
        }
        else if (charging)
        {
            // Carregando: o número só sobe
            Level = Math.Max(shown, median);
        }
        else if (median < shown || (_samples.Count >= WindowSize && median >= shown + RiseWithoutCable))
        {
            Level = median;
        }
        return Level;
    }

    /// <summary>Dongle desconectado: a próxima leitura começa do zero.</summary>
    public void Reset()
    {
        _samples.Clear();
        _charging = null;
        _settled = false;
        Level = null;
    }

    // Com número par de leituras, a média das duas do meio, arredondada para cima
    private static int Median(IEnumerable<int> levels)
    {
        var sorted = levels.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle] + 1) / 2;
    }
}
