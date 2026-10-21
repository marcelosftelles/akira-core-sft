using static AkiraCore.App.Platform.NativeSystem;

namespace AkiraCore.App.Platform;

/// <summary>
/// Atalhos do Windows registrados na janela principal (RegisterHotKey): funcionam com qualquer
/// programa em foco e chegam como WM_HOTKEY. Cada grupo (atalhos globais, teclas com ação do app)
/// troca os seus de uma vez com <see cref="Replace"/>.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private readonly Dictionary<int, Action> _handlers = [];
    private IntPtr _window;

    public void Attach(IntPtr window) => _window = window;

    /// <summary>
    /// Troca todos os atalhos com id entre <paramref name="firstId"/> e firstId + 999. Devolve os
    /// que o Windows recusou (outro programa já usa a mesma combinação).
    /// </summary>
    public IReadOnlyList<int> Replace(int firstId, IEnumerable<(int Id, uint Modifiers, int VirtualKey, Action Handler)> hotkeys)
    {
        foreach (var id in _handlers.Keys.Where(id => id >= firstId && id < firstId + 1000).ToList())
        {
            UnregisterHotKey(_window, id);
            _handlers.Remove(id);
        }
        var refused = new List<int>();
        if (_window == IntPtr.Zero)
        {
            return refused;
        }
        foreach (var (id, modifiers, virtualKey, handler) in hotkeys)
        {
            if (RegisterHotKey(_window, id, modifiers | ModNoRepeat, (uint)virtualKey))
            {
                _handlers[id] = handler;
            }
            else
            {
                refused.Add(id);
            }
        }
        return refused;
    }

    /// <summary>Chamado pelo WndProc com o WM_HOTKEY.</summary>
    public void Process(IntPtr wParam)
    {
        if (_handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handler();
        }
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys)
        {
            UnregisterHotKey(_window, id);
        }
        _handlers.Clear();
    }
}
