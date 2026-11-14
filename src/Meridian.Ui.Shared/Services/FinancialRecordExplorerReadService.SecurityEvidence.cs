using System.Globalization;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FinancialRecordExplorerReadService
{
    private static string BuildReportUsageSummary(IReadOnlyList<SecurityReportLineUsage> usages)
    {
        if (usages.Count == 0)
        {
            return "No report usage";
        }

        var firstLine = usages
            .Select(static usage => usage.Line.LineKey)
            .FirstOrDefault(HasText);
        return usages.Count == 1 && HasText(firstLine)
            ? firstLine!
            : $"{usages.Count.ToString(CultureInfo.InvariantCulture)} report line{Plural(usages.Count)}";
    }

    private static string BuildSecurityEvidenceSummary(SecurityInstrumentEnrichment enrichment)
    {
        var count = CountSecurityEvidenceAnchors(enrichment);
        return count == 0
            ? "No evidence"
            : $"{count.ToString(CultureInfo.InvariantCulture)} evidence anchor{Plural(count)}";
    }

    private static string BuildSecurityEvidenceDetail(SecurityInstrumentEnrichment enrichment)
    {
        var reportLineEvidence = enrichment.ReportLineUsages
            .Select(static usage => usage.Line.EvidenceId)
            .FirstOrDefault(HasText);
        if (HasText(reportLineEvidence))
        {
            return $"Report-line evidence {reportLineEvidence} anchors this instrument's retained proof chain.";
        }

        var reconciliationEvidence = enrichment.Operations?.ReconciliationResults
            .Select(static result => result.EvidenceLink)
            .FirstOrDefault(HasText);
        if (HasText(reconciliationEvidence))
        {
            return $"AssetOperations reconciliation evidence is retained at {reconciliationEvidence}.";
        }

        var termsHash = enrichment.Operations?.TermsHistory
            .Select(static terms => terms.TermsHash)
            .FirstOrDefault(HasText);
        return HasText(termsHash)
            ? $"Terms hash {termsHash} is retained as instrument evidence."
            : "No retained evidence anchor is available.";
    }

    private static FinancialRecordExplorerTone ToneFromSecurityEvidence(SecurityInstrumentEnrichment enrichment)
        => CountSecurityEvidenceAnchors(enrichment) == 0
            ? FinancialRecordExplorerTone.Warning
            : FinancialRecordExplorerTone.Info;

    private static string BuildSecurityAuditSummary(SecurityInstrumentEnrichment enrichment)
    {
        var count = CountSecurityAuditEvents(enrichment);
        return count == 0
            ? "No audit"
            : $"{count.ToString(CultureInfo.InvariantCulture)} audit event{Plural(count)}";
    }

    private static string BuildSecurityAuditDetail(SecurityInstrumentEnrichment enrichment)
    {
        var latestWorkflowAudit = enrichment.Operations?.WorkflowAudit
            .OrderByDescending(static item => item.RecordedAt)
            .FirstOrDefault();
        if (latestWorkflowAudit is not null)
        {
            return $"{latestWorkflowAudit.EventType} audit event retained from {latestWorkflowAudit.SourceDomain}: {latestWorkflowAudit.Summary}";
        }

        if (enrichment.ReportLineUsages.Any(static usage => HasText(usage.Line.EvidenceId)))
        {
            return "Report-line evidence id links this instrument to retained audit-packet history.";
        }

        return enrichment.Passport is null
            ? "No retained audit event is available."
            : $"Instrument passport was retrieved at {enrichment.Passport.RetrievedAtUtc.ToString("u", CultureInfo.InvariantCulture)}.";
    }

    private static FinancialRecordExplorerTone ToneFromSecurityAudit(SecurityInstrumentEnrichment enrichment)
        => CountSecurityAuditEvents(enrichment) == 0
            ? FinancialRecordExplorerTone.Warning
            : FinancialRecordExplorerTone.Info;

    private static int CountSecurityEvidenceAnchors(SecurityInstrumentEnrichment enrichment)
        => enrichment.ReportLineUsages.Count(static usage => HasText(usage.Line.EvidenceId)) +
           enrichment.InstrumentJournalProofs.Sum(static proof => proof.Lineage.TriggerEvent.EvidenceLinks.Count) +
           (enrichment.Operations?.ActualActivity.Count(static activity => HasText(activity.EvidenceLink)) ?? 0) +
           (enrichment.Operations?.ReconciliationResults.Count(static result => HasText(result.EvidenceLink)) ?? 0) +
           (enrichment.Operations?.TermsHistory.Count(static terms => HasText(terms.TermsHash)) ?? 0);

    private static int CountSecurityAuditEvents(SecurityInstrumentEnrichment enrichment)
        => (enrichment.Operations?.WorkflowAudit.Count ?? 0) +
           (enrichment.Passport?.LifecycleEvents.Count ?? 0) +
           enrichment.InstrumentJournalProofs.Count(static proof => proof.Journal is not null) +
           enrichment.ReportLineUsages.Count(static usage => HasText(usage.Line.EvidenceId));

    private static bool HasSecurityPositionTransaction(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
        => reference.SecurityId != Guid.Empty ||
           enrichment.ReportLineUsages.Any(static usage =>
               HasText(usage.Line.ProviderEventId) ||
               HasText(usage.Line.SourceSessionId) ||
               HasText(usage.Line.SourceId));

    private static bool HasSecurityReconciliation(SecurityInstrumentEnrichment enrichment)
        => enrichment.Operations?.ReconciliationResults.Count > 0 ||
           enrichment.ReportLineUsages.Any(static usage =>
               HasText(usage.Line.ReconciliationRunId) ||
               HasText(usage.Line.ReconciliationCaseId));

    private static bool HasSecurityJournal(SecurityInstrumentEnrichment enrichment)
        => enrichment.InstrumentJournalProofs.Any(static proof => proof.Journal is not null);

    private static bool HasSecurityEvidence(SecurityInstrumentEnrichment enrichment)
        => CountSecurityEvidenceAnchors(enrichment) > 0;

    private static bool HasSecurityAudit(SecurityInstrumentEnrichment enrichment)
        => CountSecurityAuditEvents(enrichment) > 0 || enrichment.Passport is not null;

    private static FinancialRecordExplorerTone ToneFromSecurityReconciliation(SecurityInstrumentEnrichment enrichment)
    {
        if (enrichment.Operations is not null && enrichment.Operations.ReconciliationResults.Count > 0)
        {
            return ToneFromReconciliationResults(enrichment.Operations);
        }

        var outcome = enrichment.ReportLineUsages
            .Select(static usage => usage.Line.ReconciliationOutcome)
            .FirstOrDefault(HasText);
        return HasText(outcome) ? ToneFromReconciliationOutcome(outcome) : FinancialRecordExplorerTone.Warning;
    }

    private static FinancialRecordExplorerTone ToneFromSecurityJournal(SecurityInstrumentEnrichment enrichment)
        => enrichment.InstrumentJournalProofs.Any(static proof => proof.Journal is not null)
            ? FinancialRecordExplorerTone.Success
            : FinancialRecordExplorerTone.Warning;

    private static string BuildSecurityPositionTransactionHref(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
    {
        var usage = enrichment.ReportLineUsages.FirstOrDefault();
        return BuildFinancialRecordExplorerHref(
            PortfolioExplorerId,
            BuildSecurityQueryParameters(reference, usage?.Line));
    }

    private static string BuildSecurityReconciliationHref(SecurityInstrumentEnrichment enrichment)
    {
        var reportLineHref = enrichment.ReportLineUsages
            .Select(static usage => BuildReportLineReconciliationHref(usage.Line))
            .FirstOrDefault(HasText);
        if (HasText(reportLineHref))
        {
            return reportLineHref!;
        }

        var securityId = enrichment.Operations?.Subject.SecurityId ?? Guid.Empty;
        return securityId == Guid.Empty ? string.Empty : BuildAssetOperationsHref(securityId);
    }

    private static string BuildSecurityReportLineHref(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
    {
        var usage = enrichment.ReportLineUsages.FirstOrDefault();
        return BuildFinancialRecordExplorerHref(
            ReportLineProvenanceExplorerId,
            BuildSecurityQueryParameters(reference, usage?.Line));
    }

    private static string BuildSecurityEvidenceHref(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
    {
        var reportLineHref = enrichment.ReportLineUsages
            .Select(static usage => BuildReportLineAuditHref(usage.Line))
            .FirstOrDefault(HasText);
        if (HasText(reportLineHref))
        {
            return reportLineHref!;
        }

        var reconciliationEvidence = enrichment.Operations?.ReconciliationResults
            .Select(static result => result.EvidenceLink)
            .FirstOrDefault(HasText);
        if (HasText(reconciliationEvidence))
        {
            return reconciliationEvidence!;
        }

        var subjectId = reference.SecurityId != Guid.Empty
            ? reference.SecurityId.ToString("D")
            : reference.PrimaryIdentifier ?? reference.MatchedIdentifierValue ?? reference.DisplayName;
        return HasText(subjectId)
            ? BuildEvidenceSubjectRoute(UiApiRoutes.WorkstationEvidenceSubjectPacket, "security-instrument", subjectId!)
            : string.Empty;
    }

    private static string BuildSecurityAuditHref(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
    {
        var subjectId = reference.SecurityId != Guid.Empty
            ? reference.SecurityId.ToString("D")
            : reference.PrimaryIdentifier ?? reference.MatchedIdentifierValue ?? reference.DisplayName;
        if (HasText(subjectId))
        {
            return BuildEvidenceSubjectRoute(
                UiApiRoutes.WorkstationEvidenceSubjectGraph,
                "security-instrument",
                subjectId!);
        }

        return BuildSecurityEvidenceHref(reference, enrichment);
    }

    private static string BuildDirectLendingSummary(SecurityInstrumentEnrichment enrichment)
    {
        var count = enrichment.DirectLendingHealth.Count;
        if (count == 0)
        {
            return "No linked loans";
        }

        var blocked = enrichment.DirectLendingHealth.Count(static health => IsNegativeStatus(health.HealthStatus));
        return blocked > 0
            ? $"{blocked.ToString(CultureInfo.InvariantCulture)}/{count.ToString(CultureInfo.InvariantCulture)} loan health blocker{Plural(blocked)}"
            : $"{count.ToString(CultureInfo.InvariantCulture)} linked loan{Plural(count)}";
    }

    private static string BuildDirectLendingDetail(SecurityInstrumentEnrichment enrichment)
    {
        if (enrichment.DirectLendingHealth.Count == 0)
        {
            return "No direct-lending operations read model row references this Security Master instrument.";
        }

        return string.Join(
            "; ",
            enrichment.DirectLendingHealth
                .OrderBy(static health => health.FacilityName, StringComparer.OrdinalIgnoreCase)
                .Select(static health => $"{health.FacilityName}: {health.HealthStatus} - {health.HealthDetail}"));
    }

    private static string BuildDirectLendingHref(SecurityInstrumentEnrichment enrichment)
        => enrichment.DirectLendingHealth.FirstOrDefault()?.Route ?? "/api/loans/operations";

    private static IReadOnlyList<FinancialRecordExplorerRelationshipDto> BuildSecurityUsedIn(
        StrategyRunDetail detail,
        WorkstationSecurityReference reference,
        string href,
        SecurityInstrumentEnrichment enrichment,
        bool includeRunIdentity)
    {
        var relationships = new List<FinancialRecordExplorerRelationshipDto>();

        // Both of these say that a particular run's portfolio or ledger touched this security, and the
        // second addresses the trial balance by run id. That is run identity, and the routes behind
        // them admit only the strategy permissions, so a Security Master-only caller is not offered a
        // link it would be refused.
        if (includeRunIdentity &&
            detail.Portfolio?.Positions.Any(position => IsSameSecurity(position.Security, reference)) == true)
        {
            relationships.Add(new("portfolio-position", "Portfolio position", "Referenced by retained portfolio position rows.", UiApiRoutes.WorkstationPortfolio));
        }

        if (includeRunIdentity &&
            detail.Ledger?.TrialBalance.Any(line => IsSameSecurity(line.Security, reference)) == true)
        {
            relationships.Add(new("ledger-line", "Ledger trial balance", "Referenced by retained ledger trial-balance rows.", UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerTrialBalance, "runId", detail.Summary.RunId)));
        }

        if (enrichment.ReportLineUsages.Count > 0)
        {
            relationships.Add(new(
                "report-line-provenance",
                "Report-line provenance",
                $"{enrichment.ReportLineUsages.Count.ToString(CultureInfo.InvariantCulture)} published or restated report line{Plural(enrichment.ReportLineUsages.Count)} with publication evidence reference this security.",
                BuildReportLineProvenanceHref(),
                FinancialRecordExplorerTone.Success));
        }

        if (enrichment.Operations?.ReconciliationResults.Count > 0)
        {
            relationships.Add(new(
                "asset-operations-reconciliation",
                "AssetOperations reconciliation",
                $"{enrichment.Operations.ReconciliationResults.Count.ToString(CultureInfo.InvariantCulture)} retained reconciliation result{Plural(enrichment.Operations.ReconciliationResults.Count)} reference projected or actual activity for this security.",
                BuildAssetOperationsHref(enrichment.Operations.Subject.SecurityId),
                ToneFromReconciliationResults(enrichment.Operations)));
        }

        AppendInstrumentJournalProofUsedIn(relationships, reference, enrichment);

        if (relationships.Count == 0)
        {
            relationships.Add(new("security-master", "Security Master", "Reference is retained by Security Master lookup.", href));
        }

        return relationships;
    }

}
