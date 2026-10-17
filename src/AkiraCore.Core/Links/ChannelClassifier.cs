using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Links;

public enum ChannelKind
{
    None,
    Feature,   // USB com fio: feature report de 65 bytes
    Dongle,    // 2.4G: input e output de 33 bytes
}

/// <summary>
/// Decide o papel de uma coleção HID só pelos tamanhos de report, como o
/// app oficial faz em fcn.0044f900. O número da interface (MI_xx) não entra:
/// no cabo a coleção certa está em MI_02, apesar do config.xml dizer MI_00.
/// </summary>
public static class ChannelClassifier
{
    public static ChannelKind Classify(int inputLength, int outputLength, int featureLength) =>
        featureLength == FeatureProtocol.ReportLength ? ChannelKind.Feature
        : inputLength == DongleProtocol.ReportLength && outputLength == DongleProtocol.ReportLength
            ? ChannelKind.Dongle
            : ChannelKind.None;
}
