using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed record CertifiedReportingRunContext(
    ReportingOperationalScope OperationalScope,
    ReportingAccessScope AccessScope,
    ReportingCertifiedSnapshotScope Snapshot,
    ReportingAuthoritativeSourceCheckpoint AuthoritativeSource,
    ImmutableArray<IReadOnlyDictionary<string, string>> DatasetRows,
    ReportingRunReadinessDto Readiness);

/// <summary>
/// Freezes the exact durable source rows, normalized parameters, readiness evidence, and authority
/// scope before orchestration. Caller/workflow rows are never accepted as certification input.
/// </summary>
public sealed class ReportingRunCertificationService
{
    private readonly IReportingAuthoritativeSource? _authoritativeSource;
    private readonly IReportingReconciliationEvidenceSource? _reconciliationEvidenceSource;

    public ReportingRunCertificationService(
        IReportingAuthoritativeSource? authoritativeSource = null,
        IReportingReconciliationEvidenceSource? reconciliationEvidenceSource = null)
    {
        _authoritativeSource = authoritativeSource;
        _reconciliationEvidenceSource = reconciliationEvidenceSource;
    }

    public async ValueTask<CertifiedReportingRunContext> CertifyAsync(
        ReportingTemplateMetadata template,
        ReportingRunReadinessDto readiness,
        ReportAccessQueryContext accessContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(accessContext);
        RequireBoundScope(accessContext);
        ValidateParameters(readiness.ResolvedParameters);
        var source = _authoritativeSource
            ?? throw new ReportingAuthoritativeSourceUnavailableException(
                "No durable authoritative reporting source is configured; certification is blocked.");
        var reconciliationSource = _reconciliationEvidenceSource
            ?? throw new ReportingAuthoritativeSourceUnavailableException(
                "No durable reconciliation/close evidence source is configured; certification is blocked.");

        var capture = await source
            .CaptureAsync(
                readiness.ResolvedParameters,
                accessContext,
                ReportingAuthoritativeSourceCaptureIntent.FromTemplate(template),
                cancellationToken)
            .ConfigureAwait(false);
        ValidateCapture(capture, readiness.ResolvedParameters, accessContext);
        RequireCertifiedPresentationEvidence(
            capture.Checkpoint,
            template.TemplateId,
            readiness.ResolvedParameters.OutputFormat);
        ReportingReconciliationEvidenceReceipt reconciliation;
        try
        {
            reconciliation = await reconciliationSource
                .ResolveAsync(readiness.ResolvedParameters, capture.Checkpoint, accessContext, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ReportingReconciliationReadinessException exception)
        {
            var invalid = exception as ReportingReconciliationEvidenceInvalidException;
            throw new ReportingRunReadinessBlockedException(
                BuildMissingReconciliationReadiness(
                    template,
                    readiness,
                    capture,
                    exception.Message,
                    invalid?.BlockingCount ?? 1,
                    invalid?.EvidenceReferences));
        }
        if (readiness.ResolvedParameters.Finality == ReportingFinalityDto.Final)
        {
            try
            {
                ReportingReconciliationEvidenceValidation.RequireCommittedCloseWorkflow(reconciliation);
            }
            catch (ArgumentException exception)
            {
                throw new ReportingRunReadinessBlockedException(
                    BuildMissingReconciliationReadiness(
                        template,
                        readiness,
                        capture,
                        exception.Message,
                        reconciliationEvidence: reconciliation.EvidenceIds));
            }
        }

        var parameters = NormalizeParameters(readiness.ResolvedParameters, capture.Checkpoint);
        var access = BuildAccessScope(template, readiness.ResolvedTemplate, accessContext);
        var scope = new ReportingOperationalScope(
            capture.Checkpoint.TenantId,
            capture.Checkpoint.OrganizationId,
            capture.Checkpoint.CompanyId,
            capture.Checkpoint.FundId,
            capture.Checkpoint.LedgerBookId,
            capture.Checkpoint.AccountingPeriodId);

        var sourceReady = template.ReportWriterGrids is not { Count: > 0 }
            || !capture.DatasetRows.IsDefaultOrEmpty;
        var sourceCheck = new ReportingRunReadinessCheckDto(
            "authoritative-report-source",
            "Exact authoritative reporting source",
            sourceReady ? ReportingRunReadinessStatusDto.Ready : ReportingRunReadinessStatusDto.Blocked,
            sourceReady
                ? $"The durable ledger checkpoint resolved {capture.DatasetRows.Length} exact as-of row(s)."
                : "The exact durable ledger scope contains no rows required by this report template.",
            sourceReady ? 0 : 1,
            BlocksDraft: !sourceReady,
            BlocksFinal: !sourceReady,
            "/workstation/reporting/data",
            capture.Checkpoint.EvidenceIds);
        var reconciliationCheck = new ReportingRunReadinessCheckDto(
            "exact-reconciliation-evidence",
            "Exact close and reconciliation evidence",
            ReportingRunReadinessStatusDto.Ready,
            $"Exact retained close/reconciliation checkpoint {reconciliation.ReconciliationCheckpointId} matches certified source checkpoint {capture.Checkpoint.CheckpointId}.",
            0,
            BlocksDraft: false,
            BlocksFinal: false,
            "/workstation/accounting/close",
            reconciliation.EvidenceIds);

        var checksWithoutReconciliation = readiness.Checks
            .Where(static check =>
                !string.Equals(check.CheckId, "report-dataset", StringComparison.Ordinal)
                && !string.Equals(check.CheckId, "authoritative-report-source", StringComparison.Ordinal)
                && !string.Equals(check.CheckId, "exact-reconciliation-evidence", StringComparison.Ordinal)
                && !string.Equals(check.CheckId, "certified-scope-readiness", StringComparison.Ordinal))
            .Append(sourceCheck)
            .Append(reconciliationCheck)
            .OrderBy(static check => check.CheckId, StringComparer.Ordinal)
            .ToArray();
        var preflightReadiness = RebuildReadiness(readiness, parameters, checksWithoutReconciliation);
        var requestedPreflightReady = parameters.Finality == ReportingFinalityDto.Final
            ? preflightReadiness.CanGenerateFinal
            : preflightReadiness.CanGenerateDraft;
        var scopeCheck = new ReportingRunReadinessCheckDto(
            "certified-scope-readiness",
            "Exact-scope reporting and reconciliation certification",
            requestedPreflightReady
                ? ReportingRunReadinessStatusDto.Ready
                : ReportingRunReadinessStatusDto.Blocked,
            requestedPreflightReady
                ? "The exact tenant/organization/company/fund/book/period/basis/as-of source and all final-output controls are ready."
                : "One or more exact-scope readiness controls are not ready for certification.",
            requestedPreflightReady ? 0 : 1,
            BlocksDraft: parameters.Finality == ReportingFinalityDto.Draft && !requestedPreflightReady,
            BlocksFinal: parameters.Finality == ReportingFinalityDto.Final && !requestedPreflightReady,
            "/workstation/reporting/readiness",
            capture.Checkpoint.EvidenceIds
                .Concat(reconciliation.EvidenceIds)
                .Append($"reporting-parameters:{ComputeParametersHash(parameters)}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static evidence => evidence, StringComparer.Ordinal)
                .ToArray());
        var certifiedReadiness = RebuildReadiness(
            readiness,
            parameters,
            checksWithoutReconciliation.Append(scopeCheck).ToArray());

        var parametersJson = SerializeParameters(parameters);
        var parametersHash = ComputeHash(parametersJson);
        var snapshotHash = ComputeHash(JsonSerializer.Serialize(new
        {
            template = new
            {
                readiness.ResolvedTemplate.Name,
                readiness.ResolvedTemplate.Version
            },
            scope,
            access,
            parametersHash,
            sourceCheckpointId = capture.Checkpoint.CheckpointId,
            sourceCheckpointHash = capture.Checkpoint.CheckpointHash,
            reconciliationId = reconciliation.ReconciliationCheckpointId,
            reconciliationHash = reconciliation.ReconciliationCheckpointHash,
            readinessHash = certifiedReadiness.EvidenceHash,
            certifiedDatasetHash = ComputeCertifiedRowsHash(capture.DatasetRows)
        }));
        var certifiedAtUtc = capture.Checkpoint.CapturedAtUtc >= reconciliation.ReconciledAtUtc
            ? capture.Checkpoint.CapturedAtUtc
            : reconciliation.ReconciledAtUtc;
        var snapshot = new ReportingCertifiedSnapshotScope(
            scope.TenantId,
            scope.OrganizationId,
            scope.CompanyId,
            scope.FundId,
            scope.BookId,
            scope.PeriodId,
            $"report-snapshot-{snapshotHash[..32]}",
            snapshotHash,
            reconciliation.ReconciliationCheckpointId,
            certifiedAtUtc,
            capture.Checkpoint.CheckpointId,
            capture.Checkpoint.CheckpointHash,
            reconciliation.ReconciliationCheckpointHash,
            parametersJson,
            parametersHash);

        return new CertifiedReportingRunContext(
            scope,
            access,
            snapshot,
            capture.Checkpoint,
            capture.DatasetRows,
            certifiedReadiness);
    }

    /// <summary>
    /// Rechecks the exact authoritative source and canonical reconciliation queue immediately
    /// before final release. Approval of a frozen snapshot must not allow a later queue mutation
    /// or ledger change to pass on stale certification.
    /// </summary>
    public async ValueTask RevalidateForReleaseAsync(
        ReportingRunParametersDto parameters,
        string templateId,
        ReportingAuthoritativeSourceCheckpoint certifiedSource,
        ReportingCertifiedSnapshotScope certifiedSnapshot,
        ReportAccessQueryContext accessContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentNullException.ThrowIfNull(certifiedSource);
        ArgumentNullException.ThrowIfNull(certifiedSnapshot);
        ArgumentNullException.ThrowIfNull(accessContext);
        RequireBoundScope(accessContext);
        ValidateParameters(parameters);
        var source = _authoritativeSource
            ?? throw new ReportingAuthoritativeSourceUnavailableException(
                "No durable authoritative reporting source is configured; final release revalidation is blocked.");
        var reconciliationSource = _reconciliationEvidenceSource
            ?? throw new ReportingAuthoritativeSourceUnavailableException(
                "No durable reconciliation/close evidence source is configured; final release revalidation is blocked.");

        var current = await source
            .CaptureAsync(
                parameters,
                accessContext,
                new ReportingAuthoritativeSourceCaptureIntent(templateId),
                cancellationToken)
            .ConfigureAwait(false);
        ValidateCapture(current, parameters, accessContext);
        if (!SameCertifiedSource(
                current.Checkpoint,
                certifiedSource,
                certifiedSnapshot,
                templateId,
                parameters.OutputFormat))
        {
            throw new ReportingGovernanceException(
                "Final release is blocked because the authoritative ledger source changed after certification. Regenerate and reapprove the report from a fresh certified snapshot.");
        }

        var reconciliation = await reconciliationSource
            .ResolveAsync(parameters, current.Checkpoint, accessContext, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ReportingReconciliationEvidenceValidation.RequireCommittedCloseWorkflow(reconciliation);
        }
        catch (ArgumentException exception)
        {
            throw new ReportingGovernanceException(
                $"Final release is blocked because committed accounting-close workflow evidence is unavailable or invalid: {exception.Message}");
        }
        if (!string.Equals(
                reconciliation.ReconciliationCheckpointId,
                certifiedSnapshot.ReconciliationCheckpointId,
                StringComparison.Ordinal)
            || !string.Equals(
                reconciliation.ReconciliationCheckpointHash,
                certifiedSnapshot.ReconciliationCheckpointHash,
                StringComparison.Ordinal))
        {
            throw new ReportingGovernanceException(
                "Final release is blocked because reconciliation evidence changed after certification. Re-run reconciliation and close, then regenerate and reapprove the report.");
        }
    }

    private static bool SameCertifiedSource(
        ReportingAuthoritativeSourceCheckpoint current,
        ReportingAuthoritativeSourceCheckpoint certified,
        ReportingCertifiedSnapshotScope snapshot,
        string templateId,
        ReportingOutputFormatDto outputFormat) =>
        string.Equals(current.TenantId, certified.TenantId, StringComparison.Ordinal)
        && string.Equals(current.OrganizationId, certified.OrganizationId, StringComparison.Ordinal)
        && string.Equals(current.CompanyId, certified.CompanyId, StringComparison.Ordinal)
        && string.Equals(current.FundId, certified.FundId, StringComparison.Ordinal)
        && string.Equals(current.LedgerBookId, certified.LedgerBookId, StringComparison.Ordinal)
        && string.Equals(current.AccountingPeriodId, certified.AccountingPeriodId, StringComparison.Ordinal)
        && string.Equals(current.AccountingBasis, certified.AccountingBasis, StringComparison.Ordinal)
        && current.AsOfDate == certified.AsOfDate
        && string.Equals(current.CheckpointId, certified.CheckpointId, StringComparison.Ordinal)
        && string.Equals(current.CheckpointHash, certified.CheckpointHash, StringComparison.Ordinal)
        && string.Equals(current.TenantId, snapshot.TenantId, StringComparison.Ordinal)
        && string.Equals(current.OrganizationId, snapshot.OrganizationId, StringComparison.Ordinal)
        && string.Equals(current.CompanyId, snapshot.CompanyId, StringComparison.Ordinal)
        && string.Equals(current.FundId, snapshot.FundId, StringComparison.Ordinal)
        && string.Equals(current.LedgerBookId, snapshot.BookId, StringComparison.Ordinal)
        && string.Equals(current.AccountingPeriodId, snapshot.PeriodId, StringComparison.Ordinal)
        && string.Equals(current.CheckpointId, snapshot.SourceCheckpointId, StringComparison.Ordinal)
        && string.Equals(current.CheckpointHash, snapshot.SourceCheckpointHash, StringComparison.Ordinal)
        && HasSameRequiredPresentationEvidence(current, certified, templateId, outputFormat);

    private static bool HasSameRequiredPresentationEvidence(
        ReportingAuthoritativeSourceCheckpoint current,
        ReportingAuthoritativeSourceCheckpoint certified,
        string templateId,
        ReportingOutputFormatDto outputFormat)
    {
        if (!ReportingCertifiedLedgerPresentationBinding.IsRequired(templateId, outputFormat))
        {
            return true;
        }

        var currentEvidence =
            ReportingCertifiedLedgerPresentationBinding.GetSingleEvidenceId(current);
        var certifiedEvidence =
            ReportingCertifiedLedgerPresentationBinding.GetSingleEvidenceId(certified);
        return currentEvidence is not null
            && certifiedEvidence is not null
            && string.Equals(currentEvidence, certifiedEvidence, StringComparison.OrdinalIgnoreCase);
    }

    private static void RequireCertifiedPresentationEvidence(
        ReportingAuthoritativeSourceCheckpoint checkpoint,
        string templateId,
        ReportingOutputFormatDto outputFormat)
    {
        if (ReportingCertifiedLedgerPresentationBinding.IsRequired(templateId, outputFormat)
            && ReportingCertifiedLedgerPresentationBinding.GetSingleEvidenceId(checkpoint) is null)
        {
            throw new ReportingAuthoritativeSourceUnavailableException(
                "Capital-account client-package certification is blocked because the authoritative source did not retain one signed canonical ledger-presentation checksum.");
        }
    }

    /// <summary>
    /// Compatibility signature retained so old callers fail closed instead of silently certifying
    /// caller-owned or mutable workflow rows. Governed code must use <see cref="CertifyAsync"/>.
    /// </summary>
    [Obsolete("Caller/workflow rows are not certifiable. Use CertifyAsync with a durable authoritative source.")]
    public CertifiedReportingRunContext Certify(
        ReportingTemplateMetadata template,
        ReportingRunReadinessDto readiness,
        IReadOnlyList<IReadOnlyDictionary<string, string>>? datasetRows,
        string? datasetSourceId,
        ReportAccessQueryContext accessContext) =>
        throw new ReportingAuthoritativeSourceUnavailableException(
            "Synchronous certification of caller or workflow rows is disabled. A durable authoritative source is required.");

    public ReportingReadinessReceipt BuildGovernanceReadiness(
        string runId,
        CertifiedReportingRunContext certified,
        ReportingRunReadinessDto readiness)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(certified);
        ArgumentNullException.ThrowIfNull(readiness);
        if (!string.Equals(certified.Readiness.EvaluationId, readiness.EvaluationId, StringComparison.Ordinal)
            || !string.Equals(certified.Readiness.EvidenceHash, readiness.EvidenceHash, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                certified.Snapshot.ParametersHash,
                ComputeParametersHash(readiness.ResolvedParameters),
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(certified.Snapshot.SourceCheckpointId, certified.AuthoritativeSource.CheckpointId, StringComparison.Ordinal)
            || !string.Equals(certified.Snapshot.SourceCheckpointHash, certified.AuthoritativeSource.CheckpointHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Governance readiness must use the exact readiness and source checkpoint captured during certification.");
        }

        var finality = readiness.ResolvedParameters.Finality;
        var checks = readiness.Checks
            .Select(check => new ReportingReadinessCheck(
                check.CheckId,
                check.Status == ReportingRunReadinessStatusDto.Ready
                    || !IsRequiredForFinality(check, finality),
                check.EvidenceReferences
                    .Where(static evidence => !string.IsNullOrWhiteSpace(evidence))
                    .Select(static evidence => evidence.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static evidence => evidence, StringComparer.Ordinal)
                    .ToImmutableArray(),
                check.Status == ReportingRunReadinessStatusDto.Ready
                    || !IsRequiredForFinality(check, finality)
                        ? null
                        : check.Summary))
            .ToImmutableArray();
        var receipt = new ReportingReadinessReceipt(
            readiness.EvaluationId,
            string.Empty,
            runId.Trim(),
            certified.OperationalScope.TenantId,
            certified.OperationalScope.OrganizationId,
            certified.OperationalScope.CompanyId,
            certified.Snapshot.SnapshotId,
            certified.Snapshot.SnapshotHash,
            readiness.EvaluatedAtUtc,
            checks);
        return receipt with
        {
            ReceiptHash = ReportingGovernanceCanonicalValidation.ComputeReadinessReceiptHash(receipt)
        };
    }

    internal static void ValidateCapture(
        ReportingAuthoritativeSourceCapture capture,
        ReportingRunParametersDto parameters,
        ReportAccessQueryContext accessContext)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(capture.Checkpoint);
        var expectedBasis = parameters.AccountingBasis switch
        {
            ReportingAccountingBasisDto.Gaap => AccountingBasisKindDto.Gaap.ToString(),
            ReportingAccountingBasisDto.Tax => AccountingBasisKindDto.Tax.ToString(),
            ReportingAccountingBasisDto.Cash => AccountingBasisKindDto.Cash.ToString(),
            ReportingAccountingBasisDto.Statutory => AccountingBasisKindDto.Statutory.ToString(),
            _ => AccountingBasisKindDto.Primary.ToString()
        };
        var expectedCutoffUtc = new DateTimeOffset(
            parameters.AsOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
            TimeSpan.Zero);
        if (!string.Equals(capture.Checkpoint.TenantId, accessContext.TenantId, StringComparison.Ordinal)
            || !string.Equals(capture.Checkpoint.CompanyId, accessContext.CompanyId, StringComparison.Ordinal)
            || !string.Equals(capture.Checkpoint.FundId, parameters.Scope.FundProfileId.Trim(), StringComparison.Ordinal)
            || capture.Checkpoint.AsOfDate != parameters.AsOfDate
            || capture.Checkpoint.CutoffUtc != expectedCutoffUtc
            || !string.Equals(capture.Checkpoint.AccountingBasis, expectedBasis, StringComparison.Ordinal)
            || !IsLowercaseSha256(capture.Checkpoint.CheckpointHash)
            || string.IsNullOrWhiteSpace(capture.Checkpoint.CheckpointId)
            || string.IsNullOrWhiteSpace(capture.Checkpoint.SourceKind)
            || string.IsNullOrWhiteSpace(capture.Checkpoint.SourceId)
            || string.IsNullOrWhiteSpace(capture.Checkpoint.OrganizationId)
            || string.IsNullOrWhiteSpace(capture.Checkpoint.LedgerBookId)
            || string.IsNullOrWhiteSpace(capture.Checkpoint.AccountingPeriodId)
            || capture.Checkpoint.CapturedAtUtc.Offset != TimeSpan.Zero
            || capture.Checkpoint.CutoffUtc.Offset != TimeSpan.Zero
            || capture.Checkpoint.HighestGlobalSequence < 0
            || capture.Checkpoint.JournalEntryCount < 0
            || capture.Checkpoint.LedgerLineCount < 0
            || capture.Checkpoint.LedgerLineCount != capture.DatasetRows.Length
            || capture.Checkpoint.EvidenceIds.IsDefaultOrEmpty
            || !capture.Checkpoint.EvidenceIds.Contains(
                $"reporting-source-checkpoint:{capture.Checkpoint.CheckpointId}:{capture.Checkpoint.CheckpointHash}",
                StringComparer.Ordinal))
        {
            throw new ReportingAuthoritativeSourceUnavailableException(
                "The authoritative source returned an incomplete or mismatched tenant/scope/checkpoint receipt.");
        }
    }

    private static ReportingRunReadinessDto RebuildReadiness(
        ReportingRunReadinessDto original,
        ReportingRunParametersDto parameters,
        IReadOnlyList<ReportingRunReadinessCheckDto> checks)
    {
        var orderedChecks = checks.OrderBy(static check => check.CheckId, StringComparer.Ordinal).ToArray();
        var canDraft = orderedChecks.All(static check =>
            !check.BlocksDraft || check.Status == ReportingRunReadinessStatusDto.Ready);
        var canFinal = orderedChecks.All(static check =>
            !check.BlocksFinal || check.Status == ReportingRunReadinessStatusDto.Ready);
        var relevantChecks = orderedChecks
            .Where(check => IsRequiredForFinality(check, parameters.Finality))
            .ToArray();
        var status = relevantChecks.Any(static check => check.Status is ReportingRunReadinessStatusDto.Blocked or ReportingRunReadinessStatusDto.Unavailable)
            ? ReportingRunReadinessStatusDto.Blocked
            : relevantChecks.Any(static check => check.Status == ReportingRunReadinessStatusDto.ReviewRequired)
                ? ReportingRunReadinessStatusDto.ReviewRequired
                : ReportingRunReadinessStatusDto.Ready;
        var blockers = orderedChecks
            .Where(check =>
                check.Status != ReportingRunReadinessStatusDto.Ready
                && (parameters.Finality == ReportingFinalityDto.Final ? check.BlocksFinal : check.BlocksDraft))
            .Select(static check => check.Summary)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static reason => reason, StringComparer.Ordinal)
            .ToArray();
        var hash = ComputeHash(JsonSerializer.Serialize(new
        {
            template = original.ResolvedTemplate,
            parametersCanonicalJson = SerializeParameters(parameters),
            checks = orderedChecks.Select(static check => new
            {
                check.CheckId,
                check.Status,
                check.BlocksDraft,
                check.BlocksFinal,
                evidence = check.EvidenceReferences.OrderBy(static value => value, StringComparer.Ordinal)
            })
        }));
        return original with
        {
            ResolvedParameters = parameters,
            Status = status,
            CanGenerateDraft = canDraft,
            CanGenerateFinal = canFinal,
            Checks = orderedChecks,
            BlockingReasons = blockers,
            EvidenceHash = hash
        };
    }

    private static ReportingRunReadinessDto BuildMissingReconciliationReadiness(
        ReportingTemplateMetadata template,
        ReportingRunReadinessDto readiness,
        ReportingAuthoritativeSourceCapture capture,
        string reason,
        int blockingCount = 1,
        IReadOnlyList<string>? reconciliationEvidence = null)
    {
        var parameters = NormalizeParameters(readiness.ResolvedParameters, capture.Checkpoint);
        var sourceReady = template.ReportWriterGrids is not { Count: > 0 }
            || capture.DatasetRows.Length > 0;
        var sourceCheck = new ReportingRunReadinessCheckDto(
            "authoritative-report-source",
            "Exact authoritative reporting source",
            sourceReady ? ReportingRunReadinessStatusDto.Ready : ReportingRunReadinessStatusDto.Blocked,
            sourceReady
                ? $"The durable ledger checkpoint resolved {capture.DatasetRows.Length} exact as-of row(s)."
                : "The exact durable ledger scope contains no rows required by this report template.",
            sourceReady ? 0 : 1,
            BlocksDraft: !sourceReady,
            BlocksFinal: !sourceReady,
            "/workstation/reporting/data",
            capture.Checkpoint.EvidenceIds);
        var reconciliationCheck = new ReportingRunReadinessCheckDto(
            "exact-reconciliation-evidence",
            "Exact close and reconciliation evidence",
            ReportingRunReadinessStatusDto.Blocked,
            reason,
            blockingCount,
            BlocksDraft: true,
            BlocksFinal: true,
            "/workstation/accounting/close",
            (reconciliationEvidence ?? capture.Checkpoint.EvidenceIds)
                .Concat(capture.Checkpoint.EvidenceIds)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static evidence => evidence, StringComparer.Ordinal)
                .ToArray());
        var checks = readiness.Checks
            .Where(static check =>
                !string.Equals(check.CheckId, "report-dataset", StringComparison.Ordinal)
                && !string.Equals(check.CheckId, "authoritative-report-source", StringComparison.Ordinal)
                && !string.Equals(check.CheckId, "exact-reconciliation-evidence", StringComparison.Ordinal)
                && !string.Equals(check.CheckId, "certified-scope-readiness", StringComparison.Ordinal))
            .Append(sourceCheck)
            .Append(reconciliationCheck)
            .ToArray();
        return RebuildReadiness(readiness, parameters, checks);
    }

    private static ReportingRunParametersDto NormalizeParameters(
        ReportingRunParametersDto parameters,
        ReportingAuthoritativeSourceCheckpoint source)
    {
        var templateParameters = parameters.TemplateParameters
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(
                static pair => pair.Key.Trim(),
                static pair => pair.Value?.Trim() ?? string.Empty,
                StringComparer.Ordinal);
        var dimensions = parameters.Scope.Dimensions;
        var normalizedDimensions = dimensions is null
            ? null
            : dimensions with
            {
                FundId = NormalizeOptional(dimensions.FundId),
                EntityId = NormalizeOptional(dimensions.EntityId),
                SleeveId = NormalizeOptional(dimensions.SleeveId),
                StrategyId = NormalizeOptional(dimensions.StrategyId),
                InvestorId = NormalizeOptional(dimensions.InvestorId),
                CapitalAccountId = NormalizeOptional(dimensions.CapitalAccountId),
                TaxLotId = NormalizeOptional(dimensions.TaxLotId),
                CostCenterId = NormalizeOptional(dimensions.CostCenterId),
                CounterpartyId = NormalizeOptional(dimensions.CounterpartyId),
                OrganizationId = NormalizeOptional(dimensions.OrganizationId),
                PortfolioId = NormalizeOptional(dimensions.PortfolioId),
                BookId = NormalizeOptional(dimensions.BookId),
                AccountId = NormalizeOptional(dimensions.AccountId),
                CustomerId = NormalizeOptional(dimensions.CustomerId),
                VendorId = NormalizeOptional(dimensions.VendorId),
                ProjectId = NormalizeOptional(dimensions.ProjectId),
                ExternalGlDimensions = dimensions.ExternalGlDimensions
                    .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(
                        static pair => pair.Key.Trim(),
                        static pair => pair.Value?.Trim() ?? string.Empty,
                        StringComparer.Ordinal)
            };
        return parameters with
        {
            Scope = parameters.Scope with
            {
                FundProfileId = parameters.Scope.FundProfileId.Trim(),
                EntityId = NormalizeOptional(parameters.Scope.EntityId),
                PortfolioId = NormalizeOptional(parameters.Scope.PortfolioId),
                InvestorId = NormalizeOptional(parameters.Scope.InvestorId),
                Dimensions = normalizedDimensions
            },
            PeriodId = source.AccountingPeriodId,
            LedgerBook = new ReportingLedgerBookSelectionDto(
                Guid.Parse(source.LedgerBookId),
                NormalizeOptional(parameters.LedgerBook.LedgerBookCode)),
            PresentationCurrency = parameters.PresentationCurrency.Trim().ToUpperInvariant(),
            TemplateParameters = templateParameters
        };
    }

    private static void ValidateParameters(ReportingRunParametersDto parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(parameters.Scope);
        ArgumentNullException.ThrowIfNull(parameters.LedgerBook);
        if (!Enum.IsDefined(parameters.Scope.EntityScopeKind)
            || !Enum.IsDefined(parameters.AccountingBasis)
            || !Enum.IsDefined(parameters.ConsolidationLevel)
            || !Enum.IsDefined(parameters.OutputFormat)
            || !Enum.IsDefined(parameters.Finality)
            || string.IsNullOrWhiteSpace(parameters.Scope.FundProfileId)
            || string.IsNullOrWhiteSpace(parameters.PeriodId)
            || string.IsNullOrWhiteSpace(parameters.PresentationCurrency)
            || parameters.AsOfDate == default
            || parameters.LedgerBook.LedgerBookId is null && string.IsNullOrWhiteSpace(parameters.LedgerBook.LedgerBookCode)
            || parameters.Scope.EntityScopeKind == ReportingEntityScopeKindDto.Entity && string.IsNullOrWhiteSpace(parameters.Scope.EntityId)
            || parameters.Scope.EntityScopeKind == ReportingEntityScopeKindDto.Portfolio && string.IsNullOrWhiteSpace(parameters.Scope.PortfolioId)
            || parameters.Scope.EntityScopeKind == ReportingEntityScopeKindDto.Investor && string.IsNullOrWhiteSpace(parameters.Scope.InvestorId)
            || parameters.Finality == ReportingFinalityDto.Final && !parameters.IncludeEvidenceAppendix)
        {
            throw new ReportingAuthoritativeSourceUnavailableException(
                "Reporting parameters contain an invalid enum/scope/consolidation/output/finality selection or omit the evidence appendix required for final output.");
        }

        var expectedConsolidation = parameters.Scope.EntityScopeKind switch
        {
            ReportingEntityScopeKindDto.Entity => ReportingConsolidationLevelDto.Entity,
            ReportingEntityScopeKindDto.Portfolio => ReportingConsolidationLevelDto.Portfolio,
            ReportingEntityScopeKindDto.Investor => ReportingConsolidationLevelDto.Investor,
            _ => ReportingConsolidationLevelDto.Fund
        };
        if (parameters.ConsolidationLevel != expectedConsolidation)
        {
            throw new ReportingAuthoritativeSourceUnavailableException(
                $"Consolidation level '{parameters.ConsolidationLevel}' does not match entity scope '{parameters.Scope.EntityScopeKind}'.");
        }
    }

    private static bool IsRequiredForFinality(
        ReportingRunReadinessCheckDto check,
        ReportingFinalityDto finality) =>
        finality == ReportingFinalityDto.Final ? check.BlocksFinal : check.BlocksDraft;

    private static string SerializeParameters(ReportingRunParametersDto parameters)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("scope");
            writer.WriteString("fundProfileId", parameters.Scope.FundProfileId);
            writer.WriteString("entityScopeKind", parameters.Scope.EntityScopeKind.ToString());
            WriteOptional(writer, "entityId", parameters.Scope.EntityId);
            WriteOptional(writer, "portfolioId", parameters.Scope.PortfolioId);
            WriteOptional(writer, "investorId", parameters.Scope.InvestorId);
            writer.WritePropertyName("dimensions");
            JsonSerializer.Serialize(writer, parameters.Scope.Dimensions);
            writer.WriteEndObject();
            writer.WriteString("periodId", parameters.PeriodId);
            writer.WriteString("asOfDate", parameters.AsOfDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteString("ledgerBookId", parameters.LedgerBook.LedgerBookId?.ToString("D"));
            WriteOptional(writer, "ledgerBookCode", parameters.LedgerBook.LedgerBookCode);
            writer.WriteString("accountingBasis", parameters.AccountingBasis.ToString());
            writer.WriteString("presentationCurrency", parameters.PresentationCurrency);
            writer.WriteString("consolidationLevel", parameters.ConsolidationLevel.ToString());
            writer.WriteString("outputFormat", parameters.OutputFormat.ToString());
            writer.WriteString("finality", parameters.Finality.ToString());
            writer.WriteBoolean("includeSupportingSchedules", parameters.IncludeSupportingSchedules);
            writer.WriteBoolean("includeEvidenceAppendix", parameters.IncludeEvidenceAppendix);
            writer.WriteStartObject("templateParameters");
            foreach (var pair in parameters.TemplateParameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteString(pair.Key, pair.Value);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static ReportingRunParametersDto DeserializeParameters(string canonicalJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalJson);
        using var document = JsonDocument.Parse(canonicalJson);
        var root = document.RootElement;
        var scope = root.GetProperty("scope");
        var dimensions = scope.GetProperty("dimensions").ValueKind == JsonValueKind.Null
            ? null
            : JsonSerializer.Deserialize<LedgerDimensionSetDto>(scope.GetProperty("dimensions").GetRawText());
        var templateParameters = root.GetProperty("templateParameters")
            .EnumerateObject()
            .ToDictionary(static property => property.Name, static property => property.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
        return new ReportingRunParametersDto(
            new ReportingRunScopeDto(
                scope.GetProperty("fundProfileId").GetString()!,
                Enum.Parse<ReportingEntityScopeKindDto>(scope.GetProperty("entityScopeKind").GetString()!, ignoreCase: false),
                ReadOptional(scope, "entityId"),
                ReadOptional(scope, "portfolioId"),
                ReadOptional(scope, "investorId"),
                dimensions),
            root.GetProperty("periodId").GetString()!,
            DateOnly.ParseExact(root.GetProperty("asOfDate").GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            new ReportingLedgerBookSelectionDto(
                Guid.TryParse(ReadOptional(root, "ledgerBookId"), out var ledgerBookId)
                    ? ledgerBookId
                    : null,
                ReadOptional(root, "ledgerBookCode")),
            Enum.Parse<ReportingAccountingBasisDto>(root.GetProperty("accountingBasis").GetString()!, ignoreCase: false),
            root.GetProperty("presentationCurrency").GetString()!,
            Enum.Parse<ReportingConsolidationLevelDto>(root.GetProperty("consolidationLevel").GetString()!, ignoreCase: false),
            Enum.Parse<ReportingOutputFormatDto>(root.GetProperty("outputFormat").GetString()!, ignoreCase: false),
            Enum.Parse<ReportingFinalityDto>(root.GetProperty("finality").GetString()!, ignoreCase: false),
            root.GetProperty("includeSupportingSchedules").GetBoolean(),
            root.GetProperty("includeEvidenceAppendix").GetBoolean(),
            templateParameters);
    }

    private static ReportingAccessScope BuildAccessScope(
        ReportingTemplateMetadata template,
        VersionedReportTemplateIdDto templateId,
        ReportAccessQueryContext accessContext)
    {
        var policy = ReportAccessPolicyEvaluator.Normalize(
            template.AccessPolicy ?? new ReportAccessPolicyDto(
            ReportAccessModeDto.CompanyWide,
            CompanyId: accessContext.CompanyId),
            accessContext.ActorPrincipalId);
        var policyCompanyId = NormalizeOptional(policy.CompanyId) ?? accessContext.CompanyId!.Trim();
        if (!string.Equals(policyCompanyId, accessContext.CompanyId!.Trim(), StringComparison.Ordinal)
            || !ReportAccessPolicyEvaluator.Evaluate(policy with { CompanyId = policyCompanyId }, accessContext).IsAccessible)
        {
            throw new UnauthorizedAccessException(
                "The approved reporting template access policy is not exactly bound to the authenticated company and caller audience.");
        }
        var principals = (policy.Principals ?? [])
            .Where(static principal => !string.IsNullOrWhiteSpace(principal.PrincipalId))
            .Select(static principal => new ReportingAccessPrincipalScope(
                principal.Kind switch
                {
                    ReportAccessPrincipalKindDto.User => ReportingAccessPrincipalKind.User,
                    ReportAccessPrincipalKindDto.Group => ReportingAccessPrincipalKind.Group,
                    ReportAccessPrincipalKindDto.Company => ReportingAccessPrincipalKind.Company,
                    _ => throw new ReportingGovernanceException(
                        $"Unsupported report access principal kind '{principal.Kind}'.")
                },
                principal.PrincipalId.Trim()))
            .Distinct()
            .OrderBy(static principal => principal.Kind)
            .ThenBy(static principal => principal.PrincipalId, StringComparer.Ordinal)
            .ToImmutableArray();
        var mode = policy.Mode switch
        {
            ReportAccessModeDto.Private => ReportingGovernanceAccessMode.Private,
            ReportAccessModeDto.Restricted => ReportingGovernanceAccessMode.Restricted,
            _ => ReportingGovernanceAccessMode.CompanyWide
        };
        var owner = NormalizeOptional(policy.OwnerPrincipalId);
        if (mode == ReportingGovernanceAccessMode.Private && owner is null)
        {
            owner = accessContext.ActorPrincipalId!.Trim();
        }

        var canonicalPolicy = JsonSerializer.Serialize(new
        {
            mode,
            owner,
            allowOwnerAccess = policy.AllowOwnerAccess,
            principals = principals.Select(static principal => new
            {
                kind = principal.Kind.ToString(),
                id = principal.PrincipalId
            }),
            companyId = policyCompanyId,
        });
        return new ReportingAccessScope(
            $"report-template:{templateId.Name}:access",
            templateId.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            mode,
            owner,
            policy.AllowOwnerAccess,
            principals,
            ComputeHash(canonicalPolicy));
    }

    private static void RequireBoundScope(ReportAccessQueryContext context)
    {
        if (!context.RequireBoundScope
            || string.IsNullOrWhiteSpace(context.ActorPrincipalId)
            || string.IsNullOrWhiteSpace(context.TenantId)
            || string.IsNullOrWhiteSpace(context.CompanyId))
        {
            throw new UnauthorizedAccessException(
                "A server-resolved actor, tenant, and company scope is required to certify a reporting run.");
        }
    }

    private static string ComputeParametersHash(ReportingRunParametersDto parameters) =>
        ComputeHash(SerializeParameters(parameters));

    internal static string ComputeCertifiedRowsHash(
        ImmutableArray<IReadOnlyDictionary<string, string>> rows)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var row in rows.IsDefault
                         ? ImmutableArray<IReadOnlyDictionary<string, string>>.Empty
                         : rows)
            {
                writer.WriteStartObject();
                foreach (var pair in row.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WriteString(pair.Key, pair.Value);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return ComputeHash(stream.ToArray());
    }

    private static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string ComputeHash(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsLowercaseSha256(string? value) =>
        IsSha256(value)
        && string.Equals(value, value!.ToLowerInvariant(), StringComparison.Ordinal);

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void WriteOptional(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static string? ReadOptional(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString()
            : null;
}
