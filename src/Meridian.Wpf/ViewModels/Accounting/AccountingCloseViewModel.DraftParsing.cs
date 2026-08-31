using System.Globalization;
using Meridian.Contracts.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class AccountingCloseViewModel
{
    private static DateOnly? ParseCloseSetupDueDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dueDate))
        {
            return dueDate;
        }

        throw new ArgumentException("Close task due date must use yyyy-MM-dd format.", nameof(CloseSetupTaskDueDateText));
    }

    private bool TryParseLateAdjustmentAmount(out decimal amount)
    {
        amount = 0m;
        return decimal.TryParse(
            LateAdjustmentAmountText,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out amount);
    }

    private decimal ParseLateAdjustmentAmount()
        => decimal.Parse(LateAdjustmentAmountText.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture);

    private bool TryParseCloseTaskSignOffDecision(out ManualJournalEntryStatusDto decision)
    {
        if (Enum.TryParse(CloseTaskSignOffDecision, ignoreCase: true, out decision) &&
            decision is ManualJournalEntryStatusDto.Approved or ManualJournalEntryStatusDto.Rejected)
        {
            return true;
        }

        decision = default;
        return false;
    }

    private static ManualJournalEntryStatusDto ParseCloseTaskSignOffDecision(string value)
        => Enum.TryParse<ManualJournalEntryStatusDto>(value, ignoreCase: true, out var decision) &&
           decision is ManualJournalEntryStatusDto.Approved or ManualJournalEntryStatusDto.Rejected
            ? decision
            : throw new ArgumentException("Close task sign-off decision must be Approved or Rejected.", nameof(CloseTaskSignOffDecision));

    private static ManualJournalEntryStatusDto ParseCloseReviewDecision(string value)
        => Enum.TryParse<ManualJournalEntryStatusDto>(value, ignoreCase: true, out var decision) &&
           decision is ManualJournalEntryStatusDto.Approved or ManualJournalEntryStatusDto.Rejected
            ? decision
            : throw new ArgumentException("Close review decision must be Approved or Rejected.", nameof(LateAdjustmentReviewDecision));

    private static bool TryParseCloseReviewDecision(string value, out ManualJournalEntryStatusDto decision)
    {
        if (Enum.TryParse(value, ignoreCase: true, out decision) &&
            decision is ManualJournalEntryStatusDto.Approved or ManualJournalEntryStatusDto.Rejected)
        {
            return true;
        }

        decision = default;
        return false;
    }

    private static IReadOnlyList<string> ParseCloseSetupDependencies(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static dependency => ParseCloseSetupDependencyEntry(dependency).DependencyId)
                .Where(static dependency => dependency.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static IReadOnlyList<CloseTaskSignOffRequirementConfigurationDto> ParseCloseSetupSignOffRequirements(string? value)
    {
        var requirements = new Dictionary<string, CloseTaskSignOffRequirementConfigurationDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in SplitCloseSetupSignOffRequirements(value)
                     .Select(static item => ParseCloseSetupSignOffRequirement(item)))
        {
            if (!string.IsNullOrWhiteSpace(entry.Role) && entry.RequiredApprovalCount > 0)
            {
                requirements[entry.Role] = entry;
            }
        }

        return requirements.Values.ToArray();
    }

    private static IEnumerable<string> SplitCloseSetupSignOffRequirements(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static item => item.Length > 0);

    private static CloseTaskSignOffRequirementConfigurationDto ParseCloseSetupSignOffRequirement(string value)
    {
        var parts = value.Contains('|', StringComparison.Ordinal)
            ? value.Split('|', StringSplitOptions.TrimEntries)
            : value.Split(':', StringSplitOptions.TrimEntries);
        var role = parts.Length > 0 ? parts[0] : string.Empty;
        var requiredCountText = parts.Length > 1 ? parts[1] : "1";
        _ = int.TryParse(requiredCountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requiredCount);
        var evidence = parts.Length > 2
            ? string.Join(":", parts.Skip(2)).Trim()
            : string.Empty;
        return new CloseTaskSignOffRequirementConfigurationDto(role, requiredCount, NormalizeOptional(evidence));
    }

    private static IReadOnlyList<CloseTaskSignOffRequirementConfigurationDto> BuildCloseSetupSignOffRequirementConfigurations(
        IReadOnlyList<CloseSignOffRequirementDto> requirements)
        => requirements
            .Select(static requirement => new CloseTaskSignOffRequirementConfigurationDto(
                requirement.Role,
                Math.Max(1, requirement.RequiredApprovalCount),
                string.IsNullOrWhiteSpace(requirement.EvidenceRequirement)
                    ? "Retained close checklist evidence"
                    : requirement.EvidenceRequirement.Trim()))
            .ToArray();

    private static string BuildCloseSetupSignOffRequirementText(IReadOnlyList<CloseSignOffRequirementDto> requirements)
        => string.Join(
            Environment.NewLine,
            requirements.Select(static requirement =>
                $"{requirement.Role} | {Math.Max(1, requirement.RequiredApprovalCount).ToString(CultureInfo.InvariantCulture)} | {(string.IsNullOrWhiteSpace(requirement.EvidenceRequirement) ? "Retained close checklist evidence" : requirement.EvidenceRequirement.Trim())}"));

    private static IReadOnlyDictionary<string, string> ParseCloseSetupDependencyReasonOverrides(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in value.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(static item => ParseCloseSetupDependencyEntry(item)))
        {
            if (!string.IsNullOrWhiteSpace(entry.DependencyId) &&
                !string.IsNullOrWhiteSpace(entry.Reason) &&
                !overrides.ContainsKey(entry.DependencyId))
            {
                overrides[entry.DependencyId] = entry.Reason;
            }
        }

        return overrides;
    }

    private static (string DependencyId, string? Reason) ParseCloseSetupDependencyEntry(string value)
    {
        var item = value.Trim();
        if (item.Length == 0)
        {
            return (string.Empty, null);
        }

        var colonIndex = item.IndexOf(':', StringComparison.Ordinal);
        var equalsIndex = item.IndexOf('=', StringComparison.Ordinal);
        var separatorIndex = new[] { colonIndex, equalsIndex }
            .Where(static index => index > 0)
            .DefaultIfEmpty(-1)
            .Min();
        if (separatorIndex < 0)
        {
            return (item, null);
        }

        var dependencyId = item[..separatorIndex].Trim();
        var reason = item[(separatorIndex + 1)..].Trim();
        return (dependencyId, reason.Length == 0 ? null : reason);
    }

    private static IReadOnlyList<CloseTaskDependencyConfigurationDto> BuildCloseSetupDependencyConfigurations(
        IReadOnlyList<string> dependencyIds,
        IReadOnlyDictionary<string, string> dependencyIdReasons,
        IReadOnlyDictionary<string, string> dependencyReasonOverrides,
        string? fallbackReason,
        IReadOnlyList<CloseDependencyDto> existingDependencies)
        => dependencyIds
            .Select(dependencyId => new CloseTaskDependencyConfigurationDto(
                dependencyId,
                ResolveCloseSetupDependencyReason(
                    dependencyId,
                    dependencyIdReasons,
                    dependencyReasonOverrides,
                    fallbackReason,
                    existingDependencies)))
            .ToArray();

    private static string ResolveCloseSetupDependencyReason(
        string dependencyId,
        IReadOnlyDictionary<string, string> dependencyIdReasons,
        IReadOnlyDictionary<string, string> dependencyReasonOverrides,
        string? fallbackReason,
        IReadOnlyList<CloseDependencyDto> existingDependencies)
        => dependencyIdReasons.TryGetValue(dependencyId, out var reasonFromDependencyIds) && !string.IsNullOrWhiteSpace(reasonFromDependencyIds)
            ? reasonFromDependencyIds
            : dependencyReasonOverrides.TryGetValue(dependencyId, out var reasonFromOverrides) && !string.IsNullOrWhiteSpace(reasonFromOverrides)
                ? reasonFromOverrides
                : fallbackReason
                  ?? existingDependencies.FirstOrDefault(dependency =>
                      string.Equals(dependency.DependsOnTaskId, dependencyId, StringComparison.OrdinalIgnoreCase))?.Reason
                  ?? "Configured close-plan dependency.";

    private static string BuildCloseSetupDependencyReason(IReadOnlyList<CloseDependencyDto> dependencies)
    {
        var reasons = dependencies
            .Select(static dependency => dependency.Reason.Trim())
            .Where(static reason => reason.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return reasons.Length == 1 ? reasons[0] : "Configured close-plan dependency.";
    }
}
