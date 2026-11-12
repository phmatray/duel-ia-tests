using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>Règles transverses : absence de mémoire, passage par le module.</summary>
public class MachineTests
{
    [Fact]
    public void La_machine_ne_depend_pas_des_appels_precedents_mais_seulement_du_compte_fourni()
    {
        var (m, spy) = Build(N(5, 5, 5));

        var first = m.Execute(Acc(250, 0), W(150));
        Assert.Equal(AtmStatus.Ok, first.Status);

        // On repasse le compte d'origine, et non celui rendu : la machine ne doit pas s'en souvenir.
        var second = m.Execute(Acc(250, 0), W(50));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(1, 0, 0), 0, Acc(200, 50)), second);

        // Et un compte sans rapport ne doit pas hériter du plafond ou du solde du premier.
        var third = m.Execute(Acc(10, 190), W(10));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(0, 0, 1), 0, Acc(0, 200)), third);
    }

    [Fact]
    public void Un_refus_ne_laisse_pas_de_trace_sur_les_appels_suivants()
    {
        var (m, _) = Build(N(5, 5, 5));
        Assert.Equal(AtmStatus.DailyLimitExceeded, m.Execute(Acc(250, 200), W(10)).Status);
        Assert.Equal(AtmStatus.InvalidAmount, m.Execute(Acc(250, 0), D(NoteCounts.None)).Status);

        var r = m.Execute(Acc(250, 0), W(200));
        Assert.Equal(new AtmResult(AtmStatus.Ok, N(4, 0, 0), 5, Acc(45, 200)), r);
    }

    [Fact]
    public void La_machine_lit_le_stock_du_module_a_chaque_appel()
    {
        var (m, spy) = Build(N(1, 0, 0));
        Assert.Equal(AtmStatus.Ok, m.Execute(Acc(250, 0), W(50)).Status);
        Assert.Equal(N(0, 0, 0), spy.Stock);

        // Le module est vide : le retrait suivant doit être impossible.
        Assert.Equal(AtmStatus.CannotDispense, m.Execute(Acc(250, 0), W(50)).Status);

        // Un dépôt remplit le module, et le retrait redevient possible.
        Assert.Equal(AtmStatus.Ok, m.Execute(Acc(0, 0), D(N(1, 0, 0))).Status);
        Assert.Equal(N(1, 0, 0), spy.Stock);
        Assert.Equal(AtmStatus.Ok, m.Execute(Acc(250, 0), W(50)).Status);
    }

    [Fact]
    public void Un_depot_dans_un_module_rempli_par_des_depots_precedents_peut_deborder()
    {
        var (m, spy) = Build(N(0, 3, 0));
        Assert.Equal(AtmStatus.Ok, m.Execute(Acc(0, 0), D(N(0, 1, 0))).Status);
        Assert.Equal(AtmStatus.Ok, m.Execute(Acc(0, 0), D(N(0, 1, 0))).Status);
        Assert.Equal(AtmStatus.CassetteFull, m.Execute(Acc(0, 0), D(N(0, 1, 0))).Status);
        Assert.Equal(N(0, 5, 0), spy.Stock);
    }

    [Fact]
    public void Deux_machines_sur_le_meme_etat_rendent_le_meme_resultat()
    {
        var a = Build(N(3, 2, 4), Denomination.Twenty).Machine.Execute(Acc(200, 40), W(120));
        var b = Build(N(3, 2, 4), Denomination.Twenty).Machine.Execute(Acc(200, 40), W(120));
        Assert.Equal(a, b);
    }
}
