// L'arbitre : compare deux implémentations sur tout le domaine de spec.md.
//   compare <reference.dll> <candidat.dll>   rapporte chaque écart, sa surface et l'empreinte de l'ensemble des paires divergentes
//   check <reference.dll>                    vérifie la référence contre des invariants, sans passer par son algorithme
//   examples <reference.dll>                 vérifie les exemples de spec.md
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Atm;

Denomination?[] jams = [null, Denomination.Fifty, Denomination.Twenty, Denomination.Ten];
int[] balances = [.. Enumerable.Range(0, 51).Select(i => i * 5)];
int[] withdrawn = [.. Enumerable.Range(0, 21).Select(i => i * 10)];
Operation[] operations =
[
    .. new[] { -10, 0, 5 }.Concat(Enumerable.Range(1, 22).Select(i => i * 10)).Select(amount => new Operation.Withdraw(amount)),
    .. (from a in new[] { 0, 1 } from b in new[] { 0, 1 } from c in new[] { 0, 1 } select new Operation.Deposit(new NoteCounts(a, b, c))),
];
NoteCounts[] stocks =
[
    .. from a in Enumerable.Range(0, 6) from b in Enumerable.Range(0, 6) from c in Enumerable.Range(0, 6) select new NoteCounts(a, b, c),
];
long PerStock = jams.Length * balances.Length * withdrawn.Length * operations.Length;
long TotalPairs = stocks.Length * PerStock;

Func<IMoneyDispenser, ICashMachine> Load(string path)
{
    // Chaque candidat a son contexte ; le contrat reste celui de l'arbitre, pour que les types soient les mêmes.
    var context = new AssemblyLoadContext(path);
    context.Resolving += (_, name) => name.Name == typeof(ICashMachine).Assembly.GetName().Name ? typeof(ICashMachine).Assembly : null;
    var type = context.LoadFromAssemblyPath(Path.GetFullPath(path)).GetType("Atm.CashMachine")
        ?? throw new InvalidOperationException($"Atm.CashMachine introuvable dans {path}");
    var constructor = type.GetConstructor([typeof(IMoneyDispenser)])
        ?? throw new InvalidOperationException("Constructeur CashMachine(IMoneyDispenser) introuvable");

    return dispenser => (ICashMachine)constructor.Invoke([dispenser]);
}

(string Outcome, AtmResult? Result, NoteCounts Stock) Run(
    Func<IMoneyDispenser, ICashMachine> make, NoteCounts stock, Denomination? jam, AccountState account, Operation operation)
{
    var dispenser = new MoneyDispenser(stock, jam);
    try
    {
        return ("ok", make(dispenser).Execute(account, operation), dispenser.Stock);
    }
    catch (Exception error) when (error is not OutOfMemoryException)
    {
        return (error.InnerException?.GetType().Name ?? error.GetType().Name, null, dispenser.Stock);
    }
}

string Show(NoteCounts stock, Denomination? jam, AccountState account, Operation operation) =>
    $"solde={account.Balance} retiré={account.WithdrawnToday} stock={stock.Fifties}/{stock.Twenties}/{stock.Tens} bloquée={jam?.ToString() ?? "-"} {operation}";

void Visit(int stockIndex, Action<long, NoteCounts, Denomination?, AccountState, Operation> visit)
{
    var index = stockIndex * PerStock;
    foreach (var jam in jams)
    {
        foreach (var balance in balances)
        {
            foreach (var today in withdrawn)
            {
                foreach (var operation in operations)
                {
                    visit(index++, stocks[stockIndex], jam, new AccountState(balance, today), operation);
                }
            }
        }
    }
}

switch (args)
{
    case ["compare", var referencePath, var candidatePath]:
    {
        var reference = Load(referencePath);
        var candidate = Load(candidatePath);
        var divergent = new byte[(TotalPairs + 7) / 8];
        var samples = new List<string>();
        long count = 0;
        Parallel.For(0, stocks.Length, stockIndex => Visit(stockIndex, (index, stock, jam, account, operation) =>
        {
            var expected = Run(reference, stock, jam, account, operation);
            var actual = Run(candidate, stock, jam, account, operation);
            if (expected == actual)
            {
                return;
            }

            Interlocked.Increment(ref count);
            lock (divergent)
            {
                divergent[index / 8] |= (byte)(1 << (int)(index % 8));
                if (samples.Count < 5)
                {
                    samples.Add($"{Show(stock, jam, account, operation)}\n    attendu {expected}\n    obtenu  {actual}");
                }
            }
        }));
        Console.WriteLine($"paires={TotalPairs} écarts={count} surface={100.0 * count / TotalPairs:F3}% empreinte={Convert.ToHexString(SHA256.HashData(divergent))[..16]}");
        samples.ForEach(Console.WriteLine);
        return count == 0 ? 0 : 1;
    }

    case ["nouveau", var referencePath, var originalPath, var patchedPath]:
    {
        // Un bug nouveau seulement (protocole §3) : une paire où l'original suivait la référence et où le patch s'en écarte.
        var reference = Load(referencePath);
        var original = Load(originalPath);
        var patched = Load(patchedPath);
        var nouveaux = new byte[(TotalPairs + 7) / 8];
        long count = 0, deja = 0;
        string? exemple = null;
        Parallel.For(0, stocks.Length, stockIndex => Visit(stockIndex, (index, stock, jam, account, operation) =>
        {
            var attendu = Run(reference, stock, jam, account, operation);
            if (Run(original, stock, jam, account, operation) != attendu)
            {
                Interlocked.Increment(ref deja);
                return;
            }

            var obtenu = Run(patched, stock, jam, account, operation);
            if (obtenu == attendu)
            {
                return;
            }

            Interlocked.Increment(ref count);
            lock (nouveaux)
            {
                nouveaux[index / 8] |= (byte)(1 << (int)(index % 8));
                exemple ??= $"{Show(stock, jam, account, operation)} : attendu {attendu.Result?.Status} {attendu.Result?.Notes.Value} €, obtenu {(obtenu.Result is { } r ? $"{r.Status} {r.Notes.Value} €" : obtenu.Outcome)}";
            }
        }));
        Console.WriteLine($"paires={TotalPairs} nouveaux={count} surface={100.0 * count / TotalPairs:F4}% ecarts_origine={deja} empreinte={Convert.ToHexString(SHA256.HashData(nouveaux))[..16]}");
        if (exemple is not null)
        {
            Console.WriteLine($"exemple={exemple}");
        }

        return 0;
    }

    case ["check", var referencePath]:
    {
        var reference = Load(referencePath);
        long failures = 0;
        var statuses = new long[Enum.GetValues<AtmStatus>().Length];
        Parallel.For(0, stocks.Length, stockIndex => Visit(stockIndex, (_, stock, jam, account, operation) =>
        {
            var run = Run(reference, stock, jam, account, operation);
            var result = run.Result!.Value;
            Interlocked.Increment(ref statuses[(int)result.Status]);
            var isWithdrawal = operation is Operation.Withdraw;
            var amount = operation is Operation.Withdraw withdraw ? withdraw.Amount : 0;
            var deposited = operation is Operation.Deposit deposit ? deposit.Notes : NoteCounts.None;
            var usable = jam switch
            {
                Denomination.Fifty => stock with { Fifties = 0 },
                Denomination.Twenty => stock with { Twenties = 0 },
                Denomination.Ten => stock with { Tens = 0 },
                _ => stock,
            };

            // Unicité et minimalité par force brute, sans passer par la référence.
            var combos = (from a in Enumerable.Range(0, usable.Fifties + 1)
                          from b in Enumerable.Range(0, usable.Twenties + 1)
                          from c in Enumerable.Range(0, usable.Tens + 1)
                          where a * 50 + b * 20 + c * 10 == amount
                          select new NoteCounts(a, b, c)).ToList();
            var fewest = combos.Count == 0 ? int.MaxValue : combos.Min(combo => combo.Total);
            var isOk = result.Status == AtmStatus.Ok;
            var holds = result.Status switch
            {
                AtmStatus.Ok when isWithdrawal =>
                    result.Notes.Value == amount
                    && result.Notes.Total == fewest
                    && combos.Count(combo => combo.Total == fewest) == 1
                    && account.Balance - result.Account.Balance == amount + result.Fee
                    && result.Account.Balance >= 0
                    && result.Account.WithdrawnToday == account.WithdrawnToday + amount
                    && result.Account.WithdrawnToday <= 200
                    && result.Fee == (amount > 100 ? 5 : 0)
                    && run.Stock == stock - result.Notes,
                AtmStatus.Ok =>
                    run.Stock == stock + deposited
                    && result.Notes == NoteCounts.None
                    && result.Account == account with { Balance = account.Balance + deposited.Value },
                AtmStatus.DispenserFault => false, // jamais atteinte avec le vrai module
                _ => result.Notes == NoteCounts.None && result.Fee == 0 && result.Account == account && run.Stock == stock,
            };
            var mustBeOk = isWithdrawal
                && amount > 0 && amount % 10 == 0
                && account.WithdrawnToday + amount <= 200
                && amount + (amount > 100 ? 5 : 0) <= account.Balance
                && combos.Count > 0;
            if (!holds || (isWithdrawal && mustBeOk != isOk))
            {
                if (Interlocked.Increment(ref failures) <= 5)
                {
                    Console.WriteLine($"ÉCHEC {Show(stock, jam, account, operation)} -> {result}");
                }
            }
        }));
        Console.WriteLine($"paires={TotalPairs} violations={failures}");
        foreach (var status in Enum.GetValues<AtmStatus>())
        {
            Console.WriteLine($"  {status,-20} {statuses[(int)status],12} {100.0 * statuses[(int)status] / TotalPairs,6:F2}%");
        }

        return failures == 0 ? 0 : 1;
    }

    case ["examples", var referencePath]:
    {
        var reference = Load(referencePath);
        NoteCounts Full = new(5, 5, 5);
        (int Balance, int Today, NoteCounts Stock, Denomination? Jam, Operation Op, AtmStatus Status, NoteCounts Notes, int Fee, int NewBalance, int NewToday, NoteCounts NewStock)[] rows =
        [
            (100, 0, new(1, 3, 0), null, new Operation.Withdraw(60), AtmStatus.Ok, new(0, 3, 0), 0, 40, 60, new(1, 0, 0)),
            (100, 0, new(1, 1, 1), null, new Operation.Withdraw(80), AtmStatus.Ok, new(1, 1, 1), 0, 20, 80, default),
            (105, 0, Full, null, new Operation.Withdraw(100), AtmStatus.Ok, new(2, 0, 0), 0, 5, 100, new(3, 5, 5)),
            (115, 0, Full, null, new Operation.Withdraw(110), AtmStatus.Ok, new(2, 0, 1), 5, 0, 110, new(3, 5, 4)),
            (110, 0, Full, null, new Operation.Withdraw(110), AtmStatus.InsufficientFunds, default, 0, 110, 0, Full),
            (250, 150, Full, null, new Operation.Withdraw(60), AtmStatus.DailyLimitExceeded, default, 0, 250, 150, Full),
            (250, 0, new(1, 0, 0), Denomination.Fifty, new Operation.Withdraw(50), AtmStatus.CannotDispense, default, 0, 250, 0, new(1, 0, 0)),
            (250, 0, new(2, 2, 0), Denomination.Fifty, new Operation.Withdraw(40), AtmStatus.Ok, new(0, 2, 0), 0, 210, 40, new(2, 0, 0)),
            (0, 200, default, null, new Operation.Withdraw(5), AtmStatus.InvalidAmount, default, 0, 0, 200, default),
            (10, 0, new(0, 5, 0), null, new Operation.Deposit(new(0, 1, 0)), AtmStatus.CassetteFull, default, 0, 10, 0, new(0, 5, 0)),
            (10, 30, new(0, 1, 0), Denomination.Twenty, new Operation.Deposit(new(0, 1, 0)), AtmStatus.CassetteJammed, default, 0, 10, 30, new(0, 1, 0)),
            (10, 30, new(1, 1, 0), null, new Operation.Deposit(new(1, 0, 1)), AtmStatus.Ok, default, 0, 70, 30, new(2, 1, 1)),
        ];
        var wrong = 0;
        foreach (var row in rows)
        {
            var run = Run(reference, row.Stock, row.Jam, new AccountState(row.Balance, row.Today), row.Op);
            var expected = new AtmResult(row.Status, row.Notes, row.Fee, new AccountState(row.NewBalance, row.NewToday));
            if (run.Result != expected || run.Stock != row.NewStock)
            {
                wrong++;
                Console.WriteLine($"ÉCHEC {row.Op}: attendu {expected} stock {row.NewStock}, obtenu {run.Result} stock {run.Stock}");
            }
        }

        Console.WriteLine($"exemples={rows.Length} faux={wrong}");
        return wrong == 0 ? 0 : 1;
    }

    default:
        Console.Error.WriteLine("usage : arbiter compare <reference.dll> <candidat.dll> | nouveau <reference.dll> <original.dll> <patché.dll> | check <reference.dll> | examples <reference.dll>");
        return 2;
}
