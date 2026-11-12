namespace Atm;

/// <summary>Le vrai module de billets, déterministe.</summary>
public sealed class MoneyDispenser(NoteCounts stock, Denomination? jammed = null) : IMoneyDispenser
{
    public const int MaxNotesPerPass = 4;
    public const int CassetteCapacity = 5;

    public NoteCounts Stock { get; private set; } = stock;

    public Denomination? Jammed { get; } = jammed;

    public bool Dispense(NoteCounts notes)
    {
        var enough = notes.Fifties <= Stock.Fifties && notes.Twenties <= Stock.Twenties && notes.Tens <= Stock.Tens;
        if (!IsValid(notes) || notes.Total > MaxNotesPerPass || !enough)
        {
            return false;
        }

        Stock -= notes;

        return true;
    }

    public bool Load(NoteCounts notes)
    {
        var after = Stock + notes;
        var fits = after.Fifties <= CassetteCapacity && after.Twenties <= CassetteCapacity && after.Tens <= CassetteCapacity;
        if (!IsValid(notes) || !fits)
        {
            return false;
        }

        Stock = after;

        return true;
    }

    private bool IsValid(NoteCounts notes) =>
        notes is { Fifties: >= 0, Twenties: >= 0, Tens: >= 0, Total: > 0 }
        && (Jammed is not { } cassette || notes.Of(cassette) == 0);
}
