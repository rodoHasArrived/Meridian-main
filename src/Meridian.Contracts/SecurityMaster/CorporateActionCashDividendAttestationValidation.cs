using System.Globalization;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Write-boundary predicates shared by preflight and native cash-dividend persistence.</summary>
public static class CorporateActionCashDividendAttestationValidation
{
    public const string ExcludedProblemCode = "cash_dividend_attestation_excluded";

    public static string RelatedEventKey(Guid securityId, DateOnly exDate) =>
        $"security:{securityId:D}:regular-dividend:{exDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    public static void EnsureEligibility(
        CorporateActionProcessingCaseDto processingCase,
        CorporateActionCaseAccountingProjectionDto projection,
        CorporateActionCashDividendEligibilityDto eligibility)
    {
        ArgumentNullException.ThrowIfNull(processingCase);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(eligibility);
        CorporateActionCaseAccountingPolicy.EnsureExactAccountingScope(processingCase.Scope);
        CorporateActionCaseAccountingPolicy.EnsureProjectionMatchesCaseScope(projection, processingCase.Scope);
        CorporateActionCaseAccountingPolicy.EnsureBalanced(projection);
        CorporateActionCaseAccountingPolicy.EnsurePolicyCoverage(projection);
        CorporateActionCaseAccountingPolicy.EnsureLotResolution(projection);
        EnsureAccountRoleBinding(eligibility.AccountRoleBinding, projection);
        var source = processingCase.SourceSnapshot;
        var action = source?.ProposedAction;
        if (source is null || action is null || processingCase.SecurityId == Guid.Empty
            || action.SecurityId != processingCase.SecurityId
            || CorporateActionEventTypes.Normalize(action.EventType) != CorporateActionEventTypes.Dividend
            || action.SupersedesCorpActId is not null
            || action.LifecycleState is not (null or CorporateActionLifecycleStates.Confirmed)
            || action.ExDate == DateOnly.MinValue || action.PayDate is null || action.PayDate < action.ExDate
            || action.RecordDate is { } recordDate && (recordDate < action.ExDate || recordDate > action.PayDate)
            || HasPayload(action.Payload) || action.SplitRatio is not null || action.NewSecurityId is not null
            || action.DistributionRatio is not null || action.AcquirerSecurityId is not null || action.ExchangeRatio is not null
            || action.SubscriptionPricePerShare is not null || action.RightsPerShare is not null
            || action.RedemptionPricePercentOfPar is not null
            || action.DividendPerShare is not > 0m || projection.EffectiveDate != action.ExDate
            || !Same(action.Currency, "USD") || !Same(projection.Currency, "USD")
            || !Same(processingCase.Scope.FunctionalCurrency, "USD")
            || source.ProviderIdentity.ReleaseStatus != CorporateActionProviderReleaseStatusDto.AcceptanceEligible)
            throw Excluded("Only a confirmed, uncorrected regular USD cash dividend from an acceptance-eligible source is permitted.");
        if (eligibility.SecurityId != processingCase.SecurityId || eligibility.ExDate != action.ExDate
            || eligibility.DividendPerShare != action.DividendPerShare
            || eligibility.RelatedEventKey != RelatedEventKey(processingCase.SecurityId, action.ExDate)
            || eligibility.ProviderId != source.ProviderIdentity.ProviderId
            || eligibility.SourceEventId != source.ProviderIdentity.SourceEventId
            || eligibility.SourceEventVersion != source.ProviderIdentity.SourceEventVersion
            || !Sha256Digest.FixedEquals(eligibility.SourceEvidenceHash, source.ProviderIdentity.EvidenceHash ?? "")
            || string.IsNullOrWhiteSpace(eligibility.ProviderId) || string.IsNullOrWhiteSpace(eligibility.SourceEventId)
            || string.IsNullOrWhiteSpace(eligibility.SourceEventVersion)
            || !Sha256Digest.IsCanonical(eligibility.SourceEvidenceHash))
            throw Excluded("The complete-event entitlement does not match this case's exact retained source identity.");
        if (eligibility.GrossImpactUsd != projection.TotalDebits || eligibility.GrossImpactUsd <= 0m
            || eligibility.WholeEventGrossUsd < eligibility.GrossImpactUsd
            || eligibility.WholeEventGrossUsd > CorporateActionCashDividendAttestationPolicy.RelatedEventGrossLimitUsd
            || decimal.Round(eligibility.GrossImpactUsd, 2) != eligibility.GrossImpactUsd
            || eligibility.BudgetMonth != new DateOnly(action.ExDate.Year, action.ExDate.Month, 1))
            throw Excluded("The gross related-event entitlement or calendar-month budget basis is unavailable or outside policy.");
        if (!Sha256Digest.IsCanonical(eligibility.EntitlementSnapshotHash)
            || string.IsNullOrWhiteSpace(eligibility.EntitlementSnapshotJson)
            || !Sha256Digest.FixedEquals(eligibility.EntitlementSnapshotHash,
                Sha256Digest.ComputeUtf8(eligibility.EntitlementSnapshotJson)))
            throw Excluded("The retained complete-book entitlement snapshot failed its digest binding.");
    }

    public static void EnsureAccountRoleBinding(CashDividendAccountRoleBindingDto? binding,
        CorporateActionCaseAccountingProjectionDto projection)
    {
        if (binding is not { SchemaVersion: 1 } || binding.LedgerBookId != projection.LedgerBookId
            || string.IsNullOrWhiteSpace(binding.DividendReceivableAccount)
            || string.IsNullOrWhiteSpace(binding.DividendIncomeAccount)
            || string.IsNullOrWhiteSpace(binding.DividendReceivableAccountName)
            || string.IsNullOrWhiteSpace(binding.DividendIncomeAccountName)
            || binding.DividendReceivableAccount == binding.DividendIncomeAccount
            || binding.RulePackId != projection.RulePackId || binding.RulePackVersion != projection.RulePackVersion
            || binding.SelectedRuleId != projection.SelectedRuleId
            || binding.SelectedRuleVersion != projection.SelectedRuleVersion)
            throw Excluded("The explicit dividend-receivable/income binding must match this book's exact promoted accounting rule.");
    }

    public static void EnsureSameAccountRoleBinding(CashDividendAccountRoleBindingDto? declarationBinding,
        CashDividendAccountRoleBindingDto? retainedBinding)
    {
        if (declarationBinding is null || retainedBinding is null || declarationBinding != retainedBinding)
            throw Excluded("The confirmed accounting role binding differs from the current declaration or retained entitlement.");
    }

    private static bool Same(string? left, string right) =>
        string.Equals(left?.Trim(), right, StringComparison.OrdinalIgnoreCase);

    private static bool HasPayload(System.Text.Json.JsonElement? payload) =>
        payload is { } value && value.ValueKind is not (System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined)
        && (value.ValueKind != System.Text.Json.JsonValueKind.Object || value.EnumerateObject().Any());

    private static CorporateActionOperationException Excluded(string message) => new(ExcludedProblemCode, message);
}
