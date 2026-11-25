using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Export;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FundOperationsWorkspaceReadService
{

    public async Task<FundReportPackPreviewDto> PreviewReportPackAsync(
        FundReportPackPreviewRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FundProfileId);
        ct.ThrowIfCancellationRequested();

        var normalizedFundProfileId = request.FundProfileId.Trim();
        var brandingTheme = FundReportArtifactRenderer.ResolveReportBrandingTheme(request.BrandingThemeId, request.BrandingThemeOverride);
        var runs = await LoadRunsAsync(normalizedFundProfileId, ct).ConfigureAwait(false);
        var displayName = ResolveDisplayName(normalizedFundProfileId, runs);
        var currency = ResolveCurrency(request.Currency, []);
        var asOf = request.AsOf ?? DateTimeOffset.UtcNow;
        var ledgerBook = await BuildLedgerBookAsync(normalizedFundProfileId, runs, asOf, ct).ConfigureAwait(false);

        var report = await _reportGenerationService.GenerateAsync(
            new ReportRequest(
                FundId: normalizedFundProfileId,
                AsOf: asOf,
                FundLedger: ledgerBook,
                ReportKind: MapReportKind(request.ReportKind)),
            ct).ConfigureAwait(false);

        var assetClassSections = report.AssetClassSections
            .Select(static section => new FundReportAssetClassSectionDto(
                AssetClass: section.AssetClass,
                Total: section.Total))
            .ToArray();

        return new FundReportPackPreviewDto(
            ReportId: report.ReportId,
            FundProfileId: normalizedFundProfileId,
            DisplayName: displayName,
            ReportKind: request.ReportKind,
            Currency: currency,
            AsOf: asOf,
            GeneratedAt: report.GeneratedAt,
            TotalNetAssets: report.TotalNetAssets,
            TrialBalanceLineCount: report.TrialBalance.Count,
            AssetClassSectionCount: report.AssetClassSections.Count,
            AssetClassSections: assetClassSections,
            BrandingTheme: brandingTheme);
    }

    public async Task<FundReportPackSnapshotDto> GenerateReportPackAsync(
        FundReportPackGenerateRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AuditActor);
        ct.ThrowIfCancellationRequested();

        var generationStopwatch = Stopwatch.StartNew();
        var repository = _reportPackRepository
            ?? throw new InvalidOperationException("Report-pack repository has not been configured.");
        var schemaVersion = ResolveReportPackSchemaVersion(request.ExpectedSchemaVersion);
        var formats = FundReportArtifactRenderer.NormalizeReportFormats(request.Formats);
        var brandingTheme = FundReportArtifactRenderer.ResolveReportBrandingTheme(request.BrandingThemeId, request.BrandingThemeOverride);
        var normalizedFundProfileId = request.FundProfileId.Trim();
        var fundId = TranslateFundProfileId(normalizedFundProfileId);
        var runs = await LoadRunsAsync(normalizedFundProfileId, ct).ConfigureAwait(false);
        var displayName = ResolveDisplayName(normalizedFundProfileId, runs);
        var asOf = request.AsOf ?? DateTimeOffset.UtcNow;
        var accountProjections = await GetAccountProjectionsAsync(fundId, ct).ConfigureAwait(false);
        var accountSummaries = accountProjections.Select(static projection => projection.Summary).ToArray();
        var currency = ResolveCurrency(request.Currency, accountSummaries);
        var ledgerBook = await BuildLedgerBookAsync(normalizedFundProfileId, runs, asOf, ct).ConfigureAwait(false);

        var reportTask = _reportGenerationService.GenerateAsync(
            new ReportRequest(normalizedFundProfileId, asOf, ledgerBook, MapReportKind(request.ReportKind)),
            ct);
        var ledgerTask = BuildLedgerSummaryAsync(
            normalizedFundProfileId,
            displayName,
            FundLedgerScope.Consolidated,
            scopeId: null,
            asOf,
            ledgerBook,
            ct);
        var reconciliationTask = BuildReconciliationSummaryAsync(
            normalizedFundProfileId,
            accountProjections,
            runs,
            accessScope: null,
            ct);
        var navTask = BuildNavSummaryAsync(normalizedFundProfileId, currency, ledgerBook, asOf, ct);

        await Task.WhenAll(reportTask, ledgerTask, reconciliationTask, navTask).ConfigureAwait(false);

        var report = await reportTask.ConfigureAwait(false);
        var ledger = await ledgerTask.ConfigureAwait(false);
        var reconciliation = await reconciliationTask.ConfigureAwait(false);
        var nav = await navTask.ConfigureAwait(false);
        var securityResolvedCount = report.TrialBalance.Count(static row =>
            !string.Equals(row.LookupQuality, "missing", StringComparison.OrdinalIgnoreCase));
        var securityMissingCount = report.TrialBalance.Count(static row =>
            !string.IsNullOrWhiteSpace(row.Symbol)
            && string.Equals(row.LookupQuality, "missing", StringComparison.OrdinalIgnoreCase));
        var auditActor = request.AuditActor.Trim();
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId.Trim();
        var securityValidationResults = await ValidateReportPackSecuritiesAsync(
            report,
            auditActor,
            ct).ConfigureAwait(false);
        var derivedProvenanceToken = ReportPackProvenanceResolver.ResolveDerivedToken(runs, ledgerBook.ConsolidatedJournalEntries());
        var validationIssues = _reportPackValidationService.Validate(new ReportPackValidationContext(
            ReportId: report.ReportId,
            AsOf: asOf,
            Report: report,
            Ledger: ledger,
            Reconciliation: reconciliation,
            RunCount: runs.Count,
            SecurityMissingCount: securityMissingCount,
            Formats: formats,
            StaleReplayCount: 0,
            UnresolvedSecurityMasterConflictCount: securityValidationResults.Count(result =>
                result.Report.Issues.Any(issue => issue.Severity is SecurityValidationSeverityDto.Critical or SecurityValidationSeverityDto.Error)),
            SecurityValidationResults: securityValidationResults,
            DataProvenanceToken: derivedProvenanceToken));
        var status = _reportPackValidationService.ResolveStatus(validationIssues);
        var lifecycleEvents = _reportPackValidationService.BuildGenerationLifecycle(
            auditActor,
            correlationId,
            report.GeneratedAt,
            status);
        var lineagePointers = BuildLineagePointers(report, ledgerBook, runs, reconciliation, asOf);
        var provenance = new FundReportPackProvenanceDto(
            RelatedRunIds: runs.Select(static run => run.RunId).ToArray(),
            JournalEntryCount: ledger.JournalEntryCount,
            LedgerEntryCount: ledger.LedgerEntryCount,
            TrialBalanceLineCount: report.TrialBalance.Count,
            ReconciliationRunCount: reconciliation.RunCount,
            OpenReconciliationBreakCount: reconciliation.OpenBreakCount,
            SecurityResolvedCount: securityResolvedCount,
            SecurityMissingCount: securityMissingCount,
            LineagePointers: lineagePointers,
            SourceSnapshotHash: ComputeSourceSnapshotHash(
                normalizedFundProfileId,
                asOf,
                report,
                ledger,
                reconciliation,
                nav,
                runs),
            SchemaVersion: schemaVersion,
            DataProvenanceToken: derivedProvenanceToken);
        var artifactContents = FundReportArtifactRenderer.BuildReportPackArtifacts(report, formats, brandingTheme, ct);
        generationStopwatch.Stop();
        var warnings = BuildReportPackWarnings(report, reconciliation, runs.Count, securityMissingCount);
        var auditPackReadiness = BuildAuditPackReadiness(
            generationStopwatch.Elapsed,
            warnings,
            validationIssues,
            lifecycleEvents,
            provenance,
            artifactContents,
            report,
            ledger,
            reconciliation,
            runs.Count);
        var snapshot = new FundReportPackSnapshotDto(
            ReportId: report.ReportId,
            FundProfileId: normalizedFundProfileId,
            DisplayName: displayName,
            ReportKind: request.ReportKind,
            Currency: currency,
            AsOf: asOf,
            GeneratedAt: report.GeneratedAt,
            TotalNetAssets: report.TotalNetAssets,
            AuditActor: auditActor,
            CorrelationId: correlationId,
            DecisionRationale: string.IsNullOrWhiteSpace(request.DecisionRationale)
                ? null
                : request.DecisionRationale.Trim(),
            Provenance: provenance,
            Artifacts: [],
            Warnings: warnings,
            ContractName: GovernanceReportPackContract.ContractName,
            SchemaVersion: schemaVersion,
            BrandingTheme: brandingTheme)
        {
            Status = status,
            ValidationIssues = validationIssues,
            LifecycleEvents = lifecycleEvents,
            AuditPackReadiness = auditPackReadiness
        };

        return await repository
            .SaveAsync(snapshot, artifactContents, ct)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<FundReportPackHistoryItemDto>> GetReportPackHistoryAsync(
        string fundProfileId,
        int limit = 20,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        var repository = _reportPackRepository
            ?? throw new InvalidOperationException("Report-pack repository has not been configured.");
        return repository.GetHistoryAsync(fundProfileId.Trim(), limit, ct);
    }

    public Task<FundReportPackSnapshotDto?> GetReportPackAsync(
        Guid reportId,
        CancellationToken ct = default)
    {
        var repository = _reportPackRepository
            ?? throw new InvalidOperationException("Report-pack repository has not been configured.");
        return repository.GetAsync(reportId, ct);
    }

    public async Task<FundReportPackEvidenceBundleDto?> ExportReportPackEvidenceBundleAsync(
        Guid reportId,
        string? auditActor = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var repository = _reportPackRepository
            ?? throw new InvalidOperationException("Report-pack repository has not been configured.");
        var snapshot = await repository.GetAsync(reportId, ct).ConfigureAwait(false);
        if (snapshot is null)
        {
            return null;
        }

        var bundleId = Guid.NewGuid();
        var actor = string.IsNullOrWhiteSpace(auditActor)
            ? snapshot.AuditActor
            : auditActor.Trim();
        var manifestPath = ResolveReportPackArtifactPath(snapshot, "manifest") ?? $"{BuildReportPackFundKey(snapshot.FundProfileId)}/{snapshot.ReportId:N}/manifest.json";
        var provenancePath = ResolveReportPackArtifactPath(snapshot, "provenance") ?? $"{BuildReportPackFundKey(snapshot.FundProfileId)}/{snapshot.ReportId:N}/provenance.json";
        var bundle = new FundReportPackEvidenceBundleDto(
            BundleId: bundleId,
            ReportId: snapshot.ReportId,
            FundProfileId: snapshot.FundProfileId,
            DisplayName: snapshot.DisplayName,
            ReportKind: snapshot.ReportKind,
            Currency: snapshot.Currency,
            AsOf: snapshot.AsOf,
            GeneratedAt: snapshot.GeneratedAt,
            ExportedAtUtc: DateTimeOffset.UtcNow,
            ExportedBy: actor,
            ManifestPath: manifestPath,
            ProvenancePath: provenancePath,
            SourceSnapshotHash: snapshot.Provenance.SourceSnapshotHash,
            Manifest: snapshot,
            Provenance: snapshot.Provenance,
            Approvals: BuildEvidenceBundleApprovals(snapshot),
            SourceLinks: BuildEvidenceBundleSourceLinks(snapshot),
            Artifacts: snapshot.Artifacts,
            Warnings: BuildEvidenceBundleWarnings(snapshot),
            BundleArtifact: null,
            ContractName: snapshot.ContractName,
            SchemaVersion: snapshot.SchemaVersion);

        return await repository
            .SaveEvidenceBundleAsync(snapshot, bundle, ct)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SecurityValidationGateResultDto>> ValidateReportPackSecuritiesAsync(
        ReportPack report,
        string auditActor,
        CancellationToken ct)
    {
        if (_securityValidationGate is null)
        {
            return [];
        }

        var symbols = report.TrialBalance
            .Select(static row => row.Symbol)
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(static symbol => symbol!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        var results = new List<SecurityValidationGateResultDto>(symbols.Length);
        foreach (var symbol in symbols)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await _securityValidationGate
                .ValidateSymbolAsync(
                    symbol,
                    SecurityValidationWorkflowDto.ReportPackEvidence,
                    workflowReference: report.ReportId.ToString("N"),
                    actor: auditActor,
                    persistSnapshot: true,
                    ct)
                .ConfigureAwait(false));
        }

        return results;
    }

    private static int ResolveReportPackSchemaVersion(int? expectedSchemaVersion)
    {
        var schemaVersion = expectedSchemaVersion ?? GovernanceReportPackContract.CurrentSchemaVersion;
        if (schemaVersion != GovernanceReportPackContract.CurrentSchemaVersion)
        {
            throw new ArgumentException(
                $"Unsupported governed report-pack schema version '{schemaVersion}'. Current version is {GovernanceReportPackContract.CurrentSchemaVersion}.",
                nameof(expectedSchemaVersion));
        }

        return schemaVersion;
    }

    // Preserve the internal export seam used by regression tests. Rows already carry decoded
    // certified labels; the renderer must export them verbatim, including literal "None".
    internal static byte[] BuildTrialBalanceCsv(IReadOnlyList<EnrichedLedgerRow> rows, CancellationToken ct)
        => FundReportArtifactRenderer.BuildTrialBalanceCsv(rows, ct);

    private static IReadOnlyList<string> BuildReportPackWarnings(
        ReportPack report,
        ReconciliationSummary reconciliation,
        int runCount,
        int securityMissingCount)
    {
        var warnings = new List<string>();
        if (runCount == 0)
        {
            warnings.Add("No recorded fund-scoped runs contributed ledger data for this report pack.");
        }

        if (report.TrialBalance.Count == 0)
        {
            warnings.Add("The generated report pack contains no trial-balance rows.");
        }

        if (report.AssetClassSections.Count == 0)
        {
            warnings.Add("The generated report pack contains no asset-class sections.");
        }

        if (securityMissingCount > 0)
        {
            warnings.Add($"{securityMissingCount} trial-balance row(s) could not be resolved through Security Master.");
        }

        if (reconciliation.OpenBreakCount > 0)
        {
            warnings.Add($"{reconciliation.OpenBreakCount} open reconciliation break(s) were present at generation time.");
        }

        return warnings;
    }

    private static IReadOnlyList<FundReportPackEvidenceBundleApprovalDto> BuildEvidenceBundleApprovals(
        FundReportPackSnapshotDto snapshot)
        => snapshot.LifecycleEvents
            .Where(static item => item.ToStatus is
                GovernanceReportPackStatusDto.Generated or
                GovernanceReportPackStatusDto.Validated or
                GovernanceReportPackStatusDto.ReviewRequired or
                GovernanceReportPackStatusDto.Approved or
                GovernanceReportPackStatusDto.Published)
            .Select(static item => new FundReportPackEvidenceBundleApprovalDto(
                FromStatus: item.FromStatus,
                ToStatus: item.ToStatus,
                ApprovedAtUtc: item.ChangedAt,
                Actor: item.Actor,
                Reason: item.Reason,
                CorrelationId: item.CorrelationId))
            .OrderBy(static item => item.ApprovedAtUtc)
            .ToArray();

    private static IReadOnlyList<FundReportPackEvidenceBundleSourceLinkDto> BuildEvidenceBundleSourceLinks(
        FundReportPackSnapshotDto snapshot)
        => snapshot.Provenance.LineagePointers
            .Select(static pointer => new FundReportPackEvidenceBundleSourceLinkDto(
                SourceType: pointer.EvidenceType,
                SourceId: pointer.EvidenceId,
                Label: string.IsNullOrWhiteSpace(pointer.DisplayLabel)
                    ? $"{pointer.ScopeType} {pointer.ScopeKey}"
                    : pointer.DisplayLabel.Trim(),
                Route: pointer.Route,
                SourceSystem: string.IsNullOrWhiteSpace(pointer.SourceSystem)
                    ? pointer.ScopeType
                    : pointer.SourceSystem.Trim(),
                RelatedEvidenceIds: pointer.RelatedEvidenceIds ?? [],
                CapturedAtUtc: pointer.CapturedAt))
            .OrderBy(static item => item.SourceType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> BuildEvidenceBundleWarnings(FundReportPackSnapshotDto snapshot)
    {
        var warnings = new List<string>(snapshot.Warnings);
        if (snapshot.AuditPackReadiness is null)
        {
            warnings.Add("Audit-pack readiness metadata is not present on the retained manifest.");
        }

        if (snapshot.LifecycleEvents.Count == 0)
        {
            warnings.Add("No lifecycle approval events are retained on the report-pack manifest.");
        }

        if (snapshot.Provenance.LineagePointers.Count == 0)
        {
            warnings.Add("No source lineage pointers are retained on the report-pack provenance record.");
        }

        return warnings.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string? ResolveReportPackArtifactPath(FundReportPackSnapshotDto snapshot, string artifactKind)
        => snapshot.Artifacts
            .FirstOrDefault(artifact => string.Equals(artifact.ArtifactKind, artifactKind, StringComparison.OrdinalIgnoreCase))
            ?.RelativePath;

    private static string BuildReportPackFundKey(string fundProfileId)
    {
        var trimmed = fundProfileId.Trim();
        var buffer = new StringBuilder(trimmed.Length);
        foreach (var character in trimmed.ToLowerInvariant())
        {
            buffer.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '-');
        }

        var key = buffer.ToString().Trim('-');
        return key.Length == 0 ? "fund" : key.Length <= 96 ? key : key[..96];
    }

    private static FundAuditPackReadinessDto BuildAuditPackReadiness(
        TimeSpan generatedIn,
        IReadOnlyList<string> reportWarnings,
        IReadOnlyList<FundReportPackValidationIssueDto> validationIssues,
        IReadOnlyList<FundReportPackLifecycleEventDto> lifecycleEvents,
        FundReportPackProvenanceDto provenance,
        IReadOnlyList<GovernanceReportPackArtifactContent> artifacts,
        ReportPack report,
        FundLedgerSummary ledger,
        ReconciliationSummary reconciliation,
        int runCount)
    {
        const int slaTargetSeconds = 60;
        var criticalValidationCount = validationIssues.Count(static issue =>
            issue.Severity == GovernanceReportValidationSeverityDto.Critical);
        var approvalEvidenceIds = lifecycleEvents
            .Where(static item => item.ToStatus is GovernanceReportPackStatusDto.Approved
                or GovernanceReportPackStatusDto.Published
                or GovernanceReportPackStatusDto.Retained)
            .Select(static item => item.CorrelationId)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var categories = new[]
        {
            BuildAuditCategory(
                FundAuditEvidenceCategoryKeyDto.SourceRecords,
                "Source records",
                runCount > 0,
                runCount > 0
                    ? $"{runCount} fund-scoped run source record(s) contributed to the pack."
                    : "No retained fund-scoped source run is linked.",
                EvidenceIds(provenance.LineagePointers, "run"),
                UiApiRoutes.RunHistory),
            BuildAuditCategory(
                FundAuditEvidenceCategoryKeyDto.NormalizedActivity,
                "Normalized activity",
                report.TrialBalance.Count > 0,
                report.TrialBalance.Count > 0
                    ? $"{report.TrialBalance.Count} normalized trial-balance row(s) are included."
                    : "No normalized trial-balance rows are included.",
                EvidenceIds(provenance.LineagePointers, "ledger-account"),
                UiApiRoutes.FundReportPacks),
            BuildAuditCategory(
                FundAuditEvidenceCategoryKeyDto.ReconciliationCases,
                "Reconciliation cases",
                reconciliation.RunCount > 0 && reconciliation.OpenBreakCount == 0,
                reconciliation.RunCount == 0
                    ? "No retained reconciliation run is linked."
                    : reconciliation.OpenBreakCount == 0
                        ? $"{reconciliation.RunCount} reconciliation run(s) are linked with no open breaks."
                        : $"{reconciliation.OpenBreakCount} open reconciliation break(s) remain.",
                EvidenceIds(provenance.LineagePointers, "reconciliation-summary"),
                UiApiRoutes.ReconciliationRuns),
            BuildAuditCategory(
                FundAuditEvidenceCategoryKeyDto.LedgerEvidence,
                "Ledger evidence",
                ledger.JournalEntryCount > 0 && ledger.LedgerEntryCount > 0,
                ledger.LedgerEntryCount > 0
                    ? $"{ledger.JournalEntryCount} journal entr{(ledger.JournalEntryCount == 1 ? "y" : "ies")} and {ledger.LedgerEntryCount} ledger entr{(ledger.LedgerEntryCount == 1 ? "y" : "ies")} are linked."
                    : "No journal or ledger evidence is linked.",
                EvidenceIds(provenance.LineagePointers, "ledger-account"),
                UiApiRoutes.FundReportPacks),
            BuildAuditCategory(
                FundAuditEvidenceCategoryKeyDto.Approvals,
                "Approvals",
                approvalEvidenceIds.Length > 0,
                approvalEvidenceIds.Length > 0
                    ? "Approved, published, or retained lifecycle evidence is linked."
                    : "Report-pack approval or publication lifecycle evidence is still required.",
                approvalEvidenceIds,
                UiApiRoutes.FundReportPacks),
            BuildAuditCategory(
                FundAuditEvidenceCategoryKeyDto.ReportPack,
                "Report pack",
                report.TrialBalance.Count > 0 && criticalValidationCount == 0,
                criticalValidationCount == 0
                    ? "Report-pack manifest and provenance are ready for review."
                    : $"{criticalValidationCount} critical validation issue(s) block the report pack.",
                [report.ReportId.ToString("D"), provenance.SourceSnapshotHash],
                UiApiRoutes.FundReportPacks),
            BuildAuditCategory(
                FundAuditEvidenceCategoryKeyDto.Exports,
                "Exports",
                artifacts.Count > 0,
                artifacts.Count > 0
                    ? $"{artifacts.Count} export artifact(s) are prepared for retention."
                    : "No export artifacts were requested.",
                artifacts.Select(static item => item.FileName).ToArray(),
                UiApiRoutes.FundReportPacks),
            BuildAuditCategory(
                FundAuditEvidenceCategoryKeyDto.RestatementLineage,
                "Restatement lineage",
                true,
                "Source snapshot hash is retained so future restatements can point to this frozen baseline.",
                [provenance.SourceSnapshotHash],
                UiApiRoutes.FundReportPacks)
        };
        var missing = categories
            .Where(static category => !category.IsComplete)
            .Select(static category => category.Key)
            .ToArray();
        var readinessWarnings = reportWarnings
            .Concat(validationIssues
                .Where(static issue => issue.Severity != GovernanceReportValidationSeverityDto.Info)
                .Select(static issue => issue.Message))
            .Concat(categories
                .Where(static category => !category.IsComplete)
                .Select(static category => category.Status))
            .Where(static warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new FundAuditPackReadinessDto(
            IsComplete: missing.Length == 0,
            GeneratedInSeconds: Math.Round(generatedIn.TotalSeconds, 3, MidpointRounding.AwayFromZero),
            SlaTargetSeconds: slaTargetSeconds,
            SlaMet: generatedIn.TotalSeconds <= slaTargetSeconds,
            MissingEvidenceCategories: missing,
            Warnings: readinessWarnings,
            EvidenceCategorySummaries: categories);
    }

    private static FundAuditEvidenceCategorySummaryDto BuildAuditCategory(
        FundAuditEvidenceCategoryKeyDto key,
        string label,
        bool isComplete,
        string status,
        IReadOnlyList<string> evidenceIds,
        string? route)
        => new(
            key,
            label,
            isComplete,
            status,
            evidenceIds.Count,
            evidenceIds
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            route);

    private static IReadOnlyList<string> EvidenceIds(
        IReadOnlyList<FundReportPackLineagePointerDto> pointers,
        string evidenceType)
        => pointers
            .Where(pointer => string.Equals(pointer.EvidenceType, evidenceType, StringComparison.OrdinalIgnoreCase))
            .SelectMany(static pointer => new[] { pointer.EvidenceId }.Concat(pointer.RelatedEvidenceIds ?? []))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string ComputeSourceSnapshotHash(
        string fundProfileId,
        DateTimeOffset asOf,
        ReportPack report,
        FundLedgerSummary ledger,
        ReconciliationSummary reconciliation,
        FundNavAttributionSummaryDto nav,
        IReadOnlyList<StrategyRunEntry> runs)
    {
        var source = new
        {
            FundProfileId = fundProfileId,
            AsOf = asOf,
            ReportKind = report.ReportKind.ToString(),
            Runs = runs.OrderBy(static run => run.RunId, StringComparer.Ordinal).Select(static run => new { run.RunId, run.StrategyName, run.FundProfileId, run.StartedAt }).ToArray(),
            Ledger = new { ledger.JournalEntryCount, ledger.LedgerEntryCount, ledger.AssetBalance, ledger.LiabilityBalance, ledger.EquityBalance, ledger.RevenueBalance, ledger.ExpenseBalance },
            Reconciliation = new { reconciliation.RunCount, reconciliation.OpenBreakCount, reconciliation.BreakAmountTotal, reconciliation.SecurityCoverageIssueCount },
            Nav = new
            {
                nav.Currency,
                nav.TotalNav,
                nav.ComponentCount,
                nav.EntityCount,
                nav.SleeveCount,
                nav.VehicleCount,
                AssetClassExposure = nav.AssetClassExposure.OrderBy(static exposure => exposure.AssetClass, StringComparer.Ordinal).ToArray()
            },
            TrialBalance = FundReportArtifactRenderer.OrderTrialBalanceRows(report.TrialBalance),
            AssetClassSections = FundReportArtifactRenderer.OrderAssetClassSections(report.AssetClassSections).Select(static section => new { section.AssetClass, section.Total, RowCount = section.Rows.Count }).ToArray()
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(source, FundReportArtifactRenderer.ReportArtifactJsonOptions);
        return Sha256Digest.Compute(json);
    }

    private static IReadOnlyList<FundReportPackLineagePointerDto> BuildLineagePointers(
        ReportPack report,
        FundLedgerBook ledgerBook,
        IReadOnlyList<StrategyRunEntry> runs,
        ReconciliationSummary reconciliation,
        DateTimeOffset asOf)
    {
        var pointers = new List<FundReportPackLineagePointerDto>();
        var ledgerEvidence = BuildLedgerLineEvidence(ledgerBook, asOf);
        foreach (var run in runs)
        {
            pointers.Add(new FundReportPackLineagePointerDto(
                "report",
                "summary",
                "run",
                run.RunId,
                DisplayLabel: $"{run.StrategyName} ({run.RunId})",
                Route: UiApiRoutes.WithParam(UiApiRoutes.RunsContinuity, "runId", run.RunId),
                SourceSystem: "strategy-run"));
        }

        foreach (var line in report.TrialBalance)
        {
            var lineKey = string.IsNullOrWhiteSpace(line.Symbol)
                ? line.AccountName
                : $"{line.AccountName}:{line.Symbol}";
            var evidenceKey = BuildLedgerEvidenceKey(line.AccountName, line.Symbol);
            ledgerEvidence.TryGetValue(evidenceKey, out var lineEvidence);
            pointers.Add(new FundReportPackLineagePointerDto(
                "line",
                lineKey,
                "ledger-account",
                line.AccountName,
                DisplayLabel: BuildLedgerLineLabel(line),
                Route: BuildLedgerLineRoute(runs, line.AccountName, line.Symbol),
                SourceSystem: "ledger",
                RelatedEvidenceIds: lineEvidence?.LedgerEntryIds,
                EvidenceCount: lineEvidence?.LedgerEntryIds.Count,
                Amount: line.NetBalance,
                CapturedAt: lineEvidence?.LatestTimestamp));
            if (!string.IsNullOrWhiteSpace(line.Symbol))
            {
                var symbol = line.Symbol.Trim();
                pointers.Add(new FundReportPackLineagePointerDto(
                    "line",
                    lineKey,
                    "security",
                    symbol,
                    DisplayLabel: BuildSecurityLineLabel(line),
                    Route: BuildSecurityMasterSearchRoute(symbol),
                    SourceSystem: "security-master",
                    RelatedEvidenceIds: lineEvidence?.JournalEntryIds,
                    EvidenceCount: lineEvidence?.JournalEntryIds.Count,
                    Amount: line.NetBalance,
                    CapturedAt: lineEvidence?.LatestTimestamp));
            }
        }

        if (reconciliation.RunCount > 0)
        {
            pointers.Add(new FundReportPackLineagePointerDto(
                "section",
                "reconciliation",
                "reconciliation-summary",
                $"runs:{reconciliation.RunCount};open-breaks:{reconciliation.OpenBreakCount}",
                DisplayLabel: $"{reconciliation.RunCount} reconciliation run(s), {reconciliation.OpenBreakCount} open break(s)",
                Route: UiApiRoutes.ReconciliationRuns,
                SourceSystem: "reconciliation"));
        }

        return pointers;
    }

    private static IReadOnlyDictionary<string, LedgerLineEvidence> BuildLedgerLineEvidence(
        FundLedgerBook ledgerBook,
        DateTimeOffset asOf)
        => ledgerBook
            .ConsolidatedJournalEntries()
            .Where(entry => entry.Timestamp <= asOf)
            .SelectMany(entry => entry.Lines.Select(line => new { entry.JournalEntryId, entry.Timestamp, Line = line }))
            .GroupBy(item => BuildLedgerEvidenceKey(item.Line.Account.Name, item.Line.Account.Symbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group =>
                {
                    var ordered = group
                        .OrderBy(static item => item.Timestamp)
                        .ThenBy(static item => item.Line.EntryId)
                        .ToArray();
                    return new LedgerLineEvidence(
                        ordered.Select(static item => item.Line.EntryId.ToString("D")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        ordered.Select(static item => item.JournalEntryId.ToString("D")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        ordered.Length == 0 ? null : ordered[^1].Timestamp);
                },
                StringComparer.OrdinalIgnoreCase);

    private static string BuildLedgerEvidenceKey(string accountName, string? symbol)
        => string.IsNullOrWhiteSpace(symbol)
            ? accountName.Trim()
            : $"{accountName.Trim()}:{symbol.Trim()}";

    private static string BuildLedgerLineRoute(
        IReadOnlyList<StrategyRunEntry> runs,
        string accountName,
        string? symbol)
    {
        var baseRoute = runs.Count == 1
            ? UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerTrialBalance, "runId", runs[0].RunId)
            : UiApiRoutes.FundReportPacks;
        var queryParts = new List<string>
        {
            $"accountName={Uri.EscapeDataString(accountName.Trim())}"
        };
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            queryParts.Add($"symbol={Uri.EscapeDataString(symbol.Trim())}");
        }

        return UiApiRoutes.WithQuery(baseRoute, string.Join("&", queryParts));
    }

    private static string BuildSecurityMasterSearchRoute(string symbol)
        => UiApiRoutes.WithQuery(
            UiApiRoutes.WorkstationSecurityMasterSearch,
            $"query={Uri.EscapeDataString(symbol.Trim())}");

    private static string BuildLedgerLineLabel(EnrichedLedgerRow line)
        => string.IsNullOrWhiteSpace(line.Symbol)
            ? $"{line.AccountName} ledger line"
            : $"{line.AccountName} / {line.Symbol.Trim()} ledger line";

    private static string BuildSecurityLineLabel(EnrichedLedgerRow line)
    {
        var symbol = line.Symbol?.Trim();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return "Security Master lookup";
        }

        return string.IsNullOrWhiteSpace(line.DisplayName)
            ? symbol
            : $"{line.DisplayName.Trim()} ({symbol})";
    }

    private sealed record LedgerLineEvidence(
        IReadOnlyList<string> LedgerEntryIds,
        IReadOnlyList<string> JournalEntryIds,
        DateTimeOffset? LatestTimestamp);

    private IReadOnlyList<ReportPackWorkflowRecordDto> BuildReportPackWorkflowRecords(
        IReadOnlyList<FundAccountSummary> accounts,
        DateTimeOffset asOf)
    {
        if (_reportPackWorkflowService is null || accounts.Count == 0)
        {
            return [];
        }

        var period = asOf.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        return accounts
            .SelectMany(account => _reportPackWorkflowService.GetHistory(period, account.AccountId.ToString("D")))
            .OrderByDescending(static record => record.UpdatedAt)
            .ThenByDescending(static record => record.Version)
            .Take(8)
            .ToArray();
    }

    private static IReadOnlyList<ReportPackWorkflowRecordDto> FilterReportPackWorkflowRecords(
        IReadOnlyList<ReportPackWorkflowRecordDto> records,
        ReportAccessQueryContext? accessContext)
    {
        if (accessContext is null)
        {
            return records;
        }

        return ReportPackRunReadService.FilterWorkflowRecords(records, accessContext);
    }

    private static ReportKind MapReportKind(GovernanceReportKindDto reportKind)
        => reportKind switch
        {
            GovernanceReportKindDto.TrialBalance => ReportKind.TrialBalance,
            GovernanceReportKindDto.NavSummary => ReportKind.NavSummary,
            GovernanceReportKindDto.AssetAllocation => ReportKind.AssetAllocation,
            GovernanceReportKindDto.ReconciliationPack => ReportKind.ReconciliationPack,
            GovernanceReportKindDto.PerformanceReport => ReportKind.PerformanceReport,
            GovernanceReportKindDto.HoldingsReport => ReportKind.HoldingsReport,
            GovernanceReportKindDto.CapitalAccountStatement => ReportKind.CapitalAccountStatement,
            GovernanceReportKindDto.InvestorStatement => ReportKind.InvestorStatement,
            GovernanceReportKindDto.BoardPacket => ReportKind.BoardPacket,
            GovernanceReportKindDto.AuditPackage => ReportKind.AuditPackage,
            GovernanceReportKindDto.CertifiedDataset => ReportKind.CertifiedDataset,
            GovernanceReportKindDto.CustomReport => ReportKind.CustomReport,
            _ => ReportKind.TrialBalance
        };
}
