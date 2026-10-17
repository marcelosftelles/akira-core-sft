using AkiraCore.Core.Links;

namespace AkiraCore.Core.Tests;

// Linhas copiadas dos diagnósticos reais do teclado (cabo e dongle).
// Viraram teste de regressão: se a regra mudar, estes casos protegem.
public class ChannelClassifierTests
{
    [Theory]
    [InlineData(65, 65, 65, ChannelKind.Feature)]  // cabo, mi_02, page 0xFF13
    [InlineData(33, 33, 0, ChannelKind.Dongle)]    // dongle, mi_03, page 0xFF60
    [InlineData(65, 65, 0, ChannelKind.None)]      // dongle, mi_04, page 0xFF59
    [InlineData(9, 2, 0, ChannelKind.None)]        // interface de teclado padrão
    [InlineData(4, 0, 0, ChannelKind.None)]        // page 0xFFFF, notificações
    public void ClassifiesRealCollections(int input, int output, int feature, ChannelKind expected)
    {
        Assert.Equal(expected, ChannelClassifier.Classify(input, output, feature));
    }
}
