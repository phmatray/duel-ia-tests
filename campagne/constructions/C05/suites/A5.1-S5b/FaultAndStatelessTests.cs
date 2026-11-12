using static Atm.Tests.S;

namespace Atm.Tests;

public class DispenserFaultTests
{
    [Fact]
    public void Panne_au_premier_passage_du_retrait()
    {
        var dispenser = new FaultyDispenser(N(5, 5, 5), null, failFromCall: 1);
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), W(150));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Acc(250, 0)), r);
        Assert.Single(dispenser.DispenseCalls);  // on s'arrête aussitôt
        Assert.Empty(dispenser.LoadCalls);
    }

    [Theory]
    [InlineData(1, 5, 0, 150, 2)]  // 6 billets : au moins 2 passages
    [InlineData(0, 5, 5, 140, 2)]  // 9 billets : au moins 3 passages
    [InlineData(0, 5, 5, 140, 3)]
    public void Panne_en_cours_de_retrait_rend_les_billets_deja_sortis(int f, int t, int d10, int amount, int failFromCall)
    {
        var dispenser = new FaultyDispenser(N(f, t, d10), null, failFromCall);
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), W(amount));

        var already = dispenser.Succeeded.Aggregate(NoteCounts.None, (a, b) => a + b);
        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(failFromCall - 1, dispenser.Succeeded.Count);
        Assert.Equal(already, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(250, 0), r.Account);
        Assert.Equal(failFromCall, dispenser.DispenseCalls.Count);  // aucun passage après la panne
        Assert.Empty(dispenser.LoadCalls);
    }

    [Fact]
    public void Panne_au_second_passage_d_un_retrait_de_5_billets()
    {
        var dispenser = new FaultyDispenser(N(5, 5, 5), null, failFromCall: 2);
        var r = new CashMachine(dispenser).Execute(Acc(250, 0), W(190));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(2, dispenser.DispenseCalls.Count);
        Assert.Equal(dispenser.Succeeded.Single(), r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Acc(250, 0), r.Account);
        Assert.Equal(N(5, 5, 5) - r.Notes, dispenser.Stock);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(1, 1, 1)]
    public void Panne_lors_d_un_depot(int f, int t, int d10)
    {
        var dispenser = new FaultyDispenser(N(0, 0, 0), null, failFromCall: 1);
        var r = new CashMachine(dispenser).Execute(Acc(40, 30), D(N(f, t, d10)));

        Assert.Equal(new AtmResult(AtmStatus.DispenserFault, NoteCounts.None, 0, Acc(40, 30)), r);
        Assert.Single(dispenser.LoadCalls);
        Assert.Empty(dispenser.DispenseCalls);
    }

    public static TheoryData<AccountState, Operation, NoteCounts, int?, AtmStatus> Refusals => new()
    {
        { Acc(250, 0), W(5), N(5, 5, 5), null, AtmStatus.InvalidAmount },
        { Acc(250, 200), W(10), N(5, 5, 5), null, AtmStatus.DailyLimitExceeded },
        { Acc(0, 0), W(10), N(5, 5, 5), null, AtmStatus.InsufficientFunds },
        { Acc(250, 0), W(30), N(5, 5, 0), null, AtmStatus.CannotDispense },
        { Acc(0, 0), D(NoteCounts.None), N(0, 0, 0), null, AtmStatus.InvalidAmount },
        { Acc(0, 0), D(N(1, 0, 0)), N(0, 0, 0), 50, AtmStatus.CassetteJammed },
        { Acc(0, 0), D(N(1, 0, 0)), N(5, 0, 0), null, AtmStatus.CassetteFull },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Un_refus_est_decide_avant_le_module_et_ne_devient_pas_une_panne(
        AccountState account, Operation op, NoteCounts stock, int? jammed, AtmStatus expected)
    {
        // Le module tomberait en panne au moindre appel : un refus ne doit pas l'atteindre.
        var dispenser = new FaultyDispenser(stock, Jam(jammed), failFromCall: 1);
        var r = new CashMachine(dispenser).Execute(account, op);
        Assert.Equal(new AtmResult(expected, NoteCounts.None, 0, account), r);
        Assert.Equal(stock, dispenser.Stock);
    }
}

public class StatelessTests
{
    [Fact]
    public void La_machine_ne_retient_pas_le_compte_entre_deux_appels()
    {
        var (m, _) = Real(N(5, 5, 5));
        var first = m.Execute(Acc(200, 0), W(100));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, Acc(100, 100)), first);

        // Même compte de départ : seul le compte passé compte, pas le précédent résultat.
        var second = m.Execute(Acc(200, 0), W(100));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(2, 0, 0), 0, Acc(100, 100)), second);
    }

    [Fact]
    public void La_machine_lit_le_stock_du_module_a_chaque_appel()
    {
        var (m, d) = Real(N(1, 0, 0));
        Assert.Equal(AtmStatus.Ok, m.Execute(Acc(100, 0), W(50)).Status);
        Assert.Equal(AtmStatus.CannotDispense, m.Execute(Acc(100, 0), W(50)).Status);

        Assert.True(d.Load(N(1, 0, 0)));  // le module change hors de la machine
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 0, 0), 0, Acc(50, 50)), m.Execute(Acc(100, 0), W(50)));
    }

    [Fact]
    public void Un_depot_puis_un_retrait_utilisent_le_meme_module()
    {
        var (m, d) = Real(N(0, 0, 0));
        Assert.Equal(AtmStatus.CannotDispense, m.Execute(Acc(100, 0), W(70)).Status);
        Assert.Equal(AtmStatus.Ok, m.Execute(Acc(0, 0), D(N(1, 1, 0))).Status);
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 1, 0), 0, Acc(30, 70)), m.Execute(Acc(100, 0), W(70)));
        Assert.Equal(N(0, 0, 0), d.Stock);
    }
}
