using System.Globalization;

namespace AkiraCore.Core.Automation;

/// <summary>O que as regras pedem para a luz agora.</summary>
public enum LightOverride
{
    None, // segue a configuração normal
    Dim,  // mesma iluminação com brilho mínimo
    Off,  // apagada
}

/// <summary>
/// Regras de luz do lado do PC (o firmware só tem a suspensão): apagar com o Windows bloqueado ou
/// com a tela desligada, e um horário noturno que apaga ou deixa a luz no mínimo.
/// Horários em "HH:mm"; NightAction é "off" ou "dim".
/// </summary>
public sealed record LightRulesState(
    bool OffWhenLocked,
    bool OffWhenDisplayOff,
    bool Night,
    string NightStart,
    string NightEnd,
    string NightAction)
{
    public static LightRulesState Default { get; } = new(false, false, false, "23:00", "07:00", "off");

    /// <exception cref="ArgumentException">Horário que não é HH:mm ou ação desconhecida.</exception>
    public void Validate()
    {
        LightRules.ParseTime(NightStart);
        LightRules.ParseTime(NightEnd);
        if (NightAction is not ("off" or "dim"))
        {
            throw new ArgumentException($"Ação do horário noturno desconhecida: {NightAction}.");
        }
    }
}

public static class LightRules
{
    /// <summary>Brilho do "dim" (0 a 5): o mínimo que ainda acende.</summary>
    public const int DimBrightness = 1;

    public static LightOverride Evaluate(LightRulesState rules, bool locked, bool displayOff, TimeOnly now)
    {
        if ((rules.OffWhenLocked && locked) || (rules.OffWhenDisplayOff && displayOff))
        {
            return LightOverride.Off;
        }
        if (rules.Night && InWindow(now, ParseTime(rules.NightStart), ParseTime(rules.NightEnd)))
        {
            return rules.NightAction == "dim" ? LightOverride.Dim : LightOverride.Off;
        }
        return LightOverride.None;
    }

    /// <summary>Início incluído, fim excluído; janela que passa da meia-noite (23:00 a 07:00) também vale.</summary>
    public static bool InWindow(TimeOnly now, TimeOnly start, TimeOnly end) =>
        start == end ? false
        : start < end ? now >= start && now < end
        : now >= start || now < end;

    /// <exception cref="ArgumentException">Não é um horário HH:mm.</exception>
    public static TimeOnly ParseTime(string? text) =>
        TimeOnly.TryParseExact(text ?? "", "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : throw new ArgumentException($"Horário inválido: {text}. Use HH:mm, ex.: 23:00.");
}
