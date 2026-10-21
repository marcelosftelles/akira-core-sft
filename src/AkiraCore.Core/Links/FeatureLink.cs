using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Links;

/// <summary>
/// Canal USB com fio: transação 18 → opcode → dados → 02 → F0 por feature reports.
/// Pacotes de dados nunca esperam ACK: o envio multi-pacote do oficial (fcn.0044ed60)
/// ignora o pedido de leitura e só repete o SetFeature se ele falhar.
/// </summary>
public sealed class FeatureLink(IFeaturePort port, IDelay? delay = null) : IKeyboardLink
{
    private readonly IDelay _delay = delay ?? ThreadDelay.Instance;

    public string Description => "USB (feature report)";

    public void SendLightBlock(ReadOnlySpan<byte> block, LightTarget target = LightTarget.Keys)
    {
        // No cabo, teclas e fitas usam a mesma transação; o firmware separa pela
        // faixa do byte 0 (efeitos 0..19, fitas 0x20..0x29). Por isso target é ignorado.
        Run(
        [
            (PacketBuilder.FeatureCommand(Opcode.Begin), true),
            (PacketBuilder.FeatureCommand(Opcode.Lighting, dataPackets: 1), true),
            (PacketBuilder.FeatureData(block), false),
            (PacketBuilder.FeatureCommand(Opcode.End), true),
            (PacketBuilder.FeatureCommand(Opcode.Finalize), false),
        ]);
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
        Run(steps);
    }

    public void SendKeyRemap(KeyLayer layer, ReadOnlySpan<byte> table)
    {
        // fcn.00418550: 04 18 → 04 11 (ou 04 27) com [8] = 9 → 9 pacotes → 04 02 → 04 F0,
        // e aqui o F0 também espera o ACK (push 1 antes do envio em 0x418d8d)
        var opcode = layer == KeyLayer.Fn ? Opcode.FnKeyRemap : Opcode.KeyRemap;
        var steps = new List<(byte[] Report, bool WaitAck)>
        {
            (PacketBuilder.FeatureCommand(Opcode.Begin), true),
            (PacketBuilder.FeatureCommand(opcode, (byte)KeyRemapProtocol.FeatureChunks), true),
        };
        steps.AddRange(PacketBuilder.FeatureChunks(table).Select(r => (r, false)));
        steps.Add((PacketBuilder.FeatureCommand(Opcode.End), true));
        steps.Add((PacketBuilder.FeatureCommand(Opcode.Finalize), true));
        Run(steps);
    }

    public void SendMacros(ReadOnlySpan<byte> data)
    {
        // fcn.0042d5d0: 04 19 → 04 15 com [8] = pacotes → 30 ms → dados → 30 ms → 04 02.
        // Sem 04 18 e sem 04 F0.
        var payload = PacketBuilder.FeatureMacroPayload(data);
        var chunks = PacketBuilder.FeatureChunks(payload);
        Run(
        [
            (PacketBuilder.FeatureCommand(Opcode.MacroBegin), true),
            (PacketBuilder.FeatureCommand(Opcode.MacroData, (byte)chunks.Count), true),
        ]);
        _delay.Wait(MacroProtocol.FeatureStepDelay);
        Run(chunks.Select(r => (r, false)));
        _delay.Wait(MacroProtocol.FeatureStepDelay);
        Run([(PacketBuilder.FeatureCommand(Opcode.End), true)]);
    }

    public void SendDeviceSettings(ReadOnlySpan<byte> block)
    {
        // fcn.004141a0: 04 18 → 04 17 com [2] = 1 e [8] = 1 → dados → 04 02. Sem 04 F0.
        var header = PacketBuilder.FeatureCommand(Opcode.DeviceSettings, dataPackets: 1);
        header[3] = 1;
        Run(
        [
            (PacketBuilder.FeatureCommand(Opcode.Begin), true),
            (header, true),
            (PacketBuilder.FeatureDeviceSettings(block), false),
            (PacketBuilder.FeatureCommand(Opcode.End), true),
        ]);
    }

    // No cabo o app oficial não tem comando de reset: só reenvia os padrões (fcn.00434ee0)
    public bool HasDeviceFactoryReset => false;

    public void SendFactoryReset() =>
        throw new NotSupportedException("O cabo não tem comando de reset de fábrica no app oficial.");

    public bool HasBatteryQuery => false;

    public byte[] QueryBattery() =>
        throw new NotSupportedException("O app oficial só lê a bateria pelo dongle.");

    public bool HasStreaming => true;

    public void SendStreamFrame(ReadOnlySpan<byte> frame)
    {
        // fcn.00427f60 → fcn.0044ef50: 04 20 com [8] = 8 (ACK) → 8 pacotes sem ACK → 04 02 (ACK).
        // Ritmo da função rápida (5 ms), não o da transação normal (35 ms).
        if (frame.Length != StreamProtocol.FrameLength)
        {
            throw new ArgumentException($"O quadro precisa ter {StreamProtocol.FrameLength} bytes.", nameof(frame));
        }
        var chunks = PacketBuilder.FeatureChunks(frame);
        var pause = StreamProtocol.PacketDelay;
        SendFast(PacketBuilder.FeatureCommand(Opcode.Stream, StreamProtocol.FramePackets), StreamProtocol.AckReads, pause);
        foreach (var chunk in chunks)
        {
            SendFast(chunk, ackReads: 0, pause);
        }
        SendFast(PacketBuilder.FeatureCommand(Opcode.End), StreamProtocol.AckReads, pause);
    }

    public bool HasColorReadback => true;

    public byte[] ReadLedColors()
    {
        // fcn.0042adb0: 04 F5 com [8] = 9 → ACK (até 10 leituras de 5 ms) → 9 GET_REPORT → 04 02.
        // Não abre a transação com 04 18.
        var pause = ReadColorsProtocol.PacketDelay;
        SendFast(PacketBuilder.FeatureCommand(Opcode.ReadColors, ReadColorsProtocol.Packets),
            ReadColorsProtocol.AckReads, pause);
        var buffer = new byte[ReadColorsProtocol.Packets * FeatureProtocol.PayloadLength];
        for (var packet = 0; packet < ReadColorsProtocol.Packets; packet++)
        {
            _delay.Wait(pause);
            var report = port.GetFeature(FeatureProtocol.ReportLength)
                         ?? throw new KeyboardException("O teclado não devolveu as cores dos LEDs (USB).");
            report.AsSpan(1, Math.Min(FeatureProtocol.PayloadLength, report.Length - 1))
                .CopyTo(buffer.AsSpan(packet * FeatureProtocol.PayloadLength));
        }
        // O fechamento não decide nada: as cores já chegaram
        _delay.Wait(pause);
        if (port.SetFeature(PacketBuilder.FeatureCommand(Opcode.End)))
        {
            _delay.Wait(pause);
            port.GetFeature(FeatureProtocol.ReportLength);
        }
        return buffer;
    }

    // O ritmo musical do cabo é desenhado pelo app e vai pelo 04 20 (fcn.004328c0)
    public bool HasMusicReport => false;

    public void SendMusicReport(byte[] report) =>
        throw new NotSupportedException("No cabo o ritmo musical vai pelo quadro em tempo real.");

    public bool HasKeyboardReset => false;

    public void SendKeyboardReset() =>
        throw new NotSupportedException("No cabo o \"Reset Keyboard\" do oficial só regrava as tabelas de teclas.");

    /// <summary>Envio da função rápida (fcn.0044ef50): pausa curta e ACK lido várias vezes.</summary>
    private void SendFast(byte[] report, int ackReads, TimeSpan pause)
    {
        _delay.Wait(pause);
        if (!port.SetFeature(report))
        {
            throw new KeyboardException($"Falha ao enviar {report[1]:X2} {report[2]:X2} (USB).");
        }
        if (ackReads == 0)
        {
            return;
        }
        for (var attempt = 0; attempt < ackReads; attempt++)
        {
            _delay.Wait(pause);
            var response = port.GetFeature(FeatureProtocol.ReportLength);
            if (response is not null && response.Length > FeatureProtocol.AckIndex
                                     && response[FeatureProtocol.AckIndex] == 1)
            {
                return;
            }
        }
        throw new KeyboardException($"O teclado não confirmou o comando {report[1]:X2} {report[2]:X2} (USB).");
    }

    private void Run(IEnumerable<(byte[] Report, bool WaitAck)> steps)
    {
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
