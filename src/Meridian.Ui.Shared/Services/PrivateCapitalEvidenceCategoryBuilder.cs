using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Shared.Services;

internal static class PrivateCapitalEvidenceCategoryBuilder
{
    public static IReadOnlyList<PrivateCapitalEvidenceCategoryDto> BuildForFundEvent(
        PrivateCapitalFundEventDto fundEvent,
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> subledgerEntries,
        IReadOnlyList<PrivateCapitalLedgerImpactDto> ledgerImpacts,
        IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs,
        string? approvalRoute,
        PrivateCapitalPaymentIntentEvidenceDto? paymentIntentEvidence = null)
    {
        var sourceLinks = Normalize(fundEvent.EvidenceLinks);
        var subledgerLinks = Normalize(subledgerEntries.SelectMany(static item => item.EvidenceLinks));
        var ledgerLinks = Normalize(ledgerImpacts
            .SelectMany(static item => item.EvidenceLinks)
            .Concat(ledgerImpacts.SelectMany(static item => item.Lines.Select(static line => line.EvidenceLink ?? string.Empty))));
        var approvalLinks = Normalize(BuildApprovalLinks(fundEvent.ApprovalId, approvalRoute));
        var reportLinks = Normalize(reportOutputs.SelectMany(static item => item.EvidenceLinks));
        paymentIntentEvidence ??= PrivateCapitalPaymentIntentEvidenceBuilder.BuildForFundEvent(
            fundEvent,
            sourceLinks,
            null);

        return new[]
        {
            BuildCategory(
                "source-support",
                "Source support",
                sourceLinks.Count > 0,
                sourceLinks.Count > 0
                    ? "Source documents or retained evidence links support the fund event."
                    : "Source support is missing for the fund event.",
                sourceLinks,
                new[] { "Source document or retained evidence link" }),
            BuildCategory(
                "capital-account-subledger",
                "Capital-account subledger",
                subledgerEntries.Count > 0 && subledgerLinks.Count > 0,
                subledgerEntries.Count > 0
                    ? "Capital-account impact is represented in the subledger."
                    : "Capital-account impact has not been represented in the subledger.",
                subledgerLinks,
                new[] { "Capital-account impact" }),
            BuildCategory(
                "ledger-impact",
                "Ledger impact",
                ledgerImpacts.Count > 0 && ledgerImpacts.All(static item => item.IsPostingReady) && ledgerLinks.Count > 0,
                ledgerImpacts.Count > 0
                    ? "Balanced ledger impact and line evidence are available for the fund event."
                    : "Ledger impact is missing for the fund event.",
                ledgerLinks,
                new[] { "Balanced ledger impact", "Ledger line evidence" }),
            BuildCategory(
                "approval-state",
                "Approval state",
                IsApprovalReady(fundEvent.JournalStatus, approvalLinks),
                fundEvent.ApprovalId is null
                    ? "Approval reference is missing for the fund event."
                    : "Approval reference links the fund event to its review state.",
                approvalLinks,
                new[] { "Approval reference" }),
            BuildPaymentIntentCategory(paymentIntentEvidence),
            BuildCashEvidenceCategory(paymentIntentEvidence),
            BuildCategory(
                "report-output",
                "Report output",
                reportOutputs.Count > 0 && reportOutputs.All(static item => IsReportOutputEvidenceReady(item)) && reportLinks.Count > 0,
                reportOutputs.Count > 0
                    ? "Governed report outputs are linked to the fund event."
                    : "Governed report output is missing for the fund event.",
                reportLinks,
                new[] { "Governed report output" })
        };
    }

    public static IReadOnlyList<PrivateCapitalEvidenceCategoryDto> BuildForCapitalAccountSubledger(
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> subledgerEntries,
        IReadOnlyList<PrivateCapitalLedgerImpactDto> ledgerImpacts,
        IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs,
        PrivateCapitalPaymentIntentEvidenceDto? paymentIntentEvidence = null)
    {
        var sourceLinks = Normalize(records.SelectMany(static item => item.FundEvent.EvidenceLinks));
        var subledgerLinks = Normalize(subledgerEntries.SelectMany(static item => item.EvidenceLinks));
        var ledgerLinks = Normalize(ledgerImpacts
            .SelectMany(static item => item.EvidenceLinks)
            .Concat(ledgerImpacts.SelectMany(static item => item.Lines.Select(static line => line.EvidenceLink ?? string.Empty))));
        var approvalLinks = Normalize(records.SelectMany(static item => BuildApprovalLinks(item.ApprovalId, item.ApprovalRoute)));
        var reportLinks = Normalize(reportOutputs.SelectMany(static item => item.EvidenceLinks));
        paymentIntentEvidence ??= PrivateCapitalPaymentIntentEvidenceBuilder.BuildForSubledger(
            records,
            sourceLinks
                .Concat(subledgerLinks)
                .Concat(ledgerLinks)
                .Concat(reportLinks),
            records.FirstOrDefault()?.Currency ?? "USD",
            records.Select(static item => (DateOnly?)item.EffectiveDate).DefaultIfEmpty(null).Max(),
            records.Sum(static item => item.NetCapitalActivity),
            records.FirstOrDefault()?.EvidenceRoute);

        return new[]
        {
            BuildCategory(
                "source-support",
                "Source support",
                sourceLinks.Count > 0,
                sourceLinks.Count > 0
                    ? "Source support is retained for this capital account's fund events."
                    : "Source support is missing for this capital account.",
                sourceLinks,
                new[] { "Source document or retained evidence link" }),
            BuildCategory(
                "capital-account-subledger",
                "Capital-account subledger",
                subledgerEntries.Count > 0 && subledgerLinks.Count > 0,
                subledgerEntries.Count > 0
                    ? "Capital-account impacts are represented in the running subledger."
                    : "No capital-account subledger entries are available.",
                subledgerLinks,
                new[] { "Capital-account impact" }),
            BuildCategory(
                "ledger-impact",
                "Ledger impact",
                ledgerImpacts.Count > 0 && ledgerImpacts.All(static item => item.IsPostingReady) && ledgerLinks.Count > 0,
                ledgerImpacts.Count > 0
                    ? "Ledger impacts are linked to this capital account."
                    : "Ledger impact is missing for this capital account.",
                ledgerLinks,
                new[] { "Balanced ledger impact", "Ledger line evidence" }),
            BuildCategory(
                "approval-state",
                "Approval state",
                records.Count > 0 &&
                records.All(static item => IsApprovalReady(
                    item.ApprovalState,
                    Normalize(BuildApprovalLinks(item.ApprovalId, item.ApprovalRoute)))),
                approvalLinks.Count > 0
                    ? "Approval references are linked to this capital account's fund events."
                    : "Approval references are missing for this capital account.",
                approvalLinks,
                new[] { "Approval reference" }),
            BuildPaymentIntentCategory(paymentIntentEvidence),
            BuildCashEvidenceCategory(paymentIntentEvidence),
            BuildCategory(
                "report-output",
                "Report output",
                reportOutputs.Count > 0 && reportOutputs.All(static item => IsReportOutputEvidenceReady(item)) && reportLinks.Count > 0,
                reportOutputs.Count > 0
                    ? "Governed report outputs are linked to this capital account."
                    : "Governed report output is missing for this capital account.",
                reportLinks,
                new[] { "Governed report output" })
        };
    }

    private static PrivateCapitalEvidenceCategoryDto BuildCategory(
        string categoryId,
        string label,
        bool isReady,
        string summary,
        IReadOnlyList<string> evidenceLinks,
        IReadOnlyList<string> requiredEvidence)
        => new(
            categoryId,
            label,
            isReady,
            summary,
            evidenceLinks.Count,
            evidenceLinks,
            requiredEvidence);

    private static PrivateCapitalEvidenceCategoryDto BuildPaymentIntentCategory(
        PrivateCapitalPaymentIntentEvidenceDto paymentIntentEvidence)
    {
        var links = Normalize([paymentIntentEvidence.PaymentIntentId, paymentIntentEvidence.SettlementReference]);
        return BuildCategory(
            "payment-intent",
            "Payment intent",
            paymentIntentEvidence.Status != PrivateCapitalPaymentIntentEvidenceStatusDto.MissingIntent,
            paymentIntentEvidence.PaymentIntentId is null
                ? "Payment intent is missing."
                : paymentIntentEvidence.SettlementReference is null
                    ? "Payment intent is captured without a settlement reference."
                    : "Payment intent and settlement reference are captured.",
            links,
            new[] { "Payment intent id", "Settlement reference" });
    }

    private static PrivateCapitalEvidenceCategoryDto BuildCashEvidenceCategory(
        PrivateCapitalPaymentIntentEvidenceDto paymentIntentEvidence)
        => BuildCategory(
            "cash-evidence",
            "Cash evidence",
            paymentIntentEvidence.IsReady,
            paymentIntentEvidence.Summary,
            paymentIntentEvidence.CashEvidenceLinks,
            paymentIntentEvidence.RequiredEvidence.Count == 0
                ? new[] { "Retained bank, cash, or settlement evidence" }
                : paymentIntentEvidence.RequiredEvidence);

    private static bool IsApprovalReady(
        ManualJournalEntryStatusDto approvalState,
        IReadOnlyList<string> approvalLinks)
        => approvalLinks.Count > 0 &&
           (approvalState == ManualJournalEntryStatusDto.Submitted ||
            approvalState == ManualJournalEntryStatusDto.Approved);

    private static bool IsReportOutputEvidenceReady(PrivateCapitalReportOutputDto reportOutput)
        => reportOutput.IsReportReady && Normalize(reportOutput.EvidenceLinks).Count > 0;

    private static IEnumerable<string> BuildApprovalLinks(
        string? approvalId,
        string? approvalRoute)
    {
        if (!string.IsNullOrWhiteSpace(approvalRoute))
        {
            yield return approvalRoute;
        }

        if (!string.IsNullOrWhiteSpace(approvalId))
        {
            yield return approvalId;
        }
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string?> evidenceLinks)
        => evidenceLinks
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
