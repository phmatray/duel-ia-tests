namespace Atm.Tests;

/// <summary>La machine ne dépend que du compte, de l'opération et du module, et passe toujours par le module.</summary>
public class StatelessnessTests
{
    [Fact]
    public void Same_machine_gives_independent_results_for_successive_calls()
    {
        var dispenser = new SpyDispenser(Notes(5, 5, 5));
        var machine = new CashMachine(dispenser);

        var first = machine.Execute(Account(250, 0), Withdraw(100));
        var second = machine.Execute(Account(250, 0), Withdraw(100));

        // Le compte n'est pas mémorisé : le second appel repart du compte qu'on lui donne.
        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(2, 0, 0), 0, Account(150, 100)), first);
        Assert.Equal(first, second);
        Assert.Equal(Notes(1, 5, 5), dispenser.Stock);
    }

    [Fact]
    public void Machine_reads_the_current_stock_of_the_module_on_each_call()
    {
        var dispenser = new SpyDispenser(Notes(1, 0, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.Ok, machine.Execute(Account(250, 0), Withdraw(50)).Status);
        // La cassette de 50 est maintenant vide.
        Assert.Equal(AtmStatus.CannotDispense, machine.Execute(Account(250, 0), Withdraw(50)).Status);

        Assert.Equal(AtmStatus.Ok, machine.Execute(Account(0, 0), Deposit(1, 0, 0)).Status);
        // Le billet déposé est de nouveau disponible.
        Assert.Equal(AtmStatus.Ok, machine.Execute(Account(250, 0), Withdraw(50)).Status);
        Assert.Equal(Notes(0, 0, 0), dispenser.Stock);
    }

    [Fact]
    public void Deposit_fills_cassette_until_full_on_the_same_module()
    {
        var dispenser = new SpyDispenser(Notes(0, 4, 0));
        var machine = new CashMachine(dispenser);

        Assert.Equal(AtmStatus.Ok, machine.Execute(Account(0, 0), Deposit(0, 1, 0)).Status);
        Assert.Equal(AtmStatus.CassetteFull, machine.Execute(Account(0, 0), Deposit(0, 1, 0)).Status);
        Assert.Equal(Notes(0, 5, 0), dispenser.Stock);
    }

    [Fact]
    public void A_fault_does_not_affect_the_next_call()
    {
        var faulty = new SpyDispenser(Notes(5, 5, 5)) { FailOnDispenseCall = 1 };
        var machine = new CashMachine(faulty);

        Assert.Equal(AtmStatus.DispenserFault, machine.Execute(Account(250, 0), Withdraw(50)).Status);
        Assert.Equal(new AtmResult(AtmStatus.Ok, Notes(1, 0, 0), 0, Account(200, 50)),
            machine.Execute(Account(250, 0), Withdraw(50)));
    }
}
