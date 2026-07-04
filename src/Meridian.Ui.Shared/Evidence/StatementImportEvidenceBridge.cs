using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Evidence;

public sealed record StatementImportEvidenceBridgeRequest(
    string SourceKind,
    string SourceInstitution,
    string FundAccountId,
    string ExternalAccountId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string ImportedBy);

/// <summary>
/// Retains statement-connector imports in Evidence Vault without moving reconciliation
/// ownership out of Financial Operations.
/// </summary>
public sealed class StatementImportEvidenceBridge(
    IEvidenceArtifactStore store,
    string dataRoot)
{
    private const string StatementRunSubjectKind = "statement-run";

    public async Task<StatementImportCommitResultDto> RetainAsync(
        StatementImportCommitResultDto result,
        StatementImportEvidenceBridgeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(request);

        if (result.EvidenceVaultIdentity is not null)
        {
            return result;
        }

        var retainedSourcePath = ResolveRetainedPath(result.RetainedSourcePath);
        var evidenceRoute = BuildEvidenceWorkbenchRoute(result.RunId);
        var reconciliationRoute = BuildReconciliationRoute(result.RunId);
        var intake = await store.WriteIntakeArtifactAsync(
                new EvidenceVaultIntakeRequestDto(
                    SubjectKind: StatementRunSubjectKind,
                    SubjectId: result.RunId,
                    IntakeChannel: "statement-import",
                    FileName: Path.GetFileName(retainedSourcePath),
                    ContentType: ResolveContentType(retainedSourcePath),
                    SourceSystem: request.SourceInstitution,
                    SourceReference: result.RetainedSourcePath,
                    ReceivedBy: request.ImportedBy,
                    ExtractedFields: BuildExtractedFields(result, request),
                    Linkage: new EvidenceSubjectLinkageDto(
                        EvidenceSubject: $"{StatementRunSubjectKind}/{result.RunId}",
                        RunId: result.RunId,
                        PeriodId: BuildPeriodId(request.PeriodStart, request.PeriodEnd),
                        ReportPackId: null,
                        ReconciliationCaseId: result.CaseIds.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))))
                {
                    Classification = EvidenceDocumentClassificationDto.Statement,
                    Actor = request.ImportedBy,
                    ExtractionStatus = result.CaseCount > 0 || result.BreakCount > 0
                        ? EvidenceExtractionStatusDto.NeedsReview
                        : EvidenceExtractionStatusDto.Extracted,
                    IntakeChannelKind = EvidenceDocumentIntakeChannelDto.ImportedFileReference,
                    ExtractorId = "statement-connector-import",
                    ReviewerState = new EvidenceDocumentReviewStateDto(
                        result.CaseCount > 0 || result.BreakCount > 0
                            ? EvidenceDocumentReviewStatusDto.NeedsReview
                            : EvidenceDocumentReviewStatusDto.Unreviewed),
                    ObjectLinks = BuildObjectLinks(result, request, reconciliationRoute),
                    IntakeSource = new EvidenceDocumentIntakeSourceDto(
                        EvidenceDocumentIntakeSourceKindDto.ImportedFileReference,
                        Path: retainedSourcePath,
                        DisplayName: result.RetainedSourcePath)
                },
                ct)
            .ConfigureAwait(false);

        return result with
        {
            EvidenceVaultIdentity = intake.VaultIdentity,
            EvidenceWorkbenchRoute = evidenceRoute,
            ReconciliationRoute = reconciliationRoute,
            NextActions = BuildNextActions(result)
        };
    }

    private string ResolveRetainedPath(string retainedPath)
    {
        if (string.IsNullOrWhiteSpace(retainedPath))
        {
            throw new InvalidOperationException("Statement import did not return a retained source path.");
        }

        var candidate = retainedPath.Trim();
        var fullPath = Path.IsPathRooted(candidate)
            ? Path.GetFullPath(candidate)
            : Path.GetFullPath(Path.Combine(dataRoot, candidate.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(dataRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, PathComparison))
        {
            throw new InvalidOperationException("Statement import retained source path is outside the configured data root.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Statement import retained source file was not found.", fullPath);
        }

        return fullPath;
    }

    private static IReadOnlyList<EvidenceDocumentLinkDto> BuildObjectLinks(
        StatementImportCommitResultDto result,
        StatementImportEvidenceBridgeRequest request,
        string reconciliationRoute)
    {
        var links = new List<EvidenceDocumentLinkDto>
        {
            new(
                EvidenceDocumentLinkKindDto.StatementRun,
                result.RunId,
                Label: $"Statement run {result.RunId}",
                Route: reconciliationRoute,
                Relationship: "created-reconciliation-run"),
            new(
                EvidenceDocumentLinkKindDto.Account,
                request.FundAccountId,
                Label: $"Fund account {request.FundAccountId}",
                Route: $"/accounting/reconciliation?fundAccountId={Uri.EscapeDataString(request.FundAccountId)}",
                Relationship: "supports-fund-account"),
            new(
                EvidenceDocumentLinkKindDto.Period,
                BuildPeriodId(request.PeriodStart, request.PeriodEnd),
                Label: $"{request.PeriodStart:yyyy-MM-dd} to {request.PeriodEnd:yyyy-MM-dd}",
                Route: reconciliationRoute,
                Relationship: "covers-statement-period"),
            new(
                EvidenceDocumentLinkKindDto.StatementImport,
                result.RunId,
                Label: $"Statement import {result.RunId}",
                Route: reconciliationRoute,
                Relationship: "retains-import-source")
        };

        foreach (var caseId in result.CaseIds.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            links.Add(new EvidenceDocumentLinkDto(
                EvidenceDocumentLinkKindDto.ReconciliationCase,
                caseId,
                Label: $"Reconciliation case {caseId}",
                Route: ResolveReconciliationCaseRoute(result, caseId),
                Relationship: "supports-reconciliation-case"));
        }

        return links;
    }

    private static IReadOnlyList<EvidenceArtifactExtractionFieldDto> BuildExtractedFields(
        StatementImportCommitResultDto result,
        StatementImportEvidenceBridgeRequest request)
        =>
        [
            ReadyField("sourceInstitution", request.SourceInstitution, "broker or custodian source institution"),
            ReadyField("sourceKind", request.SourceKind, "broker or custodian source kind"),
            ReadyField("fundAccountId", request.FundAccountId, "fund account scope"),
            ReadyField("externalAccountId", request.ExternalAccountId, "external account scope"),
            ReadyField("periodStart", request.PeriodStart.ToString("yyyy-MM-dd"), "statement period start"),
            ReadyField("periodEnd", request.PeriodEnd.ToString("yyyy-MM-dd"), "statement period end"),
            ReadyField("recordCount", result.RecordCount.ToString(System.Globalization.CultureInfo.InvariantCulture), "canonical record count"),
            new(
                FieldName: "breakCount",
                ExtractedValue: result.BreakCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ExpectedValue: "0",
                ConfidenceScore: 1.0m,
                ReviewState: result.BreakCount == 0 ? "Accepted" : "NeedsReview",
                ValidationStatus: result.BreakCount == 0 ? EvidenceStatusDto.Ready : EvidenceStatusDto.ReviewRequired,
                ValidationMessage: result.BreakCount == 0
                    ? "Statement import did not create reconciliation breaks."
                    : "Statement import created reconciliation breaks that require queue review.",
                LinkedRecordKind: StatementRunSubjectKind,
                LinkedRecordId: result.RunId)
        ];

    private static EvidenceArtifactExtractionFieldDto ReadyField(
        string fieldName,
        string value,
        string validationMessage)
        => new(
            FieldName: fieldName,
            ExtractedValue: value,
            ExpectedValue: value,
            ConfidenceScore: 1.0m,
            ReviewState: "Accepted",
            ValidationStatus: EvidenceStatusDto.Ready,
            ValidationMessage: validationMessage,
            LinkedRecordKind: null,
            LinkedRecordId: null);

    private static IReadOnlyList<string> BuildNextActions(StatementImportCommitResultDto result)
    {
        var nextActions = new List<string>
        {
            "Open retained statement evidence in Evidence Vault.",
            result.CaseCount > 0 || result.BreakCount > 0
                ? "Review reconciliation cases and linked statement evidence."
                : "Review the statement run and retained source document before close support."
        };

        if (!string.IsNullOrWhiteSpace(result.NextAction))
        {
            nextActions.Add(result.NextAction);
        }

        return nextActions;
    }

    private static string ResolveReconciliationCaseRoute(StatementImportCommitResultDto result, string caseId)
    {
        var index = result.CaseIds
            .Select((value, itemIndex) => new { value, itemIndex })
            .FirstOrDefault(item => string.Equals(item.value, caseId, StringComparison.OrdinalIgnoreCase))
            ?.itemIndex;
        if (index is int routeIndex &&
            routeIndex >= 0 &&
            routeIndex < result.ReconciliationCaseRoutes.Count &&
            !string.IsNullOrWhiteSpace(result.ReconciliationCaseRoutes[routeIndex]))
        {
            return result.ReconciliationCaseRoutes[routeIndex];
        }

        var route = BuildReconciliationRoute(result.RunId)
            + $"&caseId={Uri.EscapeDataString(caseId)}";
        const string casePrefix = "case:";
        if (caseId.StartsWith(casePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var breakId = caseId[casePrefix.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(breakId))
            {
                route += $"&breakId={Uri.EscapeDataString(breakId)}";
            }
        }

        return route;
    }

    private static string BuildEvidenceWorkbenchRoute(string runId)
        => "/accounting/evidence"
           + $"?subjectKind={Uri.EscapeDataString(StatementRunSubjectKind)}"
           + $"&subjectId={Uri.EscapeDataString(runId)}"
           + "&documentClassification=Statement";

    private static string BuildReconciliationRoute(string runId)
        => $"/accounting/reconciliation/match?runId={Uri.EscapeDataString(runId)}";

    private static string BuildPeriodId(DateOnly start, DateOnly end)
        => $"{start:yyyy-MM-dd}..{end:yyyy-MM-dd}";

    private static string ResolveContentType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            ".ofx" or ".qfx" => "application/x-ofx",
            ".xml" => "application/xml",
            ".json" => "application/json",
            _ => "application/octet-stream"
        };

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
