using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Archival;

namespace Meridian.Ui.Shared.Services;

public sealed class InMemoryAccountingConfigurationStore : IAccountingConfigurationStore
{
    private readonly Dictionary<string, AccountingConfigurationWorkspaceDto> _workspaces = new(StringComparer.OrdinalIgnoreCase);

    public Task<AccountingConfigurationWorkspaceDto?> GetAsync(string fundProfileId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _workspaces.TryGetValue(NormalizeFundProfileId(fundProfileId), out var workspace);
        return Task.FromResult(workspace);
    }

    public Task SaveAsync(AccountingConfigurationWorkspaceDto workspace, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(workspace);
        _workspaces[NormalizeFundProfileId(workspace.FundProfileId)] = workspace;
        return Task.CompletedTask;
    }

    private static string NormalizeFundProfileId(string value)
        => string.IsNullOrWhiteSpace(value) ? "default-fund" : value.Trim();
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
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var events = _events
            .Where(item => string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(item.FundProfileId, fundProfileId.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId)
            .OrderByDescending(item => item.RecordedAtUtc)
            .ThenBy(item => item.AuditEventId)
            .ToArray();

        return Task.FromResult<IReadOnlyList<AccountingActionAuditEventDto>>(events);
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
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var workspace = await LoadWorkspaceAsync(normalizedFundProfileId, ledgerBookId, ct).ConfigureAwait(false);
        var ledgerBooks = await LoadLedgerBooksAsync(normalizedFundProfileId, ledgerBookId, ct).ConfigureAwait(false);
        var audit = await _auditStore.ListAsync(normalizedFundProfileId, ledgerBookId, ct).ConfigureAwait(false);
        var validation = Validate(workspace with { LedgerBooks = ledgerBooks });

        return workspace with
        {
            LedgerBookId = ledgerBookId ?? workspace.LedgerBookId,
            LedgerBooks = ledgerBooks,
            ValidationIssues = validation,
            AuditTrail = audit
        };
    }

    public async Task<AccountingConfigurationWorkspaceDto> UpsertChartNodeAsync(
        UpsertChartOfAccountsNodeRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await LoadWorkspaceAsync(NormalizeFundProfileId(request.FundProfileId), ledgerBookId: null, ct).ConfigureAwait(false);
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

        return await SaveWithAuditAsync(workspace, beforeHash, request.Actor, "chart.upsert", null, request.CorrelationId, request.EvidenceLinks, ct).ConfigureAwait(false);
    }

    public async Task<AccountingConfigurationWorkspaceDto> UpsertTemplateAsync(
        UpsertJournalEntryTemplateRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await LoadWorkspaceAsync(NormalizeFundProfileId(request.FundProfileId), ledgerBookId: null, ct).ConfigureAwait(false);
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

        return await SaveWithAuditAsync(workspace, beforeHash, request.Actor, "template.upsert", null, request.CorrelationId, request.EvidenceLinks, ct).ConfigureAwait(false);
    }

    public async Task<AccountingConfigurationWorkspaceDto> UpsertPostingRuleAsync(
        UpsertPostingRuleRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await LoadWorkspaceAsync(NormalizeFundProfileId(request.FundProfileId), ledgerBookId: null, ct).ConfigureAwait(false);
        var beforeHash = Hash(workspace);
        RequireText(request.Rule.RuleId, nameof(request.Rule.RuleId));
        RequireText(request.Rule.SourceEventType, nameof(request.Rule.SourceEventType));
        RequireText(request.Rule.TemplateId, nameof(request.Rule.TemplateId));

        var rules = workspace.PostingRules
            .Where(item => !string.Equals(item.RuleId, request.Rule.RuleId, StringComparison.OrdinalIgnoreCase))
            .Append(request.Rule)
            .OrderBy(item => item.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        workspace = workspace with
        {
            Status = AccountingConfigurationStatusDto.Draft,
            PostingRules = rules,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        return await SaveWithAuditAsync(workspace, beforeHash, request.Actor, "posting-rule.upsert", null, request.CorrelationId, request.EvidenceLinks, ct).ConfigureAwait(false);
    }

    public async Task<AccountingJournalTemplatePreviewDto> PreviewTemplateAsync(
        PreviewJournalTemplateRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await LoadWorkspaceAsync(NormalizeFundProfileId(request.FundProfileId), request.LedgerBookId, ct).ConfigureAwait(false);
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

        var chartByPath = workspace.ChartOfAccounts.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
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

    public async Task<AccountingConfigurationWorkspaceDto> ActivateAsync(
        ActivateAccountingConfigurationRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var workspace = await LoadWorkspaceAsync(NormalizeFundProfileId(request.FundProfileId), request.LedgerBookId, ct).ConfigureAwait(false);
        var beforeHash = Hash(workspace);
        var issues = Validate(workspace);
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

        return await SaveWithAuditAsync(workspace, beforeHash, request.Actor, "configuration.activate", request.LedgerBookId, request.CorrelationId, request.EvidenceLinks, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAuditAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default)
        => _auditStore.ListAsync(NormalizeFundProfileId(fundProfileId), ledgerBookId, ct);

    private async Task<AccountingConfigurationWorkspaceDto> SaveWithAuditAsync(
        AccountingConfigurationWorkspaceDto workspace,
        string beforeHash,
        string actor,
        string action,
        Guid? ledgerBookId,
        string? correlationId,
        IReadOnlyList<string>? evidenceLinks,
        CancellationToken ct)
    {
        var validation = Validate(workspace);
        var finalWorkspace = workspace with { ValidationIssues = validation };
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
                evidenceLinks ?? []),
            ct).ConfigureAwait(false);

        return await GetWorkspaceAsync(finalWorkspace.FundProfileId, ledgerBookId ?? finalWorkspace.LedgerBookId, ct).ConfigureAwait(false);
    }

    private async Task<AccountingConfigurationWorkspaceDto> LoadWorkspaceAsync(
        string fundProfileId,
        Guid? ledgerBookId,
        CancellationToken ct)
    {
        var workspace = await _store.GetAsync(fundProfileId, ct).ConfigureAwait(false);
        if (workspace is not null)
        {
            return workspace with { LedgerBookId = ledgerBookId ?? workspace.LedgerBookId };
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
            AuditTrail: []);
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

        return ledgerBookId.HasValue
            ? books.Where(book => book.LedgerBookId == ledgerBookId.Value).ToArray()
            : books;
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> Validate(AccountingConfigurationWorkspaceDto workspace)
    {
        var issues = new List<AccountingConfigurationValidationIssueDto>();
        if (workspace.ChartOfAccounts.Count == 0)
        {
            issues.Add(Issue("chart.empty", AccountingConfigurationValidationSeverityDto.Critical, "No chart-of-accounts nodes are configured.", null, "Create at least one account node."));
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
            if (!templateIds.Contains(rule.TemplateId))
            {
                issues.Add(Issue("posting-rule.template-missing", AccountingConfigurationValidationSeverityDto.Critical, $"Posting rule '{rule.RuleId}' references missing template '{rule.TemplateId}'.", rule.RuleId, "Point the rule at an active journal template."));
            }
        }

        return issues.OrderByDescending(issue => issue.Severity).ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase).ToArray();
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

    private static string NormalizeFundProfileId(string? value)
        => string.IsNullOrWhiteSpace(value) ? DefaultFundProfileId : value.Trim();

    private static string RequireText(string? value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class InMemoryManualJournalEntryDraftStore : IManualJournalEntryDraftStore
{
    private readonly Dictionary<string, ManualJournalEntryDraftDto> _drafts = new(StringComparer.OrdinalIgnoreCase);

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

public sealed class ManualJournalEntryWorkbenchService : IManualJournalEntryWorkbenchService
{
    private const string DefaultFundProfileId = "default-fund";

    private readonly IManualJournalEntryDraftStore _draftStore;
    private readonly IAccountingConfigurationService _configurationService;
    private readonly IAccountingActionAuditStore _auditStore;
    private readonly ISecurityMasterQueryService? _securityMasterQueryService;

    public ManualJournalEntryWorkbenchService(
        IManualJournalEntryDraftStore draftStore,
        IAccountingConfigurationService configurationService,
        IAccountingActionAuditStore auditStore,
        ISecurityMasterQueryService? securityMasterQueryService = null)
    {
        _draftStore = draftStore ?? throw new ArgumentNullException(nameof(draftStore));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _securityMasterQueryService = securityMasterQueryService;
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

        return new ManualJournalEntryWorkbenchDto(
            normalizedFundProfileId,
            ledgerBookId,
            DateTimeOffset.UtcNow,
            configuration.LedgerBooks,
            configuration.ChartOfAccounts,
            drafts,
            audit);
    }

    public async Task<ManualJournalEntryDraftDto> SaveDraftAsync(
        SaveManualJournalEntryDraftRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var normalizedDraft = await NormalizeAndValidateAsync(request.Draft, allowIncomplete: true, ct).ConfigureAwait(false);
        var existing = await _draftStore.GetAsync(normalizedDraft.FundProfileId, normalizedDraft.JournalEntryId, ct).ConfigureAwait(false);
        if (existing is not null && existing.Version != request.Draft.Version)
        {
            throw new InvalidOperationException("Manual journal entry draft version is stale.");
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

        await _draftStore.SaveAsync(saved, ct).ConfigureAwait(false);
        await AppendAuditAsync(saved, "manual-je.save-draft", request.Actor, request.CorrelationId, saved.EvidenceLinks, ct).ConfigureAwait(false);
        return saved;
    }

    public Task<ManualJournalEntryDraftDto> ValidateDraftAsync(
        ValidateManualJournalEntryDraftRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        return NormalizeAndValidateAsync(request.Draft, allowIncomplete: false, ct);
    }

    public async Task<ManualJournalEntryDraftDto> SubmitApprovalAsync(
        SubmitManualJournalEntryApprovalRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        var fundProfileId = NormalizeFundProfileId(request.FundProfileId);
        var draft = await _draftStore.GetAsync(fundProfileId, request.JournalEntryId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Manual journal entry '{request.JournalEntryId:D}' was not found.");
        if (draft.Version != request.Version)
        {
            throw new InvalidOperationException("Manual journal entry draft version is stale.");
        }

        var validated = await NormalizeAndValidateAsync(draft, allowIncomplete: false, ct).ConfigureAwait(false);
        if (validated.ValidationIssues.Any(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
        {
            throw new InvalidOperationException("Manual journal entry cannot be submitted while critical validation issues remain.");
        }

        var submitted = validated with
        {
            Status = ManualJournalEntryStatusDto.Submitted,
            ApprovalId = validated.ApprovalId ?? $"manual-je-approval-{validated.JournalEntryId:N}",
            SubmittedAtUtc = DateTimeOffset.UtcNow,
            SubmittedBy = RequireText(request.Actor, nameof(request.Actor)),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Version = validated.Version + 1,
            EvidenceLinks = MergeEvidenceLinks(validated.EvidenceLinks, request.EvidenceLinks)
        };

        await _draftStore.SaveAsync(submitted, ct).ConfigureAwait(false);
        await AppendAuditAsync(submitted, "manual-je.submit-approval", request.Actor, request.CorrelationId, submitted.EvidenceLinks, ct).ConfigureAwait(false);
        return submitted;
    }

    private async Task<ManualJournalEntryDraftDto> NormalizeAndValidateAsync(
        ManualJournalEntryDraftDto draft,
        bool allowIncomplete,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var fundProfileId = NormalizeFundProfileId(draft.FundProfileId);
        var configuration = await _configurationService.GetWorkspaceAsync(fundProfileId, draft.LedgerBookId, ct).ConfigureAwait(false);
        var chartByPath = configuration.ChartOfAccounts.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
        var issues = new List<AccountingConfigurationValidationIssueDto>();
        var lines = new List<ManualJournalEntryLineDto>(draft.Lines.Count);
        var attachments = NormalizeAttachments(draft.EvidenceAttachments, draft.PreparedBy);
        var evidenceLinks = MergeEvidenceLinks(draft.EvidenceLinks, attachments.Select(item => item.Uri).ToArray());

        if (!draft.LedgerBookId.HasValue)
        {
            issues.Add(Issue("manual-je.book-missing", AccountingConfigurationValidationSeverityDto.Critical, "Ledger book is required before a manual journal entry can be approved.", "ledgerBookId", "Select the book that owns this journal entry."));
        }

        if (string.IsNullOrWhiteSpace(draft.Currency))
        {
            issues.Add(Issue("manual-je.currency-missing", AccountingConfigurationValidationSeverityDto.Critical, "Journal currency is required.", "currency", "Select the journal entry currency."));
        }

        if (!allowIncomplete && draft.Lines.Count < 2)
        {
            issues.Add(Issue("manual-je.lines-minimum", AccountingConfigurationValidationSeverityDto.Critical, "At least two journal lines are required for approval submission.", "lines", "Add debit and credit lines."));
        }

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
            var normalizedLine = line with
            {
                LineId = string.IsNullOrWhiteSpace(line.LineId) ? Guid.NewGuid().ToString("N") : line.LineId.Trim(),
                Currency = string.IsNullOrWhiteSpace(line.Currency) ? draft.Currency : line.Currency.Trim(),
                AccountPath = line.AccountPath?.Trim() ?? string.Empty,
                Description = NormalizeOptional(line.Description),
                EvidenceLink = NormalizeOptional(line.EvidenceLink)
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

            lines.Add(normalizedLine);
        }

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
            ValidationIssues = issues.OrderByDescending(issue => issue.Severity).ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase).ToArray()
        };
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

    private static string NormalizeFundProfileId(string? value)
        => string.IsNullOrWhiteSpace(value) ? DefaultFundProfileId : value.Trim();

    private static string RequireText(string? value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
