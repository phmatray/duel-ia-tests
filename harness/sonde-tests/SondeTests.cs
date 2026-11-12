using Atm;

namespace Sonde;

// Une petite suite par exemples, comme un bâtisseur pourrait l'écrire : elle sert seulement aux sondes du banc (protocole §6).
public class SondeTests
{
    private static (AtmResult Resultat, NoteCounts Stock) Executer(int solde, int retire, NoteCounts stock, Operation operation, Denomination? bloquee = null)
    {
        var module = new MoneyDispenser(stock, bloquee);
        var resultat = new CashMachine(module).Execute(new AccountState(solde, retire), operation);

        return (resultat, module.Stock);
    }

    [Fact]
    public void Un_retrait_de_60_sort_trois_billets_de_20()
    {
        var (r, stock) = Executer(100, 0, new NoteCounts(1, 3, 0), new Operation.Withdraw(60));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(new NoteCounts(0, 3, 0), r.Notes);
        Assert.Equal(new AccountState(40, 60), r.Account);
        Assert.Equal(new NoteCounts(1, 0, 0), stock);
    }

    [Fact]
    public void Le_plafond_du_jour_refuse_un_retrait_de_trop() =>
        Assert.Equal(AtmStatus.DailyLimitExceeded, Executer(250, 150, new NoteCounts(5, 5, 5), new Operation.Withdraw(60)).Resultat.Status);

    [Fact]
    public void Un_solde_trop_bas_refuse_le_retrait() =>
        Assert.Equal(AtmStatus.InsufficientFunds, Executer(50, 0, new NoteCounts(5, 5, 5), new Operation.Withdraw(60)).Resultat.Status);

    [Fact]
    public void Un_montant_qui_n_est_pas_un_multiple_de_10_est_refuse() =>
        Assert.Equal(AtmStatus.InvalidAmount, Executer(100, 0, new NoteCounts(5, 5, 5), new Operation.Withdraw(5)).Resultat.Status);

    [Fact]
    public void Un_retrait_de_110_coute_5_euros_de_frais()
    {
        var (r, _) = Executer(115, 0, new NoteCounts(5, 5, 5), new Operation.Withdraw(110));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(0, r.Account.Balance);
    }

    [Fact]
    public void Un_depot_credite_le_solde()
    {
        var (r, stock) = Executer(10, 30, new NoteCounts(1, 1, 0), new Operation.Deposit(new NoteCounts(1, 0, 1)));
        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(new AccountState(70, 30), r.Account);
        Assert.Equal(new NoteCounts(2, 1, 1), stock);
    }

    [Fact]
    public void Une_cassette_de_50_bloquee_ne_sert_pas() =>
        Assert.Equal(AtmStatus.CannotDispense, Executer(250, 0, new NoteCounts(1, 0, 0), new Operation.Withdraw(50), Denomination.Fifty).Resultat.Status);
}
