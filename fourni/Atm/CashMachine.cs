namespace Atm;

public sealed class CashMachine(IMoneyDispenser dispenser) : ICashMachine
{
    public AtmResult Execute(AccountState account, Operation operation) =>
        throw new NotImplementedException();
}
