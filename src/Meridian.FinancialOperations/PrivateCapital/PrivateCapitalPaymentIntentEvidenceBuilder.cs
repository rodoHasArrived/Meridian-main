using Meridian.Contracts.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.FinancialOperations.PrivateCapital;

public static class PrivateCapitalPaymentIntentEvidenceBuilder
{
    private static readonly string[] CashEvidenceKeywords =
    [
        "bank",
        "cash",
        "custodian",
        "plaid",
        "reconciliation",
        "settlement",
        "treasury",
        "wire",
        "ach",
        "swift",
        "return",
        "revers",
        "reject",
        "void",
        "failed",
        "failure"
    ];

    public static PrivateCapitalPaymentIntentEvidenceDto BuildForFundEvent(
        PrivateCapitalFundEventDto fundEvent,
        IEnumerable<string> evidenceLinks,
        string? evidenceRoute)
    {
        var cashEvidenceLinks = SelectCashEvidenceLinks(
            evidenceLinks,
            fundEvent.PaymentIntentId,
            fundEvent.SettlementReference);
        var status = ResolveStatus(
            string.IsNullOrWhiteSpace(fundEvent.PaymentIntentId),
            string.IsNullOrWhiteSpace(fundEvent.SettlementReference),
            cashEvidenceLinks.Count);

        return new PrivateCapitalPaymentIntentEvidenceDto(
            NormalizeOptional(fundEvent.PaymentIntentId),
            NormalizeOptional(fundEvent.SettlementReference),
            status,
            IsReadyStatus(status),
            ResolveDirection(fundEvent.EntryType, fundEvent.NetCapitalActivity),
            Math.Abs(fundEvent.NetCapitalActivity),
            fundEvent.Currency,
            fundEvent.EffectiveDate,
            BuildFundEventSummary(fundEvent, status, cashEvidenceLinks.Count),
            cashEvidenceLinks.Count,
            cashEvidenceLinks,
            BuildRequiredEvidence(status),
            evidenceRoute);
    }

    public static PrivateCapitalPaymentIntentEvidenceDto BuildForSubledger(
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IEnumerable<string> evidenceLinks,
        string currency,
        DateOnly? effectiveDate,
        decimal netActivity,
        string? evidenceRoute)
    {
        var paymentIntentIds = Normalize(records.Select(static item => item.PaymentIntentId));
        var settlementReferences = Normalize(records.Select(static item => item.SettlementReference));
        var cashEvidenceLinks = SelectCashEvidenceLinks(
            evidenceLinks,
            paymentIntentIds,
            settlementReferences);
        var status = ResolveSubledgerStatus(records, paymentIntentIds.Count, settlementReferences.Count, cashEvidenceLinks.Count);

        return new PrivateCapitalPaymentIntentEvidenceDto(
            paymentIntentIds.Count == 1 ? paymentIntentIds[0] : paymentIntentIds.Count == 0 ? null : $"{paymentIntentIds.Count} payment intents",
            settlementReferences.Count == 1 ? settlementReferences[0] : settlementReferences.Count == 0 ? null : $"{settlementReferences.Count} settlement references",
            status,
            IsReadyStatus(status),
            ResolveDirection(netActivity),
            Math.Abs(netActivity),
            string.IsNullOrWhiteSpace(currency) ? "USD" : currency,
            effectiveDate ?? DateOnly.MinValue,
            BuildSubledgerSummary(records.Count, paymentIntentIds.Count, settlementReferences.Count, status, cashEvidenceLinks.Count),
            cashEvidenceLinks.Count,
            cashEvidenceLinks,
            BuildRequiredEvidence(status),
            evidenceRoute);
    }

    private static PrivateCapitalPaymentIntentEvidenceStatusDto ResolveStatus(
        bool missingPaymentIntent,
        bool missingSettlementReference,
        int cashEvidenceLinkCount)
    {
        if (missingPaymentIntent)
        {
            return PrivateCapitalPaymentIntentEvidenceStatusDto.MissingIntent;
        }

        if (cashEvidenceLinkCount == 0)
        {
            return PrivateCapitalPaymentIntentEvidenceStatusDto.CashEvidenceMissing;
        }

        return missingSettlementReference
            ? PrivateCapitalPaymentIntentEvidenceStatusDto.IntentCaptured
            : PrivateCapitalPaymentIntentEvidenceStatusDto.SettlementMatched;
    }

    private static PrivateCapitalPaymentIntentEvidenceStatusDto ResolveSubledgerStatus(
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        int paymentIntentCount,
        int settlementReferenceCount,
        int cashEvidenceLinkCount)
    {
        if (records.Count == 0 || paymentIntentCount == 0)
        {
            return PrivateCapitalPaymentIntentEvidenceStatusDto.MissingIntent;
        }

        if (cashEvidenceLinkCount == 0)
        {
            return PrivateCapitalPaymentIntentEvidenceStatusDto.CashEvidenceMissing;
        }

        return settlementReferenceCount == 0
            ? PrivateCapitalPaymentIntentEvidenceStatusDto.IntentCaptured
            : PrivateCapitalPaymentIntentEvidenceStatusDto.SettlementMatched;
    }

    private static bool IsReadyStatus(PrivateCapitalPaymentIntentEvidenceStatusDto status)
        => status is PrivateCapitalPaymentIntentEvidenceStatusDto.IntentCaptured
            or PrivateCapitalPaymentIntentEvidenceStatusDto.SettlementMatched;

    private static string BuildFundEventSummary(
        PrivateCapitalFundEventDto fundEvent,
        PrivateCapitalPaymentIntentEvidenceStatusDto status,
        int cashEvidenceLinkCount)
        => status switch
        {
            PrivateCapitalPaymentIntentEvidenceStatusDto.MissingIntent =>
                "Payment intent is missing for this fund event; cash movement evidence is not source-backed.",
            PrivateCapitalPaymentIntentEvidenceStatusDto.CashEvidenceMissing =>
                $"Payment intent {fundEvent.PaymentIntentId} is captured, but retained bank, cash, or settlement evidence is missing.",
            PrivateCapitalPaymentIntentEvidenceStatusDto.IntentCaptured =>
                $"Payment intent {fundEvent.PaymentIntentId} has {cashEvidenceLinkCount} retained cash evidence link(s); settlement reference is not attached and live execution remains deferred.",
            _ =>
                $"Payment intent {fundEvent.PaymentIntentId} and settlement {fundEvent.SettlementReference} have {cashEvidenceLinkCount} retained cash evidence link(s); live execution remains deferred."
        };

    private static string BuildSubledgerSummary(
        int recordCount,
        int paymentIntentCount,
        int settlementReferenceCount,
        PrivateCapitalPaymentIntentEvidenceStatusDto status,
        int cashEvidenceLinkCount)
        => status switch
        {
            PrivateCapitalPaymentIntentEvidenceStatusDto.MissingIntent =>
                "No payment intents are linked to this capital-account subledger.",
            PrivateCapitalPaymentIntentEvidenceStatusDto.CashEvidenceMissing =>
                $"{paymentIntentCount} payment intent(s) are linked across {recordCount} fund event(s), but retained cash evidence is missing.",
            PrivateCapitalPaymentIntentEvidenceStatusDto.IntentCaptured =>
                $"{paymentIntentCount} payment intent(s) have {cashEvidenceLinkCount} retained cash evidence link(s); settlement reference is not attached and live execution remains deferred.",
            _ =>
                $"{paymentIntentCount} payment intent(s), {settlementReferenceCount} settlement reference(s), and {cashEvidenceLinkCount} retained cash evidence link(s) support this subledger; live execution remains deferred."
        };

    private static IReadOnlyList<string> BuildRequiredEvidence(PrivateCapitalPaymentIntentEvidenceStatusDto status)
        => status switch
        {
            PrivateCapitalPaymentIntentEvidenceStatusDto.MissingIntent =>
                ["Payment intent id", "Retained bank, cash, or settlement evidence"],
            PrivateCapitalPaymentIntentEvidenceStatusDto.CashEvidenceMissing =>
                ["Retained bank, cash, or settlement evidence"],
            PrivateCapitalPaymentIntentEvidenceStatusDto.IntentCaptured =>
                ["Settlement reference"],
            _ => []
        };

    private static PaymentIntentCashDirectionDto ResolveDirection(ManualJournalEntryTypeDto entryType, decimal netActivity)
        => entryType switch
        {
            ManualJournalEntryTypeDto.CapitalCall or ManualJournalEntryTypeDto.Subscription => PaymentIntentCashDirectionDto.Inflow,
            ManualJournalEntryTypeDto.Distribution or ManualJournalEntryTypeDto.Redemption or ManualJournalEntryTypeDto.ManagementFee => PaymentIntentCashDirectionDto.Outflow,
            _ => ResolveDirection(netActivity)
        };

    private static PaymentIntentCashDirectionDto ResolveDirection(decimal netActivity)
        => netActivity > 0m
            ? PaymentIntentCashDirectionDto.Inflow
            : netActivity < 0m
                ? PaymentIntentCashDirectionDto.Outflow
                : PaymentIntentCashDirectionDto.Neutral;

    private static IReadOnlyList<string> SelectCashEvidenceLinks(
        IEnumerable<string> evidenceLinks,
        string? paymentIntentId,
        string? settlementReference)
        => SelectCashEvidenceLinks(
            evidenceLinks,
            Normalize([paymentIntentId]),
            Normalize([settlementReference]));

    private static IReadOnlyList<string> SelectCashEvidenceLinks(
        IEnumerable<string> evidenceLinks,
        IReadOnlyList<string> paymentIntentIds,
        IReadOnlyList<string> settlementReferences)
    {
        var cashEvidenceLinks = Normalize(evidenceLinks)
            .Where(IsCashEvidenceLink)
            .ToArray();
        var targetIdentifiers = paymentIntentIds
            .Concat(settlementReferences)
            .ToArray();
        if (targetIdentifiers.Length == 0)
        {
            return cashEvidenceLinks;
        }

        var matched = cashEvidenceLinks
            .Where(link => ContainsAnyIdentifier(link, targetIdentifiers))
            .ToArray();
        if (matched.Length > 0)
        {
            return matched;
        }

        return cashEvidenceLinks
            .Where(link => !ContainsExplicitPaymentOrSettlementIdentifier(link))
            .ToArray();
    }

    private static bool IsCashEvidenceLink(string link)
        => CashEvidenceKeywords.Any(keyword => link.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAnyIdentifier(string link, IReadOnlyList<string> identifiers)
        => identifiers.Any(identifier =>
            link.Contains(identifier, StringComparison.OrdinalIgnoreCase) ||
            link.Contains(Uri.EscapeDataString(identifier), StringComparison.OrdinalIgnoreCase));

    private static bool ContainsExplicitPaymentOrSettlementIdentifier(string link)
        => link.Contains("payment:", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("payment%3A", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("settlement:", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("settlement%3A", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> Normalize(IEnumerable<string?> values)
        => values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
