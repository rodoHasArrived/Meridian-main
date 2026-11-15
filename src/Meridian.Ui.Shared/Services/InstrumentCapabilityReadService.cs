using System.Text.Json;
using Meridian.Application.SecurityMaster.Validation;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Passive, selected-version diagnostics. Cash-flow checks use the service's explicit no-retention
/// path; valuation, accounting and case operations stay under review without their execution context.
/// </summary>
public sealed class InstrumentCapabilityReadService(
    ISecurityMasterQueryService securities,
    ISecurityMasterCashFlowService? cashFlows = null,
    AssetClassValidatorRegistry? validators = null,
    TimeProvider? clock = null,
    ILogger<InstrumentCapabilityReadService>? logger = null)
{
    public const int MaximumBatchSize = 200;
    private readonly AssetClassValidatorRegistry _validators = validators ?? AssetClassValidatorRegistry.CreateDefault();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger<InstrumentCapabilityReadService> _logger = logger ?? NullLogger<InstrumentCapabilityReadService>.Instance;

    public async Task<InstrumentCapabilityBatchDto> GetBatchAsync(IReadOnlyCollection<Guid> securityIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(securityIds);
        if (securityIds.Count is 0 or > MaximumBatchSize || securityIds.Contains(Guid.Empty))
            throw new ArgumentException($"Select between 1 and {MaximumBatchSize} nonempty security identifiers.", nameof(securityIds));
        var asOf = _clock.GetUtcNow();
        var assessments = new List<InstrumentCapabilitiesDto>();
        // Bounded serial I/O avoids a batch multiplying database/provider load. The browser chunks
        // larger observed holdings sets and permits at most two batches in flight.
        foreach (var id in securityIds.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var security = await securities.GetByIdAsync(id, ct).ConfigureAwait(false);
                assessments.Add(security is null
                    ? Unavailable(id, asOf, "SecurityIdentityUnavailable", "The selected Security Master identity is unavailable.")
                    : new(id, security.Version, asOf, await AssessSelectedAsync(security, asOf, ct: ct).ConfigureAwait(false)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Instrument capability evaluation failed for security {SecurityId}", id);
                assessments.Add(Unavailable(id, asOf, "CapabilityEvaluationUnavailable", "Eligibility could not be evaluated; reload or inspect the service diagnostics."));
            }
        }
        return new(asOf, assessments);
    }

    public async Task<IReadOnlyList<InstrumentCapabilityAssessmentDto>> AssessSelectedAsync(
        SecurityDetailDto security, DateTimeOffset asOf, SecurityValidationReportDto? validation = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(security);
        validation ??= ValidateTerms(security, asOf);
        var detailPath = $"/accounting/security-master?securityId={security.SecurityId:D}";
        InstrumentCapabilityReasonDto Reason(string code, string reason)
            => new(code, reason, code == RateUnitNormalizer.UndeclaredCode
                ? $"/accounting/security-master?rateRepair=queue&securityId={security.SecurityId:D}" : detailPath);
        InstrumentCapabilityAssessmentDto Row(string key, string label, string operation, string status,
            string evaluation, IReadOnlyList<InstrumentCapabilityReasonDto> reasons, bool applicable = true)
            => new(key, label, operation, applicable ? "Applicable" : "NotApplicable", status, evaluation,
                security.Version, asOf, key switch
                {
                    "terms" => "Economic term validation on the selected security definition.",
                    "cash-flows" => "Base-scenario cash flows for the assigned source and selected security definition. External schedules require their own provider evidence; posting requires its own approval and period checks.",
                    "accrual" => "Recurring Security Master fixed-income accrual. Loan servicing has its own accrual workflow. Book, lot, period and journal-template context are not supplied.",
                    "valuation" => "Valuation with a selected model, date and retained market inputs; those inputs are not supplied.",
                    "corporate-actions" => "Processing a retained corporate-action case; case context is not supplied.",
                    _ => "Contractual redemption against an actual event, holding and book; that context is not supplied."
                }, reasons);

        var termReasons = validation.Issues.Where(static issue => issue.Severity >= SecurityValidationSeverityDto.Warning)
            .Select(issue => Reason(issue.Code, issue.Message)).ToArray();
        var rows = new List<InstrumentCapabilityAssessmentDto>
        {
            Row("terms", "Terms", "EconomicTermsValidation",
                validation.HasBlockingIssues ? "Blocked" : termReasons.Length > 0 ? "Review" : "Ready", "Complete", termReasons)
        };
        var modeled = SecurityAssetClassCatalog.TryGet(security.AssetClass, out var descriptor);
        descriptor ??= SecurityAssetClassCatalog.GetOrDefault(security.AssetClass);
        SecurityCashFlowSourceDto? source = null;
        SecurityCashFlowBlockerDto? blocker = null;
        var evaluated = false;
        if (cashFlows is not null)
        {
            try
            {
                source = await cashFlows.GetCashFlowSourceAsync(security.SecurityId, ct).ConfigureAwait(false);
                // The selected-terms calculation preflight cannot evaluate external schedules.
                // Keep their provider/maintained evidence outside the calculated terms reader.
                if (source?.SourceKind is StructuredCashFlowSourceKind.CalculatedBullet or StructuredCashFlowSourceKind.CalculatedSinker)
                    blocker = await cashFlows.GetProjectionBlockerAsync(security,
                        new BondCalculationContext(asOf, SecurityVersion: security.Version), ct).ConfigureAwait(false);
                evaluated = true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Passive cash-flow eligibility failed for security {SecurityId} version {Version}", security.SecurityId, security.Version);
            }
        }

        var externalSource = source?.SourceKind is StructuredCashFlowSourceKind.MIAC
            or StructuredCashFlowSourceKind.MoodysAnalytics or StructuredCashFlowSourceKind.ClientProvided;
        var unknownSource = source is not null && !externalSource
            && source.SourceKind is not (StructuredCashFlowSourceKind.CalculatedBullet or StructuredCashFlowSourceKind.CalculatedSinker);
        var applicableFlows = source is not null || !modeled || descriptor.CashFlowBehavior != SecurityCashFlowBehavior.None;
        var flowReasons = !applicableFlows ? Array.Empty<InstrumentCapabilityReasonDto>()
            : !evaluated ? [Reason("CashFlowEvaluationUnavailable", "The passive cash-flow evaluator is unavailable on this node.")]
            : source is null ? [Reason("CashFlowSourceMissing", "Assign a supported cash-flow source before calculating this security.")]
            : unknownSource ? [Reason("CashFlowSourceEvaluationUnavailable", "The assigned source kind is not recognized on this node; no calculated or external schedule readiness was inferred.")]
            : externalSource ? [Reason("ExternalScheduleContextRequired", "Inspect the assigned provider or maintained schedule, its freshness and retained source evidence. The calculated-terms preflight does not evaluate external schedules.")]
            : blocker is not null ? new[] { Reason(blocker.Code, blocker.Reason) }
            : [];
        rows.Add(Row("cash-flows", "Cash flows", externalSource ? "ExternalCashFlowScheduleReview" : "BaseCashFlowCalculation",
            !applicableFlows || !evaluated || externalSource || unknownSource ? "Review" : flowReasons.Length > 0 ? "Blocked" : "Ready",
            (!evaluated || unknownSource) && applicableFlows ? "Unavailable" : externalSource ? "RequiresContext" : "Complete", flowReasons, applicableFlows));

        var classBlocker = RecurringFixedIncomeAccrualEligibility.GetClassBlocker(security, security.DisplayName);
        var accrualSourceReason = externalSource ? Reason("AccrualCalculatedSourceRequired",
            "Recurring fixed-income accrual requires a supported calculated schedule with retained fixed-rate interest evidence; the assigned external schedule is outside this operation.") : null;
        var flowBlocked = evaluated && !externalSource && !unknownSource && flowReasons.Length > 0;
        var accrualReasons = classBlocker is not null ? new[] { Reason(classBlocker.Code, classBlocker.Reason) }
            : accrualSourceReason is not null ? [accrualSourceReason]
            : flowReasons.Length > 0 ? flowReasons
            : [Reason("AccrualBookContextRequired", "Select a book position, currency and accrual window to verify dated lots, source freshness, period posture and an approved journal template.")];
        rows.Add(Row("accrual", "Accrual", "RecurringFixedIncomeAccrual",
            classBlocker is not null || accrualSourceReason is not null || flowBlocked ? "Blocked" : "Review",
            classBlocker is not null || accrualSourceReason is not null || flowBlocked ? "Complete"
                : !evaluated || unknownSource ? "Unavailable" : "RequiresContext", accrualReasons));
        rows.Add(Row("valuation", "Valuation", "SelectedModelValuation", "Review", "RequiresContext",
            [Reason("ValuationContextRequired", "Choose the valuation date, account and model inputs; a pricing declaration does not prove a usable quote or pinned curves.")]));
        rows.Add(Row("corporate-actions", "Corporate actions", "CorporateActionCaseProcessing", "Review", "RequiresContext",
            [Reason("CorporateActionCaseRequired", "Select a retained corporate-action case to inspect its exact evidence, conflicts, approvals and allowed actions.")]));
        var redemptionApplicable = !modeled || descriptor.RequiresMaturity || descriptor.CashFlowBehavior != SecurityCashFlowBehavior.None;
        rows.Add(Row("redemption", "Redemption", "ContractualRedemption", "Review",
            redemptionApplicable ? "RequiresContext" : "Complete", redemptionApplicable
                ? [Reason("RedemptionContextRequired", "Select the contractual maturity or exercise event, holding and book before verifying redemption eligibility.")]
                : [], redemptionApplicable));
        return rows;
    }

    private SecurityValidationReportDto ValidateTerms(SecurityDetailDto security, DateTimeOffset asOf)
    {
        var issues = _validators.TryGetValidator(security.AssetClass, out var validator)
            ? validator.Validate(new(new SecurityProjectionRecord(security.SecurityId, security.AssetClass, security.Status,
                security.DisplayName, security.Currency, "", "", security.CommonTerms, security.AssetSpecificTerms,
                JsonSerializer.SerializeToElement(new { }), security.Version, security.EffectiveFrom, security.EffectiveTo,
                security.Identifiers, security.Aliases), asOf))
            : new[] { new SecurityValidationIssueDto(SecurityValidationSeverityDto.Error, "SM_ASSET_CLASS_UNSUPPORTED",
                "Asset class is unsupported", "No validator is registered for this asset class.", [], "Review the instrument class.", []) };
        return new(security.SecurityId, "Economic terms", asOf,
            issues.Any(static issue => issue.Severity >= SecurityValidationSeverityDto.Error),
            issues.Count(static issue => issue.Severity == SecurityValidationSeverityDto.Critical),
            issues.Count(static issue => issue.Severity == SecurityValidationSeverityDto.Error), issues);
    }

    private static InstrumentCapabilitiesDto Unavailable(Guid id, DateTimeOffset asOf, string code, string reason)
        => new(id, null, asOf, new[] { ("terms", "Terms"), ("cash-flows", "Cash flows"), ("accrual", "Accrual"),
                ("valuation", "Valuation"), ("corporate-actions", "Corporate actions"), ("redemption", "Redemption") }
            .Select(item => new InstrumentCapabilityAssessmentDto(item.Item1, item.Item2, "Unavailable", "Unknown", "Review",
                "Unavailable", null, asOf, "Selected identity could not be evaluated.",
                [new(code, reason, $"/accounting/security-master?securityId={id:D}")])).ToArray());
}
