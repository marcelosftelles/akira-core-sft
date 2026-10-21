using AkiraCore.Core.Links;
using AkiraCore.Core.Macros;
using AkiraCore.Core.Presentation;
using AkiraCore.Core.Protocol;

namespace AkiraCore.Core.Tests;

// Macros: fcn.0042d5d0 (cabo) e fcn.0042db50 (dongle), que montam o mesmo buffer.
public class MacroTests
{
    private sealed class NoDelay : IDelay
    {
        public void Wait(TimeSpan duration) { }
    }

    private static Macro TypeA(int delay = 50) => new("A", [
        new MacroEvent(MacroEventKind.KeyDown, 0x04, delay),
        new MacroEvent(MacroEventKind.KeyUp, 0x04, delay),
    ]);

    [Fact]
    public void Encode_WritesHeaderOffsetBlockAndPairsActionWithDelay()
    {
        var data = MacroEncoder.Encode([TypeA()]);

        Assert.Equal([0x90, 0x01, 0x00, 0x00], data[0..4]);        // macro 0 em 0x190
        Assert.Equal([0x04, 0x00], data[0x190..0x192]);             // 2 ações viram 4 eventos
        Assert.Equal(new byte[6], data[0x192..0x198]);
        Assert.Equal([0x00, 0x00, 0x04, 0xB0], data[0x198..0x19C]); // A pressionado
        Assert.Equal([0x32, 0x00, 0x00, 0x50], data[0x19C..0x1A0]); // 50 ms
        Assert.Equal([0x00, 0x00, 0x04, 0x30], data[0x1A0..0x1A4]); // A solto
        Assert.Equal(0x190 + 8 + 4 * 4, data.Length);
    }

    [Fact]
    public void Encode_ClampsDelayToTenAndKeepsSixteenBits()
    {
        // 0x42d909: atraso = max(atraso, 10), gravado em 2 bytes
        var data = MacroEncoder.Encode([TypeA(delay: 0), TypeA(delay: 1000)]);

        Assert.Equal([0x0A, 0x00], data[0x19C..0x19E]);
        var second = data[4] | data[5] << 8;
        Assert.Equal([0xE8, 0x03], data[(second + 12)..(second + 14)]);
    }

    [Fact]
    public void Encode_MouseUsesHidButtonBits()
    {
        var data = MacroEncoder.Encode([new Macro("Clique", [
            new MacroEvent(MacroEventKind.MouseDown, (byte)MouseButton.Middle, 10),
            new MacroEvent(MacroEventKind.MouseUp, (byte)MouseButton.Right, 10),
        ])]);

        Assert.Equal([0x04, 0x90], data[0x19A..0x19C]);
        Assert.Equal([0x02, 0x10], data[0x1A2..0x1A4]);
    }

    [Fact]
    public void Encode_EmptyMacroGetsFfHeaderAndNoBlock()
    {
        var data = MacroEncoder.Encode([new Macro("Vazia", []), TypeA()]);

        Assert.Equal([0xFF, 0xFF, 0xFF, 0xFF], data[0..4]);
        Assert.Equal([0x90, 0x01, 0x00, 0x00], data[4..8]);
        Assert.Equal(new byte[4], data[8..12]); // sem macro: zerado
    }

    [Fact]
    public void Encode_RejectsAboveOfficialSizeLimit()
    {
        // (macros + ações) × 8 ≤ 0xA8C (0x42d61c)
        var events = Enumerable.Range(0, 337).Select(_ => new MacroEvent(MacroEventKind.KeyDown, 0x04, 10)).ToList();
        Assert.Equal(2704, MacroEncoder.Size([new Macro("Grande", events)]));
        Assert.Throws<ArgumentException>(() => MacroEncoder.Encode([new Macro("Grande", events)]));
        MacroEncoder.Encode([new Macro("Cabe", events.Take(336).ToList())]);
    }

    [Fact]
    public void Event_RejectsUnknownKeyAndButton()
    {
        Assert.Throws<ArgumentException>(() => new MacroEvent(MacroEventKind.KeyDown, 0xAF, 10));
        Assert.Throws<ArgumentException>(() => new MacroEvent(MacroEventKind.MouseDown, 0x03, 10));
    }

    [Fact]
    public void Cable_SendsBeginHeaderDataAndEnd()
    {
        var port = new RecordingFeaturePort();

        new KeyboardClient(new FeatureLink(port, new NoDelay())).ApplyMacros([TypeA()]);

        // 424 bytes → 6 × 64 + 40 → 8 pacotes (resto ≠ 0 soma 2), AA 55 no fim do último
        Assert.Equal(1 + 1 + 8 + 1, port.Written.Count);
        Assert.Equal("000419", Convert.ToHexString(port.Written[0], 0, 3));
        Assert.Equal("000415", Convert.ToHexString(port.Written[1], 0, 3));
        Assert.Equal(8, port.Written[1][9]);
        Assert.Equal([0x90, 0x01], port.Written[2][1..3]);
        Assert.Equal([0xAA, 0x55], port.Written[9][63..65]);
        Assert.Equal("000402", Convert.ToHexString(port.Written[10], 0, 3));
    }

    [Fact]
    public void CablePayload_AddsOnePacketWhenEndFallsOnBoundary()
    {
        Assert.Equal(2 * 64, PacketBuilder.FeatureMacroPayload(new byte[64]).Length);
        Assert.Equal(3 * 64, PacketBuilder.FeatureMacroPayload(new byte[65]).Length);
    }

    [Fact]
    public void Dongle_SendsChunksWithHeaderNineAndRemainderAsLastLength()
    {
        var port = new RecordingReportPort();

        new KeyboardClient(new DongleLink(port, new NoDelay())).ApplyMacros([TypeA()]);

        // 424 bytes = 15 × 28 + 4 → 17 pedaços; o último declara 4 e traz o AA 55 no fim
        Assert.Equal(17, port.Written.Count);
        Assert.All(port.Written.Select((r, i) => (r, i)), c =>
        {
            Assert.Equal(0x09, c.r[1]);
            Assert.Equal(c.i == 16 ? 4 : 0x1C, c.r[2]);
            Assert.Equal(c.i, c.r[3]);
        });
        Assert.Equal([0x90, 0x01], port.Written[0][4..6]);
        Assert.Equal([0xAA, 0x55], port.Written[16][30..32]);
    }

    [Fact]
    public void State_ValidatesNameAndConvertsEvents()
    {
        var state = new MacroState("id", " Copiar ", [new MacroEventState("down", 0xE0, 20), new MacroEventState("mouse-up", 1, 10)]);

        var macro = state.ToMacro();

        Assert.Equal("Copiar", macro.Name);
        Assert.Equal(MacroEventKind.KeyDown, macro.Events[0].Kind);
        Assert.Equal(MacroEventKind.MouseUp, macro.Events[1].Kind);
        Assert.Throws<ArgumentException>(() => (state with { Name = "" }).ToMacro());
        Assert.Throws<ArgumentException>(() => MacroState.ToMacros([state, state]));
    }
}
