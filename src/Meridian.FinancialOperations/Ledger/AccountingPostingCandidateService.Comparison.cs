using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>
/// Text, event and chart comparison helpers plus candidate-issue construction.
/// </summary>
public sealed partial class AccountingPostingCandidateService
{
    private static bool EventsMatch(EconomicEventReferenceDto left, EconomicEventReferenceDto right)
        => left.EventId == right.EventId &&
           TextEquals(left.EventType, right.EventType) &&
           left.EventVersion == right.EventVersion &&
           left.EffectiveDate == right.EffectiveDate &&
           left.CorrelationId == right.CorrelationId &&
           left.CausationId == right.CausationId &&
           left.SecurityId == right.SecurityId &&
           left.BookPositionId == right.BookPositionId &&
           TextEquals(left.SourceDomain, right.SourceDomain) &&
           OptionalTextEquals(left.SourceEntityId, right.SourceEntityId) &&
           OptionalTextEquals(left.SourceContentHash, right.SourceContentHash);

    private static HashSet<string> NormalizeEvidence(IReadOnlyList<string>? links)
        => (links ?? [])
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool TextEquals(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool OptionalTextEquals(string? left, string? right)
        => string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right) ||
           TextEquals(left, right);

    private static void AddMismatch(
        List<PostingRuleJournalCandidateIssueDto> issues,
        bool isValid,
        string code,
        string message,
        string targetId)
    {
        if (isValid)
        {
            return;
        }

        issues.Add(Issue(
            code,
            AccountingConfigurationValidationSeverityDto.Critical,
            message,
            targetId,
            "Align the typed assertion with the authoritative and legacy candidate fields before retrying."));
    }

    private static Dictionary<string, ChartOfAccountsNodeDto> BuildChartByPath(
        IReadOnlyList<ChartOfAccountsNodeDto> chart)
        => chart
            .Where(static node => !string.IsNullOrWhiteSpace(node.Path))
            .GroupBy(static node => node.Path.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.Single(), StringComparer.OrdinalIgnoreCase);

    private static bool TryMapLedgerAccountType(string? accountType, out LedgerAccountType ledgerAccountType)
    {
        if (Enum.TryParse(accountType?.Trim(), ignoreCase: true, out ledgerAccountType))
        {
            return ledgerAccountType is LedgerAccountType.Asset
                or LedgerAccountType.Liability
                or LedgerAccountType.Equity
                or LedgerAccountType.Revenue
                or LedgerAccountType.Expense;
        }

        return false;
    }

    private static PostingRuleJournalCandidateIssueDto ToCandidateIssue(
        AccountingConfigurationValidationIssueDto issue)
        => new(
            issue.Code,
            issue.Severity,
            issue.Message,
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical,
            issue.TargetId,
            issue.SuggestedAction);

    private static PostingRuleJournalCandidateIssueDto Issue(
        string code,
        AccountingConfigurationValidationSeverityDto severity,
        string message,
        string? targetId,
        string? suggestedAction)
        => new(
            code,
            severity,
            message,
            severity == AccountingConfigurationValidationSeverityDto.Critical,
            targetId,
            suggestedAction);
}
