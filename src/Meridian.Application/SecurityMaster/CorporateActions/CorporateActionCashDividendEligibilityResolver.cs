using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster.CorporateActions;

/// <summary>
/// Resolves the owner's bounded cash-dividend class from canonical whole-book holdings, not a
/// processing-case quantity. This is eligibility composition over existing source/lot/rule
/// authority, never a second accounting engine or an assertion of external completeness.
/// </summary>
public sealed class CorporateActionCashDividendEligibilityResolver(
    ILedgerSecurityEntitlementInventory? inventory,
    IInstrumentPositionProjectionStore? positions,
    ISecurityMasterStore? securities,
    ICorporateActionOperationsStore? operations,
    IAccountingConfigurationService? accountingConfiguration = null,
    ICorporateActionCashDividendReconciliationSupport? reconciliation = null,
    ISecurityCorporateActionsQueryService? corporateActions = null,
    ICorporateActionCashDividendAccountingStore? caseFacts = null,
    TimeProvider? timeProvider = null) : ICorporateActionCashDividendEligibilityResolver
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CorporateActionCashDividendEligibilityDto> ResolveAsync(
        CorporateActionProcessingCaseDto processingCase,
        CorporateActionCaseAccountingProjectionDto projection,
        AssetAccountingEventSpineDto spine,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(processingCase);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(spine);
        var lots = inventory ?? throw Missing("Complete canonical book-lot entitlement inventory is not configured.");
        var positionStore = positions ?? throw Missing("Canonical book-position inventory is not configured.");
        var securityStore = securities ?? throw Missing("Authoritative Security Master is not configured.");
        var sourceStore = operations ?? throw Missing("Authoritative corporate-action source storage is not configured.");
        var configuration = accountingConfiguration ?? throw Missing("Governed accounting configuration is not configured.");
        var reconciled = reconciliation ?? throw Missing("Complete retained external holdings reconciliation support is not configured.");
        var currentActions = corporateActions ?? throw Missing("Current canonical corporate-action authority is not configured.");
        var factsStore = caseFacts ?? throw Missing("Complete retained processing-case facts are not configured.");
        var source = processingCase.SourceSnapshot ?? throw Ineligible("The retained provider source snapshot is unavailable.");
        var action = source.ProposedAction;
        var provider = source.ProviderIdentity;
        CorporateActionCaseAccountingPolicy.EnsureProjectionMatchesCaseScope(projection, processingCase.Scope);
        Require(processingCase.SecurityId != Guid.Empty && action.SecurityId == processingCase.SecurityId
            && processingCase.CorporateActionId != Guid.Empty && projection.CaseId == processingCase.CaseId
            && spine.Scope.SecurityId == processingCase.SecurityId && spine.Scope.LedgerBookId == projection.LedgerBookId
            && spine.Scope.PeriodId == projection.PeriodId && spine.EventId == projection.AccountingEventId
            && spine.EventVersion == projection.AccountingEventVersion
            && Same(spine.Scope.TenantId, processingCase.Scope.TenantId)
            && Same(spine.Scope.CompanyId, processingCase.Scope.CompanyId), "Source, case, spine and exact accounting scope must agree.");
        Require(projection.IsCurrent && projection.HasAuthoritativeLotResolution && projection.LotSnapshotId != Guid.Empty
            && projection.LotSnapshotVersion > 0 && projection.PolicyDecisionId != Guid.Empty && projection.PolicyDecisionVersion > 0,
            "The retained current native projection requires authoritative lot and approved policy bindings.");
        Require(string.IsNullOrWhiteSpace(processingCase.BlockedReason) && processingCase.MethodologyProfileId is null
            && (source.DisplayMetadata?.DissentingSources.Count ?? 0) == 0
            && (source.DisplayMetadata?.DissentingFields?.Count ?? 0) == 0,
            "Blocked, custom-methodology or dissenting-source cases require the ordinary reviewed workflow.");
        Require(action.EventType == CorporateActionEventTypes.Dividend
            && action.SupersedesCorpActId is null && action.LifecycleState is null or CorporateActionLifecycleStates.Confirmed
            && action.DividendPerShare is > 0m && action.Currency == "USD"
            && action.PayloadSchemaVersion == 1 && !HasPayload(action.Payload) && action.SplitRatio is null && action.NewSecurityId is null
            && action.DistributionRatio is null && action.AcquirerSecurityId is null && action.ExchangeRatio is null
            && action.SubscriptionPricePerShare is null && action.RightsPerShare is null
            && action.RedemptionPricePercentOfPar is null,
            "Only confirmed regular mandatory USD cash dividends without special, tax, election, estimation or mixed-treatment payloads qualify.");
        Require(action.ExDate > DateOnly.MinValue && projection.EffectiveDate == action.ExDate
            && action.PayDate is { } payDate && payDate >= action.ExDate
            && action.RecordDate is { } recordDate && recordDate >= action.ExDate && recordDate <= payDate
            && recordDate < DateOnly.FromDateTime((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime),
            "Explicit coherent ex, record and payment dates and a completed record date are required; a future entitlement window is not complete.");
        Require(provider.ReleaseStatus == CorporateActionProviderReleaseStatusDto.AcceptanceEligible
            && !string.IsNullOrWhiteSpace(provider.ProviderId) && !string.IsNullOrWhiteSpace(provider.SourceEventId)
            && !string.IsNullOrWhiteSpace(provider.SourceEventVersion) && Sha256Digest.IsCanonical(provider.EvidenceHash)
            && CorporateActionEvidenceKinds.IsTrustedReference(provider.EvidenceReference),
            "A versioned acceptance-eligible provider identity and retained source hash are required.");
        Require(spine.Correction is null && spine.CorporateAction is null && spine.CapitalReturn is null && spine.FactorPaydown is null
            && spine.Direction == OpenLotDirection.Long && spine.DraftedLotMutation is null
            && spine.DraftedCandidate?.PostingKind == LedgerPostingKindDto.Originating
            && spine.DraftedCandidate.AdjustmentApproval is null && spine.DraftedCandidate.SourceJournalEntryId is null
            && spine.DraftedCandidate.AssetLotMutation is null,
            "Corrections, reversals, elections, short payments and lot-changing treatments are excluded.");

        var proposal = await sourceStore.GetSourceProposalAsync(processingCase.ProposalId, ct).ConfigureAwait(false);
        Require(proposal is not null && proposal.State == CorporateActionSourceProposalStates.Accepted
            && proposal.SupersedesProposalId is null && proposal.PayloadSchemaVersion == 1
            && proposal.AcceptedCorporateActionId == processingCase.CorporateActionId
            && proposal.SecurityId == processingCase.SecurityId && proposal.ProviderIdentity == provider
            && JsonSerializer.Serialize(proposal.ProposedAction, Json) == JsonSerializer.Serialize(action, Json),
            "The retained source proposal is missing, changed or not the accepted event bound to this case.");
        var currentSource = await currentActions.GetCorporateActionsAsync(processingCase.SecurityId, ct).ConfigureAwait(false);
        var canonicalActions = currentSource.Where(item => item.CorpActId == processingCase.CorporateActionId).ToArray();
        var relatedActions = currentSource.Where(item => item.SecurityId == processingCase.SecurityId && item.ExDate == action.ExDate
            && item.EventType is CorporateActionEventTypes.Dividend or CorporateActionEventTypes.SpecialDividend).ToArray();
        Require(canonicalActions.Length == 1
            && relatedActions.Length == 1 && relatedActions[0].CorpActId == processingCase.CorporateActionId
            && JsonSerializer.Serialize(canonicalActions[0], Json) == JsonSerializer.Serialize(action with { CorpActId = processingCase.CorporateActionId }, Json)
            && !currentSource.Any(item => item.SupersedesCorpActId == processingCase.CorporateActionId),
            "The accepted source has been cancelled, superseded, corrected or changed in current canonical corporate-action authority.");
        bool MatchesSource(RetainedEvidenceIdentityDto evidence) => RetainedEvidenceIdentityValidator.IsComplete(evidence)
            && evidence.SubjectType == AssetAccountingEvidenceSubjects.Event && evidence.EffectiveDate == action.ExDate
            && Same(evidence.SourceSystem, provider.ProviderId) && evidence.SourceReference == provider.SourceEventId
            && evidence.EvidenceUri == provider.EvidenceReference
            && Sha256Digest.FixedEquals(evidence.ContentHashSha256, provider.EvidenceHash);
        Require(spine.RetainedEvidence.Any(MatchesSource) && spine.DraftedCandidate!.RetainedEvidence.Any(MatchesSource),
            "The native candidate must retain reviewed event evidence for this exact provider source and digest.");
        var conflicts = await sourceStore.ListConflictsAsync(processingCase.CaseId, processingCase.Scope.TenantId,
            processingCase.Scope.CompanyId, null, 1, ct).ConfigureAwait(false);
        Require(conflicts.Count == 0, "Disputed source history requires the ordinary reviewed workflow.");
        CorporateActionCashDividendCaseFactsDto facts;
        try
        {
            facts = await factsStore.GetCashDividendCaseFactsAsync(processingCase.CaseId, processingCase.Scope.TenantId,
                processingCase.Scope.CompanyId, processingCase.Version, ct).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            throw Missing("Complete retained processing-case facts are unavailable.");
        }
        ValidateCaseFacts(facts, processingCase);
        var security = await securityStore.GetProjectionAsync(processingCase.SecurityId, ct).ConfigureAwait(false);
        Require(security is not null && security.Version == spine.Scope.ExpectedSecurityVersion
            && security.Currency == "USD" && security.AssetClass == "Equity"
            && security.Status == SecurityStatusDto.Active,
            "The cash-dividend class requires the exact current active USD equity Security Master version.");
        var canonicalSymbol = SecurityMasterLedgerSymbol.ResolveUniqueActiveTicker(security!.Identifiers ?? [],
            spine.DraftedCandidate!.AccountingTimestamp);
        Require(canonicalSymbol is not null && string.Equals(canonicalSymbol, spine.DraftedCandidate.InstrumentSymbol, StringComparison.Ordinal),
            "The native dividend candidate must retain the unique active canonical ticker from its exact current Security Master at the accounting timestamp.");

        var cutoff = action.ExDate.AddDays(-1);
        var recordCutoff = action.RecordDate!.Value;
        var before = await lots.ReadSecurityEntitlementInventoryAsync(processingCase.Scope.TenantId,
            processingCase.Scope.CompanyId, projection.LedgerBookId, processingCase.SecurityId, cutoff, ct).ConfigureAwait(false);
        ValidateInventory(before, processingCase, projection, cutoff, canonicalSymbol!);
        var throughRecord = await lots.ReadSecurityEntitlementInventoryAsync(processingCase.Scope.TenantId,
            processingCase.Scope.CompanyId, projection.LedgerBookId, processingCase.SecurityId, recordCutoff, ct).ConfigureAwait(false);
        ValidateInventory(throughRecord, processingCase, projection, recordCutoff, canonicalSymbol!);
        Require(SameQuantities(before.HeldLots, throughRecord.HeldLots),
            "Holdings changed between the pre-ex-date cutoff and record/ex date; settlement and due-bill entitlement need specialist review.");
        Require(before.HeldLots.Count > 0, "The complete book has no qualifying long-held entitlement.");
        var positionSnapshot = await positionStore.GetAsOfAsync(processingCase.SecurityId, projection.LedgerBookId, cutoff, ct)
            .ConfigureAwait(false);
        ValidatePositions(positionSnapshot, before, spine);

        var perShare = action.DividendPerShare!.Value;
        var groups = before.HeldLots.GroupBy(lot => new { lot.BookPositionId, lot.Account.FinancialAccountId })
            .Select(group => new
            {
                group.Key.BookPositionId, group.Key.FinancialAccountId,
                Units = group.Sum(lot => lot.OpenQuantity),
                ExactGross = checked(perShare * group.Sum(lot => lot.OpenQuantity)),
                RoundedGross = LedgerCurrencyRounding.RoundCurrency(checked(perShare * group.Sum(lot => lot.OpenQuantity)), "USD")
            }).ToArray();
        var expectedAccounts = groups.GroupBy(group => group.FinancialAccountId!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CashDividendReconciliationAccountDto(group.Key, group.Sum(item => item.Units)))
            .OrderBy(account => account.FinancialAccountId, StringComparer.OrdinalIgnoreCase).ToArray();
        var reconciliationEvidence = await reconciled.GetSupportAsync(processingCase.Scope, processingCase.SecurityId,
            cutoff, expectedAccounts, ct).ConfigureAwait(false);
        Require(reconciliationEvidence is not null && Sha256Digest.IsCanonical(reconciliationEvidence.EvidenceHash)
            && !string.IsNullOrWhiteSpace(reconciliationEvidence.EvidenceJson)
            && Sha256Digest.FixedEquals(reconciliationEvidence.EvidenceHash, Sha256Digest.ComputeUtf8(reconciliationEvidence.EvidenceJson)),
            "Complete retained external reconciliation support is missing or has changed.");
        var rawWhole = checked(perShare * before.HeldLots.Sum(lot => Math.Abs(lot.OpenQuantity)));
        // Materiality retains fractional cents and includes each native rounded account part;
        // neither rounding nor signed-account netting can make an event qualify.
        var wholeGross = Math.Max(rawWhole, groups.Sum(group => Math.Abs(group.RoundedGross)));
        Require(wholeGross > 0m && wholeGross <= CorporateActionCashDividendAttestationPolicy.RelatedEventGrossLimitUsd,
            "The complete related event exceeds the USD 10,000 gross limit; accounts, lots and signed amounts cannot split or net that limit.");
        var account = processingCase.Scope.FinancialAccountId ?? spine.Scope.Dimensions?.AccountId;
        var part = groups.Where(group => group.BookPositionId == spine.Scope.BookPositionId
            && (account is null || Same(group.FinancialAccountId, account))).ToArray();
        Require(part.Length == 1, "The exact case must resolve one canonical held account/position entitlement.");
        var gross = part[0].RoundedGross;
        ValidateCandidate(projection, spine, gross);
        var workspace = await configuration.GetWorkspaceAsync(projection.FundProfileId, projection.LedgerBookId, ct,
            processingCase.Scope.TenantId, processingCase.Scope.CompanyId).ConfigureAwait(false);
        var ruleBinding = ValidateGovernedRule(workspace, processingCase, projection, spine);
        var nativeLines = spine.DraftedCandidateResult!.GeneratedPostingLines;
        var accountRoles = new CashDividendAccountRoleBindingDto(projection.LedgerBookId,
            nativeLines.Single(line => line.Side == AccountingTemplateLineSideDto.Debit).AccountPath,
            nativeLines.Single(line => line.Side == AccountingTemplateLineSideDto.Credit).AccountPath,
            projection.RulePackId, projection.RulePackVersion, projection.SelectedRuleId!, projection.SelectedRuleVersion!,
            DividendReceivableAccountName: workspace.ChartOfAccounts.Single(node => Same(node.Path,
                nativeLines.Single(line => line.Side == AccountingTemplateLineSideDto.Debit).AccountPath)).AccountName,
            DividendIncomeAccountName: workspace.ChartOfAccounts.Single(node => Same(node.Path,
                nativeLines.Single(line => line.Side == AccountingTemplateLineSideDto.Credit).AccountPath)).AccountName);
        var relatedKey = CorporateActionCashDividendAttestationValidation.RelatedEventKey(processingCase.SecurityId, action.ExDate);
        using var beforeJson = JsonDocument.Parse(before.SnapshotJson);
        using var recordJson = JsonDocument.Parse(throughRecord.SnapshotJson);
        var snapshot = JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            PolicyVersion = CorporateActionCashDividendAttestationPolicy.Version,
            Class = "regular-mandatory-usd-cash-dividend-long-holder",
            RelatedEventKey = relatedKey,
            Source = source,
            CurrentCanonicalSource = canonicalActions[0],
            CurrentRelatedActions = relatedActions,
            processingCase.ProposalId, processingCase.CorporateActionId,
            ProcessingCaseFactsHash = facts.SnapshotHash, ProcessingCaseFactsJson = facts.SnapshotJson,
            Scope = processingCase.Scope,
            projection.CaseId, projection.ProjectionId, projection.AccountingEventId,
            projection.AccountingEventVersion, projection.ProjectionInputHash, projection.PostingIntentHash,
            projection.DraftedCandidateFingerprint, projection.PolicyDecisionId, projection.PolicyDecisionVersion,
            projection.RulePackId, projection.RulePackVersion, projection.SelectedRuleId, projection.SelectedRuleVersion,
            projection.LotSnapshotId, projection.LotSnapshotVersion,
            GovernedRuleBinding = ruleBinding,
            AccountRoleBinding = accountRoles,
            CanonicalInstrumentSymbol = canonicalSymbol,
            Security = security,
            EntitlementCutoff = cutoff, RecordOrExCutoff = recordCutoff,
            EntitlementRule = "canonical lots held before ex date; unchanged quantities through record/ex date; no settlement/due-bill inference",
            Completeness = "Complete retained canonical ledger-book population with current retained external reconciliation support for every held account",
            ExternalReconciliationEvidenceHash = reconciliationEvidence!.EvidenceHash,
            ExternalReconciliationEvidenceJson = reconciliationEvidence.EvidenceJson,
            Before = beforeJson.RootElement,
            ThroughRecord = recordJson.RootElement,
            Positions = positionSnapshot,
            PerAccountEntitlements = groups,
            DividendPerShare = perShare,
            ExactWholeGrossUsd = rawWhole,
            WholeEventGrossUsd = wholeGross,
            GrossImpactUsd = gross,
            GrossRule = "max(exact absolute whole-book entitlement,sum of absolute native-currency rounded account entitlements); no netting",
            Candidate = spine.DraftedCandidate,
            CandidateResult = spine.DraftedCandidateResult,
            ProjectedEffect = spine.ProjectedEffect
        }, Json);
        return new(processingCase.SecurityId, action.ExDate, relatedKey, provider.ProviderId, provider.SourceEventId,
            provider.SourceEventVersion, provider.EvidenceHash!, perShare, gross, wholeGross,
            new DateOnly(action.ExDate.Year, action.ExDate.Month, 1), Sha256Digest.ComputeUtf8(snapshot), snapshot, accountRoles);
    }

    private static void ValidateInventory(LedgerSecurityEntitlementInventorySnapshot value,
        CorporateActionProcessingCaseDto processingCase, CorporateActionCaseAccountingProjectionDto projection, DateOnly date,
        string canonicalSymbol)
    {
        Require(Same(value.TenantId, processingCase.Scope.TenantId) && Same(value.CompanyId, processingCase.Scope.CompanyId)
            && value.Book.LedgerBookId == projection.LedgerBookId && value.SecurityId == processingCase.SecurityId
            && value.AsOfDate == date && value.Book.BaseCurrency == "USD" && value.Book.FundProfileId == projection.FundProfileId
            && value.Book.AccountingBasis.ToString() == projection.AccountingBasis
            && value.InspectedBookLotCount >= value.HeldLots.Count && Sha256Digest.IsCanonical(value.SnapshotHash)
            && Sha256Digest.FixedEquals(value.SnapshotHash, Sha256Digest.ComputeUtf8(value.SnapshotJson)),
            "The canonical inventory scope, date, currency, policy or retained snapshot is incomplete or inconsistent.");
        try
        {
            using var json = JsonDocument.Parse(value.SnapshotJson);
            var root = json.RootElement;
            Require(root.GetProperty("schemaVersion").GetInt32() == 1
                && root.GetProperty("method").GetString() == "complete-book-journal-effective-lot-inventory-v1"
                && root.GetProperty("complete").GetBoolean()
                && Same(root.GetProperty("tenantId").GetString(), value.TenantId)
                && Same(root.GetProperty("companyId").GetString(), value.CompanyId)
                && root.GetProperty("securityId").GetGuid() == value.SecurityId
                && root.GetProperty("asOfDate").GetString() == value.AsOfDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                && root.GetProperty("book").GetRawText() == JsonSerializer.Serialize(value.Book, Json)
                && root.GetProperty("inspectedBookLotCount").GetInt32() == value.InspectedBookLotCount
                && root.GetProperty("sourceBookLots").GetArrayLength() == value.InspectedBookLotCount
                && root.GetProperty("heldLots").GetRawText() == JsonSerializer.Serialize(value.HeldLots, Json),
                "A case-scoped, paged or unverified population is not complete-book entitlement authority.");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException
            || exception is InvalidOperationException and not CorporateActionOperationException)
        {
            throw Ineligible("The retained complete-book inventory evidence cannot be verified.");
        }
        Require(value.HeldLots.Select(lot => lot.TaxLotRecordId).Distinct().Count() == value.HeldLots.Count,
            "Duplicate lot identities cannot multiply event entitlement.");
        foreach (var lot in value.HeldLots)
        {
            Require(lot.SecurityId == value.SecurityId && lot.LedgerBookId == value.Book.LedgerBookId
                && lot.BookPositionId != Guid.Empty && lot.AcquiredDate <= date && lot.OpenQuantity > 0m
                && lot.Currency == "USD" && !string.IsNullOrWhiteSpace(lot.Account.FinancialAccountId)
                && Same(lot.Account.Symbol, canonicalSymbol)
                && lot.Acquisition is { QuantityBasis: LotQuantityBasis.Units, Direction: OpenLotDirection.Long,
                    AcquisitionCurrency: "USD", FunctionalCurrency: "USD", AcquisitionFxRateToFunctional: 1m }
                && lot.Acquisition.Evidence is { Count: > 0 } && lot.Acquisition.Evidence.All(RetainedEvidenceIdentityValidator.IsComplete),
                "Every related holding needs identified USD unit-lot long-holder acquisition evidence; short netting, missing scope and FX are excluded.");
            try
            {
                _ = lot.ToOpenLot();
            }
            catch (Exception exception) when (exception is ArgumentException or Meridian.Ledger.LedgerValidationException or OverflowException)
            {
                throw Ineligible("Canonical acquisition facts do not bind the exact durable lot, date, quantity and retained basis.");
            }
        }
    }

    private static void ValidateCaseFacts(CorporateActionCashDividendCaseFactsDto? facts, CorporateActionProcessingCaseDto processingCase)
    {
        Require(facts is not null && facts.CaseId == processingCase.CaseId && facts.CaseVersion == processingCase.Version
            && Sha256Digest.IsCanonical(facts.SnapshotHash) && !string.IsNullOrWhiteSpace(facts.SnapshotJson)
            && Sha256Digest.FixedEquals(facts.SnapshotHash, Sha256Digest.ComputeUtf8(facts.SnapshotJson)),
            "The complete current processing-case facts are missing, changed or unverifiable.");
        Require(facts!.Options.Count == 0 && facts.Evidence.All(evidence => evidence.CaseId == processingCase.CaseId
            && CorporateActionEvidenceKinds.IsKnown(evidence.EvidenceKind)
            && evidence.EvidenceKind != CorporateActionEvidenceKinds.ElectionInstruction && !HasPayload(evidence.Metadata)
            && CorporateActionEvidenceKinds.IsTrustedReference(evidence.EvidenceReference) && Sha256Digest.IsCanonical(evidence.EvidenceHash)),
            "Options, elections and unclassified typed evidence facts are excluded; document prose does not classify the treatment.");
        try
        {
            using var json = JsonDocument.Parse(facts.SnapshotJson);
            var root = json.RootElement;
            Require(root.GetProperty("schemaVersion").GetInt32() == 1
                && root.GetProperty("method").GetString() == "complete-scoped-cash-dividend-case-facts-v1"
                && root.GetProperty("caseId").GetGuid() == processingCase.CaseId && root.GetProperty("complete").GetBoolean()
                && root.GetProperty("scope").GetRawText() == JsonSerializer.Serialize(processingCase.Scope, Json)
                && root.GetProperty("options").GetRawText() == JsonSerializer.Serialize(facts.Options, Json)
                && root.GetProperty("evidence").GetRawText() == JsonSerializer.Serialize(facts.Evidence, Json),
                "The retained case-fact snapshot does not bind its complete typed scope and content.");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException
            || exception is InvalidOperationException and not CorporateActionOperationException)
        {
            throw Ineligible("The complete retained case-fact snapshot cannot be verified.");
        }
    }

    private static void ValidatePositions(InstrumentPositionProjectionSnapshot snapshot,
        LedgerSecurityEntitlementInventorySnapshot inventory, AssetAccountingEventSpineDto spine)
    {
        Require(snapshot.SecurityId == inventory.SecurityId && snapshot.LedgerBookId == inventory.Book.LedgerBookId
            && snapshot.AsOfDate == inventory.AsOfDate, "The position inventory is not bound to the entitlement cutoff.");
        Require(snapshot.BookPositions.Select(position => position.PositionId).Distinct().Count() == snapshot.BookPositions.Count,
            "Position inventory identities are ambiguous.");
        var lotGroups = inventory.HeldLots.GroupBy(lot => lot.BookPositionId).ToDictionary(group => group.Key, group => group.Sum(lot => lot.OpenQuantity));
        foreach (var position in snapshot.BookPositions)
        {
            var roles = snapshot.InstrumentRoles.Where(item => item.RoleId == position.RoleId).ToArray();
            Require(roles.Length == 1, "The holder role identity is missing or ambiguous.");
            var role = roles[0];
            var quantity = position.CurrentEconomicState?.Quantity;
            Require(position.SecurityId == inventory.SecurityId && position.BookContext.LedgerBookId == inventory.Book.LedgerBookId
                && position.BookContext.BaseCurrency == "USD" && position.PositionSide is BookPositionSides.Long or BookPositionSides.Asset
                && position.BookContext.FundProfileId == inventory.Book.FundProfileId
                && position.BookContext.FundStructureNodeId == inventory.Book.FundStructureNodeId
                && position.BookContext.FundStructureNodeKind == inventory.Book.FundStructureNodeKind
                && role.SecurityId == inventory.SecurityId && role.RoleKind == InstrumentRoleKinds.Holder
                && role.EffectiveFrom <= inventory.AsOfDate
                && (role.EffectiveTo is null || role.EffectiveTo >= inventory.AsOfDate)
                && position.EffectiveFrom <= inventory.AsOfDate
                && (position.EffectiveTo is null || position.EffectiveTo >= inventory.AsOfDate)
                && position.CurrentEconomicState?.Currency == "USD"
                && position.CurrentEconomicState.AsOfDate <= inventory.AsOfDate
                && quantity.HasValue && quantity.Value >= 0m && quantity.Value == lotGroups.GetValueOrDefault(position.PositionId),
                "Unmatched, short, stale or unknown book-position quantities prevent complete long-holder entitlement.");
            if (position.PositionId == spine.Scope.BookPositionId)
                Require(position.Version == spine.Scope.ExpectedBookPositionVersion, "The case's canonical position version changed.");
        }
        Require(lotGroups.Keys.All(id => snapshot.BookPositions.Any(position => position.PositionId == id)),
            "Canonical lots have no matching retained book-position scope.");
    }

    private static void ValidateCandidate(CorporateActionCaseAccountingProjectionDto projection, AssetAccountingEventSpineDto spine, decimal gross)
    {
        var candidate = spine.DraftedCandidateResult;
        var request = spine.DraftedCandidate;
        Require(gross > 0m && projection.Currency == "USD" && projection.TotalDebits == gross && projection.TotalCredits == gross
            && projection.EffectiveDate == spine.EconomicEvent.EffectiveDate
            && spine.EventAmount == gross && spine.Currency == "USD" && spine.ProjectedEffect is { } effect
            && effect.Currency == "USD" && effect.TotalDebits == gross && effect.TotalCredits == gross
            && candidate is { IsBalanced: true, HasBlockingIssues: false } && candidate.TotalDebits == gross
            && candidate.TotalCredits == gross && candidate.GeneratedPostingLines.Count == 2
            && !string.IsNullOrWhiteSpace(projection.SelectedRuleId) && !string.IsNullOrWhiteSpace(projection.SelectedRuleVersion)
            && candidate.SelectedRuleId == projection.SelectedRuleId && candidate.SelectedRuleVersion == projection.SelectedRuleVersion
            && request is not null && request.EventAmount == gross && request.Currency == "USD"
            && request.EffectiveDate == projection.EffectiveDate && request.LedgerBookId == projection.LedgerBookId
            && request.PeriodId == projection.PeriodId && request.SourceEventId == spine.EventId
            && request.RulePackReference is { } rule && rule.RulePackId == projection.RulePackId
            && rule.RulePackVersion == projection.RulePackVersion && rule.SelectedRuleId == projection.SelectedRuleId
            && rule.SelectedRuleVersion == projection.SelectedRuleVersion
            && spine.DraftedCandidateFingerprint == projection.DraftedCandidateFingerprint
            && spine.DraftedCandidateFingerprint == AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(request)
            && spine.DraftedCandidateResultFingerprint == AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidate),
            "The exact balanced native cash-dividend candidate must equal canonical held entitlement and retain its promoted rule.");
        var debits = candidate!.GeneratedPostingLines.Where(line => line.Side == AccountingTemplateLineSideDto.Debit).ToArray();
        var credits = candidate.GeneratedPostingLines.Where(line => line.Side == AccountingTemplateLineSideDto.Credit).ToArray();
        Require(debits.Length == 1 && credits.Length == 1, "Exactly one receivable debit and one dividend-income credit are required.");
        var debit = debits[0];
        var credit = credits[0];
        Require(debit.Amount == gross && credit.Amount == gross
            && debit.Currency == "USD" && credit.Currency == "USD"
            && !string.IsNullOrWhiteSpace(debit.AccountPath) && !string.IsNullOrWhiteSpace(credit.AccountPath)
            && !Same(debit.AccountPath, credit.AccountPath),
            "Only a two-line asset-debit/income-credit native declaration candidate qualifies; tax, fee and special allocations are excluded.");
    }

    private static object ValidateGovernedRule(AccountingConfigurationWorkspaceDto workspace,
        CorporateActionProcessingCaseDto processingCase, CorporateActionCaseAccountingProjectionDto projection,
        AssetAccountingEventSpineDto spine)
    {
        Require(workspace.Status == AccountingConfigurationStatusDto.Active
            && workspace.LedgerBookId == projection.LedgerBookId && workspace.FundProfileId == projection.FundProfileId
            && Same(workspace.TenantId, processingCase.Scope.TenantId) && Same(workspace.CompanyId, processingCase.Scope.CompanyId)
            && !workspace.ValidationIssues.Any(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical),
            "The exact book requires active tenant/company accounting configuration.");
        var matches = workspace.PostingRules.Where(rule => rule.RuleId == projection.SelectedRuleId
            && rule.RuleVersion == projection.SelectedRuleVersion).ToArray();
        Require(matches.Length == 1, "The retained selected rule/version is no longer uniquely configured.");
        var rule = matches[0];
        Require(!rule.IsArchived && rule.SourceEventType == spine.DraftedCandidate!.SourceEventType
            && (rule.EffectiveFrom is null || rule.EffectiveFrom <= projection.EffectiveDate)
            && (rule.EffectiveTo is null || rule.EffectiveTo >= projection.EffectiveDate)
            && rule.PromotionApproval is { ApprovalState: ManualJournalEntryStatusDto.Approved } approval
            && !string.IsNullOrWhiteSpace(approval.ApprovalId) && !string.IsNullOrWhiteSpace(approval.RequestedBy)
            && !string.IsNullOrWhiteSpace(approval.ApprovedBy) && approval.ApprovedAtUtc.HasValue
            && !Same(approval.RequestedBy, approval.ApprovedBy),
            "This class requires the existing exact active independently promoted native rule; it grants no rule promotion authority.");
        var lines = spine.DraftedCandidateResult!.GeneratedPostingLines;
        var debit = lines.Single(line => line.Side == AccountingTemplateLineSideDto.Debit);
        var credit = lines.Single(line => line.Side == AccountingTemplateLineSideDto.Credit);
        var debitNodes = workspace.ChartOfAccounts.Where(node => Same(node.Path, debit.AccountPath)).ToArray();
        var creditNodes = workspace.ChartOfAccounts.Where(node => Same(node.Path, credit.AccountPath)).ToArray();
        Require(debitNodes.Length == 1 && creditNodes.Length == 1
            && !debitNodes[0].IsArchived && !creditNodes[0].IsArchived
            && !string.IsNullOrWhiteSpace(debitNodes[0].AccountName) && !string.IsNullOrWhiteSpace(creditNodes[0].AccountName)
            && Same(debitNodes[0].AccountType, "Asset") && Same(creditNodes[0].AccountType, "Revenue"),
            "The existing native posting paths must resolve uniquely to active Asset and Revenue chart accounts.");
        bool MatchesNativeMapping(ChartOfAccountsNodeDto node, GeneratedPostingLineDto line) =>
            (string.IsNullOrWhiteSpace(node.Symbol) || Same(node.Symbol, spine.DraftedCandidate!.InstrumentSymbol!))
            && (string.IsNullOrWhiteSpace(node.FinancialAccountId) || Same(node.FinancialAccountId, line.Dimensions?.AccountId ?? string.Empty));
        Require(MatchesNativeMapping(debitNodes[0], debit) && MatchesNativeMapping(creditNodes[0], credit),
            "Explicit chart symbols and financial-account mappings must match the retained canonical security and native generated line dimensions.");
        return new { workspace.ConfigurationVersion, Rule = rule, Receivable = debitNodes[0], DividendIncome = creditNodes[0] };
    }

    private static bool SameQuantities(IReadOnlyList<LedgerTaxLotRecord> left, IReadOnlyList<LedgerTaxLotRecord> right) =>
        left.Count == right.Count && left.All(lot => right.Any(other => other.TaxLotRecordId == lot.TaxLotRecordId
            && other.SecurityId == lot.SecurityId && other.LedgerBookId == lot.LedgerBookId
            && other.BookPositionId == lot.BookPositionId && other.Account == lot.Account
            && other.AcquiredDate == lot.AcquiredDate && other.Currency == lot.Currency && other.OpenQuantity == lot.OpenQuantity
            && other.Acquisition?.Direction == lot.Acquisition?.Direction));
    private static bool HasPayload(JsonElement? payload) => payload.HasValue && payload.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
        && (payload.Value.ValueKind != JsonValueKind.Object || payload.Value.EnumerateObject().Any());
    private static bool Same(string? left, string? right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    private static void Require(bool condition, string message) { if (!condition) throw Ineligible(message); }
    private static CorporateActionOperationException Ineligible(string message) =>
        new(CorporateActionCashDividendAttestationValidation.ExcludedProblemCode, message);
    private static CorporateActionPersistenceUnavailableException Missing(string message) => new(message);
}
