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
    public async Task SeedBaselineConfigurationAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            StatusText = "Select a fund-linked context before seeding accounting configuration.";
            return;
        }

        var fundProfileId = _activeFundProfile.FundProfileId;
        var evidence = new[] { $"accounting-config://{fundProfileId}/desktop-baseline" };
        try
        {
            _configuration ??= await _configurationService.GetWorkspaceAsync(fundProfileId, ct: ct).ConfigureAwait(false);
            var ledgerBookId = ResolveActiveLedgerBook(_configuration)?.LedgerBookId;
            await _configurationService.UpsertChartNodeAsync(
                new UpsertChartOfAccountsNodeRequest(
                    fundProfileId,
                    new ChartOfAccountsNodeDto("assets-cash", "Assets:Cash", "Cash", "Asset"),
                    ResolveActor(),
                    CorrelationId: "wpf-accounting-config-seed",
                    EvidenceLinks: evidence,
                    LedgerBookId: ledgerBookId),
                ct).ConfigureAwait(false);
            await _configurationService.UpsertChartNodeAsync(
                new UpsertChartOfAccountsNodeRequest(
                    fundProfileId,
                    new ChartOfAccountsNodeDto("income-investment", "Income:Investment Income", "Investment Income", "Revenue"),
                    ResolveActor(),
                    CorrelationId: "wpf-accounting-config-seed",
                    EvidenceLinks: evidence,
                    LedgerBookId: ledgerBookId),
                ct).ConfigureAwait(false);
            await _configurationService.UpsertChartNodeAsync(
                new UpsertChartOfAccountsNodeRequest(
                    fundProfileId,
                    new ChartOfAccountsNodeDto("expenses-investment-fees", "Expenses:Investment Fees", "Investment Fees", "Expense"),
                    ResolveActor(),
                    CorrelationId: "wpf-accounting-config-seed",
                    EvidenceLinks: evidence,
                    LedgerBookId: ledgerBookId),
                ct).ConfigureAwait(false);
            foreach (var node in BuildManualJournalEntryTypeChartNodes())
            {
                await _configurationService.UpsertChartNodeAsync(
                    new UpsertChartOfAccountsNodeRequest(
                        fundProfileId,
                        node,
                        ResolveActor(),
                        CorrelationId: "wpf-accounting-config-seed",
                        EvidenceLinks: evidence,
                        LedgerBookId: ledgerBookId),
                    ct).ConfigureAwait(false);
            }

            await _configurationService.UpsertTemplateAsync(
                new UpsertJournalEntryTemplateRequest(
                    fundProfileId,
                    new JournalEntryTemplateDto(
                        "desktop-manual-adjustment-v1",
                        "Desktop manual adjustment",
                        "Balanced manual adjustment template for WPF accounting drafts.",
                        [
                            new JournalEntryTemplateLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, 1m, _activeFundProfile.BaseCurrency, "Debit configured cash account."),
                            new JournalEntryTemplateLineDto("credit-income", "Income:Investment Income", AccountingTemplateLineSideDto.Credit, 1m, _activeFundProfile.BaseCurrency, "Credit configured income account.")
                        ]),
                    ResolveActor(),
                    CorrelationId: "wpf-accounting-config-seed",
                    EvidenceLinks: evidence,
                    LedgerBookId: ledgerBookId),
                ct).ConfigureAwait(false);
            foreach (var template in BuildManualJournalEntryTypeTemplates(_activeFundProfile.BaseCurrency))
            {
                await _configurationService.UpsertTemplateAsync(
                    new UpsertJournalEntryTemplateRequest(
                        fundProfileId,
                        template,
                        ResolveActor(),
                        CorrelationId: "wpf-accounting-config-seed",
                        EvidenceLinks: evidence,
                        LedgerBookId: ledgerBookId),
                    ct).ConfigureAwait(false);
            }

            await _configurationService.UpsertPostingRuleAsync(
                new UpsertPostingRuleRequest(
                    fundProfileId,
                    new PostingRuleDto(
                        "manual-adjustment-policy-v1",
                        "Manual adjustment policy",
                        "ManualJournalEntry",
                        "desktop-manual-adjustment-v1",
                        Description: "Fund/book configurable WPF policy route for manual accounting adjustments."),
                    ResolveActor(),
                    CorrelationId: "wpf-accounting-config-seed",
                    EvidenceLinks: evidence,
                    LedgerBookId: ledgerBookId),
                ct).ConfigureAwait(false);
            foreach (var rule in BuildManualJournalEntryTypePostingRules())
            {
                await _configurationService.UpsertPostingRuleAsync(
                    new UpsertPostingRuleRequest(
                        fundProfileId,
                        rule,
                        ResolveActor(),
                        CorrelationId: "wpf-accounting-config-seed",
                        EvidenceLinks: evidence,
                        LedgerBookId: ledgerBookId),
                    ct).ConfigureAwait(false);
            }

            _configuration = await _configurationService.GetWorkspaceAsync(fundProfileId, ledgerBookId, ct).ConfigureAwait(false);
            ApplyConfiguration(_configuration);
            await LoadManualJournalPeriodAsync(ct).ConfigureAwait(false);
            await LoadManualJournalWorkbenchAsync(ct).ConfigureAwait(false);
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);
            StatusText = "Baseline chart, template, and posting policy were saved with audit evidence.";
        }
        catch (Exception ex)
        {
            StatusText = $"Baseline accounting configuration could not be saved: {ex.Message}";
        }
    }

    public async Task ActivateConfigurationAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            StatusText = "Select a fund-linked context before activating accounting configuration.";
            return;
        }

        try
        {
            _configuration = await _configurationService.ActivateAsync(
                new ActivateAccountingConfigurationRequest(
                    _activeFundProfile.FundProfileId,
                    ResolveActor(),
                    _configuration?.LedgerBookId,
                    CorrelationId: "wpf-accounting-config-activate",
                    EvidenceLinks: ["accounting-config://desktop-activation"]),
                ct).ConfigureAwait(false);
            ApplyConfiguration(_configuration);
            await LoadManualJournalPeriodAsync(ct).ConfigureAwait(false);
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);
            StatusText = "Accounting configuration is active.";
        }
        catch (Exception ex)
        {
            StatusText = $"Accounting configuration activation is blocked: {ex.Message}";
        }
    }

    public async Task CreateLedgerBookSetupAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            LedgerBookSetupStatusText = "Select a fund-linked context before creating ledger-book setup.";
            StatusText = LedgerBookSetupStatusText;
            return;
        }

        if (_ledgerBookService is null)
        {
            LedgerBookSetupStatusText = "Ledger-book service is not registered for this desktop session.";
            StatusText = LedgerBookSetupStatusText;
            return;
        }

        var candidate = _configuration?.LedgerBookSetupCandidate;
        if (candidate is null)
        {
            LedgerBookSetupStatusText = "No shared ledger-book setup candidate is available for the current configuration scope.";
            StatusText = LedgerBookSetupStatusText;
            return;
        }

        try
        {
            var created = await _ledgerBookService.CreateBookAsync(
                new CreateLedgerBookRequest(
                    candidate.FundProfileId,
                    candidate.FundStructureNodeId,
                    candidate.FundStructureNodeKind,
                    candidate.DisplayName,
                    candidate.BaseCurrency,
                    candidate.Description,
                    candidate.AccountingBasis,
                    candidate.AccountingPolicyId,
                    candidate.AccountingPolicyVersion),
                ct).ConfigureAwait(false);

            _configuration = await _configurationService.GetWorkspaceAsync(
                candidate.FundProfileId,
                created.LedgerBookId,
                ct).ConfigureAwait(false);
            ApplyConfiguration(_configuration);
            await LoadManualJournalPeriodAsync(ct).ConfigureAwait(false);
            await LoadManualJournalWorkbenchAsync(ct).ConfigureAwait(false);
            await LoadPolicyRowsAsync(ct).ConfigureAwait(false);
            await LoadOperationsPostureAsync(ct).ConfigureAwait(false);
            await RefreshExternalGlAsync(ct).ConfigureAwait(false);
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);

            LedgerBookSetupStatusText = $"Created ledger book {created.DisplayName} ({created.LedgerBookId:D}) from shared setup candidate.";
            StatusText = LedgerBookSetupStatusText;
        }
        catch (Exception ex)
        {
            LedgerBookSetupStatusText = $"Ledger-book setup is blocked: {ex.Message}";
            StatusText = LedgerBookSetupStatusText;
        }
    }

    private void ApplyConfiguration(AccountingConfigurationWorkspaceDto workspace)
    {
        ApplyEntityConsolidationBooks(workspace.LedgerBooks);
        ChartRows.ReplaceWith(workspace.ChartOfAccounts.Select(node =>
            new AccountingWorkbenchRow(node.Path, node.AccountType, node.AccountName, node.IsArchived ? "Archived" : "Active")));
        TemplateRows.ReplaceWith(workspace.JournalTemplates.Select(template =>
            new AccountingWorkbenchRow(template.TemplateId, template.Version, template.DisplayName, $"{template.Lines.Count} line(s)")));
        PostingRuleRows.ReplaceWith(workspace.PostingRules.Select(rule =>
            new AccountingWorkbenchRow(rule.RuleId, rule.SourceEventType, rule.DisplayName, rule.TemplateId)));
        AuditRows.ReplaceWith(workspace.AuditTrail.Take(20).Select(audit =>
            new AccountingWorkbenchRow(audit.Action, audit.Actor, UtcFormat.FormatUtcTimestamp(audit.RecordedAtUtc), audit.CorrelationId ?? string.Empty)));
        ApplySetupReadinessRows(workspace);
        ApplyLedgerBookRows(workspace);
        ApplyManualJournalValidationRows(workspace.ValidationIssues);

        var critical = workspace.ValidationIssues.Count(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        ConfigurationStatusText = $"{workspace.Status} {workspace.ConfigurationVersion}";
        ConfigurationDetailText = critical > 0
            ? $"{critical} critical configuration issue(s) block activation."
            : "Chart, templates, posting rules, validation, and audit are shared-contract backed.";
        ApplyRulesStudio(workspace.RulesStudio);
        NotifyChartAccountSaveStateChanged();
        NotifyPostingRuleDraftSaveStateChanged();
        NotifyTenantAdministrationSandboxStateChanged();
    }

    private void ApplySetupReadinessRows(AccountingConfigurationWorkspaceDto workspace)
    {
        var selectedBook = ResolveActiveLedgerBook(workspace);
        var missingLedgerBookIssue = workspace.ValidationIssues.FirstOrDefault(issue =>
            string.Equals(issue.Code, "configuration.ledger-book-missing", StringComparison.OrdinalIgnoreCase));
        var setupCandidate = workspace.LedgerBookSetupCandidate;
        var criticalCount = workspace.ValidationIssues.Count(issue =>
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        var selectedBookStatus = missingLedgerBookIssue is not null
            ? "Missing"
            : selectedBook is not null
                ? selectedBook.DisplayName
                : workspace.LedgerBookId.HasValue
                    ? "Scoped"
                    : "Fund scope";
        var selectedBookDetail = missingLedgerBookIssue?.Message
            ?? (selectedBook is not null
                ? $"{selectedBook.AccountingBasis} basis; policy {selectedBook.AccountingPolicyId}/{selectedBook.AccountingPolicyVersion}."
                : workspace.LedgerBookId.HasValue
                    ? $"Ledger book {workspace.LedgerBookId.Value:D} is selected; setup details are not loaded."
                    : "Configuration is using the fund-level setup scope.");
        var selectedBookAction = missingLedgerBookIssue?.SuggestedAction
            ?? setupCandidate?.SuggestedAction
            ?? (workspace.LedgerBookId?.ToString("D", CultureInfo.InvariantCulture) ?? "fund");
        var activationDetail = criticalCount > 0
            ? $"{criticalCount} critical shared validation issue(s) must be resolved before activation."
            : "Shared accounting configuration validation has no critical blockers.";

        SetupReadinessRows.ReplaceWith(
        [
            new AccountingWorkbenchRow(
                "Selected ledger book",
                selectedBookStatus,
                selectedBookDetail,
                selectedBookAction,
                workspace.LedgerBookId?.ToString("D", CultureInfo.InvariantCulture) ?? "fund"),
            new AccountingWorkbenchRow(
                "Activation readiness",
                criticalCount > 0 ? "Blocked" : "Ready",
                activationDetail,
                missingLedgerBookIssue?.SuggestedAction ?? "Activation can use the current shared configuration scope.",
                $"critical:{criticalCount}")
        ]);
    }

    private void ApplyLedgerBookRows(AccountingConfigurationWorkspaceDto workspace)
    {
        var selectedLedgerBookId = workspace.LedgerBookId ?? ResolveActiveLedgerBook(workspace)?.LedgerBookId;
        var rows = workspace.LedgerBooks
            .OrderByDescending(book => selectedLedgerBookId.HasValue && book.LedgerBookId == selectedLedgerBookId.Value)
            .ThenBy(static book => book.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(book =>
            {
                var selected = selectedLedgerBookId.HasValue && book.LedgerBookId == selectedLedgerBookId.Value;
                var scope = $"{book.FundProfileId} / {book.FundStructureNodeKind} {book.FundStructureNodeId:D}";
                var detail =
                    $"{book.AccountingBasis} basis | {book.BaseCurrency} | {scope} | Updated {book.UpdatedAt:yyyy-MM-dd}";
                var evidence = string.IsNullOrWhiteSpace(book.Description)
                    ? $"Policy {book.AccountingPolicyId}/{book.AccountingPolicyVersion}"
                    : $"{book.Description} | Policy {book.AccountingPolicyId}/{book.AccountingPolicyVersion}";

                return new AccountingWorkbenchRow(
                    book.DisplayName,
                    selected ? "Selected" : "Available",
                    detail,
                    evidence,
                    book.LedgerBookId.ToString("D", CultureInfo.InvariantCulture));
            })
            .ToArray();

        LedgerBookRows.ReplaceWith(rows);
        LedgerBookAdministrationText = rows.Length == 0
            ? "No ledger books are registered for this accounting configuration workspace."
            : $"{rows.Length} ledger book(s) registered; selected {rows.FirstOrDefault(row => row.Status == "Selected")?.Name ?? "none"}.";
    }
}
