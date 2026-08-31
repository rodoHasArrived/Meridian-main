using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Banking;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Ledger;
using Meridian.Storage.Archival;
using Meridian.Storage.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

public sealed partial class AccountingConfigurationService
{
    private async Task<IReadOnlyList<AccountingConfigurationValidationIssueDto>> ValidateActivationReadinessAsync(
        AccountingConfigurationWorkspaceDto workspace,
        ActivateAccountingConfigurationRequest request,
        CancellationToken ct)
    {
        var scopedLedgerBookId = request.LedgerBookId ?? workspace.LedgerBookId;
        var ledgerBooks = await LoadLedgerBooksAsync(workspace.FundProfileId, scopedLedgerBookId, ct).ConfigureAwait(false);
        var workspaceForValidation = workspace with
        {
            LedgerBookId = scopedLedgerBookId,
            LedgerBooks = ledgerBooks
        };
        var issues = Validate(
            workspaceForValidation,
            requireLedgerBookSetup: scopedLedgerBookId.HasValue && _ledgerBookService is not null).ToList();
        var activeRules = workspace.PostingRules.Where(static rule => !rule.IsArchived).ToArray();
        var savedTestCases = workspace.RuleTestCases;

        if (!HasActivationEvidence(request.EvidenceLinks))
        {
            issues.Add(Issue(
                "configuration.activation-evidence-required",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Accounting configuration activation requires retained approval, certification, sign-off, review, or activation evidence.",
                workspace.FundProfileId,
                "Attach retained activation approval evidence before making the accounting configuration active."));
        }

        foreach (var rule in activeRules.Where(static rule => rule.RequiresPromotionApproval))
        {
            if (!HasSavedRegressionTestForRuleVersion(savedTestCases, rule))
            {
                issues.Add(Issue(
                    "posting-rule.test-case-required",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Posting rule '{rule.RuleId}' version '{NormalizeOptional(rule.RuleVersion) ?? "v1"}' requires a saved regression test case before activation.",
                    rule.RuleId,
                    "Save at least one rule test case that expects this posting rule and current rule version."));
            }
        }

        if (savedTestCases.Count > 0)
        {
            var suite = await ExecuteRuleTestCasesAsync(new ExecuteAccountingRuleTestCasesRequestDto(
                FundProfileId: workspace.FundProfileId,
                Actor: request.Actor,
                LedgerBookId: request.LedgerBookId ?? workspace.LedgerBookId,
                CorrelationId: request.CorrelationId), ct).ConfigureAwait(false);
            foreach (var failedCase in suite.Results.Where(static item => !item.Passed))
            {
                issues.Add(Issue(
                    "rule-test.activation-failed",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Rule test case '{failedCase.TestCaseId}' failed activation readiness checks.",
                    failedCase.TestCaseId,
                    "Run rule tests, review assertion issues, and fix the rule or expected result before activation."));
            }
        }

        return issues
            .OrderByDescending(static issue => issue.Severity)
            .ThenBy(static issue => issue.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsApprovedPromotion(PostingRuleDto rule)
    {
        var approval = rule.PromotionApproval;
        var ruleVersion = NormalizeOptional(rule.RuleVersion) ?? "v1";
        return approval is
        {
            ApprovalState: ManualJournalEntryStatusDto.Approved,
            ApprovedAtUtc: not null
        } &&
           !string.IsNullOrWhiteSpace(approval.ApprovedBy) &&
           HasPromotionApprovalEvidenceWithProvenance(approval.EvidenceLinks, rule.RuleId, ruleVersion, approval.ApprovalId);
    }

    private static bool HasPromotionApprovalEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(static link =>
            link.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("certification", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("signoff", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("sign-off", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static bool HasPromotionApprovalEvidenceWithProvenance(
        IReadOnlyList<string> evidenceLinks,
        string ruleId,
        string ruleVersion,
        string approvalId)
        => evidenceLinks.Any(link =>
            HasPromotionApprovalEvidence([link]) &&
            link.Contains(ruleId, StringComparison.OrdinalIgnoreCase) &&
            link.Contains(ruleVersion, StringComparison.OrdinalIgnoreCase) &&
            link.Contains(approvalId, StringComparison.OrdinalIgnoreCase));

    private static void EnsureRuleStudioHumanOrigin(OperationsActionOriginDto actionOrigin, string action)
        => OperationsOriginGuard.RequireHumanOperator(actionOrigin, action);

    private static bool HasActivationEvidence(IReadOnlyList<string>? evidenceLinks)
        => evidenceLinks?.Any(static link =>
            link.Contains("activation", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("certification", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("signoff", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("sign-off", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("review", StringComparison.OrdinalIgnoreCase)) == true;

    private static AccountingConfigurationValidationIssueDto BuildPostingRulePromotionApprovalRequiredIssue(PostingRuleDto rule)
        => Issue(
            "posting-rule.promotion-approval-required",
            AccountingConfigurationValidationSeverityDto.Critical,
            $"Posting rule '{rule.RuleId}' requires an approved promotion before activation.",
            rule.RuleId,
            "Attach an approved promotion with approval actor, timestamp, and retained approval, certification, sign-off, or review evidence.");

    private static string NormalizeCurrency(string? currency)
        => string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();

    private static IReadOnlyDictionary<string, ChartOfAccountsNodeDto> BuildChartByPath(IReadOnlyList<ChartOfAccountsNodeDto> chart)
        => chart
            .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static item => item.IsArchived).First(),
                StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> Validate(
        AccountingConfigurationWorkspaceDto workspace,
        bool requireLedgerBookSetup = false)
    {
        var issues = new List<AccountingConfigurationValidationIssueDto>();
        var chartByPath = BuildChartByPath(workspace.ChartOfAccounts);
        if (requireLedgerBookSetup && workspace.LedgerBookId is { } ledgerBookId &&
            !workspace.LedgerBooks.Any(book => book.LedgerBookId == ledgerBookId))
        {
            issues.Add(Issue(
                "configuration.ledger-book-missing",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Accounting configuration targets ledger book '{ledgerBookId:D}', but no matching ledger book setup was found.",
                ledgerBookId.ToString("D", CultureInfo.InvariantCulture),
                "Create or select the ledger book before activating book-scoped accounting configuration."));
        }

        if (workspace.ChartOfAccounts.Count == 0)
        {
            issues.Add(Issue("chart.empty", AccountingConfigurationValidationSeverityDto.Critical, "No chart-of-accounts nodes are configured.", null, "Create at least one account node."));
        }

        foreach (var duplicatePath in workspace.ChartOfAccounts
                     .Where(static node => !node.IsArchived)
                     .GroupBy(static node => node.Path, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1)
                     .Select(static group => group.Key))
        {
            issues.Add(Issue(
                "chart.path-duplicate",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Chart of accounts path '{duplicatePath}' is assigned to multiple active nodes.",
                duplicatePath,
                "Keep one active chart node per account path before posting, previewing, or exporting ledger activity."));
        }

        if (workspace.JournalTemplates.Count == 0)
        {
            issues.Add(Issue("templates.empty", AccountingConfigurationValidationSeverityDto.Critical, "No journal entry templates are configured.", null, "Create at least one balanced journal template."));
        }

        if (workspace.PostingRules.Count == 0)
        {
            issues.Add(Issue("posting-rules.empty", AccountingConfigurationValidationSeverityDto.Critical, "No posting rules map source events to templates.", null, "Create at least one posting rule."));
        }

        foreach (var template in workspace.JournalTemplates.Where(template => !template.IsArchived))
        {
            issues.AddRange(ValidateTemplate(template, workspace.ChartOfAccounts));
        }

        var templateIds = workspace.JournalTemplates
            .Where(template => !template.IsArchived)
            .Select(template => template.TemplateId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in workspace.PostingRules.Where(rule => !rule.IsArchived))
        {
            if (HasInvalidEffectiveWindow(rule))
            {
                issues.Add(BuildPostingRuleEffectiveWindowIssue(rule));
            }

            if (rule.RequiresPromotionApproval && !IsApprovedPromotion(rule))
            {
                issues.Add(BuildPostingRulePromotionApprovalRequiredIssue(rule));
            }

            if (rule.GeneratedPostings.Count == 0 && !templateIds.Contains(rule.TemplateId))
            {
                issues.Add(Issue("posting-rule.template-missing", AccountingConfigurationValidationSeverityDto.Critical, $"Posting rule '{rule.RuleId}' references missing template '{rule.TemplateId}'.", rule.RuleId, "Point the rule at an active journal template."));
            }

            issues.AddRange(ValidatePostingRuleConditions(rule));

            if (rule.GeneratedPostings.Count > 0)
            {
                issues.AddRange(ValidateGeneratedPostingLineIdentity(rule));
                issues.AddRange(ValidateGeneratedPostingAccountReferences(
                    rule,
                    chartByPath,
                    "posting-rule.generated-account-missing",
                    "posting-rule.generated-account-archived"));
                issues.AddRange(ValidatePostingRuleFormulaReferences(rule));

                var generatedDebits = rule.GeneratedPostings.Where(static line => line.Side == AccountingTemplateLineSideDto.Debit).Sum(static line => line.Amount);
                var generatedCredits = rule.GeneratedPostings.Where(static line => line.Side == AccountingTemplateLineSideDto.Credit).Sum(static line => line.Amount);
                if (generatedDebits != generatedCredits && rule.GeneratedPostings.All(static line => line.Amount > 0m))
                {
                    issues.Add(Issue("posting-rule.generated-unbalanced", AccountingConfigurationValidationSeverityDto.Warning, $"Posting rule '{rule.RuleId}' generated-posting static amounts are not balanced.", rule.RuleId, "Confirm formula-driven generated postings balance during dry run."));
                }
            }

            if (rule.Allocations.Count > 0)
            {
                issues.AddRange(ValidateAllocationRuleIdentity(rule));
                var positiveAllocationCount = 0;
                foreach (var allocation in rule.Allocations)
                {
                    var usesFormulaWeight = !string.IsNullOrWhiteSpace(allocation.FormulaId);
                    if (!usesFormulaWeight && allocation.Weight <= 0m)
                    {
                        issues.Add(Issue("posting-rule.allocation-weight", AccountingConfigurationValidationSeverityDto.Critical, $"Posting rule '{rule.RuleId}' has allocation '{allocation.AllocationRuleId}' with a non-positive weight.", allocation.AllocationRuleId, "Use positive allocation weights so dry-run previews can split generated posting lines."));
                    }
                    else
                    {
                        positiveAllocationCount++;
                    }
                }

                if (positiveAllocationCount == 0)
                {
                    issues.Add(Issue("posting-rule.allocations-empty", AccountingConfigurationValidationSeverityDto.Critical, $"Posting rule '{rule.RuleId}' has no positive allocation weights.", rule.RuleId, "Add at least one positive allocation weight or remove the allocation set."));
                }

                if (rule.GeneratedPostings.Count == 0)
                {
                    issues.AddRange(ValidatePostingRuleFormulaReferences(rule));
                }
            }
        }

        issues.AddRange(ValidatePostingRulePriorityConflicts(workspace.PostingRules));

        var ruleIds = workspace.PostingRules
            .Where(rule => !rule.IsArchived)
            .Select(rule => rule.RuleId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var testCaseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var testCase in workspace.RuleTestCases)
        {
            if (string.IsNullOrWhiteSpace(testCase.TestCaseId))
            {
                issues.Add(Issue("rule-test-case.id-missing", AccountingConfigurationValidationSeverityDto.Critical, "A rule test case is missing its id.", null, "Assign a stable test-case id before saving the workspace."));
                continue;
            }

            if (!testCaseIds.Add(testCase.TestCaseId))
            {
                issues.Add(Issue("rule-test-case.duplicate", AccountingConfigurationValidationSeverityDto.Critical, $"Rule test case '{testCase.TestCaseId}' is duplicated.", testCase.TestCaseId, "Keep one saved test case per id."));
            }

            if (string.IsNullOrWhiteSpace(testCase.DisplayName))
            {
                issues.Add(Issue("rule-test-case.name-missing", AccountingConfigurationValidationSeverityDto.Warning, $"Rule test case '{testCase.TestCaseId}' is missing a display name.", testCase.TestCaseId, "Give the regression case an operator-readable name."));
            }

            if (string.IsNullOrWhiteSpace(testCase.Request.SourceEventType))
            {
                issues.Add(Issue("rule-test-case.source-event-missing", AccountingConfigurationValidationSeverityDto.Critical, $"Rule test case '{testCase.TestCaseId}' has no source event type.", testCase.TestCaseId, "Choose the event predicate the test should exercise."));
            }

            if (testCase.Request.EventAmount < 0m)
            {
                issues.Add(Issue("rule-test-case.amount-negative", AccountingConfigurationValidationSeverityDto.Warning, $"Rule test case '{testCase.TestCaseId}' uses a negative event amount.", testCase.TestCaseId, "Confirm the sign convention or use a positive source-event amount."));
            }

            if (!string.IsNullOrWhiteSpace(testCase.ExpectedRuleId) && !ruleIds.Contains(testCase.ExpectedRuleId))
            {
                issues.Add(Issue("rule-test-case.expected-rule-missing", AccountingConfigurationValidationSeverityDto.Warning, $"Rule test case '{testCase.TestCaseId}' expects missing posting rule '{testCase.ExpectedRuleId}'.", testCase.TestCaseId, "Point the expected rule assertion at an active posting rule or archive the test."));
            }

            if (!string.IsNullOrWhiteSpace(testCase.ExpectedRuleId) && string.IsNullOrWhiteSpace(testCase.ExpectedRuleVersion))
            {
                issues.Add(Issue("rule-test-case.expected-version-missing", AccountingConfigurationValidationSeverityDto.Warning, $"Rule test case '{testCase.TestCaseId}' expects posting rule '{testCase.ExpectedRuleId}' without pinning the rule version.", testCase.TestCaseId, "Set the expected rule version so promotion coverage cannot drift silently."));
            }

            if (!HasRuleTestCaseEvidence(testCase.EvidenceLinks))
            {
                issues.Add(Issue("rule-test-case.evidence-required", AccountingConfigurationValidationSeverityDto.Critical, $"Rule test case '{testCase.TestCaseId}' has no retained regression evidence.", testCase.TestCaseId, "Attach retained regression, test, approval, certification, sign-off, or review evidence before using the saved case for activation."));
            }

            if (!HasRuleTestCaseEvidenceWithProvenance(testCase, testCase.EvidenceLinks))
            {
                issues.Add(Issue("rule-test-case.evidence-provenance-required", AccountingConfigurationValidationSeverityDto.Critical, $"Rule test case '{testCase.TestCaseId}' evidence does not identify the test case, expected rule, and expected rule version on the same artifact.", testCase.TestCaseId, "Attach retained evidence that names the test case, expected posting rule, and expected rule version."));
            }
        }

        return issues.OrderByDescending(issue => issue.Severity).ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool HasRuleTestCaseEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(static link =>
            link.Contains("regression", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("rule-test", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("test", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("certification", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("signoff", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("sign-off", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static bool HasRuleTestCaseEvidenceWithProvenance(
        AccountingRuleTestCaseDto testCase,
        IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(link =>
            HasRuleTestCaseEvidence([link]) &&
            EvidenceLinkContainsToken(link, testCase.TestCaseId) &&
            (string.IsNullOrWhiteSpace(testCase.ExpectedRuleId) ||
                EvidenceLinkContainsToken(link, testCase.ExpectedRuleId)) &&
            (string.IsNullOrWhiteSpace(testCase.ExpectedRuleVersion) ||
                EvidenceLinkContainsToken(link, testCase.ExpectedRuleVersion)));

    private static bool EvidenceLinkContainsToken(string link, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (link.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedToken = NormalizeEvidenceToken(token);
        return normalizedToken.Length > 0 &&
            NormalizeEvidenceToken(link).Contains(normalizedToken, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEvidenceToken(string value)
        => string.Concat(value.Where(static ch => char.IsLetterOrDigit(ch)));

    private static bool HasSavedRegressionTestForRuleVersion(
        IReadOnlyList<AccountingRuleTestCaseDto> testCases,
        PostingRuleDto rule)
        => GetSavedRegressionTestsForRuleVersion(testCases, rule).Count > 0;

    private static IReadOnlyList<AccountingRuleTestCaseDto> GetSavedRegressionTestsForRuleVersion(
        IReadOnlyList<AccountingRuleTestCaseDto> testCases,
        PostingRuleDto rule)
    {
        var currentVersion = NormalizeOptional(rule.RuleVersion) ?? "v1";
        return testCases.Where(testCase =>
            string.Equals(testCase.ExpectedRuleId, rule.RuleId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(testCase.ExpectedRuleVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static IEnumerable<AccountingConfigurationValidationIssueDto> ValidatePostingRuleConditions(PostingRuleDto rule)
    {
        foreach (var issue in BuildDuplicateConditionIdIssues(
            rule.RuleId,
            GetRuleConditions(rule),
            "posting-rule.condition-id-duplicate"))
        {
            yield return issue;
        }

        foreach (var condition in rule.Conditions.Where(static condition => string.IsNullOrWhiteSpace(condition.ConditionId)))
        {
            yield return Issue(
                "posting-rule.condition-id-missing",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Posting rule '{rule.RuleId}' has a condition without an id.",
                rule.RuleId,
                "Assign every rule condition a stable id before activation.");
        }

        foreach (var condition in rule.Conditions.Where(static condition => string.IsNullOrWhiteSpace(condition.Field)))
        {
            yield return Issue(
                "posting-rule.condition-field-missing",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Condition '{condition.ConditionId}' on rule '{rule.RuleId}' is missing a field.",
                condition.ConditionId,
                "Select a dry-run field for this rule condition.");
        }
        foreach (var condition in rule.Conditions)
        {
            foreach (var issue in BuildInvalidConditionOperandIssues(
                rule.RuleId,
                condition,
                condition.ConditionId,
                "posting-rule.condition-value-missing",
                "posting-rule.condition-amount-invalid",
                "posting-rule.condition-amount-range-invalid"))
            {
                yield return issue;
            }
        }

        foreach (var group in rule.ConditionGroups)
        {
            if (string.IsNullOrWhiteSpace(group.GroupId))
            {
                yield return Issue(
                    "posting-rule.condition-group-id-missing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Posting rule '{rule.RuleId}' has a condition group without an id.",
                    rule.RuleId,
                    "Assign each condition group a stable id.");
            }

            if (group.Conditions.Count == 0)
            {
                yield return Issue(
                    "posting-rule.condition-group-empty",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Condition group '{group.GroupId}' on rule '{rule.RuleId}' has no conditions.",
                    group.GroupId,
                    "Add at least one condition to the group or remove the group.");
            }

            foreach (var condition in group.Conditions.Where(static condition => string.IsNullOrWhiteSpace(condition.Field)))
            {
                yield return Issue(
                    "posting-rule.condition-field-missing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Condition '{condition.ConditionId}' in group '{group.GroupId}' on rule '{rule.RuleId}' is missing a field.",
                    condition.ConditionId,
                    "Select a dry-run field for this grouped rule condition.");
            }

            foreach (var condition in group.Conditions.Where(static condition => string.IsNullOrWhiteSpace(condition.ConditionId)))
            {
                yield return Issue(
                    "posting-rule.condition-id-missing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Condition group '{group.GroupId}' on rule '{rule.RuleId}' has a condition without an id.",
                    group.GroupId,
                    "Assign every grouped rule condition a stable id before activation.");
            }

            foreach (var condition in group.Conditions)
            {
                foreach (var issue in BuildInvalidConditionOperandIssues(
                    rule.RuleId,
                    condition,
                    condition.ConditionId,
                    "posting-rule.condition-value-missing",
                    "posting-rule.condition-amount-invalid",
                    "posting-rule.condition-amount-range-invalid"))
                {
                    yield return issue;
                }
            }
        }
    }

    private static IEnumerable<AccountingConfigurationValidationIssueDto> ValidatePostingRuleFormulaReferences(PostingRuleDto rule)
    {
        var issues = new List<AccountingConfigurationValidationIssueDto>();
        var formulaIds = rule.Formulas
            .Where(static formula => !string.IsNullOrWhiteSpace(formula.FormulaId))
            .Select(static formula => formula.FormulaId.Trim())
            .ToArray();
        var formulaIdSet = formulaIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var formula in rule.Formulas.Where(static formula => string.IsNullOrWhiteSpace(formula.FormulaId)))
        {
            issues.Add(Issue(
                "posting-rule.formula-id-missing",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Posting rule '{rule.RuleId}' has a formula without a formula id.",
                rule.RuleId,
                "Assign every formula a stable id before it can be referenced by generated postings or allocations."));
        }

        foreach (var duplicate in formulaIds
            .GroupBy(static formulaId => formulaId, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1))
        {
            issues.Add(Issue(
                "posting-rule.formula-duplicate",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Posting rule '{rule.RuleId}' defines duplicate formula id '{duplicate.Key}'.",
                rule.RuleId,
                "Keep formula ids unique so generated posting lines resolve deterministically."));
        }

        foreach (var line in rule.GeneratedPostings)
        {
            var formulaId = NormalizeOptional(line.AmountFormulaId);
            if (formulaId is null)
            {
                if (line.Amount <= 0m)
                {
                    issues.Add(Issue(
                        "posting-rule.generated-formula-missing",
                        AccountingConfigurationValidationSeverityDto.Critical,
                        $"Generated posting line '{line.LineId}' on rule '{rule.RuleId}' has no amount formula and no positive static amount.",
                        line.LineId,
                        "Reference a rule formula or provide a positive static amount for the generated line."));
                }

                continue;
            }

            if (!formulaIdSet.Contains(formulaId))
            {
                issues.Add(Issue(
                    "posting-rule.generated-formula-missing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Generated posting line '{line.LineId}' on rule '{rule.RuleId}' references missing formula '{formulaId}'.",
                    line.LineId,
                    "Add the formula to the rule or update the generated posting line formula reference."));
            }
        }

        foreach (var allocation in rule.Allocations)
        {
            var formulaId = NormalizeOptional(allocation.FormulaId);
            if (formulaId is null)
            {
                if (allocation.Basis == AllocationRuleBasisDto.CustomFormula)
                {
                    issues.Add(Issue(
                        "posting-rule.allocation-formula-missing",
                        AccountingConfigurationValidationSeverityDto.Critical,
                        $"Custom formula allocation '{allocation.AllocationRuleId}' on rule '{rule.RuleId}' has no formula reference.",
                        allocation.AllocationRuleId,
                        "Reference a rule formula or change the allocation basis to a static weighting method."));
                }

                continue;
            }

            var formula = rule.Formulas.FirstOrDefault(item => string.Equals(item.FormulaId, formulaId, StringComparison.OrdinalIgnoreCase));
            if (formula is null)
            {
                issues.Add(Issue(
                    "posting-rule.allocation-formula-missing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Allocation '{allocation.AllocationRuleId}' on rule '{rule.RuleId}' references missing formula '{formulaId}'.",
                    allocation.AllocationRuleId,
                    "Add the formula to the rule or clear the allocation formula reference."));
            }
            else if (FormulaAlwaysResolvesNonPositiveWeight(formula, allocation.Weight))
            {
                issues.Add(Issue(
                    "posting-rule.allocation-formula-nonpositive",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Allocation '{allocation.AllocationRuleId}' on rule '{rule.RuleId}' references formula '{formulaId}' that cannot produce a positive allocation weight.",
                    allocation.AllocationRuleId,
                    "Use a fixed or percentage formula value above zero, or provide a positive residual fallback weight."));
            }
        }

        return issues;
    }

    private static LedgerBookSetupCandidateDto? BuildLedgerBookSetupCandidate(
        AccountingConfigurationWorkspaceDto workspace,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> validationIssues)
    {
        var missingLedgerBookIssue = validationIssues.FirstOrDefault(issue =>
            string.Equals(issue.Code, "configuration.ledger-book-missing", StringComparison.OrdinalIgnoreCase));
        if (missingLedgerBookIssue is null)
        {
            return null;
        }

        var sourceBook = workspace.LedgerBooks.FirstOrDefault(book => book.LedgerBookId == workspace.LedgerBookId)
            ?? workspace.LedgerBooks.FirstOrDefault();
        if (sourceBook is null)
        {
            return null;
        }

        return new LedgerBookSetupCandidateDto(
            workspace.FundProfileId,
            sourceBook.FundStructureNodeId,
            sourceBook.FundStructureNodeKind,
            $"{sourceBook.DisplayName} configuration book",
            sourceBook.BaseCurrency,
            sourceBook.AccountingBasis,
            sourceBook.AccountingPolicyId,
            sourceBook.AccountingPolicyVersion,
            "Create a ledger book using the registered fund-structure scope before activating book-scoped accounting configuration.",
            Description: $"Created from Accounting Configure setup readiness for requested ledger book {workspace.LedgerBookId?.ToString("D", CultureInfo.InvariantCulture) ?? "fund scope"}.",
            SourceLedgerBookId: sourceBook.LedgerBookId,
            RequestedLedgerBookId: workspace.LedgerBookId);
    }

    private static IEnumerable<AccountingConfigurationValidationIssueDto> ValidateGeneratedPostingLineIdentity(PostingRuleDto rule)
    {
        foreach (var line in rule.GeneratedPostings.Where(static line => string.IsNullOrWhiteSpace(line.LineId)))
        {
            yield return Issue(
                "posting-rule.generated-line-id-missing",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Posting rule '{rule.RuleId}' has a generated posting line without a line id.",
                rule.RuleId,
                "Assign every generated posting line a stable id before dry-run preview or activation.");
        }

        foreach (var duplicate in rule.GeneratedPostings
            .Select(static line => line.LineId?.Trim())
            .Where(static lineId => !string.IsNullOrWhiteSpace(lineId))
            .GroupBy(static lineId => lineId!, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1))
        {
            yield return Issue(
                "posting-rule.generated-line-id-duplicate",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Posting rule '{rule.RuleId}' defines duplicate generated posting line id '{duplicate.Key}'.",
                duplicate.Key,
                "Keep generated posting line ids unique so previews, allocation expansion, evidence links, and regression assertions identify one line.");
        }
    }

    private static IEnumerable<AccountingConfigurationValidationIssueDto> ValidateGeneratedPostingAccountReferences(
        PostingRuleDto rule,
        IReadOnlyDictionary<string, ChartOfAccountsNodeDto> chartByPath,
        string missingIssueCode,
        string archivedIssueCode)
    {
        foreach (var line in rule.GeneratedPostings)
        {
            if (string.IsNullOrWhiteSpace(line.AccountPath))
            {
                yield return Issue(
                    missingIssueCode,
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Posting rule '{rule.RuleId}' has generated posting line '{line.LineId}' without an account path.",
                    string.IsNullOrWhiteSpace(line.LineId) ? rule.RuleId : line.LineId,
                    "Choose a chart account for every generated posting line.");
            }
            else if (!chartByPath.TryGetValue(line.AccountPath, out var account))
            {
                yield return Issue(
                    missingIssueCode,
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Generated posting line '{line.LineId}' on rule '{rule.RuleId}' references missing account path '{line.AccountPath}'.",
                    line.LineId,
                    "Create the chart account or map the generated posting line to an active account.");
            }
            else if (account.IsArchived)
            {
                yield return Issue(
                    archivedIssueCode,
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Generated posting line '{line.LineId}' on rule '{rule.RuleId}' references archived account path '{line.AccountPath}'.",
                    line.LineId,
                    "Map generated postings to active chart accounts.");
            }
        }
    }

    private static IEnumerable<AccountingConfigurationValidationIssueDto> ValidateAllocationRuleIdentity(PostingRuleDto rule)
    {
        foreach (var allocation in rule.Allocations.Where(static allocation => string.IsNullOrWhiteSpace(allocation.AllocationRuleId)))
        {
            yield return Issue(
                "posting-rule.allocation-id-missing",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Posting rule '{rule.RuleId}' has an allocation row without an allocation id.",
                rule.RuleId,
                "Assign every allocation row a stable id before dry-run preview or activation.");
        }

        foreach (var duplicate in rule.Allocations
            .Select(static allocation => allocation.AllocationRuleId?.Trim())
            .Where(static allocationId => !string.IsNullOrWhiteSpace(allocationId))
            .GroupBy(static allocationId => allocationId!, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1))
        {
            yield return Issue(
                "posting-rule.allocation-id-duplicate",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Posting rule '{rule.RuleId}' defines duplicate allocation id '{duplicate.Key}'.",
                duplicate.Key,
                "Keep allocation ids unique so generated posting expansion, dimensional targets, evidence links, and regression assertions identify one allocation.");
        }
    }

    private static bool FormulaAlwaysResolvesNonPositiveWeight(AccountingRuleFormulaDto formula, decimal fallbackWeight)
        => formula.Kind switch
        {
            AccountingRuleFormulaKindDto.FixedAmount => formula.Value <= 0m,
            AccountingRuleFormulaKindDto.PercentageOfSourceAmount => formula.Value <= 0m,
            AccountingRuleFormulaKindDto.AllocationResidual => fallbackWeight <= 0m,
            _ => false
        };

    private static IEnumerable<AccountingConfigurationValidationIssueDto> ValidatePostingRuleDryRunAllocationWeights(
        PostingRuleDto rule,
        decimal eventAmount)
    {
        foreach (var allocation in rule.Allocations)
        {
            var resolvedWeight = ResolveAllocationWeight(allocation, rule.Formulas, eventAmount);
            if (resolvedWeight <= 0m)
            {
                yield return Issue(
                    "rule.allocation-weight",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Allocation '{allocation.AllocationRuleId}' on rule '{rule.RuleId}' resolved to non-positive weight {resolvedWeight}.",
                    allocation.AllocationRuleId,
                    "Use a positive static weight or a formula that resolves to a positive allocation weight for this dry run.");
            }
        }
    }

    private static IEnumerable<AccountingConfigurationValidationIssueDto> ValidatePostingRulePriorityConflicts(
        IReadOnlyList<PostingRuleDto> rules)
    {
        var activeRules = rules
            .Where(static rule => !rule.IsArchived)
            .OrderBy(static rule => rule.SourceEventType, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(static rule => rule.Priority)
            .ThenBy(static rule => rule.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var leftIndex = 0; leftIndex < activeRules.Length; leftIndex++)
        {
            var left = activeRules[leftIndex];
            for (var rightIndex = leftIndex + 1; rightIndex < activeRules.Length; rightIndex++)
            {
                var right = activeRules[rightIndex];
                if (!string.Equals(left.SourceEventType, right.SourceEventType, StringComparison.OrdinalIgnoreCase) ||
                    left.Priority != right.Priority)
                {
                    continue;
                }

                if (!EffectiveWindowsOverlap(left, right) || !ScopesOverlap(left.Scope, right.Scope))
                {
                    continue;
                }

                if (!RulePredicatesCanOverlap(left, right))
                {
                    continue;
                }

                yield return Issue(
                    "posting-rule.priority-conflict",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Posting rules '{left.RuleId}' and '{right.RuleId}' share source event '{left.SourceEventType}', priority {left.Priority}, overlapping effective dates, and overlapping scope.",
                    left.RuleId,
                    "Assign a distinct priority, effective-date window, dimensional scope, or mutually exclusive predicate so dry-run rule selection is deterministic.");
            }
        }
    }

    private static bool EffectiveWindowsOverlap(PostingRuleDto left, PostingRuleDto right)
    {
        var leftFrom = left.EffectiveFrom ?? DateOnly.MinValue;
        var leftTo = left.EffectiveTo ?? DateOnly.MaxValue;
        var rightFrom = right.EffectiveFrom ?? DateOnly.MinValue;
        var rightTo = right.EffectiveTo ?? DateOnly.MaxValue;
        return leftFrom <= rightTo && rightFrom <= leftTo;
    }

    private static bool RulePredicatesCanOverlap(PostingRuleDto left, PostingRuleDto right)
    {
        var leftAmountRange = BuildRequiredAmountRange(left);
        var rightAmountRange = BuildRequiredAmountRange(right);
        return AmountRangesOverlap(leftAmountRange, rightAmountRange);
    }

    private static AmountPredicateRange BuildRequiredAmountRange(PostingRuleDto rule)
    {
        var range = AmountPredicateRange.Unbounded;
        foreach (var condition in rule.Conditions.Where(static condition => condition.IsRequired))
        {
            range = range.Intersect(BuildRequiredAmountRange(condition));
        }

        foreach (var group in rule.ConditionGroups.Where(static group =>
                     group.IsRequired && group.Operator == AccountingRuleConditionGroupOperatorDto.All))
        {
            foreach (var condition in group.Conditions.Where(static condition => condition.IsRequired))
            {
                range = range.Intersect(BuildRequiredAmountRange(condition));
            }
        }

        return range;
    }

    private static AmountPredicateRange BuildRequiredAmountRange(AccountingRuleConditionDto condition)
    {
        return condition.Operator switch
        {
            AccountingRuleConditionOperatorDto.AmountGreaterThanOrEqual
                when TryParseDecimal(condition.Value, out var minimum) => AmountPredicateRange.FromMinimum(minimum),
            AccountingRuleConditionOperatorDto.AmountLessThanOrEqual
                when TryParseDecimal(condition.Value, out var maximum) => AmountPredicateRange.FromMaximum(maximum),
            AccountingRuleConditionOperatorDto.AmountBetween
                when TryParseDecimal(condition.Value, out var lower) &&
                     TryParseDecimal(condition.SecondValue, out var upper) &&
                     lower <= upper => new AmountPredicateRange(lower, upper, true),
            _ => AmountPredicateRange.Unbounded
        };
    }

    private static bool AmountRangesOverlap(AmountPredicateRange left, AmountPredicateRange right)
    {
        if (!left.HasConstraint || !right.HasConstraint)
        {
            return true;
        }

        if (left.IsEmpty || right.IsEmpty)
        {
            return false;
        }

        var minimum = MaxNullable(left.Minimum, right.Minimum);
        var maximum = MinNullable(left.Maximum, right.Maximum);
        return minimum is null || maximum is null || minimum <= maximum;
    }

    private static decimal? MaxNullable(decimal? left, decimal? right)
        => left is null ? right : right is null ? left : Math.Max(left.Value, right.Value);

    private static decimal? MinNullable(decimal? left, decimal? right)
        => left is null ? right : right is null ? left : Math.Min(left.Value, right.Value);

    private static bool ScopesOverlap(LedgerDimensionSetDto? left, LedgerDimensionSetDto? right)
    {
        if (left is null || right is null)
        {
            return true;
        }

        return ScopeValuesOverlap(left.FundId, right.FundId) &&
               ScopeValuesOverlap(left.EntityId, right.EntityId) &&
               ScopeValuesOverlap(left.SleeveId, right.SleeveId) &&
               ScopeValuesOverlap(left.StrategyId, right.StrategyId) &&
               ScopeValuesOverlap(left.InvestorId, right.InvestorId) &&
               ScopeValuesOverlap(left.CapitalAccountId, right.CapitalAccountId) &&
               (!left.InstrumentId.HasValue || !right.InstrumentId.HasValue || left.InstrumentId == right.InstrumentId) &&
               (!left.PositionId.HasValue || !right.PositionId.HasValue || left.PositionId == right.PositionId) &&
               ScopeValuesOverlap(left.TaxLotId, right.TaxLotId) &&
               ScopeValuesOverlap(left.CostCenterId, right.CostCenterId) &&
               ScopeValuesOverlap(left.CounterpartyId, right.CounterpartyId) &&
               ExternalGlScopesOverlap(left.ExternalGlDimensions, right.ExternalGlDimensions);
    }

    private static bool ScopeValuesOverlap(string? left, string? right)
        => string.IsNullOrWhiteSpace(left) ||
           string.IsNullOrWhiteSpace(right) ||
           string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool ExternalGlScopesOverlap(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        foreach (var pair in left)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            if (right.TryGetValue(pair.Key, out var rightValue) &&
                !string.IsNullOrWhiteSpace(rightValue) &&
                !string.Equals(pair.Value.Trim(), rightValue.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<AccountingConfigurationValidationIssueDto> ValidateTemplate(
        JournalEntryTemplateDto template,
        IReadOnlyList<ChartOfAccountsNodeDto> chart)
    {
        var issues = new List<AccountingConfigurationValidationIssueDto>();
        if (template.Lines.Count == 0)
        {
            issues.Add(Issue("template.lines-empty", AccountingConfigurationValidationSeverityDto.Critical, $"Template '{template.TemplateId}' has no journal lines.", template.TemplateId, "Add at least one debit and credit line."));
            return issues;
        }

        var totalDebits = template.Lines.Where(line => line.Side == AccountingTemplateLineSideDto.Debit).Sum(line => line.Amount);
        var totalCredits = template.Lines.Where(line => line.Side == AccountingTemplateLineSideDto.Credit).Sum(line => line.Amount);
        if (totalDebits != totalCredits)
        {
            issues.Add(Issue("template.unbalanced", AccountingConfigurationValidationSeverityDto.Critical, $"Template '{template.TemplateId}' is unbalanced: debits={totalDebits}, credits={totalCredits}.", template.TemplateId, "Adjust line amounts so debits equal credits."));
        }

        var chartPaths = chart.Where(node => !node.IsArchived).Select(node => node.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var line in template.Lines)
        {
            if (line.Amount <= 0m)
            {
                issues.Add(Issue("template.line-amount", AccountingConfigurationValidationSeverityDto.Critical, $"Template '{template.TemplateId}' has a non-positive line amount.", line.LineId, "Use positive line amounts."));
            }

            if (!chartPaths.Contains(line.AccountPath))
            {
                issues.Add(Issue("template.account-missing", AccountingConfigurationValidationSeverityDto.Critical, $"Template '{template.TemplateId}' references missing account path '{line.AccountPath}'.", line.LineId, "Create the account node or update the template line."));
            }
        }

        return issues;
    }

    private static AccountingConfigurationValidationIssueDto Issue(
        string code,
        AccountingConfigurationValidationSeverityDto severity,
        string message,
        string? targetId,
        string? suggestedAction)
        => new(code, severity, message, targetId, suggestedAction);

    /// <summary>
    /// Digest of the workspace <b>as a store can hand it back</b>, used for the before- and
    /// after-state hashes an accounting audit event records.
    /// </summary>
    /// <remarks>
    /// <para>Taken over <see cref="Durable"/> rather than the DTO as it stands in memory. Every
    /// comparison this digest exists for — recovery asking whether the retained workspace is the one
    /// a mutation wrote — hashes one side before a save and the other side after a reload, so any
    /// field a store does not round-trip makes the two sides differ forever. Under PostgreSQL that
    /// was not hypothetical: <c>AfterHash</c> was taken over a workspace carrying a derived
    /// <c>RulesStudio</c> that <c>PostgresAccountingConfigurationStore</c> never persists and
    /// <c>GetAsync</c> rebuilds as null, so it could never match a reload. Both the replay path and
    /// the already-audited check then raised — and because every mutation runs recovery first, one
    /// interrupted mutation blocked the scope permanently (Codex review finding on PR #2871). The
    /// file posture round-trips the whole DTO as JSON, which is why no test on it could see this.</para>
    ///
    /// <para>This narrows what the digest covers, and deliberately so: a hash over fields no store
    /// retains is not a claim anyone can check later, which is the opposite of what an audit
    /// before/after pair is for.</para>
    /// </remarks>
    private static string Hash(AccountingConfigurationWorkspaceDto workspace)
    {
        var json = JsonSerializer.Serialize(Durable(workspace));
        return Sha256Digest.ComputeUtf8(json);
    }

    /// <summary>
    /// Projects a workspace onto the shape every <see cref="IAccountingConfigurationStore"/> posture
    /// retains and returns, so the same state digests alike whichever store is composed.
    /// </summary>
    /// <remarks>
    /// <para><b>Derived views are dropped.</b> <c>RulesStudio</c> and <c>LedgerBookSetupCandidate</c>
    /// are computed by <c>GetWorkspaceAsync</c> from the state below them; they are a rendering of
    /// the workspace, not part of it. <c>LedgerBooks</c> and <c>AuditTrail</c> are likewise composed
    /// from other services on read, and both PostgreSQL and the file store return them empty.</para>
    ///
    /// <para><b>Collections are ordered.</b> The PostgreSQL store reads each one back under its own
    /// <c>order by</c>, so a digest over the in-memory sequence would depend on the order a caller
    /// happened to build it in. Ordering here makes the digest a function of the content alone.</para>
    ///
    /// <para><b>Optional text is reduced the way a store reduces it.</b>
    /// <c>PostgresAccountingConfigurationStore.ReplaceChartAsync</c> writes <c>ParentPath</c>,
    /// <c>Symbol</c> and <c>FinancialAccountId</c> through <c>AddTextOrNull</c>, which trims and
    /// nulls blank text, so a padded or blank value reloads as something the digest never covered
    /// (Codex review finding on PR #2871). This is the rule <c>NormalizeForPersistence</c> already
    /// applies to the audit event itself, applied to the workspace for the same reason: what is
    /// hashed and what is written have to be the same string. Both postures then also agree that
    /// <c>"  x  "</c> and <c>"x"</c> are one configuration, which is the answer either would give
    /// if asked.</para>
    ///
    /// <para><b>The timestamp is reduced to storable precision.</b> <c>timestamptz</c> holds
    /// microseconds and Npgsql truncates to them when it encodes the parameter, so a workspace
    /// hashed at the full 100ns tick and then reloaded digests to two different values — the second
    /// half of the same permanent block, and load-bearing independently of the dropped fields above.
    /// See <see cref="AccountingAuditChain.ToRetainedPrecision"/>.</para>
    /// </remarks>
    private static AccountingConfigurationWorkspaceDto Durable(AccountingConfigurationWorkspaceDto workspace)
        => workspace with
        {
            UpdatedAtUtc = AccountingAuditChain.ToRetainedPrecision(workspace.UpdatedAtUtc),
            LedgerBooks = [],
            AuditTrail = [],
            RulesStudio = null,
            LedgerBookSetupCandidate = null,
            ChartOfAccounts =
            [
                .. workspace.ChartOfAccounts
                    .Select(static n => n with
                    {
                        ParentPath = NormalizeOptional(n.ParentPath),
                        Symbol = NormalizeOptional(n.Symbol),
                        FinancialAccountId = NormalizeOptional(n.FinancialAccountId),
                    })
                    .OrderBy(static n => n.Path, StringComparer.Ordinal)
                    .ThenBy(static n => n.NodeId, StringComparer.Ordinal)
            ],
            JournalTemplates = [.. workspace.JournalTemplates.OrderBy(static t => t.TemplateId, StringComparer.Ordinal)],
            PostingRules = [.. workspace.PostingRules.OrderBy(static r => r.RuleId, StringComparer.Ordinal)],
            RuleTestCases = [.. workspace.RuleTestCases.OrderBy(static c => c.TestCaseId, StringComparer.Ordinal)],
        };

    private sealed record PostingRuleApprovalProtectedDefinition(
        string RuleVersion,
        string SourceEventType,
        string TemplateId,
        DateOnly? EffectiveFrom,
        DateOnly? EffectiveTo,
        int Priority,
        LedgerDimensionSetDto? Scope,
        IReadOnlyList<AccountingRuleConditionDto> Conditions,
        IReadOnlyList<AccountingRuleConditionGroupDto> ConditionGroups,
        IReadOnlyList<AccountingRuleFormulaDto> Formulas,
        IReadOnlyList<AllocationRuleDto> Allocations,
        IReadOnlyList<GeneratedPostingLineDto> GeneratedPostings);

    private readonly record struct AmountPredicateRange(decimal? Minimum, decimal? Maximum, bool HasConstraint)
    {
        public static AmountPredicateRange Unbounded { get; } = new(null, null, false);

        public bool IsEmpty => Minimum is not null && Maximum is not null && Minimum > Maximum;

        public static AmountPredicateRange FromMinimum(decimal minimum)
            => new(minimum, null, true);

        public static AmountPredicateRange FromMaximum(decimal maximum)
            => new(null, maximum, true);

        public AmountPredicateRange Intersect(AmountPredicateRange other)
        {
            if (!other.HasConstraint)
            {
                return this;
            }

            if (!HasConstraint)
            {
                return other;
            }

            return new AmountPredicateRange(
                MaxNullable(Minimum, other.Minimum),
                MinNullable(Maximum, other.Maximum),
                true);
        }
    }

    private static string NormalizeFundProfileId(string? value)
        => string.IsNullOrWhiteSpace(value) ? DefaultFundProfileId : value.Trim();

    private static string RequireText(string? value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();

    private static string? FirstText(string? preferred, string? fallback)
        => NormalizeOptional(preferred) ?? NormalizeOptional(fallback);

    private static IReadOnlyList<string> NormalizePrincipalIds(IReadOnlyList<string>? values)
        => values?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? [];
}
