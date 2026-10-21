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

    public void SendPerKeyColors(ReadOnlySpan<byte> buffer) =>
        SendChunks(PacketBuilder.DonglePerKeyChunks(buffer), PerKeyProtocol.DongleChunkDelay);

    public void SendKeyRemap(KeyLayer layer, ReadOnlySpan<byte> table)
    {
        // fcn.00418df0: 21 pedaços com cabeçalho 10 (camada normal) ou 12 (camada Fn), 2 ms entre eles
        var header = layer == KeyLayer.Fn ? KeyRemapProtocol.DongleFnHeader : KeyRemapProtocol.DongleHeader;
        _delay.Wait(KeyRemapProtocol.DongleSettleDelay);
        SendChunks(PacketBuilder.DongleChunks(header, table), PerKeyProtocol.DongleChunkDelay);
    }

    // fcn.0042db50: só os pedaços com cabeçalho 09; o dongle não usa o 04 19 do cabo
    public void SendMacros(ReadOnlySpan<byte> data) =>
        SendChunks(PacketBuilder.DongleMacroChunks(data), MacroProtocol.DongleChunkDelay);

    // fcn.00414410: report único 07 10 00 + bloco
    public void SendDeviceSettings(ReadOnlySpan<byte> block) =>
        SendReport(PacketBuilder.DongleDeviceSettings(block));

    private void SendChunks(IEnumerable<byte[]> reports, TimeSpan pause)
    {
        foreach (var report in reports)
        {
            SendReport(report);
            _delay.Wait(pause);
        }
    }

    public bool HasDeviceFactoryReset => true;

    public void SendFactoryReset()
    {
        SendReport(PacketBuilder.DongleReport([DongleProtocol.FactoryResetCommand]));
        // O oficial espera 50 ms antes de reenviar a iluminação (fcn.00434ee0)
        _delay.Wait(DongleProtocol.FactoryResetSettleDelay);
    }

    public bool HasBatteryQuery => true;

    // fcn.004357d0: mesma transação dos outros comandos; o nível vem no eco
    public byte[] QueryBattery() => SendReport(PacketBuilder.DongleBatteryQuery());

    /// <summary>Envia e espera o eco; devolve o report de confirmação inteiro.</summary>
    private byte[] SendReport(byte[] report)
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
                return response;
            }
            _delay.Wait(DongleProtocol.ReadRetryDelay);
        }

        throw new KeyboardException(
            "O dongle não confirmou. O teclado pode estar dormindo: aperte uma tecla e tente de novo.");
    }

    public void Dispose() => port.Dispose();
}
