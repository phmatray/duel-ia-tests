using static Atm.Tests.T;

namespace Atm.Tests;

/// <summary>
/// Ordre des contrôles du retrait sur tout le domaine du compte et des montants,
/// pour quelques configurations de module.
/// </summary>
public class WithdrawOrderTests
{
    public static TheoryData<int, int, int, int> Modules => new()
    {
        { 5, 5, 5, 0 },
        { 0, 0, 0, 0 },
        { 1, 3, 0, 0 },
        { 2, 2, 0, 50 },
        { 5, 5, 5, 10 },
        { 0, 5, 5, 20 },
        { 1, 0, 5, 0 },
    };

    [Theory]
    [MemberData(nameof(Modules))]
    public void Le_premier_controle_qui_echoue_donne_le_statut_et_rien_ne_change(int f, int t, int d, int jammed)
    {
        var stock = N(f, t, d);
        var amounts = new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10));
        var failures = new List<string>();

        for (var balance = 0; balance <= 250; balance += 5)
        for (var withdrawn = 0; withdrawn <= 200; withdrawn += 10)
        foreach (var amount in amounts)
        {
            var fee = amount > 100 ? 5 : 0;
            var best = amount > 0 ? Oracle.Best(stock, Jam(jammed), amount) : null;

            AtmStatus expected;
            if (amount <= 0 || amount % 10 != 0) expected = AtmStatus.InvalidAmount;
            else if (withdrawn + amount > 200) expected = AtmStatus.DailyLimitExceeded;
            else if (amount + fee > balance) expected = AtmStatus.InsufficientFunds;
            else if (best is null) expected = AtmStatus.CannotDispense;
            else expected = AtmStatus.Ok;

            var account = Acc(balance, withdrawn);
            var expectedResult = expected == AtmStatus.Ok
                ? new AtmResult(AtmStatus.Ok, best!.Value, fee, Acc(balance - amount - fee, withdrawn + amount))
                : new AtmResult(expected, NoteCounts.None, 0, account);

            var rig = new Rig(stock, Jam(jammed));
            var result = rig.Run(account, Withdraw(amount));

            if (result != expectedResult || rig.Real.Stock != stock - expectedResult.Notes)
            {
                failures.Add($"compte {account}, retrait {amount}: attendu {expectedResult}, obtenu {result}, stock après {rig.Real.Stock}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} cas faux, dont :\n" + string.Join("\n", failures.Take(20)));
    }
}
