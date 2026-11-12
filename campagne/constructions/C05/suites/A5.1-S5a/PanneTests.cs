using static Atm.Tests.Outils;

namespace Atm.Tests;

/// <summary>Le module rend false malgré les contrôles : DispenserFault.</summary>
public class PanneTests
{
    [Fact]
    public void Panne_au_premier_passage_aucun_billet_sorti()
    {
        var espion = new ModuleEspion(Plein) { PanneAuPassage = 1 };
        var r = new CashMachine(espion).Execute(Compte(250, 0), Retrait(80));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Compte(250, 0), r.Account);
        Assert.Single(espion.Passages);
    }

    [Fact]
    public void Panne_au_second_passage_rend_les_billets_du_premier()
    {
        // 50×1 20×5 = 6 billets : deux passages au moins.
        var espion = new ModuleEspion(N(1, 5, 0)) { PanneAuPassage = 2 };
        var r = new CashMachine(espion).Execute(Compte(250, 0), Retrait(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(espion.Passages[0], r.Notes);
        Assert.Equal(N(1, 5, 0) - espion.Passages[0], espion.Stock);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Compte(250, 0), r.Account);
    }

    [Fact]
    public void Panne_on_s_arrete_aussitot()
    {
        // 50×1 20×5 10×5 = 11 billets : trois passages au moins.
        var espion = new ModuleEspion(N(1, 5, 5)) { PanneAuPassage = 2 };
        var r = new CashMachine(espion).Execute(Compte(250, 0), Retrait(200));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(2, espion.Passages.Count);
        Assert.Equal(espion.Passages[0], r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Compte(250, 0), r.Account);
    }

    [Fact]
    public void Panne_au_dernier_passage_rend_tous_les_billets_precedents()
    {
        var espion = new ModuleEspion(N(1, 5, 5)) { PanneAuPassage = 3 };
        var r = new CashMachine(espion).Execute(Compte(250, 0), Retrait(200));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(3, espion.Passages.Count);
        Assert.Equal(espion.Passages[0] + espion.Passages[1], r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Compte(250, 0), r.Account);
    }

    [Fact]
    public void Panne_au_retrait_avec_frais_ne_preleve_pas_de_frais()
    {
        var espion = new ModuleEspion(Plein) { PanneAuPassage = 1 };
        var r = new CashMachine(espion).Execute(Compte(250, 0), Retrait(150));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Compte(250, 0), r.Account);
    }

    [Fact]
    public void Panne_au_depot()
    {
        var espion = new ModuleEspion(N(1, 1, 1)) { PanneAuChargement = true };
        var r = new CashMachine(espion).Execute(Compte(10, 30), Depot(N(1, 1, 1)));

        Assert.Equal(AtmStatus.DispenserFault, r.Status);
        Assert.Equal(NoteCounts.None, r.Notes);
        Assert.Equal(0, r.Fee);
        Assert.Equal(Compte(10, 30), r.Account);
        Assert.Equal(N(1, 1, 1), espion.Stock);
        Assert.Empty(espion.Passages);
    }
}
