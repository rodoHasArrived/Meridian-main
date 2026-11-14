using Meridian.Contracts.Integrity;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Ledger;
using Meridian.Storage.Archival;
using Meridian.Storage.Ledger;

namespace Meridian.Execution.Events;

/// <summary>Opt-in configuration for one reviewed synthetic accounting cutover.</summary>
public sealed class AutomatedTradeFillAccountingOptions
{
    public AutomatedPostingGrantReferenceDto? Grant { get; set; }
    public string CashAccountPath { get; set; } = "";
    public string SecuritiesAccountPath { get; set; } = "";
    public string RealizedGainAccountPath { get; set; } = "";
    public string RealizedLossAccountPath { get; set; } = "";
    public string CommissionAccountPath { get; set; } = "";
    public string FeeAccountPath { get; set; } = "";
}

public interface IAutomatedTradeFillAccountingService
{
    Task<IReadOnlyList<JournalEntry>?> TryRecoverCompletedAsync(TradeExecutedEvent fill,
        TradeFillLedgerPostingContext context, CancellationToken ct = default);

    Task<IReadOnlyList<JournalEntry>> PostAsync(TradeExecutedEvent fill, TradeFillLedgerPostingContext context,
        Guid securityId, CancellationToken ct = default);
}

/// <summary>
/// Application adapter for synthetic, long USD fills. Its retained intent owns lot selection;
/// accounting receipts, never OMS P&amp;L, prove completion. This is not a human-approved Asset Spine event.
/// </summary>
public sealed class AutomatedTradeFillAccountingService(
    AutomatedTradeFillAccountingOptions options, string intentRoot,
    ILedgerJournalStore journals, IAtomicTaxLotJournalStore atomicLots,
    IGovernedLedgerPostingTarget journalTarget, IConfiguredLedgerAccountResolver accounts,
    IAccountingAutomaticPostingPolicyValidator policies, ISecurityMasterQueryService securities,
    IAssetOperationsQueryService positions) : IAutomatedTradeFillAccountingService, IDisposable
{
    private const string Actor = "authorized-synthetic-fill-accounting";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    public async Task<IReadOnlyList<JournalEntry>?> TryRecoverCompletedAsync(TradeExecutedEvent fill,
        TradeFillLedgerPostingContext context, CancellationToken ct = default)
    {
        fill = fill.FreezeFees() with { OccurredAt = fill.OccurredAt.ToUniversalTime() };
        ValidateFill(fill, context);
        var grant = options.Grant ?? throw new InvalidOperationException("An exact configured grant is required.");
        using var tenantAuthority = FundScopeTenantAuthority.Enter(grant.TenantId, "synthetic-fill-receipt-recovery");
        var path = Path.Combine(intentRoot, new TradeFillPostingStoreOptions(intentRoot, context).ScopeStorageKey, $"{fill.FillId:N}.json");
        if (!File.Exists(path))
            return null;
        var intent = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false),
            FillAccountingJsonContext.Default.PreparedFillAccountingIntent)
            ?? throw new InvalidDataException("The retained fill accounting intent is unreadable.");
        if (intent.Version != 2 || intent.FillHash != Hash(JsonSerializer.Serialize(fill, ExecutionJsonContext.Default.TradeExecutedEvent))
            || intent.Scope != context || intent.Trade.Journal.PostingCommand?.AutomationGrant != grant
            || !HasBoundFillEvidence(intent)
            || intent.ContentHash != IntentHash(intent with { ContentHash = "" }))
            throw new InvalidDataException("Completed fill recovery conflicts with the retained accounting intent.");
        var atomic = await journals.GetAtomicTaxLotPostingAsync(intent.Trade.MutationBatchId, ct).ConfigureAwait(false);
        if (atomic is null)
            return null;
        if (atomic.CanonicalFingerprint != intent.Trade.CanonicalFingerprint)
            throw new InvalidDataException("The retained atomic receipt conflicts with the fill intent.");
        DurableLedgerPostingTarget.VerifyRetainedEntry(atomic.Journal, intent.Trade.Journal);
        var entries = new List<JournalEntry> { atomic.Journal.Entry };
        foreach (var fee in intent.Fees)
        {
            var retained = (await journals.FindPostingIdentityCollisionsAsync(LedgerPostingIdentity.FromWrite(fee), ct)
                .ConfigureAwait(false)).SingleOrDefault(record => record.Entry.JournalEntryId == fee.Entry.JournalEntryId);
            if (retained is null)
                return null;
            DurableLedgerPostingTarget.VerifyRetainedEntry(retained, fee);
            entries.Add(retained.Entry);
        }
        return entries;
    }

    public async Task<IReadOnlyList<JournalEntry>> PostAsync(TradeExecutedEvent fill,
        TradeFillLedgerPostingContext context, Guid securityId, CancellationToken ct = default)
    {
        fill = fill.FreezeFees() with { OccurredAt = fill.OccurredAt.ToUniversalTime() };
        ValidateFill(fill, context);
        var grant = options.Grant ?? throw new InvalidOperationException("An explicit automatic posting grant is required.");
        if (grant.LedgerBookId != context.LedgerBookId || grant.PeriodId != context.PeriodId
            || grant.PolicyId != context.AccountingPolicyId || grant.PolicyVersion != context.AccountingPolicyVersion
            || !string.Equals(grant.FinancialAccountId, fill.FinancialAccountId, StringComparison.OrdinalIgnoreCase)
            || grant.SourceMode != "Synthetic")
            throw new InvalidOperationException("Fill accounting grant differs from the exact configured scope.");
        // The grant reference comes from trusted host configuration, never a request body.
        // A retained database grant and the append boundary reauthorize every new write.
        using var tenantAuthority = FundScopeTenantAuthority.Enter(grant.TenantId, "synthetic-fill-accounting");
        var requestHash = Hash(JsonSerializer.Serialize(fill, ExecutionJsonContext.Default.TradeExecutedEvent));
        var directory = Path.Combine(intentRoot, new TradeFillPostingStoreOptions(intentRoot, context).ScopeStorageKey);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{fill.FillId:N}.json");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // One process owns each host scope. This additional file lease refuses accidental concurrent
            // processes before either can choose and overwrite another process's lot intent.
            await using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            PreparedFillAccountingIntent intent;
            if (File.Exists(path))
            {
                intent = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false),
                    FillAccountingJsonContext.Default.PreparedFillAccountingIntent)
                    ?? throw new InvalidDataException("The retained fill accounting intent is unreadable.");
                if (intent.Version != 2 || intent.FillHash != requestHash || intent.Scope != context || intent.SecurityId != securityId
                    || intent.Trade.Journal.PostingCommand?.AutomationGrant != grant)
                    throw new InvalidDataException("Fill replay conflicts with the retained scope, reference identity, or economics.");
                if (!HasBoundFillEvidence(intent) || intent.ContentHash != IntentHash(intent with { ContentHash = "" }))
                    throw new InvalidDataException("The retained fill accounting intent failed its content hash.");
            }
            else
            {
                intent = await PrepareAsync(fill, context, securityId, requestHash, ct).ConfigureAwait(false);
                intent = intent with { ContentHash = IntentHash(intent) };
                await AtomicFileWriter.WriteAsync(path,
                    JsonSerializer.Serialize(intent, FillAccountingJsonContext.Default.PreparedFillAccountingIntent), ct).ConfigureAwait(false);
            }

            var committed = new List<JournalEntry>();
            var receipt = await journals.GetAtomicTaxLotPostingAsync(intent.Trade.MutationBatchId, ct).ConfigureAwait(false);
            if (receipt is null)
            {
                await using var policyAuthority = await AcquirePolicyAsync(intent.Trade.Journal, ct).ConfigureAwait(false);
                await atomicLots.AppendAssetPostingAsync(intent.Trade, ct).ConfigureAwait(false);
                receipt = await journals.GetAtomicTaxLotPostingAsync(intent.Trade.MutationBatchId, ct).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The atomic fill posting has no authoritative receipt.");
            }
            if (receipt.MutationBatchId != intent.Trade.MutationBatchId || receipt.CanonicalFingerprint != intent.Trade.CanonicalFingerprint)
                throw new InvalidDataException("The atomic fill receipt conflicts with the retained mutation intent.");
            DurableLedgerPostingTarget.VerifyRetainedEntry(receipt.Journal, intent.Trade.Journal);
            committed.Add(receipt.Journal.Entry);
            foreach (var fee in intent.Fees)
            {
                var retained = (await journals.FindPostingIdentityCollisionsAsync(LedgerPostingIdentity.FromWrite(fee), ct)
                    .ConfigureAwait(false)).SingleOrDefault(record => record.Entry.JournalEntryId == fee.Entry.JournalEntryId);
                if (retained is null)
                {
                    await using var policyAuthority = await AcquirePolicyAsync(fee, ct).ConfigureAwait(false);
                    await journalTarget.PostAsync(fee, ct).ConfigureAwait(false);
                    retained = (await journals.FindPostingIdentityCollisionsAsync(LedgerPostingIdentity.FromWrite(fee), ct)
                        .ConfigureAwait(false)).SingleOrDefault(record => record.Entry.JournalEntryId == fee.Entry.JournalEntryId)
                        ?? throw new InvalidDataException("A required fill fee has no durable receipt.");
                }
                DurableLedgerPostingTarget.VerifyRetainedEntry(retained, fee);
                committed.Add(retained.Entry);
            }
            return committed;
        }
        finally { _gate.Release(); }
    }

    private async Task<IAsyncDisposable> AcquirePolicyAsync(LedgerJournalEntryWrite write, CancellationToken ct)
    {
        var source = write.PostingCommand!.SourceEventType!;
        var date = write.Entry.Metadata.EffectiveDate!.Value;
        var grant = options.Grant!;
        var retained = journals is IAutomatedPostingGrantStore store
            ? await store.GetAsync(grant.GrantId, grant.TenantId, ct).ConfigureAwait(false) : null;
        if (retained is null || !retained.IsActive || retained.Reference != grant || retained.PolicySnapshot is null)
            throw new InvalidOperationException("The automatic posting policy snapshot is missing, inactive, or changed.");
        var lease = await policies.AcquireRetainedAuthorityAsync(retained.PolicySnapshot, date, [source], ct).ConfigureAwait(false);
        try
        {
            var currentRule = retained.PolicySnapshot.RulePack!.Rules.Single(rule => rule.SourceEventType == source);
            if (currentRule.RuleId != write.RuleId || currentRule.RuleVersion != write.RuleVersion)
                throw new InvalidOperationException("The selected automatic accounting rule changed after preparation.");
            return lease;
        }
        catch { await lease.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private async Task<PreparedFillAccountingIntent> PrepareAsync(TradeExecutedEvent fill,
        TradeFillLedgerPostingContext context, Guid securityId, string requestHash, CancellationToken ct)
    {
        var grant = options.Grant!;
        if (journals is not IAutomatedPostingGrantStore grantStore)
            throw new InvalidOperationException("Canonical fill accounting requires the authoritative posting-grant store.");
        var retainedGrant = await grantStore.GetAsync(grant.GrantId, grant.TenantId, ct).ConfigureAwait(false);
        if (retainedGrant is null || !retainedGrant.IsActive || retainedGrant.Reference != grant)
            throw new InvalidOperationException("The configured automatic posting grant is inactive, changed, or missing.");
        var date = DateOnly.FromDateTime(fill.OccurredAt.UtcDateTime);
        var book = await journals.GetLedgerBookAsync(context.LedgerBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The configured accounting book is missing.");
        if (book.BaseCurrency != "USD" || book.AccountingPolicyId != grant.PolicyId || book.AccountingPolicyVersion != grant.PolicyVersion)
            throw new InvalidOperationException("Canonical fill accounting requires the exact USD book policy.");
        var security = await securities.GetByIdAsync(securityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The fill security is missing.");
        if (security.Currency != "USD" || security.Status != SecurityStatusDto.Active
            || SecurityAssetClassCatalog.ResolveLabel(security.AssetClass, out var descriptor) != SecurityAssetClassLabelResolution.Resolved
            || descriptor.AssetClass != "Equity")
            throw new InvalidOperationException("Automatic fill accounting supports active USD cash equities only.");
        if (fill.SettlementCurrency is not null && fill.SettlementCurrency != security.Currency)
            throw new InvalidOperationException("Fill settlement currency differs from the security's authoritative currency.");
        var positionMatches = (await positions.GetOperationsAsync(securityId, ct).ConfigureAwait(false))?.BookPositions
            .Where(position => position.SecurityId == securityId && position.BookContext.LedgerBookId == book.LedgerBookId
                && string.Equals(position.PrimaryAccountId, fill.FinancialAccountId, StringComparison.OrdinalIgnoreCase) && position.Status == "Active"
                && position.PositionSide is "Long" or "Asset" && position.EffectiveFrom <= date
                && (position.EffectiveTo is null || position.EffectiveTo >= date)).ToArray() ?? [];
        if (positionMatches.Length != 1)
            throw new InvalidOperationException("The fill requires one active, account-owned long book position.");
        var position = positionMatches[0];
        var sources = new List<string> { "execution.trade-fill" };
        if (fill.Commission > 0m)
            sources.Add("execution.trade-commission");
        if (fill.Fees is { Count: > 0 })
            sources.Add("execution.trade-fee");
        var policySnapshot = retainedGrant.PolicySnapshot
            ?? throw new InvalidOperationException("The automatic posting grant has no durable operator-authorized policy snapshot.");
        await using var preparationAuthority = await policies.AcquireRetainedAuthorityAsync(policySnapshot, date, sources, ct).ConfigureAwait(false);
        var rules = sources.ToDictionary(source => source,
            source => policySnapshot.RulePack!.Rules.Single(rule => rule.SourceEventType == source), StringComparer.Ordinal);

        async Task<LedgerAccount> Resolve(string path, LedgerAccountType type, bool instrument)
        {
            var node = await accounts.ResolveAsync(new ConfiguredLedgerAccountRequestDto(grant.TenantId, grant.CompanyId,
                book.FundProfileId, book.LedgerBookId, path, type.ToString(), grant.MappingRevision,
                instrument ? fill.Symbol : null, fill.FinancialAccountId), ct).ConfigureAwait(false);
            return new LedgerAccount(node.AccountName, type, instrument ? fill.Symbol : null, fill.FinancialAccountId);
        }
        var cash = await Resolve(options.CashAccountPath, LedgerAccountType.Asset, false).ConfigureAwait(false);
        if (cash != LedgerAccounts.CashFor(fill.FinancialAccountId))
            throw new InvalidOperationException("Canonical fill accounting requires the native account-scoped Cash mapping.");
        var carrying = await Resolve(options.SecuritiesAccountPath, LedgerAccountType.Asset, true).ConfigureAwait(false);
        var gain = await Resolve(options.RealizedGainAccountPath, LedgerAccountType.Revenue, false).ConfigureAwait(false);
        var loss = await Resolve(options.RealizedLossAccountPath, LedgerAccountType.Expense, false).ConfigureAwait(false);
        if (gain != LedgerAccounts.RealizedGainOrDefault(fill.FinancialAccountId)
            || loss != LedgerAccounts.RealizedLossOrDefault(fill.FinancialAccountId))
            throw new InvalidOperationException("Canonical fill accounting requires the native account-scoped Realized Gain and Realized Loss mappings.");
        // Both directions remain cash funded: sale proceeds may cover expenses, but a large
        // charge cannot create an implicit overdraft after otherwise valid lot relief.
        {
            var history = await journals.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: book.LedgerBookId,
                AccountName: cash.Name, OccurredTo: fill.OccurredAt, EffectiveTo: date), ct).ConfigureAwait(false);
            var cashLines = history.SelectMany(record => record.Entry.Lines).Where(line => line.Account == cash).ToArray();
            if (cashLines.Length == 0)
                throw new InvalidOperationException("Canonical fill accounting requires retained opening cash evidence for the exact financial account.");
            if (cashLines.Any(line => line.Currency is not { TransactionCurrency: "USD", FunctionalCurrency: "USD", FxRateToFunctional: 1m }
                || line.Currency.TransactionDebit != line.Debit || line.Currency.TransactionCredit != line.Credit))
                throw new InvalidOperationException("The retained cash history has unproven currency; review the opening state before automatic posting.");
            var available = cashLines.Sum(line => line.Debit - line.Credit);
            var required = fill.Commission + (fill.Fees?.Sum(fee => fee.Amount) ?? 0m)
                + (fill.Side == OrderSide.Buy ? fill.GrossValue : -fill.GrossValue);
            if (available < required)
                throw new InvalidOperationException("Insufficient proven cash for this fully funded fill and its required fees; implicit margin is unsupported.");
        }
        var evidence = new RetainedEvidenceIdentityDto($"execution-fill:{fill.FillId:D}", $"evidence://execution/fills/{fill.FillId:D}",
            requestHash[7..], "SyntheticOMS", fill.FillId.ToString("D"), "Accepted", Actor, fill.OccurredAt, date, 1,
            fill.OccurredAt, Actor, "ExecutionFill", fill.FillId.ToString("D"));
        var batchId = Identity(context, fill.FillId, "trade:batch");
        LedgerTaxLotRecord? acquisition = null;
        IReadOnlyList<RetainedEvidenceIdentityDto> mutationEvidence = [evidence];
        IReadOnlyList<LedgerTaxLotDisposalSelection>? selections = null;
        string? reliefPolicy = null;
        var tradeLines = new List<(LedgerAccount Account, decimal Debit, decimal Credit)>();
        if (fill.Side == OrderSide.Buy)
        {
            tradeLines.Add((carrying, fill.GrossValue, 0m));
            tradeLines.Add((cash, 0m, fill.GrossValue));
            var lotId = Identity(context, fill.FillId, "lot");
            var lotEvidence = evidence with
            {
                EvidenceId = $"execution-lot:{lotId:D}",
                EvidenceUri = $"evidence://execution/fills/{fill.FillId:D}/acquisition",
                SubjectType = "OpenLotAcquisition",
                SubjectId = lotId.ToString("D")
            };
            mutationEvidence = [evidence, lotEvidence];
            acquisition = new LedgerTaxLotRecord(lotId, book.LedgerBookId, carrying,
                $"execution:{fill.FillId:D}", date, fill.FilledQuantity, fill.FilledQuantity, fill.FillPrice, "USD",
                fill.OccurredAt, fill.OccurredAt, SourceJournalEntryId: Identity(context, fill.FillId, "trade:journal"),
                EvidenceRef: lotEvidence.EvidenceId, Version: 1, OriginatingMutationBatchId: batchId, LastMutationBatchId: batchId,
                SecurityId: securityId, BookPositionId: position.PositionId,
                Acquisition: new OpenLotAcquisitionDto(LotQuantityBasis.Units, "USD", "USD", 1m,
                    fill.GrossValue, fill.GrossValue, date, null, [evidence, lotEvidence]));
        }
        else
        {
            var policy = (await journals.ListTaxLotPoliciesAsync(book.LedgerBookId, ct).ConfigureAwait(false))
                .Where(policy => policy.Account == carrying && policy.EffectiveDate <= date)
                .OrderByDescending(policy => policy.EffectiveDate).FirstOrDefault()
                ?? throw new InvalidOperationException("A retained FIFO account policy is required before disposal.");
            if (policy.ReliefMethod != LedgerTaxLotReliefMethod.Fifo)
                throw new InvalidOperationException("The automatic fill slice supports FIFO only.");
            reliefPolicy = policy.PolicyId;
            var lots = (await journals.ListOpenTaxLotsAsync(book.LedgerBookId, carrying, ct).ConfigureAwait(false))
                .Where(lot => lot.SecurityId == securityId && lot.BookPositionId == position.PositionId && lot.AcquiredDate <= date).ToArray();
            var relief = new OpenLotReliefService().Select(lots.Select(lot => lot.ToOpenLot()).ToArray(), fill.FilledQuantity, OpenLotReliefMethod.Fifo);
            selections = relief.Selections.Select((selected, index) =>
            {
                var lot = lots.Single(lot => lot.TaxLotRecordId == selected.TaxLotRecordId);
                return new LedgerTaxLotDisposalSelection(selected.TaxLotRecordId, selected.LotId, selected.ExpectedVersion,
                    lot.OpenQuantity, selected.Quantity, index, evidence.EvidenceId,
                    lot.UnitCost, selected.FunctionalCostBasis);
            }).ToArray();
            tradeLines.Add((cash, fill.GrossValue, 0m));
            tradeLines.Add((carrying, 0m, relief.FunctionalCostBasis));
            var pnl = fill.GrossValue - relief.FunctionalCostBasis;
            if (pnl > 0m)
                tradeLines.Add((gain, 0m, pnl));
            if (pnl < 0m)
                tradeLines.Add((loss, -pnl, 0m));
        }
        var trade = BuildWrite(fill, context, book, position, security, "trade", "execution.trade-fill", tradeLines, evidence, rules["execution.trade-fill"]);
        trade = trade with { Entry = LedgerCashFlowAttribution.WithClassifiedActivity(trade.Entry, LedgerCashFlowCategory.Investing, "Security trade") };
        var atomic = AtomicTaxLotJournalCommand.Create(batchId, book.LedgerBookId, trade, trade.SourceEventId!.Value,
            trade.Entry.Metadata.IdempotencyKey!, context.ExpectedPeriodVersion!.Value,
            fill.Side == OrderSide.Buy ? AtomicTaxLotMutationKind.Acquisition : AtomicTaxLotMutationKind.Disposal,
            mutationEvidence, acquisitionLot: acquisition, disposalSelections: selections,
            reliefMethod: reliefPolicy is null ? null : "Fifo", policyRevision: reliefPolicy,
            disposalSalePrice: fill.Side == OrderSide.Sell ? fill.FillPrice : null,
            referenceVersions: new AtomicLotReferenceVersions(security.Version, position.Version));
        var fees = new List<LedgerJournalEntryWrite>();
        if (fill.Commission > 0m)
            fees.Add(BuildWrite(fill, context, book, position, security, "commission", "execution.trade-commission",
                [(await Resolve(options.CommissionAccountPath, LedgerAccountType.Expense, false).ConfigureAwait(false), fill.Commission, 0m), (cash, 0m, fill.Commission)], evidence, rules["execution.trade-commission"]));
        foreach (var fee in fill.Fees ?? [])
            fees.Add(BuildWrite(fill, context, book, position, security, $"fee:{fee.FeeId}", "execution.trade-fee",
                [(await Resolve(options.FeeAccountPath, LedgerAccountType.Expense, false).ConfigureAwait(false), fee.Amount, 0m), (cash, 0m, fee.Amount)], evidence, rules["execution.trade-fee"]));
        return new PreparedFillAccountingIntent(2, requestHash, context, securityId, atomic, fees, "");
    }

    private LedgerJournalEntryWrite BuildWrite(TradeExecutedEvent fill, TradeFillLedgerPostingContext context,
        LedgerBookRecord book, BookPositionDto position, SecurityDetailDto security, string activity, string source,
        IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> amounts, RetainedEvidenceIdentityDto evidence, AccountingPolicyRuleDto rule)
    {
        var securityId = security.SecurityId;
        var id = Identity(context, fill.FillId, activity + ":journal");
        var date = DateOnly.FromDateTime(fill.OccurredAt.UtcDateTime);
        var sourceId = Identity(context, fill.FillId, activity + ":source");
        var commandId = Identity(context, fill.FillId, activity + ":command");
        var key = $"execution:{context.PostingScope}:{fill.FillId:D}:{activity}";
        var dimensions = new LedgerLineDimensionSet(InstrumentId: securityId, BookId: book.LedgerBookId.ToString("D"),
            AccountId: fill.FinancialAccountId)
        { PositionId = position.PositionId };
        var lines = amounts.Select((line, index) => new LedgerEntry(Identity(context, fill.FillId, $"{activity}:line:{index}"), id,
            fill.OccurredAt, line.Account, line.Debit, line.Credit, source, dimensions,
            new LedgerEntryCurrency("USD", "USD", line.Debit, line.Credit, 1m))).ToArray();
        var tags = new Dictionary<string, string>
        {
            ["ledgerPosting.scope"] = context.PostingScope,
            ["source.provenance"] = "Simulated",
            ["automation.authority"] = "OperatorDelegation",
            ["securityMasterProvenance"] = $"security-master:{securityId:N};server-resolved:true;status:{security.Status};version:{security.Version}",
            ["securityMasterLineage"] = $"{fill.Symbol}:{securityId:N}:ledger-map:execution:{fill.Symbol}:{securityId:N}:book-position:{position.PositionId:N}:version:{position.Version}:chart-revision:{options.Grant!.MappingRevision}:sm-approval:security-master-active:{securityId:N}:security-status:{security.Status}"
        };
        if (source == "execution.trade-fill")
        {
            tags["execution.cash-account"] = amounts.Single(line => line.Account.Symbol is null && line.Account.AccountType == LedgerAccountType.Asset).Account.Name;
            tags["execution.securities-account"] = amounts.Single(line => line.Account.Symbol is not null && line.Account.AccountType == LedgerAccountType.Asset).Account.Name;
            tags["tradeFill.price"] = fill.FillPrice.ToString(System.Globalization.CultureInfo.InvariantCulture);
            tags["tradeFill.side"] = fill.Side.ToString();
            tags["quantity"] = fill.FilledQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
            tags["externalTransactionId"] = fill.FillId.ToString("D");
        }
        if (activity.StartsWith("fee:", StringComparison.Ordinal))
        {
            tags["externalTransactionId"] = activity[4..];
            tags["execution.fee.currency"] = "USD";
            tags["execution.fee.kind"] = fill.Fees!.Single(fee => fee.FeeId == activity[4..]).Kind;
        }
        var entry = new JournalEntry(id, fill.OccurredAt, source, lines, new JournalEntryMetadata(ActivityType: source,
            Symbol: fill.Symbol, SecurityId: securityId, FillId: fill.FillId, FinancialAccountId: fill.FinancialAccountId,
            LedgerBook: book.LedgerBookId.ToString("D"), EffectiveDate: date, IdempotencyKey: key,
            Tags: tags));
        var command = new AccountingPostingCommandDto(commandId, context.AggregateId, context.PeriodId, date,
            fill.OccurredAt, key, SourceEventId: sourceId, CorrelationId: fill.FillId, ExpectedVersion: context.ExpectedPeriodVersion,
            SourceEventType: source, ApprovalState: AccountingPostingApprovalStateDto.NotRequired,
            Evidence: [new AccountingPostingEvidenceReferenceDto(evidence.EvidenceId, evidence.EvidenceUri, AccountingPostingEvidenceKindDto.Source,
                evidence.SourceSystem, evidence.RetainedAtUtc, evidence.RetainedBy, ContentHash: evidence.ContentHashSha256)],
            ActionOrigin: OperationsActionOriginDto.AuthorizedSystem, LedgerBookId: book.LedgerBookId)
        {
            Actor = Actor,
            AutomationGrant = options.Grant,
            Provenance = DataProvenance.Simulated,
            BookPositionId = position.PositionId,
            BookContext = position.BookContext with { PeriodId = context.PeriodId }
        };
        return new LedgerJournalEntryWrite(entry, context.AggregateId, context.PeriodId, commandId, fill.FillId,
            book.AccountingBasis, context.AccountingPolicyId, context.AccountingPolicyVersion, rule.RuleId, rule.RuleVersion, sourceId,
            PostingCommand: command, LedgerBookId: book.LedgerBookId);
    }

    private static void ValidateFill(TradeExecutedEvent fill, TradeFillLedgerPostingContext context)
    {
        if (context.AccountingProtocolVersion != 2 || !context.ExpectedPeriodVersion.HasValue || fill.FillId == Guid.Empty
            || fill.SourceMode != "Synthetic" || string.IsNullOrWhiteSpace(fill.FinancialAccountId)
            || fill.UsesFaceValuePercentageOfPar || fill.ContractMultiplier != 1m || fill.SettlesVariationMargin
            || fill.FilledQuantity <= 0m || fill.FillPrice <= 0m || fill.Commission < 0m
            || fill.Side is not (OrderSide.Buy or OrderSide.Sell))
            throw new InvalidOperationException("Canonical accounting requires an exact v2 scope and a positive synthetic long cash-equity fill.");
        if (fill.AccountingEvidenceVersion is not (0 or 1) || fill.AccountingEvidenceVersion == 1 && fill.AccountingEvidence is null
            || fill.AccountingEvidence is { } accounting && (accounting.SettlesVariationMargin
                || accounting.VariationMarginPnl != 0m || accounting.ShortCollateralHeld != 0m || accounting.ShortCollateralReleased != 0m
                || accounting.Opening is { Kind: not SecurityTradePostingKind.OpenLong }
                || accounting.Closing is { Kind: not SecurityTradePostingKind.CloseLong }
                || accounting.Opening is { } opening && (opening.BorrowedAmount != 0m || opening.LoanRepaid != 0m)
                || accounting.Closing is { } closing && (closing.BorrowedAmount != 0m || closing.LoanRepaid != 0m)))
            throw new InvalidOperationException("Canonical fill accounting does not support missing current evidence, short collateral, financing or variation margin.");
        var fees = fill.Fees ?? [];
        if (fees.Any(fee => string.IsNullOrWhiteSpace(fee.FeeId) || string.IsNullOrWhiteSpace(fee.Kind) || fee.Amount <= 0m || fee.Currency != "USD")
            || fees.Select(fee => fee.FeeId).Distinct(StringComparer.Ordinal).Count() != fees.Count)
            throw new InvalidOperationException("Fill fees require unique stable source identities, kinds, positive amounts, and USD currency.");
    }

    private static bool HasBoundFillEvidence(PreparedFillAccountingIntent intent)
        => intent.FillHash.StartsWith("sha256:", StringComparison.Ordinal) && intent.FillHash.Length == 71
            && intent.Trade.Journal.PostingCommand?.Evidence.Any(evidence =>
                evidence.Kind == AccountingPostingEvidenceKindDto.Source && evidence.ContentHash == intent.FillHash[7..]) == true;

    private static string Hash(string input) => "sha256:" + Sha256Digest.ComputeUtf8(input);
    private static string IntentHash(PreparedFillAccountingIntent intent) => Hash(JsonSerializer.Serialize(intent, FillAccountingJsonContext.Default.PreparedFillAccountingIntent));
    private static Guid Identity(TradeFillLedgerPostingContext context, Guid fillId, string component)
        => TradeFillAccountingIdentity.Create(context.PostingScope, fillId, component);
}

internal sealed record PreparedFillAccountingIntent(int Version, string FillHash, TradeFillLedgerPostingContext Scope,
    Guid SecurityId, AtomicTaxLotJournalCommand Trade, IReadOnlyList<LedgerJournalEntryWrite> Fees, string ContentHash);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PreparedFillAccountingIntent))]
internal sealed partial class FillAccountingJsonContext : JsonSerializerContext
{
}
