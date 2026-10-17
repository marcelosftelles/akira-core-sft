using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Links;

/// <summary>
/// Canal do dongle 2.4G: um output report de 33 bytes com checksum, confirmado
/// quando o dongle devolve um input report ecoando o cabeçalho.
/// Mesmo ritmo do app oficial: 1 retentativa de escrita, até 20 leituras.
/// </summary>
public sealed class DongleLink(IReportPort port, IDelay? delay = null) : IKeyboardLink
{
    private readonly IDelay _delay = delay ?? ThreadDelay.Instance;

    public string Description => "Dongle 2.4G (output report)";

    public void SendLightBlock(ReadOnlySpan<byte> block, LightTarget target = LightTarget.Keys) =>
        SendReport(PacketBuilder.DongleLighting(block, target));

    public void SendPerKeyColors(ReadOnlySpan<byte> buffer)
    {
        foreach (var report in PacketBuilder.DonglePerKeyChunks(buffer))
        {
            SendReport(report);
            _delay.Wait(PerKeyProtocol.DongleChunkDelay);
        }
    }

    public bool HasDeviceFactoryReset => true;

    public void SendFactoryReset()
    {
        SendReport(PacketBuilder.DongleReport([DongleProtocol.FactoryResetCommand]));
        // O oficial espera 50 ms antes de reenviar a iluminação (fcn.00434ee0)
        _delay.Wait(DongleProtocol.FactoryResetSettleDelay);
    }

    private void SendReport(byte[] report)
    {
        if (!port.Write(report))
        {
            _delay.Wait(DongleProtocol.WriteRetryDelay);
            if (!port.Write(report))
            {
                throw new KeyboardException("Falha ao escrever no dongle.");
            }
        }

        _delay.Wait(DongleProtocol.PostWriteDelay);
        for (var attempt = 0; attempt < DongleProtocol.ReadAttempts; attempt++)
        {
            // Outros reports (status, bateria) podem chegar antes: só o eco conta
            var response = port.Read(DongleProtocol.ReportLength, DongleProtocol.ReadTimeout);
            if (response is not null && PacketBuilder.IsDongleEcho(response, report))
            {
                return;
            }
            _delay.Wait(DongleProtocol.ReadRetryDelay);
        }

        throw new KeyboardException(
            "O dongle não confirmou. O teclado pode estar dormindo: aperte uma tecla e tente de novo.");
    }

    public void Dispose() => port.Dispose();
}
