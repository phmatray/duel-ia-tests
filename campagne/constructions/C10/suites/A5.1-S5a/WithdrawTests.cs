using static Atm.Tests.T;

namespace Atm.Tests;

public class WithdrawTests
{
    private static readonly NoteCounts Full = N(5, 5, 5);

    private static (AtmResult Result, SpyDispenser Dispenser) Run(AccountState account, Operation op, NoteCounts? stock = null, Denomination? jammed = null)
    {
        var dispenser = new SpyDispenser(stock ?? Full, jammed);
        var result = new CashMachine(dispenser).Execute(account, op);
        return (result, dispenser);
    }

    private static void AssertRefused(AtmStatus expected, AccountState account, AtmResult r, SpyDispenser d, NoteCounts stockBefore)
    {
        Assert.Equal(expected, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(account, r.Account);
        Assert.Equal(stockBefore, d.Stock);
        Assert.DoesNotContain(d.Dispenses, x => x.Result);
        Assert.DoesNotContain(d.Loads, x => x.Result);
    }

    // ---- Contrôle 1 : montant invalide ----

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_negatif_nul_ou_non_multiple_de_10_est_invalide(int amount)
    {
        var account = Acc(250, 0);
        var (r, d) = Run(account, W(amount));

        AssertRefused(AtmStatus.InvalidAmount, account, r, d, Full);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(5)]
    public void Montant_invalide_passe_avant_tous_les_autres_controles(int amount)
    {
        // Plafond atteint, solde nul, stock vide, cassette bloquée : seul le montant doit compter.
        var account = Acc(0, 200);
        var (r, d) = Run(account, W(amount), N(0, 0, 0), Denomination.Ten);

        AssertRefused(AtmStatus.InvalidAmount, account, r, d, N(0, 0, 0));
    }

    // ---- Contrôle 2 : plafond du jour ----

    [Theory]
    [InlineData(0, 200)]
    [InlineData(100, 100)]
    [InlineData(190, 10)]
    public void Atteindre_exactement_le_plafond_est_permis(int withdrawnToday, int amount)
    {
        var (r, _) = Run(Acc(250, withdrawnToday), W(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(withdrawnToday + amount, r.Account.WithdrawnToday);
    }

    [Theory]
    [InlineData(0, 210)]
    [InlineData(0, 220)]
    [InlineData(100, 110)]
    [InlineData(190, 20)]
    [InlineData(200, 10)]
    public void Depasser_le_plafond_est_refuse(int withdrawnToday, int amount)
    {
        var account = Acc(250, withdrawnToday);
        var (r, d) = Run(account, W(amount));

        AssertRefused(AtmStatus.DailyLimitExceeded, account, r, d, Full);
    }

    [Fact]
    public void Les_frais_ne_comptent_pas_dans_le_plafond()
    {
        // 90 + 110 = 200 : au plafond ; avec les frais on serait à 205.
        var (r, _) = Run(Acc(250, 90), W(110));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(5, r.Fee);
        Assert.Equal(Acc(135, 200), r.Account);
    }

    [Fact]
    public void Plafond_passe_avant_le_solde_et_la_distribution()
    {
        var account = Acc(0, 200);
        var (r, d) = Run(account, W(10), N(0, 0, 0));

        AssertRefused(AtmStatus.DailyLimitExceeded, account, r, d, N(0, 0, 0));
    }

    // ---- Contrôle 3 : solde et frais ----

    [Theory]
    [InlineData(10, 0)]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(110, 5)]
    [InlineData(150, 5)]
    [InlineData(200, 5)]
    public void Frais_de_5_euros_strictement_au_dessus_de_100(int amount, int expectedFee)
    {
        var (r, _) = Run(Acc(250, 0), W(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expectedFee, r.Fee);
        Assert.Equal(250 - amount - expectedFee, r.Account.Balance);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(115, 110)]
    [InlineData(50, 50)]
    [InlineData(205, 200)]
    public void Solde_egal_au_montant_plus_frais_suffit(int balance, int amount)
    {
        var (r, _) = Run(Acc(balance, 0), W(amount));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(0, r.Account.Balance);
    }

    [Theory]
    [InlineData(95, 100)]
    [InlineData(110, 110)]
    [InlineData(105, 110)]
    [InlineData(200, 200)]
    [InlineData(0, 10)]
    public void Solde_inferieur_au_montant_plus_frais_est_refuse(int balance, int amount)
    {
        var account = Acc(balance, 0);
        var (r, d) = Run(account, W(amount));

        AssertRefused(AtmStatus.InsufficientFunds, account, r, d, Full);
    }

    [Fact]
    public void Solde_passe_avant_la_distribution()
    {
        var account = Acc(5, 0);
        var (r, d) = Run(account, W(10), N(0, 0, 0));

        AssertRefused(AtmStatus.InsufficientFunds, account, r, d, N(0, 0, 0));
    }

    // ---- Contrôle 4 : combinaison de billets ----

    [Theory]
    [InlineData(0, 0, 0, 0, 10)]
    [InlineData(5, 5, 0, 0, 30)]    // 30 sans billet de 10 : impossible
    [InlineData(0, 2, 1, 0, 60)]    // stock total 50 < 60
    [InlineData(5, 0, 0, 50, 50)]   // seuls des billets de 50, cassette bloquée
    [InlineData(5, 5, 0, 20, 40)]   // 40 = 20×2 mais cassette 20 bloquée, pas de 10
    [InlineData(0, 0, 5, 10, 10)]   // seuls des 10, bloqués
    public void Aucune_combinaison_exacte_donne_CannotDispense(int f, int t, int x, int jam, int amount)
    {
        var stock = N(f, t, x);
        var account = Acc(250, 0);
        var (r, d) = Run(account, W(amount), stock, Jam(jam));

        AssertRefused(AtmStatus.CannotDispense, account, r, d, stock);
    }

    [Theory]
    [InlineData(1, 3, 1, 0, 60, 1, 0, 1)]   // 50+10 plutôt que 20×3
    [InlineData(5, 5, 5, 0, 90, 1, 2, 0)]   // 50+20+20
    [InlineData(5, 5, 5, 0, 200, 4, 0, 0)]
    [InlineData(5, 5, 5, 0, 30, 0, 1, 1)]
    [InlineData(5, 5, 5, 0, 160, 3, 0, 1)]
    [InlineData(5, 5, 5, 50, 100, 0, 5, 0)] // 50 bloquée
    [InlineData(5, 5, 5, 20, 80, 1, 0, 3)]  // 20 bloquée
    [InlineData(5, 5, 5, 10, 60, 0, 3, 0)]  // 10 bloquée : 50+10 impossible
    [InlineData(1, 5, 5, 0, 150, 1, 5, 0)]  // un seul 50
    [InlineData(0, 2, 5, 0, 70, 0, 2, 3)]
    public void La_combinaison_compte_le_moins_de_billets(int f, int t, int x, int jam, int amount, int ef, int et, int ex)
    {
        var (r, d) = Run(Acc(250, 0), W(amount), N(f, t, x), Jam(jam));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(N(ef, et, ex), r.Notes);
        Assert.Equal(N(f, t, x) - N(ef, et, ex), d.Stock);
    }

    [Fact]
    public void La_cassette_bloquee_n_est_jamais_sollicitee()
    {
        var (r, d) = Run(Acc(250, 0), W(100), N(5, 5, 5), Denomination.Fifty);

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.All(d.Dispenses, p => Assert.Equal(0, p.Notes.Fifties));
        Assert.Equal(5, d.Stock.Fifties);
    }

    // ---- Sortie en passages de 4 billets au plus ----

    [Theory]
    [InlineData(0, 3, 5, 100, 0, 3, 4)]   // 7 billets
    [InlineData(0, 2, 5, 70, 0, 2, 3)]    // 5 billets
    [InlineData(1, 3, 5, 120, 1, 3, 1)]   // 5 billets
    [InlineData(0, 5, 5, 150, 0, 5, 5)]   // 10 billets
    [InlineData(3, 5, 0, 200, 2, 5, 0)]   // 7 billets
    [InlineData(0, 0, 5, 50, 0, 0, 5)]    // 5 billets de 10
    [InlineData(0, 5, 0, 100, 0, 5, 0)]   // 5 billets de 20
    [InlineData(1, 1, 3, 100, 1, 1, 3)]   // 5 billets
    public void Plus_de_4_billets_sortent_en_plusieurs_passages(int f, int t, int x, int amount, int ef, int et, int ex)
    {
        var expected = N(ef, et, ex);
        var (r, d) = Run(Acc(250, 0), W(amount), N(f, t, x));

        Assert.Equal(AtmStatus.Ok, r.Status);
        Assert.Equal(expected, r.Notes);
        Assert.All(d.Dispenses, p =>
        {
            Assert.True(p.Result, $"passage refusé par le module : {p.Notes}");
            Assert.InRange(p.Notes.Total, 1, 4);
        });
        Assert.Equal(expected, d.DispensedTotal);
        Assert.True(d.Dispenses.Count >= (expected.Total + 3) / 4);
        Assert.Equal(N(f, t, x) - expected, d.Stock);
    }

    // ---- Succès ----

    [Fact]
    public void Succes_met_a_jour_compte_et_stock_sans_charger()
    {
        var (r, d) = Run(Acc(200, 40), W(130), N(5, 5, 5));

        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 1, 1), 5, Acc(65, 170)), r);
        Assert.Equal(N(3, 4, 4), d.Stock);
        Assert.Empty(d.Loads);
        Assert.Equal(r.Notes, d.DispensedTotal);
    }
}
