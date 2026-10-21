using AkiraCore.Core.Keys;
using AkiraCore.Core.Links;
using AkiraCore.Core.Presentation;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Tests;

// Remapeamento: fcn.00418550 (cabo), fcn.00418df0 e fcn.00419130 (dongle), tarefa 6 em fcn.00434840.
public class KeyRemapTests
{
    private sealed class NoDelay : IDelay
    {
        public void Wait(TimeSpan duration) { }
    }

    private const int CapsLock = 55;
    private const int Esc = 1;

    [Fact]
    public void Table_PutsSlotAtKeyIndexTimesFourWithSignature()
    {
        var buffer = new KeyRemapTable()
            .Set(CapsLock, new KeyComboAction(KeyModifiers.LeftCtrl, 0x06)) // Ctrl+C
            .ToBuffer();

        Assert.Equal(576, buffer.Length);
        Assert.Equal([0x02, 0x01, 0x06, 0x00], buffer[220..224]);
        Assert.Equal([0xAA, 0x55], buffer[574..576]);
        Assert.Equal(5, buffer.Count(b => b != 0)); // 02 01 06 + AA 55; o resto zerado
    }

    [Fact]
    public void ModifierAsTarget_BecomesBitWithZeroCode()
    {
        // 0x418760: destino entre E0 e E7 sem modificadores → bit no byte 1, código 0
        Assert.Equal([0x02, 0x01, 0x00, 0x00], new KeyComboAction(KeyModifiers.None, 0xE0).ToSlot());
        Assert.Equal([0x02, 0x80, 0x00, 0x00], new KeyComboAction(KeyModifiers.None, 0xE7).ToSlot());
        Assert.Equal([0x02, 0x00, 0x04, 0x00], new KeyComboAction(KeyModifiers.None, 0x04).ToSlot());
    }

    [Fact]
    public void Disabled_IsTypeOneWrittenAsZeroFiveZeroThree()
    {
        // "Desativar tecla" (0x41b896) grava tipo 1, que fcn.00418550 escreve como word 0x0305
        Assert.Equal([0x05, 0x03, 0x00, 0x00], DisabledKeyAction.Instance.ToSlot());
    }

    [Theory]
    [InlineData(MediaKey.PlayPause, 0xCD)]
    [InlineData(MediaKey.Stop, 0xB7)]
    [InlineData(MediaKey.Previous, 0xB6)]
    [InlineData(MediaKey.Next, 0xB5)]
    [InlineData(MediaKey.VolumeUp, 0xE9)]
    [InlineData(MediaKey.VolumeDown, 0xEA)]
    [InlineData(MediaKey.Mute, 0xE2)]
    public void Media_IsTypeThreeWithConsumerCode(MediaKey key, byte code)
    {
        // Tipo 6 da tela → 03 código (0x41899f)
        Assert.Equal([0x03, code, 0x00, 0x00], new MediaKeyAction(key).ToSlot());
    }

    [Theory]
    [InlineData(MouseFunction.LeftClick, 0x01, 0x01)]
    [InlineData(MouseFunction.MiddleClick, 0x01, 0x04)]
    [InlineData(MouseFunction.RightClick, 0x01, 0x02)]
    [InlineData(MouseFunction.DoubleClick, 0x01, 0x03)]
    [InlineData(MouseFunction.ScrollUp, 0x03, 0x01)]
    [InlineData(MouseFunction.ScrollDown, 0x03, 0xFF)]
    [InlineData(MouseFunction.Button4, 0x01, 0x08)]
    [InlineData(MouseFunction.Button5, 0x01, 0x10)]
    public void Mouse_IsTypeOneWithOfficialPairs(MouseFunction function, byte kind, byte value)
    {
        // Tipo 5 da tela (combo "Função de mouse", itens 1 a 8) → 01 + word (0x4188db)
        Assert.Equal([0x01, kind, value, 0x00], new MouseKeyAction(function).ToSlot());
    }

    [Fact]
    public void Macro_IsTypeSixWithPositionModeAndRepeat()
    {
        // 0x418872: 06, posição na lista de macros, [edi+20h] = modo, [edi+24h] = repetições
        Assert.Equal([0x06, 0x02, 0x01, 0x05], new MacroKeyAction(2, MacroPlayback.Repeat, 5).ToSlot());
        Assert.Equal([0x06, 0x00, 0x03, 0x00], new MacroKeyAction(0, MacroPlayback.WhileHeld, 9).ToSlot());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void MacroRepeat_OutsideOneTo255_IsRejected(int repeat)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MacroKeyAction(0, MacroPlayback.Repeat, repeat));
    }

    [Theory]
    [InlineData(0)]    // não existe
    [InlineData(14)]   // índice sem tecla (candidato a fita)
    [InlineData(96)]   // Fn
    public void Table_RejectsKeysOutsideLayoutAndFn(int index)
    {
        Assert.Throws<ArgumentException>(() => new KeyRemapTable().Set(index, DisabledKeyAction.Instance));
    }

    [Fact]
    public void Layout_HasTheEightyTwoOfficialKeys()
    {
        Assert.Equal(82, KeyboardLayout.All.Count);
        Assert.Equal(82, KeyboardLayout.All.Select(k => k.Index).Distinct().Count());
        Assert.Equal(0x39, KeyboardLayout.ByIndex(CapsLock)!.Usage);
        Assert.Equal(0x29, KeyboardLayout.ByIndex(Esc)!.Usage);
    }

    [Fact]
    public void Cable_SendsBothLayersWithElevenAndTwentySeven()
    {
        var port = new RecordingFeaturePort();
        var normal = new KeyRemapTable().Set(CapsLock, DisabledKeyAction.Instance);

        new KeyboardClient(new FeatureLink(port, new NoDelay())).ApplyKeyRemap(normal, new KeyRemapTable());

        // Por camada: 04 18, 04 11/27, 9 pacotes, 04 02, 04 F0
        Assert.Equal(26, port.Written.Count);
        Assert.Equal("000418", Convert.ToHexString(port.Written[0], 0, 3));
        Assert.Equal("000411", Convert.ToHexString(port.Written[1], 0, 3));
        Assert.Equal(9, port.Written[1][9]);
        // CapsLock: offset 220 = pacote 3 (220 / 64), posição 28
        Assert.Equal([0x05, 0x03], port.Written[2 + 3][29..31]);
        Assert.Equal([0xAA, 0x55], port.Written[10][63..65]);
        Assert.Equal("000402", Convert.ToHexString(port.Written[11], 0, 3));
        Assert.Equal("0004F0", Convert.ToHexString(port.Written[12], 0, 3));
        Assert.Equal("000427", Convert.ToHexString(port.Written[14], 0, 3));
    }

    [Fact]
    public void Dongle_SendsTwentyOneChunksPerLayerWithHeaderTenOrTwelve()
    {
        var port = new RecordingReportPort();
        var fn = new KeyRemapTable().Set(Esc, new MediaKeyAction(MediaKey.Mute));

        new KeyboardClient(new DongleLink(port, new NoDelay())).ApplyKeyRemap(new KeyRemapTable(), fn);

        Assert.Equal(42, port.Written.Count);
        Assert.All(port.Written.Take(21).Select((r, i) => (r, i)), c =>
        {
            Assert.Equal(0x10, c.r[1]);
            Assert.Equal(c.i == 20 ? 0x10 : 0x1C, c.r[2]);
            Assert.Equal(c.i, c.r[3]);
        });
        var fnChunks = port.Written.Skip(21).ToList();
        Assert.All(fnChunks, r => Assert.Equal(0x12, r[1]));
        // Esc: offset 4 = pedaço 0, posição 4 dos dados (byte 8 do report)
        Assert.Equal([0x03, 0xE2, 0x00, 0x00], fnChunks[0][8..12]);
        Assert.Equal([0xAA, 0x55], fnChunks[20][18..20]);
    }

    [Fact]
    public void Profile_ResolvesMacroIdToCurrentPosition()
    {
        var macros = new List<MacroState>
        {
            new("a", "Primeira", []),
            new("b", "Segunda", []),
        };
        var (normal, _) = KeyProfile.Build(
            [new KeyAssignmentState(CapsLock, "normal", "macro", MacroId: "b", Playback: "once")], macros);

        Assert.Equal([0x06, 0x01, 0x00, 0x00], normal.Actions[CapsLock].ToSlot());
    }

    [Fact]
    public void Json_FromPageMatchesRecords()
    {
        // Mesmo formato que keys.js manda em setKeys e saveMacros
        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var assignments = System.Text.Json.JsonSerializer.Deserialize<List<KeyAssignmentState>>(
            """[{"key":55,"layer":"normal","action":"key","modifiers":1,"usage":6},{"key":2,"layer":"fn","action":"macro","macroId":"m1","playback":"repeat","repeat":3}]""", web)!;
        var macros = System.Text.Json.JsonSerializer.Deserialize<List<MacroState>>(
            """[{"id":"m1","name":"Teste","events":[{"kind":"down","code":4,"delay":20}]}]""", web)!;

        var (normal, fn) = KeyProfile.Build(assignments, macros);

        Assert.Equal([0x02, 0x01, 0x06, 0x00], normal.Actions[CapsLock].ToSlot());
        Assert.Equal([0x06, 0x00, 0x01, 0x03], fn.Actions[2].ToSlot());
    }

    [Fact]
    public void Profile_RejectsMissingMacroAndIncompleteAction()
    {
        Assert.Throws<ArgumentException>(() => KeyProfile.Build(
            [new KeyAssignmentState(CapsLock, "normal", "macro", MacroId: "x", Playback: "once")], []));
        Assert.Throws<ArgumentException>(() => KeyProfile.Build(
            [new KeyAssignmentState(CapsLock, "normal", "key")], []));
        Assert.Throws<ArgumentException>(() => KeyProfile.Build(
            [new KeyAssignmentState(CapsLock, "normal", "media", Media: "rewind")], []));
    }
}
