using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Consolidation;

public sealed partial class ConsolidationService
{
    public const string RevenueExpenseRuleId = "consolidation.revenue-expense";

    private async Task<ConsolidationCalculation> CalculateGovernedAsync(ConsolidationRequestDto request,
        LedgerBookRecord book, CancellationToken ct, IReadOnlySet<string>? formerMembers = null)
    {
        var version = book.AccountingPolicyVersion;
        var translates = version is "w10-v3" or "w10-v4";
        var income = version == "w10-v4";
        var bindingStore = accountBindings ?? throw new InvalidOperationException("Governed account mapping authority is unavailable.");
        var perimeter = await perimeterResolver.ResolveAsync(request.OrganizationId, request.OwnershipRootId,
            new DateTimeOffset(request.AsOf.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero), ct,
            allowMultipleEntities: true, allowTranslation: translates).ConfigureAwait(false);
        if (book.FundStructureNodeId != request.OwnershipRootId || book.FundStructureNodeKind != FundStructureNodeKindDto.Fund ||
            book.AccountingBasis != AccountingBasisKindDto.Primary ||
            (!translates && !string.Equals(book.BaseCurrency, perimeter.Currency, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A governed consolidation book must belong to the Fund root and use Primary basis and the reporting currency.");
        perimeter = perimeter with { Currency = book.BaseCurrency };
        var period = await journals.GetPeriodAsync(request.PeriodId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The elimination period is unavailable.");
        if (period.LedgerBookId != book.LedgerBookId || request.AsOf < period.StartDate || request.AsOf > period.EndDate)
            throw new InvalidOperationException("The consolidation date must fall within the elimination book's period.");
        var policy = await policies.ResolvePolicyAsync(new AccountingPolicyQuery(AccountingBasisKindDto.Primary,
            request.AsOf, book.AccountingPolicyId, book.FundProfileId, book.FundStructureNodeId,
            PolicyVersion: version), ct).ConfigureAwait(false);
        if (policy.Version != version)
            throw new InvalidOperationException("The resolved policy version differs from the book policy.");
        _ = RequireGovernedRule(policy, RuleId, version);
        if (income)
            RequireGovernedRule(policy, RevenueExpenseRuleId, version);
        var ids = perimeter.Entities.Select(x => x.EntityId.ToString("D")).Order(StringComparer.Ordinal).ToArray();
        var scope = "consolidation:" + Hash(new
        {
            request.OrganizationId,
            request.OwnershipRootId,
            request.EliminationBookId,
            request.PeriodId,
            request.AsOf,
            Entities = ids,
            RuleVersion = version
        });
        var mappings = new List<IntercompanyAccountBindingDto>();
        var overlayBindings = await bindingStore.ListAsync(book.LedgerBookId, request.AsOf, ct).ConfigureAwait(false);
        mappings.AddRange(overlayBindings);
        IntercompanyAccountBindingDto Output(IntercompanyAccountRoleDto role)
        {
            var candidates = overlayBindings.Where(x => !x.IsArchived && x.Role == role).ToArray();
            if (candidates.Length != 1)
                throw new InvalidOperationException($"The elimination book requires one unambiguous {role} account binding.");
            return candidates[0];
        }
        _ = Output(IntercompanyAccountRoleDto.Receivable);
        _ = Output(IntercompanyAccountRoleDto.Payable);
        if (income)
        { _ = Output(IntercompanyAccountRoleDto.Revenue); _ = Output(IntercompanyAccountRoleDto.Expense); }
        if (translates)
            _ = Output(IntercompanyAccountRoleDto.TranslationReserve);
        var sources = new List<ConsolidationSourceDto>();
        var records = new List<LedgerJournalEntryRecord>();
        var versions = new List<ConsolidationBookVersionDto>();
        var blockers = new List<string>();
        var sourceRoles = new Dictionary<Guid, IntercompanyAccountRoleDto>();
        var attributions = new List<LegacyIntercompanyAttributionDto>();
        foreach (var entity in perimeter.Entities)
        {
            var books = (await journals.ListLedgerBooksAsync(book.FundProfileId, entity.EntityId,
                FundStructureNodeKindDto.Entity, ct).ConfigureAwait(false)).Where(x => x.AccountingBasis == AccountingBasisKindDto.Primary).ToArray();
            if (books.Length != 1)
                throw new InvalidOperationException("Each perimeter entity must have exactly one Primary source book.");
            var entityBook = books[0];
            if (!string.Equals(entityBook.BaseCurrency, entity.BaseCurrency, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The source book functional currency disagrees with its authoritative entity.");
            if (!translates && !string.Equals(entityBook.BaseCurrency, book.BaseCurrency, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("w10-v2 requires one shared functional currency.");
            // Retired/expired source mappings still classify immutable outstanding balances.
            // Only new posting authority and output selection require a currently active mapping.
            var bindingHistory = await bindingStore.HistoryAsync(entityBook.LedgerBookId, ct).ConfigureAwait(false);
            var bindings = bindingHistory.Where(x => x.EffectiveFrom <= request.AsOf).GroupBy(x => x.BindingId)
                .Select(x => x.MaxBy(y => y.RevisionNumber)!).ToArray();
            mappings.AddRange(bindings);
            var legacy = legacyAttributions is null ? [] : await legacyAttributions.ListAsync(entityBook.LedgerBookId, ct).ConfigureAwait(false);
            attributions.AddRange(legacy);
            var population = await ReadBookAsync(entityBook.LedgerBookId, request.AsOf, ct).ConfigureAwait(false);
            versions.Add(Version(entityBook.LedgerBookId, population));
            records.AddRange(population);
            foreach (var record in population)
            {
                if (record.Entry.Metadata.IdempotencyKey?.StartsWith("consolidation:", StringComparison.Ordinal) == true)
                    throw new InvalidOperationException("Source books cannot contain consolidation overlays.");
                foreach (var line in record.Entry.Lines)
                {
                    if (!Guid.TryParse(line.Dimensions?.EntityId, out var postingEntity) || postingEntity != entity.EntityId)
                        throw new InvalidOperationException("Every source line must identify its authoritative posting entity.");
                    if (line.Currency is { } c && !string.Equals(c.FunctionalCurrency, entityBook.BaseCurrency, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Source currency evidence disagrees with the book functional currency.");
                    if (!translates && line.Currency is { } v2Currency &&
                        (!v2Currency.IsFunctionalCurrency || v2Currency.FxRateToFunctional != 1m))
                        throw new InvalidOperationException("w10-v2 does not translate cross-currency source legs.");
                    var attribution = legacy.SingleOrDefault(x => x.JournalEntryId == record.Entry.JournalEntryId && x.LineId == line.EntryId);
                    if (attribution is not null && (line.Dimensions?.AccountingArrangementId is not null ||
                        !Meridian.Contracts.Integrity.Sha256Digest.FixedEquals(attribution.SourceLineDigest,
                            LegacyIntercompanyAttributionRules.SourceDigest(entityBook, record, line))))
                        throw new InvalidOperationException("Legacy attribution differs from its exact immutable journal source.");
                    if (attribution is not null && line.Dimensions?.IntercompanyAgreementId is { } retainedLegalId
                        && (retainedLegalId != attribution.IntercompanyAgreementId
                            || line.Dimensions.IntercompanyAgreementVersion != attribution.IntercompanyAgreementVersion))
                        throw new InvalidOperationException("Legacy attribution cannot replace retained canonical legal source provenance.");
                    var binding = ResolveBinding(bindings, line.Account.Name, attribution?.ChartAccountNodeId ?? line.Dimensions?.ChartAccountNodeId);
                    if (binding is null && (line.Dimensions?.AccountBindingRevisionId is not null ||
                        line.Account.Name is ReceivableAccount or PayableAccount))
                        blockers.Add($"Intercompany source line {line.EntryId:D} has no retained authoritative account mapping.");
                    if (binding is not null && (!Enum.TryParse<LedgerAccountType>(binding.AccountType, true, out var type) || type != line.Account.AccountType))
                        throw new InvalidOperationException("Source account binding type does not match its immutable journal line.");
                    var counterparty = Guid.TryParse(line.Dimensions?.CounterpartyId, out var cp) ? cp.ToString("D") : line.Dimensions?.CounterpartyId;
                    var supportedRole = binding is not null && binding.Role is not IntercompanyAccountRoleDto.TranslationReserve &&
                        (income || binding.Role is IntercompanyAccountRoleDto.Receivable or IntercompanyAccountRoleDto.Payable);
                    var formerExternal = supportedRole && counterparty is not null && !ids.Contains(counterparty) &&
                        formerMembers?.Contains(counterparty) == true;
                    var eligible = supportedRole && !formerExternal;
                    if (eligible && (attribution?.IntercompanyAgreementId ?? line.Dimensions?.IntercompanyAgreementId) is not null
                        && attribution is null && line.Dimensions?.AccountingArrangementId is null)
                    {
                        blockers.Add($"Intercompany source line {line.EntryId:D} retains a canonical legal agreement but requires reviewed accounting arrangement sidecar attribution before elimination.");
                        eligible = false;
                    }
                    if (eligible)
                    {
                        sourceRoles.Add(line.EntryId, binding!.Role);
                        if (counterparty is null || !ids.Contains(counterparty) || counterparty == postingEntity.ToString("D"))
                            blockers.Add($"Intercompany line {line.EntryId:D} has a missing, self, or outside-perimeter counterparty.");
                        if (binding!.Role is IntercompanyAccountRoleDto.Revenue or IntercompanyAccountRoleDto.Expense &&
                            string.IsNullOrWhiteSpace(attribution?.IntercompanyTransactionId ?? line.Dimensions?.IntercompanyTransactionId))
                            blockers.Add($"Income elimination line {line.EntryId:D} requires a shared intercompany transaction identity.");
                    }
                    var canonicalId = attribution?.IntercompanyAgreementId ?? line.Dimensions?.IntercompanyAgreementId;
                    var canonicalVersion = attribution?.IntercompanyAgreementVersion ?? line.Dimensions?.IntercompanyAgreementVersion;
                    var canonicalHash = attribution?.IntercompanyAgreementPayloadHash;
                    var arrangementId = attribution?.AccountingArrangementId.ToString("D") ?? line.Dimensions?.AccountingArrangementId;
                    var arrangementRevision = attribution?.AccountingArrangementRevisionId.ToString("D") ?? line.Dimensions?.AccountingArrangementRevisionId;
                    if (arrangementId is not null)
                    {
                        if (arrangements is null || !Guid.TryParse(arrangementId, out var profileId)
                            || !Guid.TryParse(arrangementRevision, out var profileRevision)
                            || binding is null)
                            throw new InvalidOperationException("Source accounting arrangement authority is unavailable or incomplete.");
                        var authorityDate = EffectiveDate(record);
                        var exactInverse = false;
                        if (record.SourceJournalEntryId is { } originalId)
                        {
                            var original = population.SingleOrDefault(item => item.Entry.JournalEntryId == originalId);
                            if (original is not null && IntercompanyJournalCorrectionEvidence.IsExactInverse(record.Entry, original.Entry))
                            { authorityDate = EffectiveDate(original); exactInverse = true; }
                        }
                        var resolved = exactInverse
                            ? await arrangements.GetApprovedRevisionAsync(binding.TenantId, profileId, profileRevision, ct).ConfigureAwait(false)
                            : await arrangements.ResolveApprovedRevisionAsync(binding.TenantId, profileId, profileRevision,
                                new DateTimeOffset(authorityDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero), ct).ConfigureAwait(false);
                        if (canonicalId != resolved.CanonicalAgreementId || canonicalVersion != resolved.CanonicalAgreementVersion
                            || (canonicalHash is not null && canonicalHash != resolved.CanonicalPayloadHash))
                            throw new InvalidOperationException("Source canonical legal pins differ from the exact approved accounting arrangement.");
                        canonicalHash = resolved.CanonicalPayloadHash;
                    }
                    sources.Add(new ConsolidationSourceDto(entityBook.LedgerBookId, record.Entry.JournalEntryId, line.EntryId,
                        postingEntity.ToString("D"), counterparty, supportedRole ? Output(binding!.Role).AccountPath : line.Account.Name,
                        line.Debit, line.Credit, EffectiveDate(record), $"journal:{record.Entry.JournalEntryId:D}/line:{line.EntryId:D}")
                    {
                        AccountType = line.Account.AccountType.ToString(),
                        OriginalAccountPath = line.Account.Name,
                        CounterpartyDisposition = formerExternal ? "FormerPerimeterExternal" : null,
                        Currency = entityBook.BaseCurrency,
                        ChartAccountNodeId = binding?.ChartAccountNodeId,
                        AccountBindingRevisionId = attribution?.AccountBindingRevisionId.ToString("D") ?? binding?.RevisionId.ToString("D"),
                        AccountingArrangementId = arrangementId,
                        AccountingArrangementRevisionId = arrangementRevision,
                        IntercompanyAgreementId = canonicalId,
                        IntercompanyAgreementVersion = canonicalVersion,
                        IntercompanyAgreementPayloadHash = canonicalHash,
                        IntercompanyTransactionId = attribution?.IntercompanyTransactionId ?? line.Dimensions?.IntercompanyTransactionId,
                        TransactionCurrency = line.Currency?.TransactionCurrency ?? entityBook.BaseCurrency,
                        TransactionDebit = line.Currency?.TransactionDebit ?? line.Debit,
                        TransactionCredit = line.Currency?.TransactionCredit ?? line.Credit
                    });
                }
            }
        }
        var overlay = await journals.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: book.LedgerBookId), ct).ConfigureAwait(false);
        if (overlay.Any(x => EffectiveDate(x) > request.AsOf))
            throw new InvalidOperationException("Backdated consolidation before a posted elimination is unsupported.");
        foreach (var record in overlay)
        {
            var prior = ReadEvidence(record.Entry.Metadata.Tags?.GetValueOrDefault(EvidenceTag));
            if (prior is null || prior.Request.OrganizationId != request.OrganizationId || prior.Request.OwnershipRootId != request.OwnershipRootId ||
                !string.Equals(record.Entry.Metadata.Tags?.GetValueOrDefault(DigestTag), HashEvidenceJson(record.Entry.Metadata.Tags?.GetValueOrDefault(EvidenceTag)), StringComparison.Ordinal))
                throw new InvalidOperationException("The dedicated elimination book contains invalid consolidation provenance.");
            if (prior.ExpectedLines.Any(x => x.EntityId is null || !ids.Contains(x.EntityId) || x.Dimensions?.CounterpartyId is null ||
                !ids.Contains(x.Dimensions.CounterpartyId)))
                throw new InvalidOperationException("A perimeter transition requires reviewed carry-forward adjustments; prior members cannot be silently removed.");
            foreach (var line in record.Entry.Lines)
                _ = ResolveBinding(overlayBindings, line.Account.Name, line.Dimensions?.ChartAccountNodeId)
                    ?? throw new InvalidOperationException("Historical posted overlays require approved stable account bindings before migration.");
        }
        versions.Add(Version(book.LedgerBookId, overlay));
        string? translationFingerprint = null;
        string? translationEvidence = null;
        IReadOnlyDictionary<string, decimal>? reserve = null;
        IReadOnlyDictionary<string, decimal>? translationDifferences = null;
        IReadOnlyDictionary<string, decimal>? roundingDifferences = null;
        if (translates)
        {
            var translated = await TranslateGovernedAsync(request, book, sources, ct).ConfigureAwait(false);
            sources = translated.Sources.ToList();
            translationFingerprint = translated.Fingerprint;
            translationEvidence = translated.Evidence;
            reserve = translated.Reserve;
            translationDifferences = translated.TranslationDifferences;
            roundingDifferences = translated.RoundingDifferences;
            blockers.AddRange(translated.Blockers);
        }
        if (sources.Where(x => sourceRoles.ContainsKey(x.LineId)).GroupBy(x => new { x.AccountingArrangementId, x.AccountingArrangementRevisionId })
            .Any(group => group.Select(x => x.TransactionCurrency).Distinct(StringComparer.Ordinal).Count() > 1))
            throw new InvalidOperationException("Each agreement requires one unambiguous original matching currency; legacy balances need reviewed agreement attribution before cross-currency adoption.");
        var (matches, proposed) = MatchGoverned(sources, sourceRoles, overlay, overlayBindings, book, request, scope, income, Output);
        var orderedMappings = mappings.OrderBy(x => x.LedgerBookId).ThenBy(x => x.ChartAccountNodeId, StringComparer.Ordinal).ToArray();
        var orderedSources = sources.OrderBy(x => x.JournalEntryId).ThenBy(x => x.LineId).ToArray();
        var orderedVersions = versions.OrderBy(x => x.LedgerBookId).ToArray();
        var perimeterFingerprint = Hash(perimeter);
        var fingerprint = Hash(new
        {
            Versions = orderedVersions,
            Perimeter = perimeterFingerprint,
            Policy = policy,
            Sources = orderedSources,
            AccountBindings = orderedMappings,
            Translation = translationFingerprint,
            LegacyAttributions = attributions.OrderBy(x => x.AttributionId).ToArray()
        });
        var evidence = new ConsolidationEvidenceDto(request, scope, fingerprint, orderedVersions, perimeterFingerprint,
            version, overlay.Select(x => x.Entry.JournalEntryId).Order().ToArray(), proposed, book.BaseCurrency, ids,
            perimeter.OwnershipEvidence, orderedSources)
        {
            AccountBindings = orderedMappings,
            TranslationFingerprint = translationFingerprint,
            TranslationEvidenceJson = translationEvidence,
            LegacyAttributions = attributions.OrderBy(x => x.AttributionId).ToArray(),
            RulePackReference = new(policy.RulePack!.RulePackId, policy.RulePack.RulePackVersion, RuleId, version),
            ParticipatingRules = income ? [new(RuleId, version), new(RevenueExpenseRuleId, version)] : [new(RuleId, version)]
        };
        return new ConsolidationCalculation(request, book, period, perimeter, policy, sources, records, overlay,
            matches, proposed, evidence, blockers.Distinct().ToArray())
        {
            GovernedSources = sources,
            TranslationReserve = reserve,
            TranslationDifferences = translationDifferences,
            RoundingDifferences = roundingDifferences
        };
    }

    private static AccountingPolicyRuleDto RequireGovernedRule(AccountingPolicyDto policy, string ruleId, string version)
    {
        var rule = policy.RulePack?.Rules.SingleOrDefault(x => x.RuleId == ruleId);
        if (rule is null || rule.TreatmentKind != AccountingTreatmentKindDto.ConsolidationElimination || rule.RuleVersion != version ||
            !rule.RequiresEvidence || !rule.RequiresApproval || rule.AllowsAutoPosting)
            throw new InvalidOperationException($"The book policy must explicitly enable reviewed {ruleId} {version}.");
        return rule;
    }

    private static IntercompanyAccountBindingDto? ResolveBinding(IReadOnlyList<IntercompanyAccountBindingDto> bindings,
        string name, string? nodeId)
    {
        var candidates = bindings.Where(x => (nodeId is null || x.ChartAccountNodeId == nodeId) &&
            (x.AccountName == name || x.AccountPath == name || x.HistoricalNames.Contains(name, StringComparer.Ordinal))).ToArray();
        if (candidates.Length > 1)
            throw new InvalidOperationException("Historical account identity resolves to multiple chart nodes.");
        return candidates.SingleOrDefault();
    }

    private static string? HashEvidenceJson(string? json) => json is null ? null : Meridian.Contracts.Integrity.Sha256Digest.ComputeUtf8(json);

    private static string DraftAccountName(ConsolidationCalculation calculation, string path)
        => calculation.Evidence.AccountBindings?.Single(x => x.LedgerBookId == calculation.Book.LedgerBookId && !x.IsArchived && x.AccountPath == path).AccountName ?? path;

    private static LedgerAccountType DraftAccountType(ConsolidationCalculation calculation, string path)
        => calculation.Evidence.AccountBindings is { } bindings
            ? Enum.Parse<LedgerAccountType>(bindings.Single(x => x.LedgerBookId == calculation.Book.LedgerBookId && !x.IsArchived && x.AccountPath == path).AccountType, true)
            : path == ReceivableAccount ? LedgerAccountType.Asset : LedgerAccountType.Liability;
}
