using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Subscriptions.Models;
using Meridian.Ledger;

namespace Meridian.Application.Subscriptions.Services;

/// <summary>Projects a complete account at cost; subscription filters never remove opening assets.</summary>
public static class PortfolioOpeningBalanceProjector
{
    public static AutomatedJournalDraft Build(PortfolioImportRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var snapshot = run.Snapshot ?? throw new InvalidOperationException("An opening requires a retained complete broker snapshot.");
        if (run.SnapshotHash != PortfolioImportRunService.SnapshotHash(snapshot))
            throw new InvalidDataException("Opening source evidence does not match its retained hash.");
        var scope = run.Request.Scope;
        if (scope.EntityId.GetValueOrDefault() == Guid.Empty)
            throw new InvalidOperationException("Opening balances require the retained financial account entity.");
        if (snapshot.AccountId != run.Request.Options.BrokerAccountId || snapshot.AccountId is "unknown" or "manual")
            throw new InvalidOperationException("Opening account identity is missing or mismatched.");
        if (scope.EffectiveDate != DateOnly.FromDateTime(snapshot.RetrievedAt.UtcDateTime))
            throw new InvalidOperationException("A current broker snapshot can only open books on its observation date.");
        if (!snapshot.CashBalance.HasValue || string.IsNullOrWhiteSpace(snapshot.Currency) || snapshot.Currency.Length != 3)
            throw new InvalidOperationException("Opening balances require cash and currency evidence.");
        if (snapshot.Positions.GroupBy(p => p.Symbol.Trim().ToUpperInvariant()).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Repeated position identities make broker population completeness ambiguous.");
        var accountId = scope.FinancialAccountId.ToString("D");
        var dimensions = new LedgerLineDimensionSet(FundId: scope.FundProfileId,
            EntityId: scope.EntityId!.Value.ToString("D"),
            BookId: scope.LedgerBookId.ToString("D"), AccountId: accountId, OrganizationId: scope.CompanyId);
        var lines = new List<(LedgerAccount account, decimal debit, decimal credit, LedgerLineDimensionSet? dimensions)>();
        void AddSigned(LedgerAccount account, decimal signedDebit)
        {
            if (signedDebit != 0m)
                lines.Add((account, Math.Max(signedDebit, 0m), Math.Max(-signedDebit, 0m), dimensions));
        }
        AddSigned(LedgerAccounts.CashFor(accountId), snapshot.CashBalance.Value);
        foreach (var position in snapshot.Positions)
        {
            if (position.AssetClass?.ToLowerInvariant() is not (AlpacaAssetClassCodes.UsEquity or "equity" or "stock" or "stocks" or "stk" or "etf" or "etfs"))
                throw new InvalidOperationException($"Opening {position.Symbol} requires retained instrument price-basis and multiplier evidence; only unit-priced equities are supported by this import.");
            if ((position.Quantity < 0m && !string.Equals(position.Side, "short", StringComparison.OrdinalIgnoreCase)) ||
                (position.Quantity > 0m && !string.Equals(position.Side, "long", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Opening {position.Symbol} quantity and position direction disagree.");
            if (!string.Equals(position.Currency, snapshot.Currency, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A single opening journal requires one book currency; currency conversion needs retained FX evidence.");
            if (!position.AverageCost.HasValue || position.AverageCost < 0 || (position.Quantity != 0m && position.AverageCost == 0))
                throw new InvalidOperationException($"Opening position {position.Symbol} lacks a valid cost basis.");
            var cost = checked(position.Quantity * position.AverageCost.Value);
            AddSigned(cost < 0 ? LedgerAccounts.ShortSecuritiesPayable(position.Symbol, accountId)
                : LedgerAccounts.Securities(position.Symbol, accountId), cost);
        }
        var equity = lines.Sum(line => line.debit - line.credit);
        AddSigned(LedgerAccounts.CapitalAccountFor(accountId), -equity);
        if (lines.Count == 0)
            throw new InvalidOperationException("A zero-balance account has no opening journal to post.");
        var key = $"portfolio-opening:{scope.LedgerBookId:D}:{accountId}";
        var sourceUri = $"portfolio-import://runs/{run.Request.RunId:D}/snapshot/{run.SnapshotHash}";
        var evidence = new JournalEvidenceReference(run.Request.RunId.ToString("D"), sourceUri,
            "BrokerOpeningSnapshot", snapshot.Broker, run.StartedAtUtc, scope.Actor,
            SubjectId: accountId, ContentHash: run.SnapshotHash, EffectiveDate: scope.EffectiveDate);
        var description = $"Opening balances from {snapshot.Broker} account {snapshot.AccountId}";
        var metadata = new JournalEntryMetadata(ActivityType: "portfolio-opening", FinancialAccountId: accountId,
            EffectiveDate: scope.EffectiveDate, IdempotencyKey: key,
            Tags: new Dictionary<string, string> { ["import.runId"] = run.Request.RunId.ToString("D"), ["import.snapshotHash"] = run.SnapshotHash! },
            EvidenceReferences: [evidence]);
        return new AutomatedJournalDraft(new AutomatedJournalEvent(AutomatedJournalEventKind.OpeningBalanceImported,
            "ACCOUNT", lines.Sum(line => line.debit), snapshot.RetrievedAt, accountId, description,
            SourceEventId: run.Request.RunId.ToString("D"), EffectiveDate: scope.EffectiveDate,
            IdempotencyKey: key, EvidenceReferences: [evidence]), description, lines, metadata);
    }
}
