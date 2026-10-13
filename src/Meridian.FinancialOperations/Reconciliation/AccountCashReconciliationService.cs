using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>Durable two-source, account-scoped cash-balance continuity over the canonical matcher.</summary>
public sealed class AccountCashReconciliationService(FileAccountCashReconciliationStore store)
{
    // The bounded cash-balance slice uses the deterministic weekends-only calendar. It does not
    // borrow a deployment's mutable settlement calendar or match positions/transaction populations.
    private readonly ReconciliationMatchingEngine _matcher = new();

    public Task<AccountCashReconciliationRun> RunAsync(AccountCashReconciliationRequest request, CancellationToken ct = default)
    {
        var input = Normalize(request);
        var inputHash = InputHash(input);
        return store.WithStateAsync(document =>
        {
            var retained = document.Runs.FirstOrDefault(item => item.Input.RunId == input.RunId && item.Input.Scope == input.Scope);
            if (retained is not null)
            {
                if (!Sha256Digest.FixedEquals(retained.InputHash, inputHash))
                    throw new InvalidOperationException("A retained run cannot be rebound to changed sources, scope or tolerance.");
                return (document, retained);
            }
            foreach (var source in new[] { input.Left, input.Right })
            {
                if (document.Runs.Where(item => item.Input.Scope == input.Scope)
                    .SelectMany(item => new[] { item.Input.Left, item.Input.Right })
                    .Any(prior => prior.SourceId == source.SourceId && prior.SourceType != source.SourceType))
                    throw new InvalidOperationException("A retained source identity cannot change source type.");
            }
            var relevantCases = document.Cases.Where(item => Owns(input, item)).ToArray();
            var latest = document.Runs.Where(item => item.Input.Scope == input.Scope
                && item.Input.Left.SourceId == input.Left.SourceId && item.Input.Right.SourceId == input.Right.SourceId)
                .MaxBy(item => item.Input.ObservedAtUtc);
            if (latest is not null && input.ObservedAtUtc <= latest.Input.ObservedAtUtc)
                throw new InvalidOperationException("A new run must be later than the retained comparison head.");
            var currencies = input.Left.Rows.Concat(input.Right.Rows).Select(item => item.Row.Currency)
                .Concat(relevantCases.Select(item => item.Currency)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var comparisons = Evaluate(input, currencies);
            var run = new AccountCashReconciliationRun(input, inputHash, comparisons, ResultHash(comparisons));
            var cases = document.Cases.ToDictionary(item => item.CaseId, StringComparer.Ordinal);
            foreach (var comparison in comparisons)
            {
                var caseId = CaseId(input, comparison.Currency);
                cases.TryGetValue(caseId, out var prior);
                if (prior is null && comparison.ObservationState == "SourceCleared")
                    continue;
                var cleared = comparison.ObservationState == "SourceCleared";
                var observation = new AccountCashCaseObservation(input.RunId, input.ObservedAtUtc, comparison.ObservationState, run.ResultHash);
                cases[caseId] = new AccountCashReconciliationCase(caseId, input.Scope, input.Left.SourceId, input.Right.SourceId,
                    comparison.Currency, prior?.Owner ?? input.InitialOwner, (prior?.Version ?? 0) + 1,
                    prior?.FirstObservedAtUtc ?? input.ObservedAtUtc, input.ObservedAtUtc, comparison.ObservationState,
                    cleared ? prior?.ResolvedAtUtc ?? input.ObservedAtUtc : null,
                    cleared ? prior?.ResolutionRunId ?? input.RunId : null,
                    [.. prior?.History ?? [], observation], prior?.Assignments ?? []);
            }
            var updated = document with { Runs = [.. document.Runs, run], Cases = cases.Values.OrderBy(item => item.CaseId, StringComparer.Ordinal).ToArray() };
            return (updated, run);
        }, true, ct);
    }

    public Task<AccountCashReconciliationRun> ReplayAsync(AccountCashReconciliationScope scope, string runId, CancellationToken ct = default)
    {
        RequireScope(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return store.WithStateAsync(document =>
        {
            var run = document.Runs.SingleOrDefault(item => item.Input.Scope == scope && item.Input.RunId == runId)
                ?? throw new KeyNotFoundException("The requested run was not retained in this exact account scope.");
            var input = Normalize(run.Input);
            var comparisons = Evaluate(input, run.Comparisons.Select(item => item.Currency).ToArray());
            if (!Sha256Digest.FixedEquals(run.InputHash, InputHash(input)) || !Sha256Digest.FixedEquals(run.ResultHash, ResultHash(comparisons)))
                throw new InvalidOperationException("Retained reconciliation evidence did not reproduce under the executed tolerance.");
            return (document, run with { Comparisons = comparisons });
        }, false, ct);
    }

    public Task<IReadOnlyList<AccountCashReconciliationCase>> GetCasesAsync(AccountCashReconciliationScope scope, CancellationToken ct = default)
    {
        RequireScope(scope);
        return store.WithStateAsync<IReadOnlyList<AccountCashReconciliationCase>>(document =>
            (document, document.Cases.Where(item => item.Scope == scope).ToArray()), false, ct);
    }

    public Task<AccountCashReconciliationCase> AssignAsync(AccountCashReconciliationScope scope, string caseId, int expectedVersion,
        string owner, string actor, string reason, DateTimeOffset assignedAtUtc, CancellationToken ct = default)
    {
        RequireScope(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (assignedAtUtc == default)
            throw new ArgumentException("Assignment time is required.", nameof(assignedAtUtc));
        return store.WithStateAsync(document =>
        {
            var item = document.Cases.SingleOrDefault(item => item.Scope == scope && item.CaseId == caseId)
                ?? throw new KeyNotFoundException("The requested case was not retained in this exact account scope.");
            if (item.Version != expectedVersion)
                throw new InvalidOperationException("The case changed; reload before assigning an owner.");
            var updated = item with
            {
                Owner = owner.Trim(),
                Version = item.Version + 1,
                Assignments = [.. item.Assignments, new(owner.Trim(), actor.Trim(), reason.Trim(), assignedAtUtc.ToUniversalTime())]
            };
            return (document with { Cases = document.Cases.Select(prior => prior.CaseId == caseId ? updated : prior).ToArray() }, updated);
        }, true, ct);
    }

    private AccountCashComparison[] Evaluate(AccountCashReconciliationRequest input, IReadOnlyList<string> currencies)
        => currencies.Select(currency =>
        {
            var left = input.Left.Rows.Where(item => item.Row.Currency == currency).Select(item => item.Row).ToArray();
            var right = input.Right.Rows.Where(item => item.Row.Currency == currency).Select(item => item.Row).ToArray();
            var snapshots = new[]
            {
                new DataSourceSnapshot("left:" + input.Left.SourceId, input.Left.SourceType, input.Left.CapturedAtUtc, input.Left.Version, [], left),
                new DataSourceSnapshot("right:" + input.Right.SourceId, input.Right.SourceType, input.Right.CapturedAtUtc, input.Right.Version, [], right)
            };
            var run = new ReconciliationRun(Guid.Empty, input.BusinessDate, input.ObservedAtUtc, false, 1,
                CaseId(input, currency) + ":" + input.RunId, snapshots);
            var result = _matcher.Run(run, input.Tolerance);
            var cleared = left.Length == 1 && right.Length == 1 && result.breaks.Count == 0
                && result.matches.Count == 1 && result.matches[0].Classification is BreakClassification.Matched or BreakClassification.MatchedWithinTolerance;
            return new AccountCashComparison(currency, cleared ? "SourceCleared" : left.Length == 0 || right.Length == 0 ? "MissingEvidence" : "Open",
                result.matches, result.breaks, result.evidence);
        }).ToArray();

    private static AccountCashReconciliationRequest Normalize(AccountCashReconciliationRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);
        RequireScope(input.Scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.InitialOwner);
        ArgumentNullException.ThrowIfNull(input.Tolerance);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Tolerance.ProfileId);
        if (input.BusinessDate == default || input.ObservedAtUtc == default || DateOnly.FromDateTime(input.ObservedAtUtc.UtcDateTime) < input.BusinessDate
            || input.Tolerance.Version < 1 || input.Tolerance.CashRules is null || input.Tolerance.PositionRules is null || input.Tolerance.TransactionRules is null)
            throw new ArgumentException("Explicit date, time and versioned tolerance contents are required.", nameof(input));
        if (input.Tolerance.CashRules.Any(rule => string.IsNullOrWhiteSpace(rule.RuleId) || rule.AbsoluteCashTolerance < 0
            || rule.BasisPointCashTolerance < 0 || rule.SettlementDateTolerance < TimeSpan.Zero)
            || input.Tolerance.CashRules.Select(rule => rule.RuleId).Distinct(StringComparer.Ordinal).Count() != input.Tolerance.CashRules.Count)
            throw new ArgumentException("Cash tolerance rules must have unique identities and nonnegative thresholds.", nameof(input));
        var sources = new[] { input.Left, input.Right }.Select(source => NormalizeSource(source, input)).OrderBy(source => source.SourceId, StringComparer.Ordinal).ToArray();
        if (sources[0].SourceId == sources[1].SourceId || sources[0].SourceType == sources[1].SourceType)
            throw new ArgumentException("The first cash-balance slice requires two independent source identities and types.", nameof(input));
        // Freeze caller-owned lists before they cross the durable boundary.
        var normalized = input with { RunId = input.RunId.Trim(), Left = sources[0], Right = sources[1], InitialOwner = input.InitialOwner.Trim(), ObservedAtUtc = input.ObservedAtUtc.ToUniversalTime() };
        return JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(normalized,
            AccountCashReconciliationJsonContext.Default.AccountCashReconciliationRequest), AccountCashReconciliationJsonContext.Default.AccountCashReconciliationRequest)!;
    }

    private static AccountCashBalanceSource NormalizeSource(AccountCashBalanceSource source, AccountCashReconciliationRequest input)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.EvidenceReference);
        ArgumentNullException.ThrowIfNull(source.Rows);
        if (!Enum.IsDefined(source.SourceType) || !source.CompletePopulation || source.CapturedAtUtc == default || source.CapturedAtUtc > input.ObservedAtUtc)
            throw new ArgumentException("Both sources require complete, dated and retained observations.", nameof(source));
        var rows = source.Rows.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(item.Row);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.OriginalPayload);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Row.SourceReference);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Row.CashEntryId);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Row.Currency);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Row.BaseCurrency);
            var row = item.Row;
            if (row.AccountId != input.Scope.AccountId || row.PostedAtUtc == default || row.PostedAtUtc > source.CapturedAtUtc || DateOnly.FromDateTime(row.PostedAtUtc.UtcDateTime) != input.BusinessDate
                || row.AccountingPeriod != input.BusinessDate || row.Currency.Trim().ToUpperInvariant() != row.BaseCurrency.Trim().ToUpperInvariant() || row.AmountBase != row.Amount)
                throw new ArgumentException("Cash balances require exact account/date and native-currency amounts; unrelated accounts and undeclared FX are refused.", nameof(source));
            return item with { Row = row with { Currency = row.Currency.Trim().ToUpperInvariant(), BaseCurrency = row.BaseCurrency.Trim().ToUpperInvariant() } };
        }).OrderBy(item => item.Row.Currency, StringComparer.Ordinal).ToArray();
        if (rows.Select(item => item.Row.Currency).Distinct(StringComparer.Ordinal).Count() != rows.Length)
            throw new ArgumentException("This slice accepts one cash balance per source and currency; duplicates are ambiguous.", nameof(source));
        return source with { SourceId = source.SourceId.Trim(), Rows = rows, CapturedAtUtc = source.CapturedAtUtc.ToUniversalTime() };
    }

    private static bool Owns(AccountCashReconciliationRequest input, AccountCashReconciliationCase item)
        => item.Scope == input.Scope && item.LeftSourceId == input.Left.SourceId && item.RightSourceId == input.Right.SourceId;

    private static void RequireScope(AccountCashReconciliationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.CompanyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.AccountId);
        if (scope.LedgerBookId == Guid.Empty)
            throw new ArgumentException("An exact ledger book is required.", nameof(scope));
    }

    private static string CaseId(AccountCashReconciliationRequest input, string currency)
        => "cash-balance:" + Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(new[] { input.Scope.TenantId, input.Scope.CompanyId,
            input.Scope.AccountId, input.Scope.LedgerBookId.ToString("D"), input.Left.SourceId, input.Left.SourceType.ToString(),
            input.Right.SourceId, input.Right.SourceType.ToString(), currency }, AccountCashReconciliationJsonContext.Default.StringArray));
    private static string InputHash(AccountCashReconciliationRequest input)
        => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(input, AccountCashReconciliationJsonContext.Default.AccountCashReconciliationRequest));
    private static string ResultHash(AccountCashComparison[] result)
        => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(result, AccountCashReconciliationJsonContext.Default.AccountCashComparisonArray));
}
