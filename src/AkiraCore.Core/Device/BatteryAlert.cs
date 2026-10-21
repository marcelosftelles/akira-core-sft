namespace AkiraCore.Core.Device;

/// <summary>
/// Decide quando avisar que a bateria está fraca: uma vez ao chegar no limite e de novo só
/// depois de carregar acima da folga, para o nível oscilando entre 20 e 21 não repetir o aviso.
/// O oficial não avisa; o limite é o mesmo que a tela usa para pintar a bateria de vermelho.
/// </summary>
public sealed class BatteryAlert
{
    public const int LowLevel = 20;
    public const int RearmLevel = 30;

    private bool _warned;

    /// <param name="level">Nível lido pelo dongle; null quando não há leitura.</param>
    /// <param name="charging">Cabo ligado no PC (o único sinal de carga que o teclado dá).</param>
    /// <returns>True quando é hora de mostrar o aviso.</returns>
    public bool Update(int? level, bool charging)
    {
        if (level is not { } value)
        {
            return false;
        }
        if (charging || value >= RearmLevel)
        {
            _warned = false;
            return false;
        }
        if (value > LowLevel || _warned)
        {
            return false;
        }
        _warned = true;
        return true;
    }
}
