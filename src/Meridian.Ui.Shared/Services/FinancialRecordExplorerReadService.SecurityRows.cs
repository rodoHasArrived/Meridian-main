using System.Globalization;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Row, field, proof-action and impact construction for the security-instrument explorer. Split from
/// the read service so the file that composes every explorer stays navigable; the projection rules
/// these apply are documented on <see cref="FinancialRecordExplorerReadScope"/>.
/// </summary>
public sealed partial class FinancialRecordExplorerReadService
{
    private static FinancialRecordExplorerRowDto BuildSecurityRow(
        StrategyRunSummary run,
        StrategyRunDetail detail,
        WorkstationSecurityReference reference,
        int index,
        SecurityInstrumentEnrichment enrichment,
        bool includeRunIdentity)
    {
        // An unresolved reference has no security id to key on, and the run id is what the fallback
        // used. That makes the record id itself run identity, so a caller without strategy authority
        // gets a positional id instead -- stable within the response, which is all the drill-in needs.
        var recordId = reference.SecurityId != Guid.Empty
            ? $"security:{reference.SecurityId:D}"
            : includeRunIdentity
                ? $"security:{run.RunId}:{index}"
                : $"security:unresolved:{index}";
        var href = reference.SecurityId == Guid.Empty
            ? UiApiRoutes.WorkstationSecurityMasterSearch
            : UiApiRoutes.WithParam(UiApiRoutes.WorkstationSecurityMasterById, "securityId", reference.SecurityId.ToString("D"));
        var usedIn = BuildSecurityUsedIn(detail, reference, href, enrichment, includeRunIdentity);
        var fields = BuildSecurityFields(reference, enrichment);
        var proofActions = BuildSecurityProofActions(reference, href, enrichment);
        var impacts = BuildSecurityImpacts(reference, href, enrichment);
        var trustCell = BuildTrustCell(enrichment.Passport);
        var identifierConfidenceCell = BuildIdentifierConfidenceCell(enrichment.Passport);
        var operationsCell = BuildAssetOperationsCell(enrichment.Readiness);
        var directLendingCell = BuildDirectLendingCell(enrichment);
        var cashFlowCell = BuildCashFlowCell(enrichment.Operations);
        var ledgerCell = BuildLedgerCell(enrichment.Operations);
        var termsCell = BuildTermsCell(enrichment.Operations);
        var reportUsageCell = BuildReportUsageCell(reference, enrichment);
        var evidenceCell = BuildEvidenceCell(reference, enrichment);
        var auditTrailCell = BuildAuditTrailCell(reference, enrichment);
        var selected = new FinancialRecordExplorerSelectedRecordDto(
            recordId,
            "Security instrument",
            reference.DisplayName,
            $"{reference.AssetClass} - {reference.Currency}",
            reference.ResolutionReason ?? "Security reference retained by source-backed portfolio or ledger records.",
            ToneFromCoverage(reference.CoverageStatus),
            Fields: fields,
            ProofActions: proofActions,
            UsedIn: usedIn,
            Impacts: impacts,
            FullRecordHref: href);

        return new FinancialRecordExplorerRowDto(
            recordId,
            "security-instrument",
            reference.DisplayName,
            reference.LookupSource ?? "Security Master",
            reference.CoverageStatus.ToString(),
            ToneFromCoverage(reference.CoverageStatus),
            Cells:
            [
                new("security", reference.DisplayName, LinkHref: href),
                new("assetClass", reference.AssetClass),
                new("currency", reference.Currency),
                new("status", reference.Status.ToString()),
                new("coverage", reference.CoverageStatus.ToString(), Tone: ToneFromCoverage(reference.CoverageStatus)),
                trustCell,
                identifierConfidenceCell,
                operationsCell,
                directLendingCell,
                cashFlowCell,
                ledgerCell,
                termsCell,
                reportUsageCell,
                evidenceCell,
                auditTrailCell,
                new("identifier", reference.PrimaryIdentifier ?? reference.MatchedIdentifierValue ?? "-"),
                new("source", reference.LookupSource ?? "Security Master")
            ],
            selected);
    }

    private async Task<SecurityInstrumentEnrichment> BuildSecurityInstrumentEnrichmentAsync(
        WorkstationSecurityReference reference,
        IReadOnlyList<ReportPackWorkflowRecordDto> reportRecords,
        DirectLendingOperationsReadModelDto? directLendingOperations,
        FinancialRecordExplorerReadScope scope,
        CancellationToken ct)
    {
        var reportLineUsages = CollectSecurityReportLineUsages(reference, reportRecords);
        if (reference.SecurityId == Guid.Empty)
        {
            return new(null, null, null, [], reportLineUsages, []);
        }

        // The rows are the Security Master references a strategy run touched, so a strategy caller is
        // admitted to the explorer -- but the passport is a Security Master surface and AssetOperations
        // is an operations one, and neither route admits a strategy permission. Each is loaded only for
        // a caller who could fetch it head-on; a withheld family is not queried at all.
        InstrumentPassportDto? passport = null;
        if (scope.SecurityMaster && _securityMasterWorkbenchQueryService is not null)
        {
            passport = await _securityMasterWorkbenchQueryService
                .GetInstrumentPassportAsync(reference.SecurityId, fundProfileId: null, ct)
                .ConfigureAwait(false);
        }

        AssetOperationsDetailDto? operations = null;
        AssetOperationsReadinessDto? readiness = null;
        if (scope.AssetOperations && _assetOperationsQueryService is not null)
        {
            operations = await _assetOperationsQueryService
                .GetOperationsAsync(reference.SecurityId, ct)
                .ConfigureAwait(false);
            if (operations?.Subject.SecurityId != reference.SecurityId)
            {
                operations = null;
            }

            readiness = operations?.Readiness;
            if (readiness is null)
            {
                readiness = await _assetOperationsQueryService
                    .GetReadinessAsync(reference.SecurityId, ct)
                    .ConfigureAwait(false);
            }

            if (readiness?.SecurityId != reference.SecurityId)
            {
                readiness = null;
            }
        }

        var journalProofs = operations is null
            ? []
            : await BuildInstrumentJournalProofsAsync(operations, ct).ConfigureAwait(false);

        var directLendingHealth = directLendingOperations?.LoanHealth
            .Where(health => health.SecurityId == reference.SecurityId)
            .ToArray() ?? [];

        return new(passport, operations, readiness, directLendingHealth, reportLineUsages, journalProofs);
    }

    private static IReadOnlyList<FinancialRecordExplorerSummaryItemDto> BuildSecurityFields(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
    {
        var fields = new List<FinancialRecordExplorerSummaryItemDto>
        {
            new("Instrument Identity", BuildInstrumentIdentityLabel(reference), BuildInstrumentIdentityDetail(reference), ToneFromCoverage(reference.CoverageStatus)),
            new("Identifier Map", BuildIdentifierMapSummary(reference, enrichment.Passport), BuildIdentifierMapDetail(reference, enrichment.Passport), ToneFromIdentifierMap(reference, enrichment.Passport)),
            new("Asset Class", reference.AssetClass),
            new("Sub Type", reference.SubType ?? "None"),
            new("Currency", reference.Currency),
            new("Status", reference.Status.ToString()),
            new("Primary Identifier", reference.PrimaryIdentifier ?? "None"),
            new("Matched Provider", reference.MatchedProvider ?? "None"),
            new("Accounting Classification", BuildAccountingClassificationSummary(reference, enrichment.Operations), BuildAccountingClassificationDetail(reference, enrichment.Operations), ToneFromCoverage(reference.CoverageStatus)),
            new("Coverage", reference.CoverageStatus.ToString(), Tone: ToneFromCoverage(reference.CoverageStatus))
        };

        if (enrichment.Passport is not null)
        {
            var passport = enrichment.Passport;
            fields.Add(new(
                "Trust Posture",
                BuildTrustPostureLabel(passport.TrustPosture),
                passport.TrustPosture.Summary,
                ToneFromTrustPosture(passport.TrustPosture.Tone)));
            fields.Add(new(
                "Identifier Confidence",
                BuildIdentifierConfidenceLabel(passport),
                BuildIdentifierConfidenceDetail(passport),
                ToneFromIdentifierConfidence(passport)));
            fields.Add(new(
                "Conflict Posture",
                passport.TrustPosture.HasOpenConflicts
                    ? $"{passport.TrustPosture.OpenConflictCount.ToString(CultureInfo.InvariantCulture)} open conflict{Plural(passport.TrustPosture.OpenConflictCount)}"
                    : "No open conflicts",
                BuildTrustConflictDetail(passport.TrustPosture),
                passport.TrustPosture.HasOpenConflicts ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Success));
        }

        if (enrichment.Operations is not null)
        {
            fields.Add(new(
                "Terms / Obligations",
                BuildTermsObligationsSummary(enrichment.Operations),
                BuildTermsObligationsDetail(enrichment.Operations),
                ToneFromTerms(enrichment.Operations)));
        }

        if (enrichment.Readiness is not null)
        {
            fields.Add(new(
                "AssetOperations Readiness",
                enrichment.Readiness.Status,
                BuildReadinessDetail(enrichment.Readiness),
                ToneFromAssetOperationsReadiness(enrichment.Readiness)));
        }

        if (enrichment.DirectLendingHealth.Count > 0)
        {
            fields.Add(new(
                "Direct Lending Operations",
                BuildDirectLendingSummary(enrichment),
                BuildDirectLendingDetail(enrichment),
                ToneFromDirectLending(enrichment)));
        }

        if (enrichment.Operations is not null)
        {
            fields.Add(new(
                "Projected Cash Flows",
                BuildCashFlowSummary(enrichment.Operations),
                BuildCashFlowDetail(enrichment.Operations),
                ToneFromCashFlows(enrichment.Operations)));
            fields.Add(new(
                "Accounting Projection",
                BuildLedgerProjectionSummary(enrichment.Operations),
                BuildLedgerProjectionDetail(enrichment.Operations),
                ToneFromLedgerProjections(enrichment.Operations)));
            fields.Add(new(
                "Projected Accounting Effect",
                BuildProjectedAccountingEffectSummary(enrichment.Operations),
                BuildProjectedAccountingEffectDetail(enrichment.Operations),
                ToneFromLedgerProjections(enrichment.Operations)));
            fields.Add(new(
                "Reconciliation",
                BuildReconciliationSummary(enrichment.Operations),
                BuildReconciliationDetail(enrichment.Operations),
                ToneFromReconciliationResults(enrichment.Operations)));
        }

        AppendInstrumentJournalProofFields(fields, enrichment);

        if (enrichment.ReportLineUsages.Count > 0)
        {
            fields.Add(new(
                "Reported",
                $"{enrichment.ReportLineUsages.Count.ToString(CultureInfo.InvariantCulture)} retained line{Plural(enrichment.ReportLineUsages.Count)}",
                BuildReportedLineUsageDetail(enrichment.ReportLineUsages),
                FinancialRecordExplorerTone.Success));
        }

        fields.Add(new(
            "Evidence",
            BuildSecurityEvidenceSummary(enrichment),
            BuildSecurityEvidenceDetail(enrichment),
            ToneFromSecurityEvidence(enrichment)));
        fields.Add(new(
            "Audit Trail",
            BuildSecurityAuditSummary(enrichment),
            BuildSecurityAuditDetail(enrichment),
            ToneFromSecurityAudit(enrichment)));

        return fields;
    }

    private static IReadOnlyList<FinancialRecordExplorerProofActionDto> BuildSecurityProofActions(
        WorkstationSecurityReference reference,
        string securityHref,
        SecurityInstrumentEnrichment enrichment)
    {
        var actions = new List<FinancialRecordExplorerProofActionDto>
        {
            new(
                "open-security-master",
                "Open Security Master",
                "Open the retained Security Master record.",
                securityHref,
                reference.SecurityId != Guid.Empty,
                "Security reference is not resolved.",
                ToneFromCoverage(reference.CoverageStatus))
        };

        if (reference.SecurityId != Guid.Empty)
        {
            actions.Add(new(
                "open-instrument-passport",
                "Open instrument passport",
                "Open Security Master passport and trust evidence for this instrument.",
                BuildInstrumentPassportHref(reference.SecurityId),
                enrichment.Passport is not null,
                "Security Master passport evidence is not available.",
                enrichment.Passport is null ? FinancialRecordExplorerTone.Warning : ToneFromTrustPosture(enrichment.Passport.TrustPosture.Tone)));
            actions.Add(new(
                "open-asset-operations",
                "Open AssetOperations",
                "Open operations readiness, projected cash-flow, and ledger-projection evidence.",
                BuildAssetOperationsHref(reference.SecurityId),
                enrichment.Readiness is not null,
                "AssetOperations readiness evidence is not available.",
                ToneFromAssetOperationsReadiness(enrichment.Readiness)));
            actions.Add(new(
                "open-direct-lending-operations",
                "Open direct-lending operations",
                "Open retained direct-lending loan health, collateral, covenant/status, servicing, evidence, journal, reconciliation, and close-blocker posture.",
                BuildDirectLendingHref(enrichment),
                enrichment.DirectLendingHealth.Count > 0,
                "No direct-lending operations posture references this instrument.",
                ToneFromDirectLending(enrichment)));
            actions.Add(new(
                "open-position-transaction",
                "Open position/transaction",
                "Open the retained position or transaction rows that reference this instrument.",
                BuildSecurityPositionTransactionHref(reference, enrichment),
                HasSecurityPositionTransaction(reference, enrichment),
                "No retained position, transaction, or provider-event route references this instrument.",
                HasSecurityPositionTransaction(reference, enrichment) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));
            actions.Add(new(
                "open-reconciliation",
                "Open reconciliation",
                "Open the reconciliation result or run linked to this instrument.",
                BuildSecurityReconciliationHref(enrichment),
                HasSecurityReconciliation(enrichment),
                "No retained reconciliation result or run references this instrument.",
                HasSecurityReconciliation(enrichment) ? ToneFromSecurityReconciliation(enrichment) : FinancialRecordExplorerTone.Warning));
            if (HasSecurityJournal(enrichment))
            {
                actions.Add(new(
                    "open-journal-impact",
                    "Open posted journal",
                    "Open the durable posted journal resolved by exact ledger-book and source-event scope.",
                    BuildSecurityJournalHref(enrichment),
                    IsEnabled: true,
                    DisabledReason: string.Empty,
                    Tone: FinancialRecordExplorerTone.Success));
            }
        }

        if (enrichment.ReportLineUsages.Count > 0)
        {
            actions.Add(new(
                "open-report-line-provenance",
                "Open report-line provenance",
                "Open retained report-line provenance rows that reference this security.",
                BuildSecurityReportLineHref(reference, enrichment),
                true,
                string.Empty,
                FinancialRecordExplorerTone.Info));
        }

        actions.Add(new(
            "open-evidence",
            "Open evidence",
            "Open the retained evidence packet for this instrument's proof chain.",
            BuildSecurityEvidenceHref(reference, enrichment),
            HasSecurityEvidence(enrichment),
            "No retained evidence packet references this instrument.",
            HasSecurityEvidence(enrichment) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));
        actions.Add(new(
            "open-audit-trail",
            "Open audit trail",
            "Open the retained audit graph for this instrument's proof chain.",
            BuildSecurityAuditHref(reference, enrichment),
            HasSecurityAudit(enrichment),
            "No retained audit event references this instrument.",
            HasSecurityAudit(enrichment) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));

        return actions;
    }

    private static IReadOnlyList<FinancialRecordExplorerRelationshipDto> BuildSecurityImpacts(
        WorkstationSecurityReference reference,
        string securityHref,
        SecurityInstrumentEnrichment enrichment)
    {
        var impacts = new List<FinancialRecordExplorerRelationshipDto>
        {
            new("instrument-coverage", "Instrument coverage", $"Coverage state is {reference.CoverageStatus}.", securityHref, ToneFromCoverage(reference.CoverageStatus))
        };
        var positionTransactionHref = BuildSecurityPositionTransactionHref(reference, enrichment);
        impacts.Add(new(
            "position-transaction",
            "Position / transaction",
            HasSecurityPositionTransaction(reference, enrichment)
                ? "Retained position, transaction, or provider-event evidence references this instrument."
                : "No retained position, transaction, or provider-event evidence references this instrument.",
            positionTransactionHref,
            HasSecurityPositionTransaction(reference, enrichment) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));

        if (enrichment.Passport is not null)
        {
            var passportHref = BuildInstrumentPassportHref(enrichment.Passport.SecurityId);
            impacts.Add(new(
                "instrument-passport",
                "Instrument passport",
                enrichment.Passport.TrustPosture.Summary,
                passportHref,
                ToneFromTrustPosture(enrichment.Passport.TrustPosture.Tone)));

            if (enrichment.Passport.TrustPosture.HasOpenConflicts)
            {
                impacts.Add(new(
                    "security-master-conflicts",
                    "Conflict blockers",
                    $"{enrichment.Passport.TrustPosture.OpenConflictCount.ToString(CultureInfo.InvariantCulture)} Security Master conflict{Plural(enrichment.Passport.TrustPosture.OpenConflictCount)} require steward review.",
                    passportHref,
                FinancialRecordExplorerTone.Warning));
            }
        }

        if (enrichment.Readiness is not null)
        {
            var assetHref = BuildAssetOperationsHref(enrichment.Readiness.SecurityId);
            impacts.Add(new(
                "asset-operations-readiness",
                "AssetOperations readiness",
                BuildReadinessDetail(enrichment.Readiness),
                assetHref,
                ToneFromAssetOperationsReadiness(enrichment.Readiness)));

            if (enrichment.Readiness.MissingCapabilities.Count > 0 || enrichment.Readiness.Warnings.Count > 0)
            {
                impacts.Add(new(
                    "asset-operations-validation-blockers",
                    "Validation blockers",
                    BuildReadinessBlockerDetail(enrichment.Readiness),
                    assetHref,
                    FinancialRecordExplorerTone.Warning));
            }
        }

        impacts.Add(new(
            "direct-lending-operations",
            "Direct-lending operations",
            enrichment.DirectLendingHealth.Count > 0
                ? BuildDirectLendingDetail(enrichment)
                : "No direct-lending operations read model references this instrument.",
            BuildDirectLendingHref(enrichment),
            ToneFromDirectLending(enrichment)));

        if (enrichment.Operations is not null)
        {
            var assetHref = BuildAssetOperationsHref(enrichment.Operations.Subject.SecurityId);
            impacts.Add(new(
                "terms-obligations",
                "Terms / obligations",
                BuildTermsObligationsDetail(enrichment.Operations),
                assetHref,
                ToneFromTerms(enrichment.Operations)));
            impacts.Add(new(
                "projected-cash-flows",
                "Projected cash flows",
                BuildCashFlowDetail(enrichment.Operations),
                assetHref,
                ToneFromCashFlows(enrichment.Operations)));
            impacts.Add(new(
                "reconciliation",
                "Reconciliation",
                BuildReconciliationDetail(enrichment.Operations),
                BuildSecurityReconciliationHref(enrichment),
                ToneFromReconciliationResults(enrichment.Operations)));
            impacts.Add(new(
                "ledger-projection",
                "Accounting projection",
                BuildLedgerProjectionDetail(enrichment.Operations),
                assetHref,
                ToneFromLedgerProjections(enrichment.Operations)));
            impacts.Add(new(
                "projected-accounting-effect",
                "Projected accounting effect",
                BuildProjectedAccountingEffectDetail(enrichment.Operations),
                assetHref,
                ToneFromLedgerProjections(enrichment.Operations)));
        }

        AppendInstrumentJournalProofImpacts(impacts, reference, enrichment);

        if (enrichment.ReportLineUsages.Count > 0)
        {
            impacts.Add(new(
                "report-line",
                "Reported line",
                BuildReportedLineUsageDetail(enrichment.ReportLineUsages),
                BuildSecurityReportLineHref(reference, enrichment),
                FinancialRecordExplorerTone.Success));
        }
        impacts.Add(new(
            "evidence",
            "Evidence",
            BuildSecurityEvidenceDetail(enrichment),
            BuildSecurityEvidenceHref(reference, enrichment),
            ToneFromSecurityEvidence(enrichment)));
        impacts.Add(new(
            "audit-event",
            "Audit event",
            BuildSecurityAuditDetail(enrichment),
            BuildSecurityAuditHref(reference, enrichment),
            ToneFromSecurityAudit(enrichment)));

        return impacts;
    }
}
