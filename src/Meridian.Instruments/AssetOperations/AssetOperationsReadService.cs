using Meridian.Instruments.FixedIncome;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.AssetOperations;

namespace Meridian.Instruments.AssetOperations;

public sealed class AssetOperationsReadService : IAssetOperationsQueryService
{
    private readonly IAssetOperationsProjectionStore? _projectionStore;
    private readonly ISecurityMasterQueryService? _securityMasterQueryService;
    private readonly IBondReferenceService? _bondReferenceService;

    public AssetOperationsReadService(
        IAssetOperationsProjectionStore? projectionStore = null,
        ISecurityMasterQueryService? securityMasterQueryService = null,
        IBondReferenceService? bondReferenceService = null)
    {
        _projectionStore = projectionStore;
        _securityMasterQueryService = securityMasterQueryService;
        _bondReferenceService = bondReferenceService;
    }

    public async Task<AssetOperationsDetailDto?> GetOperationsAsync(Guid securityId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var published = _projectionStore is null
            ? null
            : await _projectionStore.GetAsync(securityId, ct).ConfigureAwait(false);
        if (published is not null)
        {
            return published;
        }

        var security = _securityMasterQueryService is null
            ? null
            : await _securityMasterQueryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
        if (security is null)
        {
            return null;
        }

        return string.Equals(security.AssetClass, "Bond", StringComparison.OrdinalIgnoreCase)
            ? await BuildBondOperationsAsync(security, ct).ConfigureAwait(false)
            : BuildIdentityOnlyOperations(security);
    }

    public async Task<AssetOperationsReadinessDto?> GetReadinessAsync(Guid securityId, CancellationToken ct = default)
    {
        var detail = await GetOperationsAsync(securityId, ct).ConfigureAwait(false);
        return detail?.Readiness;
    }

    private async Task<AssetOperationsDetailDto> BuildBondOperationsAsync(SecurityDetailDto security, CancellationToken ct)
    {
        var bond = _bondReferenceService is null
            ? null
            : await _bondReferenceService.GetReferenceAsync(security.SecurityId, ct).ConfigureAwait(false);
        if (bond is null)
        {
            return BuildIdentityOnlyOperations(security);
        }

        return AssetOperationsProjectionBuilder.FromBondReference(bond, BuildSubject(security));
    }

    private static AssetOperationsDetailDto BuildIdentityOnlyOperations(SecurityDetailDto security)
    {
        var subject = BuildSubject(security);
        var readiness = new AssetOperationsReadinessDto(
            subject.SecurityId,
            "ReviewRequired",
            subject.OperationalProfile,
            ["Identity", "TermsHistory"],
            subject.OperationalProfile.Except(["Identity", "TermsHistory"], StringComparer.OrdinalIgnoreCase).ToArray(),
            ["No asset-operation domain projection has been published for this Security Master subject."],
            DateTimeOffset.UtcNow,
            "SecurityMaster",
            subject.SecurityId.ToString("D"));

        return new AssetOperationsDetailDto(
            subject,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            readiness,
            []);
    }

    private static AssetOperationSubjectDto BuildSubject(SecurityDetailDto security)
        => new(
            security.SecurityId,
            security.AssetClass,
            security.DisplayName,
            ResolvePrimaryIdentifier(security.Identifiers),
            SecurityAssetClassCatalog.GetAssetOperationsCapabilities(security.AssetClass));

    private static string? ResolvePrimaryIdentifier(IReadOnlyList<SecurityIdentifierDto> identifiers)
    {
        var primary = identifiers.FirstOrDefault(static identifier => identifier.IsPrimary)
            ?? identifiers.FirstOrDefault();
        return primary is null ? null : $"{primary.Kind}:{primary.Value}";
    }
}

public sealed class AssetOperationsProjectionCommandService : IAssetOperationsCommandService
{
    private readonly IAssetOperationsProjectionStore _projectionStore;

    public AssetOperationsProjectionCommandService(IAssetOperationsProjectionStore projectionStore)
    {
        _projectionStore = projectionStore;
    }

    public async Task<AssetOperationsDetailDto> UpsertProjectionAsync(
        AssetOperationsProjectionDto projection,
        AssetOperationsWriteApprovalDto approval,
        CancellationToken ct = default)
    {
        await _projectionStore.UpsertAsync(projection, approval, ct).ConfigureAwait(false);
        return await _projectionStore.GetAsync(projection.Subject.SecurityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Asset Operations projection was not readable after write.");
    }
}

public static class AssetOperationsProjectionBuilder
{
    public static AssetOperationsProjectionDto FromDirectLending(
        LoanContractDetailDto contract,
        IReadOnlyList<ProjectionRunDto> projectionRuns,
        IReadOnlyDictionary<Guid, IReadOnlyList<ProjectedCashFlowDto>> projectedCashFlowsByRun,
        IReadOnlyList<CashTransactionDto> actualActivity,
        IReadOnlyList<ReconciliationRunDto> reconciliationRuns,
        IReadOnlyDictionary<Guid, IReadOnlyList<ReconciliationResultDto>> reconciliationResultsByRun)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var securityReference = contract.CurrentTerms.SecurityMasterReference
            ?? throw new InvalidOperationException("Direct lending Asset Operations projection requires a Security Master reference.");
        var securityId = securityReference.SecurityId;
        var subject = new AssetOperationSubjectDto(
            securityId,
            "DirectLoan",
            contract.FacilityName,
            securityReference.Symbol,
            SecurityAssetClassCatalog.GetAssetOperationsCapabilities("DirectLoan"));

        var terms = contract.TermsVersions.Select(version => new AssetTermsVersionDto(
            Guid.NewGuid(),
            securityId,
            version.VersionNumber,
            version.TermsHash,
            version.Terms.OriginationDate,
            version.RecordedAt,
            "DirectLending",
            contract.LoanId.ToString("D"),
            $"{contract.FacilityName} terms version {version.VersionNumber}")).ToArray();
        var lifecycle = new[]
        {
            new AssetLifecycleEventDto(
                Guid.NewGuid(),
                securityId,
                "LoanStatus",
                contract.Status.ToString(),
                contract.EffectiveDate,
                DateTimeOffset.UtcNow,
                "DirectLending",
                contract.LoanId.ToString("D"),
                $"Loan is {contract.Status}.")
        };
        var runs = projectionRuns.Select(run => new AssetCashFlowProjectionRunDto(
            run.ProjectionRunId,
            securityId,
            run.ProjectionAsOf,
            run.EngineVersion,
            run.Status.ToString(),
            run.GeneratedAt,
            "DirectLending",
            contract.LoanId.ToString("D"))).ToArray();
        var flows = projectionRuns
            .SelectMany(run => projectedCashFlowsByRun.TryGetValue(run.ProjectionRunId, out var runFlows) ? runFlows : [])
            .Select(flow => new AssetProjectedCashFlowDto(
                flow.ProjectedCashFlowId,
                flow.ProjectionRunId,
                securityId,
                flow.FlowSequenceNumber,
                flow.FlowType,
                flow.DueDate,
                flow.Amount,
                flow.Currency.ToString(),
                "Projected",
                flow.AccrualStartDate,
                flow.AccrualEndDate,
                flow.PrincipalBasis,
                flow.AnnualRate,
                "DirectLending",
                contract.LoanId.ToString("D"))).ToArray();
        var activity = actualActivity.Select(row => new AssetActualActivityDto(
            row.CashTransactionId,
            securityId,
            row.TransactionType,
            row.EffectiveDate,
            row.SettlementDate,
            row.Amount,
            row.Currency.ToString(),
            row.IsVoided ? "Voided" : "Posted",
            "DirectLending",
            contract.LoanId.ToString("D"),
            row.ExternalRef)).ToArray();
        var reconciliations = reconciliationRuns.Select(run => new AssetReconciliationRunDto(
            run.ReconciliationRunId,
            securityId,
            run.ProjectionRunId,
            run.Status,
            run.RequestedAt,
            run.CompletedAt,
            "DirectLending",
            contract.LoanId.ToString("D"))).ToArray();
        var reconciliationResults = reconciliationRuns
            .SelectMany(run => reconciliationResultsByRun.TryGetValue(run.ReconciliationRunId, out var results) ? results : [])
            .Select(result => new AssetReconciliationResultDto(
                result.ReconciliationResultId,
                result.ReconciliationRunId,
                securityId,
                result.MatchStatus,
                result.ExpectedAmount,
                result.ActualAmount,
                result.VarianceAmount,
                result.ExpectedDate,
                result.ActualDate,
                "DirectLending",
                contract.LoanId.ToString("D"),
                result.CashTransactionId?.ToString("D"))).ToArray();
        var ledger = new[]
        {
            new AssetLedgerProjectionDto(
                Guid.NewGuid(),
                securityId,
                "DirectLendingLedgerProjection",
                DateOnly.FromDateTime(DateTime.UtcNow),
                "Primary",
                "Ready",
                null,
                null,
                contract.CurrentTerms.BaseCurrency.ToString(),
                "LoanAccountingProjector",
                contract.LoanId.ToString("D"),
                securityReference.LedgerMappingReference)
        };

        var readyCapabilities = ReadyCapabilities(subject.OperationalProfile, terms, lifecycle, flows, activity, reconciliations, reconciliationResults, ledger);
        var readiness = BuildReadiness(subject, readyCapabilities, "DirectLending", contract.LoanId.ToString("D"));

        return new AssetOperationsProjectionDto(
            subject,
            terms,
            lifecycle,
            runs,
            flows,
            activity,
            reconciliations,
            reconciliationResults,
            ledger,
            readiness,
            lifecycle);
    }

    public static AssetOperationsDetailDto FromBondReference(BondReferenceDto bond, AssetOperationSubjectDto subject)
    {
        var terms = new[]
        {
            new AssetTermsVersionDto(
                Guid.NewGuid(),
                subject.SecurityId,
                checked((int)Math.Min(bond.Version, int.MaxValue)),
                $"bond-reference:{bond.Version}",
                bond.Lifecycle?.IssueDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date),
                DateTimeOffset.UtcNow,
                "SecurityMaster",
                subject.SecurityId.ToString("D"),
                $"{bond.DisplayName} maturity/coupon reference terms")
        };
        IReadOnlyList<AssetLifecycleEventDto> lifecycle = bond.Lifecycle is null
            ? []
            :
            [
                new AssetLifecycleEventDto(
                    Guid.NewGuid(),
                    subject.SecurityId,
                    "BondLifecycle",
                    bond.Lifecycle.LifecycleStat.ToString(),
                    bond.Lifecycle.IssueDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date),
                    DateTimeOffset.UtcNow,
                    "SecurityMaster",
                    subject.SecurityId.ToString("D"),
                    $"Bond lifecycle is {bond.Lifecycle.LifecycleStat}.")
            ];
        var projectionRunId = Guid.NewGuid();
        var run = new AssetCashFlowProjectionRunDto(
            projectionRunId,
            subject.SecurityId,
            DateOnly.FromDateTime(DateTime.UtcNow.Date),
            "fixed-income-reference-v1",
            "Completed",
            DateTimeOffset.UtcNow,
            "SecurityMaster",
            subject.SecurityId.ToString("D"));
        var flows = BuildBondProjectedCashFlows(bond, projectionRunId).ToArray();
        var ledger = new[]
        {
            new AssetLedgerProjectionDto(
                Guid.NewGuid(),
                subject.SecurityId,
                "FixedIncomeLedgerProjection",
                flows.FirstOrDefault()?.DueDate ?? bond.Lifecycle?.MaturityDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date),
                "Primary",
                "Ready",
                null,
                null,
                bond.Currency,
                "SecurityMasterAccountingEventService",
                subject.SecurityId.ToString("D"),
                $"security-master:{subject.SecurityId:D}")
        };
        var readyCapabilities = ReadyCapabilities(subject.OperationalProfile, terms, lifecycle, flows, [], [], [], ledger);
        var readiness = BuildReadiness(subject, readyCapabilities, "SecurityMaster", subject.SecurityId.ToString("D"));

        return new AssetOperationsDetailDto(
            subject,
            terms,
            lifecycle,
            [run],
            flows,
            [],
            [],
            [],
            ledger,
            readiness,
            lifecycle);
    }

    private static IEnumerable<AssetProjectedCashFlowDto> BuildBondProjectedCashFlows(BondReferenceDto bond, Guid projectionRunId)
    {
        var maturity = bond.Lifecycle?.MaturityDate;
        if (maturity is null)
        {
            yield break;
        }

        if (bond.AccrualConvention?.FixedCouponRate is decimal coupon && coupon > 0m)
        {
            yield return new AssetProjectedCashFlowDto(
                Guid.NewGuid(),
                projectionRunId,
                bond.SecurityId,
                1,
                "Coupon",
                maturity.Value,
                decimal.Round(coupon, 4, MidpointRounding.AwayFromZero),
                bond.Currency,
                "Projected",
                null,
                maturity,
                null,
                coupon,
                "SecurityMaster",
                bond.SecurityId.ToString("D"));
        }

        yield return new AssetProjectedCashFlowDto(
            Guid.NewGuid(),
            projectionRunId,
            bond.SecurityId,
            2,
            "Maturity",
            maturity.Value,
            100m,
            bond.Currency,
            "Projected",
            null,
            maturity,
            100m,
            null,
            "SecurityMaster",
            bond.SecurityId.ToString("D"));
    }

    private static IReadOnlyList<string> ReadyCapabilities(
        IReadOnlyList<string> capabilities,
        IReadOnlyList<AssetTermsVersionDto> terms,
        IReadOnlyList<AssetLifecycleEventDto> lifecycle,
        IReadOnlyList<AssetProjectedCashFlowDto> projectedCashFlows,
        IReadOnlyList<AssetActualActivityDto> actualActivity,
        IReadOnlyList<AssetReconciliationRunDto> reconciliationRuns,
        IReadOnlyList<AssetReconciliationResultDto> reconciliationResults,
        IReadOnlyList<AssetLedgerProjectionDto> ledgerProjections)
    {
        var ready = new List<string> { "Identity" };
        if (terms.Count > 0)
        {
            ready.Add("TermsHistory");
        }
        if (lifecycle.Count > 0)
        {
            ready.Add("LifecycleState");
            ready.Add("WorkflowAudit");
        }
        if (projectedCashFlows.Count > 0)
        {
            ready.Add("ProjectedCashFlows");
        }
        if (actualActivity.Count > 0)
        {
            ready.Add("ActualActivity");
        }
        if (reconciliationRuns.Count > 0 || reconciliationResults.Count > 0)
        {
            ready.Add("Reconciliation");
        }
        if (ledgerProjections.Count > 0)
        {
            ready.Add("LedgerProjection");
        }
        ready.Add("Evidence");

        return ready
            .Where(capability => capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AssetOperationsReadinessDto BuildReadiness(
        AssetOperationSubjectDto subject,
        IReadOnlyList<string> readyCapabilities,
        string sourceDomain,
        string? sourceEntityId)
    {
        var missing = subject.OperationalProfile
            .Except(readyCapabilities, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new AssetOperationsReadinessDto(
            subject.SecurityId,
            missing.Length == 0 ? "Ready" : "ReviewRequired",
            subject.OperationalProfile,
            readyCapabilities,
            missing,
            missing.Length == 0 ? [] : missing.Select(capability => $"{capability} projection has not been published.").ToArray(),
            DateTimeOffset.UtcNow,
            sourceDomain,
            sourceEntityId);
    }
}
