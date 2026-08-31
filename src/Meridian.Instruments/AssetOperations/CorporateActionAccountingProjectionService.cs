using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;

namespace Meridian.Instruments.AssetOperations;

public interface ICorporateActionAccountingProjectionService
{
    CorporateActionAccountingProjectionDto Project(CorporateActionAccountingProjectionRequest request);
}

public sealed record CorporateActionAccountingProjectionRequest(
    Guid SourceCorporateActionId,
    long SourceEventVersion,
    CorporateActionAccountingTypeDto ActionType,
    AccountingBasisKindDto AccountingBasis,
    Guid SecurityId,
    Guid PositionId,
    long PositionVersion,
    long ExpectedPositionVersion,
    DateOnly EffectiveDate,
    DateOnly RuleProfileAsOfDate,
    DateTimeOffset OccurredAtUtc,
    string Currency,
    string SourceDomain,
    string SourceEntityId,
    string SourceContentHash,
    CorporateActionEconomicsDto Economics,
    CorporateActionPolicyInputsDto? PolicyInputs = null,
    IReadOnlyList<CorporateActionProjectionEvidenceDependencyDto>? EvidenceManifest = null,
    Guid? CorrelationId = null,
    Guid? CausationId = null,
    DateTimeOffset? GeneratedAtUtc = null,
    Guid? CaseId = null,
    long? CaseVersion = null,
    long? ElectionVersion = null,
    long? PolicyDecisionVersion = null,
    Guid? PositionSnapshotId = null,
    CorporateActionAccountingProjectionScopeDto? AccountingScope = null,
    Guid? LotSnapshotId = null,
    long? LotSnapshotVersion = null,
    Guid? PolicyDecisionId = null,
    Guid? ElectionId = null,
    IReadOnlyList<CorporateActionLotMutationDto>? AuthoritativeLotMutations = null)
{
    public IReadOnlyList<CorporateActionProjectionEvidenceDependencyDto> EvidenceManifest { get; init; } =
        EvidenceManifest ?? [];

    public CorporateActionPolicyInputsDto PolicyInputs { get; init; } = PolicyInputs ?? new();

    public IReadOnlyList<CorporateActionLotMutationDto> AuthoritativeLotMutations { get; init; } =
        AuthoritativeLotMutations ?? [];
}

/// <summary>
/// Deterministic, fail-closed projector for the Clearwater v1 corporate-action profile. It emits
/// economic and lot intent only; Financial Operations still owns promoted rule-pack mapping,
/// review, and candidate preparation, and Ledger/Storage retain posting authority.
/// </summary>
public sealed partial class CorporateActionAccountingProjectionService : ICorporateActionAccountingProjectionService
{
    public const string ModelKey = "clearwater-corporate-action";
    public const string ModelVersion = "v1";
    public const string EngineVersion = "corporate-action-projection-v1";

    public CorporateActionAccountingProjectionDto Project(CorporateActionAccountingProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Economics);

        var decision = ClearwaterCorporateActionRuleProfileV1.Resolve(request.ActionType, request.AccountingBasis);
        decision = ApplyPolicySelections(decision, request.PolicyInputs);

        var evidenceManifest = request.EvidenceManifest
            .OrderBy(static item => item.Role)
            .ThenBy(static item => item.EvidenceId, StringComparer.Ordinal)
            .ThenBy(static item => item.EvidenceVersion)
            .ToArray();
        var evidence = evidenceManifest
            .Select(static item => item.EvidenceUri.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var currency = request.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
        var blockers = ValidateCommon(request, evidenceManifest, currency);
        var projectionInputHash = BuildProjectionInputHash(request, decision, evidenceManifest, currency);

        ProjectionComputation computation;
        try
        {
            computation = blockers.Count == 0
                ? ProjectAction(request, currency, blockers)
                : ProjectionComputation.Empty;
        }
        catch (OverflowException)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.amount-overflow",
                "The corporate-action economics exceed the supported decimal range."));
            computation = ProjectionComputation.Empty;
        }

        if (blockers.Count == 0 && request.AuthoritativeLotMutations.Count > 0)
        {
            computation = BindAuthoritativeLotMutations(
                computation,
                request.AuthoritativeLotMutations,
                blockers);
        }

        if (blockers.Count > 0)
        {
            return new CorporateActionAccountingProjectionDto(
                CorporateActionProjectionStatusDto.Blocked,
                decision,
                0m,
                null,
                null,
                [],
                new CorporateActionLotMutationSetDto(request.PositionId, request.ExpectedPositionVersion),
                new CorporateActionPostingSetDto(request.AccountingBasis, currency, false),
                blockers,
                request.CaseId ?? Guid.Empty,
                request.CaseVersion ?? 0,
                request.ElectionVersion,
                request.PolicyDecisionVersion ?? 0,
                request.PositionSnapshotId ?? Guid.Empty,
                projectionInputHash,
                PostingIntentHash: null,
                request.AccountingScope,
                evidenceManifest,
                request.LotSnapshotId ?? Guid.Empty,
                request.LotSnapshotVersion ?? 0,
                request.PolicyDecisionId ?? Guid.Empty,
                request.ElectionId);
        }

        var eventId = DeterministicGuid(BuildEventIdentity(request));
        var occurredAtUtc = request.OccurredAtUtc.ToUniversalTime();
        var generatedAtUtc = (request.GeneratedAtUtc ?? request.OccurredAtUtc).ToUniversalTime();
        var sourceDomain = request.SourceDomain.Trim();
        var sourceEntityId = request.SourceEntityId.Trim();
        var sourceHash = request.SourceContentHash.Trim();

        var economicEvent = new EconomicEventReferenceDto(
            eventId,
            AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction),
            request.SourceEventVersion,
            request.EffectiveDate,
            occurredAtUtc,
            sourceDomain,
            sourceEntityId,
            request.CorrelationId,
            request.CausationId,
            sourceHash,
            evidence)
        {
            SecurityId = request.SecurityId,
            BookPositionId = request.PositionId
        };

        var lineage = new ProjectionLineageDto(
            DeterministicGuid($"{eventId:N}|projection-run|{projectionInputHash}"),
            DeterministicGuid($"{eventId:N}|projection-event|{projectionInputHash}"),
            ModelKey,
            ModelVersion,
            EngineVersion,
            request.ActionType.ToString(),
            request.EffectiveDate,
            generatedAtUtc,
            sourceDomain,
            sourceEntityId,
            economicEvent,
            TermsVersion: ClearwaterCorporateActionRuleProfileV1.ProfileVersion,
            TermsHash: projectionInputHash,
            EvidenceLinks: evidence)
        {
            BookPositionId = request.PositionId
        };

        var postingIntentHash = BuildPostingIntentHash(
            projectionInputHash,
            decision,
            computation,
            currency);

        return new CorporateActionAccountingProjectionDto(
            CorporateActionProjectionStatusDto.Projected,
            decision,
            Round(computation.EventAmount, currency),
            economicEvent,
            lineage,
            computation.Recipe,
            new CorporateActionLotMutationSetDto(
                request.PositionId,
                request.ExpectedPositionVersion,
                computation.LotMutations),
            new CorporateActionPostingSetDto(
                request.AccountingBasis,
                currency,
                computation.RequiresJournalCandidate,
                computation.PostingComponents),
            [],
            request.CaseId ?? Guid.Empty,
            request.CaseVersion ?? 0,
            request.ElectionVersion,
            request.PolicyDecisionVersion ?? 0,
            request.PositionSnapshotId ?? Guid.Empty,
            projectionInputHash,
            postingIntentHash,
            request.AccountingScope,
            evidenceManifest,
            request.LotSnapshotId ?? Guid.Empty,
            request.LotSnapshotVersion ?? 0,
            request.PolicyDecisionId ?? Guid.Empty,
            request.ElectionId);
    }

    private static CorporateActionTreatmentDecisionDto ApplyPolicySelections(
        CorporateActionTreatmentDecisionDto decision,
        CorporateActionPolicyInputsDto policy)
    {
        var holdingPeriod = policy.CarryHoldingPeriod switch
        {
            true => CorporateActionHoldingPeriodTreatmentDto.CarryOver,
            false => CorporateActionHoldingPeriodTreatmentDto.NewLot,
            null => decision.HoldingPeriodTreatment
        };

        var taxClassification = policy.ApprovedTaxClassification ?? decision.PreliminaryTaxClassification;
        return decision with
        {
            HoldingPeriodTreatment = holdingPeriod,
            PreliminaryTaxClassification = taxClassification
        };
    }

    private static List<CorporateActionProjectionBlockerDto> ValidateCommon(
        CorporateActionAccountingProjectionRequest request,
        IReadOnlyList<CorporateActionProjectionEvidenceDependencyDto> evidence,
        string currency)
    {
        var blockers = new List<CorporateActionProjectionBlockerDto>();
        AddIf(blockers, request.SourceCorporateActionId == Guid.Empty, "corporate-action.source-id-required",
            "A source corporate-action identity is required.");
        AddIf(blockers, request.SourceEventVersion <= 0, "corporate-action.source-version-invalid",
            "The source corporate-action version must be positive.");
        AddIf(blockers, request.CaseId is null || request.CaseId == Guid.Empty,
            "corporate-action.case-id-required",
            "A durable corporate-action case identity is required.");
        AddIf(blockers, request.CaseVersion is null or <= 0,
            "corporate-action.case-version-invalid",
            "The corporate-action case version must be positive.");
        AddIf(blockers, request.ElectionVersion is <= 0,
            "corporate-action.election-version-invalid",
            "An election version, when supplied, must be positive.");
        AddIf(blockers, request.PolicyDecisionVersion is null or <= 0,
            "corporate-action.policy-decision-version-invalid",
            "The basis-specific policy decision version must be positive.");
        AddIf(blockers, request.PositionSnapshotId is null || request.PositionSnapshotId == Guid.Empty,
            "corporate-action.position-snapshot-required",
            "An authoritative position snapshot identity is required.");
        AddIf(blockers, request.LotSnapshotId is null || request.LotSnapshotId == Guid.Empty,
            "corporate-action.lot-snapshot-required",
            "An authoritative lot snapshot identity is required.");
        AddIf(blockers, request.LotSnapshotVersion is null or <= 0,
            "corporate-action.lot-snapshot-version-invalid",
            "The authoritative lot snapshot version must be positive.");
        AddIf(blockers, request.PolicyDecisionId is null || request.PolicyDecisionId == Guid.Empty,
            "corporate-action.policy-decision-id-required",
            "A basis-specific policy decision identity is required.");
        AddIf(blockers,
            request.ElectionVersion.HasValue != request.ElectionId.HasValue ||
            request.ElectionId == Guid.Empty,
            "corporate-action.election-identity-invalid",
            "Election identity and positive version must either both be absent or both be supplied.");
        ValidateAccountingScope(request.AccountingScope, request.AccountingBasis, blockers);
        AddIf(blockers, request.SecurityId == Guid.Empty, "corporate-action.security-required",
            "A Security Master identity is required.");
        AddIf(blockers, request.PositionId == Guid.Empty, "corporate-action.position-required",
            "A book-position identity is required.");
        AddIf(blockers, request.PositionVersion <= 0, "corporate-action.position-version-invalid",
            "The persisted position version must be positive.");
        AddIf(blockers, request.ExpectedPositionVersion != request.PositionVersion,
            "corporate-action.position-version-stale",
            "The expected position version does not match the persisted position version.");
        AddIf(blockers, request.EffectiveDate == default, "corporate-action.effective-date-required",
            "An effective date is required.");
        AddIf(blockers, !ClearwaterCorporateActionRuleProfileV1.IsEffectiveOn(request.RuleProfileAsOfDate),
            "corporate-action.rule-profile-not-effective",
            $"Rule profile {ClearwaterCorporateActionRuleProfileV1.ProfileKey} is not effective on " +
            $"{request.RuleProfileAsOfDate:yyyy-MM-dd}.");
        AddIf(blockers, request.OccurredAtUtc == default, "corporate-action.occurred-at-required",
            "A non-default occurrence timestamp is required.");
        AddIf(blockers, string.IsNullOrWhiteSpace(request.SourceDomain), "corporate-action.source-domain-required",
            "A source domain is required.");
        AddIf(blockers, string.IsNullOrWhiteSpace(request.SourceEntityId), "corporate-action.source-entity-required",
            "A source entity identity is required.");
        AddIf(blockers, !Sha256Digest.IsCanonical(request.SourceContentHash), "corporate-action.source-hash-invalid",
            "A canonical lowercase SHA-256 source-content hash is required.");
        AddIf(blockers, evidence.Count == 0, "corporate-action.evidence-required",
            "A typed retained-evidence manifest is required.");
        ValidateEvidenceManifest(request, evidence, blockers);
        AddIf(blockers, !IsSupportedCurrency(currency), "corporate-action.currency-invalid",
            "Currency must be a three-letter ISO-style code.");

        ValidateNonNegative(request.Economics.CarryingAmount, nameof(request.Economics.CarryingAmount), blockers);
        ValidateNonNegative(request.Economics.ParAmount, nameof(request.Economics.ParAmount), blockers);
        ValidateNonNegative(request.Economics.GrossCashConsideration, nameof(request.Economics.GrossCashConsideration), blockers);
        ValidateNonNegative(request.Economics.AccruedIncome, nameof(request.Economics.AccruedIncome), blockers);
        ValidateNonNegative(request.Economics.Rate, nameof(request.Economics.Rate), blockers);
        ValidateNonNegative(request.Economics.CashRatePerUnit, nameof(request.Economics.CashRatePerUnit), blockers);
        ValidateNonNegative(request.Economics.PurchasePricePerUnit, nameof(request.Economics.PurchasePricePerUnit), blockers);
        ValidateNonNegative(request.Economics.SubscriptionPricePerUnit, nameof(request.Economics.SubscriptionPricePerUnit), blockers);
        foreach (var successor in request.Economics.Successors)
        {
            AddIf(blockers, successor.SecurityId == Guid.Empty, "corporate-action.successor-security-required",
                "Every successor requires a Security Master identity.");
            AddIf(blockers, successor.Quantity <= 0m, "corporate-action.successor-quantity-invalid",
                "Every successor quantity must be positive.");
            AddIf(blockers, successor.BookValueAllocationPercent is < 0m or > 1m,
                "corporate-action.successor-allocation-invalid",
                "Successor book-value allocation percentages must be between zero and one.");
            AddIf(blockers, successor.FairValue < 0m, "corporate-action.successor-fair-value-invalid",
                "Successor fair value cannot be negative.");
        }

        return blockers;
    }

    private static ProjectionComputation ProjectAction(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
        => request.ActionType switch
        {
            CorporateActionAccountingTypeDto.RegS144AExchange =>
                ProjectBookValueExchange(request, currency, blockers, requireAllocatedSuccessors: false),
            CorporateActionAccountingTypeDto.AdvanceRefunding =>
                ProjectAdvanceRefunding(request, currency, blockers),
            CorporateActionAccountingTypeDto.BankruptcyDistribution =>
                ProjectBankruptcy(request, currency, blockers),
            CorporateActionAccountingTypeDto.CallRedemption =>
                ProjectCall(request, currency, blockers),
            CorporateActionAccountingTypeDto.ConsentSolicitation =>
                ProjectConsent(request, currency, blockers),
            CorporateActionAccountingTypeDto.DebtToEquityConversion =>
                ProjectConversion(request, currency, blockers),
            CorporateActionAccountingTypeDto.CashDividend =>
                ProjectCashDividend(request, currency, blockers),
            CorporateActionAccountingTypeDto.StockDividend =>
                ProjectStockDividend(request, currency, blockers),
            CorporateActionAccountingTypeDto.DividendReinvestment =>
                ProjectDividendReinvestment(request, currency, blockers),
            CorporateActionAccountingTypeDto.ScripDividend =>
                ProjectScripDividend(request, blockers),
            CorporateActionAccountingTypeDto.ExchangeOffer =>
                ProjectExchangeOffer(request, currency, blockers),
            CorporateActionAccountingTypeDto.FractionalCashInLieu =>
                ProjectDisposition(request, currency, blockers, CorporateActionEconomicOperationKindDto.CorporateActionSale),
            CorporateActionAccountingTypeDto.MergerStock =>
                ProjectMergerStock(request, currency, blockers),
            CorporateActionAccountingTypeDto.MergerCash =>
                ProjectDisposition(request, currency, blockers, CorporateActionEconomicOperationKindDto.CorporateActionSale),
            CorporateActionAccountingTypeDto.MergerMixed =>
                ProjectMergerMixed(request, currency, blockers),
            CorporateActionAccountingTypeDto.NameIdentifierChange =>
                ProjectBookValueExchange(request, currency, blockers, requireAllocatedSuccessors: false),
            CorporateActionAccountingTypeDto.PaymentInKind =>
                ProjectPaymentInKind(request, currency, blockers),
            CorporateActionAccountingTypeDto.PutRedemption =>
                ProjectDisposition(request, currency, blockers, CorporateActionEconomicOperationKindDto.Redemption),
            CorporateActionAccountingTypeDto.ReturnOfCapital =>
                ProjectReturnOfCapital(request, currency, blockers),
            CorporateActionAccountingTypeDto.ReverseStockSplit =>
                ProjectSplit(request, currency, blockers, reverse: true),
            CorporateActionAccountingTypeDto.RightsDistribution =>
                ProjectRightsDistribution(request, blockers),
            CorporateActionAccountingTypeDto.RightsExercise =>
                ProjectRightsExercise(request, currency, blockers),
            CorporateActionAccountingTypeDto.RightsExpiration =>
                ProjectRightsExpiration(request, blockers),
            CorporateActionAccountingTypeDto.SpinOff =>
                ProjectSpinOff(request, currency, blockers),
            CorporateActionAccountingTypeDto.StockSplit =>
                ProjectSplit(request, currency, blockers, reverse: false),
            CorporateActionAccountingTypeDto.TenderOffer =>
                ProjectTender(request, currency, blockers),
            _ => throw new ArgumentOutOfRangeException(nameof(request.ActionType))
        };

    private static ProjectionComputation ProjectBookValueExchange(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers,
        bool requireAllocatedSuccessors)
    {
        var quantity = RequirePositive(request.Economics.AffectedQuantity, "affected-quantity", blockers);
        var carrying = RequireNonNegative(request.Economics.CarryingAmount, "carrying-amount", blockers);
        var successors = RequireSuccessors(request, blockers);
        AddIf(blockers, request.Economics.GrossCashConsideration is > 0m,
            "corporate-action.exchange-cash-not-supported",
            "This book-value-conserving exchange branch does not permit cash consideration.");
        ValidateAllocations(successors, requireAllocatedSuccessors || successors.Count > 1, blockers);
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        var recipe = new List<CorporateActionEconomicOperationDto>
        {
            new(CorporateActionEconomicOperationKindDto.ExchangeOut, request.SecurityId, Quantity: quantity)
        };
        recipe.AddRange(successors.Select(successor => new CorporateActionEconomicOperationDto(
            CorporateActionEconomicOperationKindDto.ExchangeIn,
            successor.SecurityId,
            successor.Role,
            successor.Quantity,
            Description: "Book-value-conserving successor")));

        var holdingPeriod = request.PolicyInputs.CarryHoldingPeriod switch
        {
            true => CorporateActionHoldingPeriodTreatmentDto.CarryOver,
            false => CorporateActionHoldingPeriodTreatmentDto.NewLot,
            null when request.ActionType is CorporateActionAccountingTypeDto.RegS144AExchange or
                CorporateActionAccountingTypeDto.AdvanceRefunding or
                CorporateActionAccountingTypeDto.NameIdentifierChange or
                CorporateActionAccountingTypeDto.ReverseStockSplit =>
                CorporateActionHoldingPeriodTreatmentDto.CarryOver,
            _ => CorporateActionHoldingPeriodTreatmentDto.PolicyDefined
        };
        var lotTemplates = successors.Select(successor => new CorporateActionLotMutationDto(
            successors.Count == 1
                ? CorporateActionLotMutationKindDto.CarryOver
                : CorporateActionLotMutationKindDto.Allocate,
            request.SecurityId,
            successor.SecurityId,
            successor.Quantity,
            0m,
            successor.BookValueAllocationPercent,
            holdingPeriod,
            "Carry source-lot lineage to successor"))
            .ToArray();
        var lotMutations = AllocateSourceRelief(
            AllocateSuccessorBasis(lotTemplates, carrying, currency),
            quantity,
            carrying,
            currency);

        return new ProjectionComputation(
            carrying,
            recipe,
            lotMutations,
            CarryingValueTransferComponents(carrying, currency),
            carrying > 0m);
    }

    private static ProjectionComputation ProjectAdvanceRefunding(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        var refunded = request.Economics.Successors.Count(successor =>
            successor.Role == CorporateActionSuccessorRoleDto.Refunded);
        var unrefunded = request.Economics.Successors.Count(successor =>
            successor.Role == CorporateActionSuccessorRoleDto.Unrefunded);
        AddIf(blockers, refunded != 1 || unrefunded != 1,
            "corporate-action.advance-refunding-successors-invalid",
            "Advance refunding requires exactly one refunded and one unrefunded successor.");
        var projection = ProjectBookValueExchange(request, currency, blockers, requireAllocatedSuccessors: true);
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        var refundedSecurityIds = request.Economics.Successors
            .Where(static successor => successor.Role == CorporateActionSuccessorRoleDto.Refunded)
            .Select(static successor => successor.SecurityId)
            .ToHashSet();
        return projection with
        {
            LotMutations = projection.LotMutations
                .Select(mutation => mutation.TargetSecurityId is { } target && refundedSecurityIds.Contains(target)
                    ? mutation with { ReportingTags = ["ScheduleD"] }
                    : mutation)
                .ToArray()
        };
    }

    private static ProjectionComputation ProjectBankruptcy(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        var method = request.PolicyInputs.BankruptcyMethod;
        AddIf(blockers, method is null, "corporate-action.bankruptcy-method-required",
            "Bankruptcy processing requires an approved case-specific method.");
        if (method is null)
        {
            return ProjectionComputation.Empty;
        }

        return method.Value switch
        {
            CorporateActionBankruptcyMethodDto.TransferOutAtZero => ProjectTransferOut(request, currency, blockers),
            CorporateActionBankruptcyMethodDto.CashOnlySale =>
                ProjectDisposition(request, currency, blockers, CorporateActionEconomicOperationKindDto.CorporateActionSale),
            CorporateActionBankruptcyMethodDto.EscrowExchange =>
                ProjectBankruptcyExchange(request, currency, blockers, CorporateActionSuccessorRoleDto.Escrow, allowCash: false),
            CorporateActionBankruptcyMethodDto.SecuritiesAndCashExchange =>
                ProjectBankruptcyExchange(request, currency, blockers, null, allowCash: true),
            _ => throw new ArgumentOutOfRangeException(nameof(request.PolicyInputs.BankruptcyMethod))
        };
    }

    private static ProjectionComputation ProjectTransferOut(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        var quantity = RequirePositive(request.Economics.AffectedQuantity, "affected-quantity", blockers);
        var carrying = RequireNonNegative(request.Economics.CarryingAmount, "carrying-amount", blockers);
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        return new ProjectionComputation(
            carrying,
            [new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.TransferOut,
                request.SecurityId, Quantity: quantity, Amount: 0m)],
            [new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.TransferOut,
                request.SecurityId, Quantity: quantity, CarryingAmount: carrying)],
            carrying > 0m
                ? [new CorporateActionPostingComponentDto(CorporateActionPostingComponentKindDto.CarryingValueRelief,
                    carrying, currency)]
                : [],
            carrying > 0m);
    }

    private static ProjectionComputation ProjectBankruptcyExchange(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers,
        CorporateActionSuccessorRoleDto? requiredRole,
        bool allowCash)
    {
        if (requiredRole.HasValue)
        {
            AddIf(blockers, !request.Economics.Successors.Any(successor => successor.Role == requiredRole),
                "corporate-action.bankruptcy-successor-invalid",
                $"The selected bankruptcy method requires a {requiredRole} successor.");
        }

        if (!allowCash)
        {
            AddIf(blockers, request.Economics.GrossCashConsideration is > 0m,
                "corporate-action.bankruptcy-cash-not-allowed",
                "The selected bankruptcy exchange method does not permit cash consideration.");
        }

        var cashConsideration = request.Economics.GrossCashConsideration is > 0m
            ? Round(request.Economics.GrossCashConsideration.Value, currency)
            : 0m;
        if (allowCash && cashConsideration > 0m)
        {
            AddIf(blockers, request.PolicyInputs.CashRecognition is null,
                "corporate-action.bankruptcy-cash-recognition-required",
                "A securities-and-cash bankruptcy exchange requires an approved cash classification.");
            AddIf(blockers, request.PolicyInputs.ApprovedCashRecognitionAmount is null or < 0m,
                "corporate-action.bankruptcy-recognition-amount-required",
                "A securities-and-cash bankruptcy exchange requires an approved non-negative recognition amount.");
            AddIf(blockers, request.PolicyInputs.ApprovedSuccessorBasis is null or < 0m,
                "corporate-action.bankruptcy-successor-basis-required",
                "A securities-and-cash bankruptcy exchange requires an approved successor basis.");
        }

        var exchange = ProjectBookValueExchange(request with
        {
            Economics = request.Economics with
            {
                GrossCashConsideration = null
            }
        }, currency, blockers, requireAllocatedSuccessors: request.Economics.Successors.Count > 1);
        if (blockers.Count > 0 || !allowCash || request.Economics.GrossCashConsideration is not > 0m)
        {
            return exchange;
        }

        var cash = cashConsideration;
        var carrying = request.Economics.CarryingAmount!.Value;
        var recognition = request.PolicyInputs.ApprovedCashRecognitionAmount!.Value;
        var approvedBasis = request.PolicyInputs.ApprovedSuccessorBasis!.Value;
        AddIf(blockers, recognition > cash,
            "corporate-action.bankruptcy-recognition-exceeds-cash",
            "Approved bankruptcy cash recognition cannot exceed the cash consideration.");
        AddIf(blockers, Round(carrying - cash + recognition, currency) != Round(approvedBasis, currency),
            "corporate-action.bankruptcy-basis-reconciliation-invalid",
            "Approved successor basis must reconcile old carrying value, cash received, and recognized amount.");
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        var recipe = exchange.Recipe.Append(new CorporateActionEconomicOperationDto(
            CorporateActionEconomicOperationKindDto.CashFromCorporateAction,
            Amount: cash)).ToArray();
        var components = exchange.PostingComponents
            .Where(static component => component.Kind != CorporateActionPostingComponentKindDto.PurchaseCost)
            .Concat(
            [
                new CorporateActionPostingComponentDto(
                    CorporateActionPostingComponentKindDto.PurchaseCost,
                    approvedBasis,
                    currency),
                new CorporateActionPostingComponentDto(
                    CorporateActionPostingComponentKindDto.Cash,
                    cash,
                    currency)
            ])
            .ToList();
        if (recognition > 0m)
        {
            components.Add(new CorporateActionPostingComponentDto(
                request.PolicyInputs.CashRecognition == CorporateActionCashRecognitionDto.Gain
                    ? CorporateActionPostingComponentKindDto.RealizedGain
                    : CorporateActionPostingComponentKindDto.InvestmentIncome,
                recognition,
                currency));
        }

        return exchange with
        {
            EventAmount = cash,
            Recipe = recipe,
            LotMutations = AllocateSuccessorBasis(exchange.LotMutations, approvedBasis, currency),
            PostingComponents = components,
            RequiresJournalCandidate = true
        };
    }

    private static ProjectionComputation ProjectCall(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        var quantity = RequirePositive(request.Economics.AffectedQuantity, "affected-quantity", blockers);
        var par = RequirePositive(request.Economics.ParAmount, "par-amount", blockers);
        var cash = RequirePositive(request.Economics.GrossCashConsideration, "gross-cash-consideration", blockers);
        var carrying = RequireNonNegative(request.Economics.CarryingAmount, "carrying-amount", blockers);
        var accrued = request.Economics.AccruedIncome ?? 0m;
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        var recipe = new List<CorporateActionEconomicOperationDto>
        {
            new(CorporateActionEconomicOperationKindDto.Redemption, request.SecurityId, Quantity: quantity,
                Amount: request.AccountingBasis == AccountingBasisKindDto.Statutory && request.Economics.IsMakeWhole
                    ? par
                    : cash - accrued)
        };
        var components = new List<CorporateActionPostingComponentDto>
        {
            new(CorporateActionPostingComponentKindDto.Cash, cash, currency),
            new(CorporateActionPostingComponentKindDto.CarryingValueRelief, carrying, currency),
            new(CorporateActionPostingComponentKindDto.RedemptionPrincipal,
                request.AccountingBasis == AccountingBasisKindDto.Statutory && request.Economics.IsMakeWhole
                    ? par
                    : cash - accrued,
                currency)
        };

        if (accrued > 0m)
        {
            recipe.Add(new CorporateActionEconomicOperationDto(
                CorporateActionEconomicOperationKindDto.CouponIncome,
                request.SecurityId,
                Amount: accrued));
            components.Add(new CorporateActionPostingComponentDto(
                CorporateActionPostingComponentKindDto.AccruedIncome,
                accrued,
                currency));
        }

        if (request.AccountingBasis == AccountingBasisKindDto.Statutory && request.Economics.IsMakeWhole)
        {
            var penalty = cash - par - accrued;
            AddIf(blockers, penalty < 0m, "corporate-action.make-whole-components-exceed-cash",
                "Par redemption and accrued income cannot exceed total make-whole consideration.");
            if (blockers.Count > 0)
            {
                return ProjectionComputation.Empty;
            }

            if (penalty > 0m)
            {
                recipe.Add(new CorporateActionEconomicOperationDto(
                    CorporateActionEconomicOperationKindDto.PrepaymentPenaltyIncome,
                    request.SecurityId,
                    Amount: penalty));
                components.Add(new CorporateActionPostingComponentDto(
                    CorporateActionPostingComponentKindDto.PrepaymentPenaltyIncome,
                    penalty,
                    currency));
            }
        }
        else if (request.AccountingBasis != AccountingBasisKindDto.Statutory)
        {
            AddGainLoss(cash - accrued, carrying, currency, components);
        }

        return new ProjectionComputation(
            cash,
            recipe,
            [new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.Dispose,
                request.SecurityId, Quantity: quantity, CarryingAmount: carrying)],
            components,
            true);
    }

    private static ProjectionComputation ProjectConsent(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        AddIf(blockers, request.PolicyInputs.ConsentTermsChanged is null,
            "corporate-action.consent-term-change-assessment-required",
            "Consent processing requires an explicit before/after term-change assessment.");
        AddIf(blockers,
            request.PolicyInputs.ConsentTermsChanged == true &&
            !request.PolicyInputs.ConsentModificationAssessmentApproved,
            "corporate-action.consent-modification-assessment-required",
            "Changed consent terms require an approved modification or extinguishment assessment.");
        AddIf(blockers, request.PolicyInputs.ConsentTermsChanged == true,
            "corporate-action.consent-modification-accounting-required",
            "Materially changed consent terms must be processed by the approved modification or extinguishment workflow.");
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        var cash = request.Economics.GrossCashConsideration ?? 0m;
        if (cash <= 0m)
        {
            return new ProjectionComputation(
                0m,
                [new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.ReferenceDataChange,
                    request.SecurityId, Description: "Consent solicitation without holder payment")],
                [],
                [],
                false);
        }

        return new ProjectionComputation(
            cash,
            [new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.OtherIncome,
                request.SecurityId, Amount: cash)],
            [],
            [
                new CorporateActionPostingComponentDto(CorporateActionPostingComponentKindDto.Cash, cash, currency),
                new CorporateActionPostingComponentDto(CorporateActionPostingComponentKindDto.ConsentIncome, cash, currency)
            ],
            true);
    }

    private static ProjectionComputation ProjectConversion(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        AddIf(blockers,
            request.AccountingBasis == AccountingBasisKindDto.Statutory &&
            !request.PolicyInputs.StatutoryConversionTreatmentApproved,
            "corporate-action.stat-conversion-treatment-required",
            "Statutory debt-to-equity conversion requires an approved treatment.");
        AddIf(blockers,
            request.Economics.GrossCashConsideration is > 0m && request.PolicyInputs.CashRecognition is null,
            "corporate-action.conversion-cash-recognition-required",
            "Conversion cash requires an approved gain or income classification.");
        AddIf(blockers,
            request.Economics.GrossCashConsideration is > 0m &&
            request.PolicyInputs.ApprovedCashRecognitionAmount is null or < 0m,
            "corporate-action.conversion-recognition-amount-required",
            "Conversion cash requires an approved non-negative recognition amount.");
        AddIf(blockers,
            request.Economics.GrossCashConsideration is > 0m &&
            request.PolicyInputs.ApprovedSuccessorBasis is null or < 0m,
            "corporate-action.conversion-successor-basis-required",
            "Conversion cash requires an approved successor basis.");
        var exchange = ProjectBookValueExchange(request with
        {
            Economics = request.Economics with { GrossCashConsideration = null }
        }, currency, blockers, requireAllocatedSuccessors: request.Economics.Successors.Count > 1);
        if (blockers.Count > 0 || request.Economics.GrossCashConsideration is not > 0m)
        {
            return exchange;
        }

        var cash = Round(request.Economics.GrossCashConsideration.Value, currency);
        var carrying = request.Economics.CarryingAmount!.Value;
        var recognition = request.PolicyInputs.ApprovedCashRecognitionAmount!.Value;
        var approvedBasis = request.PolicyInputs.ApprovedSuccessorBasis!.Value;
        AddIf(blockers, recognition > cash,
            "corporate-action.conversion-recognition-exceeds-cash",
            "Approved conversion recognition cannot exceed the cash received.");
        AddIf(blockers, Round(carrying - cash + recognition, currency) != Round(approvedBasis, currency),
            "corporate-action.conversion-basis-reconciliation-invalid",
            "Approved successor basis must reconcile old carrying value, cash received, and recognized amount.");
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        var componentKind = request.PolicyInputs.CashRecognition == CorporateActionCashRecognitionDto.Gain
            ? CorporateActionPostingComponentKindDto.RealizedGain
            : CorporateActionPostingComponentKindDto.InvestmentIncome;
        var components = exchange.PostingComponents
            .Where(static component => component.Kind != CorporateActionPostingComponentKindDto.PurchaseCost)
            .Concat(
            [
                new CorporateActionPostingComponentDto(
                    CorporateActionPostingComponentKindDto.PurchaseCost,
                    approvedBasis,
                    currency),
                new CorporateActionPostingComponentDto(
                    CorporateActionPostingComponentKindDto.Cash,
                    cash,
                    currency)
            ])
            .ToList();
        if (recognition > 0m)
        {
            components.Add(new CorporateActionPostingComponentDto(componentKind, recognition, currency));
        }

        return exchange with
        {
            EventAmount = cash,
            Recipe = exchange.Recipe.Append(new CorporateActionEconomicOperationDto(
                CorporateActionEconomicOperationKindDto.CashFromCorporateAction,
                Amount: cash)).ToArray(),
            LotMutations = AllocateSuccessorBasis(exchange.LotMutations, approvedBasis, currency),
            PostingComponents = components,
            RequiresJournalCandidate = true
        };
    }

    private static ProjectionComputation ProjectCashDividend(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        var cash = ResolveDistributionAmount(request, currency, blockers);
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        return new ProjectionComputation(
            cash,
            [new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.DividendIncome,
                request.SecurityId, Amount: cash)],
            [],
            [
                new CorporateActionPostingComponentDto(CorporateActionPostingComponentKindDto.Cash, cash, currency),
                new CorporateActionPostingComponentDto(CorporateActionPostingComponentKindDto.DividendIncome, cash, currency)
            ],
            true);
    }

    private static ProjectionComputation ProjectStockDividend(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        AddIf(blockers, request.PolicyInputs.StockDividendBasisTreatment is null,
            "corporate-action.stock-dividend-basis-treatment-required",
            "Stock-dividend processing requires an approved basis treatment.");
        AddIf(blockers,
            request.PolicyInputs.StockDividendBasisTreatment ==
            CorporateActionStockDividendBasisTreatmentDto.PolicyDefinedAdjustment,
            "corporate-action.stock-dividend-policy-not-implemented",
            "The selected policy-defined stock-dividend adjustment has no implemented calculator.");
        return ProjectSplit(request, currency, blockers, reverse: false);
    }

    private static ProjectionComputation ProjectDividendReinvestment(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        var dividend = ResolveDistributionAmount(request, currency, blockers);
        var successor = RequireSingleSuccessor(request, CorporateActionSuccessorRoleDto.Successor, blockers,
            allowAnyRole: true);
        var purchasePrice = RequirePositive(request.Economics.PurchasePricePerUnit, "purchase-price-per-unit", blockers);
        if (successor is null || blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        var purchaseCost = Round(successor.Quantity * purchasePrice, currency);
        AddIf(blockers, purchaseCost != dividend, "corporate-action.reinvestment-cash-mismatch",
            "The reinvestment purchase cost must equal the gross dividend amount.");
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        return new ProjectionComputation(
            dividend,
            [
                new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.DividendIncome,
                    request.SecurityId, Amount: dividend),
                new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.Purchase,
                    successor.SecurityId, successor.Role, successor.Quantity, purchaseCost)
            ],
            [new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.Acquire,
                successor.SecurityId, Quantity: successor.Quantity, CarryingAmount: purchaseCost,
                HoldingPeriodTreatment: CorporateActionHoldingPeriodTreatmentDto.NewLot)],
            [
                new CorporateActionPostingComponentDto(CorporateActionPostingComponentKindDto.DividendIncome,
                    dividend, currency),
                new CorporateActionPostingComponentDto(CorporateActionPostingComponentKindDto.PurchaseCost,
                    purchaseCost, currency)
            ],
            true);
    }

    private static ProjectionComputation ProjectScripDividend(
        CorporateActionAccountingProjectionRequest request,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        AddIf(blockers, !request.PolicyInputs.ScripDividendTreatmentApproved,
            "corporate-action.scrip-treatment-required",
            "Scrip processing requires an approved lifecycle and basis treatment.");
        AddIf(blockers,
            request.PolicyInputs.ScripFinalDistributionCaseId is not { } finalDistributionCaseId ||
            finalDistributionCaseId == Guid.Empty,
            "corporate-action.scrip-final-distribution-link-required",
            "Scrip processing requires the linked final-distribution case identity.");
        var scrip = RequireSingleSuccessor(request, CorporateActionSuccessorRoleDto.Scrip, blockers);
        if (scrip is null || blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        return new ProjectionComputation(
            0m,
            [
                new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.TransferIn,
                    scrip.SecurityId, scrip.Role, scrip.Quantity, 0m,
                    LinkedCaseId: request.PolicyInputs.ScripFinalDistributionCaseId)
            ],
            [
                new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.TransferIn,
                    scrip.SecurityId, Quantity: scrip.Quantity, CarryingAmount: 0m,
                    LinkedCaseId: request.PolicyInputs.ScripFinalDistributionCaseId)
            ],
            [],
            false);
    }

    private static ProjectionComputation ProjectExchangeOffer(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        AddIf(blockers, request.PolicyInputs.ExchangeOfferMethod is null,
            "corporate-action.exchange-offer-method-required",
            "Exchange-offer processing requires an approved direct-exchange or sale-and-purchase election.");
        AddIf(blockers, request.PolicyInputs.ExchangeOfferIsMaterial is null,
            "corporate-action.exchange-offer-materiality-required",
            "Exchange-offer processing requires a retained 10% cash-flow materiality assessment.");
        AddIf(blockers,
            request.PolicyInputs.ApprovedTaxClassification is null or CorporateActionTaxClassificationDto.FactDependent or
                CorporateActionTaxClassificationDto.Unknown,
            "corporate-action.exchange-offer-tax-assessment-required",
            "Exchange-offer processing requires an approved tax classification.");
        if (request.PolicyInputs.ExchangeOfferMethod is null || blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        if (request.PolicyInputs.ExchangeOfferMethod == CorporateActionExchangeOfferMethodDto.DirectExchange)
        {
            AddIf(blockers,
                request.Economics.GrossCashConsideration is > 0m && request.PolicyInputs.CashRecognition is null,
                "corporate-action.exchange-offer-cash-recognition-required",
                "Direct exchange cash requires an approved gain or income classification.");
            AddIf(blockers,
                request.Economics.GrossCashConsideration is > 0m &&
                request.PolicyInputs.ApprovedCashRecognitionAmount is null or < 0m,
                "corporate-action.exchange-offer-recognition-amount-required",
                "Direct exchange cash requires an approved non-negative recognition amount.");
            AddIf(blockers,
                request.Economics.GrossCashConsideration is > 0m &&
                request.PolicyInputs.ApprovedSuccessorBasis is null or < 0m,
                "corporate-action.exchange-offer-successor-basis-required",
                "Direct exchange cash requires an approved successor basis.");
            var exchange = ProjectBookValueExchange(request with
            {
                Economics = request.Economics with { GrossCashConsideration = null }
            }, currency, blockers, requireAllocatedSuccessors: request.Economics.Successors.Count > 1);
            if (blockers.Count > 0 || request.Economics.GrossCashConsideration is not > 0m)
            {
                return exchange;
            }

            var cash = Round(request.Economics.GrossCashConsideration.Value, currency);
            var carrying = request.Economics.CarryingAmount!.Value;
            var recognition = request.PolicyInputs.ApprovedCashRecognitionAmount!.Value;
            var approvedBasis = request.PolicyInputs.ApprovedSuccessorBasis!.Value;
            AddIf(blockers, recognition > cash,
                "corporate-action.exchange-offer-recognition-exceeds-cash",
                "Approved direct-exchange recognition cannot exceed cash consideration.");
            AddIf(blockers, Round(carrying - cash + recognition, currency) != Round(approvedBasis, currency),
                "corporate-action.exchange-offer-basis-reconciliation-invalid",
                "Approved successor basis must reconcile old carrying value, cash received, and recognized amount.");
            if (blockers.Count > 0)
            {
                return ProjectionComputation.Empty;
            }

            var cashKind = request.PolicyInputs.CashRecognition == CorporateActionCashRecognitionDto.Gain
                ? CorporateActionPostingComponentKindDto.RealizedGain
                : CorporateActionPostingComponentKindDto.InvestmentIncome;
            var components = exchange.PostingComponents
                .Where(static component => component.Kind != CorporateActionPostingComponentKindDto.PurchaseCost)
                .Concat(
                [
                    new CorporateActionPostingComponentDto(
                        CorporateActionPostingComponentKindDto.PurchaseCost,
                        approvedBasis,
                        currency),
                    new CorporateActionPostingComponentDto(
                        CorporateActionPostingComponentKindDto.Cash,
                        cash,
                        currency)
                ])
                .ToList();
            if (recognition > 0m)
            {
                components.Add(new CorporateActionPostingComponentDto(cashKind, recognition, currency));
            }

            return exchange with
            {
                EventAmount = cash,
                Recipe = exchange.Recipe.Append(new CorporateActionEconomicOperationDto(
                    CorporateActionEconomicOperationKindDto.CashFromCorporateAction, Amount: cash)).ToArray(),
                LotMutations = AllocateSuccessorBasis(exchange.LotMutations, approvedBasis, currency),
                PostingComponents = components,
                RequiresJournalCandidate = true
            };
        }

        return ProjectSaleAndPurchase(request, currency, blockers);
    }

    private static ProjectionComputation ProjectSaleAndPurchase(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        var sale = ProjectDisposition(request, currency, blockers,
            CorporateActionEconomicOperationKindDto.CorporateActionSale);
        var successors = RequireSuccessors(request, blockers);
        AddIf(blockers, successors.Any(successor => successor.FairValue is null),
            "corporate-action.successor-fair-value-required",
            "Sale-and-purchase processing requires the purchase value of every successor.");
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        var purchaseCost = Round(successors.Sum(static successor => successor.FairValue!.Value), currency);
        var recipe = sale.Recipe.Concat(successors.Select(successor => new CorporateActionEconomicOperationDto(
            CorporateActionEconomicOperationKindDto.Purchase,
            successor.SecurityId,
            successor.Role,
            successor.Quantity,
            successor.FairValue))).ToArray();
        var lots = sale.LotMutations.Concat(successors.Select(successor => new CorporateActionLotMutationDto(
            CorporateActionLotMutationKindDto.Acquire,
            successor.SecurityId,
            Quantity: successor.Quantity,
            CarryingAmount: successor.FairValue,
            HoldingPeriodTreatment: CorporateActionHoldingPeriodTreatmentDto.NewLot))).ToArray();
        var components = sale.PostingComponents.Append(new CorporateActionPostingComponentDto(
            CorporateActionPostingComponentKindDto.PurchaseCost,
            purchaseCost,
            currency)).ToArray();
        return sale with
        {
            Recipe = recipe,
            LotMutations = lots,
            PostingComponents = components
        };
    }

    private static ProjectionComputation ProjectDisposition(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers,
        CorporateActionEconomicOperationKindDto operationKind)
    {
        var quantity = RequirePositive(request.Economics.AffectedQuantity, "affected-quantity", blockers);
        var carrying = RequireNonNegative(request.Economics.CarryingAmount, "carrying-amount", blockers);
        var cash = RequireNonNegative(request.Economics.GrossCashConsideration, "gross-cash-consideration", blockers);
        var accrued = request.Economics.AccruedIncome ?? 0m;
        AddIf(blockers, accrued > cash, "corporate-action.accrued-income-exceeds-cash",
            "Accrued income cannot exceed gross cash consideration.");
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        var disposalProceeds = cash - accrued;
        var components = new List<CorporateActionPostingComponentDto>
        {
            new(CorporateActionPostingComponentKindDto.Cash, cash, currency),
            new(CorporateActionPostingComponentKindDto.CarryingValueRelief, carrying, currency)
        };
        var recipe = new List<CorporateActionEconomicOperationDto>
        {
            new(operationKind, request.SecurityId, Quantity: quantity, Amount: disposalProceeds)
        };
        if (accrued > 0m)
        {
            recipe.Add(new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.CouponIncome,
                request.SecurityId, Amount: accrued));
            components.Add(new CorporateActionPostingComponentDto(
                CorporateActionPostingComponentKindDto.AccruedIncome, accrued, currency));
        }

        AddGainLoss(disposalProceeds, carrying, currency, components);
        return new ProjectionComputation(
            cash,
            recipe,
            [new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.Dispose,
                request.SecurityId, Quantity: quantity, CarryingAmount: carrying)],
            components,
            true);
    }

    private static ProjectionComputation ProjectMergerStock(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        AddIf(blockers, request.PolicyInputs.CarryHoldingPeriod is null,
            "corporate-action.merger-holding-period-required",
            "Stock-merger processing requires an approved holding-period instruction.");
        return ProjectBookValueExchange(request, currency, blockers, requireAllocatedSuccessors: false);
    }

    private static ProjectionComputation ProjectMergerMixed(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        AddIf(blockers, request.PolicyInputs.MergerRecognition is null,
            "corporate-action.merger-recognition-required",
            "Mixed merger processing requires an approved gain-recognition model.");
        AddIf(blockers, request.PolicyInputs.CarryHoldingPeriod is null,
            "corporate-action.merger-holding-period-required",
            "Mixed merger processing requires an approved holding-period instruction.");
        var quantity = RequirePositive(request.Economics.AffectedQuantity, "affected-quantity", blockers);
        var carrying = RequireNonNegative(request.Economics.CarryingAmount, "carrying-amount", blockers);
        var cash = RequireNonNegative(request.Economics.GrossCashConsideration, "gross-cash-consideration", blockers);
        var successors = RequireSuccessors(request, blockers);
        ValidateAllocations(successors, required: true, blockers);
        AddIf(blockers, successors.Any(successor => successor.FairValue is null),
            "corporate-action.successor-fair-value-required",
            "Mixed-merger recognition requires fair value for every successor.");
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        var totalConsideration = cash + successors.Sum(static successor => successor.FairValue!.Value);
        var fullGainLoss = totalConsideration - carrying;
        var recognizedGainLoss = request.PolicyInputs.MergerRecognition switch
        {
            CorporateActionMergerRecognitionDto.GainLimitedToCashNoLoss => Math.Min(Math.Max(fullGainLoss, 0m), cash),
            CorporateActionMergerRecognitionDto.FullGainLoss => fullGainLoss,
            _ => 0m
        };
        var successorBasis = request.PolicyInputs.MergerRecognition ==
                             CorporateActionMergerRecognitionDto.FullGainLoss
            ? Round(successors.Sum(static successor => successor.FairValue!.Value), currency)
            : Round(carrying - cash + recognizedGainLoss, currency);
        AddIf(blockers, successorBasis < 0m,
            "corporate-action.merger-successor-basis-invalid",
            "The approved mixed-merger recognition model produced a negative successor basis.");
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }
        var recipe = new List<CorporateActionEconomicOperationDto>
        {
            new(CorporateActionEconomicOperationKindDto.ExchangeOut, request.SecurityId, Quantity: quantity),
            new(CorporateActionEconomicOperationKindDto.CashFromCorporateAction, Amount: cash)
        };
        recipe.AddRange(successors.Select(successor => new CorporateActionEconomicOperationDto(
            CorporateActionEconomicOperationKindDto.ExchangeIn,
            successor.SecurityId,
            successor.Role,
            successor.Quantity,
            successor.FairValue)));
        var lotTemplates = successors.Select(successor => new CorporateActionLotMutationDto(
            CorporateActionLotMutationKindDto.Allocate,
            request.SecurityId,
            successor.SecurityId,
            successor.Quantity,
            0m,
            successor.BookValueAllocationPercent,
            request.PolicyInputs.CarryHoldingPeriod == true
                ? CorporateActionHoldingPeriodTreatmentDto.CarryOver
                : CorporateActionHoldingPeriodTreatmentDto.NewLot)).ToArray();
        var lots = AllocateSourceRelief(
            AllocateSuccessorBasis(lotTemplates, successorBasis, currency),
            quantity,
            carrying,
            currency);
        var components = new List<CorporateActionPostingComponentDto>
        {
            new(CorporateActionPostingComponentKindDto.Cash, cash, currency),
            new(CorporateActionPostingComponentKindDto.CarryingValueRelief, carrying, currency),
            new(CorporateActionPostingComponentKindDto.PurchaseCost, successorBasis, currency)
        };
        AddSignedGainLoss(recognizedGainLoss, currency, components);
        return new ProjectionComputation(cash, recipe, lots, components, true);
    }

    private static ProjectionComputation ProjectPaymentInKind(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        var par = RequirePositive(request.Economics.ParAmount, "par-amount", blockers);
        var rate = RequirePositive(request.Economics.Rate, "pik-rate", blockers);
        var purchasePrice = RequirePositive(
            request.Economics.PurchasePricePerUnit,
            "pik-purchase-price-per-unit",
            blockers);
        var successor = RequireSingleSuccessor(request, CorporateActionSuccessorRoleDto.Successor, blockers,
            allowAnyRole: true);
        if (successor is null || blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        var income = Round(par * rate, currency);
        var purchaseCost = Round(successor.Quantity * purchasePrice, currency);
        AddIf(blockers, income <= 0m, "corporate-action.pik-result-invalid",
            "PIK income must be representable in currency minor units.");
        AddIf(blockers, purchaseCost != income, "corporate-action.pik-price-reconciliation-invalid",
            "The approved PIK issue price and units must reconcile to the non-cash income amount.");
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        return new ProjectionComputation(
            income,
            [
                new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.CouponIncome,
                    request.SecurityId, Amount: income),
                new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.Purchase,
                    successor.SecurityId, successor.Role, successor.Quantity, purchaseCost)
            ],
            [new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.Acquire,
                successor.SecurityId, Quantity: successor.Quantity, CarryingAmount: purchaseCost,
                HoldingPeriodTreatment: CorporateActionHoldingPeriodTreatmentDto.NewLot)],
            [
                new CorporateActionPostingComponentDto(CorporateActionPostingComponentKindDto.AccruedIncome,
                    income, currency),
                new CorporateActionPostingComponentDto(CorporateActionPostingComponentKindDto.PurchaseCost,
                    purchaseCost, currency)
            ],
            true);
    }

    private static ProjectionComputation ProjectReturnOfCapital(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        var distribution = ResolveDistributionAmount(request, currency, blockers);
        var carrying = RequireNonNegative(request.Economics.CarryingAmount, "carrying-amount", blockers);
        AddIf(blockers, distribution > carrying, "corporate-action.return-of-capital-exceeds-basis",
            "Return of capital exceeds available carrying value; excess-distribution policy is unresolved.");
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        return new ProjectionComputation(
            distribution,
            [new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.ReturnOfCapital,
                request.SecurityId, Amount: distribution)],
            [new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.ReduceCarryingValue,
                request.SecurityId, CarryingAmount: distribution,
                HoldingPeriodTreatment: CorporateActionHoldingPeriodTreatmentDto.Unchanged)],
            [
                new CorporateActionPostingComponentDto(CorporateActionPostingComponentKindDto.Cash,
                    distribution, currency),
                new CorporateActionPostingComponentDto(CorporateActionPostingComponentKindDto.ReturnOfCapital,
                    distribution, currency)
            ],
            true);
    }

    private static ProjectionComputation ProjectSplit(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers,
        bool reverse)
    {
        var positionQuantity = RequirePositive(request.Economics.PositionQuantity, "position-quantity", blockers);
        var splitRatio = RequirePositive(request.Economics.SplitRatio, "split-ratio", blockers);
        AddIf(blockers, !reverse && splitRatio <= 1m, "corporate-action.forward-split-ratio-invalid",
            "A forward stock split ratio must be greater than one.");
        AddIf(blockers, reverse && splitRatio >= 1m, "corporate-action.reverse-split-ratio-invalid",
            "A reverse stock split ratio must be between zero and one.");

        if (request.Economics.IdentifierChanged)
        {
            return ProjectBookValueExchange(request with
            {
                Economics = request.Economics with
                {
                    AffectedQuantity = positionQuantity,
                    GrossCashConsideration = null
                }
            }, currency, blockers, requireAllocatedSuccessors: false);
        }

        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        var postQuantity = positionQuantity * splitRatio;
        var hasFractionalResidual = decimal.Truncate(postQuantity) != postQuantity;
        AddIf(blockers,
            hasFractionalResidual &&
            (request.PolicyInputs.FractionalCashInLieuCaseId is not { } cashInLieuCaseId ||
             cashInLieuCaseId == Guid.Empty),
            "corporate-action.split-fractional-residual",
            "The split produces fractional units that require a linked cash-in-lieu case.");
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        return new ProjectionComputation(
            0m,
            [new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.SplitAdjustment,
                request.SecurityId, Quantity: postQuantity, Amount: splitRatio,
                LinkedCaseId: hasFractionalResidual
                    ? request.PolicyInputs.FractionalCashInLieuCaseId
                    : null)],
            [new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.ChangeQuantity,
                request.SecurityId, Quantity: postQuantity, CarryingAmount: request.Economics.CarryingAmount,
                HoldingPeriodTreatment: CorporateActionHoldingPeriodTreatmentDto.Unchanged,
                LinkedCaseId: hasFractionalResidual
                    ? request.PolicyInputs.FractionalCashInLieuCaseId
                    : null,
                BasisAmount: 0m)],
            [],
            false);
    }

    private static ProjectionComputation ProjectRightsDistribution(
        CorporateActionAccountingProjectionRequest request,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        AddIf(blockers, !request.PolicyInputs.RightsZeroValueApproved,
            "corporate-action.rights-zero-value-approval-required",
            "The Clearwater zero-value rights convention requires explicit approval.");
        var right = RequireSingleSuccessor(request, CorporateActionSuccessorRoleDto.Right, blockers);
        AddIf(blockers, right?.FairValue is > 0m,
            "corporate-action.rights-observable-value-review-required",
            "A right with positive observable value cannot use the zero-value Clearwater convention.");
        if (right is null || blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        return new ProjectionComputation(
            0m,
            [new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.TransferIn,
                right.SecurityId, right.Role, right.Quantity, 0m)],
            [new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.TransferIn,
                right.SecurityId, Quantity: right.Quantity, CarryingAmount: 0m)],
            [],
            false);
    }

    private static ProjectionComputation ProjectRightsExercise(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        var rightsQuantity = RequirePositive(request.Economics.AffectedQuantity, "affected-quantity", blockers);
        var underlying = RequireSingleSuccessor(request, CorporateActionSuccessorRoleDto.Underlying, blockers);
        var price = RequirePositive(request.Economics.SubscriptionPricePerUnit,
            "subscription-price-per-unit", blockers);
        var rightsBasis = RequireNonNegative(request.Economics.CarryingAmount,
            "rights-carrying-amount", blockers);
        if (underlying is null || blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        var subscriptionCash = Round(underlying.Quantity * price, currency);
        var purchaseCost = Round(subscriptionCash + rightsBasis, currency);
        return new ProjectionComputation(
            purchaseCost,
            [
                new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.TransferOut,
                    request.SecurityId, Quantity: rightsQuantity, Amount: 0m),
                new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.Purchase,
                    underlying.SecurityId, underlying.Role, underlying.Quantity, purchaseCost)
            ],
            [
                new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.TransferOut,
                    request.SecurityId, Quantity: rightsQuantity, CarryingAmount: rightsBasis),
                new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.Acquire,
                    underlying.SecurityId, Quantity: underlying.Quantity, CarryingAmount: purchaseCost,
                    HoldingPeriodTreatment: CorporateActionHoldingPeriodTreatmentDto.NewLot)
            ],
            BuildRightsExerciseComponents(subscriptionCash, rightsBasis, purchaseCost, currency),
            true);
    }

    private static ProjectionComputation ProjectRightsExpiration(
        CorporateActionAccountingProjectionRequest request,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        var quantity = RequirePositive(request.Economics.AffectedQuantity, "affected-quantity", blockers);
        AddIf(blockers, request.Economics.CarryingAmount is > 0m,
            "corporate-action.rights-expiration-basis-policy-required",
            "Rights expiration with non-zero carrying value requires an approved basis and tax treatment.");
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        return new ProjectionComputation(
            0m,
            [new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.TransferOut,
                request.SecurityId, Quantity: quantity, Amount: 0m)],
            [new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.TransferOut,
                request.SecurityId, Quantity: quantity, CarryingAmount: 0m)],
            [],
            false);
    }

    private static ProjectionComputation ProjectSpinOff(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        AddIf(blockers, request.PolicyInputs.SpinOffTaxTreatment is null,
            "corporate-action.spin-off-tax-treatment-required",
            "Spin-off processing requires an approved taxable or non-taxable treatment.");
        if (request.PolicyInputs.SpinOffTaxTreatment is null)
        {
            return ProjectionComputation.Empty;
        }

        if (request.PolicyInputs.SpinOffTaxTreatment == CorporateActionSpinOffTaxTreatmentDto.NonTaxableBasisAllocation)
        {
            AddIf(blockers,
                request.Economics.Successors.Count(successor =>
                    successor.SecurityId == request.SecurityId &&
                    successor.Role == CorporateActionSuccessorRoleDto.Successor) != 1,
                "corporate-action.spin-off-retained-parent-required",
                "Non-taxable spin-off allocation must retain the parent security as one successor.");
            AddIf(blockers,
                request.Economics.Successors.All(static successor =>
                    successor.Role != CorporateActionSuccessorRoleDto.Child),
                "corporate-action.spin-off-child-required",
                "Non-taxable spin-off allocation requires at least one child security.");
            return ProjectBookValueExchange(request, currency, blockers, requireAllocatedSuccessors: true);
        }

        var dividend = ResolveDistributionAmount(request, currency, blockers);
        var children = RequireSuccessors(request, blockers);
        AddIf(blockers, children.Any(child => child.FairValue is null),
            "corporate-action.spin-off-child-value-required",
            "Taxable spin-off processing requires the purchase value of every child security.");
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        var purchaseCost = Round(children.Sum(static child => child.FairValue!.Value), currency);
        AddIf(blockers, purchaseCost != dividend, "corporate-action.spin-off-dividend-purchase-mismatch",
            "Taxable spin-off child purchase cost must equal the recognized dividend amount.");
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        return new ProjectionComputation(
            dividend,
            new[]
            {
                new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.DividendIncome,
                    request.SecurityId, Amount: dividend)
            }
                .Concat(children.Select(child => new CorporateActionEconomicOperationDto(
                    CorporateActionEconomicOperationKindDto.Purchase,
                    child.SecurityId,
                    child.Role,
                    child.Quantity,
                    child.FairValue)))
                .ToArray(),
            children.Select(child => new CorporateActionLotMutationDto(
                CorporateActionLotMutationKindDto.Acquire,
                child.SecurityId,
                Quantity: child.Quantity,
                CarryingAmount: child.FairValue,
                HoldingPeriodTreatment: CorporateActionHoldingPeriodTreatmentDto.NewLot)).ToArray(),
            [
                new CorporateActionPostingComponentDto(CorporateActionPostingComponentKindDto.DividendIncome,
                    dividend, currency),
                new CorporateActionPostingComponentDto(CorporateActionPostingComponentKindDto.PurchaseCost,
                    purchaseCost, currency)
            ],
            true);
    }

    private static ProjectionComputation ProjectTender(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        if (request.AccountingBasis != AccountingBasisKindDto.Statutory)
        {
            return ProjectDisposition(request, currency, blockers,
                CorporateActionEconomicOperationKindDto.CorporateActionSale);
        }

        var quantity = RequirePositive(request.Economics.AffectedQuantity, "affected-quantity", blockers);
        var carrying = RequireNonNegative(request.Economics.CarryingAmount, "carrying-amount", blockers);
        var par = RequirePositive(request.Economics.ParAmount, "par-amount", blockers);
        var cash = RequirePositive(request.Economics.GrossCashConsideration, "gross-cash-consideration", blockers);
        var allocation = request.PolicyInputs.StatutoryTenderIncomeAllocationPercent;
        AddIf(blockers, allocation is < 0m or > 1m, "corporate-action.tender-allocation-invalid",
            "Statutory tender income allocation must be between zero and one.");
        if (blockers.Count > 0)
        {
            return ProjectionComputation.Empty;
        }

        decimal income;
        decimal redemption;
        if (allocation.HasValue)
        {
            income = Round(cash * allocation.Value, currency);
            redemption = cash - income;
        }
        else if (cash > par)
        {
            redemption = par;
            income = cash - par;
        }
        else
        {
            redemption = cash;
            income = 0m;
        }

        var recipe = new List<CorporateActionEconomicOperationDto>
        {
            new(CorporateActionEconomicOperationKindDto.Redemption, request.SecurityId,
                Quantity: quantity, Amount: redemption)
        };
        var components = new List<CorporateActionPostingComponentDto>
        {
            new(CorporateActionPostingComponentKindDto.Cash, cash, currency),
            new(CorporateActionPostingComponentKindDto.CarryingValueRelief, carrying, currency),
            new(CorporateActionPostingComponentKindDto.RedemptionPrincipal, redemption, currency)
        };
        if (income > 0m)
        {
            recipe.Add(new CorporateActionEconomicOperationDto(CorporateActionEconomicOperationKindDto.OtherIncome,
                request.SecurityId, Amount: income));
            components.Add(new CorporateActionPostingComponentDto(
                CorporateActionPostingComponentKindDto.InvestmentIncome, income, currency));
        }

        return new ProjectionComputation(
            cash,
            recipe,
            [new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.Dispose,
                request.SecurityId, Quantity: quantity, CarryingAmount: carrying)],
            components,
            true);
    }

    private static decimal ResolveDistributionAmount(
        CorporateActionAccountingProjectionRequest request,
        string currency,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        var supplied = request.Economics.GrossCashConsideration;
        var calculated = request.Economics.PositionQuantity is > 0m &&
                         request.Economics.CashRatePerUnit is > 0m
            ? Round(request.Economics.PositionQuantity.Value * request.Economics.CashRatePerUnit.Value, currency)
            : (decimal?)null;
        AddIf(blockers, supplied is not > 0m && calculated is not > 0m,
            "corporate-action.distribution-amount-required",
            "A positive gross distribution or positive quantity and rate per unit is required.");
        AddIf(blockers, supplied is > 0m && calculated.HasValue && Round(supplied.Value, currency) != calculated.Value,
            "corporate-action.distribution-amount-mismatch",
            "Supplied gross distribution does not match quantity multiplied by the rate per unit.");
        return Round(supplied ?? calculated ?? 0m, currency);
    }

    private static IReadOnlyList<CorporateActionSuccessorAllocationDto> RequireSuccessors(
        CorporateActionAccountingProjectionRequest request,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        AddIf(blockers, request.Economics.Successors.Count == 0,
            "corporate-action.successor-required",
            "At least one successor security is required.");
        return request.Economics.Successors;
    }

    private static CorporateActionSuccessorAllocationDto? RequireSingleSuccessor(
        CorporateActionAccountingProjectionRequest request,
        CorporateActionSuccessorRoleDto role,
        List<CorporateActionProjectionBlockerDto> blockers,
        bool allowAnyRole = false)
    {
        var matches = allowAnyRole
            ? request.Economics.Successors
            : request.Economics.Successors.Where(successor => successor.Role == role).ToArray();
        AddIf(blockers, matches.Count != 1,
            "corporate-action.single-successor-required",
            allowAnyRole
                ? "Exactly one successor security is required."
                : $"Exactly one {role} successor security is required.");
        return matches.Count == 1 ? matches[0] : null;
    }

    private static void ValidateAllocations(
        IReadOnlyList<CorporateActionSuccessorAllocationDto> successors,
        bool required,
        List<CorporateActionProjectionBlockerDto> blockers)
    {
        if (!required)
        {
            return;
        }

        AddIf(blockers, successors.Any(static successor => successor.BookValueAllocationPercent is null),
            "corporate-action.successor-allocation-required",
            "Every successor requires an approved book-value allocation percentage.");
        if (successors.All(static successor => successor.BookValueAllocationPercent.HasValue))
        {
            var total = successors.Sum(static successor => successor.BookValueAllocationPercent!.Value);
            AddIf(blockers, total != 1m, "corporate-action.successor-allocation-total-invalid",
                "Successor book-value allocation percentages must total exactly one.");
        }
    }

    private static decimal Allocate(decimal carrying, decimal? allocation, int successorCount)
        => allocation.HasValue
            ? carrying * allocation.Value
            : successorCount == 1
                ? carrying
                : 0m;

    private static IReadOnlyList<CorporateActionPostingComponentDto> CarryingValueTransferComponents(
        decimal carrying,
        string currency)
        => carrying > 0m
            ?
            [
                new CorporateActionPostingComponentDto(CorporateActionPostingComponentKindDto.CarryingValueRelief,
                    carrying, currency),
                new CorporateActionPostingComponentDto(CorporateActionPostingComponentKindDto.PurchaseCost,
                    carrying, currency)
            ]
            : [];

    private static IReadOnlyList<CorporateActionPostingComponentDto> BuildRightsExerciseComponents(
        decimal subscriptionCash,
        decimal rightsBasis,
        decimal purchaseCost,
        string currency)
    {
        var components = new List<CorporateActionPostingComponentDto>
        {
            new(CorporateActionPostingComponentKindDto.Cash, subscriptionCash, currency),
            new(CorporateActionPostingComponentKindDto.PurchaseCost, purchaseCost, currency)
        };
        if (rightsBasis > 0m)
        {
            components.Add(new CorporateActionPostingComponentDto(
                CorporateActionPostingComponentKindDto.CarryingValueRelief,
                rightsBasis,
                currency));
        }

        return components;
    }

    private static IReadOnlyList<CorporateActionLotMutationDto> AllocateSuccessorBasis(
        IReadOnlyList<CorporateActionLotMutationDto> lotMutations,
        decimal totalBasis,
        string currency)
    {
        if (lotMutations.Count == 0)
        {
            return [];
        }

        var allocated = new CorporateActionLotMutationDto[lotMutations.Count];
        var running = 0m;
        for (var index = 0; index < lotMutations.Count; index++)
        {
            var amount = index == lotMutations.Count - 1
                ? totalBasis - running
                : Round(
                    totalBasis * (lotMutations[index].AllocationPercent ?? (1m / lotMutations.Count)),
                    currency);
            running += amount;
            allocated[index] = lotMutations[index] with { CarryingAmount = amount };
        }

        return allocated;
    }

    private static void AddGainLoss(
        decimal proceeds,
        decimal carrying,
        string currency,
        ICollection<CorporateActionPostingComponentDto> components)
        => AddSignedGainLoss(proceeds - carrying, currency, components);

    private static void AddSignedGainLoss(
        decimal gainLoss,
        string currency,
        ICollection<CorporateActionPostingComponentDto> components)
    {
        if (gainLoss > 0m)
        {
            components.Add(new CorporateActionPostingComponentDto(
                CorporateActionPostingComponentKindDto.RealizedGain,
                gainLoss,
                currency));
        }
        else if (gainLoss < 0m)
        {
            components.Add(new CorporateActionPostingComponentDto(
                CorporateActionPostingComponentKindDto.RealizedLoss,
                Math.Abs(gainLoss),
                currency));
        }
    }

    private static decimal RequirePositive(
        decimal? value,
        string field,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        if (value is > 0m)
        {
            return value.Value;
        }

        blockers.Add(new CorporateActionProjectionBlockerDto(
            $"corporate-action.{field}-required",
            $"A positive {field.Replace('-', ' ')} is required."));
        return 0m;
    }

    private static decimal RequireNonNegative(
        decimal? value,
        string field,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        if (value is >= 0m)
        {
            return value.Value;
        }

        blockers.Add(new CorporateActionProjectionBlockerDto(
            $"corporate-action.{field}-required",
            $"A non-negative {field.Replace('-', ' ')} is required."));
        return 0m;
    }

    private static void ValidateNonNegative(
        decimal? value,
        string field,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        if (value < 0m)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.negative-economic-input",
                $"{field} cannot be negative."));
        }
    }

    private static void ValidateAccountingScope(
        CorporateActionAccountingProjectionScopeDto? scope,
        AccountingBasisKindDto accountingBasis,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        AddIf(blockers, scope is null, "corporate-action.accounting-scope-required",
            "Tenant, company, fund, ledger book, period, period version, and jurisdiction are required.");
        if (scope is null)
        {
            return;
        }

        AddIf(blockers,
            string.IsNullOrWhiteSpace(scope.TenantId) ||
            string.IsNullOrWhiteSpace(scope.CompanyId) ||
            string.IsNullOrWhiteSpace(scope.FundProfileId) ||
            string.IsNullOrWhiteSpace(scope.Jurisdiction),
            "corporate-action.accounting-scope-incomplete",
            "Tenant, company, fund profile, and jurisdiction must be non-empty.");
        AddIf(blockers, scope.LedgerBookId == Guid.Empty || scope.PeriodId == Guid.Empty,
            "corporate-action.accounting-scope-identity-invalid",
            "Ledger book and accounting period identities are required.");
        AddIf(blockers, scope.ExpectedPeriodVersion <= 0,
            "corporate-action.period-version-invalid",
            "The expected accounting-period version must be positive.");
        AddIf(blockers, !Enum.IsDefined(accountingBasis),
            "corporate-action.accounting-basis-invalid",
            "A defined accounting basis is required.");
    }

    private static void ValidateEvidenceManifest(
        CorporateActionAccountingProjectionRequest request,
        IReadOnlyList<CorporateActionProjectionEvidenceDependencyDto> evidence,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        foreach (var item in evidence)
        {
            AddIf(blockers,
                string.IsNullOrWhiteSpace(item.EvidenceId) ||
                string.IsNullOrWhiteSpace(item.EvidenceUri) ||
                !Uri.TryCreate(item.EvidenceUri, UriKind.Absolute, out _) ||
                string.IsNullOrWhiteSpace(item.SubjectType) ||
                string.IsNullOrWhiteSpace(item.SubjectId) ||
                item.EvidenceVersion <= 0 ||
                !Sha256Digest.IsCanonical(item.ContentHashSha256),
                "corporate-action.evidence-manifest-entry-invalid",
                "Every evidence dependency requires a stable id, absolute URI, canonical hash, positive version, role, and subject id.");
        }

        var duplicate = evidence
            .GroupBy(static item => (item.Role, item.EvidenceId, item.EvidenceVersion))
            .Any(static group => group.Count() > 1);
        AddIf(blockers, duplicate, "corporate-action.evidence-manifest-duplicate",
            "Evidence dependencies must be unique by role, evidence id, and version.");

        RequireEvidenceRole(evidence, CorporateActionProjectionEvidenceRoleDto.SourceEvent, blockers);
        RequireEvidenceRole(evidence, CorporateActionProjectionEvidenceRoleDto.PositionSnapshot, blockers);
        RequireEvidenceRole(evidence, CorporateActionProjectionEvidenceRoleDto.LotSnapshot, blockers);
        RequireEvidenceRole(evidence, CorporateActionProjectionEvidenceRoleDto.PolicyDecision, blockers);
        if (request.ElectionVersion.HasValue)
        {
            RequireEvidenceRole(evidence, CorporateActionProjectionEvidenceRoleDto.Election, blockers);
        }

        var sourceSubject = request.SourceCorporateActionId.ToString("D");
        AddIf(blockers,
            !evidence.Any(item =>
                item.Role == CorporateActionProjectionEvidenceRoleDto.SourceEvent &&
                string.Equals(item.SubjectId, sourceSubject, StringComparison.OrdinalIgnoreCase) &&
                item.EvidenceVersion == request.SourceEventVersion &&
                Sha256Digest.FixedEquals(item.ContentHashSha256, request.SourceContentHash)),
            "corporate-action.source-evidence-binding-mismatch",
            "Source-event evidence must bind the exact corporate action id and source content hash.");

        var snapshotSubject = request.PositionSnapshotId?.ToString("D");
        AddIf(blockers,
            !evidence.Any(item =>
                item.Role == CorporateActionProjectionEvidenceRoleDto.PositionSnapshot &&
                string.Equals(item.SubjectId, snapshotSubject, StringComparison.OrdinalIgnoreCase) &&
                item.EvidenceVersion == request.PositionVersion),
            "corporate-action.position-evidence-binding-mismatch",
            "Position evidence must bind the exact authoritative position snapshot.");

        var lotSnapshotSubject = request.LotSnapshotId?.ToString("D");
        AddIf(blockers,
            !evidence.Any(item =>
                item.Role == CorporateActionProjectionEvidenceRoleDto.LotSnapshot &&
                string.Equals(item.SubjectId, lotSnapshotSubject, StringComparison.OrdinalIgnoreCase) &&
                item.EvidenceVersion == request.LotSnapshotVersion),
            "corporate-action.lot-evidence-binding-mismatch",
            "Lot evidence must bind the exact authoritative lot snapshot and version.");

        var policySubject = request.PolicyDecisionId?.ToString("D");
        AddIf(blockers,
            !evidence.Any(item =>
                item.Role == CorporateActionProjectionEvidenceRoleDto.PolicyDecision &&
                string.Equals(item.SubjectId, policySubject, StringComparison.OrdinalIgnoreCase) &&
                item.EvidenceVersion == request.PolicyDecisionVersion),
            "corporate-action.policy-evidence-binding-mismatch",
            "Policy evidence must bind the exact basis-specific decision and version.");

        if (request.ElectionVersion.HasValue)
        {
            var electionSubject = request.ElectionId?.ToString("D");
            AddIf(blockers,
                !evidence.Any(item =>
                    item.Role == CorporateActionProjectionEvidenceRoleDto.Election &&
                    string.Equals(item.SubjectId, electionSubject, StringComparison.OrdinalIgnoreCase) &&
                    item.EvidenceVersion == request.ElectionVersion),
                "corporate-action.election-evidence-binding-mismatch",
                "Election evidence must bind the exact election and version.");
        }
    }

    private static void RequireEvidenceRole(
        IReadOnlyList<CorporateActionProjectionEvidenceDependencyDto> evidence,
        CorporateActionProjectionEvidenceRoleDto role,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        AddIf(blockers, evidence.All(item => item.Role != role),
            $"corporate-action.{role.ToString().ToLowerInvariant()}-evidence-required",
            $"Accepted {role} evidence is required for this projection.");
    }

    private static void AddIf(
        ICollection<CorporateActionProjectionBlockerDto> blockers,
        bool condition,
        string code,
        string message)
    {
        if (condition)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(code, message));
        }
    }

}
