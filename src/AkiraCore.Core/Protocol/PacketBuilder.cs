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
    public static IReadOnlyList<byte[]> DonglePerKeyChunks(ReadOnlySpan<byte> buffer) =>
        DongleChunks(PerKeyProtocol.DongleHeader, buffer);

    /// <summary>
    /// Fatia um buffer em reports do dongle de 28 bytes de dados: cabeçalho, tamanho, índice do
    /// pedaço, dados. O mesmo formato serve às cores por LED (14), às teclas (10 e 12) e às
    /// macros (09). O tamanho declarado no último pedaço é o que sobra do buffer, salvo
    /// <paramref name="lastLength"/> (as macros declaram outro valor, ver <see cref="DongleMacroChunks"/>).
    /// </summary>
    public static IReadOnlyList<byte[]> DongleChunks(byte header, ReadOnlySpan<byte> buffer, int? lastLength = null)
    {
        const int size = PerKeyProtocol.DongleChunkData;
        var count = (buffer.Length + size - 1) / size;
        var reports = new List<byte[]>(count);
        for (var chunk = 0; chunk < count; chunk++)
        {
            var available = Math.Min(size, buffer.Length - chunk * size);
            var isLast = chunk == count - 1;
            var content = new byte[3 + size];
            content[0] = header;
            content[1] = (byte)(isLast ? lastLength ?? available : size);
            content[2] = (byte)chunk;
            buffer.Slice(chunk * size, available).CopyTo(content.AsSpan(3));
            reports.Add(DongleReport(content));
        }
        return reports;
    }

    /// <summary>Buffer de cores por LED fatiado em 9 feature reports de 64 bytes.</summary>
    public static IReadOnlyList<byte[]> FeaturePerKeyChunks(ReadOnlySpan<byte> buffer) => FeatureChunks(buffer);

    /// <summary>Fatia um buffer (múltiplo de 64 bytes) em feature reports de dados.</summary>
    public static IReadOnlyList<byte[]> FeatureChunks(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length % FeatureProtocol.PayloadLength != 0)
        {
            throw new ArgumentException("O buffer precisa ter tamanho múltiplo de 64 bytes.", nameof(buffer));
        }
        var reports = new List<byte[]>();
        for (var offset = 0; offset < buffer.Length; offset += FeatureProtocol.PayloadLength)
        {
            reports.Add(FeatureData(buffer.Slice(offset, FeatureProtocol.PayloadLength)));
        }
        return reports;
    }

    /// <summary>
    /// Macros no cabo (fcn.0042d5d0): os dados ocupam um número inteiro de pacotes de 64 bytes,
    /// sempre com um pacote a mais quando o fim não cai na divisa, e o AA 55 nos dois últimos bytes.
    /// </summary>
    public static byte[] FeatureMacroPayload(ReadOnlySpan<byte> data)
    {
        const int size = FeatureProtocol.PayloadLength;
        // Mesma conta do oficial (0x42da83): resto zero → n/64 + 1; senão → n/64 + 2
        var packets = data.Length / size + (data.Length % size == 0 ? 1 : 2);
        var payload = new byte[packets * size];
        data.CopyTo(payload);
        payload[^2] = ProtocolConstants.SignatureLow;
        payload[^1] = ProtocolConstants.SignatureHigh;
        return payload;
    }

    /// <summary>
    /// Macros no dongle (fcn.0042db50): pedaços de 28 bytes com cabeçalho 09. Como no cabo, sobra
    /// um pedaço no fim com o AA 55, e esse último declara como tamanho o resto da divisão por 28.
    /// </summary>
    public static IReadOnlyList<byte[]> DongleMacroChunks(ReadOnlySpan<byte> data)
    {
        const int size = MacroProtocol.DongleChunkData;
        var remainder = data.Length % size;
        var chunks = data.Length / size + (remainder == 0 ? 1 : 2);
        var padded = new byte[chunks * size];
        data.CopyTo(padded);
        padded[^2] = ProtocolConstants.SignatureLow;
        padded[^1] = ProtocolConstants.SignatureHigh;
        return DongleChunks(MacroProtocol.DongleHeader, padded, lastLength: remainder);
    }

    /// <summary>Configurações gerais no cabo: o bloco sem a assinatura, e o AA 55 no fim do payload.</summary>
    public static byte[] FeatureDeviceSettings(ReadOnlySpan<byte> block)
    {
        var payload = new byte[FeatureProtocol.PayloadLength];
        block[..ProtocolConstants.SignatureIndex].CopyTo(payload);
        payload[DeviceSettingsProtocol.FeatureSignatureOffset] = ProtocolConstants.SignatureLow;
        payload[DeviceSettingsProtocol.FeatureSignatureOffset + 1] = ProtocolConstants.SignatureHigh;
        return FeatureData(payload);
    }

    /// <summary>Configurações gerais no dongle: 07 10 00 + bloco de 16 bytes (AA 55 no byte 14 dele).</summary>
    public static byte[] DongleDeviceSettings(ReadOnlySpan<byte> block)
    {
        var header = DeviceSettingsProtocol.DongleHeader;
        var content = new byte[header.Length + block.Length];
        header.CopyTo(content, 0);
        block.CopyTo(content.AsSpan(header.Length));
        return DongleReport(content);
    }

    /// <summary>Pedido do nível da bateria (fcn.004357d0): só o comando, sem dados.</summary>
    public static byte[] DongleBatteryQuery() => DongleReport(BatteryProtocol.DongleQuery);

    /// <summary>
    /// Nível em porcentagem a partir da resposta ao <see cref="DongleBatteryQuery"/>, ou null
    /// quando o oficial também a descartaria (outro comando ou nível 0).
    /// </summary>
    public static int? DongleBatteryLevel(ReadOnlySpan<byte> response)
    {
        if (response.Length <= BatteryProtocol.ResponseLevelIndex
            || response[BatteryProtocol.ResponseCommandIndex] != BatteryProtocol.DongleQuery[0])
        {
            return null;
        }
        var level = response[BatteryProtocol.ResponseLevelIndex];
        return level == 0 ? null : Math.Min((int)level, BatteryProtocol.MaxLevel);
    }

    /// <summary>
    /// Ritmo musical no dongle (fcn.00432b00): 0B 1C 00, modo de frente, modo de fundo, brilho e
    /// a altura de cada coluna. Colunas que sobram ficam zeradas.
    /// </summary>
    public static byte[] DongleMusic(byte frontMode, byte backMode, byte brightness, ReadOnlySpan<byte> heights)
    {
        if (heights.Length > MusicProtocol.MaxColumns)
        {
            throw new ArgumentException($"O report do dongle cabe no máximo {MusicProtocol.MaxColumns} colunas.",
                nameof(heights));
        }
        var header = MusicProtocol.DongleHeader;
        var content = new byte[header.Length + MusicProtocol.ContentLength];
        header.CopyTo(content, 0);
        var data = content.AsSpan(header.Length);
        data[MusicProtocol.FrontModeOffset] = frontMode;
        data[MusicProtocol.BackModeOffset] = backMode;
        data[MusicProtocol.BrightnessOffset] = brightness;
        heights.CopyTo(data[MusicProtocol.HeightsOffset..]);
        return DongleReport(content);
    }

    /// <summary>"Reset Keyboard" do oficial no 2.4G (fcn.00414600): report só com 1F.</summary>
    public static byte[] DongleKeyboardReset() => DongleReport([DongleProtocol.KeyboardResetCommand]);

    /// <summary>
    /// Cores lidas pelo 04 F5 (fcn.0042adb0): a mesma tabela do 04 23, com o LED i em i × 4 como
    /// [i, R, G, B]. Valem as posições cujo primeiro byte é o próprio índice (posição zerada é LED
    /// que o firmware não usa). Se nenhuma posição trouxer o índice, o firmware devolveu só as
    /// cores: aí vale a posição.
    /// </summary>
    public static IReadOnlyDictionary<int, Lighting.Rgb> ParseLedColors(ReadOnlySpan<byte> buffer)
    {
        var colors = new Dictionary<int, Lighting.Rgb>();
        var positional = new Dictionary<int, Lighting.Rgb>();
        for (var index = 1; index <= PerKeyProtocol.MaxLedIndex; index++)
        {
            var offset = index * 4;
            if (offset + 3 >= buffer.Length)
            {
                break;
            }
            var color = new Lighting.Rgb(buffer[offset + 1], buffer[offset + 2], buffer[offset + 3]);
            positional[index] = color;
            if (buffer[offset] == index)
            {
                colors[index] = color;
            }
        }
        return colors.Count > 0 ? colors : positional;
    }

    /// <summary>O dongle confirma ecoando os 3 bytes de cabeçalho.</summary>
    public static bool IsDongleEcho(ReadOnlySpan<byte> response, ReadOnlySpan<byte> sentReport) =>
        response.Length > 3 && response.Slice(1, 3).SequenceEqual(sentReport.Slice(1, 3));
}
