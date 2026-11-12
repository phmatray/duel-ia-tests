namespace Atm;

public enum Denomination { Fifty = 50, Twenty = 20, Ten = 10 }

public enum AtmStatus
{
    Ok,
    InvalidAmount,
    DailyLimitExceeded,
    InsufficientFunds,
    CannotDispense,
    CassetteJammed,
    CassetteFull,
    DispenserFault,
}

public readonly record struct NoteCounts(int Fifties, int Twenties, int Tens)
{
    public static NoteCounts None => default;
    public int Total => Fifties + Twenties + Tens;
    public int Value => Fifties * 50 + Twenties * 20 + Tens * 10;

    public int Of(Denomination denomination) => denomination switch
    {
        Denomination.Fifty => Fifties,
        Denomination.Twenty => Twenties,
        _ => Tens,
    };

    public static NoteCounts operator +(NoteCounts a, NoteCounts b) =>
        new(a.Fifties + b.Fifties, a.Twenties + b.Twenties, a.Tens + b.Tens);

    public static NoteCounts operator -(NoteCounts a, NoteCounts b) =>
        new(a.Fifties - b.Fifties, a.Twenties - b.Twenties, a.Tens - b.Tens);
}

public readonly record struct AccountState(int Balance, int WithdrawnToday);

public abstract record Operation
{
    public sealed record Withdraw(int Amount) : Operation;

    public sealed record Deposit(NoteCounts Notes) : Operation;
}

public readonly record struct AtmResult(AtmStatus Status, NoteCounts Notes, int Fee, AccountState Account);

public interface IMoneyDispenser
{
    NoteCounts Stock { get; }

    Denomination? Jammed { get; }

    bool Dispense(NoteCounts notes);

    bool Load(NoteCounts notes);
}

public interface ICashMachine
{
    AtmResult Execute(AccountState account, Operation operation);
}
