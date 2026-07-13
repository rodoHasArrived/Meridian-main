using Meridian.Contracts.FundStructure;

namespace Meridian.PortfolioRecords.FundAccounts;

/// <summary>
/// Shared reconciliation-check builders for <see cref="InMemoryFundAccountService"/> and
/// <see cref="PostgresFundAccountService"/>. Every check compares two genuinely independent
/// sources; when no independent counterpart exists for a value, the check is reported as
/// <see cref="StatusUnverified"/> instead of fabricating agreement. "Matched" only ever
/// appears when two independently sourced values were actually compared and agreed.
/// </summary>
internal static class AccountReconciliationChecks
{
    internal const string StatusMatched = "Matched";
    internal const string StatusBreak = "Break";
    internal const string StatusUnverified = "Unverified";

    internal const string RunStatusMatched = "Matched";
    internal const string RunStatusBreaks = "Breaks";
    internal const string RunStatusUnverified = "Unverified";

    /// <summary>An unverified check is not a break: no independent counterpart was available to compare.</summary>
    internal static bool IsUnverified(AccountReconciliationResultDto result) =>
        string.Equals(result.Status, StatusUnverified, StringComparison.OrdinalIgnoreCase);

    /// <summary>A break is a check where two independently sourced values were compared and disagreed.</summary>
    internal static bool IsBreak(AccountReconciliationResultDto result) =>
        !result.IsMatch && !IsUnverified(result);

    /// <summary>
    /// Compares the internally recorded balance-snapshot cash balance (source A) against the
    /// external bank-statement closing balance ingested for the same as-of date (source B).
    /// When either side is missing, the check reports <see cref="StatusUnverified"/> because no
    /// independent comparison was possible. Returns <c>null</c> when neither side has any data.
    /// </summary>
    internal static AccountReconciliationResultDto? BuildCashBalanceCheck(
        Guid runId,
        AccountBalanceSnapshotDto? snapshot,
        IReadOnlyList<BankStatementLineDto> bankLinesForDate)
    {
        var bankClosingBalance = bankLinesForDate
            .Where(static line => line.ClosingBalance.HasValue)
            .OrderBy(static line => line.ValueDate)
            .ThenBy(static line => line.LineId)
            .Select(static line => line.ClosingBalance)
            .LastOrDefault();

        if (snapshot is null && bankClosingBalance is null)
        {
            return null;
        }

        if (snapshot is not null && bankClosingBalance is not null)
        {
            var variance = bankClosingBalance.Value - snapshot.CashBalance;
            return new AccountReconciliationResultDto(
                Guid.NewGuid(),
                runId,
                CheckLabel: "CashBalance",
                IsMatch: variance == 0m,
                Category: "Cash",
                Status: variance == 0m ? StatusMatched : StatusBreak,
                ExpectedAmount: snapshot.CashBalance,
                ActualAmount: bankClosingBalance.Value,
                Variance: variance,
                Reason: variance == 0m
                    ? "Recorded cash balance agrees with the bank statement closing balance."
                    : "Recorded cash balance diverges from the bank statement closing balance.");
        }

        if (snapshot is not null)
        {
            return new AccountReconciliationResultDto(
                Guid.NewGuid(),
                runId,
                CheckLabel: "CashBalance",
                IsMatch: false,
                Category: "Cash",
                Status: StatusUnverified,
                ExpectedAmount: snapshot.CashBalance,
                ActualAmount: null,
                Variance: null,
                Reason: "No independent source was available to verify the recorded cash balance: no bank statement closing balance exists for the as-of date.");
        }

        return new AccountReconciliationResultDto(
            Guid.NewGuid(),
            runId,
            CheckLabel: "CashBalance",
            IsMatch: false,
            Category: "Cash",
            Status: StatusUnverified,
            ExpectedAmount: null,
            ActualAmount: bankClosingBalance,
            Variance: null,
            Reason: "A bank statement closing balance exists, but no internally recorded balance snapshot was available for the as-of date.");
    }

    /// <summary>
    /// Compares the custodian statement's declared line count (source A, batch header metadata
    /// captured at ingestion) against the count of persisted custodian position records for the
    /// as-of date (source B). When no batch metadata exists to declare an expected count, the
    /// check reports <see cref="StatusUnverified"/>. Returns <c>null</c> when neither positions
    /// nor batch metadata exist for the date.
    /// </summary>
    internal static AccountReconciliationResultDto? BuildPositionCountCheck(
        Guid runId,
        IReadOnlyList<CustodianPositionLineDto> positionsForDate,
        IReadOnlyList<CustodianStatementBatchDto> batchesForDate)
    {
        var latestBatch = batchesForDate
            .OrderBy(static batch => batch.IngestedAt)
            .ThenBy(static batch => batch.BatchId)
            .LastOrDefault();

        if (positionsForDate.Count == 0 && latestBatch is null)
        {
            return null;
        }

        if (latestBatch is not null)
        {
            var variance = positionsForDate.Count - latestBatch.LineCount;
            return new AccountReconciliationResultDto(
                Guid.NewGuid(),
                runId,
                CheckLabel: $"PositionCount ({positionsForDate.Count} lines)",
                IsMatch: variance == 0,
                Category: "Positions",
                Status: variance == 0 ? StatusMatched : StatusBreak,
                ExpectedAmount: latestBatch.LineCount,
                ActualAmount: positionsForDate.Count,
                Variance: variance,
                Reason: variance == 0
                    ? "Persisted custodian position lines agree with the latest statement's declared line count."
                    : "Persisted custodian position line count diverges from the latest statement's declared line count.");
        }

        return new AccountReconciliationResultDto(
            Guid.NewGuid(),
            runId,
            CheckLabel: $"PositionCount ({positionsForDate.Count} lines)",
            IsMatch: false,
            Category: "Positions",
            Status: StatusUnverified,
            ExpectedAmount: null,
            ActualAmount: positionsForDate.Count,
            Variance: null,
            Reason: "No custodian statement batch metadata was available to verify the persisted position line count.");
    }

    /// <summary>
    /// Builds the run summary. A run is only "Matched" when at least one check ran and every
    /// check compared two independent values that agreed. Unverified checks demote the run to
    /// "Unverified" without counting as breaks; an empty run is likewise "Unverified" because
    /// nothing was compared.
    /// </summary>
    internal static AccountReconciliationRunDto BuildRunSummary(
        Guid runId,
        Guid accountId,
        DateOnly asOfDate,
        string requestedBy,
        DateTimeOffset now,
        IReadOnlyList<AccountReconciliationResultDto> results)
    {
        var breaks = results.Count(IsBreak);
        var matched = results.Count(static result => result.IsMatch);

        var status = breaks > 0
            ? RunStatusBreaks
            : matched == results.Count && results.Count > 0
                ? RunStatusMatched
                : RunStatusUnverified;

        return new AccountReconciliationRunDto(
            runId,
            accountId,
            asOfDate,
            Status: status,
            TotalChecks: results.Count,
            TotalMatched: matched,
            TotalBreaks: breaks,
            BreakAmountTotal: results
                .Where(result => IsBreak(result) && result.Variance.HasValue)
                .Sum(static result => Math.Abs(result.Variance!.Value)),
            RequestedAt: now,
            CompletedAt: now,
            requestedBy);
    }
}
