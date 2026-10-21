namespace AkiraCore.App;

/// <summary>
/// Envio em que só o pedido mais recente importa: enquanto um envio roda, os pedidos que
/// chegam viram um só, feito ao terminar. Usado na thread da interface.
/// </summary>
public sealed class LatestOnly(Func<Task> send)
{
    private bool _running;
    private bool _pending;

    public async void Request()
    {
        _pending = true;
        if (_running)
        {
            return;
        }
        _running = true;
        try
        {
            while (_pending)
            {
                _pending = false;
                await send();
            }
        }
        finally
        {
            _running = false;
        }
    }
}
