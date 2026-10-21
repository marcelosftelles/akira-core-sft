using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Keys;

/// <summary>
/// Tabela de remapeamento de uma camada. Teclas sem ação ficam com o slot zerado,
/// que é o comportamento original; por isso a tabela vazia desfaz todo remapeamento.
/// </summary>
public sealed class KeyRemapTable
{
    private readonly Dictionary<int, KeyAction> _actions = [];

    public IReadOnlyDictionary<int, KeyAction> Actions => _actions;

    /// <exception cref="ArgumentException">Tecla fora do layout oficial ou o Fn.</exception>
    public KeyRemapTable Set(int keyIndex, KeyAction action)
    {
        var key = KeyboardLayout.ByIndex(keyIndex)
                  ?? throw new ArgumentException($"O AKIRA CORE não tem tecla no índice {keyIndex}.", nameof(keyIndex));
        if (!key.Remappable)
        {
            throw new ArgumentException("O Fn não pode ser remapeado.", nameof(keyIndex));
        }
        if (action is DefaultKeyAction)
        {
            _actions.Remove(keyIndex);
        }
        else
        {
            _actions[keyIndex] = action;
        }
        return this;
    }

    public byte[] ToBuffer()
    {
        var buffer = new byte[KeyRemapProtocol.BufferLength];
        foreach (var (index, action) in _actions)
        {
            action.ToSlot().CopyTo(buffer, index * 4);
        }
        buffer[KeyRemapProtocol.SignatureOffset] = ProtocolConstants.SignatureLow;
        buffer[KeyRemapProtocol.SignatureOffset + 1] = ProtocolConstants.SignatureHigh;
        return buffer;
    }
}
