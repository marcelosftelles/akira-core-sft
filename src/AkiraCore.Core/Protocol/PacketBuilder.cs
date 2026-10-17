namespace AkiraCore.Core.Protocol;

/// <summary>
/// Monta blocos e envelopes. Funções puras: testáveis sem hardware.
/// </summary>
public static class PacketBuilder
{
    /// <summary>Bloco de 16 bytes com a assinatura AA 55 no fim.</summary>
    public static byte[] LightBlock(IReadOnlyDictionary<int, byte> fields)
    {
        var block = new byte[ProtocolConstants.LightBlockLength];
        foreach (var (index, value) in fields)
        {
            block[index] = value;
        }
        block[ProtocolConstants.SignatureIndex] = ProtocolConstants.SignatureLow;
        block[ProtocolConstants.SignatureIndex + 1] = ProtocolConstants.SignatureHigh;
        return block;
    }

    /// <summary>Liga o modo personalizado (cores por LED) com o brilho dado.</summary>
    public static byte[] CustomModeBlock(int brightness) => LightBlock(new Dictionary<int, byte>
    {
        [0] = ProtocolConstants.CustomModeFlag,
        [ProtocolConstants.BrightnessIndex] = (byte)brightness,
    });

    /// <summary>Report de comando do canal com fio: 00 04 opcode ... [9] = pacotes seguintes.</summary>
    public static byte[] FeatureCommand(Opcode opcode, byte dataPackets = 0)
    {
        var report = new byte[FeatureProtocol.ReportLength];
        report[1] = FeatureProtocol.CommandHeader;
        report[2] = (byte)opcode;
        report[9] = dataPackets;
        return report;
    }

    /// <summary>Report de dados do canal com fio: o bloco vai logo após o Report ID.</summary>
    public static byte[] FeatureData(ReadOnlySpan<byte> block)
    {
        var report = new byte[FeatureProtocol.ReportLength];
        block.CopyTo(report.AsSpan(1));
        return report;
    }

    public static byte[] SideLightBlock(byte mode) => LightBlock(new Dictionary<int, byte> { [0] = mode });

    /// <summary>
    /// Report único do dongle: 00 + cabeçalho + bloco + checksum no byte 32.
    /// Cabeçalho 05 10 00 para as teclas, 05 01 00 para as fitas laterais.
    /// </summary>
    public static byte[] DongleLighting(ReadOnlySpan<byte> block, LightTarget target = LightTarget.Keys)
    {
        var header = target == LightTarget.SideStrips
            ? DongleProtocol.SideLightHeader
            : DongleProtocol.LightingHeader;
        var content = new byte[DongleProtocol.BlockOffset - 1 + block.Length];
        header.CopyTo(content, 0);
        block.CopyTo(content.AsSpan(header.Length));
        return DongleReport(content);
    }

    /// <summary>Envelope genérico do dongle: Report ID 0 + conteúdo + checksum de 8 bits no byte 32.</summary>
    public static byte[] DongleReport(ReadOnlySpan<byte> content)
    {
        if (content.Length > DongleProtocol.ChecksumIndex - 1)
        {
            throw new ArgumentException("Conteúdo não cabe no report do dongle.", nameof(content));
        }
        var report = new byte[DongleProtocol.ReportLength];
        content.CopyTo(report.AsSpan(1));

        var sum = 0;
        for (var i = 0; i < DongleProtocol.ChecksumIndex; i++)
        {
            sum += report[i];
        }
        report[DongleProtocol.ChecksumIndex] = (byte)sum;
        return report;
    }

    /// <summary>Buffer de cores por LED fatiado em 21 reports: 14, tamanho, índice do pedaço, dados.</summary>
    public static IReadOnlyList<byte[]> DonglePerKeyChunks(ReadOnlySpan<byte> buffer)
    {
        var reports = new List<byte[]>();
        for (var chunk = 0; chunk < PerKeyProtocol.DongleChunks; chunk++)
        {
            var isLast = chunk == PerKeyProtocol.DongleChunks - 1;
            var length = isLast ? PerKeyProtocol.DongleLastChunkLength : PerKeyProtocol.DongleChunkData;
            var content = new byte[3 + PerKeyProtocol.DongleChunkData];
            content[0] = PerKeyProtocol.DongleHeader;
            content[1] = (byte)length;
            content[2] = (byte)chunk;
            buffer.Slice(chunk * PerKeyProtocol.DongleChunkData, length).CopyTo(content.AsSpan(3));
            reports.Add(DongleReport(content));
        }
        return reports;
    }

    /// <summary>Buffer de cores por LED fatiado em 9 feature reports de 64 bytes.</summary>
    public static IReadOnlyList<byte[]> FeaturePerKeyChunks(ReadOnlySpan<byte> buffer)
    {
        var reports = new List<byte[]>();
        for (var chunk = 0; chunk < PerKeyProtocol.FeatureChunks; chunk++)
        {
            reports.Add(FeatureData(buffer.Slice(chunk * FeatureProtocol.PayloadLength, FeatureProtocol.PayloadLength)));
        }
        return reports;
    }

    /// <summary>O dongle confirma ecoando os 3 bytes de cabeçalho.</summary>
    public static bool IsDongleEcho(ReadOnlySpan<byte> response, ReadOnlySpan<byte> sentReport) =>
        response.Length > 3 && response.Slice(1, 3).SequenceEqual(sentReport.Slice(1, 3));
}
