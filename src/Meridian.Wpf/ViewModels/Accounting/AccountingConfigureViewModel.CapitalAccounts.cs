using System;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Contracts.Integrity;
using UtcFormat = Meridian.Ui.Services.FormatHelpers;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class AccountingConfigureViewModel
{
    private async Task LoadCapitalAccountWorkbenchAsync(CancellationToken ct)
    {
        if (_activeFundProfile is null)
        {
            ClearCapitalAccountWorkbenchRows();
            CapitalAccountWorkbenchStatusText = "Select a fund-linked context before loading the Capital Account Workbench.";
            return;
        }

        if (_capitalAccountWorkbenchService is null)
        {
            ClearCapitalAccountWorkbenchRows();
            CapitalAccountWorkbenchStatusText = "Capital Account Workbench service is not registered in this desktop session.";
            return;
        }

        var workbench = await _capitalAccountWorkbenchService
            .GetWorkbenchAsync(_activeFundProfile.FundProfileId, _configuration?.LedgerBookId, ct: ct)
            .ConfigureAwait(false);

        ApplyCapitalAccountWorkbenchRows(workbench);
    }

    private void ApplyCapitalAccountWorkbenchRows(CapitalAccountWorkbenchDto workbench)
    {
        CapitalAccountInvestorRows.ReplaceWith(workbench.InvestorAccounts.Select(account =>
        {
            var payment = account.PaymentIntentEvidence is null
                ? "payment evidence not included"
                : $"{account.PaymentIntentEvidence.Status} | {account.PaymentIntentEvidence.Summary}";
            return new AccountingWorkbenchRow(
                account.CapitalAccountId,
                account.ReadinessLabel,
                $"{AccountingWorkbenchPresentation.FormatSignedCurrencyAmount(account.OpeningNetActivity, account.Currency)} opening -> {AccountingWorkbenchPresentation.FormatSignedCurrencyAmount(account.EndingNetActivity, account.Currency)} ending | {AccountingWorkbenchPresentation.FormatSignedCurrencyAmount(account.NetCapitalActivity, account.Currency)} net",
                $"{account.FundEventCount} event(s); {account.PostedFundEventCount} posted; {account.PublishedReportOutputCount} published statement(s); {account.EvidenceCategorySummary} | {account.EvidenceLinkCount} evidence | {payment}",
                account.ActivityRoute);
        }));

        CapitalAccountAllocationRuleRows.ReplaceWith(workbench.AllocationRules.Select(rule =>
            new AccountingWorkbenchRow(
                rule.Label,
                rule.IsSatisfied ? "Satisfied" : "Needs evidence",
                $"{rule.CapitalAccountId} | {rule.InvestorId ?? "unassigned investor"} | {rule.Basis}",
                $"{rule.Reason} | {rule.EvidenceLinkCount} evidence | required: {AccountingWorkbenchPresentation.FormatRequiredEvidence(rule.RequiredEvidence)}",
                rule.Route ?? string.Empty)));

        CapitalAccountStatementLineageRows.ReplaceWith(workbench.StatementLineage.Select(lineage =>
        {
            var published = lineage.PublishedAtUtc is null
                ? lineage.ReportWorkflowState ?? "not published"
                : $"{lineage.ReportWorkflowState ?? "Published"} by {lineage.PublishedBy ?? "unknown"} at {UtcFormat.FormatUtcTimestamp(lineage.PublishedAtUtc.Value)}";
            var restatement = lineage.HasRestatementLineage
                ? $"{lineage.RestatementReasonCode ?? "Restated"} | {lineage.RestatementChangedLineCount} changed line(s) | {lineage.RestatementEvidenceLinkCount} evidence"
                : lineage.RestatementStatus;
            return new AccountingWorkbenchRow(
                lineage.DisplayName,
                lineage.HasRestatementLineage ? "Restated" : lineage.IsPublished ? "Published" : lineage.IsReportReady ? "Ready" : "Review",
                $"{lineage.ReportOutputType} | {published} | {lineage.ReportLineProvenanceCount} provenance line(s)",
                $"{restatement} | manifest {lineage.PublicationManifestId ?? "none"} | retained {lineage.RetainedManifestPath ?? "none"} | hash {lineage.PublicationEvidenceHash ?? "none"}",
                lineage.ReportOutputRoute ?? lineage.ReportRoute);
        }));

        CapitalAccountAuditDrillThroughRows.ReplaceWith(workbench.AuditDrillThroughs.Select(drill =>
            new AccountingWorkbenchRow(
                drill.Label,
                drill.IsAvailable ? "Available" : "Missing route",
                $"{drill.Kind} | {drill.Summary}",
                $"{drill.EvidenceLinkCount} evidence | related: {AccountingWorkbenchPresentation.FormatRequiredEvidence(drill.RelatedIds)}",
                drill.Route ?? string.Empty)));

        CapitalAccountCapabilityRows.ReplaceWith(
            workbench.LiveCapabilities
                .Select(item => new AccountingWorkbenchRow(item, "Live", "v0.18 Capital Account Workbench slice", "Backed by shared service records and browser/WPF rows.", workbench.WorkbenchRoute))
                .Concat(workbench.PlannedCapabilities.Select(item => new AccountingWorkbenchRow(item, "Planned", "Deferred outside this narrow slice.", "Not represented as live runtime behavior.", string.Empty))));

        CapitalAccountWorkbenchStatusText =
            $"{workbench.StatusLabel}: {workbench.StatusReason} {workbench.InvestorAccountCount} investor account(s), {workbench.AllocationRules.Count} allocation rule(s), {workbench.StatementCount} statement lineage row(s), {workbench.RestatementLineageCount} restatement row(s), {workbench.AuditDrillThroughCount} audit drill-through(s).";
    }

    private void ApplyPrivateCapitalActivityRows(PrivateCapitalActivityProjectionDto? activity)
    {
        if (activity is null)
        {
            ManualJournalFundEventLedgerRecordRows.Clear();
            ManualJournalCapitalAccountRows.Clear();
            ManualJournalCapitalAccountSubledgerRows.Clear();
            ManualJournalPaymentIntentRows.Clear();
            ManualJournalFundEventRows.Clear();
            ManualJournalLedgerImpactRows.Clear();
            ManualJournalReportOutputRows.Clear();
            return;
        }

        ManualJournalFundEventLedgerRecordRows.ReplaceWith(activity.FundEventRecords.Select(record =>
        {
            var reportOutput = string.IsNullOrWhiteSpace(record.PrimaryReportOutputType)
                ? "no primary report"
                : record.PrimaryReportOutputType;
            var reportWorkflow = string.IsNullOrWhiteSpace(record.ReportWorkflowState)
                ? "no workflow"
                : record.ReportWorkflowState;
            var approval = string.IsNullOrWhiteSpace(record.ApprovalId)
                ? "no approval route"
                : $"{record.ApprovalId} via {record.ApprovalRoute ?? "approval route unavailable"}";
            var nextAction = string.IsNullOrWhiteSpace(record.NextActionRoute)
                ? record.NextAction
                : $"{record.NextAction} via {record.NextActionRoute}";
            return new AccountingWorkbenchRow(
                $"{record.FundEventType} | {record.Memo}",
                record.IsPosted ? "Posted" : record.ApprovalState.ToString(),
                $"{AccountingWorkbenchPresentation.FormatSignedCurrencyAmount(record.NetCapitalActivity, record.Currency)} net / {AccountingWorkbenchPresentation.FormatCurrencyAmount(record.GrossAmount, record.Currency)} gross | {AccountingWorkbenchPresentation.FormatSignedCurrencyAmount(record.CapitalAccountOpeningNetActivity, record.Currency)} opening -> {AccountingWorkbenchPresentation.FormatSignedCurrencyAmount(record.CapitalAccountEndingNetActivity, record.Currency)} ending | {record.EffectiveDate:yyyy-MM-dd}",
                $"{record.CapitalAccountSubledgerEntryCount} subledger | {record.LedgerImpactCount} ledger | {record.ReportOutputCount} report | {reportOutput} | {reportWorkflow} | {record.ReportLineProvenanceCount} provenance | readiness {record.ReadinessLabel}: {record.ReadinessReason} | next {nextAction} | {record.EvidenceLinkCount} evidence via {record.EvidenceRoute} | approval {approval} | {record.ValidationIssueCount} issue(s)",
                record.ActivityRoute);
        }));
        ManualJournalCapitalAccountRows.ReplaceWith(activity.CapitalAccounts.Select(account =>
            new AccountingWorkbenchRow(
                account.CapitalAccountId,
                AccountingWorkbenchPresentation.FormatSignedCurrencyAmount(account.NetActivity, account.Currency),
                $"{account.FundEventCount} event(s); last {FormatFundEventDate(account.LastFundEventType, account.LastEffectiveDate)}",
                $"Calls {AccountingWorkbenchPresentation.FormatCurrencyAmount(account.Contributions, account.Currency)}; distributions {AccountingWorkbenchPresentation.FormatCurrencyAmount(account.Distributions, account.Currency)}",
                account.InvestorId ?? string.Empty)));
        ManualJournalCapitalAccountSubledgerRows.ReplaceWith(activity.CapitalAccountSubledgers.Select(subledger =>
            new AccountingWorkbenchRow(
                subledger.CapitalAccountId,
                AccountingWorkbenchPresentation.BuildCapitalAccountSubledgerStatus(subledger),
                $"{AccountingWorkbenchPresentation.FormatSignedCurrencyAmount(subledger.OpeningNetActivity, subledger.Currency)} opening -> {AccountingWorkbenchPresentation.FormatSignedCurrencyAmount(subledger.EndingNetActivity, subledger.Currency)} ending | {AccountingWorkbenchPresentation.FormatSignedCurrencyAmount(subledger.NetCapitalActivity, subledger.Currency)} net | {AccountingWorkbenchPresentation.FormatSubledgerDateRange(subledger)}",
                $"Calls {AccountingWorkbenchPresentation.FormatCurrencyAmount(subledger.Contributions, subledger.Currency)}; distributions {AccountingWorkbenchPresentation.FormatCurrencyAmount(subledger.Distributions, subledger.Currency)}; subscriptions {AccountingWorkbenchPresentation.FormatCurrencyAmount(subledger.Subscriptions, subledger.Currency)}; redemptions {AccountingWorkbenchPresentation.FormatCurrencyAmount(subledger.Redemptions, subledger.Currency)}; fees {AccountingWorkbenchPresentation.FormatCurrencyAmount(subledger.ManagementFees, subledger.Currency)} | {subledger.FundEventCount} event(s); {subledger.ApprovalQueueCount} approval queue; {subledger.PostedFundEventCount} posted; {subledger.PublishedReportOutputCount} published report(s) | readiness {subledger.ReadinessLabel}: {subledger.ReadinessReason}; next {AccountingWorkbenchPresentation.FormatSubledgerNextAction(subledger)} | {subledger.EvidenceLinkCount} evidence; {AccountingWorkbenchPresentation.BuildEvidenceCategoryReadinessSummary(subledger.EvidenceCategories)} | {subledger.ValidationIssueCount} issue(s)",
                subledger.ActivityRoute)));
        ManualJournalPaymentIntentRows.ReplaceWith(activity.PaymentIntents.Select(AccountingWorkbenchPresentation.BuildManualJournalPaymentIntentRow));
        ManualJournalFundEventRows.ReplaceWith(activity.FundEvents.Select(item =>
            new AccountingWorkbenchRow(
                $"{item.FundEventType} | {item.Memo}",
                item.IsPosted ? "Posted" : item.JournalStatus.ToString(),
                $"{AccountingWorkbenchPresentation.FormatSignedCurrencyAmount(item.NetCapitalActivity, item.Currency)} net / {AccountingWorkbenchPresentation.FormatCurrencyAmount(item.GrossAmount, item.Currency)} gross",
                $"{item.CapitalAccountId} | {item.EvidenceLinks.Count} evidence",
                item.FundEventId)));
        ManualJournalLedgerImpactRows.ReplaceWith(activity.LedgerImpacts.Select(impact =>
            new AccountingWorkbenchRow(
                impact.FundEventType,
                impact.IsPostingReady ? "Posting ready" : impact.IsBalanced ? "Review" : "Unbalanced",
                $"{AccountingWorkbenchPresentation.FormatCurrencyAmount(impact.TotalDebits, impact.Currency)} debit / {AccountingWorkbenchPresentation.FormatCurrencyAmount(impact.TotalCredits, impact.Currency)} credit",
                $"{impact.LineCount} line(s) | {impact.EvidenceLinks.Count} evidence | {impact.ValidationIssues.Count} issue(s)",
                impact.CapitalAccountId)));
        ManualJournalReportOutputRows.ReplaceWith(activity.ReportOutputs.Select(output =>
        {
            var workflow = string.IsNullOrWhiteSpace(output.ReportWorkflowState)
                ? output.ReportOutputType
                : output.ReportWorkflowState;
            var reportPack = string.IsNullOrWhiteSpace(output.ReportPackId)
                ? workflow
                : $"{workflow} | {output.ReportPackId}";
            var publication = string.IsNullOrWhiteSpace(output.PublicationManifestId)
                ? "no publication manifest"
                : output.PublicationManifestId;
            var reportOutputRoute = string.IsNullOrWhiteSpace(output.ReportOutputRoute)
                ? output.ReportRoute
                : output.ReportOutputRoute;
            var nextActionRoute = string.IsNullOrWhiteSpace(output.NextActionRoute)
                ? reportOutputRoute
                : output.NextActionRoute;
            var nextAction = string.IsNullOrWhiteSpace(output.NextAction)
                ? "Open report output"
                : output.NextAction;
            var readinessLabel = string.IsNullOrWhiteSpace(output.ReadinessLabel)
                ? output.IsPublished ? "Published" : output.IsReportReady ? "Ready" : "Review"
                : output.ReadinessLabel;
            var readinessReason = string.IsNullOrWhiteSpace(output.ReadinessReason)
                ? "No report-output readiness reason"
                : output.ReadinessReason;

            return new AccountingWorkbenchRow(
                output.DisplayName,
                readinessLabel,
                $"{AccountingWorkbenchPresentation.FormatSignedCurrencyAmount(output.NetCapitalActivity, output.Currency)} | {output.EffectiveDate:yyyy-MM-dd} | {reportPack}",
                $"{output.EvidenceLinkCount} evidence | {output.ValidationIssues.Count} issue(s) | {output.ReportLineProvenanceCount} provenance | {publication} | readiness {readinessLabel}: {readinessReason} | next {nextAction} via {nextActionRoute}",
                nextActionRoute);
        }));
    }

    private static string FormatFundEventDate(string? eventType, DateOnly? effectiveDate)
        => effectiveDate is null
            ? eventType ?? "No effective date"
            : $"{eventType ?? "Fund event"} {effectiveDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    private static string FormatLedgerDimensions(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return "none";
        }

        var values = new[]
            {
                dimensions.FundId,
                dimensions.EntityId,
                dimensions.SleeveId,
                dimensions.StrategyId,
                dimensions.InvestorId,
                dimensions.CapitalAccountId,
                dimensions.InstrumentId?.ToString("D"),
                dimensions.TaxLotId,
                dimensions.CostCenterId,
                dimensions.CounterpartyId
            }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Take(5)
            .ToArray();

        return values.Length == 0 ? "none" : string.Join(" / ", values);
    }
}
