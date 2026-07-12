using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Banking;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Ledger;
using Meridian.Storage.Archival;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class AccountingConfigurationService
{
    private static AccountingRulesStudioDto BuildRulesStudio(
        AccountingConfigurationWorkspaceDto workspace,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> validationIssues)
    {
        var rules = workspace.PostingRules;
        var activeRules = rules.Where(static rule => !rule.IsArchived).ToArray();
        var rows = rules
            .Select(rule => BuildRulesStudioRuleRow(rule, workspace.RuleTestCases, validationIssues))
            .OrderByDescending(static row => row.CriticalIssueCount)
            .ThenByDescending(static row => row.RequiresPromotionApproval && !row.IsPromotionApproved)
            .ThenByDescending(static row => row.Priority)
            .ThenBy(static row => row.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var promotionQueue = activeRules
            .Where(static rule => rule.RequiresPromotionApproval && !IsApprovedPromotion(rule))
            .Select(rule => BuildRulesStudioPromotionQueueItem(rule, workspace.RuleTestCases, validationIssues))
            .OrderByDescending(static item => item.CriticalIssueCount)
            .ThenBy(static item => item.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rulesReadyForActivation = rows.Count(static row => row.CanActivate);
        var rulesBlockedByPromotionApproval = rows.Count(static row =>
            !row.IsArchived &&
            row.RequiresPromotionApproval &&
            !row.IsPromotionApproved);
        var rulesBlockedByRegressionTests = rows.Count(static row =>
            !row.IsArchived &&
            row.RequiresPromotionApproval &&
            row.SavedTestCaseCount == 0);
        var rulesBlockedByCriticalIssues = rows.Count(static row =>
            !row.IsArchived &&
            row.CriticalIssueCount > 0);
        var criticalIssueCount = validationIssues.Count(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        var warningIssueCount = validationIssues.Count(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Warning);
        var requiredActions = BuildRulesStudioRequiredActions(
            activeRules.Length,
            rulesBlockedByPromotionApproval,
            rulesBlockedByRegressionTests,
            rulesBlockedByCriticalIssues,
            criticalIssueCount,
            promotionQueue.Length,
            rulesReadyForActivation);

        var summary = new AccountingRulesStudioSummaryDto(
            TotalRules: rules.Count,
            ActiveRules: activeRules.Length,
            ArchivedRules: rules.Count - activeRules.Length,
            EffectiveDatedRules: rules.Count(rule => rule.EffectiveFrom.HasValue || rule.EffectiveTo.HasValue),
            GeneratedPostingRules: rules.Count(static rule => rule.GeneratedPostings.Count > 0),
            TemplateMappingRules: rules.Count(static rule => rule.GeneratedPostings.Count == 0),
            RulesWithConditions: rules.Count(static rule => rule.Conditions.Count > 0 || rule.ConditionGroups.Count > 0),
            RulesWithFormulas: rules.Count(static rule => rule.Formulas.Count > 0),
            RulesWithAllocations: rules.Count(static rule => rule.Allocations.Count > 0),
            RulesRequiringPromotionApproval: activeRules.Count(static rule => rule.RequiresPromotionApproval),
            ApprovedPromotionRules: activeRules.Count(IsApprovedPromotion),
            PendingPromotionApprovalRules: promotionQueue.Length,
            SavedTestCaseCount: workspace.RuleTestCases.Count,
            RulesWithSavedRegressionTests: activeRules.Count(rule => HasSavedRegressionTestForRuleVersion(workspace.RuleTestCases, rule)),
            RulesMissingCurrentVersionRegressionTests: activeRules.Count(rule =>
                rule.RequiresPromotionApproval &&
                !HasSavedRegressionTestForRuleVersion(workspace.RuleTestCases, rule)),
            CriticalIssueCount: criticalIssueCount,
            WarningIssueCount: warningIssueCount,
            RulesReadyForActivation: rulesReadyForActivation,
            RulesBlockedByPromotionApproval: rulesBlockedByPromotionApproval,
            RulesBlockedByRegressionTests: rulesBlockedByRegressionTests,
            RulesBlockedByCriticalIssues: rulesBlockedByCriticalIssues,
            RequiredActions: requiredActions);

        return new AccountingRulesStudioDto(summary, rows, promotionQueue);
    }

    private static IReadOnlyList<string> BuildRulesStudioRequiredActions(
        int activeRuleCount,
        int rulesBlockedByPromotionApproval,
        int rulesBlockedByRegressionTests,
        int rulesBlockedByCriticalIssues,
        int criticalIssueCount,
        int pendingPromotionQueueCount,
        int rulesReadyForActivation)
    {
        var actions = new List<string>();
        if (activeRuleCount == 0)
        {
            actions.Add("Configure at least one active posting rule before production activation.");
            return actions;
        }

        if (criticalIssueCount > 0)
        {
            actions.Add($"{criticalIssueCount} critical validation issue(s) must be resolved before activation.");
        }

        if (rulesBlockedByRegressionTests > 0)
        {
            actions.Add($"{rulesBlockedByRegressionTests} promotion-gated rule(s) need a current-version saved regression test.");
        }

        if (pendingPromotionQueueCount > 0 || rulesBlockedByPromotionApproval > 0)
        {
            actions.Add($"{Math.Max(pendingPromotionQueueCount, rulesBlockedByPromotionApproval)} promotion approval(s) need human review.");
        }

        if (actions.Count == 0 && rulesReadyForActivation == activeRuleCount)
        {
            actions.Add("Rules Studio is ready for activation review.");
        }

        return actions;
    }

    private static AccountingRulesStudioRuleRowDto BuildRulesStudioRuleRow(
        PostingRuleDto rule,
        IReadOnlyList<AccountingRuleTestCaseDto> testCases,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> validationIssues)
    {
        var ruleVersion = NormalizeOptional(rule.RuleVersion) ?? "v1";
        var savedTests = GetSavedRegressionTestsForRuleVersion(testCases, rule);
        var criticalIssueCount = CountRuleIssues(rule.RuleId, validationIssues, AccountingConfigurationValidationSeverityDto.Critical);
        var warningIssueCount = CountRuleIssues(rule.RuleId, validationIssues, AccountingConfigurationValidationSeverityDto.Warning);
        var isPromotionApproved = IsApprovedPromotion(rule);
        var canRequestPromotion = !rule.IsArchived &&
                                  rule.RequiresPromotionApproval &&
                                  !isPromotionApproved &&
                                  savedTests.Count > 0 &&
                                  criticalIssueCount == 0;

        return new AccountingRulesStudioRuleRowDto(
            rule.RuleId,
            rule.DisplayName,
            rule.SourceEventType,
            ruleVersion,
            rule.Priority,
            rule.EffectiveFrom,
            rule.EffectiveTo,
            rule.TemplateId,
            rule.IsArchived,
            rule.GeneratedPostings.Count > 0,
            rule.Conditions.Count,
            rule.ConditionGroups.Count,
            rule.Formulas.Count,
            rule.Allocations.Count,
            rule.GeneratedPostings.Count,
            rule.Versions.Count,
            savedTests.Count,
            savedTests.Sum(static test => test.EvidenceLinks.Count),
            rule.RequiresPromotionApproval,
            isPromotionApproved,
            rule.PromotionApproval?.ApprovalState,
            rule.PromotionApproval?.ApprovalId,
            criticalIssueCount,
            warningIssueCount,
            !rule.IsArchived && !string.IsNullOrWhiteSpace(rule.SourceEventType),
            canRequestPromotion,
            !rule.IsArchived &&
            criticalIssueCount == 0 &&
            (!rule.RequiresPromotionApproval || isPromotionApproved) &&
            (!rule.RequiresPromotionApproval || savedTests.Count > 0));
    }

    private static AccountingRulesStudioPromotionQueueItemDto BuildRulesStudioPromotionQueueItem(
        PostingRuleDto rule,
        IReadOnlyList<AccountingRuleTestCaseDto> testCases,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> validationIssues)
    {
        var savedTests = GetSavedRegressionTestsForRuleVersion(testCases, rule);
        var missingEvidenceCount = savedTests.Count(testCase =>
            !HasRuleTestCaseEvidence(testCase.EvidenceLinks) ||
            !HasRuleTestCaseEvidenceWithProvenance(testCase, testCase.EvidenceLinks));
        var criticalIssueCount = CountRuleIssues(rule.RuleId, validationIssues, AccountingConfigurationValidationSeverityDto.Critical);
        var suggestedAction = criticalIssueCount > 0
            ? "Resolve critical validation issues before promotion."
            : savedTests.Count == 0
                ? "Save at least one regression test for the current rule version."
                : missingEvidenceCount > 0
                    ? "Attach retained regression evidence to every current-version test case."
                    : "Review evidence and approve promotion with a human operator.";

        return new AccountingRulesStudioPromotionQueueItemDto(
            rule.RuleId,
            rule.DisplayName,
            NormalizeOptional(rule.RuleVersion) ?? "v1",
            rule.PromotionApproval?.RequestedBy ?? string.Empty,
            rule.PromotionApproval?.RequestedAtUtc,
            rule.PromotionApproval?.ApprovalState,
            rule.PromotionApproval?.ApprovalId,
            savedTests.Count,
            missingEvidenceCount,
            criticalIssueCount,
            suggestedAction);
    }

    private static int CountRuleIssues(
        string ruleId,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> validationIssues,
        AccountingConfigurationValidationSeverityDto severity)
        => validationIssues.Count(issue =>
            issue.Severity == severity &&
            string.Equals(issue.TargetId, ruleId, StringComparison.OrdinalIgnoreCase));

    private static bool IsEffective(PostingRuleDto rule, DateOnly effectiveDate)
        => (!rule.EffectiveFrom.HasValue || rule.EffectiveFrom.Value <= effectiveDate) &&
           (!rule.EffectiveTo.HasValue || rule.EffectiveTo.Value >= effectiveDate);

    private static bool HasInvalidEffectiveWindow(PostingRuleDto rule)
        => rule.EffectiveFrom.HasValue &&
           rule.EffectiveTo.HasValue &&
           rule.EffectiveFrom.Value > rule.EffectiveTo.Value;

    private static AccountingConfigurationValidationIssueDto BuildPostingRuleEffectiveWindowIssue(PostingRuleDto rule)
        => Issue(
            "posting-rule.effective-date-range",
            AccountingConfigurationValidationSeverityDto.Critical,
            $"Posting rule '{rule.RuleId}' has an effective-from date after effective-to.",
            rule.RuleId,
            "Correct the effective-dated rule window.");

    private static PostingRuleDto ResetCarriedForwardPromotionApproval(PostingRuleDto? existingRule, PostingRuleDto incomingRule)
    {
        var existingApproval = existingRule?.PromotionApproval;
        var incomingApproval = incomingRule.PromotionApproval;
        if (incomingRule.RequiresPromotionApproval &&
            incomingApproval is not null &&
            !IsApprovedPromotion(incomingRule))
        {
            return incomingRule with { PromotionApproval = null };
        }

        if (existingRule is null ||
            !incomingRule.RequiresPromotionApproval ||
            existingApproval is null ||
            incomingApproval is null ||
            !IsApprovedPromotion(existingRule) ||
            !IsApprovedPromotion(incomingRule) ||
            !string.Equals(existingApproval.ApprovalId, incomingApproval.ApprovalId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(HashApprovalProtectedRuleDefinition(existingRule), HashApprovalProtectedRuleDefinition(incomingRule), StringComparison.Ordinal))
        {
            return incomingRule;
        }

        return incomingRule with { PromotionApproval = null };
    }

    private static string HashApprovalProtectedRuleDefinition(PostingRuleDto rule)
    {
        var json = JsonSerializer.Serialize(new PostingRuleApprovalProtectedDefinition(
            NormalizeOptional(rule.RuleVersion) ?? "v1",
            NormalizeOptional(rule.SourceEventType) ?? string.Empty,
            NormalizeOptional(rule.TemplateId) ?? string.Empty,
            rule.EffectiveFrom,
            rule.EffectiveTo,
            rule.Priority,
            rule.Scope,
            rule.Conditions,
            rule.ConditionGroups,
            rule.Formulas,
            rule.Allocations,
            rule.GeneratedPostings));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static IReadOnlyList<AccountingRuleVersionDto> BuildPostingRuleVersionHistory(
        PostingRuleDto? existingRule,
        PostingRuleDto incomingRule,
        string actor,
        IReadOnlyList<string>? evidenceLinks)
    {
        var versions = new List<AccountingRuleVersionDto>();
        versions.AddRange(existingRule?.Versions ?? []);
        foreach (var version in incomingRule.Versions)
        {
            var index = versions.FindIndex(item => string.Equals(item.Version, version.Version, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                versions[index] = version;
            }
            else
            {
                versions.Add(version);
            }
        }

        var currentVersion = string.IsNullOrWhiteSpace(incomingRule.RuleVersion)
            ? "v1"
            : incomingRule.RuleVersion.Trim();
        if (!versions.Any(item => string.Equals(item.Version, currentVersion, StringComparison.OrdinalIgnoreCase)))
        {
            var summary = existingRule is null
                ? $"Created posting rule '{incomingRule.RuleId}' at version '{currentVersion}'."
                : $"Updated posting rule '{incomingRule.RuleId}' to version '{currentVersion}'.";
            versions.Add(new AccountingRuleVersionDto(
                currentVersion,
                DateTimeOffset.UtcNow,
                RequireText(actor, nameof(actor)),
                summary,
                incomingRule.PromotionApproval,
                NormalizeRuleEvidenceLinks(evidenceLinks)));
        }

        return versions
            .OrderBy(static version => version.CreatedAtUtc)
            .ThenBy(static version => version.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<AccountingRuleVersionDto> ApplyPostingRuleVersionPromotionApproval(
        PostingRuleDto rule,
        RulePromotionApprovalDto approval,
        string actor,
        IReadOnlyList<string> evidenceLinks)
    {
        var currentVersion = NormalizeOptional(rule.RuleVersion) ?? "v1";
        var versions = rule.Versions.ToList();
        var index = versions.FindIndex(item => string.Equals(item.Version, currentVersion, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            var version = versions[index];
            versions[index] = version with
            {
                PromotionApproval = approval,
                EvidenceLinks = NormalizeRuleEvidenceLinks(version.EvidenceLinks.Concat(evidenceLinks).ToArray())
            };
        }
        else
        {
            versions.Add(new AccountingRuleVersionDto(
                currentVersion,
                DateTimeOffset.UtcNow,
                RequireText(actor, nameof(actor)),
                $"Approved posting rule '{rule.RuleId}' at version '{currentVersion}'.",
                approval,
                evidenceLinks));
        }

        return versions
            .OrderBy(static version => version.CreatedAtUtc)
            .ThenBy(static version => version.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizeRuleEvidenceLinks(IReadOnlyList<string>? evidenceLinks)
        => (evidenceLinks ?? [])
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool MatchesScope(LedgerDimensionSetDto? ruleScope, LedgerDimensionSetDto? eventScope, string? counterpartyId)
    {
        if (ruleScope is null)
        {
            return true;
        }

        return MatchesScopeValue(ruleScope.FundId, eventScope?.FundId) &&
               MatchesScopeValue(ruleScope.EntityId, eventScope?.EntityId) &&
               MatchesScopeValue(ruleScope.SleeveId, eventScope?.SleeveId) &&
               MatchesScopeValue(ruleScope.StrategyId, eventScope?.StrategyId) &&
               MatchesScopeValue(ruleScope.InvestorId, eventScope?.InvestorId) &&
               MatchesScopeValue(ruleScope.CapitalAccountId, eventScope?.CapitalAccountId) &&
               (!ruleScope.InstrumentId.HasValue || ruleScope.InstrumentId == eventScope?.InstrumentId) &&
               MatchesScopeValue(ruleScope.TaxLotId, eventScope?.TaxLotId) &&
               MatchesScopeValue(ruleScope.CostCenterId, eventScope?.CostCenterId) &&
               MatchesScopeValue(ruleScope.CounterpartyId, eventScope?.CounterpartyId ?? counterpartyId) &&
               MatchesExternalGlScope(ruleScope.ExternalGlDimensions, eventScope?.ExternalGlDimensions);
    }

    private static bool MatchesScopeValue(string? expected, string? actual)
        => string.IsNullOrWhiteSpace(expected) || string.Equals(expected.Trim(), actual?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesExternalGlScope(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string>? actual)
    {
        if (expected.Count == 0)
        {
            return true;
        }

        if (actual is null || actual.Count == 0)
        {
            return false;
        }

        foreach (var pair in expected)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            if (!actual.TryGetValue(pair.Key, out var actualValue) ||
                !string.Equals(pair.Value.Trim(), actualValue?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EvaluateConditions(
        PostingRuleDto rule,
        RuleDryRunRequestDto request,
        List<AccountingConfigurationValidationIssueDto> issues,
        List<string> explanations)
    {
        var allMatched = true;
        issues.AddRange(BuildDuplicateConditionIdIssues(
            rule.RuleId,
            GetRuleConditions(rule),
            "rule.condition-id-duplicate"));
        foreach (var condition in rule.Conditions)
        {
            ValidateConditionIdentity(rule.RuleId, condition, issues, rule.RuleId, "rule.condition-id-missing");
            ValidateConditionOperand(rule.RuleId, condition, issues, condition.ConditionId);
            var matched = EvaluateCondition(condition, request);
            if (!matched && condition.IsRequired)
            {
                allMatched = false;
                explanations.Add($"Required condition '{condition.ConditionId}' did not match.");
            }
            else if (matched)
            {
                explanations.Add($"Condition '{condition.ConditionId}' matched.");
            }

            if (string.IsNullOrWhiteSpace(condition.Field))
            {
                issues.Add(Issue("rule.condition-field-missing", AccountingConfigurationValidationSeverityDto.Critical, $"Condition '{condition.ConditionId}' is missing a field.", condition.ConditionId, "Select a dry-run field for this rule condition."));
            }
        }

        foreach (var group in rule.ConditionGroups)
        {
            if (string.IsNullOrWhiteSpace(group.GroupId))
            {
                issues.Add(Issue(
                    "rule.condition-group-id-missing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Posting rule '{rule.RuleId}' has a condition group without an id.",
                    rule.RuleId,
                    "Assign each condition group a stable id."));
            }

            if (group.Conditions.Count == 0)
            {
                issues.Add(Issue(
                    "rule.condition-group-empty",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Condition group '{group.GroupId}' on rule '{rule.RuleId}' has no conditions.",
                    group.GroupId,
                    "Add at least one condition to the group or remove the group."));
            }

            foreach (var condition in group.Conditions.Where(static condition => string.IsNullOrWhiteSpace(condition.Field)))
            {
                issues.Add(Issue(
                    "rule.condition-field-missing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Condition '{condition.ConditionId}' in group '{group.GroupId}' is missing a field.",
                    condition.ConditionId,
                    "Select a dry-run field for this grouped rule condition."));
            }

            foreach (var condition in group.Conditions)
            {
                ValidateConditionIdentity(rule.RuleId, condition, issues, group.GroupId, "rule.condition-id-missing");
                ValidateConditionOperand(rule.RuleId, condition, issues, condition.ConditionId);
            }

            var matched = EvaluateConditionGroup(group, request);
            if (!matched && group.IsRequired)
            {
                allMatched = false;
                explanations.Add($"Required condition group '{group.GroupId}' did not match.");
            }
            else if (matched)
            {
                explanations.Add($"Condition group '{group.GroupId}' matched.");
            }
        }

        return allMatched;
    }

    private static bool EvaluateConditionGroup(AccountingRuleConditionGroupDto group, RuleDryRunRequestDto request)
    {
        if (group.Conditions.Count == 0)
        {
            return false;
        }

        return group.Operator switch
        {
            AccountingRuleConditionGroupOperatorDto.Any => group.Conditions.Any(condition => EvaluateCondition(condition, request)),
            _ => group.Conditions.All(condition => EvaluateCondition(condition, request))
        };
    }

    private static bool EvaluateCondition(AccountingRuleConditionDto condition, RuleDryRunRequestDto request)
    {
        var actual = ResolveConditionField(condition.Field, request);
        return condition.Operator switch
        {
            AccountingRuleConditionOperatorDto.Equals => string.Equals(actual, condition.Value, StringComparison.OrdinalIgnoreCase),
            AccountingRuleConditionOperatorDto.NotEquals => !string.Equals(actual, condition.Value, StringComparison.OrdinalIgnoreCase),
            AccountingRuleConditionOperatorDto.Contains => actual?.Contains(condition.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true,
            AccountingRuleConditionOperatorDto.AmountGreaterThanOrEqual => TryParseDecimal(condition.Value, out var minimum) && request.EventAmount >= minimum,
            AccountingRuleConditionOperatorDto.AmountLessThanOrEqual => TryParseDecimal(condition.Value, out var maximum) && request.EventAmount <= maximum,
            AccountingRuleConditionOperatorDto.AmountBetween => TryParseDecimal(condition.Value, out var lower) && TryParseDecimal(condition.SecondValue, out var upper) && lower <= upper && request.EventAmount >= lower && request.EventAmount <= upper,
            AccountingRuleConditionOperatorDto.IsPresent => !string.IsNullOrWhiteSpace(actual),
            _ => false
        };
    }

    private static string? ResolveConditionField(string field, RuleDryRunRequestDto request)
    {
        var dimensions = request.Dimensions;
        var normalizedField = field.Trim();
        return normalizedField.ToLowerInvariant() switch
        {
            "sourceeventtype" or "source_event_type" => request.SourceEventType,
            "currency" => request.Currency,
            "counterparty" or "counterpartyid" or "counterparty_id" => request.CounterpartyId ?? dimensions?.CounterpartyId,
            "fund" or "fundid" or "fund_id" => dimensions?.FundId,
            "entity" or "entityid" or "entity_id" => dimensions?.EntityId,
            "sleeve" or "sleeveid" or "sleeve_id" => dimensions?.SleeveId,
            "strategy" or "strategyid" or "strategy_id" => dimensions?.StrategyId,
            "investor" or "investorid" or "investor_id" => dimensions?.InvestorId,
            "capitalaccount" or "capitalaccountid" or "capital_account_id" => dimensions?.CapitalAccountId,
            "instrument" or "instrumentid" or "instrument_id" => dimensions?.InstrumentId?.ToString("D"),
            "taxlot" or "taxlotid" or "tax_lot_id" => dimensions?.TaxLotId,
            "costcenter" or "costcenterid" or "cost_center_id" => dimensions?.CostCenterId,
            "instrumentsymbol" or "instrument_symbol" => request.InstrumentSymbol,
            _ => ResolveExternalGlConditionField(normalizedField, dimensions?.ExternalGlDimensions)
        };
    }

    private static string? ResolveExternalGlConditionField(
        string field,
        IReadOnlyDictionary<string, string>? externalGlDimensions)
    {
        if (externalGlDimensions is null || externalGlDimensions.Count == 0)
        {
            return null;
        }

        var key = StripExternalGlConditionPrefix(field);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (externalGlDimensions.TryGetValue(key, out var value))
        {
            return value;
        }

        var normalizedKey = NormalizeConditionFieldKey(key);
        return externalGlDimensions
            .FirstOrDefault(pair => NormalizeConditionFieldKey(pair.Key) == normalizedKey)
            .Value;
    }

    private static string StripExternalGlConditionPrefix(string field)
    {
        var trimmed = field.Trim();
        foreach (var prefix in new[] { "externalgl", "external_gl", "external-gl", "external.gl", "gl" })
        {
            if (trimmed.Length <= prefix.Length ||
                !trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separator = trimmed[prefix.Length];
            if (separator is '.' or ':' or '/' or '\\')
            {
                return trimmed[(prefix.Length + 1)..].Trim();
            }

            if (separator == '[' && trimmed.EndsWith(']'))
            {
                return trimmed[(prefix.Length + 1)..^1].Trim();
            }
        }

        return trimmed;
    }

    private static string NormalizeConditionFieldKey(string value)
        => string.Concat(value.Where(static ch => char.IsLetterOrDigit(ch))).ToLowerInvariant();

    private static bool TryParseDecimal(string? value, out decimal result)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private static IEnumerable<AccountingRuleConditionDto> GetRuleConditions(PostingRuleDto rule)
        => rule.Conditions.Concat(rule.ConditionGroups.SelectMany(static group => group.Conditions));

    private static void ValidateConditionIdentity(
        string ruleId,
        AccountingRuleConditionDto condition,
        List<AccountingConfigurationValidationIssueDto> issues,
        string? targetId,
        string issueCode)
    {
        if (!string.IsNullOrWhiteSpace(condition.ConditionId))
        {
            return;
        }

        issues.Add(Issue(
            issueCode,
            AccountingConfigurationValidationSeverityDto.Critical,
            $"Posting rule '{ruleId}' has a condition without an id.",
            targetId,
            "Assign every rule condition a stable id before dry-run preview or activation."));
    }

    private static IEnumerable<AccountingConfigurationValidationIssueDto> BuildDuplicateConditionIdIssues(
        string ruleId,
        IEnumerable<AccountingRuleConditionDto> conditions,
        string issueCode)
    {
        foreach (var duplicate in conditions
            .Select(static condition => condition.ConditionId?.Trim())
            .Where(static conditionId => !string.IsNullOrWhiteSpace(conditionId))
            .GroupBy(static conditionId => conditionId!, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1))
        {
            yield return Issue(
                issueCode,
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Posting rule '{ruleId}' defines duplicate condition id '{duplicate.Key}'.",
                duplicate.Key,
                "Keep rule condition ids unique so test evidence, dry-run issues, and promotion review can identify one predicate.");
        }
    }

    private static void ValidateConditionOperand(
        string ruleId,
        AccountingRuleConditionDto condition,
        List<AccountingConfigurationValidationIssueDto> issues,
        string? targetId)
    {
        issues.AddRange(BuildInvalidConditionOperandIssues(
            ruleId,
            condition,
            targetId,
            "rule.condition-value-missing",
            "rule.condition-amount-invalid",
            "rule.condition-amount-range-invalid"));
    }

    private static IEnumerable<AccountingConfigurationValidationIssueDto> BuildInvalidConditionOperandIssues(
        string ruleId,
        AccountingRuleConditionDto condition,
        string? targetId,
        string missingValueIssueCode,
        string invalidAmountIssueCode,
        string invalidRangeIssueCode)
    {
        switch (condition.Operator)
        {
            case AccountingRuleConditionOperatorDto.Equals:
            case AccountingRuleConditionOperatorDto.NotEquals:
            case AccountingRuleConditionOperatorDto.Contains:
                if (string.IsNullOrWhiteSpace(condition.Value))
                {
                    yield return Issue(
                        missingValueIssueCode,
                        AccountingConfigurationValidationSeverityDto.Critical,
                        $"Text condition '{condition.ConditionId}' on rule '{ruleId}' is missing a comparison value.",
                        targetId,
                        "Enter the retained predicate value before dry-run preview or activation.");
                }

                break;
            case AccountingRuleConditionOperatorDto.AmountGreaterThanOrEqual:
            case AccountingRuleConditionOperatorDto.AmountLessThanOrEqual:
                if (!TryParseDecimal(condition.Value, out _))
                {
                    yield return Issue(
                        invalidAmountIssueCode,
                        AccountingConfigurationValidationSeverityDto.Critical,
                        $"Amount condition '{condition.ConditionId}' on rule '{ruleId}' has an invalid numeric value '{condition.Value ?? "missing"}'.",
                        targetId,
                        "Enter a valid invariant decimal amount for the condition threshold.");
                }

                break;
            case AccountingRuleConditionOperatorDto.AmountBetween:
                if (!TryParseDecimal(condition.Value, out var lower) || !TryParseDecimal(condition.SecondValue, out var upper))
                {
                    yield return Issue(
                        invalidAmountIssueCode,
                        AccountingConfigurationValidationSeverityDto.Critical,
                        $"Amount-between condition '{condition.ConditionId}' on rule '{ruleId}' has invalid numeric bounds '{condition.Value ?? "missing"}' and '{condition.SecondValue ?? "missing"}'.",
                        targetId,
                        "Enter valid invariant decimal amounts for both condition bounds.");
                }
                else if (lower > upper)
                {
                    yield return Issue(
                        invalidRangeIssueCode,
                        AccountingConfigurationValidationSeverityDto.Critical,
                        $"Amount-between condition '{condition.ConditionId}' on rule '{ruleId}' has lower bound {lower} greater than upper bound {upper}.",
                        targetId,
                        "Enter amount-between bounds with the lower amount less than or equal to the upper amount.");
                }

                break;
        }
    }

    private static IReadOnlyList<GeneratedPostingLineDto> BuildGeneratedPostingLines(
        PostingRuleDto rule,
        JournalEntryTemplateDto? template,
        RuleDryRunRequestDto request)
    {
        var eventDimensions = string.IsNullOrWhiteSpace(request.CounterpartyId)
            ? request.Dimensions
            : MergeDimensions(request.Dimensions, new LedgerDimensionSetDto(CounterpartyId: request.CounterpartyId));
        IReadOnlyList<GeneratedPostingLineDto> generatedLines;
        if (rule.GeneratedPostings.Count > 0)
        {
            generatedLines = rule.GeneratedPostings
                .Select(line => line with
                {
                    Amount = ResolveFormulaAmount(line.AmountFormulaId, line.Amount, rule.Formulas, request.EventAmount),
                    Currency = string.IsNullOrWhiteSpace(line.Currency) ? NormalizeCurrency(request.Currency) : line.Currency.Trim().ToUpperInvariant(),
                    Dimensions = MergeDimensions(rule.Scope, eventDimensions, line.Dimensions)
                })
                .ToArray();
        }
        else
        {
            generatedLines = template is null
                ? []
                : template.Lines
                .Select(line => new GeneratedPostingLineDto(
                    line.LineId,
                    line.AccountPath,
                    line.Side,
                    "template.amount",
                    line.Amount == 0m ? request.EventAmount : line.Amount,
                    string.IsNullOrWhiteSpace(line.Currency) ? NormalizeCurrency(request.Currency) : line.Currency.Trim().ToUpperInvariant(),
                    MergeDimensions(rule.Scope, eventDimensions),
                    line.Description))
                .ToArray();
        }

        return ApplyAllocations(generatedLines, rule.Allocations, rule.Formulas, request.EventAmount, request.Dimensions);
    }

    private static decimal ResolveFormulaAmount(
        string formulaId,
        decimal fallbackAmount,
        IReadOnlyList<AccountingRuleFormulaDto> formulas,
        decimal eventAmount)
    {
        var formula = formulas.FirstOrDefault(item => string.Equals(item.FormulaId, formulaId, StringComparison.OrdinalIgnoreCase));
        if (formula is null)
        {
            return fallbackAmount == 0m ? eventAmount : fallbackAmount;
        }

        return formula.Kind switch
        {
            AccountingRuleFormulaKindDto.SourceAmount => eventAmount,
            AccountingRuleFormulaKindDto.PercentageOfSourceAmount => Math.Round(eventAmount * formula.Value, 2, MidpointRounding.AwayFromZero),
            AccountingRuleFormulaKindDto.FixedAmount => formula.Value,
            AccountingRuleFormulaKindDto.AllocationResidual => fallbackAmount == 0m ? eventAmount : fallbackAmount,
            _ => fallbackAmount
        };
    }

    private static IReadOnlyList<GeneratedPostingLineDto> ApplyAllocations(
        IReadOnlyList<GeneratedPostingLineDto> generatedLines,
        IReadOnlyList<AllocationRuleDto> allocations,
        IReadOnlyList<AccountingRuleFormulaDto> formulas,
        decimal eventAmount,
        LedgerDimensionSetDto? requestDimensions)
    {
        var positiveAllocations = allocations
            .Select(allocation => allocation with { Weight = ResolveAllocationWeight(allocation, formulas, eventAmount) })
            .Where(static allocation => allocation.Weight > 0m)
            .OrderBy(static allocation => allocation.AllocationRuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (generatedLines.Count == 0 || positiveAllocations.Length == 0)
        {
            return generatedLines;
        }

        var totalWeight = positiveAllocations.Sum(static allocation => allocation.Weight);
        return generatedLines
            .SelectMany(line => AllocateGeneratedPostingLine(line, positiveAllocations, totalWeight, requestDimensions))
            .ToArray();
    }

    private static decimal ResolveAllocationWeight(
        AllocationRuleDto allocation,
        IReadOnlyList<AccountingRuleFormulaDto> formulas,
        decimal eventAmount)
    {
        var formulaId = NormalizeOptional(allocation.FormulaId);
        return formulaId is null
            ? allocation.Weight
            : ResolveFormulaAmount(formulaId, allocation.Weight, formulas, eventAmount);
    }

    private static IReadOnlyList<GeneratedPostingLineDto> AllocateGeneratedPostingLine(
        GeneratedPostingLineDto sourceLine,
        IReadOnlyList<AllocationRuleDto> allocations,
        decimal totalWeight,
        LedgerDimensionSetDto? requestDimensions)
    {
        var allocatedLines = new List<GeneratedPostingLineDto>(allocations.Count);
        var allocatedAmount = 0m;
        for (var index = 0; index < allocations.Count; index++)
        {
            var allocation = allocations[index];
            var amount = index == allocations.Count - 1
                ? sourceLine.Amount - allocatedAmount
                : Math.Round(sourceLine.Amount * allocation.Weight / totalWeight, 2, MidpointRounding.AwayFromZero);
            allocatedAmount += amount;

            allocatedLines.Add(sourceLine with
            {
                LineId = $"{sourceLine.LineId}:{allocation.AllocationRuleId}",
                Amount = amount,
                Dimensions = MergeDimensions(requestDimensions, sourceLine.Dimensions, allocation.TargetDimensions),
                Description = string.IsNullOrWhiteSpace(allocation.Description)
                    ? sourceLine.Description
                    : string.IsNullOrWhiteSpace(sourceLine.Description)
                        ? allocation.Description
                        : $"{sourceLine.Description} - {allocation.Description}"
            });
        }

        return allocatedLines;
    }

    private static LedgerDimensionSetDto? MergeDimensions(params LedgerDimensionSetDto?[] dimensions)
    {
        LedgerDimensionSetDto? merged = null;
        foreach (var dimension in dimensions.Where(static item => item is not null))
        {
            merged = MergeDimension(merged, dimension!);
        }

        return merged;
    }

    private static LedgerDimensionSetDto MergeDimension(LedgerDimensionSetDto? baseDimensions, LedgerDimensionSetDto overlay)
    {
        var externalGlDimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (baseDimensions is not null)
        {
            foreach (var pair in baseDimensions.ExternalGlDimensions)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    externalGlDimensions[pair.Key.Trim()] = pair.Value.Trim();
                }
            }
        }

        foreach (var pair in overlay.ExternalGlDimensions)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                externalGlDimensions[pair.Key.Trim()] = pair.Value.Trim();
            }
        }

        return new LedgerDimensionSetDto(
            FundId: FirstText(overlay.FundId, baseDimensions?.FundId),
            EntityId: FirstText(overlay.EntityId, baseDimensions?.EntityId),
            SleeveId: FirstText(overlay.SleeveId, baseDimensions?.SleeveId),
            StrategyId: FirstText(overlay.StrategyId, baseDimensions?.StrategyId),
            InvestorId: FirstText(overlay.InvestorId, baseDimensions?.InvestorId),
            CapitalAccountId: FirstText(overlay.CapitalAccountId, baseDimensions?.CapitalAccountId),
            InstrumentId: overlay.InstrumentId ?? baseDimensions?.InstrumentId,
            TaxLotId: FirstText(overlay.TaxLotId, baseDimensions?.TaxLotId),
            CostCenterId: FirstText(overlay.CostCenterId, baseDimensions?.CostCenterId),
            CounterpartyId: FirstText(overlay.CounterpartyId, baseDimensions?.CounterpartyId),
            ExternalGlDimensions: externalGlDimensions,
            OrganizationId: FirstText(overlay.OrganizationId, baseDimensions?.OrganizationId),
            PortfolioId: FirstText(overlay.PortfolioId, baseDimensions?.PortfolioId),
            BookId: FirstText(overlay.BookId, baseDimensions?.BookId),
            AccountId: FirstText(overlay.AccountId, baseDimensions?.AccountId),
            CustomerId: FirstText(overlay.CustomerId, baseDimensions?.CustomerId),
            VendorId: FirstText(overlay.VendorId, baseDimensions?.VendorId),
            ProjectId: FirstText(overlay.ProjectId, baseDimensions?.ProjectId));
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> EvaluateRuleTestCaseAssertions(
        AccountingRuleTestCaseDto testCase,
        RuleDryRunResultDto dryRun)
    {
        var issues = new List<AccountingConfigurationValidationIssueDto>();
        if (!string.IsNullOrWhiteSpace(testCase.ExpectedRuleId) &&
            !string.Equals(testCase.ExpectedRuleId, dryRun.SelectedRuleId, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Issue(
                "rule-test.expected-rule-mismatch",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Test case '{testCase.TestCaseId}' expected rule '{testCase.ExpectedRuleId}' but selected '{dryRun.SelectedRuleId ?? "none"}'.",
                testCase.TestCaseId,
                "Review rule priority, effective date, conditions, and dimensional scope."));
        }

        if (!string.IsNullOrWhiteSpace(testCase.ExpectedRuleVersion))
        {
            var selectedMatch = dryRun.RuleMatches.FirstOrDefault(match =>
                match.IsMatched &&
                string.Equals(match.RuleId, dryRun.SelectedRuleId, StringComparison.OrdinalIgnoreCase));
            if (!string.Equals(testCase.ExpectedRuleVersion.Trim(), selectedMatch?.RuleVersion, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(Issue(
                    "rule-test.expected-version-mismatch",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Test case '{testCase.TestCaseId}' expected rule version '{testCase.ExpectedRuleVersion}' but selected '{selectedMatch?.RuleVersion ?? "none"}'.",
                    testCase.TestCaseId,
                    "Review the saved regression case or update the posting rule version under test."));
            }
        }

        if (testCase.ExpectBalancedPosting != dryRun.IsPostingBalanced)
        {
            issues.Add(Issue(
                "rule-test.balance-mismatch",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Test case '{testCase.TestCaseId}' expected balanced posting '{testCase.ExpectBalancedPosting}' but dry run returned '{dryRun.IsPostingBalanced}'.",
                testCase.TestCaseId,
                "Review generated posting lines, formulas, allocations, and account validation issues."));
        }

        issues.AddRange(EvaluateRuleTestCaseGeneratedPostingAssertions(testCase, dryRun));

        var actualIssueCodes = dryRun.ValidationIssues
            .Select(static issue => issue.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var expectedCode in testCase.ExpectedIssueCodes.Where(static item => !string.IsNullOrWhiteSpace(item)))
        {
            if (!actualIssueCodes.Contains(expectedCode))
            {
                issues.Add(Issue(
                    "rule-test.expected-issue-missing",
                    AccountingConfigurationValidationSeverityDto.Warning,
                    $"Test case '{testCase.TestCaseId}' expected issue code '{expectedCode}' but the dry run did not return it.",
                    testCase.TestCaseId,
                    "Update the expected issue list or restore the rule validation behavior."));
            }
        }

        return issues;
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> EvaluateRuleTestCaseGeneratedPostingAssertions(
        AccountingRuleTestCaseDto testCase,
        RuleDryRunResultDto dryRun)
    {
        if (testCase.ExpectedGeneratedPostingLines.Count == 0)
        {
            return [];
        }

        var issues = new List<AccountingConfigurationValidationIssueDto>();
        if (testCase.ExpectedGeneratedPostingLines.Count != dryRun.GeneratedPostingLines.Count)
        {
            issues.Add(Issue(
                "rule-test.generated-line-count-mismatch",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Test case '{testCase.TestCaseId}' expected {testCase.ExpectedGeneratedPostingLines.Count} generated posting lines but dry run returned {dryRun.GeneratedPostingLines.Count}.",
                testCase.TestCaseId,
                "Update the expected generated posting lines or restore the rule formula/allocation behavior."));
        }

        var actualById = dryRun.GeneratedPostingLines
            .Where(static line => !string.IsNullOrWhiteSpace(line.LineId))
            .ToDictionary(static line => line.LineId.Trim(), StringComparer.OrdinalIgnoreCase);
        foreach (var expectedLine in testCase.ExpectedGeneratedPostingLines)
        {
            var expectedLineId = NormalizeOptional(expectedLine.LineId);
            if (expectedLineId is null)
            {
                issues.Add(Issue(
                    "rule-test.expected-generated-line-id-missing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Test case '{testCase.TestCaseId}' includes an expected generated posting line without a stable line id.",
                    testCase.TestCaseId,
                    "Capture expected generated posting lines with the line ids returned by dry-run preview."));
                continue;
            }

            if (!actualById.TryGetValue(expectedLineId, out var actualLine))
            {
                issues.Add(Issue(
                    "rule-test.generated-line-missing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Test case '{testCase.TestCaseId}' expected generated posting line '{expectedLineId}' but the dry run did not return it.",
                    expectedLineId,
                    "Update the expected generated posting lines or restore the rule formula/allocation behavior."));
                continue;
            }

            if (!GeneratedPostingLineMatches(expectedLine, actualLine))
            {
                issues.Add(Issue(
                    "rule-test.generated-line-mismatch",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Test case '{testCase.TestCaseId}' expected generated posting line '{expectedLineId}' to match account, side, amount, currency, formula id, and dimensions.",
                    expectedLineId,
                    "Review generated posting formulas, allocation targets, dimensional scope, and expected regression output."));
            }
        }

        return issues;
    }

    private static bool GeneratedPostingLineMatches(GeneratedPostingLineDto expected, GeneratedPostingLineDto actual)
        => string.Equals(NormalizeOptional(expected.AccountPath), NormalizeOptional(actual.AccountPath), StringComparison.OrdinalIgnoreCase) &&
           expected.Side == actual.Side &&
           expected.Amount == actual.Amount &&
           string.Equals(NormalizeCurrency(expected.Currency), NormalizeCurrency(actual.Currency), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(NormalizeOptional(expected.AmountFormulaId), NormalizeOptional(actual.AmountFormulaId), StringComparison.OrdinalIgnoreCase) &&
           DimensionsMatch(expected.Dimensions, actual.Dimensions);

    private static bool DimensionsMatch(LedgerDimensionSetDto? expected, LedgerDimensionSetDto? actual)
    {
        if (expected is null)
        {
            return actual is null;
        }

        if (actual is null)
        {
            return false;
        }

        return string.Equals(NormalizeOptional(expected.FundId), NormalizeOptional(actual.FundId), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeOptional(expected.EntityId), NormalizeOptional(actual.EntityId), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeOptional(expected.SleeveId), NormalizeOptional(actual.SleeveId), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeOptional(expected.StrategyId), NormalizeOptional(actual.StrategyId), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeOptional(expected.InvestorId), NormalizeOptional(actual.InvestorId), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeOptional(expected.CapitalAccountId), NormalizeOptional(actual.CapitalAccountId), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeDimensionValue(expected.InstrumentId), NormalizeDimensionValue(actual.InstrumentId), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeOptional(expected.TaxLotId), NormalizeOptional(actual.TaxLotId), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeOptional(expected.CostCenterId), NormalizeOptional(actual.CostCenterId), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeOptional(expected.CounterpartyId), NormalizeOptional(actual.CounterpartyId), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeOptional(expected.OrganizationId), NormalizeOptional(actual.OrganizationId), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeOptional(expected.PortfolioId), NormalizeOptional(actual.PortfolioId), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeOptional(expected.BookId), NormalizeOptional(actual.BookId), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeOptional(expected.AccountId), NormalizeOptional(actual.AccountId), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeOptional(expected.CustomerId), NormalizeOptional(actual.CustomerId), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeOptional(expected.VendorId), NormalizeOptional(actual.VendorId), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeOptional(expected.ProjectId), NormalizeOptional(actual.ProjectId), StringComparison.OrdinalIgnoreCase) &&
               DictionaryMatches(expected.ExternalGlDimensions, actual.ExternalGlDimensions);
    }

    private static string? NormalizeDimensionValue(Guid? value)
        => value?.ToString("D", CultureInfo.InvariantCulture);

    private static bool DictionaryMatches(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        var expectedNormalized = NormalizeDimensionDictionary(expected);
        var actualNormalized = NormalizeDimensionDictionary(actual);
        return expectedNormalized.Count == actualNormalized.Count &&
               expectedNormalized.All(pair =>
                   actualNormalized.TryGetValue(pair.Key, out var actualValue) &&
                   string.Equals(pair.Value, actualValue, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyDictionary<string, string> NormalizeDimensionDictionary(IReadOnlyDictionary<string, string> source)
        => source
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(static pair => pair.Key.Trim(), static pair => pair.Value.Trim(), StringComparer.OrdinalIgnoreCase);
}
