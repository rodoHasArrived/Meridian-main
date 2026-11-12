using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Consolidation;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Intercompany;

/// <summary>Resolves agreement and account authority under leases retained through ledger commit.</summary>
public sealed class IntercompanyLedgerPostingAuthority(IIntercompanyAgreementStore agreements,
    IntercompanyAgreementService service, IIntercompanyAccountBindingStore bindings,
    IAccountingConfigurationStore configuration, ILedgerJournalStore journals, IFundProfileTenancyRegistry owners,
    ILegacyIntercompanyAttributionStore? legacyAttributions = null,
    IConsolidationFxAuthority? fxAuthority = null)
    : ILedgerPostingAuthority, ILedgerBookControlPreflight
{
    public string ControlName => "intercompany";

    public async Task<IAsyncDisposable> AcquireAsync(IReadOnlyList<LedgerPostingAuthorityContext> contexts, CancellationToken ct = default)
    {
        var ordinary = new List<LedgerPostingAuthorityContext>();
        foreach (var context in contexts)
        {
            if (ConsolidationPostingEvidenceGuard.Validate(context.Write) is not null)
                context.Decisions.Add(new("intercompany", "DelegatedConsolidationAuthority", true,
                    "The governed consolidation append retains specialized mapping, source and policy authority."));
            else ordinary.Add(context);
        }
        if (ordinary.Count == 0) return EmptyLedgerAuthorityLease.Instance;
        var leases = new List<IAsyncDisposable>();
        try
        {
            leases.Add(await bindings.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false));
            leases.Add(await agreements.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false));
            if (fxAuthority is not null) leases.Add(await fxAuthority.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false));
            foreach (var context in ordinary.OrderBy(x => x.Book.LedgerBookId))
            {
                var date = context.Write.PostingCommand?.EffectiveDate ?? context.Write.Entry.Metadata.EffectiveDate
                    ?? DateOnly.FromDateTime(context.Write.Entry.Timestamp.UtcDateTime);
                if (string.IsNullOrWhiteSpace(context.TenantId))
                {
                    Check(context, "AgreementTenantUnknown", !context.Write.Entry.Lines.Any(x => x.Dimensions?.AgreementId is not null),
                        "Agreement authority requires an attributed tenant.");
                    continue;
                }
                var accounts = await bindings.ListAsync(context.Book.LedgerBookId, date, ct).ConfigureAwait(false);
                var historical = await bindings.HistoryAsync(context.Book.LedgerBookId, ct).ConfigureAwait(false);
                var owner = await owners.ResolveAsync(context.Book.FundProfileId, ct).ConfigureAwait(false);
                if (owner is null || !owner.IsHeldBy(context.TenantId))
                {
                    Check(context, "BookOwnerUnresolved", false, "The authoritative book owner is unavailable.");
                    continue;
                }
                var workspace = await configuration.GetAsync(context.Book.FundProfileId, context.Book.LedgerBookId, ct,
                    context.TenantId, owner.CompanyId).ConfigureAwait(false);
                var retainedInverse = false;
                DateOnly? retainedOriginalEffectiveDate = null;
                if (context.Write.PostingCommand?.Intent == AccountingPostingIntentDto.Reversal
                    && (context.Write.SourceJournalEntryId ?? context.Write.PostingCommand.SourceJournalEntryId) is { } originalId)
                {
                    var originals = await journals.QueryAsync(new(LedgerBookId: context.Book.LedgerBookId, JournalEntryId: originalId), ct).ConfigureAwait(false);
                    if (originals.Count == 1 && originals[0].Entry.JournalEntryId == originalId
                        && (context.Write.SourceJournalEntryId is null || context.Write.PostingCommand.SourceJournalEntryId is null
                            || context.Write.SourceJournalEntryId == context.Write.PostingCommand.SourceJournalEntryId))
                    {
                        // The scoped query proves the original belongs to this same authoritative book.
                        // Reuse its date only after checking exact economics and agreement identities.
                        retainedInverse = IsExactIntercompanyInverse(context.Write.Entry, originals[0].Entry);
                        if (retainedInverse)
                        {
                            retainedOriginalEffectiveDate = originals[0].Entry.Metadata.EffectiveDate
                                ?? DateOnly.FromDateTime(originals[0].Entry.Timestamp.UtcDateTime);
                            context.Decisions.Add(new("IntercompanyAgreement", "HistoricalIntercompanyReversal", true,
                                "An exact retained source inverse uses the original agreement effective date.",
                                $"{originalId:D}:{retainedOriginalEffectiveDate:yyyy-MM-dd}"));
                        }
                    }
                    if (!retainedInverse) Check(context, "InvalidIntercompanyReversal", false, "A source-linked reversal must be the exact economic inverse of its immutable original journal.");
                }
                context.Decisions.Add(new("intercompany", "IntercompanyControlsEvaluated", true, "Intercompany account and agreement checks evaluated."));
                foreach (var line in context.Write.Entry.Lines)
                {
                    var dimensions = line.Dimensions;
                    var matches = accounts.Where(x => dimensions?.ChartAccountNodeId is { } node
                        ? x.ChartAccountNodeId == node
                        : x.AccountName == line.Account.Name || x.HistoricalNames.Contains(line.Account.Name, StringComparer.Ordinal)).ToArray();
                    var knownIntercompany = line.Account.Name is ConsolidationService.ReceivableAccount or ConsolidationService.PayableAccount
                        || historical.Any(x => x.AccountName == line.Account.Name || x.HistoricalNames.Contains(line.Account.Name, StringComparer.Ordinal))
                        || workspace?.ChartOfAccounts.Any(x => x.IntercompanyRole is not null &&
                            (x.AccountName == line.Account.Name || x.NodeId == dimensions?.ChartAccountNodeId)) == true;
                    if (matches.Length == 0 && dimensions?.AgreementId is null && !knownIntercompany) continue;
                    try
                    {
                        if (matches.Length != 1) throw new InvalidOperationException("The intercompany account binding is missing or ambiguous.");
                        var binding = matches[0];
                        var chart = workspace?.ChartOfAccounts.SingleOrDefault(x => x.NodeId == binding.ChartAccountNodeId);
                        if (binding.IsArchived || chart is null || chart.IsArchived || chart.IntercompanyRole != binding.Role
                            || chart.AccountType != binding.AccountType || line.Account.Symbol is not null || line.Account.FinancialAccountId is not null)
                            throw new InvalidOperationException("The intercompany account is archived, reclassified, scoped or unavailable.");
                        if (dimensions is null || !Guid.TryParse(dimensions.AgreementId, out var agreementId)
                            || !Guid.TryParse(dimensions.AgreementRevisionId, out var revisionId)
                            || string.IsNullOrWhiteSpace(dimensions.IntercompanyTransactionId)
                            || dimensions.AccountBindingRevisionId != binding.RevisionId.ToString("D")
                            || dimensions.ChartAccountNodeId != binding.ChartAccountNodeId)
                            throw new InvalidOperationException("Intercompany lines require exact agreement, mapping and transaction identities.");
                        if (!Guid.TryParse(dimensions.EntityId, out var entityId) || entityId != context.Book.FundStructureNodeId
                            || !Guid.TryParse(dimensions.CounterpartyId, out var counterpartyId) || entityId == counterpartyId)
                            throw new InvalidOperationException("Intercompany parties must match the authoritative book and a distinct counterparty.");
                        var agreement = await service.ResolveApprovedRevisionAsync(context.TenantId, agreementId, revisionId,
                            new DateTimeOffset((retainedOriginalEffectiveDate ?? date).ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero), ct).ConfigureAwait(false);
                        if (agreement.Kind == IntercompanyAgreementKindDto.Guarantee)
                            throw new InvalidOperationException("Guarantees have no supported posting treatment in this release.");
                        if (!((agreement.PartyOneEntityId == entityId && agreement.PartyTwoEntityId == counterpartyId)
                            || (agreement.PartyTwoEntityId == entityId && agreement.PartyOneEntityId == counterpartyId)))
                            throw new InvalidOperationException("Journal parties differ from the approved agreement.");
                        var mapping = agreement.PartyMappings.SingleOrDefault(x => x.EntityId == entityId
                            && x.LedgerBookId == context.Book.LedgerBookId)
                            ?? throw new InvalidOperationException("The agreement does not authorize this book.");
                        var expectedNode = binding.Role switch
                        {
                            IntercompanyAccountRoleDto.Receivable => mapping.ReceivableAccountNodeId,
                            IntercompanyAccountRoleDto.Payable => mapping.PayableAccountNodeId,
                            IntercompanyAccountRoleDto.Revenue => mapping.RevenueAccountNodeId,
                            IntercompanyAccountRoleDto.Expense => mapping.ExpenseAccountNodeId,
                            _ => null
                        };
                        if (expectedNode != binding.ChartAccountNodeId || binding.AccountType != line.Account.AccountType.ToString()
                            || !(binding.AccountName == line.Account.Name || binding.HistoricalNames.Contains(line.Account.Name, StringComparer.Ordinal)))
                            throw new InvalidOperationException("The posted account differs from its approved party mapping.");
                        if ((line.Currency?.TransactionCurrency ?? context.Book.BaseCurrency) != agreement.Currency)
                            throw new InvalidOperationException("Intercompany lines must retain original agreement-currency amounts.");
                        if (line.Currency is { } declared && declared.FunctionalCurrency != context.Book.BaseCurrency)
                            throw new InvalidOperationException("The line functional currency differs from the authoritative book currency.");
                        if (agreement.Currency != context.Book.BaseCurrency && !retainedInverse)
                        {
                            var original = line.Currency
                                ?? throw new InvalidOperationException("Original agreement-currency amounts are required for foreign-currency postings.");
                            var fx = fxAuthority is null ? throw new InvalidOperationException("Approved book FX authority is required for foreign-currency intercompany posting.")
                                : await fxAuthority.ResolveAsync(context.Book.LedgerBookId, date, ct).ConfigureAwait(false);
                            if (fx.Configuration.Policy.ReportingCurrency != context.Book.BaseCurrency)
                                throw new InvalidOperationException("The book FX policy must convert to its functional currency.");
                            var retainedFx = context.Write.PostingCommand?.IntercompanyFx;
                            if (retainedFx is null || retainedFx.ConfigurationHash != fx.Configuration.ContentHash
                                || retainedFx.RateSetId != fx.RateSet.RateSetId || retainedFx.RateSetRevision != fx.RateSet.Revision
                                || retainedFx.PolicyId != fx.Configuration.Policy.PolicyId || retainedFx.PolicyRevision != fx.Configuration.Policy.Revision)
                                throw new InvalidOperationException("The exact reviewed intercompany FX configuration or rate set changed; renew review before posting.");
                            var converted = ConsolidationFxTranslator.ConvertAmount(original.TransactionDebit + original.TransactionCredit,
                                original.TransactionCurrency, fx, date);
                            if (converted.Amount != line.Debit + line.Credit || converted.Rate != original.FxRateToFunctional)
                                throw new InvalidOperationException("Original amounts and functional posting differ from the approved transaction-date rate.");
                        }
                        context.Decisions.Add(new("IntercompanyAgreement", "AgreementVerified", true,
                            "Approved agreement and account revisions verified.", $"{agreement.RevisionId:D}:{binding.RevisionId:D}"));
                    }
                    catch (InvalidOperationException error)
                    {
                        Check(context, "IntercompanyAuthorityMissing", false, error.Message);
                    }
                }
            }
            return new Lease(leases);
        }
        catch { await new Lease(leases).DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task<LedgerBookControlPreflightLease> CheckAsync(LedgerBookRecord book, string? tenantId, CancellationToken ct = default)
    {
        var leases = new List<IAsyncDisposable>();
        try
        {
            leases.Add(await bindings.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false));
            leases.Add(await agreements.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false));
            if (legacyAttributions is not null)
                leases.Add(await legacyAttributions.AcquireAuthorityLeaseAsync(ct).ConfigureAwait(false));
            var owner = await owners.ResolveAsync(book.FundProfileId, ct).ConfigureAwait(false);
            if (owner is null || !owner.IsHeldBy(tenantId))
                return new([new("intercompany", "BookOwnerUnresolved", false, "An authoritative tenant and company owner are required.")], new Lease(leases));
            var history = await bindings.HistoryAsync(book.LedgerBookId, ct).ConfigureAwait(false);
            var workspace = await configuration.GetAsync(book.FundProfileId, book.LedgerBookId, ct, tenantId, owner.CompanyId).ConfigureAwait(false);
            var decisions = new List<LedgerControlDecisionDto>();
            foreach (var node in workspace?.ChartOfAccounts.Where(x => !x.IsArchived && (x.IntercompanyRole is not null || x.AccountName is ConsolidationService.ReceivableAccount or ConsolidationService.PayableAccount)) ?? [])
                decisions.Add(new("intercompany", "CurrentRoleBinding", history.Any(x => x.ChartAccountNodeId == node.NodeId
                    && x.Role == node.IntercompanyRole && !x.IsArchived), $"Account {node.Path} requires a retained role binding."));
            var facts = legacyAttributions is null ? [] : await legacyAttributions.ListAsync(book.LedgerBookId, ct).ConfigureAwait(false);
            var population = await journals.QueryAsync(new(LedgerBookId: book.LedgerBookId), ct).ConfigureAwait(false);
            var known = population.SelectMany(record => record.Entry.Lines.Select(line => (record, line)))
                .Where(item => item.line.Account.Name is ConsolidationService.ReceivableAccount or ConsolidationService.PayableAccount
                    || history.Any(x => x.AccountName == item.line.Account.Name || x.HistoricalNames.Contains(item.line.Account.Name, StringComparer.Ordinal))
                    || workspace?.ChartOfAccounts.Any(x => x.IntercompanyRole is not null && x.AccountName == item.line.Account.Name) == true)
                .GroupBy(item => (item.line.Account.Name, item.line.Dimensions?.EntityId, item.line.Dimensions?.CounterpartyId))
                .Where(group => group.Sum(item => item.line.Debit - item.line.Credit) != 0m).SelectMany(group => group);
            foreach (var (record, line) in known)
            {
                var fact = facts.SingleOrDefault(x => x.JournalEntryId == record.Entry.JournalEntryId && x.LineId == line.EntryId);
                var agreementId = line.Dimensions?.AgreementId ?? fact?.AgreementId.ToString("D");
                var revisionId = line.Dimensions?.AgreementRevisionId ?? fact?.AgreementRevisionId.ToString("D");
                var valid = Guid.TryParse(agreementId, out var id) && Guid.TryParse(revisionId, out var revision);
                if (valid && fact is not null)
                    valid = fact.SourceLineDigest == LegacyIntercompanyAttributionRules.SourceDigest(book, record, line);
                if (valid)
                {
                    try
                    {
                        var date = record.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime);
                        await service.ResolveApprovedRevisionAsync(tenantId!, Guid.Parse(agreementId!), Guid.Parse(revisionId!),
                            new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero), ct).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException) { valid = false; }
                }
                decisions.Add(new("intercompany", "LegacyBalanceAttribution", valid,
                    $"Outstanding source journal {record.Entry.JournalEntryId:D}, line {line.EntryId:D} requires exact approved agreement attribution."));
            }
            if (decisions.Count == 0) decisions.Add(new("intercompany", "NoIntercompanyBalances", true,
                "No unresolved intercompany balances or account-role mappings were found; new postings remain checked."));
            return new(decisions, new Lease(leases));
        }
        catch { await new Lease(leases).DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static bool IsExactIntercompanyInverse(JournalEntry inverse, JournalEntry original)
        => IntercompanyJournalCorrectionEvidence.IsExactInverse(inverse, original);

    private static void Check(LedgerPostingAuthorityContext context, string code, bool allowed, string message)
    {
        context.Decisions.Add(new("IntercompanyAgreement", code, allowed, message));
        if (!allowed && context.Controls.Mode == LedgerBookControlModeDto.Enforced)
            throw new LedgerValidationException(message);
    }
    private sealed class Lease(IReadOnlyList<IAsyncDisposable> leases) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
        }
    }
}
