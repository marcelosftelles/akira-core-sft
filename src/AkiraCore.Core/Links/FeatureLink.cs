using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Links;

/// <summary>
/// Canal USB com fio: transação 18 → 13 → dados → 02 → F0 por feature reports.
/// </summary>
public sealed class FeatureLink(IFeaturePort port, IDelay? delay = null) : IKeyboardLink
{
    private readonly IDelay _delay = delay ?? ThreadDelay.Instance;

    public string Description => "USB (feature report)";

    public void SendLightBlock(ReadOnlySpan<byte> block, LightTarget target = LightTarget.Keys)
    {
        // No cabo, teclas e fitas usam a mesma transação; o firmware separa pela
        // faixa do byte 0 (efeitos 0..19, fitas 0x20..0x29). Por isso target é ignorado.
        (byte[] Report, bool WaitAck)[] steps =
        [
            (PacketBuilder.FeatureCommand(Opcode.Begin), true),
            (PacketBuilder.FeatureCommand(Opcode.Lighting, dataPackets: 1), true),
            (PacketBuilder.FeatureData(block), false),
            (PacketBuilder.FeatureCommand(Opcode.End), true),
            (PacketBuilder.FeatureCommand(Opcode.Finalize), false),
        ];

        foreach (var (report, waitAck) in steps)
        {
            if (!SendOne(report, waitAck))
            {
                throw new KeyboardException(
                    $"O teclado não confirmou o comando {report[1]:X2} {report[2]:X2} (USB).");
            }
        }
    }

    public void SendPerKeyColors(ReadOnlySpan<byte> buffer)
    {
        // Mesmo envelope da iluminação, mas anunciando 9 pacotes e sem ACK nos dados
        var steps = new List<(byte[] Report, bool WaitAck)>
        {
            (PacketBuilder.FeatureCommand(Opcode.Begin), true),
            (PacketBuilder.FeatureCommand(Opcode.PerKeyColors, (byte)PerKeyProtocol.FeatureChunks), true),
        };
        steps.AddRange(PacketBuilder.FeaturePerKeyChunks(buffer).Select(r => (r, false)));
        steps.Add((PacketBuilder.FeatureCommand(Opcode.End), true));
        steps.Add((PacketBuilder.FeatureCommand(Opcode.Finalize), false));

        foreach (var (report, waitAck) in steps)
        {
            if (!SendOne(report, waitAck))
            {
                throw new KeyboardException(
                    $"O teclado não confirmou o comando {report[1]:X2} {report[2]:X2} (USB).");
            }
        }
    }

    private bool SendOne(byte[] report, bool waitAck)
    {
        _delay.Wait(FeatureProtocol.CommandDelay);
        if (!port.SetFeature(report))
        {
            return false;
        }
        if (!waitAck)
        {
            return true;
        }

        _delay.Wait(FeatureProtocol.CommandDelay);
        var response = port.GetFeature(FeatureProtocol.ReportLength);
        return response is not null
               && response.Length > FeatureProtocol.AckIndex
               && response[FeatureProtocol.AckIndex] == 1;
    }

    public void Dispose() => port.Dispose();
}
