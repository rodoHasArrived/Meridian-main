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
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

public sealed partial class AccountingConfigurationService : IAccountingConfigurationService
{
    private const string DefaultFundProfileId = "default-fund";

    private readonly IAccountingConfigurationStore _store;
    private readonly IAccountingActionAuditStore _auditStore;
    private readonly ILedgerBookService? _ledgerBookService;

    public AccountingConfigurationService(
        IAccountingConfigurationStore store,
        IAccountingActionAuditStore auditStore,
        ILedgerBookService? ledgerBookService = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _ledgerBookService = ledgerBookService;
    }

    public async Task<AccountingConfigurationWorkspaceDto> GetWorkspaceAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var workspace = await LoadWorkspaceAsync(normalizedFundProfileId, ledgerBookId, ct, tenantId, companyId).ConfigureAwait(false);
        var ledgerBooks = await LoadLedgerBooksAsync(normalizedFundProfileId, ledgerBookId, ct).ConfigureAwait(false);
        var audit = await _auditStore.ListAsync(normalizedFundProfileId, ledgerBookId, ct, tenantId, companyId).ConfigureAwait(false);
        var scopedWorkspace = workspace with
        {
            LedgerBookId = ledgerBookId ?? workspace.LedgerBookId,
            TenantId = NormalizeOptional(tenantId) ?? workspace.TenantId,
            CompanyId = NormalizeOptional(companyId) ?? workspace.CompanyId,
            LedgerBooks = ledgerBooks
        };
        var validation = Validate(
            scopedWorkspace,
            requireLedgerBookSetup: ledgerBookId.HasValue && _ledgerBookService is not null);

        return workspace with
        {
            LedgerBookId = scopedWorkspace.LedgerBookId,
            LedgerBooks = ledgerBooks,
            ValidationIssues = validation,
            AuditTrail = audit,
            RulesStudio = BuildRulesStudio(scopedWorkspace, validation),
            LedgerBookSetupCandidate = BuildLedgerBookSetupCandidate(scopedWorkspace, validation)
        };
    }

    public async Task<AccountingConfigurationWorkspaceDto> UpsertChartNodeAsync(
        UpsertChartOfAccountsNodeRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await LoadWorkspaceAsync(NormalizeFundProfileId(request.FundProfileId), request.LedgerBookId, ct, request.TenantId, request.CompanyId).ConfigureAwait(false);
        var beforeHash = Hash(workspace);
        RequireText(request.Node.NodeId, nameof(request.Node.NodeId));
        RequireText(request.Node.Path, nameof(request.Node.Path));
        RequireText(request.Node.AccountName, nameof(request.Node.AccountName));
        RequireText(request.Node.AccountType, nameof(request.Node.AccountType));

        var nodes = workspace.ChartOfAccounts
            .Where(item => !string.Equals(item.NodeId, request.Node.NodeId, StringComparison.OrdinalIgnoreCase))
            .Append(request.Node)
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        workspace = workspace with
        {
            Status = AccountingConfigurationStatusDto.Draft,
            ChartOfAccounts = nodes,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        return await SaveWithAuditAsync(workspace, beforeHash, request.Actor, "chart.upsert", request.LedgerBookId, request.CorrelationId, request.EvidenceLinks, request.TenantId, request.CompanyId, request.ReportGroupPrincipalIds, ct).ConfigureAwait(false);
    }

    public async Task<AccountingConfigurationWorkspaceDto> UpsertTemplateAsync(
        UpsertJournalEntryTemplateRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await LoadWorkspaceAsync(NormalizeFundProfileId(request.FundProfileId), request.LedgerBookId, ct, request.TenantId, request.CompanyId).ConfigureAwait(false);
        var beforeHash = Hash(workspace);
        RequireText(request.Template.TemplateId, nameof(request.Template.TemplateId));
        RequireText(request.Template.DisplayName, nameof(request.Template.DisplayName));

        var templates = workspace.JournalTemplates
            .Where(item => !string.Equals(item.TemplateId, request.Template.TemplateId, StringComparison.OrdinalIgnoreCase))
            .Append(request.Template)
            .OrderBy(item => item.TemplateId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        workspace = workspace with
        {
            Status = AccountingConfigurationStatusDto.Draft,
            JournalTemplates = templates,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        return await SaveWithAuditAsync(workspace, beforeHash, request.Actor, "template.upsert", request.LedgerBookId, request.CorrelationId, request.EvidenceLinks, request.TenantId, request.CompanyId, request.ReportGroupPrincipalIds, ct).ConfigureAwait(false);
    }

    public async Task<AccountingConfigurationWorkspaceDto> UpsertPostingRuleAsync(
        UpsertPostingRuleRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await LoadWorkspaceAsync(NormalizeFundProfileId(request.FundProfileId), request.LedgerBookId, ct, request.TenantId, request.CompanyId).ConfigureAwait(false);
        var beforeHash = Hash(workspace);
        RequireText(request.Rule.RuleId, nameof(request.Rule.RuleId));
        RequireText(request.Rule.SourceEventType, nameof(request.Rule.SourceEventType));
        if (request.Rule.GeneratedPostings.Count == 0)
        {
            RequireText(request.Rule.TemplateId, nameof(request.Rule.TemplateId));
        }

        var existingRule = workspace.PostingRules.FirstOrDefault(item =>
            string.Equals(item.RuleId, request.Rule.RuleId, StringComparison.OrdinalIgnoreCase));
        var incomingRule = ResetCarriedForwardPromotionApproval(existingRule, request.Rule);
        var rule = incomingRule with
        {
            Versions = BuildPostingRuleVersionHistory(existingRule, incomingRule, request.Actor, request.EvidenceLinks)
        };
        var rules = workspace.PostingRules
            .Where(item => !string.Equals(item.RuleId, request.Rule.RuleId, StringComparison.OrdinalIgnoreCase))
            .Append(rule)
            .OrderBy(item => item.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        workspace = workspace with
        {
            Status = AccountingConfigurationStatusDto.Draft,
            PostingRules = rules,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        return await SaveWithAuditAsync(workspace, beforeHash, request.Actor, "posting-rule.upsert", request.LedgerBookId, request.CorrelationId, request.EvidenceLinks, request.TenantId, request.CompanyId, request.ReportGroupPrincipalIds, ct).ConfigureAwait(false);
    }

    public async Task<AccountingConfigurationWorkspaceDto> ApprovePostingRulePromotionAsync(
        ApprovePostingRulePromotionRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await LoadWorkspaceAsync(NormalizeFundProfileId(request.FundProfileId), request.LedgerBookId, ct, request.TenantId, request.CompanyId).ConfigureAwait(false);
        var beforeHash = Hash(workspace);
        var ruleId = RequireText(request.RuleId, nameof(request.RuleId));
        var ruleVersion = RequireText(request.RuleVersion, nameof(request.RuleVersion));
        var actor = RequireText(request.Actor, nameof(request.Actor));
        EnsureRuleStudioHumanOrigin(request.ActionOrigin, "approve posting rule promotions");
        var approvalId = RequireText(request.ApprovalId, nameof(request.ApprovalId));
        var notes = RequireText(request.Notes, nameof(request.Notes));
        var evidenceLinks = NormalizeRuleEvidenceLinks(request.EvidenceLinks);
        if (!HasPromotionApprovalEvidence(evidenceLinks))
        {
            throw new InvalidOperationException("Posting rule promotion approval requires retained approval, certification, sign-off, or review evidence.");
        }

        if (!HasPromotionApprovalEvidenceWithProvenance(evidenceLinks, ruleId, ruleVersion, approvalId))
        {
            throw new InvalidOperationException("Posting rule promotion approval evidence must reference the retained rule, rule version, and approval id in the same artifact.");
        }

        var existingRule = workspace.PostingRules.FirstOrDefault(item =>
            string.Equals(item.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));
        if (existingRule is null)
        {
            throw new ArgumentException($"Posting rule '{ruleId}' was not found.", nameof(request.RuleId));
        }

        var currentVersion = NormalizeOptional(existingRule.RuleVersion) ?? "v1";
        if (!string.Equals(currentVersion, ruleVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Posting rule '{ruleId}' is currently at version '{currentVersion}' and cannot approve requested version '{ruleVersion}'.");
        }

        if (IsApprovedPromotion(existingRule))
        {
            if (!string.Equals(existingRule.PromotionApproval!.ApprovalId, approvalId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Posting rule '{ruleId}' version '{currentVersion}' is already approved by promotion '{existingRule.PromotionApproval.ApprovalId}'.");
            }

            return workspace;
        }

        var promotionTestCases = GetSavedRegressionTestsForRuleVersion(workspace.RuleTestCases, existingRule);
        if (promotionTestCases.Count == 0)
        {
            throw new InvalidOperationException("Posting rule promotion approval requires at least one saved regression test case for the current rule version.");
        }

        if (promotionTestCases.Any(testCase => !HasRuleTestCaseEvidence(testCase.EvidenceLinks)))
        {
            throw new InvalidOperationException("Posting rule promotion approval requires retained regression evidence on every current-version saved test case.");
        }

        if (promotionTestCases.Any(testCase => !HasRuleTestCaseEvidenceWithProvenance(testCase, testCase.EvidenceLinks)))
        {
            throw new InvalidOperationException("Posting rule promotion approval requires every current-version saved test case evidence to reference the test case, expected rule, and expected rule version in the same artifact.");
        }

        var promotionSuite = await ExecuteRuleTestCasesAsync(new ExecuteAccountingRuleTestCasesRequestDto(
            workspace.FundProfileId,
            actor,
            promotionTestCases,
            workspace.LedgerBookId,
            request.CorrelationId,
            request.TenantId,
            request.CompanyId), ct).ConfigureAwait(false);
        if (promotionSuite.Results.Any(static result => !result.Passed))
        {
            throw new InvalidOperationException("Posting rule promotion approval requires all saved regression tests for the current rule version to pass.");
        }

        var now = DateTimeOffset.UtcNow;
        var approval = new RulePromotionApprovalDto(
            approvalId,
            NormalizeOptional(request.RequestedBy) ?? existingRule.PromotionApproval?.RequestedBy ?? actor,
            request.RequestedAtUtc ?? existingRule.PromotionApproval?.RequestedAtUtc ?? now,
            ManualJournalEntryStatusDto.Approved,
            ApprovedBy: actor,
            ApprovedAtUtc: now,
            Notes: notes,
            EvidenceLinks: evidenceLinks);

        var updatedRule = existingRule with
        {
            RequiresPromotionApproval = true,
            PromotionApproval = approval,
            Versions = ApplyPostingRuleVersionPromotionApproval(existingRule, approval, actor, evidenceLinks)
        };
        var rules = workspace.PostingRules
            .Where(item => !string.Equals(item.RuleId, ruleId, StringComparison.OrdinalIgnoreCase))
            .Append(updatedRule)
            .OrderBy(item => item.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        workspace = workspace with
        {
            Status = AccountingConfigurationStatusDto.Draft,
            PostingRules = rules,
            UpdatedAtUtc = now
        };

        return await SaveWithAuditAsync(workspace, beforeHash, actor, "posting-rule.promotion-approve", request.LedgerBookId, request.CorrelationId, evidenceLinks, request.TenantId, request.CompanyId, request.ReportGroupPrincipalIds, ct).ConfigureAwait(false);
    }

    public async Task<AccountingConfigurationWorkspaceDto> UpsertRuleTestCaseAsync(
        UpsertAccountingRuleTestCaseRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.TestCase);

        var workspace = await LoadWorkspaceAsync(NormalizeFundProfileId(request.FundProfileId), request.LedgerBookId, ct, request.TenantId, request.CompanyId).ConfigureAwait(false);
        var beforeHash = Hash(workspace);
        RequireText(request.TestCase.TestCaseId, nameof(request.TestCase.TestCaseId));
        RequireText(request.TestCase.DisplayName, nameof(request.TestCase.DisplayName));
        RequireText(request.TestCase.Request.SourceEventType, nameof(request.TestCase.Request.SourceEventType));

        var evidenceLinks = NormalizeRuleEvidenceLinks(request.TestCase.EvidenceLinks.Concat(request.EvidenceLinks ?? []).ToArray());
        var testCases = workspace.RuleTestCases
            .Where(item => !string.Equals(item.TestCaseId, request.TestCase.TestCaseId, StringComparison.OrdinalIgnoreCase))
            .Append(request.TestCase with
            {
                Request = request.TestCase.Request with
                {
                    FundProfileId = workspace.FundProfileId,
                    LedgerBookId = request.TestCase.Request.LedgerBookId ?? request.LedgerBookId ?? workspace.LedgerBookId
                },
                EvidenceLinks = evidenceLinks
            })
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.TestCaseId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        workspace = workspace with
        {
            Status = AccountingConfigurationStatusDto.Draft,
            RuleTestCases = testCases,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        return await SaveWithAuditAsync(workspace, beforeHash, request.Actor, "rule-test-case.upsert", request.LedgerBookId, request.CorrelationId, request.EvidenceLinks, request.TenantId, request.CompanyId, request.ReportGroupPrincipalIds, ct).ConfigureAwait(false);
    }

    public async Task<AccountingJournalTemplatePreviewDto> PreviewTemplateAsync(
        PreviewJournalTemplateRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await LoadWorkspaceAsync(NormalizeFundProfileId(request.FundProfileId), request.LedgerBookId, ct, request.TenantId, request.CompanyId).ConfigureAwait(false);
        var template = workspace.JournalTemplates.FirstOrDefault(item =>
            string.Equals(item.TemplateId, request.TemplateId, StringComparison.OrdinalIgnoreCase) && !item.IsArchived);

        if (template is null)
        {
            return new AccountingJournalTemplatePreviewDto(
                request.TemplateId,
                "Missing template",
                IsBalanced: false,
                TotalDebits: 0m,
                TotalCredits: 0m,
                Lines: [],
                ValidationIssues:
                [
                    Issue("template.missing", AccountingConfigurationValidationSeverityDto.Critical, $"Template '{request.TemplateId}' was not found.", request.TemplateId, "Create or unarchive the journal template before preview.")
                ]);
        }

        var chartByPath = BuildChartByPath(workspace.ChartOfAccounts);
        var lines = template.Lines
            .Select(line =>
            {
                chartByPath.TryGetValue(line.AccountPath, out var node);
                return new AccountingJournalPreviewLineDto(
                    line.AccountPath,
                    node?.AccountName ?? line.AccountPath,
                    line.Side,
                    line.Amount,
                    line.Currency,
                    line.Description);
            })
            .ToArray();
        var issues = ValidateTemplate(template, workspace.ChartOfAccounts).ToArray();
        var totalDebits = template.Lines.Where(line => line.Side == AccountingTemplateLineSideDto.Debit).Sum(line => line.Amount);
        var totalCredits = template.Lines.Where(line => line.Side == AccountingTemplateLineSideDto.Credit).Sum(line => line.Amount);

        return new AccountingJournalTemplatePreviewDto(
            template.TemplateId,
            template.DisplayName,
            totalDebits == totalCredits && issues.All(issue => issue.Severity != AccountingConfigurationValidationSeverityDto.Critical),
            totalDebits,
            totalCredits,
            lines,
            issues);
    }

    public async Task<RuleDryRunResultDto> DryRunPostingRuleAsync(
        RuleDryRunRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await LoadWorkspaceAsync(NormalizeFundProfileId(request.FundProfileId), request.LedgerBookId, ct, request.TenantId, request.CompanyId).ConfigureAwait(false);
        var chartByPath = BuildChartByPath(workspace.ChartOfAccounts);
        var templateById = workspace.JournalTemplates
            .Where(static item => !item.IsArchived)
            .ToDictionary(static item => item.TemplateId, StringComparer.OrdinalIgnoreCase);
        var sourceEventRules = workspace.PostingRules
            .Where(rule => !rule.IsArchived)
            .Where(rule => string.Equals(rule.SourceEventType, request.SourceEventType, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var invalidEffectiveWindowRules = sourceEventRules
            .Where(HasInvalidEffectiveWindow)
            .OrderByDescending(static rule => rule.Priority)
            .ThenBy(static rule => rule.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var candidates = sourceEventRules
            .Where(rule => !HasInvalidEffectiveWindow(rule))
            .Where(rule => IsEffective(rule, request.EffectiveDate))
            .OrderByDescending(static rule => rule.Priority)
            .ThenBy(static rule => rule.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var matches = new List<AccountingRuleDryRunMatchDto>(invalidEffectiveWindowRules.Length + candidates.Length);
        var issues = new List<AccountingConfigurationValidationIssueDto>();
        var matchedCandidateRules = new List<PostingRuleDto>();
        PostingRuleDto? selectedRule = null;

        foreach (var rule in invalidEffectiveWindowRules)
        {
            matches.Add(new AccountingRuleDryRunMatchDto(
                rule.RuleId,
                rule.DisplayName,
                rule.RuleVersion,
                rule.Priority,
                IsMatched: false,
                Explanations: ["Rule effective-date window is invalid."],
                ValidationIssues: [BuildPostingRuleEffectiveWindowIssue(rule)]));
        }

        foreach (var rule in candidates)
        {
            var ruleIssues = new List<AccountingConfigurationValidationIssueDto>();
            var explanations = new List<string>();
            if (!templateById.ContainsKey(rule.TemplateId) && rule.GeneratedPostings.Count == 0)
            {
                ruleIssues.Add(Issue("rule.template-missing", AccountingConfigurationValidationSeverityDto.Critical, $"Posting rule '{rule.RuleId}' references missing template '{rule.TemplateId}'.", rule.RuleId, "Select an active template or configure generated posting lines."));
            }
            ruleIssues.AddRange(ValidateGeneratedPostingLineIdentity(rule));
            ruleIssues.AddRange(ValidateGeneratedPostingAccountReferences(
                rule,
                chartByPath,
                "rule.generated-account-missing",
                "rule.generated-account-archived"));
            ruleIssues.AddRange(ValidateAllocationRuleIdentity(rule));
            ruleIssues.AddRange(ValidatePostingRuleFormulaReferences(rule));
            ruleIssues.AddRange(ValidatePostingRuleDryRunAllocationWeights(rule, request.EventAmount));

            var scopeMatches = MatchesScope(rule.Scope, request.Dimensions, request.CounterpartyId);
            if (!scopeMatches)
            {
                explanations.Add("Rule scope does not match the dry-run dimensions.");
            }

            var conditionsMatch = EvaluateConditions(rule, request, ruleIssues, explanations);
            var isMatched = scopeMatches && conditionsMatch && ruleIssues.All(issue => issue.Severity != AccountingConfigurationValidationSeverityDto.Critical);
            if (isMatched && selectedRule is null)
            {
                selectedRule = rule;
            }
            if (isMatched)
            {
                matchedCandidateRules.Add(rule);
            }

            matches.Add(new AccountingRuleDryRunMatchDto(
                rule.RuleId,
                rule.DisplayName,
                rule.RuleVersion,
                rule.Priority,
                isMatched,
                explanations.Count == 0 ? ["Rule matched source event, effective date, scope, and conditions."] : explanations,
                ruleIssues));
        }

        var matchedTopPriorityRules = matchedCandidateRules.Count == 0
            ? []
            : matchedCandidateRules
                .Where(rule => rule.Priority == matchedCandidateRules.Max(static item => item.Priority))
                .OrderBy(static rule => rule.RuleId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        if (matchedTopPriorityRules.Length > 1)
        {
            issues.Add(Issue(
                "posting-rule.priority-conflict",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Dry run matched {matchedTopPriorityRules.Length} posting rules at priority {matchedTopPriorityRules[0].Priority}: {string.Join(", ", matchedTopPriorityRules.Select(static rule => rule.RuleId))}.",
                selectedRule?.RuleId ?? request.SourceEventType,
                "Assign a distinct priority, effective-date window, or dimensional scope so dry-run rule selection is deterministic."));
            selectedRule = null;
        }

        if (candidates.Length == 0)
        {
            issues.Add(Issue("rule.none", AccountingConfigurationValidationSeverityDto.Critical, $"No active posting rule matched source event '{request.SourceEventType}' for {request.EffectiveDate:yyyy-MM-dd}.", request.SourceEventType, "Create an effective-dated posting rule for this source event."));
        }
        else if (selectedRule is null && matchedCandidateRules.Count == 0)
        {
            issues.Add(Issue(
                "rule.no-candidate-match",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"No effective posting rule matched source event '{request.SourceEventType}' after evaluating dimensional scope and rule conditions.",
                request.SourceEventType,
                "Review the dry-run match explanations, event predicates, amount thresholds, and dimensional scope before promoting or posting this rule set."));
        }

        IReadOnlyList<GeneratedPostingLineDto> generatedPostingLines = selectedRule is null
            ? []
            : BuildGeneratedPostingLines(selectedRule, templateById.GetValueOrDefault(selectedRule.TemplateId), request);
        if (selectedRule is not null)
        {
            issues.AddRange(ValidatePostingRuleFormulaReferences(selectedRule));
            issues.AddRange(ValidatePostingRuleDryRunAllocationWeights(selectedRule, request.EventAmount));
        }

        var previewLines = generatedPostingLines
            .Select(line =>
            {
                chartByPath.TryGetValue(line.AccountPath, out var account);
                return new AccountingJournalPreviewLineDto(
                    line.AccountPath,
                    account?.AccountName ?? line.AccountPath,
                    line.Side,
                    line.Amount,
                    line.Currency,
                    line.Description);
            })
            .ToArray();

        foreach (var line in generatedPostingLines)
        {
            if (!chartByPath.TryGetValue(line.AccountPath, out var account))
            {
                issues.Add(Issue("rule.generated-account-missing", AccountingConfigurationValidationSeverityDto.Critical, $"Generated posting line '{line.LineId}' references missing account path '{line.AccountPath}'.", line.LineId, "Map generated postings to active chart accounts."));
            }
            else if (account.IsArchived)
            {
                issues.Add(Issue("rule.generated-account-archived", AccountingConfigurationValidationSeverityDto.Critical, $"Generated posting line '{line.LineId}' references archived account path '{line.AccountPath}'.", line.LineId, "Map generated postings to active chart accounts."));
            }
        }

        var totalDebits = generatedPostingLines.Where(static line => line.Side == AccountingTemplateLineSideDto.Debit).Sum(static line => line.Amount);
        var totalCredits = generatedPostingLines.Where(static line => line.Side == AccountingTemplateLineSideDto.Credit).Sum(static line => line.Amount);
        var isBalanced = generatedPostingLines.Count > 0 && totalDebits == totalCredits;
        if (selectedRule is not null && !isBalanced)
        {
            issues.Add(Issue("rule.generated-unbalanced", AccountingConfigurationValidationSeverityDto.Critical, $"Posting rule '{selectedRule.RuleId}' generated unbalanced lines: debits={totalDebits}, credits={totalCredits}.", selectedRule.RuleId, "Adjust formulas, allocations, or generated posting lines so debits equal credits."));
        }

        issues.AddRange(matches.SelectMany(static match => match.ValidationIssues));
        return new RuleDryRunResultDto(
            workspace.FundProfileId,
            request.LedgerBookId ?? workspace.LedgerBookId,
            request.SourceEventType,
            request.EffectiveDate,
            request.EventAmount,
            string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency.Trim().ToUpperInvariant(),
            isBalanced && issues.All(static issue => issue.Severity != AccountingConfigurationValidationSeverityDto.Critical),
            selectedRule?.RuleId,
            matches,
            previewLines,
            issues.OrderByDescending(static issue => issue.Severity).ThenBy(static issue => issue.Code, StringComparer.OrdinalIgnoreCase).ToArray(),
            generatedPostingLines);
    }

    public async Task<AccountingRuleTestSuiteResultDto> ExecuteRuleTestCasesAsync(
        ExecuteAccountingRuleTestCasesRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var fundProfileId = NormalizeFundProfileId(request.FundProfileId);
        var actor = RequireText(request.Actor, nameof(request.Actor));
        var workspace = request.TestCases.Count == 0
            ? await LoadWorkspaceAsync(fundProfileId, request.LedgerBookId, ct, request.TenantId, request.CompanyId).ConfigureAwait(false)
            : null;
        var testCases = request.TestCases.Count == 0
            ? workspace?.RuleTestCases ?? []
            : request.TestCases;
        var results = new List<AccountingRuleTestCaseResultDto>(testCases.Count);

        foreach (var testCase in testCases)
        {
            ct.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(testCase);

            var dryRunRequest = testCase.Request with
            {
                FundProfileId = fundProfileId,
                LedgerBookId = testCase.Request.LedgerBookId ?? request.LedgerBookId,
                Actor = actor,
                CorrelationId = string.IsNullOrWhiteSpace(testCase.Request.CorrelationId)
                    ? request.CorrelationId
                    : testCase.Request.CorrelationId,
                // SEC-005 slice 4: the server-resolved tenant on the parent request is authoritative; a
                // body-supplied per-test-case tenant/company must not override it.
                TenantId = request.TenantId,
                CompanyId = request.CompanyId
            };
            var dryRun = await DryRunPostingRuleAsync(dryRunRequest, ct).ConfigureAwait(false);
            var assertionIssues = EvaluateRuleTestCaseAssertions(testCase, dryRun);

            results.Add(new AccountingRuleTestCaseResultDto(
                RequireText(testCase.TestCaseId, nameof(testCase.TestCaseId)),
                RequireText(testCase.DisplayName, nameof(testCase.DisplayName)),
                assertionIssues.Count == 0,
                dryRun,
                assertionIssues));
        }

        var passedCount = results.Count(static item => item.Passed);
        return new AccountingRuleTestSuiteResultDto(
            fundProfileId,
            request.LedgerBookId ?? workspace?.LedgerBookId,
            DateTimeOffset.UtcNow,
            actor,
            results.Count,
            passedCount,
            results.Count - passedCount,
            results);
    }

    public async Task<AccountingConfigurationWorkspaceDto> ActivateAsync(
        ActivateAccountingConfigurationRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await LoadWorkspaceAsync(NormalizeFundProfileId(request.FundProfileId), request.LedgerBookId, ct, request.TenantId, request.CompanyId).ConfigureAwait(false);
        var beforeHash = Hash(workspace);
        EnsureRuleStudioHumanOrigin(request.ActionOrigin, "activate accounting configurations");
        var issues = await ValidateActivationReadinessAsync(workspace, request, ct).ConfigureAwait(false);
        if (issues.Any(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
        {
            throw new InvalidOperationException("Accounting configuration cannot be activated while critical validation issues remain.");
        }

        workspace = workspace with
        {
            LedgerBookId = request.LedgerBookId ?? workspace.LedgerBookId,
            Status = AccountingConfigurationStatusDto.Active,
            ConfigurationVersion = $"v{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            ValidationIssues = issues,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        return await SaveWithAuditAsync(workspace, beforeHash, request.Actor, "configuration.activate", request.LedgerBookId, request.CorrelationId, request.EvidenceLinks, request.TenantId, request.CompanyId, request.ReportGroupPrincipalIds, ct, issues).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAuditAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
        => _auditStore.ListAsync(NormalizeFundProfileId(fundProfileId), ledgerBookId, ct, tenantId, companyId);

    private async Task<AccountingConfigurationWorkspaceDto> SaveWithAuditAsync(
        AccountingConfigurationWorkspaceDto workspace,
        string beforeHash,
        string actor,
        string action,
        Guid? ledgerBookId,
        string? correlationId,
        IReadOnlyList<string>? evidenceLinks,
        string? tenantId,
        string? companyId,
        IReadOnlyList<string>? reportGroupPrincipalIds,
        CancellationToken ct,
        IReadOnlyList<AccountingConfigurationValidationIssueDto>? validationOverride = null)
    {
        var validation = validationOverride ?? Validate(workspace);
        var finalWorkspace = workspace with
        {
            TenantId = NormalizeOptional(tenantId) ?? workspace.TenantId,
            CompanyId = NormalizeOptional(companyId) ?? workspace.CompanyId,
            ValidationIssues = validation,
            RulesStudio = BuildRulesStudio(workspace, validation)
        };
        var afterHash = Hash(finalWorkspace);
        await _store.SaveAsync(finalWorkspace, ct).ConfigureAwait(false);
        await _auditStore.AppendAsync(
            new AccountingActionAuditEventDto(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                RequireText(actor, nameof(actor)),
                action,
                finalWorkspace.FundProfileId,
                ledgerBookId ?? finalWorkspace.LedgerBookId,
                NormalizeOptional(correlationId),
                beforeHash,
                afterHash,
                validation,
                evidenceLinks ?? [],
                NormalizeOptional(companyId),
                NormalizePrincipalIds(reportGroupPrincipalIds),
                NormalizeOptional(tenantId)),
            ct).ConfigureAwait(false);

        return await GetWorkspaceAsync(finalWorkspace.FundProfileId, ledgerBookId ?? finalWorkspace.LedgerBookId, ct, finalWorkspace.TenantId, finalWorkspace.CompanyId).ConfigureAwait(false);
    }

    private async Task<AccountingConfigurationWorkspaceDto> LoadWorkspaceAsync(
        string fundProfileId,
        Guid? ledgerBookId,
        CancellationToken ct,
        string? tenantId = null,
        string? companyId = null)
    {
        var workspace = await _store.GetAsync(fundProfileId, ledgerBookId, ct, tenantId, companyId).ConfigureAwait(false);
        if (workspace is not null)
        {
            return workspace with
            {
                LedgerBookId = ledgerBookId ?? workspace.LedgerBookId,
                TenantId = NormalizeOptional(tenantId) ?? workspace.TenantId,
                CompanyId = NormalizeOptional(companyId) ?? workspace.CompanyId
            };
        }

        if (ledgerBookId.HasValue && _ledgerBookService is null)
        {
            var fundWorkspace = await _store.GetAsync(fundProfileId, ledgerBookId: null, ct, tenantId, companyId).ConfigureAwait(false);
            if (fundWorkspace is not null)
            {
                return fundWorkspace with
                {
                    LedgerBookId = ledgerBookId,
                    TenantId = NormalizeOptional(tenantId) ?? fundWorkspace.TenantId,
                    CompanyId = NormalizeOptional(companyId) ?? fundWorkspace.CompanyId
                };
            }
        }

        return new AccountingConfigurationWorkspaceDto(
            fundProfileId,
            ledgerBookId,
            AccountingConfigurationStatusDto.Draft,
            "draft",
            DateTimeOffset.UtcNow,
            LedgerBooks: [],
            ChartOfAccounts: [],
            JournalTemplates: [],
            PostingRules: [],
            ValidationIssues: [],
            AuditTrail: [],
            RuleTestCases: [],
            TenantId: NormalizeOptional(tenantId),
            CompanyId: NormalizeOptional(companyId));
    }

    private async Task<IReadOnlyList<LedgerBookDto>> LoadLedgerBooksAsync(string fundProfileId, Guid? ledgerBookId, CancellationToken ct)
    {
        if (_ledgerBookService is null)
        {
            return [];
        }

        var books = await _ledgerBookService
            .ListBooksAsync(new LedgerBookQuery(fundProfileId, FundStructureNodeId: null), ct)
            .ConfigureAwait(false);

        if (!ledgerBookId.HasValue)
        {
            return books;
        }

        var scopedBooks = books.Where(book => book.LedgerBookId == ledgerBookId.Value).ToArray();
        return scopedBooks.Length > 0 ? scopedBooks : books;
    }
}
