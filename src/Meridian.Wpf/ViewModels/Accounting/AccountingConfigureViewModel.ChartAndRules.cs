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
    public async Task SaveChartAccountAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            ChartAccountSetupStatusText = "Select a fund-linked context before saving chart accounts.";
            StatusText = ChartAccountSetupStatusText;
            return;
        }

        var nodeId = ChartAccountNodeId.Trim();
        var path = ChartAccountPath.Trim();
        var accountName = ChartAccountName.Trim();
        var accountType = ChartAccountType.Trim();
        var evidence = NormalizeTenantAdministrationEvidence(ChartAccountEvidenceText);
        if (string.IsNullOrWhiteSpace(nodeId) ||
            string.IsNullOrWhiteSpace(path) ||
            string.IsNullOrWhiteSpace(accountName) ||
            string.IsNullOrWhiteSpace(accountType) ||
            evidence.Count == 0)
        {
            ChartAccountSetupStatusText = "Node id, account path, account name, account type, and retained evidence are required before saving chart accounts.";
            StatusText = ChartAccountSetupStatusText;
            NotifyChartAccountSaveStateChanged();
            return;
        }

        try
        {
            _configuration ??= await _configurationService.GetWorkspaceAsync(_activeFundProfile.FundProfileId, ct: ct).ConfigureAwait(false);
            var ledgerBookId = ResolveActiveLedgerBook(_configuration)?.LedgerBookId ?? _configuration.LedgerBookId;
            var node = new ChartOfAccountsNodeDto(
                nodeId,
                path,
                accountName,
                accountType,
                string.IsNullOrWhiteSpace(ChartAccountParentPath) ? null : ChartAccountParentPath.Trim(),
                FinancialAccountId: string.IsNullOrWhiteSpace(ChartAccountFinancialAccountId) ? null : ChartAccountFinancialAccountId.Trim());
            _configuration = await _configurationService.UpsertChartNodeAsync(
                new UpsertChartOfAccountsNodeRequest(
                    _activeFundProfile.FundProfileId,
                    node,
                    ResolveActor(),
                    CorrelationId: $"wpf-accounting-chart-account-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    EvidenceLinks: evidence,
                    LedgerBookId: ledgerBookId),
                ct).ConfigureAwait(false);

            ApplyConfiguration(_configuration);
            ApplyNextChartAccountDraft(_configuration);
            ChartAccountSetupStatusText = $"Chart account {path} saved through shared Accounting Configure.";
            StatusText = ChartAccountSetupStatusText;
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ChartAccountSetupStatusText = $"Chart account setup failed: {ex.Message}";
            StatusText = ChartAccountSetupStatusText;
        }
    }

    public async Task SavePostingRuleDraftAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            PostingRuleDraftStatusText = "Select a fund-linked context before saving posting-rule drafts.";
            StatusText = PostingRuleDraftStatusText;
            return;
        }

        var ruleId = PostingRuleDraftRuleId.Trim();
        var displayName = PostingRuleDraftDisplayName.Trim();
        var sourceEventType = PostingRuleDraftSourceEventType.Trim();
        var templateId = PostingRuleDraftTemplateId.Trim();
        var evidence = NormalizeTenantAdministrationEvidence(PostingRuleDraftEvidenceText);
        if (string.IsNullOrWhiteSpace(ruleId) ||
            string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrWhiteSpace(sourceEventType) ||
            string.IsNullOrWhiteSpace(templateId) ||
            evidence.Count == 0)
        {
            PostingRuleDraftStatusText = "Rule id, display name, source event, template id, and retained evidence are required before saving posting-rule drafts.";
            StatusText = PostingRuleDraftStatusText;
            NotifyPostingRuleDraftSaveStateChanged();
            return;
        }

        try
        {
            _configuration ??= await _configurationService.GetWorkspaceAsync(_activeFundProfile.FundProfileId, ct: ct).ConfigureAwait(false);
            var ledgerBookId = ResolveActiveLedgerBook(_configuration)?.LedgerBookId ?? _configuration.LedgerBookId;
            if (!_configuration.JournalTemplates.Any(template =>
                    string.Equals(template.TemplateId, templateId, StringComparison.OrdinalIgnoreCase) &&
                    !template.IsArchived))
            {
                PostingRuleDraftStatusText = $"Posting-rule draft blocked: template {templateId} is not available in the loaded Accounting Configure workspace.";
                StatusText = PostingRuleDraftStatusText;
                return;
            }

            var rule = new PostingRuleDto(
                ruleId,
                displayName,
                sourceEventType,
                templateId,
                RuleVersion: "draft",
                Description: string.IsNullOrWhiteSpace(PostingRuleDraftDescription)
                    ? null
                    : PostingRuleDraftDescription.Trim(),
                Priority: PostingRuleDraftPriority,
                Scope: new LedgerDimensionSetDto(
                    FundId: _activeFundProfile.FundProfileId,
                    BookId: ledgerBookId?.ToString("D", CultureInfo.InvariantCulture)),
                RequiresPromotionApproval: true);

            _configuration = await _configurationService.UpsertPostingRuleAsync(
                new UpsertPostingRuleRequest(
                    _activeFundProfile.FundProfileId,
                    rule,
                    ResolveActor(),
                    CorrelationId: $"wpf-accounting-posting-rule-draft-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    EvidenceLinks: evidence,
                    LedgerBookId: ledgerBookId),
                ct).ConfigureAwait(false);

            ApplyConfiguration(_configuration);
            ApplyNextPostingRuleDraft(_configuration, ruleId);
            PostingRuleDraftStatusText = $"Posting-rule draft {ruleId} saved through shared Accounting Configure.";
            StatusText = PostingRuleDraftStatusText;
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PostingRuleDraftStatusText = $"Posting-rule draft save failed: {ex.Message}";
            StatusText = PostingRuleDraftStatusText;
        }
    }

    public async Task CreateFundScopedPolicyAsync(CancellationToken ct = default)
    {
        if (_accountingPolicyService is null)
        {
            PolicyPostureText = "Accounting policy service is not registered for this desktop session.";
            return;
        }

        if (_activeFundProfile is null)
        {
            PolicyPostureText = "Select a fund-linked context before creating a fund-scoped accounting policy.";
            return;
        }

        try
        {
            var policyId = $"{_activeFundProfile.FundProfileId}-{SelectedAccountingBasis.ToString().ToLowerInvariant()}-desktop-v1";
            await _accountingPolicyService.CreatePolicyAsync(
                new CreateAccountingPolicyRequest(
                    SelectedAccountingBasis,
                    policyId,
                    "v1",
                    $"{_activeFundProfile.DisplayName} {SelectedAccountingBasis} desktop policy",
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    IsDefault: true,
                    RulesJson: "{\"source\":\"wpf-accounting-configure\"}",
                    FundProfileId: _activeFundProfile.FundProfileId),
                ct).ConfigureAwait(false);
            await LoadPolicyRowsAsync(ct).ConfigureAwait(false);
            PolicyPostureText = $"Fund-scoped {SelectedAccountingBasis} policy is available for {_activeFundProfile.DisplayName}.";
        }
        catch (Exception ex)
        {
            PolicyPostureText = $"Fund-scoped accounting policy could not be created: {ex.Message}";
        }
    }

    public async Task ExecuteRulesStudioTestsAsync(CancellationToken ct = default)
    {
        RulesStudioActionRows.Clear();
        if (_activeFundProfile is null || _configuration is null)
        {
            RulesStudioActionText = "Load a fund-linked accounting configuration before executing rule tests.";
            return;
        }

        try
        {
            var result = await _configurationService.ExecuteRuleTestCasesAsync(
                new ExecuteAccountingRuleTestCasesRequestDto(
                    _activeFundProfile.FundProfileId,
                    ResolveActor(),
                    TestCases: [],
                    LedgerBookId: _configuration.LedgerBookId),
                ct).ConfigureAwait(false);

            ApplyRulesStudioTestResult(result);
            RulesStudioActionText =
                $"Executed {result.Results.Count} saved rule test case(s): {result.PassedCount} passed, {result.FailedCount} failed.";
        }
        catch (Exception ex)
        {
            RulesStudioActionText = $"Rules Studio test execution is blocked: {ex.Message}";
            RulesStudioActionRows.ReplaceWith(
            [
                new AccountingWorkbenchRow(
                    "Run saved tests",
                    "Blocked",
                    ex.Message,
                    "No rule state was changed.",
                    _configuration.LedgerBookId?.ToString("D", CultureInfo.InvariantCulture) ?? "fund")
            ]);
        }
    }

    public async Task ApproveRulesStudioPromotionAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null || _configuration?.RulesStudio is null)
        {
            RulesStudioActionText = "Load a fund-linked Rules Studio workspace before approving promotion.";
            return;
        }

        var queueItem = _configuration.RulesStudio.PromotionQueue
            .OrderBy(static item => item.CriticalIssueCount)
            .ThenBy(static item => item.MissingRegressionEvidenceCount)
            .ThenBy(static item => item.RuleId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (queueItem is null)
        {
            RulesStudioActionText = "No posting-rule promotions are waiting for approval.";
            RulesStudioActionRows.ReplaceWith(
            [
                new AccountingWorkbenchRow(
                    "Promotion approval",
                    "No queue",
                    "The shared Rules Studio promotion queue is empty.",
                    "Seed or edit a promotion-gated rule before approval.",
                    string.Empty)
            ]);
            return;
        }

        try
        {
            var approvalId = string.IsNullOrWhiteSpace(queueItem.ApprovalId)
                ? $"wpf-approval-{queueItem.RuleId}-{queueItem.RuleVersion}"
                : queueItem.ApprovalId;
            var evidenceLink = $"evidence://accounting/rules/{queueItem.RuleId}/{queueItem.RuleVersion}/desktop-approval/{approvalId}";
            _configuration = await _configurationService.ApprovePostingRulePromotionAsync(
                new ApprovePostingRulePromotionRequest(
                    _activeFundProfile.FundProfileId,
                    queueItem.RuleId,
                    queueItem.RuleVersion,
                    ResolveActor(),
                    approvalId,
                    "Desktop controller approved the posting rule after Rules Studio regression review.",
                    EvidenceLinks: [evidenceLink],
                    RequestedBy: queueItem.RequestedBy,
                    RequestedAtUtc: queueItem.RequestedAtUtc,
                    CorrelationId: "wpf-rules-studio-promotion-approve",
                    LedgerBookId: _configuration.LedgerBookId),
                ct).ConfigureAwait(false);

            ApplyConfiguration(_configuration);
            RulesStudioActionText = $"Approved posting-rule promotion {queueItem.RuleId}/{queueItem.RuleVersion}.";
            RulesStudioActionRows.ReplaceWith(
            [
                new AccountingWorkbenchRow(
                    queueItem.RuleId,
                    "Approved",
                    $"{queueItem.DisplayName} | {queueItem.RuleVersion}",
                    evidenceLink,
                    approvalId)
            ]);
        }
        catch (Exception ex)
        {
            RulesStudioActionText = $"Posting-rule promotion approval is blocked: {ex.Message}";
            RulesStudioActionRows.ReplaceWith(
            [
                new AccountingWorkbenchRow(
                    queueItem.RuleId,
                    "Blocked",
                    ex.Message,
                    queueItem.SuggestedAction,
                    queueItem.ApprovalId ?? string.Empty)
            ]);
        }
    }

    private void ApplyRulesStudioTestResult(AccountingRuleTestSuiteResultDto result)
    {
        var rows = new List<AccountingWorkbenchRow>
        {
            new(
                "Regression suite",
                $"{result.PassedCount}/{result.TotalCount} passed",
                $"Executed by {result.Actor} at {result.ExecutedAtUtc:yyyy-MM-dd HH:mm:ss} UTC.",
                result.FailedCount == 0
                    ? "All saved rule tests passed."
                    : $"{result.FailedCount} saved rule test(s) failed and block promotion or activation.",
                result.LedgerBookId?.ToString("D", CultureInfo.InvariantCulture) ?? "fund")
        };

        rows.AddRange(result.Results.Select(testCase =>
        {
            var issueText = testCase.AssertionIssues.Count == 0
                ? "No assertion issues."
                : string.Join("; ", testCase.AssertionIssues.Select(static issue => issue.Code).Take(4));
            var selectedMatch = testCase.DryRunResult.RuleMatches.FirstOrDefault(match =>
                string.Equals(match.RuleId, testCase.DryRunResult.SelectedRuleId, StringComparison.OrdinalIgnoreCase));
            var selectedRule = testCase.DryRunResult.SelectedRuleId is null
                ? "No selected rule"
                : $"{testCase.DryRunResult.SelectedRuleId}/{selectedMatch?.RuleVersion ?? "unknown"}";
            return new AccountingWorkbenchRow(
                testCase.TestCaseId,
                testCase.Passed ? "Passed" : "Failed",
                $"{testCase.DisplayName} | {selectedRule}",
                issueText,
                $"{testCase.DryRunResult.FundProfileId}:{testCase.DryRunResult.SourceEventType}");
        }));

        RulesStudioActionRows.ReplaceWith(rows);
    }

    public async Task BuildPostingCandidateAsync(CancellationToken ct = default)
    {
        PostingCandidateRows.Clear();
        if (_accountingPostingCandidateService is null)
        {
            PostingCandidateStatusText = "Posting-rule candidate service is not registered for this desktop session.";
            PostingCandidateDetailText = "The desktop surface cannot build governed draft candidates until the shared service is registered.";
            return;
        }

        if (_activeFundProfile is null || _configuration is null)
        {
            PostingCandidateStatusText = "Select and load a fund-linked accounting configuration before building a posting candidate.";
            PostingCandidateDetailText = "Candidate generation requires chart accounts, posting rules, dimensions, and retained evidence.";
            return;
        }

        var rule = ResolvePostingCandidateRule(_configuration);
        if (rule is null)
        {
            PostingCandidateStatusText = "No active posting rule is available for the selected manual journal entry type.";
            PostingCandidateDetailText = "Seed or configure posting rules before building a governed draft candidate.";
            return;
        }

        var ledgerBook = ResolveActiveLedgerBook(_configuration);
        var currency = string.IsNullOrWhiteSpace(DraftCurrency)
            ? _activeFundProfile.BaseCurrency
            : DraftCurrency.Trim().ToUpperInvariant();
        var effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var correlationId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var evidenceLinks = BuildPostingCandidateEvidenceLinks(rule);

        try
        {
            var result = await _accountingPostingCandidateService.BuildCandidateAsync(
                    new PostingRuleJournalCandidateRequestDto(
                        _activeFundProfile.FundProfileId,
                        rule.SourceEventType,
                        Math.Abs(DraftAmount),
                        currency,
                        effectiveDate,
                        ResolveActor(),
                        AggregateId: Guid.NewGuid(),
                        PeriodId: Guid.NewGuid(),
                        AccountingTimestamp: DateTimeOffset.UtcNow,
                        Description: $"{rule.DisplayName} governed journal draft candidate",
                        AccountingBasis: ledgerBook?.AccountingBasis ?? SelectedAccountingBasis,
                        LedgerBookId: _configuration.LedgerBookId ?? ledgerBook?.LedgerBookId,
                        Dimensions: BuildPostingCandidateDimensions(rule),
                        CounterpartyId: rule.Scope?.CounterpartyId,
                        InstrumentSymbol: rule.Scope?.InstrumentId?.ToString("D"),
                        CorrelationId: correlationId,
                        SourceEventId: sourceEventId,
                        PolicyId: ledgerBook?.AccountingPolicyId,
                        TreatmentKind: MapPostingCandidateTreatment(SelectedEntryType),
                        PostingKind: LedgerPostingKindDto.Originating,
                        TreasuryContext: BuildManualJournalTreasuryContext(
                            _activeFundProfile.FundProfileId,
                            SelectedEntryType,
                            effectiveDate,
                            sourceEventId),
                        EvidenceLinks: evidenceLinks),
                    ct)
                .ConfigureAwait(false);

            ApplyPostingCandidateResult(rule, result, correlationId, sourceEventId);
        }
        catch (Exception ex)
        {
            PostingCandidateStatusText = $"Posting-rule candidate generation failed: {ex.Message}";
            PostingCandidateDetailText = "No journal draft candidate was created; ledger posting remains unavailable.";
        }
    }

    private void ApplyNextChartAccountDraft(AccountingConfigurationWorkspaceDto workspace)
    {
        var nextIndex = workspace.ChartOfAccounts.Count(node => !node.IsArchived) + 1;
        ChartAccountNodeId = $"wpf-chart-account-{nextIndex.ToString(CultureInfo.InvariantCulture)}";
        ChartAccountPath = $"Assets:Configured Account {nextIndex.ToString(CultureInfo.InvariantCulture)}";
        ChartAccountName = $"Configured Account {nextIndex.ToString(CultureInfo.InvariantCulture)}";
        ChartAccountType = "Asset";
        ChartAccountParentPath = "Assets";
        ChartAccountFinancialAccountId = string.Empty;
        ChartAccountEvidenceText = $"wpf://accounting/configure/chart-account/{nextIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    private void ApplyNextPostingRuleDraft(AccountingConfigurationWorkspaceDto workspace, string previousRuleId)
    {
        var nextIndex = workspace.PostingRules.Count(rule => !rule.IsArchived) + 1;
        PostingRuleDraftRuleId = $"wpf-configured-posting-rule-{nextIndex.ToString(CultureInfo.InvariantCulture)}";
        PostingRuleDraftDisplayName = $"WPF configured posting rule {nextIndex.ToString(CultureInfo.InvariantCulture)}";
        PostingRuleDraftSourceEventType = "ManualJournalEntry.Configured";
        PostingRuleDraftTemplateId = workspace.JournalTemplates.FirstOrDefault(template =>
                !template.IsArchived &&
                string.Equals(template.TemplateId, PostingRuleDraftTemplateId, StringComparison.OrdinalIgnoreCase))
            ?.TemplateId
            ?? workspace.JournalTemplates.FirstOrDefault(template => !template.IsArchived)?.TemplateId
            ?? "desktop-manual-adjustment-v1";
        PostingRuleDraftDescription = $"Drafted after {previousRuleId} from WPF Accounting Configure.";
        PostingRuleDraftPriority = nextIndex * 10;
        PostingRuleDraftEvidenceText = $"evidence://wpf/accounting/rules/configured-posting-rule-{nextIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    private void ApplyRulesStudio(AccountingRulesStudioDto? rulesStudio)
    {
        if (rulesStudio is null)
        {
            RulesStudioStatusText = "Rules Studio unavailable";
            RulesStudioDetailText = "The shared accounting configuration workspace did not include a Rules Studio projection.";
            RulesStudioRuleRows.Clear();
            RulesStudioPromotionRows.Clear();
            RulesStudioActionRows.Clear();
            return;
        }

        var summary = rulesStudio.Summary;
        RulesStudioStatusText =
            $"{summary.ActiveRules}/{summary.TotalRules} active rule(s), {summary.GeneratedPostingRules} generated posting rule(s), {summary.SavedTestCaseCount} saved test case(s).";
        RulesStudioDetailText =
            $"{summary.EffectiveDatedRules} effective-dated; {summary.PendingPromotionApprovalRules} pending promotion approval; {summary.RulesMissingCurrentVersionRegressionTests} missing current-version regression tests; {summary.CriticalIssueCount} critical issue(s).";

        RulesStudioRuleRows.ReplaceWith(rulesStudio.Rules.Select(rule =>
            new AccountingWorkbenchRow(
                rule.RuleId,
                rule.CanActivate ? "Activation-ready" : rule.CanRequestPromotion ? "Promotion-ready" : "Blocked",
                $"{rule.SourceEventType} | {rule.RuleVersion} | priority {rule.Priority} | {AccountingWorkbenchPresentation.FormatEffectiveWindow(rule.EffectiveFrom, rule.EffectiveTo)}",
                $"{rule.ConditionCount + rule.ConditionGroupCount} condition set(s); {rule.FormulaCount} formula(s); {rule.AllocationCount} allocation(s); {rule.GeneratedPostingLineCount} generated line(s); {rule.SavedTestCaseCount} test(s)",
                rule.PromotionApprovalId ?? string.Empty)));

        RulesStudioPromotionRows.ReplaceWith(rulesStudio.PromotionQueue.Select(item =>
            new AccountingWorkbenchRow(
                item.RuleId,
                item.ApprovalState?.ToString() ?? "Not requested",
                $"{item.DisplayName} | {item.RuleVersion} | requested by {item.RequestedBy}",
                $"{item.RegressionTestCaseCount} regression test(s); {item.MissingRegressionEvidenceCount} missing evidence; {item.CriticalIssueCount} critical issue(s); {item.SuggestedAction}",
                item.ApprovalId ?? string.Empty)));
    }

    private void NotifyChartAccountSaveStateChanged()
    {
        RaisePropertyChanged(nameof(CanSaveChartAccount));
        SaveChartAccountCommand.NotifyCanExecuteChanged();
    }

    private void NotifyPostingRuleDraftSaveStateChanged()
    {
        RaisePropertyChanged(nameof(CanSavePostingRuleDraft));
        SavePostingRuleDraftCommand.NotifyCanExecuteChanged();
    }

    private PostingRuleDto? ResolvePostingCandidateRule(AccountingConfigurationWorkspaceDto workspace)
    {
        var sourceEventType = $"ManualJournalEntry.{SelectedEntryType}";
        return workspace.PostingRules
                   .Where(static rule => !rule.IsArchived)
                   .Where(rule => string.Equals(rule.SourceEventType, sourceEventType, StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(static rule => rule.Priority)
                   .ThenBy(static rule => rule.RuleId, StringComparer.OrdinalIgnoreCase)
                   .FirstOrDefault()
               ?? workspace.PostingRules
                   .Where(static rule => !rule.IsArchived)
                   .OrderByDescending(static rule => rule.Priority)
                   .ThenBy(static rule => rule.RuleId, StringComparer.OrdinalIgnoreCase)
                   .FirstOrDefault();
    }

    private LedgerBookDto? ResolveActiveLedgerBook(AccountingConfigurationWorkspaceDto workspace)
        => workspace.LedgerBooks.FirstOrDefault(book => book.LedgerBookId == workspace.LedgerBookId)
           ?? workspace.LedgerBooks.FirstOrDefault();

    private LedgerDimensionSetDto BuildPostingCandidateDimensions(PostingRuleDto rule)
        => rule.Scope is { } scope
            ? scope with
            {
                FundId = string.IsNullOrWhiteSpace(scope.FundId) ? _activeFundProfile?.FundProfileId : scope.FundId,
                EntityId = string.IsNullOrWhiteSpace(scope.EntityId) ? AccountingWorkbenchPresentation.FirstDimensionValue(_activeFundProfile?.EntityIds) : scope.EntityId
            }
            : new LedgerDimensionSetDto(
                FundId: _activeFundProfile?.FundProfileId,
                EntityId: AccountingWorkbenchPresentation.FirstDimensionValue(_activeFundProfile?.EntityIds),
                SleeveId: AccountingWorkbenchPresentation.FirstDimensionValue(_activeFundProfile?.SleeveIds),
                InvestorId: RequiresPrivateCapitalTreasuryContext(SelectedEntryType)
                    ? $"investor:{_activeFundProfile?.FundProfileId}:default"
                    : null,
                CapitalAccountId: RequiresPrivateCapitalTreasuryContext(SelectedEntryType)
                    ? $"capital-account:{_activeFundProfile?.FundProfileId}:default"
                    : null,
                CounterpartyId: rule.Scope?.CounterpartyId);

    private IReadOnlyList<string> BuildPostingCandidateEvidenceLinks(PostingRuleDto rule)
    {
        var links = new List<string>();
        links.AddRange(NormalizeEvidenceLink(DraftEvidenceLink));
        links.Add($"accounting-rule://{rule.RuleId}/{rule.RuleVersion}");
        links.Add($"desktop://accounting/configure/posting-candidate/{rule.RuleId}");
        return links
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void ApplyPostingCandidateResult(
        PostingRuleDto rule,
        PostingRuleJournalCandidateResultDto result,
        Guid correlationId,
        Guid sourceEventId)
    {
        var rows = new List<AccountingWorkbenchRow>
        {
            new(
                result.SelectedRuleId ?? rule.RuleId,
                result.SelectedRuleVersion ?? rule.RuleVersion,
                $"{result.DryRunResult.SourceEventType} | priority match | correlation {correlationId:D}",
                $"{result.DryRunResult.RuleMatches.Count} dry-run match(es); source event {sourceEventId:D}",
                result.SelectedRuleId ?? rule.RuleId),
            new(
                "Draft command",
                result.PostingCommand?.ApprovalState.ToString() ?? "Blocked",
                result.JournalEntryId is null
                    ? "No journal draft candidate was created."
                    : $"Journal draft {result.JournalEntryId:D}; debits {result.TotalDebits:0.##}; credits {result.TotalCredits:0.##}; imbalance {result.Imbalance:0.##}",
                $"Submit for approval: {result.CanSubmitForApproval}; auto-post: {result.CanPostWithoutAdditionalApproval}; posting remains JE lifecycle-gated.",
                result.PostingCommand?.CommandId.ToString("D") ?? string.Empty),
            new(
                "Evidence",
                $"{result.EvidenceLinks.Count} retained link(s)",
                string.Join("; ", result.EvidenceLinks.Take(4)),
                result.EvidenceLinks.Count > 4 ? $"{result.EvidenceLinks.Count - 4} additional link(s)" : "All links shown",
                string.Empty)
        };

        rows.AddRange(result.GeneratedPostingLines.Select(line =>
            new AccountingWorkbenchRow(
                line.AccountPath,
                line.Side.ToString(),
                $"{line.Amount:0.##} {line.Currency} | dimensions {FormatLedgerDimensions(line.Dimensions)}",
                line.Description ?? line.AmountFormulaId,
                line.LineId)));

        rows.AddRange(result.Issues.Select(issue =>
            new AccountingWorkbenchRow(
                issue.Code,
                issue.BlocksCandidate ? "Blocking" : issue.Severity.ToString(),
                issue.Message,
                issue.SuggestedAction ?? string.Empty,
                issue.TargetId ?? string.Empty)));

        PostingCandidateRows.ReplaceWith(rows);
        PostingCandidateStatusText = result.HasBlockingIssues
            ? $"Posting candidate is blocked by {result.Issues.Count(issue => issue.BlocksCandidate)} issue(s); no posting command is executable."
            : $"Posting candidate built for {result.SelectedRuleId ?? rule.RuleId}; draft is {(result.CanSubmitForApproval ? "approval-ready" : "validation-gated")} and not posted.";
        PostingCandidateDetailText =
            $"{result.GeneratedPostingLines.Count} generated line(s), debits {result.TotalDebits:0.##}, credits {result.TotalCredits:0.##}, {result.EvidenceLinks.Count} evidence link(s); posting remains governed by JE lifecycle.";
    }
}
