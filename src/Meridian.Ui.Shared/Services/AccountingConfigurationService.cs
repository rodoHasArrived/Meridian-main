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

public sealed class InMemoryAccountingConfigurationStore : IAccountingConfigurationStore
{
    private readonly Dictionary<string, AccountingConfigurationWorkspaceDto> _workspaces = new(StringComparer.OrdinalIgnoreCase);

    public Task<AccountingConfigurationWorkspaceDto?> GetAsync(
        string fundProfileId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        _workspaces.TryGetValue(Key(fundProfileId, ledgerBookId, tenantId, companyId), out var workspace);
        return Task.FromResult(workspace);
    }

    public Task SaveAsync(AccountingConfigurationWorkspaceDto workspace, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(workspace);
        _workspaces[Key(workspace.FundProfileId, workspace.LedgerBookId, workspace.TenantId, workspace.CompanyId)] = workspace;
        return Task.CompletedTask;
    }

    private static string Key(string fundProfileId, Guid? ledgerBookId, string? tenantId, string? companyId)
        => $"{NormalizeOptional(tenantId) ?? "all"}|{NormalizeOptional(companyId) ?? "all"}|{NormalizeFundProfileId(fundProfileId)}|{ledgerBookId?.ToString("D") ?? "fund"}";

    private static string NormalizeFundProfileId(string value)
        => string.IsNullOrWhiteSpace(value) ? "default-fund" : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class InMemoryAccountingActionAuditStore : IAccountingActionAuditStore
{
    private readonly List<AccountingActionAuditEventDto> _events = [];

    public Task AppendAsync(AccountingActionAuditEventDto auditEvent, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _events.Add(auditEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedCompanyId = NormalizeOptional(companyId);
        var events = _events
            .Where(item => string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(item.FundProfileId, fundProfileId.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId)
            .Where(item => normalizedCompanyId is null || string.Equals(item.CompanyId, normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.RecordedAtUtc)
            .ThenBy(item => item.AuditEventId)
            .ToArray();

        return Task.FromResult<IReadOnlyList<AccountingActionAuditEventDto>>(events);
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class FileAccountingConfigurationStore : IAccountingConfigurationStore, IAccountingActionAuditStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _snapshotPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileAccountingConfigurationStore(string snapshotPath)
    {
        _snapshotPath = string.IsNullOrWhiteSpace(snapshotPath)
            ? throw new ArgumentException("Accounting configuration snapshot path is required.", nameof(snapshotPath))
            : snapshotPath;
    }

    public async Task<AccountingConfigurationWorkspaceDto?> GetAsync(
        string fundProfileId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.Workspaces.FirstOrDefault(item =>
            string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase) &&
            item.LedgerBookId == ledgerBookId &&
            string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeOptional(item.CompanyId), normalizedCompanyId, StringComparison.OrdinalIgnoreCase)) is { } workspace
            ? workspace with { FundProfileId = normalizedFundProfileId, AuditTrail = [] }
            : null;
    }

    public async Task SaveAsync(AccountingConfigurationWorkspaceDto workspace, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var normalizedFundProfileId = NormalizeFundProfileId(workspace.FundProfileId);
            var normalizedTenantId = NormalizeOptional(workspace.TenantId);
            var normalizedCompanyId = NormalizeOptional(workspace.CompanyId);
            var snapshot = await ReadSnapshotWithoutLockAsync(ct).ConfigureAwait(false);
            var workspaces = snapshot.Workspaces
                .Where(item =>
                    !string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase) ||
                    item.LedgerBookId != workspace.LedgerBookId ||
                    !string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(NormalizeOptional(item.CompanyId), normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
                .Append(workspace with
                {
                    FundProfileId = normalizedFundProfileId,
                    TenantId = normalizedTenantId,
                    CompanyId = normalizedCompanyId,
                    AuditTrail = []
                })
                .OrderBy(item => item.FundProfileId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.LedgerBookId?.ToString("D") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TenantId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.CompanyId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            await WriteSnapshotAsync(snapshot with { Workspaces = workspaces }, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(AccountingActionAuditEventDto auditEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await ReadSnapshotWithoutLockAsync(ct).ConfigureAwait(false);
            var events = snapshot.AuditEvents
                .Append(auditEvent with { FundProfileId = NormalizeOptional(auditEvent.FundProfileId) })
                .OrderByDescending(item => item.RecordedAtUtc)
                .ThenBy(item => item.AuditEventId)
                .ToArray();

            await WriteSnapshotAsync(snapshot with { AuditEvents = events }, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        var normalizedFundProfileId = NormalizeOptional(fundProfileId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.AuditEvents
            .Where(item => string.IsNullOrWhiteSpace(normalizedFundProfileId) ||
                           string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
            .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId)
            .Where(item => normalizedCompanyId is null || string.Equals(item.CompanyId, normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.RecordedAtUtc)
            .ThenBy(item => item.AuditEventId)
            .ToArray();
    }

    private async Task<AccountingConfigurationSnapshot> ReadSnapshotAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadSnapshotWithoutLockAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AccountingConfigurationSnapshot> ReadSnapshotWithoutLockAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(_snapshotPath))
        {
            return AccountingConfigurationSnapshot.Empty;
        }

        await using var stream = File.OpenRead(_snapshotPath);
        return await JsonSerializer
            .DeserializeAsync<AccountingConfigurationSnapshot>(stream, JsonOptions, ct)
            .ConfigureAwait(false) ?? AccountingConfigurationSnapshot.Empty;
    }

    private Task WriteSnapshotAsync(AccountingConfigurationSnapshot snapshot, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        return AtomicFileWriter.WriteAsync(_snapshotPath, json, ct);
    }

    private static string NormalizeFundProfileId(string? value)
        => string.IsNullOrWhiteSpace(value) ? "default-fund" : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record AccountingConfigurationSnapshot(
        IReadOnlyList<AccountingConfigurationWorkspaceDto> Workspaces,
        IReadOnlyList<AccountingActionAuditEventDto> AuditEvents)
    {
        public static AccountingConfigurationSnapshot Empty { get; } = new([], []);
    }
}

public sealed class AccountingConfigurationService : IAccountingConfigurationService
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
            request.CorrelationId), ct).ConfigureAwait(false);
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
                TenantId = testCase.Request.TenantId ?? request.TenantId,
                CompanyId = testCase.Request.CompanyId ?? request.CompanyId
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
                NormalizePrincipalIds(reportGroupPrincipalIds)),
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
            ExternalGlDimensions: externalGlDimensions);
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
    {
        if (actionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new InvalidOperationException(
                $"Reviewed automation cannot {action}; a human operator approval is required.");
        }
    }

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

    private static string Hash(AccountingConfigurationWorkspaceDto workspace)
    {
        var json = JsonSerializer.Serialize(workspace);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

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

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

public sealed class InMemoryManualJournalEntryDraftStore : IManualJournalEntryDraftStore
{
    private readonly Dictionary<string, ManualJournalEntryDraftDto> _drafts = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var fundProfileIds = _drafts.Values
            .Select(static item => NormalizeFundProfileId(item.FundProfileId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(fundProfileIds);
    }

    public Task<IReadOnlyList<ManualJournalEntryDraftDto>> ListAsync(
        string fundProfileId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var drafts = _drafts.Values
            .Where(item => string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
            .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ThenBy(item => item.JournalEntryId)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ManualJournalEntryDraftDto>>(drafts);
    }

    public Task<ManualJournalEntryDraftDto?> GetAsync(
        string fundProfileId,
        Guid journalEntryId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _drafts.TryGetValue(Key(NormalizeFundProfileId(fundProfileId), journalEntryId), out var draft);
        return Task.FromResult(draft);
    }

    public Task SaveAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(draft);
        _drafts[Key(NormalizeFundProfileId(draft.FundProfileId), draft.JournalEntryId)] = draft;
        return Task.CompletedTask;
    }

    private static string Key(string fundProfileId, Guid journalEntryId)
        => $"{NormalizeFundProfileId(fundProfileId)}|{journalEntryId:D}";

    private static string NormalizeFundProfileId(string value)
        => string.IsNullOrWhiteSpace(value) ? "default-fund" : value.Trim();
}

public sealed class FileManualJournalEntryDraftStore : IManualJournalEntryDraftStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _snapshotPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileManualJournalEntryDraftStore(string snapshotPath)
    {
        _snapshotPath = string.IsNullOrWhiteSpace(snapshotPath)
            ? throw new ArgumentException("Manual journal entry draft snapshot path is required.", nameof(snapshotPath))
            : snapshotPath;
    }

    public async Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
    {
        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.Drafts
            .Select(static item => NormalizeFundProfileId(item.FundProfileId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<ManualJournalEntryDraftDto>> ListAsync(
        string fundProfileId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default)
    {
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.Drafts
            .Where(item => string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
            .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ThenBy(item => item.JournalEntryId)
            .ToArray();
    }

    public async Task<ManualJournalEntryDraftDto?> GetAsync(
        string fundProfileId,
        Guid journalEntryId,
        CancellationToken ct = default)
    {
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.Drafts.FirstOrDefault(item =>
            item.JournalEntryId == journalEntryId &&
            string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SaveAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await ReadSnapshotWithoutLockAsync(ct).ConfigureAwait(false);
            var normalizedFundProfileId = NormalizeFundProfileId(draft.FundProfileId);
            var drafts = snapshot.Drafts
                .Where(item => item.JournalEntryId != draft.JournalEntryId ||
                               !string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
                .Append(draft with { FundProfileId = normalizedFundProfileId })
                .OrderByDescending(item => item.UpdatedAtUtc)
                .ThenBy(item => item.JournalEntryId)
                .ToArray();

            var next = new ManualJournalEntryDraftSnapshot(drafts);
            var json = JsonSerializer.Serialize(next, JsonOptions);
            await AtomicFileWriter.WriteAsync(_snapshotPath, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ManualJournalEntryDraftSnapshot> ReadSnapshotAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadSnapshotWithoutLockAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ManualJournalEntryDraftSnapshot> ReadSnapshotWithoutLockAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(_snapshotPath))
        {
            return new ManualJournalEntryDraftSnapshot([]);
        }

        await using var stream = File.OpenRead(_snapshotPath);
        return await JsonSerializer
            .DeserializeAsync<ManualJournalEntryDraftSnapshot>(stream, JsonOptions, ct)
            .ConfigureAwait(false) ?? new ManualJournalEntryDraftSnapshot([]);
    }

    private static string NormalizeFundProfileId(string value)
        => string.IsNullOrWhiteSpace(value) ? "default-fund" : value.Trim();

    private sealed record ManualJournalEntryDraftSnapshot(IReadOnlyList<ManualJournalEntryDraftDto> Drafts);
}

public sealed class ManualJournalEntryWorkbenchService : IManualJournalEntryWorkbenchService, IManualJournalEntryLifecycleService
{
    private const string DefaultFundProfileId = "default-fund";
    private static readonly IReadOnlyDictionary<Guid, string> EmptyJournalEntryCurrencies = new Dictionary<Guid, string>();

    private readonly IManualJournalEntryDraftStore _draftStore;
    private readonly IAccountingConfigurationService _configurationService;
    private readonly IAccountingActionAuditStore _auditStore;
    private readonly ISecurityMasterQueryService? _securityMasterQueryService;
    private readonly ILedgerJournalStore? _journalStore;
    private readonly ReportPackWorkflowService? _reportPackWorkflowService;
    private readonly IBankTransactionSource? _bankTransactionSource;

    public ManualJournalEntryWorkbenchService(
        IManualJournalEntryDraftStore draftStore,
        IAccountingConfigurationService configurationService,
        IAccountingActionAuditStore auditStore,
        ISecurityMasterQueryService? securityMasterQueryService = null,
        ILedgerJournalStore? journalStore = null,
        ReportPackWorkflowService? reportPackWorkflowService = null,
        IBankTransactionSource? bankTransactionSource = null)
    {
        _draftStore = draftStore ?? throw new ArgumentNullException(nameof(draftStore));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _securityMasterQueryService = securityMasterQueryService;
        _journalStore = journalStore;
        _reportPackWorkflowService = reportPackWorkflowService;
        _bankTransactionSource = bankTransactionSource;
    }

    public async Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var fundProfileIds = new List<string>();
        fundProfileIds.AddRange(await _draftStore.ListFundProfileIdsAsync(ct).ConfigureAwait(false));

        if (_journalStore is not null)
        {
            var ledgerBooks = await _journalStore
                .ListLedgerBooksAsync(fundProfileId: null, fundStructureNodeId: null, fundStructureNodeKind: null, ct)
                .ConfigureAwait(false);
            fundProfileIds.AddRange(ledgerBooks.Select(static book => NormalizeFundProfileId(book.FundProfileId)));
        }

        return fundProfileIds
            .Where(static fundProfileId => !string.IsNullOrWhiteSpace(fundProfileId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static fundProfileId => fundProfileId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<BankTransactionDto>> ListBankTransactionsAsync(CancellationToken ct)
    {
        if (_bankTransactionSource is null)
        {
            return [];
        }

        return await _bankTransactionSource
            .GetBankTransactionsAsync(entityId: null, ct)
            .ConfigureAwait(false);
    }

    public async Task<ManualJournalEntryWorkbenchDto> GetWorkbenchAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var configuration = await _configurationService.GetWorkspaceAsync(normalizedFundProfileId, ledgerBookId, ct).ConfigureAwait(false);
        var drafts = await _draftStore.ListAsync(normalizedFundProfileId, ledgerBookId, ct).ConfigureAwait(false);
        var audit = await _auditStore.ListAsync(normalizedFundProfileId, ledgerBookId, ct).ConfigureAwait(false);
        var bankTransactions = await ListBankTransactionsAsync(ct).ConfigureAwait(false);
        var posted = await BuildPostedPrivateCapitalActivityProjectionAsync(normalizedFundProfileId, ledgerBookId, ct).ConfigureAwait(false);
        var reportPackWorkflowRecords = _reportPackWorkflowService?.ListRecords(200) ?? [];
        var privateCapitalActivity = PrivateCapitalActivityProjectionBuilder.Build(
            new PrivateCapitalActivityProjectionInput(
                normalizedFundProfileId,
                ledgerBookId,
                drafts,
                audit,
                bankTransactions,
                posted,
                reportPackWorkflowRecords));

        return new ManualJournalEntryWorkbenchDto(
            normalizedFundProfileId,
            ledgerBookId,
            DateTimeOffset.UtcNow,
            configuration.LedgerBooks,
            configuration.ChartOfAccounts,
            drafts,
            audit,
            privateCapitalActivity);
    }

    public async Task<PrivateCapitalActivityProjectionDto> GetPrivateCapitalActivityAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var drafts = await _draftStore.ListAsync(normalizedFundProfileId, ledgerBookId, ct).ConfigureAwait(false);
        var audit = await _auditStore.ListAsync(normalizedFundProfileId, ledgerBookId, ct).ConfigureAwait(false);
        var bankTransactions = await ListBankTransactionsAsync(ct).ConfigureAwait(false);
        var posted = await BuildPostedPrivateCapitalActivityProjectionAsync(normalizedFundProfileId, ledgerBookId, ct).ConfigureAwait(false);
        var reportPackWorkflowRecords = _reportPackWorkflowService?.ListRecords(200) ?? [];
        return PrivateCapitalActivityProjectionBuilder.Build(
            new PrivateCapitalActivityProjectionInput(
                normalizedFundProfileId,
                ledgerBookId,
                drafts,
                audit,
                bankTransactions,
                posted,
                reportPackWorkflowRecords));
    }

    private async Task<PostedPrivateCapitalActivityProjection?> BuildPostedPrivateCapitalActivityProjectionAsync(
        string fundProfileId,
        Guid? ledgerBookId,
        CancellationToken ct)
    {
        if (_journalStore is null)
        {
            return null;
        }

        var periods = await _journalStore
            .ListPeriodsAsync(ledgerBookId, status: null, fundProfileId, fundStructureNodeId: null, ct)
            .ConfigureAwait(false);
        if (periods.Count == 0)
        {
            return new PostedPrivateCapitalActivityProjection(new PrivateCapitalFundEventLedgerProjection([]), EmptyJournalEntryCurrencies);
        }

        var ledger = new Meridian.Ledger.Ledger();
        var journalEntryIds = new HashSet<Guid>();
        var journalEntryCurrencies = new Dictionary<Guid, string>();
        var ledgerBookCurrencies = new Dictionary<Guid, string>();
        foreach (var period in periods.OrderBy(static item => item.StartDate).ThenBy(static item => item.PeriodNo))
        {
            ct.ThrowIfCancellationRequested();
            var bookCurrency = await ResolveLedgerBookCurrencyAsync(period.LedgerBookId, ledgerBookCurrencies, ct).ConfigureAwait(false);
            var records = await _journalStore.GetByPeriodAsync(period.PeriodId, ct).ConfigureAwait(false);
            foreach (var record in records.OrderBy(static item => item.GlobalSequence).ThenBy(static item => item.Entry.Timestamp))
            {
                if (journalEntryIds.Add(record.Entry.JournalEntryId))
                {
                    ledger.Post(record.Entry);
                    journalEntryCurrencies[record.Entry.JournalEntryId] = bookCurrency;
                }
            }
        }

        return new PostedPrivateCapitalActivityProjection(
            PrivateCapitalFundEventLedgerProjector.Project(ledger),
            journalEntryCurrencies);
    }

    private async Task<string> ResolveLedgerBookCurrencyAsync(
        Guid? ledgerBookId,
        Dictionary<Guid, string> cache,
        CancellationToken ct)
    {
        if (ledgerBookId is not { } resolvedLedgerBookId)
        {
            return "USD";
        }

        if (cache.TryGetValue(resolvedLedgerBookId, out var cached))
        {
            return cached;
        }

        var book = _journalStore is null
            ? null
            : await _journalStore.GetLedgerBookAsync(resolvedLedgerBookId, ct).ConfigureAwait(false);
        var currency = NormalizeCurrency(book?.BaseCurrency);
        cache[resolvedLedgerBookId] = currency;
        return currency;
    }

    public async Task<ManualJournalEntryDraftDto> SaveDraftAsync(
        SaveManualJournalEntryDraftRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        EnsurePeriodUnlocked(request.PeriodIsLocked, "save manual journal entry drafts");
        var normalizedDraft = await NormalizeAndValidateAsync(request.Draft, allowIncomplete: true, ct).ConfigureAwait(false);
        EnsureRequestedLedgerBookMatchesDraft(request.LedgerBookId, normalizedDraft);
        var existing = await _draftStore.GetAsync(normalizedDraft.FundProfileId, normalizedDraft.JournalEntryId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            EnsureRequestedLedgerBookMatchesDraft(request.LedgerBookId, existing, requireScopeForBookScopedDraft: true);
        }
        if (existing is not null && existing.Version != request.Draft.Version)
        {
            throw new InvalidOperationException("Manual journal entry draft version is stale.");
        }

        if (existing is not null && !CanSaveManualJournalDraft(existing.Status))
        {
            throw new InvalidOperationException(
                $"Manual journal entry '{existing.JournalEntryId:D}' is {existing.Status} and cannot be edited through draft save; use the governed lifecycle or correction workflow.");
        }

        var now = DateTimeOffset.UtcNow;
        var saved = normalizedDraft with
        {
            Status = normalizedDraft.ValidationIssues.Any(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
                ? ManualJournalEntryStatusDto.NeedsFix
                : ManualJournalEntryStatusDto.Draft,
            CreatedAtUtc = existing?.CreatedAtUtc ?? (normalizedDraft.CreatedAtUtc == default ? now : normalizedDraft.CreatedAtUtc),
            UpdatedAtUtc = now,
            Version = (existing?.Version ?? 0) + 1,
            PreparedBy = RequireText(request.Actor, nameof(request.Actor)),
            EvidenceLinks = MergeEvidenceLinks(normalizedDraft.EvidenceLinks, request.EvidenceLinks)
        };
        saved = ClearManualJournalReviewMetadataForEditableDraft(saved);

        await _draftStore.SaveAsync(saved, ct).ConfigureAwait(false);
        await AppendAuditAsync(saved, "manual-je.save-draft", request.Actor, request.CorrelationId, saved.EvidenceLinks, ct).ConfigureAwait(false);
        return saved;
    }

    private static ManualJournalEntryDraftDto ClearManualJournalReviewMetadataForEditableDraft(
        ManualJournalEntryDraftDto draft)
        => draft.Status is ManualJournalEntryStatusDto.Draft or ManualJournalEntryStatusDto.NeedsFix
            ? draft with
            {
                ApprovalId = null,
                SubmittedAtUtc = null,
                SubmittedBy = null,
                ApprovedAtUtc = null,
                ApprovedBy = null,
                PostedAtUtc = null,
                PostedBy = null,
                ClosedLockedAtUtc = null,
                CloseLockedBy = null
            }
            : draft;

    private static bool CanSaveManualJournalDraft(ManualJournalEntryStatusDto status)
        => status is ManualJournalEntryStatusDto.Draft
            or ManualJournalEntryStatusDto.NeedsFix
            or ManualJournalEntryStatusDto.Rejected;

    public Task<ManualJournalEntryDraftDto> ValidateDraftAsync(
        ValidateManualJournalEntryDraftRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        EnsureRequestedLedgerBookMatchesDraft(request.LedgerBookId, request.Draft);
        return NormalizeAndValidateAsync(request.Draft, allowIncomplete: false, ct, periodIsLocked: request.PeriodIsLocked);
    }

    public async Task<ManualJournalEntryDraftDto> SubmitApprovalAsync(
        SubmitManualJournalEntryApprovalRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "submit manual journal entries for approval");
        EnsurePeriodUnlocked(request.PeriodIsLocked, "submit manual journal entries for approval");
        var fundProfileId = NormalizeFundProfileId(request.FundProfileId);
        var draft = await _draftStore.GetAsync(fundProfileId, request.JournalEntryId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Manual journal entry '{request.JournalEntryId:D}' was not found.");
        EnsureRequestedLedgerBookMatchesDraft(request.LedgerBookId, draft, requireScopeForBookScopedDraft: true);
        if (FindIdempotentLifecycleTransition(
                draft,
                JournalEntryLifecycleActionDto.Submit,
                request.Actor,
                request.CorrelationId) is not null)
        {
            return draft;
        }

        if (draft.Version != request.Version)
        {
            throw new InvalidOperationException("Manual journal entry draft version is stale.");
        }

        if (!CanSubmitManualJournalEntry(draft.Status))
        {
            throw new InvalidOperationException(
                $"Manual journal entry '{draft.JournalEntryId:D}' is {draft.Status} and cannot be submitted for approval.");
        }

        var validated = await NormalizeAndValidateAsync(
            draft,
            allowIncomplete: false,
            ct,
            periodIsLocked: request.PeriodIsLocked).ConfigureAwait(false);
        if (validated.ValidationIssues.Any(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
        {
            throw new InvalidOperationException("Manual journal entry cannot be submitted while critical validation issues remain.");
        }

        var now = DateTimeOffset.UtcNow;
        var transition = BuildSubmitTransition(validated.Status, request, now);
        var submitted = validated with
        {
            Status = ManualJournalEntryStatusDto.Submitted,
            ApprovalId = validated.ApprovalId ?? $"manual-je-approval-{validated.JournalEntryId:N}",
            SubmittedAtUtc = now,
            SubmittedBy = RequireText(request.Actor, nameof(request.Actor)),
            UpdatedAtUtc = now,
            Version = validated.Version + 1,
            EvidenceLinks = MergeEvidenceLinks(validated.EvidenceLinks, request.EvidenceLinks),
            LifecycleTransitions = validated.LifecycleTransitions.Append(transition).ToArray()
        };

        await _draftStore.SaveAsync(submitted, ct).ConfigureAwait(false);
        await AppendAuditAsync(submitted, "manual-je.submit-approval", request.Actor, request.CorrelationId, submitted.EvidenceLinks, ct).ConfigureAwait(false);
        return submitted;
    }

    public async Task<ManualJournalEntryDraftDto> AttachEvidenceAsync(
        AttachManualJournalEntryEvidenceRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Attachment);
        EnsureHumanOrigin(request.ActionOrigin, "attach evidence to manual journal entries");
        EnsurePeriodUnlocked(request.PeriodIsLocked, "attach evidence to manual journal entries");

        var fundProfileId = NormalizeFundProfileId(request.FundProfileId);
        var draft = await _draftStore.GetAsync(fundProfileId, request.JournalEntryId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Manual journal entry '{request.JournalEntryId:D}' was not found.");
        EnsureRequestedLedgerBookMatchesDraft(request.LedgerBookId, draft, requireScopeForBookScopedDraft: true);
        if (draft.Version != request.Version)
        {
            throw new InvalidOperationException("Manual journal entry draft version is stale.");
        }

        if (draft.Status is ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.Reversed or ManualJournalEntryStatusDto.Rebooked or ManualJournalEntryStatusDto.CloseLocked)
        {
            throw new InvalidOperationException("Posted, reversed, rebooked, and close-locked journal entries are immutable; attach evidence before posting or create a correction draft.");
        }

        var normalizedAttachments = NormalizeAttachments([request.Attachment], request.Actor);
        if (normalizedAttachments.Count == 0)
        {
            throw new ArgumentException("Manual journal evidence attachment requires a display name and URI.", nameof(request));
        }

        var normalizedAttachment = normalizedAttachments[0];
        if (string.IsNullOrWhiteSpace(normalizedAttachment.DisplayName) || string.IsNullOrWhiteSpace(normalizedAttachment.Uri))
        {
            throw new ArgumentException("Manual journal evidence attachment requires a display name and URI.", nameof(request));
        }

        if (!string.IsNullOrWhiteSpace(normalizedAttachment.LineId) &&
            !draft.Lines.Any(line => string.Equals(line.LineId, normalizedAttachment.LineId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Manual journal evidence attachment references missing line '{normalizedAttachment.LineId}'.");
        }

        var attachments = (draft.EvidenceAttachments ?? [])
            .Where(item => !string.Equals(item.AttachmentId, normalizedAttachment.AttachmentId, StringComparison.OrdinalIgnoreCase))
            .Append(normalizedAttachment)
            .OrderBy(item => item.AddedAtUtc)
            .ThenBy(item => item.AttachmentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var evidenceLinks = MergeEvidenceLinks(
            MergeEvidenceLinks(draft.EvidenceLinks, request.EvidenceLinks),
            [normalizedAttachment.Uri]);
        var next = draft with
        {
            EvidenceAttachments = attachments,
            EvidenceLinks = evidenceLinks,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Version = draft.Version + 1
        };

        await _draftStore.SaveAsync(next, ct).ConfigureAwait(false);
        await AppendAuditAsync(next, "manual-je.attach-evidence", request.Actor, request.CorrelationId, evidenceLinks, ct).ConfigureAwait(false);
        return next;
    }

    public async Task<JournalEntryLifecycleActionResultDto> ApplyLifecycleActionAsync(
        JournalEntryLifecycleActionRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, $"apply journal entry lifecycle action '{request.Action}'");
        var fundProfileId = NormalizeFundProfileId(request.FundProfileId);
        var draft = await _draftStore.GetAsync(fundProfileId, request.JournalEntryId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Manual journal entry '{request.JournalEntryId:D}' was not found.");
        EnsureRequestedLedgerBookMatchesDraft(request.LedgerBookId, draft, requireScopeForBookScopedDraft: true);
        var idempotent = await TryBuildIdempotentLifecycleResultAsync(fundProfileId, draft, request, ct).ConfigureAwait(false);
        if (idempotent is not null)
        {
            return idempotent;
        }

        if (draft.Version != request.Version)
        {
            throw new InvalidOperationException("Manual journal entry draft version is stale.");
        }

        if (request.PeriodIsLocked &&
            request.Action is not (JournalEntryLifecycleActionDto.Validate or JournalEntryLifecycleActionDto.LockAfterClose))
        {
            throw new InvalidOperationException("Manual journal entry lifecycle action is blocked because the accounting period is locked after close.");
        }

        var validated = await NormalizeAndValidateAsync(
            draft,
            allowIncomplete: false,
            ct,
            periodIsLocked: request.PeriodIsLocked).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        return request.Action switch
        {
            JournalEntryLifecycleActionDto.Validate => await ApplyStatusTransitionAsync(
                validated,
                request,
                validated.ValidationIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
                    ? ManualJournalEntryStatusDto.NeedsFix
                    : validated.Status,
                "manual-je.validate",
                now,
                ct).ConfigureAwait(false),
            JournalEntryLifecycleActionDto.Submit => await ApplySubmitLifecycleActionAsync(
                request,
                ct).ConfigureAwait(false),
            JournalEntryLifecycleActionDto.Approve => await ApplyStatusTransitionAsync(
                RequireStatus(validated, ManualJournalEntryStatusDto.Submitted, request.Action),
                RequireApprovalLifecycleEvidence(RequireLifecycleDecisionNotes(request), validated),
                ManualJournalEntryStatusDto.Approved,
                "manual-je.approve",
                now,
                ct,
                item => item with { ApprovedAtUtc = now, ApprovedBy = RequireText(request.Actor, nameof(request.Actor)) }).ConfigureAwait(false),
            JournalEntryLifecycleActionDto.Reject => await ApplyStatusTransitionAsync(
                RequireStatus(validated, ManualJournalEntryStatusDto.Submitted, request.Action),
                RequireApprovalLifecycleEvidence(RequireLifecycleDecisionNotes(request), validated),
                ManualJournalEntryStatusDto.Rejected,
                "manual-je.reject",
                now,
                ct).ConfigureAwait(false),
            JournalEntryLifecycleActionDto.Post => await PostApprovedManualJournalEntryAsync(
                RequireStatus(validated, ManualJournalEntryStatusDto.Approved, request.Action),
                RequirePostingLifecycleEvidence(
                    RequirePostingLifecycleNotes(request),
                    validated),
                now,
                ct).ConfigureAwait(false),
            JournalEntryLifecycleActionDto.LockAfterClose => await ApplyStatusTransitionAsync(
                RequireStatus(validated, ManualJournalEntryStatusDto.Posted, request.Action),
                RequireCloseLockLifecycleEvidence(RequirePostingLifecycleNotes(request), validated),
                ManualJournalEntryStatusDto.CloseLocked,
                "manual-je.lock-after-close",
                now,
                ct,
                item => item with { ClosedLockedAtUtc = now, CloseLockedBy = RequireText(request.Actor, nameof(request.Actor)) }).ConfigureAwait(false),
            JournalEntryLifecycleActionDto.Reverse => await CreateCorrectionDraftAsync(
                RequirePostedForCorrection(validated, request.Action),
                request,
                reverseSides: true,
                "manual-je.reverse-draft",
                now,
                ct).ConfigureAwait(false),
            JournalEntryLifecycleActionDto.Rebook => await CreateCorrectionDraftAsync(
                RequirePostedForCorrection(validated, request.Action),
                request,
                reverseSides: false,
                "manual-je.rebook-draft",
                now,
                ct).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(request), "Unsupported journal entry lifecycle action.")
        };
    }

    private async Task<JournalEntryLifecycleActionResultDto> ApplySubmitLifecycleActionAsync(
        JournalEntryLifecycleActionRequestDto request,
        CancellationToken ct)
    {
        var submitted = await SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            request.JournalEntryId,
            request.FundProfileId,
            request.Actor,
            request.Version,
            request.Notes,
            request.CorrelationId,
            request.EvidenceLinks,
            request.ActionOrigin,
            request.PeriodIsLocked,
            request.LedgerBookId), ct).ConfigureAwait(false);
        var transition = submitted.LifecycleTransitions.Last(static item =>
            item.Action == JournalEntryLifecycleActionDto.Submit &&
            item.ToStatus == ManualJournalEntryStatusDto.Submitted);
        return new JournalEntryLifecycleActionResultDto(submitted, transition);
    }

    private async Task<JournalEntryLifecycleActionResultDto> ApplyStatusTransitionAsync(
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionRequestDto request,
        ManualJournalEntryStatusDto toStatus,
        string auditAction,
        DateTimeOffset recordedAtUtc,
        CancellationToken ct,
        Func<ManualJournalEntryDraftDto, ManualJournalEntryDraftDto>? mutate = null)
    {
        if (draft.ValidationIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical) &&
            toStatus is ManualJournalEntryStatusDto.Approved or ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked)
        {
            throw new InvalidOperationException("Manual journal entry lifecycle action is blocked while critical validation issues remain.");
        }

        EnsureLifecycleActorIndependentFromPreparer(draft, request);

        var transition = BuildTransition(draft.Status, toStatus, request, recordedAtUtc);
        var next = draft with
        {
            Status = toStatus,
            UpdatedAtUtc = recordedAtUtc,
            Version = draft.Version + 1,
            EvidenceLinks = MergeEvidenceLinks(draft.EvidenceLinks, request.EvidenceLinks),
            LifecycleTransitions = draft.LifecycleTransitions.Append(transition).ToArray()
        };
        if (mutate is not null)
        {
            next = mutate(next);
        }

        await _draftStore.SaveAsync(next, ct).ConfigureAwait(false);
        await AppendAuditAsync(next, auditAction, request.Actor, request.CorrelationId, next.EvidenceLinks, ct).ConfigureAwait(false);
        return new JournalEntryLifecycleActionResultDto(next, transition);
    }

    private async Task<JournalEntryLifecycleActionResultDto> PostApprovedManualJournalEntryAsync(
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionRequestDto request,
        DateTimeOffset recordedAtUtc,
        CancellationToken ct)
    {
        var journalStore = _journalStore
            ?? throw new InvalidOperationException("Manual journal entries cannot be posted because no ledger journal store is configured.");
        if (draft.ValidationIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
        {
            throw new InvalidOperationException("Manual journal entry lifecycle action is blocked while critical validation issues remain.");
        }

        EnsureLifecycleActorIndependentFromPreparer(draft, request);
        var ledgerBookId = draft.LedgerBookId
            ?? throw new InvalidOperationException("Manual journal entry posting requires a ledger book id.");
        if (string.IsNullOrWhiteSpace(draft.PeriodId) ||
            !Guid.TryParse(draft.PeriodId, out var periodId) ||
            periodId == Guid.Empty)
        {
            throw new InvalidOperationException("Manual journal entry posting requires a durable ledger period id.");
        }

        var ledgerBook = await journalStore.GetLedgerBookAsync(ledgerBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Ledger book '{ledgerBookId:D}' was not found.");
        if (ledgerBook.AccountingBasis != draft.AccountingBasis)
        {
            throw new InvalidOperationException(
                $"Manual journal entry basis '{draft.AccountingBasis}' does not match ledger book '{ledgerBook.LedgerBookId:D}' basis '{ledgerBook.AccountingBasis}'.");
        }

        var mergedEvidence = MergeEvidenceLinks(draft.EvidenceLinks, request.EvidenceLinks);
        var postingCommand = BuildManualPostingCommand(draft, request, ledgerBookId, periodId, mergedEvidence, recordedAtUtc);
        var write = await BuildManualJournalEntryWriteAsync(
                draft,
                ledgerBook,
                periodId,
                postingCommand,
                mergedEvidence,
                recordedAtUtc,
                ct)
            .ConfigureAwait(false);

        await journalStore.AppendAsync(write, ct).ConfigureAwait(false);

        var transition = BuildTransition(draft.Status, ManualJournalEntryStatusDto.Posted, request, recordedAtUtc);
        var next = draft with
        {
            Status = ManualJournalEntryStatusDto.Posted,
            UpdatedAtUtc = recordedAtUtc,
            Version = draft.Version + 1,
            EvidenceLinks = mergedEvidence,
            LifecycleTransitions = draft.LifecycleTransitions.Append(transition).ToArray(),
            PostedAtUtc = recordedAtUtc,
            PostedBy = RequireText(request.Actor, nameof(request.Actor))
        };

        await _draftStore.SaveAsync(next, ct).ConfigureAwait(false);
        await AppendAuditAsync(next, "manual-je.post", request.Actor, request.CorrelationId, next.EvidenceLinks, ct).ConfigureAwait(false);
        return new JournalEntryLifecycleActionResultDto(
            next,
            transition,
            PostedJournal: new PostedLedgerJournalEntryResultDto(
                write.Entry.JournalEntryId,
                ledgerBookId,
                write.AccountingBasis,
                periodId,
                write.AggregateId,
                write.CommandId,
                write.SourceEventId,
                write.CorrelationId,
                PostedAtUtc: recordedAtUtc,
                IdempotencyKey: postingCommand.IdempotencyKey));
    }

    private async Task<LedgerJournalEntryWrite> BuildManualJournalEntryWriteAsync(
        ManualJournalEntryDraftDto draft,
        LedgerBookRecord ledgerBook,
        Guid periodId,
        AccountingPostingCommandDto postingCommand,
        IReadOnlyList<string> evidenceLinks,
        DateTimeOffset recordedAtUtc,
        CancellationToken ct)
    {
        var configuration = await _configurationService
            .GetWorkspaceAsync(draft.FundProfileId, draft.LedgerBookId, ct)
            .ConfigureAwait(false);
        var chartByPath = BuildChartByPath(configuration.ChartOfAccounts);
        var timestamp = ToAccountingTimestamp(draft.AccountingDate);
        var description = NormalizeOptional(draft.Memo) ?? $"Manual journal entry {draft.JournalEntryId:D}";
        var lines = new List<LedgerEntry>(draft.Lines.Count);

        foreach (var line in draft.Lines)
        {
            if (!chartByPath.TryGetValue(line.AccountPath, out var account) || account.IsArchived)
            {
                throw new InvalidOperationException($"Manual journal entry line '{line.LineId}' references unavailable GL account '{line.AccountPath}'.");
            }

            if (!TryMapLedgerAccountType(account.AccountType, out var accountType))
            {
                throw new InvalidOperationException(
                    $"Manual journal entry line '{line.LineId}' references GL account '{account.Path}' with unsupported account type '{account.AccountType}'.");
            }

            var amount = Math.Abs(line.Amount);
            lines.Add(new LedgerEntry(
                CreateDeterministicGuid("manual-je-line", draft.JournalEntryId.ToString("D"), line.LineId, line.Side.ToString()),
                draft.JournalEntryId,
                timestamp,
                new LedgerAccount(account.AccountName, accountType),
                line.Side == AccountingTemplateLineSideDto.Debit ? amount : 0m,
                line.Side == AccountingTemplateLineSideDto.Credit ? amount : 0m,
                description,
                ToLedgerLineDimensions(line.Dimensions)));
        }

        var entry = new JournalEntry(
            draft.JournalEntryId,
            timestamp,
            description,
            lines,
            BuildManualJournalEntryMetadata(draft, postingCommand, evidenceLinks, recordedAtUtc));

        return new LedgerJournalEntryWrite(
            entry,
            postingCommand.AggregateId,
            periodId,
            postingCommand.CommandId,
            postingCommand.CorrelationId,
            draft.AccountingBasis,
            ledgerBook.AccountingPolicyId,
            ledgerBook.AccountingPolicyVersion,
            "manual-journal-entry",
            "v1",
            postingCommand.SourceEventId,
            postingCommand.SourceJournalEntryId,
            BuildManualPostingKind(draft),
            PostingCommand: postingCommand,
            LedgerBookId: ledgerBook.LedgerBookId);
    }

    private static AccountingPostingCommandDto BuildManualPostingCommand(
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionRequestDto request,
        Guid ledgerBookId,
        Guid periodId,
        IReadOnlyList<string> evidenceLinks,
        DateTimeOffset recordedAtUtc)
    {
        var treasuryContext = NormalizeTreasuryContext(draft.TreasuryContext, draft.AccountingDate);
        var sourceJournalEntryId = draft.ReversalOfJournalEntryId ?? draft.RebookedFromJournalEntryId;
        var idempotencyKey = NormalizeOptional(treasuryContext?.IdempotencyKey)
                             ?? $"manual-je:{ledgerBookId:N}:{draft.JournalEntryId:N}";
        var commandId = CreateDeterministicGuid(
            "manual-je-posting-command",
            ledgerBookId.ToString("D"),
            periodId.ToString("D"),
            draft.JournalEntryId.ToString("D"),
            idempotencyKey);

        var treasuryFundEventType = NormalizeOptional(treasuryContext?.FundEventType);
        var sourceEventType = treasuryFundEventType is not null
            ? $"ManualJournalEntry:{treasuryFundEventType}"
            : null;

        return new AccountingPostingCommandDto(
            commandId,
            ledgerBookId,
            periodId,
            draft.AccountingDate,
            recordedAtUtc,
            idempotencyKey,
            BuildManualPostingIntent(draft),
            SourceEventId: draft.JournalEntryId,
            CorrelationId: TryParseGuid(request.CorrelationId),
            CausationId: draft.JournalEntryId,
            SourceJournalEntryId: sourceJournalEntryId,
            SourceEventType: sourceEventType,
            TreasuryContext: treasuryContext,
            ApprovalState: AccountingPostingApprovalStateDto.Approved,
            ApprovalId: NormalizeOptional(draft.ApprovalId),
            OperatorRationale: NormalizeOptional(request.Notes),
            Evidence: evidenceLinks.Select(link => new AccountingPostingEvidenceReferenceDto(
                EvidenceId: link,
                Uri: link,
                Kind: ClassifyManualPostingEvidence(link),
                SourceSystem: "ManualJournalEntryWorkbench",
                RetainedAtUtc: recordedAtUtc,
                RetainedBy: RequireText(request.Actor, nameof(request.Actor)),
                SubjectId: draft.JournalEntryId.ToString("D"))).ToArray(),
            ActionOrigin: request.ActionOrigin,
            LedgerBookId: ledgerBookId);
    }

    private static JournalEntryMetadata BuildManualJournalEntryMetadata(
        ManualJournalEntryDraftDto draft,
        AccountingPostingCommandDto postingCommand,
        IReadOnlyList<string> evidenceLinks,
        DateTimeOffset recordedAtUtc)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddMetadataTag(tags, "manualJournalEntryId", draft.JournalEntryId.ToString("D"));
        AddMetadataTag(tags, "manualJournalEntryStatus", draft.Status.ToString());
        AddMetadataTag(tags, "manualJournalEntryType", draft.EntryType.ToString());
        AddMetadataTag(tags, "accountingBasis", draft.AccountingBasis.ToString());
        AddMetadataTag(tags, "ledgerBookId", draft.LedgerBookId?.ToString("D"));
        AddMetadataTag(tags, "periodId", draft.PeriodId);
        AddMetadataTag(tags, "sourceEventId", postingCommand.SourceEventId?.ToString("D"));
        AddMetadataTag(tags, "sourceJournalEntryId", postingCommand.SourceJournalEntryId?.ToString("D"));
        if (evidenceLinks.Count > 0)
        {
            tags["evidenceLinks"] = string.Join("|", evidenceLinks);
        }

        return new JournalEntryMetadata(
            ActivityType: "ManualJournalEntry",
            ProjectId: NormalizeOptional(draft.FundProfileId),
            LedgerBook: draft.LedgerBookId?.ToString("D"),
            EffectiveDate: postingCommand.TreasuryContext?.EffectiveDate ?? draft.AccountingDate,
            IdempotencyKey: postingCommand.IdempotencyKey,
            FundEventId: NormalizeOptional(postingCommand.TreasuryContext?.FundEventId),
            FundEventType: NormalizeOptional(postingCommand.TreasuryContext?.FundEventType),
            CapitalAccountId: NormalizeOptional(postingCommand.TreasuryContext?.CapitalAccountId),
            InvestorId: NormalizeOptional(postingCommand.TreasuryContext?.InvestorId),
            PaymentIntentId: NormalizeOptional(postingCommand.TreasuryContext?.PaymentIntentId),
            SettlementReference: NormalizeOptional(postingCommand.TreasuryContext?.SettlementReference),
            Tags: tags,
            EvidenceReferences: evidenceLinks.Select(link => new JournalEvidenceReference(
                EvidenceId: link,
                Uri: link,
                Kind: ClassifyManualPostingEvidence(link).ToString(),
                SourceSystem: "ManualJournalEntryWorkbench",
                RetainedAtUtc: recordedAtUtc,
                RetainedBy: NormalizeOptional(draft.PostedBy) ?? "manual-journal-entry-workbench",
                SubjectId: draft.JournalEntryId.ToString("D"))).ToArray());
    }

    private static AccountingPostingIntentDto BuildManualPostingIntent(ManualJournalEntryDraftDto draft)
    {
        if (draft.ReversalOfJournalEntryId.HasValue)
        {
            return AccountingPostingIntentDto.Reversal;
        }

        if (draft.RebookedFromJournalEntryId.HasValue)
        {
            return AccountingPostingIntentDto.Rebook;
        }

        return AccountingPostingIntentDto.Originating;
    }

    private static LedgerPostingKindDto BuildManualPostingKind(ManualJournalEntryDraftDto draft)
        => draft.ReversalOfJournalEntryId.HasValue || draft.RebookedFromJournalEntryId.HasValue
            ? LedgerPostingKindDto.Adjustment
            : LedgerPostingKindDto.Originating;

    private static AccountingPostingEvidenceKindDto ClassifyManualPostingEvidence(string evidenceLink)
    {
        if (evidenceLink.Contains("approval", StringComparison.OrdinalIgnoreCase))
        {
            return AccountingPostingEvidenceKindDto.Approval;
        }

        if (evidenceLink.Contains("reconciliation", StringComparison.OrdinalIgnoreCase) ||
            evidenceLink.Contains("reconcile", StringComparison.OrdinalIgnoreCase))
        {
            return AccountingPostingEvidenceKindDto.Reconciliation;
        }

        if (evidenceLink.Contains("posting", StringComparison.OrdinalIgnoreCase) ||
            evidenceLink.Contains("audit", StringComparison.OrdinalIgnoreCase))
        {
            return AccountingPostingEvidenceKindDto.AuditSupport;
        }

        if (evidenceLink.Contains("correction", StringComparison.OrdinalIgnoreCase) ||
            evidenceLink.Contains("reversal", StringComparison.OrdinalIgnoreCase) ||
            evidenceLink.Contains("rebook", StringComparison.OrdinalIgnoreCase))
        {
            return AccountingPostingEvidenceKindDto.Correction;
        }

        return AccountingPostingEvidenceKindDto.Source;
    }

    private static LedgerLineDimensionSet? ToLedgerLineDimensions(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return null;
        }

        return new LedgerLineDimensionSet(
            FundId: NormalizeOptional(dimensions.FundId),
            EntityId: NormalizeOptional(dimensions.EntityId),
            SleeveId: NormalizeOptional(dimensions.SleeveId),
            StrategyId: NormalizeOptional(dimensions.StrategyId),
            InvestorId: NormalizeOptional(dimensions.InvestorId),
            CapitalAccountId: NormalizeOptional(dimensions.CapitalAccountId),
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: NormalizeOptional(dimensions.TaxLotId),
            CostCenterId: NormalizeOptional(dimensions.CostCenterId),
            CounterpartyId: NormalizeOptional(dimensions.CounterpartyId),
            ExternalGlDimensions: dimensions.ExternalGlDimensions
                .Where(static item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
                .OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static item => item.Key.Trim(), static item => item.Value.Trim(), StringComparer.OrdinalIgnoreCase),
            OrganizationId: NormalizeOptional(dimensions.OrganizationId),
            PortfolioId: NormalizeOptional(dimensions.PortfolioId),
            BookId: NormalizeOptional(dimensions.BookId),
            AccountId: NormalizeOptional(dimensions.AccountId),
            CustomerId: NormalizeOptional(dimensions.CustomerId),
            VendorId: NormalizeOptional(dimensions.VendorId),
            ProjectId: NormalizeOptional(dimensions.ProjectId));
    }

    private static bool TryMapLedgerAccountType(string? value, out LedgerAccountType accountType)
    {
        switch (value?.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant())
        {
            case "ASSET":
            case "CONTRAASSET":
                accountType = LedgerAccountType.Asset;
                return true;
            case "LIABILITY":
            case "CONTRALIABILITY":
                accountType = LedgerAccountType.Liability;
                return true;
            case "EQUITY":
                accountType = LedgerAccountType.Equity;
                return true;
            case "REVENUE":
            case "INCOME":
                accountType = LedgerAccountType.Revenue;
                return true;
            case "EXPENSE":
                accountType = LedgerAccountType.Expense;
                return true;
            default:
                accountType = default;
                return false;
        }
    }

    private static DateTimeOffset ToAccountingTimestamp(DateOnly accountingDate)
        => new(accountingDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static Guid? TryParseGuid(string? value)
        => Guid.TryParse(value, out var parsed) && parsed != Guid.Empty ? parsed : null;

    private static Guid CreateDeterministicGuid(params string?[] parts)
    {
        var input = string.Join("|", parts.Select(NormalizeOptional));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(hash[..16]);
    }

    private static void AddMetadataTag(Dictionary<string, string> tags, string key, string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is not null)
        {
            tags[key] = normalized;
        }
    }

    private async Task<JournalEntryLifecycleActionResultDto> CreateCorrectionDraftAsync(
        ManualJournalEntryDraftDto posted,
        JournalEntryLifecycleActionRequestDto request,
        bool reverseSides,
        string auditAction,
        DateTimeOffset recordedAtUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Notes))
        {
            throw new InvalidOperationException("Manual journal reversal and rebook actions require a correction reason.");
        }

        if (request.EvidenceLinks.Count == 0)
        {
            throw new InvalidOperationException("Manual journal reversal and rebook actions require retained correction evidence.");
        }

        if (!HasManualJournalCorrectionEvidence(request.EvidenceLinks))
        {
            throw new InvalidOperationException("Manual journal reversal and rebook actions require retained reversal, rebook, correction, approval, or review evidence.");
        }

        if (!HasManualJournalCorrectionEvidenceWithProvenance(posted, request.EvidenceLinks))
        {
            throw new InvalidOperationException("Manual journal reversal and rebook evidence must reference correction intent, the posted journal entry or accounting period, and the scoped ledger book on the same evidence artifact.");
        }

        EnsureLifecycleActorIndependentFromPreparer(posted, request);

        var correctionId = Guid.NewGuid();
        var correctionLines = request.RebookLines.Count > 0 && !reverseSides
            ? request.RebookLines
            : posted.Lines
                .Select(line => line with
                {
                    LineId = $"{(reverseSides ? "reversal" : "rebook")}-{line.LineId}",
                    Side = reverseSides ? ReverseSide(line.Side) : line.Side,
                    EvidenceLink = line.EvidenceLink ?? request.EvidenceLinks.FirstOrDefault()
                })
                .ToArray();
        var toStatus = reverseSides
            ? ManualJournalEntryStatusDto.Reversed
            : ManualJournalEntryStatusDto.Rebooked;
        var transition = BuildTransition(posted.Status, toStatus, request, recordedAtUtc);
        var actor = RequireText(request.Actor, nameof(request.Actor));
        var reason = request.Notes.Trim();
        var reversal = reverseSides
            ? new JournalEntryReversalDto(posted.JournalEntryId, correctionId, reason, recordedAtUtc, actor)
            : null;
        var rebook = reverseSides
            ? null
            : new JournalEntryRebookDto(posted.JournalEntryId, correctionId, reason, recordedAtUtc, actor);
        var correctionTransition = BuildTransition(posted.Status, ManualJournalEntryStatusDto.Draft, request, recordedAtUtc);
        var corrected = posted with
        {
            Status = toStatus,
            UpdatedAtUtc = recordedAtUtc,
            Version = posted.Version + 1,
            EvidenceLinks = MergeEvidenceLinks(posted.EvidenceLinks, request.EvidenceLinks),
            LifecycleTransitions = posted.LifecycleTransitions.Append(transition).ToArray(),
            Reversal = reversal,
            Rebook = rebook
        };
        var correction = posted with
        {
            JournalEntryId = correctionId,
            Status = ManualJournalEntryStatusDto.Draft,
            Memo = string.IsNullOrWhiteSpace(request.Notes)
                ? $"{(reverseSides ? "Reversal" : "Rebook")} for journal entry {posted.JournalEntryId:D}"
                : request.Notes.Trim(),
            CreatedAtUtc = recordedAtUtc,
            UpdatedAtUtc = recordedAtUtc,
            Version = 1,
            PreparedBy = RequireText(request.Actor, nameof(request.Actor)),
            Lines = correctionLines,
            EvidenceLinks = MergeEvidenceLinks(posted.EvidenceLinks, request.EvidenceLinks),
            EvidenceAttachments = NormalizeAttachments(posted.EvidenceAttachments, request.Actor),
            ValidationIssues = [],
            ApprovalId = null,
            SubmittedAtUtc = null,
            SubmittedBy = null,
            EntryType = reverseSides ? ManualJournalEntryTypeDto.Reversal : posted.EntryType,
            LifecycleTransitions = [correctionTransition],
            ReversalOfJournalEntryId = reverseSides ? posted.JournalEntryId : null,
            RebookedFromJournalEntryId = reverseSides ? null : posted.JournalEntryId,
            ApprovedAtUtc = null,
            ApprovedBy = null,
            PostedAtUtc = null,
            PostedBy = null,
            ClosedLockedAtUtc = null,
            CloseLockedBy = null,
            Reversal = reversal,
            Rebook = rebook
        };
        correction = await NormalizeAndValidateAsync(correction, allowIncomplete: false, ct).ConfigureAwait(false);
        if (correction.ValidationIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
        {
            throw new InvalidOperationException("Manual journal reversal and rebook actions cannot transition the posted entry while the generated correction draft has critical validation issues.");
        }

        await _draftStore.SaveAsync(corrected, ct).ConfigureAwait(false);
        await _draftStore.SaveAsync(correction, ct).ConfigureAwait(false);
        await AppendAuditAsync(corrected, reverseSides ? "manual-je.reverse" : "manual-je.rebook", request.Actor, request.CorrelationId, corrected.EvidenceLinks, ct).ConfigureAwait(false);
        await AppendAuditAsync(correction, auditAction, request.Actor, request.CorrelationId, correction.EvidenceLinks, ct).ConfigureAwait(false);
        return new JournalEntryLifecycleActionResultDto(corrected, transition, [correction]);
    }

    private static ManualJournalEntryDraftDto RequireStatus(
        ManualJournalEntryDraftDto draft,
        ManualJournalEntryStatusDto requiredStatus,
        JournalEntryLifecycleActionDto action)
    {
        if (draft.Status != requiredStatus)
        {
            throw new InvalidOperationException($"Manual journal entry lifecycle action '{action}' requires status '{requiredStatus}', but current status is '{draft.Status}'.");
        }

        return draft;
    }

    private static ManualJournalEntryDraftDto RequirePostedForCorrection(
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionDto action)
    {
        if (draft.Status == ManualJournalEntryStatusDto.CloseLocked)
        {
            throw new InvalidOperationException(
                $"Manual journal entry lifecycle action '{action}' cannot change a close-locked journal entry; use governed late-adjustment or restatement workflows.");
        }

        if (draft.Status != ManualJournalEntryStatusDto.Posted)
        {
            throw new InvalidOperationException($"Manual journal entry lifecycle action '{action}' requires a posted journal entry.");
        }

        return draft;
    }

    private static void EnsureLifecycleActorIndependentFromPreparer(
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionRequestDto request)
    {
        if (request.Action is JournalEntryLifecycleActionDto.Validate or JournalEntryLifecycleActionDto.Submit)
        {
            return;
        }

        var actor = RequireText(request.Actor, nameof(request.Actor));
        var preparedBy = NormalizeOptional(draft.PreparedBy);
        if (preparedBy is null)
        {
            return;
        }

        if (string.Equals(actor, preparedBy, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Manual journal lifecycle action '{request.Action}' requires an independent actor; '{actor}' prepared the journal entry and cannot approve, reject, post, close-lock, reverse, or rebook it.");
        }
    }

    private async Task<JournalEntryLifecycleActionResultDto?> TryBuildIdempotentLifecycleResultAsync(
        string fundProfileId,
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionRequestDto request,
        CancellationToken ct)
    {
        var transition = FindIdempotentLifecycleTransition(draft, request.Action, request.Actor, request.CorrelationId);
        if (transition is null)
        {
            return null;
        }

        var generated = await LoadIdempotentCorrectionDraftsAsync(fundProfileId, draft, request.Action, ct).ConfigureAwait(false);
        return new JournalEntryLifecycleActionResultDto(draft, transition, generated);
    }

    private async Task<IReadOnlyList<ManualJournalEntryDraftDto>> LoadIdempotentCorrectionDraftsAsync(
        string fundProfileId,
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionDto action,
        CancellationToken ct)
    {
        var correctionId = action switch
        {
            JournalEntryLifecycleActionDto.Reverse => draft.Reversal?.ReversalJournalEntryId,
            JournalEntryLifecycleActionDto.Rebook => draft.Rebook?.RebookJournalEntryId,
            _ => null
        };
        if (!correctionId.HasValue)
        {
            return [];
        }

        var correction = await _draftStore.GetAsync(fundProfileId, correctionId.Value, ct).ConfigureAwait(false);
        return correction is null ? [] : [correction];
    }

    private static JournalEntryLifecycleTransitionDto? FindIdempotentLifecycleTransition(
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionDto action,
        string? actor,
        string? correlationId)
    {
        var normalizedActor = NormalizeOptional(actor);
        var normalizedCorrelationId = NormalizeOptional(correlationId);
        if (normalizedActor is null || normalizedCorrelationId is null)
        {
            return null;
        }

        return draft.LifecycleTransitions.LastOrDefault(transition =>
            transition.Action == action &&
            string.Equals(transition.Actor, normalizedActor, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(transition.CorrelationId, normalizedCorrelationId, StringComparison.OrdinalIgnoreCase));
    }

    private static JournalEntryLifecycleActionRequestDto RequireLifecycleDecisionNotes(
        JournalEntryLifecycleActionRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Notes))
        {
            throw new InvalidOperationException("Manual journal approval and rejection actions require reviewer notes.");
        }

        return request;
    }

    private static JournalEntryLifecycleActionRequestDto RequirePostingLifecycleNotes(
        JournalEntryLifecycleActionRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Notes))
        {
            throw new InvalidOperationException("Manual journal posting and close-lock actions require operator notes.");
        }

        return request;
    }

    private static JournalEntryLifecycleActionRequestDto RequireApprovalLifecycleEvidence(
        JournalEntryLifecycleActionRequestDto request,
        ManualJournalEntryDraftDto journalEntry)
    {
        if (request.EvidenceLinks.Count == 0)
        {
            throw new InvalidOperationException("Manual journal approval and rejection actions require retained reviewer evidence.");
        }

        if (!HasManualJournalApprovalEvidence(request.EvidenceLinks))
        {
            throw new InvalidOperationException("Manual journal approval and rejection actions require retained approval, rejection, sign-off, or review evidence.");
        }

        if (!HasManualJournalApprovalEvidenceWithProvenance(journalEntry, request.EvidenceLinks))
        {
            throw new InvalidOperationException("Manual journal approval and rejection evidence must reference reviewer intent, the journal entry or accounting period, and the scoped ledger book on the same evidence artifact.");
        }

        return request;
    }

    private static JournalEntryLifecycleActionRequestDto RequirePostingLifecycleEvidence(
        JournalEntryLifecycleActionRequestDto request,
        ManualJournalEntryDraftDto journalEntry)
    {
        if (request.EvidenceLinks.Count == 0)
        {
            throw new InvalidOperationException("Manual journal posting actions require retained posting evidence.");
        }

        if (!HasManualJournalPostingEvidence(request.EvidenceLinks))
        {
            throw new InvalidOperationException("Manual journal posting actions require retained posting, approval, certification, sign-off, or review evidence.");
        }

        if (!HasManualJournalPostingEvidenceWithProvenance(journalEntry, request.EvidenceLinks))
        {
            throw new InvalidOperationException("Manual journal posting evidence must reference posting intent, the journal entry or accounting period, and the scoped ledger book on the same evidence artifact.");
        }

        return request;
    }

    private static JournalEntryLifecycleActionRequestDto RequireCloseLockLifecycleEvidence(
        JournalEntryLifecycleActionRequestDto request,
        ManualJournalEntryDraftDto journalEntry)
    {
        if (request.EvidenceLinks.Count == 0)
        {
            throw new InvalidOperationException("Manual journal close-lock actions require retained close evidence.");
        }

        if (!HasManualJournalCloseLockEvidence(request.EvidenceLinks))
        {
            throw new InvalidOperationException("Manual journal close-lock actions require retained close, period-lock, sign-off, certification, approval, or review evidence.");
        }

        if (!HasManualJournalCloseLockEvidenceWithProvenance(journalEntry, request.EvidenceLinks))
        {
            throw new InvalidOperationException("Manual journal close-lock evidence must reference close-lock intent, the journal entry or accounting period, and the scoped ledger book on the same evidence artifact.");
        }

        return request;
    }

    private static bool HasManualJournalLifecycleEvidenceProvenance(
        ManualJournalEntryDraftDto journalEntry,
        IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(link =>
            HasManualJournalEntryOrPeriodProvenance(journalEntry, link) &&
            HasManualJournalLedgerBookProvenance(journalEntry, link));

    private static bool HasManualJournalEntryOrPeriodProvenance(
        ManualJournalEntryDraftDto journalEntry,
        string evidenceLink)
        => evidenceLink.Contains(journalEntry.JournalEntryId.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
           evidenceLink.Contains(journalEntry.JournalEntryId.ToString("N"), StringComparison.OrdinalIgnoreCase) ||
           (!string.IsNullOrWhiteSpace(journalEntry.PeriodId) &&
               evidenceLink.Contains(journalEntry.PeriodId, StringComparison.OrdinalIgnoreCase));

    private static bool HasManualJournalLedgerBookProvenance(
        ManualJournalEntryDraftDto journalEntry,
        string evidenceLink)
    {
        if (journalEntry.LedgerBookId is not { } ledgerBookId)
        {
            return true;
        }

        return evidenceLink.Contains(ledgerBookId.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
               evidenceLink.Contains(ledgerBookId.ToString("N"), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasManualJournalCorrectionEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(static link =>
            link.Contains("reversal", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("reverse", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("rebook", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("correction", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static bool HasManualJournalApprovalEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(static link =>
            link.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("approved", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("rejection", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("rejected", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("sign-off", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("signoff", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static bool HasManualJournalPostingEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(static link =>
            link.Contains("posting", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("posted", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("ledger-post", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("certification", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("sign-off", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("signoff", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static bool HasManualJournalCloseLockEvidence(IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(static link =>
            link.Contains("close", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("period-lock", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("periodlock", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("sign-off", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("signoff", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("certification", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static bool HasManualJournalCorrectionEvidenceWithProvenance(
        ManualJournalEntryDraftDto journalEntry,
        IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(link =>
            HasManualJournalCorrectionEvidence([link]) &&
            HasManualJournalLifecycleEvidenceProvenance(journalEntry, [link]));

    private static bool HasManualJournalApprovalEvidenceWithProvenance(
        ManualJournalEntryDraftDto journalEntry,
        IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(link =>
            HasManualJournalApprovalEvidence([link]) &&
            HasManualJournalLifecycleEvidenceProvenance(journalEntry, [link]));

    private static bool HasManualJournalPostingEvidenceWithProvenance(
        ManualJournalEntryDraftDto journalEntry,
        IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(link =>
            HasManualJournalPostingEvidence([link]) &&
            HasManualJournalLifecycleEvidenceProvenance(journalEntry, [link]));

    private static bool HasManualJournalCloseLockEvidenceWithProvenance(
        ManualJournalEntryDraftDto journalEntry,
        IReadOnlyList<string> evidenceLinks)
        => evidenceLinks.Any(link =>
            HasManualJournalCloseLockEvidence([link]) &&
            HasManualJournalLifecycleEvidenceProvenance(journalEntry, [link]));

    private static AccountingTemplateLineSideDto ReverseSide(AccountingTemplateLineSideDto side)
        => side == AccountingTemplateLineSideDto.Debit
            ? AccountingTemplateLineSideDto.Credit
            : AccountingTemplateLineSideDto.Debit;

    private static JournalEntryLifecycleTransitionDto BuildTransition(
        ManualJournalEntryStatusDto fromStatus,
        ManualJournalEntryStatusDto toStatus,
        JournalEntryLifecycleActionRequestDto request,
        DateTimeOffset recordedAtUtc)
        => new(
            $"manual-je-transition-{Guid.NewGuid():N}",
            fromStatus,
            toStatus,
            request.Action,
            RequireText(request.Actor, nameof(request.Actor)),
            recordedAtUtc,
            NormalizeOptional(request.Notes),
            NormalizeOptional(request.CorrelationId),
            request.EvidenceLinks);

    private static JournalEntryLifecycleTransitionDto BuildSubmitTransition(
        ManualJournalEntryStatusDto fromStatus,
        SubmitManualJournalEntryApprovalRequest request,
        DateTimeOffset recordedAtUtc)
        => new(
            $"manual-je-transition-{Guid.NewGuid():N}",
            fromStatus,
            ManualJournalEntryStatusDto.Submitted,
            JournalEntryLifecycleActionDto.Submit,
            RequireText(request.Actor, nameof(request.Actor)),
            recordedAtUtc,
            NormalizeOptional(request.Notes),
            NormalizeOptional(request.CorrelationId),
            request.EvidenceLinks);

    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin, string action)
    {
        if (actionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new InvalidOperationException(
                $"Reviewed automation cannot {action}; a human operator approval is required.");
        }
    }

    private static void EnsurePeriodUnlocked(bool periodIsLocked, string action)
    {
        if (periodIsLocked)
        {
            throw new InvalidOperationException(
                $"Cannot {action} because the accounting period is locked after close.");
        }
    }

    private static void EnsureRequestedLedgerBookMatchesDraft(
        Guid? requestedLedgerBookId,
        ManualJournalEntryDraftDto draft,
        bool requireScopeForBookScopedDraft = false)
    {
        if (!requestedLedgerBookId.HasValue)
        {
            if (requireScopeForBookScopedDraft && draft.LedgerBookId.HasValue)
            {
                throw new InvalidOperationException(
                    $"Manual journal entry '{draft.JournalEntryId:D}' belongs to ledger book '{draft.LedgerBookId.Value:D}', but the request was not scoped to a ledger book.");
            }

            return;
        }

        if (draft.LedgerBookId == requestedLedgerBookId.Value)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Manual journal entry '{draft.JournalEntryId:D}' belongs to ledger book '{draft.LedgerBookId?.ToString("D") ?? "unscoped"}', not requested ledger book '{requestedLedgerBookId.Value:D}'.");
    }

    private static IReadOnlyDictionary<string, ChartOfAccountsNodeDto> BuildChartByPath(IReadOnlyList<ChartOfAccountsNodeDto> chart)
        => chart
            .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static item => item.IsArchived).First(),
                StringComparer.OrdinalIgnoreCase);

    private async Task<ManualJournalEntryDraftDto> NormalizeAndValidateAsync(
        ManualJournalEntryDraftDto draft,
        bool allowIncomplete,
        CancellationToken ct,
        bool periodIsLocked = false)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var fundProfileId = NormalizeFundProfileId(draft.FundProfileId);
        var configuration = await _configurationService.GetWorkspaceAsync(fundProfileId, draft.LedgerBookId, ct).ConfigureAwait(false);
        var chartByPath = BuildChartByPath(configuration.ChartOfAccounts);
        var issues = new List<AccountingConfigurationValidationIssueDto>();
        var lines = new List<ManualJournalEntryLineDto>(draft.Lines.Count);
        var attachments = NormalizeAttachments(draft.EvidenceAttachments, draft.PreparedBy);
        var evidenceLinks = MergeEvidenceLinks(draft.EvidenceLinks, attachments.Select(item => item.Uri).ToArray());
        var entryType = Enum.IsDefined(draft.EntryType)
            ? draft.EntryType
            : ManualJournalEntryTypeDto.General;
        var treasuryContext = NormalizeTreasuryContext(draft.TreasuryContext, draft.AccountingDate);
        var headerDimensions = NormalizeDimensionSet(
            draft.Dimensions,
            fundId: draft.FundNodeId ?? fundProfileId,
            entityId: draft.EntityId,
            investorId: treasuryContext?.InvestorId,
            capitalAccountId: treasuryContext?.CapitalAccountId);

        if (!Enum.IsDefined(draft.EntryType))
        {
            issues.Add(Issue("manual-je.entry-type-invalid", AccountingConfigurationValidationSeverityDto.Critical, "Manual journal entry type is not supported.", "entryType", "Select a supported entry type before submitting approval."));
        }

        if (periodIsLocked)
        {
            issues.Add(Issue(
                "manual-je.period-locked",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Manual journal entry is in a locked accounting period.",
                draft.PeriodId ?? draft.AccountingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "Create a reversal or rebook workflow in an unlocked adjustment period."));
        }

        if (!draft.LedgerBookId.HasValue)
        {
            issues.Add(Issue("manual-je.book-missing", AccountingConfigurationValidationSeverityDto.Critical, "Ledger book is required before a manual journal entry can be approved.", "ledgerBookId", "Select the book that owns this journal entry."));
        }
        else
        {
            await ValidateLedgerBookPeriodScopeAsync(
                draft,
                draft.LedgerBookId.Value,
                issues,
                ct).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(draft.Currency))
        {
            issues.Add(Issue("manual-je.currency-missing", AccountingConfigurationValidationSeverityDto.Critical, "Journal currency is required.", "currency", "Select the journal entry currency."));
        }

        if (!allowIncomplete && draft.Lines.Count < 2)
        {
            issues.Add(Issue("manual-je.lines-minimum", AccountingConfigurationValidationSeverityDto.Critical, "At least two journal lines are required for approval submission.", "lines", "Add debit and credit lines."));
        }

        ValidateRequiredDimensions(headerDimensions, allowIncomplete, "manual-je.dimensions", issues);

        if (!allowIncomplete && evidenceLinks.Count == 0)
        {
            issues.Add(Issue("manual-je.evidence-missing", AccountingConfigurationValidationSeverityDto.Critical, "At least one source document or evidence link is required before approval submission.", "evidence", "Attach source support or link retained evidence before submitting approval."));
        }

        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.DisplayName) || string.IsNullOrWhiteSpace(attachment.Uri))
            {
                issues.Add(Issue("manual-je.evidence-invalid", AccountingConfigurationValidationSeverityDto.Critical, "Evidence attachments require a display name and route or path.", attachment.AttachmentId, "Complete the attachment label and evidence route before submitting approval."));
            }
        }

        foreach (var line in draft.Lines)
        {
            var lineDimensions = NormalizeDimensionSet(
                line.Dimensions,
                fundId: headerDimensions?.FundId,
                entityId: line.EntityId ?? headerDimensions?.EntityId,
                investorId: headerDimensions?.InvestorId,
                capitalAccountId: headerDimensions?.CapitalAccountId,
                instrumentId: line.SecurityId ?? headerDimensions?.InstrumentId,
                taxLotId: line.TaxLotId ?? headerDimensions?.TaxLotId,
                costCenterId: headerDimensions?.CostCenterId,
                counterpartyId: headerDimensions?.CounterpartyId,
                fallbackExternalDimensions: headerDimensions?.ExternalGlDimensions);
            var normalizedLine = line with
            {
                LineId = string.IsNullOrWhiteSpace(line.LineId) ? Guid.NewGuid().ToString("N") : line.LineId.Trim(),
                Currency = string.IsNullOrWhiteSpace(line.Currency) ? draft.Currency : line.Currency.Trim(),
                AccountPath = line.AccountPath?.Trim() ?? string.Empty,
                Description = NormalizeOptional(line.Description),
                EvidenceLink = NormalizeOptional(line.EvidenceLink),
                EntityId = NormalizeOptional(line.EntityId) ?? lineDimensions?.EntityId,
                FundAllocationId = NormalizeOptional(line.FundAllocationId),
                SecurityDisplayName = NormalizeOptional(line.SecurityDisplayName),
                TaxLotId = NormalizeOptional(line.TaxLotId) ?? lineDimensions?.TaxLotId,
                Dimensions = lineDimensions
            };

            if (normalizedLine.Amount <= 0m)
            {
                issues.Add(Issue("manual-je.line-amount", AccountingConfigurationValidationSeverityDto.Critical, "Manual journal lines must use positive amounts.", normalizedLine.LineId, "Enter a positive debit or credit amount."));
            }

            if (!chartByPath.TryGetValue(normalizedLine.AccountPath, out var account))
            {
                issues.Add(Issue("manual-je.account-missing", AccountingConfigurationValidationSeverityDto.Critical, $"GL account '{normalizedLine.AccountPath}' was not found.", normalizedLine.LineId, "Choose an active GL account from accounting configuration."));
            }
            else if (account.IsArchived)
            {
                issues.Add(Issue("manual-je.account-archived", AccountingConfigurationValidationSeverityDto.Critical, $"GL account '{account.Path}' is archived.", normalizedLine.LineId, "Choose an active GL account."));
            }

            if (!string.Equals(normalizedLine.Currency, draft.Currency, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(Issue("manual-je.currency-mismatch", AccountingConfigurationValidationSeverityDto.Critical, "Line currency must match the journal entry currency for approval submission.", normalizedLine.LineId, "Use one currency per manual JE draft."));
            }

            if (normalizedLine.SecurityId.HasValue && !await SecurityExistsAsync(normalizedLine.SecurityId.Value, ct).ConfigureAwait(false))
            {
                issues.Add(Issue("manual-je.security-missing", AccountingConfigurationValidationSeverityDto.Critical, $"Security Master id '{normalizedLine.SecurityId:D}' was not found.", normalizedLine.LineId, "Choose a resolved Security Master instrument or clear the line security."));
            }

            ValidateRequiredDimensions(lineDimensions, allowIncomplete, normalizedLine.LineId, issues);

            lines.Add(normalizedLine);
        }

        ValidateTreasuryContext(treasuryContext, entryType, allowIncomplete, issues);

        var totalDebits = lines.Where(line => line.Side == AccountingTemplateLineSideDto.Debit).Sum(line => line.Amount);
        var totalCredits = lines.Where(line => line.Side == AccountingTemplateLineSideDto.Credit).Sum(line => line.Amount);
        var imbalance = totalDebits - totalCredits;
        if (imbalance != 0m)
        {
            issues.Add(Issue("manual-je.unbalanced", AccountingConfigurationValidationSeverityDto.Critical, $"Manual journal entry is unbalanced: debits={totalDebits}, credits={totalCredits}.", draft.JournalEntryId.ToString("D"), "Adjust lines so debits equal credits."));
        }

        return draft with
        {
            JournalEntryId = draft.JournalEntryId == Guid.Empty ? Guid.NewGuid() : draft.JournalEntryId,
            FundProfileId = fundProfileId,
            Currency = string.IsNullOrWhiteSpace(draft.Currency) ? "USD" : draft.Currency.Trim().ToUpperInvariant(),
            Memo = draft.Memo?.Trim() ?? string.Empty,
            PreparedBy = NormalizeOptional(draft.PreparedBy) ?? "unknown",
            Lines = lines,
            EvidenceLinks = evidenceLinks,
            EvidenceAttachments = attachments,
            TotalDebits = totalDebits,
            TotalCredits = totalCredits,
            Imbalance = imbalance,
            EntryType = entryType,
            TreasuryContext = treasuryContext,
            Dimensions = headerDimensions,
            ValidationIssues = issues.OrderByDescending(issue => issue.Severity).ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private async Task ValidateLedgerBookPeriodScopeAsync(
        ManualJournalEntryDraftDto draft,
        Guid ledgerBookId,
        List<AccountingConfigurationValidationIssueDto> issues,
        CancellationToken ct)
    {
        if (_journalStore is null ||
            string.IsNullOrWhiteSpace(draft.PeriodId) ||
            !Guid.TryParse(draft.PeriodId, out var periodId))
        {
            return;
        }

        var period = await _journalStore.GetPeriodAsync(periodId, ct).ConfigureAwait(false);
        if (period is null)
        {
            issues.Add(Issue(
                "manual-je.period-missing",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Ledger period '{periodId:D}' was not found.",
                "periodId",
                "Select an accounting period from the active ledger book before approval submission."));
            return;
        }

        if (period.LedgerBookId != ledgerBookId)
        {
            issues.Add(Issue(
                "manual-je.period-book-mismatch",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Ledger period '{periodId:D}' belongs to ledger book '{period.LedgerBookId?.ToString("D") ?? "unscoped"}', not '{ledgerBookId:D}'.",
                "periodId",
                "Select a period that belongs to the journal entry ledger book."));
        }

        if (!string.Equals(period.Status, "Open", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Issue(
                "manual-je.period-closed",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Ledger period '{periodId:D}' is {period.Status} and cannot accept manual journal approval workflow changes.",
                "periodId",
                "Create the journal entry in an open adjustment period or use governed late-adjustment workflow."));
        }
    }

    private async Task<bool> SecurityExistsAsync(Guid securityId, CancellationToken ct)
    {
        if (_securityMasterQueryService is null)
        {
            return true;
        }

        var detail = await _securityMasterQueryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
        return detail is not null;
    }

    private async Task AppendAuditAsync(
        ManualJournalEntryDraftDto draft,
        string action,
        string actor,
        string? correlationId,
        IReadOnlyList<string> evidenceLinks,
        CancellationToken ct)
    {
        var hash = Hash(draft);
        await _auditStore.AppendAsync(
            new AccountingActionAuditEventDto(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                RequireText(actor, nameof(actor)),
                action,
                draft.FundProfileId,
                draft.LedgerBookId,
                NormalizeOptional(correlationId),
                hash,
                hash,
                draft.ValidationIssues,
                evidenceLinks),
            ct).ConfigureAwait(false);
    }

    private static AccountingConfigurationValidationIssueDto Issue(
        string code,
        AccountingConfigurationValidationSeverityDto severity,
        string message,
        string? targetId,
        string? suggestedAction)
        => new(code, severity, message, targetId, suggestedAction);

    private static string Hash(ManualJournalEntryDraftDto draft)
    {
        var json = JsonSerializer.Serialize(draft);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static IReadOnlyList<string> MergeEvidenceLinks(
        IReadOnlyList<string> existing,
        IReadOnlyList<string>? incoming)
        => existing.Concat(incoming ?? [])
            .Where(link => !string.IsNullOrWhiteSpace(link))
            .Select(link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<ManualJournalEntryEvidenceAttachmentDto> NormalizeAttachments(
        IReadOnlyList<ManualJournalEntryEvidenceAttachmentDto>? attachments,
        string? fallbackActor)
    {
        var actor = NormalizeOptional(fallbackActor) ?? "unknown";
        return (attachments ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.DisplayName) || !string.IsNullOrWhiteSpace(item.Uri))
            .Select(item => item with
            {
                AttachmentId = string.IsNullOrWhiteSpace(item.AttachmentId) ? Guid.NewGuid().ToString("N") : item.AttachmentId.Trim(),
                DisplayName = item.DisplayName?.Trim() ?? string.Empty,
                EvidenceKind = string.IsNullOrWhiteSpace(item.EvidenceKind) ? "SourceDocument" : item.EvidenceKind.Trim(),
                Uri = item.Uri?.Trim() ?? string.Empty,
                SourceSystem = string.IsNullOrWhiteSpace(item.SourceSystem) ? "ManualUpload" : item.SourceSystem.Trim(),
                AddedAtUtc = item.AddedAtUtc == default ? DateTimeOffset.UtcNow : item.AddedAtUtc,
                AddedBy = string.IsNullOrWhiteSpace(item.AddedBy) ? actor : item.AddedBy.Trim(),
                LineId = NormalizeOptional(item.LineId),
                Description = NormalizeOptional(item.Description)
            })
            .GroupBy(item => item.AttachmentId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.AddedAtUtc)
            .ThenBy(item => item.AttachmentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static TreasuryLedgerContextDto? NormalizeTreasuryContext(
        TreasuryLedgerContextDto? context,
        DateOnly accountingDate)
    {
        if (context is null)
        {
            return null;
        }

        return context with
        {
            EffectiveDate = context.EffectiveDate ?? accountingDate,
            IdempotencyKey = NormalizeOptional(context.IdempotencyKey),
            FundEventId = NormalizeOptional(context.FundEventId),
            FundEventType = NormalizeOptional(context.FundEventType),
            CapitalAccountId = NormalizeOptional(context.CapitalAccountId),
            InvestorId = NormalizeOptional(context.InvestorId),
            PaymentIntentId = NormalizeOptional(context.PaymentIntentId),
            SettlementReference = NormalizeOptional(context.SettlementReference)
        };
    }

    private static LedgerDimensionSetDto? NormalizeDimensionSet(
        LedgerDimensionSetDto? dimensions,
        string? fundId = null,
        string? entityId = null,
        string? sleeveId = null,
        string? strategyId = null,
        string? investorId = null,
        string? capitalAccountId = null,
        Guid? instrumentId = null,
        string? taxLotId = null,
        string? costCenterId = null,
        string? counterpartyId = null,
        IReadOnlyDictionary<string, string>? fallbackExternalDimensions = null)
    {
        var externalDimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in fallbackExternalDimensions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            var key = NormalizeOptional(item.Key);
            var value = NormalizeOptional(item.Value);
            if (key is not null && value is not null)
            {
                externalDimensions[key] = value;
            }
        }

        foreach (var item in dimensions?.ExternalGlDimensions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            var key = NormalizeOptional(item.Key);
            var value = NormalizeOptional(item.Value);
            if (key is not null && value is not null)
            {
                externalDimensions[key] = value;
            }
        }

        var normalized = new LedgerDimensionSetDto(
            FundId: NormalizeOptional(dimensions?.FundId) ?? NormalizeOptional(fundId),
            EntityId: NormalizeOptional(dimensions?.EntityId) ?? NormalizeOptional(entityId),
            SleeveId: NormalizeOptional(dimensions?.SleeveId) ?? NormalizeOptional(sleeveId),
            StrategyId: NormalizeOptional(dimensions?.StrategyId) ?? NormalizeOptional(strategyId),
            InvestorId: NormalizeOptional(dimensions?.InvestorId) ?? NormalizeOptional(investorId),
            CapitalAccountId: NormalizeOptional(dimensions?.CapitalAccountId) ?? NormalizeOptional(capitalAccountId),
            InstrumentId: dimensions?.InstrumentId ?? instrumentId,
            TaxLotId: NormalizeOptional(dimensions?.TaxLotId) ?? NormalizeOptional(taxLotId),
            CostCenterId: NormalizeOptional(dimensions?.CostCenterId) ?? NormalizeOptional(costCenterId),
            CounterpartyId: NormalizeOptional(dimensions?.CounterpartyId) ?? NormalizeOptional(counterpartyId),
            ExternalGlDimensions: externalDimensions
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase));

        return HasAnyDimension(normalized) ? normalized : null;
    }

    private static bool HasAnyDimension(LedgerDimensionSetDto dimensions)
        => !string.IsNullOrWhiteSpace(dimensions.FundId) ||
           !string.IsNullOrWhiteSpace(dimensions.EntityId) ||
           !string.IsNullOrWhiteSpace(dimensions.SleeveId) ||
           !string.IsNullOrWhiteSpace(dimensions.StrategyId) ||
           !string.IsNullOrWhiteSpace(dimensions.InvestorId) ||
           !string.IsNullOrWhiteSpace(dimensions.CapitalAccountId) ||
           dimensions.InstrumentId.HasValue ||
           !string.IsNullOrWhiteSpace(dimensions.TaxLotId) ||
           !string.IsNullOrWhiteSpace(dimensions.CostCenterId) ||
           !string.IsNullOrWhiteSpace(dimensions.CounterpartyId) ||
           dimensions.ExternalGlDimensions.Count > 0;

    private static void ValidateRequiredDimensions(
        LedgerDimensionSetDto? dimensions,
        bool allowIncomplete,
        string targetId,
        List<AccountingConfigurationValidationIssueDto> issues)
    {
        var severity = allowIncomplete
            ? AccountingConfigurationValidationSeverityDto.Warning
            : AccountingConfigurationValidationSeverityDto.Critical;
        if (string.IsNullOrWhiteSpace(dimensions?.FundId))
        {
            issues.Add(Issue(
                "manual-je.dimension-fund-missing",
                severity,
                "Manual journal entry requires a fund dimension.",
                targetId,
                "Attach the fund dimension before approval submission."));
        }

        if (string.IsNullOrWhiteSpace(dimensions?.EntityId))
        {
            issues.Add(Issue(
                "manual-je.dimension-entity-missing",
                severity,
                "Manual journal entry requires an entity dimension.",
                targetId,
                "Attach the legal entity dimension before approval submission."));
        }
    }

    private static void ValidateTreasuryContext(
        TreasuryLedgerContextDto? context,
        ManualJournalEntryTypeDto entryType,
        bool allowIncomplete,
        List<AccountingConfigurationValidationIssueDto> issues)
    {
        var requiresPrivateCapitalContext = RequiresPrivateCapitalTreasuryContext(entryType);
        if (allowIncomplete && context is null)
        {
            return;
        }

        if (context is null)
        {
            if (requiresPrivateCapitalContext)
            {
                issues.Add(Issue("manual-je.treasury-context-missing", AccountingConfigurationValidationSeverityDto.Critical, "Private-capital manual journal entries require treasury ledger context before approval submission.", "treasuryContext", "Attach effective date, idempotency, fund event, and capital account context."));
            }

            return;
        }

        var hasAnyContext =
            context.EffectiveDate is not null ||
            !string.IsNullOrWhiteSpace(context.IdempotencyKey) ||
            !string.IsNullOrWhiteSpace(context.FundEventId) ||
            !string.IsNullOrWhiteSpace(context.FundEventType) ||
            !string.IsNullOrWhiteSpace(context.CapitalAccountId) ||
            !string.IsNullOrWhiteSpace(context.InvestorId) ||
            !string.IsNullOrWhiteSpace(context.PaymentIntentId) ||
            !string.IsNullOrWhiteSpace(context.SettlementReference);
        if (allowIncomplete && !hasAnyContext)
        {
            return;
        }

        if (!allowIncomplete && context.EffectiveDate is null)
        {
            issues.Add(Issue("manual-je.treasury-effective-date-missing", AccountingConfigurationValidationSeverityDto.Critical, "Treasury ledger context requires an effective date before approval submission.", "treasuryContext.effectiveDate", "Set the business-effective date used for balance reconstruction."));
        }

        if (!allowIncomplete && string.IsNullOrWhiteSpace(context.IdempotencyKey))
        {
            issues.Add(Issue("manual-je.treasury-idempotency-missing", AccountingConfigurationValidationSeverityDto.Critical, "Treasury ledger context requires an idempotency key before approval submission.", "treasuryContext.idempotencyKey", "Use a stable source-event key for retry-safe posting."));
        }

        var hasFundEventContext = requiresPrivateCapitalContext ||
            !string.IsNullOrWhiteSpace(context.FundEventId) ||
            !string.IsNullOrWhiteSpace(context.FundEventType) ||
            !string.IsNullOrWhiteSpace(context.CapitalAccountId) ||
            !string.IsNullOrWhiteSpace(context.InvestorId);
        if (!allowIncomplete && hasFundEventContext)
        {
            RequireTreasuryContextText(issues, context.FundEventId, "manual-je.fund-event-missing", "fund event id", "treasuryContext.fundEventId");
            RequireTreasuryContextText(issues, context.FundEventType, "manual-je.fund-event-type-missing", "fund event type", "treasuryContext.fundEventType");
            RequireTreasuryContextText(issues, context.CapitalAccountId, "manual-je.capital-account-missing", "capital account id", "treasuryContext.capitalAccountId");
        }
    }

    private static bool RequiresPrivateCapitalTreasuryContext(ManualJournalEntryTypeDto entryType)
        => entryType is ManualJournalEntryTypeDto.CapitalCall
            or ManualJournalEntryTypeDto.Distribution
            or ManualJournalEntryTypeDto.Subscription
            or ManualJournalEntryTypeDto.Redemption
            or ManualJournalEntryTypeDto.LpTransfer
            or ManualJournalEntryTypeDto.ManagementFee;

    private static bool CanSubmitManualJournalEntry(ManualJournalEntryStatusDto status)
        => status is ManualJournalEntryStatusDto.Draft
            or ManualJournalEntryStatusDto.NeedsFix
            or ManualJournalEntryStatusDto.Rejected;

    private static void RequireTreasuryContextText(
        List<AccountingConfigurationValidationIssueDto> issues,
        string? value,
        string code,
        string label,
        string targetId)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        issues.Add(Issue(
            code,
            AccountingConfigurationValidationSeverityDto.Critical,
            $"Treasury ledger context requires {label} before approval submission.",
            targetId,
            $"Attach {label} to the retained fund-event evidence."));
    }

    private static string NormalizeFundProfileId(string? value)
        => string.IsNullOrWhiteSpace(value) ? DefaultFundProfileId : value.Trim();

    private static string NormalizeCurrency(string? value)
        => string.IsNullOrWhiteSpace(value) ? "USD" : value.Trim().ToUpperInvariant();

    private static string RequireText(string? value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
