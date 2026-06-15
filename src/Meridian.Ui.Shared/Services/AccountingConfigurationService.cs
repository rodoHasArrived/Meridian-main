using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Banking;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Archival;
using Meridian.Storage.Ledger;

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

    public async Task<AccountingConfigurationWorkspaceDto?> GetAsync(string fundProfileId, CancellationToken ct = default)
    {
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.Workspaces.FirstOrDefault(item =>
            string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase)) is { } workspace
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
            var snapshot = await ReadSnapshotWithoutLockAsync(ct).ConfigureAwait(false);
            var workspaces = snapshot.Workspaces
                .Where(item => !string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
                .Append(workspace with { FundProfileId = normalizedFundProfileId, AuditTrail = [] })
                .OrderBy(item => item.FundProfileId, StringComparer.OrdinalIgnoreCase)
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
        CancellationToken ct = default)
    {
        var normalizedFundProfileId = NormalizeOptional(fundProfileId);
        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.AuditEvents
            .Where(item => string.IsNullOrWhiteSpace(normalizedFundProfileId) ||
                           string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
            .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId)
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

        return await SaveWithAuditAsync(workspace, beforeHash, request.Actor, "chart.upsert", null, request.CorrelationId, request.EvidenceLinks, request.CompanyId, request.ReportGroupPrincipalIds, ct).ConfigureAwait(false);
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

        return await SaveWithAuditAsync(workspace, beforeHash, request.Actor, "template.upsert", null, request.CorrelationId, request.EvidenceLinks, request.CompanyId, request.ReportGroupPrincipalIds, ct).ConfigureAwait(false);
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

        return await SaveWithAuditAsync(workspace, beforeHash, request.Actor, "posting-rule.upsert", null, request.CorrelationId, request.EvidenceLinks, request.CompanyId, request.ReportGroupPrincipalIds, ct).ConfigureAwait(false);
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

        return await SaveWithAuditAsync(workspace, beforeHash, request.Actor, "configuration.activate", request.LedgerBookId, request.CorrelationId, request.EvidenceLinks, request.CompanyId, request.ReportGroupPrincipalIds, ct).ConfigureAwait(false);
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
        string? companyId,
        IReadOnlyList<string>? reportGroupPrincipalIds,
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
                evidenceLinks ?? [],
                NormalizeOptional(companyId),
                NormalizePrincipalIds(reportGroupPrincipalIds)),
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

public sealed class ManualJournalEntryWorkbenchService : IManualJournalEntryWorkbenchService
{
    private const string DefaultFundProfileId = "default-fund";
    private const decimal BalanceTolerance = 0.000001m;
    private static readonly IReadOnlyDictionary<Guid, string> EmptyJournalEntryCurrencies = new Dictionary<Guid, string>();

    private sealed record PrivateCapitalCapitalAccountSubledgerSource(
        string SubledgerEntryId,
        string CapitalAccountId,
        string? InvestorId,
        string Currency,
        string FundEventId,
        string FundEventType,
        ManualJournalEntryTypeDto EntryType,
        ManualJournalEntryStatusDto ApprovalState,
        Guid JournalEntryId,
        DateOnly EffectiveDate,
        decimal GrossAmount,
        decimal NetCapitalActivity,
        string Memo,
        IReadOnlyList<string> EvidenceLinks,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
        DateTimeOffset UpdatedAtUtc,
        bool IsPosted);

    private sealed record PrivateCapitalReportOutputAccountScope(
        string CapitalAccountId,
        string? InvestorId,
        decimal NetCapitalActivity);

    private sealed record PrivateCapitalReportOutputReadinessProjection(
        string Label,
        string Reason,
        string NextAction,
        string? NextActionRoute);

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
        var privateCapitalActivity = BuildPrivateCapitalActivityProjection(
            normalizedFundProfileId,
            ledgerBookId,
            drafts,
            audit,
            bankTransactions,
            posted);

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
        return BuildPrivateCapitalActivityProjection(
            normalizedFundProfileId,
            ledgerBookId,
            drafts,
            audit,
            bankTransactions,
            posted);
    }

    private PrivateCapitalActivityProjectionDto BuildPrivateCapitalActivityProjection(
        string fundProfileId,
        Guid? ledgerBookId,
        IReadOnlyList<ManualJournalEntryDraftDto> drafts,
        IReadOnlyList<AccountingActionAuditEventDto> audit,
        IReadOnlyList<BankTransactionDto> bankTransactions,
        PostedPrivateCapitalActivityProjection? postedProjection)
    {
        var projectedAtUtc = DateTimeOffset.UtcNow;
        var fundEvents = new List<PrivateCapitalFundEventDto>();
        var ledgerImpacts = new List<PrivateCapitalLedgerImpactDto>();
        var projectionIssues = new List<AccountingConfigurationValidationIssueDto>();

        foreach (var draft in drafts)
        {
            var context = NormalizeTreasuryContext(draft.TreasuryContext, draft.AccountingDate);
            if (!IsPrivateCapitalActivityCandidate(draft.EntryType, context))
            {
                continue;
            }

            if (context?.EffectiveDate is null ||
                string.IsNullOrWhiteSpace(context.FundEventId) ||
                string.IsNullOrWhiteSpace(context.FundEventType) ||
                string.IsNullOrWhiteSpace(context.CapitalAccountId))
            {
                projectionIssues.Add(Issue(
                    "manual-je.private-capital-context-pending",
                    AccountingConfigurationValidationSeverityDto.Warning,
                    "Private-capital activity projection skipped a manual journal entry because fund event or capital account context is incomplete.",
                    draft.JournalEntryId.ToString("D"),
                    "Validate and complete treasury ledger context before relying on capital-account activity."));
                continue;
            }

            var grossAmount = Math.Max(Math.Abs(draft.TotalDebits), Math.Abs(draft.TotalCredits));
            if (grossAmount == 0m)
            {
                var debitAmount = draft.Lines
                    .Where(line => line.Side == AccountingTemplateLineSideDto.Debit)
                    .Sum(line => Math.Abs(line.Amount));
                var creditAmount = draft.Lines
                    .Where(line => line.Side == AccountingTemplateLineSideDto.Credit)
                    .Sum(line => Math.Abs(line.Amount));
                grossAmount = Math.Max(debitAmount, creditAmount);
            }

            var evidenceLinks = MergeEvidenceLinks(
                draft.EvidenceLinks,
                draft.EvidenceAttachments?.Select(attachment => attachment.Uri).ToArray());

            ledgerImpacts.Add(BuildPrivateCapitalLedgerImpact(draft, context, evidenceLinks));

            fundEvents.Add(new PrivateCapitalFundEventDto(
                context.FundEventId,
                context.FundEventType,
                draft.EntryType,
                draft.Status,
                draft.JournalEntryId,
                context.EffectiveDate.Value,
                context.CapitalAccountId,
                NormalizeOptional(context.InvestorId),
                string.IsNullOrWhiteSpace(draft.Currency) ? "USD" : draft.Currency.Trim().ToUpperInvariant(),
                grossAmount,
                CalculateNetCapitalActivity(draft.EntryType, grossAmount),
                draft.Memo?.Trim() ?? string.Empty,
                NormalizeOptional(context.PaymentIntentId),
                NormalizeOptional(context.SettlementReference),
                evidenceLinks,
                draft.ValidationIssues,
                draft.UpdatedAtUtc,
                ApprovalId: NormalizeOptional(draft.ApprovalId)));
        }

        var orderedFundEvents = fundEvents
            .OrderByDescending(item => item.EffectiveDate)
            .ThenByDescending(item => item.UpdatedAtUtc)
            .ThenBy(item => item.FundEventId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var postedJournalEntryCurrencies = postedProjection?.JournalEntryCurrencies ?? EmptyJournalEntryCurrencies;
        var postedLedgerEvents = postedProjection?.Projection.Events ?? [];
        var postedEvents = postedLedgerEvents
            .Select(item => MapPostedFundEvent(item, postedJournalEntryCurrencies))
            .ToArray();
        var postedEventIds = postedEvents
            .Select(static item => item.FundEventId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeDraftFundEvents = orderedFundEvents
            .Where(item => !postedEventIds.Contains(item.FundEventId))
            .ToArray();
        var combinedFundEvents = activeDraftFundEvents
            .Concat(postedEvents)
            .OrderByDescending(item => item.EffectiveDate)
            .ThenByDescending(item => item.UpdatedAtUtc)
            .ThenBy(item => item.FundEventId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var capitalAccountSubledgerEntries = BuildCapitalAccountSubledgerEntries(
            activeDraftFundEvents
                .Select(MapFundEventSubledgerSource)
                .Concat(postedLedgerEvents.SelectMany(item => MapPostedCapitalAccountSubledgerSources(item, postedJournalEntryCurrencies)))
                .ToArray());
        var capitalAccounts = BuildCapitalAccounts(capitalAccountSubledgerEntries);
        var orderedLedgerImpacts = ledgerImpacts
            .Where(item => !postedEventIds.Contains(item.FundEventId))
            .Concat(postedLedgerEvents.SelectMany(item => MapPostedLedgerImpacts(item, postedJournalEntryCurrencies)))
            .OrderByDescending(item => item.EffectiveDate)
            .ThenBy(item => item.FundEventId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.JournalEntryId)
            .ToArray();
        var projectionCurrency = combinedFundEvents
            .Select(item => item.Currency)
            .FirstOrDefault(currency => !string.IsNullOrWhiteSpace(currency)) ?? string.Empty;
        var reportPackWorkflowRecords = _reportPackWorkflowService?.ListRecords(200) ?? [];
        var postingReadyByFundEventId = BuildPostingReadyByFundEventId(orderedLedgerImpacts);
        var reportOutputs = orderedFundEvents
            .Where(item => !postedEventIds.Contains(item.FundEventId))
            .Select(item => BuildPrivateCapitalReportOutput(fundProfileId, ledgerBookId, item, postingReadyByFundEventId))
            .Concat(BuildPostedReportOutputs(fundProfileId, ledgerBookId, postedLedgerEvents, postedJournalEntryCurrencies, reportPackWorkflowRecords))
            .OrderByDescending(item => item.EffectiveDate)
            .ThenBy(item => item.ReportOutputType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ReportOutputId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var publishedReportOutputCount = CountPublishedReportOutputs(
            fundProfileId,
            postedLedgerEvents,
            reportPackWorkflowRecords);
        var fundEventRecords = PrivateCapitalFundEventLedgerRecordBuilder.Build(
            fundProfileId,
            combinedFundEvents,
            capitalAccountSubledgerEntries,
            orderedLedgerImpacts,
            reportOutputs);
        var capitalAccountSubledgers = PrivateCapitalCapitalAccountSubledgerBuilder.Build(
            fundProfileId,
            ledgerBookId,
            projectedAtUtc,
            capitalAccounts,
            fundEventRecords,
            capitalAccountSubledgerEntries,
            orderedLedgerImpacts,
            reportOutputs,
            projectionIssues);
        var paymentIntents = BuildPaymentIntentWorkflows(
            fundProfileId,
            ledgerBookId,
            fundEventRecords,
            drafts,
            audit,
            bankTransactions);

        return new PrivateCapitalActivityProjectionDto(
            fundProfileId,
            ledgerBookId,
            projectedAtUtc,
            combinedFundEvents.Length,
            capitalAccounts.Count,
            combinedFundEvents.Count(item => item.JournalStatus is ManualJournalEntryStatusDto.Submitted or ManualJournalEntryStatusDto.Approved),
            combinedFundEvents.Count(item => item.JournalStatus == ManualJournalEntryStatusDto.Submitted),
            postedEvents.Length,
            publishedReportOutputCount,
            combinedFundEvents.Sum(item => item.NetCapitalActivity),
            projectionCurrency,
            combinedFundEvents,
            capitalAccounts,
            capitalAccountSubledgerEntries,
            orderedLedgerImpacts,
            reportOutputs,
            projectionIssues
                .OrderByDescending(issue => issue.Severity)
                .ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.TargetId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            fundEventRecords,
            capitalAccountSubledgers,
            paymentIntents);
    }

    private static IReadOnlyList<PaymentIntentWorkflowDto> BuildPaymentIntentWorkflows(
        string fundProfileId,
        Guid? ledgerBookId,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> fundEventRecords,
        IReadOnlyList<ManualJournalEntryDraftDto> drafts,
        IReadOnlyList<AccountingActionAuditEventDto> audit,
        IReadOnlyList<BankTransactionDto> bankTransactions)
    {
        var draftsByJournalEntryId = drafts.ToDictionary(static draft => draft.JournalEntryId);
        return fundEventRecords
            .Where(static record => !string.IsNullOrWhiteSpace(record.PaymentIntentId))
            .GroupBy(static record => record.PaymentIntentId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildPaymentIntentWorkflow(
                fundProfileId,
                ledgerBookId,
                group.Key,
                group
                    .OrderByDescending(static record => record.EffectiveDate)
                    .ThenBy(static record => record.FundEventId, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                draftsByJournalEntryId,
                audit,
                bankTransactions))
            .OrderByDescending(static workflow => workflow.ExpectedCashMovement.EffectiveDate)
            .ThenBy(static workflow => workflow.PaymentIntentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PaymentIntentWorkflowDto BuildPaymentIntentWorkflow(
        string fundProfileId,
        Guid? ledgerBookId,
        string paymentIntentId,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyDictionary<Guid, ManualJournalEntryDraftDto> draftsByJournalEntryId,
        IReadOnlyList<AccountingActionAuditEventDto> audit,
        IReadOnlyList<BankTransactionDto> bankTransactions)
    {
        var primary = records[0];
        draftsByJournalEntryId.TryGetValue(primary.JournalEntryId, out var primaryDraft);
        var settlementReference = NormalizeOptional(primary.SettlementReference)
            ?? records.Select(static record => NormalizeOptional(record.SettlementReference))
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        var allEvidenceLinks = BuildPaymentIntentEvidenceLinks(records, draftsByJournalEntryId, audit, paymentIntentId, settlementReference);
        var expectedCashMovement = BuildExpectedCashMovement(
            fundProfileId,
            ledgerBookId,
            paymentIntentId,
            settlementReference,
            records,
            allEvidenceLinks);
        var approvalChain = BuildPaymentIntentApprovalChain(records, draftsByJournalEntryId);
        var bankEvidence = BuildPaymentIntentBankEvidence(paymentIntentId, settlementReference, records, bankTransactions, allEvidenceLinks);
        var reconciliationLinks = BuildPaymentIntentReconciliationLinks(paymentIntentId, settlementReference, records, allEvidenceLinks);
        var auditHistory = BuildPaymentIntentAuditHistory(
            paymentIntentId,
            settlementReference,
            records,
            draftsByJournalEntryId,
            audit,
            bankEvidence,
            reconciliationLinks);
        var status = ResolvePaymentIntentWorkflowStatus(records, approvalChain, bankEvidence, reconciliationLinks);
        var evidenceRoute = PrivateCapitalActivityRouteBuilder.BuildPaymentIntentEvidenceRoute(paymentIntentId);
        var workbenchRoute = PrivateCapitalActivityRouteBuilder.BuildPaymentIntentWorkbenchRoute(fundProfileId, ledgerBookId, paymentIntentId);
        var requester = NormalizeOptional(primaryDraft?.PreparedBy)
            ?? NormalizeOptional(primaryDraft?.SubmittedBy)
            ?? "ledger-posting";
        var requestedAtUtc = primaryDraft?.CreatedAtUtc ?? primary.FundEvent.UpdatedAtUtc;

        return new PaymentIntentWorkflowDto(
            paymentIntentId,
            settlementReference,
            fundProfileId,
            ledgerBookId,
            primary.FundEventId,
            primary.JournalEntryId,
            requester,
            requestedAtUtc,
            status,
            FormatPaymentIntentWorkflowStatus(status),
            BuildPaymentIntentReadinessReason(status, records, bankEvidence, reconciliationLinks),
            "Full payment execution is explicitly deferred in v0.18; this layer only retains intent, control, cash-evidence, reconciliation, and audit history before any bank-side instruction.",
            expectedCashMovement,
            evidenceRoute,
            workbenchRoute,
            approvalChain,
            bankEvidence,
            reconciliationLinks,
            auditHistory);
    }

    private static PaymentIntentExpectedCashMovementDto BuildExpectedCashMovement(
        string fundProfileId,
        Guid? ledgerBookId,
        string paymentIntentId,
        string? settlementReference,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<string> evidenceLinks)
    {
        var primary = records[0];
        var netActivity = records.Sum(static record => record.NetCapitalActivity);
        var amount = records.Count == 1
            ? Math.Abs(primary.NetCapitalActivity)
            : records.Sum(static record => Math.Abs(record.NetCapitalActivity));
        var direction = ResolvePaymentIntentDirection(records, netActivity);
        var currency = records
            .Select(static record => NormalizeCurrency(record.Currency))
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "USD";
        var purpose = records.Count == 1
            ? $"{primary.FundEventType} for {primary.CapitalAccountId}"
            : $"{records.Count} private-capital fund events";
        var payee = ResolvePaymentIntentPayee(fundProfileId, primary, direction);
        var accountScope = BuildPaymentIntentAccountScope(fundProfileId, ledgerBookId, primary);
        var businessPurpose = NormalizeOptional(primary.Memo) ?? purpose;
        var approvalPolicy = ResolvePaymentIntentApprovalPolicy(records);

        return new PaymentIntentExpectedCashMovementDto(
            paymentIntentId,
            direction,
            amount,
            currency,
            records.Select(static record => record.EffectiveDate).DefaultIfEmpty(DateOnly.MinValue).Max(),
            settlementReference,
            primary.FundEventId,
            primary.FundEventType,
            primary.CapitalAccountId,
            primary.InvestorId,
            purpose,
            payee,
            accountScope,
            businessPurpose,
            approvalPolicy,
            BuildPaymentIntentSourceEvidenceLinks(records, evidenceLinks));
    }

    private static string ResolvePaymentIntentPayee(
        string fundProfileId,
        PrivateCapitalFundEventLedgerRecordDto primary,
        PaymentIntentCashDirectionDto direction)
    {
        if (direction == PaymentIntentCashDirectionDto.Inflow)
        {
            return $"fund:{fundProfileId}";
        }

        return NormalizeOptional(primary.InvestorId)
            ?? NormalizeOptional(primary.CapitalAccountId)
            ?? $"fund:{fundProfileId}";
    }

    private static string BuildPaymentIntentAccountScope(
        string fundProfileId,
        Guid? ledgerBookId,
        PrivateCapitalFundEventLedgerRecordDto primary)
    {
        var parts = new List<string> { $"fund:{fundProfileId}" };
        if (ledgerBookId.HasValue)
        {
            parts.Add($"book:{ledgerBookId.Value:D}");
        }

        parts.Add(primary.CapitalAccountId);
        if (!string.IsNullOrWhiteSpace(primary.InvestorId))
        {
            parts.Add(primary.InvestorId);
        }

        return string.Join(" / ", parts);
    }

    private static string ResolvePaymentIntentApprovalPolicy(IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records)
    {
        if (records.Any(static record => record.ApprovalState is ManualJournalEntryStatusDto.Approved))
        {
            return "Controller approval retained before execution-deferred reliance";
        }

        if (records.Any(static record => record.ApprovalState is ManualJournalEntryStatusDto.Submitted))
        {
            return "Controller approval pending before execution-deferred reliance";
        }

        return "Controller approval required before execution-deferred reliance";
    }

    private static IReadOnlyList<string> BuildPaymentIntentSourceEvidenceLinks(
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<string> evidenceLinks)
    {
        return records
            .Select(static record => record.ActivityRoute)
            .Concat(records.Select(static record => record.EvidenceRoute))
            .Concat(evidenceLinks)
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PaymentIntentCashDirectionDto ResolvePaymentIntentDirection(
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        decimal netActivity)
    {
        if (records.All(static record => record.FundEvent.EntryType is ManualJournalEntryTypeDto.CapitalCall or ManualJournalEntryTypeDto.Subscription))
        {
            return PaymentIntentCashDirectionDto.Inflow;
        }

        if (records.All(static record => record.FundEvent.EntryType is ManualJournalEntryTypeDto.Distribution or ManualJournalEntryTypeDto.Redemption or ManualJournalEntryTypeDto.ManagementFee))
        {
            return PaymentIntentCashDirectionDto.Outflow;
        }

        return netActivity > 0m
            ? PaymentIntentCashDirectionDto.Inflow
            : netActivity < 0m
                ? PaymentIntentCashDirectionDto.Outflow
                : PaymentIntentCashDirectionDto.Neutral;
    }

    private static IReadOnlyList<PaymentIntentApprovalStepDto> BuildPaymentIntentApprovalChain(
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyDictionary<Guid, ManualJournalEntryDraftDto> draftsByJournalEntryId)
    {
        var steps = new List<PaymentIntentApprovalStepDto>();
        var sequence = 1;
        foreach (var record in records.OrderBy(static record => record.EffectiveDate).ThenBy(static record => record.FundEventId, StringComparer.OrdinalIgnoreCase))
        {
            draftsByJournalEntryId.TryGetValue(record.JournalEntryId, out var draft);
            var requester = NormalizeOptional(draft?.PreparedBy) ?? "ledger-posting";
            steps.Add(new PaymentIntentApprovalStepDto(
                sequence++,
                "Requester",
                requester,
                "Requested",
                draft?.CreatedAtUtc,
                record.ActivityRoute));

            if (draft?.SubmittedAtUtc is not null || !string.IsNullOrWhiteSpace(draft?.SubmittedBy) || !string.IsNullOrWhiteSpace(record.ApprovalId))
            {
                steps.Add(new PaymentIntentApprovalStepDto(
                    sequence++,
                    "Controller approval",
                    NormalizeOptional(draft?.SubmittedBy) ?? NormalizeOptional(record.ApprovalId) ?? "controller",
                    record.ApprovalState.ToString(),
                    draft?.SubmittedAtUtc ?? record.FundEvent.UpdatedAtUtc,
                    record.ApprovalRoute));
            }
            else
            {
                steps.Add(new PaymentIntentApprovalStepDto(
                    sequence++,
                    "Controller approval",
                    "controller",
                    "Pending",
                    null,
                    record.ApprovalRoute));
            }
        }

        return steps
            .GroupBy(step => $"{step.Role}:{step.Actor}:{step.Status}:{step.EvidenceRoute}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select((step, index) => step with { Sequence = index + 1 })
            .ToArray();
    }

    private static IReadOnlyList<PaymentIntentBankEvidenceDto> BuildPaymentIntentBankEvidence(
        string paymentIntentId,
        string? settlementReference,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<BankTransactionDto> bankTransactions,
        IReadOnlyList<string> evidenceLinks)
    {
        var evidence = new List<PaymentIntentBankEvidenceDto>();
        var matchedTransactions = bankTransactions
            .Where(transaction => MatchesPaymentIntentBankTransaction(transaction, paymentIntentId, settlementReference, records))
            .OrderByDescending(static transaction => transaction.RecordedAt)
            .ThenBy(static transaction => transaction.BankTransactionId)
            .ToArray();

        foreach (var transaction in matchedTransactions)
        {
            var isReturn = IsReturnBankTransaction(transaction);
            evidence.Add(new PaymentIntentBankEvidenceDto(
                $"bank-transaction:{transaction.BankTransactionId:D}",
                isReturn ? "BankReturn" : "BankConfirmation",
                isReturn ? "Returned" : "Confirmed",
                isReturn
                    ? $"Bank-side transaction {transaction.BankTransactionId:D} indicates a returned, voided, or reversed payment intent."
                    : $"Bank-side transaction {transaction.BankTransactionId:D} confirms expected cash movement.",
                transaction.BankTransactionId,
                transaction.TransactionType,
                transaction.Amount,
                NormalizeCurrency(transaction.Currency),
                transaction.EffectiveDate,
                transaction.RecordedAt,
                NormalizeOptional(transaction.ExternalRef),
                BuildBankTransactionEvidenceRoute(transaction),
                NormalizeOptional(transaction.RecordedBy)));
        }

        foreach (var link in SelectPaymentIntentCashEvidenceLinks(evidenceLinks, paymentIntentId, settlementReference))
        {
            evidence.Add(new PaymentIntentBankEvidenceDto(
                $"retained-cash-evidence:{SanitizePaymentIntentPart(link)}",
                "RetainedCashEvidence",
                "Retained",
                $"Retained cash, bank, treasury, or settlement evidence is linked at {link}.",
                EvidenceRoute: link));
        }

        if (evidence.Count == 0)
        {
            evidence.Add(new PaymentIntentBankEvidenceDto(
                $"bank-evidence-missing:{SanitizePaymentIntentPart(paymentIntentId)}",
                "BankConfirmation",
                "Missing",
                "No bank confirmation, return, custodian cash, treasury, or settlement evidence is retained for this payment intent."));
        }

        return evidence
            .GroupBy(static item => item.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private static IReadOnlyList<PaymentIntentReconciliationLinkDto> BuildPaymentIntentReconciliationLinks(
        string paymentIntentId,
        string? settlementReference,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<string> evidenceLinks)
    {
        var reconciliationLinks = SelectReconciliationEvidenceLinks(evidenceLinks, paymentIntentId, settlementReference).ToArray();
        if (reconciliationLinks.Length == 0)
        {
            return
            [
                new PaymentIntentReconciliationLinkDto(
                    $"reconciliation-pending:{SanitizePaymentIntentPart(paymentIntentId)}",
                    "Pending",
                    "No reconciliation case, matching run, or break-review evidence is linked to this payment intent.")
            ];
        }

        return reconciliationLinks
            .Select((link, index) => new PaymentIntentReconciliationLinkDto(
                $"reconciliation-link:{index + 1}:{SanitizePaymentIntentPart(paymentIntentId)}",
                "Ready",
                $"Reconciliation evidence links payment intent {paymentIntentId} to retained cash or ledger review.",
                EvidenceRoute: link,
                ReconciliationCaseId: TryExtractEvidenceToken(link, "case"),
                ReconciliationRunId: TryExtractEvidenceToken(link, "run")))
            .ToArray();
    }

    private static IReadOnlyList<PaymentIntentAuditEventDto> BuildPaymentIntentAuditHistory(
        string paymentIntentId,
        string? settlementReference,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyDictionary<Guid, ManualJournalEntryDraftDto> draftsByJournalEntryId,
        IReadOnlyList<AccountingActionAuditEventDto> audit,
        IReadOnlyList<PaymentIntentBankEvidenceDto> bankEvidence,
        IReadOnlyList<PaymentIntentReconciliationLinkDto> reconciliationLinks)
    {
        var events = new List<PaymentIntentAuditEventDto>();
        foreach (var record in records.OrderBy(static record => record.FundEvent.UpdatedAtUtc).ThenBy(static record => record.FundEventId, StringComparer.OrdinalIgnoreCase))
        {
            draftsByJournalEntryId.TryGetValue(record.JournalEntryId, out var draft);
            events.Add(new PaymentIntentAuditEventDto(
                $"payment-intent-requested:{record.JournalEntryId:D}",
                draft?.CreatedAtUtc ?? record.FundEvent.UpdatedAtUtc,
                NormalizeOptional(draft?.PreparedBy) ?? "ledger-posting",
                "payment-intent.requested",
                $"Payment intent {paymentIntentId} was captured from treasury context for {record.FundEventType}.",
                record.EvidenceLinks));

            if (draft?.SubmittedAtUtc is not null || record.ApprovalState is ManualJournalEntryStatusDto.Submitted or ManualJournalEntryStatusDto.Approved)
            {
                events.Add(new PaymentIntentAuditEventDto(
                    $"payment-intent-approval:{record.JournalEntryId:D}",
                    draft?.SubmittedAtUtc ?? record.FundEvent.UpdatedAtUtc,
                    NormalizeOptional(draft?.SubmittedBy) ?? NormalizeOptional(record.ApprovalId) ?? "controller",
                    "payment-intent.approval-state",
                    $"Approval chain state is {record.ApprovalState} for payment intent {paymentIntentId}.",
                    NormalizeOptional(record.ApprovalRoute) is { } approvalRoute
                        ? [approvalRoute]
                        : []));
            }
        }

        foreach (var auditEvent in audit.Where(auditEvent => MatchesPaymentIntentAuditEvent(auditEvent, paymentIntentId, settlementReference, records)))
        {
            events.Add(new PaymentIntentAuditEventDto(
                auditEvent.AuditEventId.ToString("D"),
                auditEvent.RecordedAtUtc,
                auditEvent.Actor,
                auditEvent.Action,
                $"Accounting audit event {auditEvent.Action} is linked to payment intent {paymentIntentId}.",
                auditEvent.EvidenceLinks));
        }

        events.Add(new PaymentIntentAuditEventDto(
            $"payment-intent-cash-evidence:{SanitizePaymentIntentPart(paymentIntentId)}",
            bankEvidence
                .Where(static evidence => evidence.RecordedAtUtc.HasValue)
                .Select(static evidence => evidence.RecordedAtUtc!.Value)
                .DefaultIfEmpty(records.Max(static record => record.FundEvent.UpdatedAtUtc))
                .Max(),
            "treasury-control",
            "payment-intent.cash-evidence-reviewed",
            $"{bankEvidence.Count} bank confirmation, return, or retained cash evidence item(s) are attached to the payment intent.",
            bankEvidence
                .Select(static evidence => evidence.EvidenceRoute)
                .Where(static route => !string.IsNullOrWhiteSpace(route))
                .Select(static route => route!)
                .ToArray()));

        events.Add(new PaymentIntentAuditEventDto(
            $"payment-intent-reconciliation:{SanitizePaymentIntentPart(paymentIntentId)}",
            records.Max(static record => record.FundEvent.UpdatedAtUtc),
            "treasury-control",
            "payment-intent.reconciliation-reviewed",
            $"{reconciliationLinks.Count} reconciliation linkage item(s) are attached to the payment intent.",
            reconciliationLinks
                .Select(static link => link.EvidenceRoute)
                .Where(static route => !string.IsNullOrWhiteSpace(route))
                .Select(static route => route!)
                .ToArray()));

        events.Add(new PaymentIntentAuditEventDto(
            $"payment-intent-execution-deferred:{SanitizePaymentIntentPart(paymentIntentId)}",
            DateTimeOffset.UtcNow,
            "treasury-control",
            "payment-intent.execution-deferred",
            "Full payment execution is deferred; this record is an evidence and control checkpoint only."));

        return events
            .GroupBy(static item => item.AuditEventId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static item => item.RecordedAtUtc)
            .ThenBy(static item => item.Action, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PaymentIntentWorkflowStatusDto ResolvePaymentIntentWorkflowStatus(
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<PaymentIntentApprovalStepDto> approvalChain,
        IReadOnlyList<PaymentIntentBankEvidenceDto> bankEvidence,
        IReadOnlyList<PaymentIntentReconciliationLinkDto> reconciliationLinks)
    {
        if (records.Any(static record => record.Readiness == PrivateCapitalFundEventLedgerReadinessDto.Blocked) ||
            approvalChain.Any(static step => step.Status is nameof(ManualJournalEntryStatusDto.Rejected) or nameof(ManualJournalEntryStatusDto.NeedsFix)))
        {
            return PaymentIntentWorkflowStatusDto.Blocked;
        }

        if (bankEvidence.Any(static evidence => string.Equals(evidence.Status, "Returned", StringComparison.OrdinalIgnoreCase)))
        {
            return PaymentIntentWorkflowStatusDto.BankReturned;
        }

        if (records.Any(static record => record.PaymentIntentEvidence is null ||
                                         record.PaymentIntentEvidence.Status == PrivateCapitalPaymentIntentEvidenceStatusDto.MissingIntent))
        {
            return PaymentIntentWorkflowStatusDto.EvidenceMissing;
        }

        if (approvalChain.Any(static step => step.Status is "Pending" or nameof(ManualJournalEntryStatusDto.Draft) or nameof(ManualJournalEntryStatusDto.Submitted)))
        {
            return PaymentIntentWorkflowStatusDto.ApprovalPending;
        }

        if (records.Any(static record => record.PaymentIntentEvidence is not null &&
                                         record.PaymentIntentEvidence.Status == PrivateCapitalPaymentIntentEvidenceStatusDto.CashEvidenceMissing))
        {
            return PaymentIntentWorkflowStatusDto.BankEvidencePending;
        }

        if (!bankEvidence.Any(static evidence =>
                string.Equals(evidence.Status, "Confirmed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(evidence.Status, "Retained", StringComparison.OrdinalIgnoreCase)))
        {
            return PaymentIntentWorkflowStatusDto.BankEvidencePending;
        }

        if (!reconciliationLinks.Any(static link => string.Equals(link.Status, "Ready", StringComparison.OrdinalIgnoreCase)))
        {
            return PaymentIntentWorkflowStatusDto.ReconciliationPending;
        }

        return PaymentIntentWorkflowStatusDto.ExecutionDeferred;
    }

    private static string FormatPaymentIntentWorkflowStatus(PaymentIntentWorkflowStatusDto status)
        => status switch
        {
            PaymentIntentWorkflowStatusDto.EvidenceMissing => "Intent evidence missing",
            PaymentIntentWorkflowStatusDto.ApprovalPending => "Approval pending",
            PaymentIntentWorkflowStatusDto.BankEvidencePending => "Bank evidence pending",
            PaymentIntentWorkflowStatusDto.BankReturned => "Bank return captured",
            PaymentIntentWorkflowStatusDto.ReconciliationPending => "Reconciliation pending",
            PaymentIntentWorkflowStatusDto.ExecutionDeferred => "Ready, execution deferred",
            PaymentIntentWorkflowStatusDto.Blocked => "Blocked",
            _ => status.ToString()
        };

    private static string BuildPaymentIntentReadinessReason(
        PaymentIntentWorkflowStatusDto status,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<PaymentIntentBankEvidenceDto> bankEvidence,
        IReadOnlyList<PaymentIntentReconciliationLinkDto> reconciliationLinks)
        => status switch
        {
            PaymentIntentWorkflowStatusDto.EvidenceMissing =>
                "A payment intent id or required cash evidence field is missing from the treasury context.",
            PaymentIntentWorkflowStatusDto.ApprovalPending =>
                "Requester and expected movement are captured, but controller approval is not complete.",
            PaymentIntentWorkflowStatusDto.BankEvidencePending =>
                "Approval evidence is captured, but no retained bank confirmation, return, or cash settlement evidence is linked.",
            PaymentIntentWorkflowStatusDto.BankReturned =>
                "A bank-side return, void, rejection, or reversal is attached and blocks execution readiness.",
            PaymentIntentWorkflowStatusDto.ReconciliationPending =>
                "Cash evidence is attached, but reconciliation linkage is not retained.",
            PaymentIntentWorkflowStatusDto.ExecutionDeferred =>
                $"All pre-execution controls are retained across {records.Count} fund event(s), {bankEvidence.Count} bank evidence item(s), and {reconciliationLinks.Count} reconciliation link(s).",
            PaymentIntentWorkflowStatusDto.Blocked =>
                "The linked fund-event readiness or approval state is blocked.",
            _ => "Payment intent workflow requires review."
        };

    private static IReadOnlyList<string> BuildPaymentIntentEvidenceLinks(
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyDictionary<Guid, ManualJournalEntryDraftDto> draftsByJournalEntryId,
        IReadOnlyList<AccountingActionAuditEventDto> audit,
        string paymentIntentId,
        string? settlementReference)
    {
        var links = new List<string>();
        links.AddRange(records.SelectMany(static record => record.EvidenceLinks));
        links.AddRange(records.SelectMany(static record => record.PaymentIntentEvidence?.CashEvidenceLinks ?? []));
        foreach (var record in records)
        {
            if (draftsByJournalEntryId.TryGetValue(record.JournalEntryId, out var draft))
            {
                links.AddRange(draft.EvidenceLinks);
                links.AddRange(draft.EvidenceAttachments?.Select(static attachment => attachment.Uri) ?? []);
            }
        }

        links.AddRange(audit
            .Where(auditEvent => MatchesPaymentIntentText(auditEvent.CorrelationId, paymentIntentId, settlementReference) ||
                auditEvent.EvidenceLinks.Any(link => MatchesPaymentIntentText(link, paymentIntentId, settlementReference)))
            .SelectMany(static auditEvent => auditEvent.EvidenceLinks));

        return links
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool MatchesPaymentIntentBankTransaction(
        BankTransactionDto transaction,
        string paymentIntentId,
        string? settlementReference,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records)
    {
        if (MatchesPaymentIntentText(transaction.ExternalRef, paymentIntentId, settlementReference))
        {
            return true;
        }

        return records.Any(record => transaction.EntityId == record.JournalEntryId);
    }

    private static bool IsReturnBankTransaction(BankTransactionDto transaction)
        => transaction.IsVoided ||
           transaction.TransactionType.Contains("return", StringComparison.OrdinalIgnoreCase) ||
           transaction.TransactionType.Contains("reject", StringComparison.OrdinalIgnoreCase) ||
           transaction.TransactionType.Contains("reversal", StringComparison.OrdinalIgnoreCase) ||
           transaction.TransactionType.Contains("void", StringComparison.OrdinalIgnoreCase) ||
           transaction.TransactionType.Contains("fail", StringComparison.OrdinalIgnoreCase);

    private static string BuildBankTransactionEvidenceRoute(BankTransactionDto transaction)
        => $"/api/banking/transactions?entityId={Uri.EscapeDataString(transaction.EntityId.ToString("D"))}&bankTransactionId={Uri.EscapeDataString(transaction.BankTransactionId.ToString("D"))}";

    private static IEnumerable<string> SelectPaymentIntentCashEvidenceLinks(
        IReadOnlyList<string> links,
        string paymentIntentId,
        string? settlementReference)
        => links
            .Where(static link => IsPaymentIntentCashEvidenceLink(link))
            .Where(link => IsScopedPaymentIntentEvidenceLink(link, paymentIntentId, settlementReference));

    private static bool IsPaymentIntentCashEvidenceLink(string link)
        => link.Contains("bank", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("cash", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("custodian", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("plaid", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("reconciliation", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("settlement", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("treasury", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("wire", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("ach", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("swift", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("return", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("revers", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("reject", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("void", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("failure", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SelectReconciliationEvidenceLinks(
        IReadOnlyList<string> links,
        string paymentIntentId,
        string? settlementReference)
        => links
            .Where(static link =>
                link.Contains("reconciliation", StringComparison.OrdinalIgnoreCase) ||
                link.Contains("reconcile", StringComparison.OrdinalIgnoreCase) ||
                link.Contains("break", StringComparison.OrdinalIgnoreCase) ||
                link.Contains("match", StringComparison.OrdinalIgnoreCase))
            .Where(link => IsScopedPaymentIntentEvidenceLink(link, paymentIntentId, settlementReference));

    private static bool MatchesPaymentIntentAuditEvent(
        AccountingActionAuditEventDto auditEvent,
        string paymentIntentId,
        string? settlementReference,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records)
        => MatchesPaymentIntentText(auditEvent.CorrelationId, paymentIntentId, settlementReference) ||
           auditEvent.EvidenceLinks.Any(link => MatchesPaymentIntentText(link, paymentIntentId, settlementReference)) ||
           records.Any(record =>
               MatchesPaymentIntentText(auditEvent.CorrelationId, record.JournalEntryId.ToString("D"), null) ||
               auditEvent.EvidenceLinks.Any(link => MatchesPaymentIntentText(link, record.FundEventId, record.SettlementReference)));

    private static bool MatchesPaymentIntentText(string? value, string paymentIntentId, string? settlementReference)
        => !string.IsNullOrWhiteSpace(value) &&
           (ContainsPaymentIntentIdentifier(value, paymentIntentId) ||
            (!string.IsNullOrWhiteSpace(settlementReference) &&
             ContainsPaymentIntentIdentifier(value, settlementReference)));

    private static bool IsScopedPaymentIntentEvidenceLink(string link, string paymentIntentId, string? settlementReference)
        => !ContainsExplicitPaymentOrSettlementIdentifier(link) ||
           MatchesPaymentIntentText(link, paymentIntentId, settlementReference);

    private static bool ContainsPaymentIntentIdentifier(string value, string identifier)
        => value.Contains(identifier, StringComparison.OrdinalIgnoreCase) ||
           value.Contains(Uri.EscapeDataString(identifier), StringComparison.OrdinalIgnoreCase);

    private static bool ContainsExplicitPaymentOrSettlementIdentifier(string link)
        => link.Contains("payment:", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("payment%3A", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("settlement:", StringComparison.OrdinalIgnoreCase) ||
           link.Contains("settlement%3A", StringComparison.OrdinalIgnoreCase);

    private static string? TryExtractEvidenceToken(string value, string label)
    {
        var marker = $"{label}:";
        var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var token = value[(index + marker.Length)..]
            .Split(['/', '?', '&', '#'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return NormalizeOptional(token);
    }

    private static string SanitizePaymentIntentPart(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "payment-intent"
            : string.Join("-", value.Trim().Split(
                Path.GetInvalidFileNameChars().Concat([':', '/', '\\', '?', '&', '=']).Distinct().ToArray(),
                StringSplitOptions.RemoveEmptyEntries));

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

    private static IReadOnlyList<PrivateCapitalCapitalAccountActivityDto> BuildCapitalAccounts(
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> subledgerEntries)
        => subledgerEntries
            .GroupBy(item => new { item.CapitalAccountId, item.InvestorId, item.Currency })
            .Select(group =>
            {
                var ordered = group
                    .OrderByDescending(item => item.EffectiveDate)
                    .ThenByDescending(item => item.UpdatedAtUtc)
                    .ToArray();
                return new PrivateCapitalCapitalAccountActivityDto(
                    group.Key.CapitalAccountId,
                    group.Key.InvestorId,
                    group.Key.Currency,
                    group.Where(item => item.EntryType == ManualJournalEntryTypeDto.CapitalCall).Sum(item => Math.Abs(item.NetCapitalActivity)),
                    group.Where(item => item.EntryType == ManualJournalEntryTypeDto.Distribution).Sum(item => Math.Abs(item.NetCapitalActivity)),
                    group.Where(item => item.EntryType == ManualJournalEntryTypeDto.Subscription).Sum(item => Math.Abs(item.NetCapitalActivity)),
                    group.Where(item => item.EntryType == ManualJournalEntryTypeDto.Redemption).Sum(item => Math.Abs(item.NetCapitalActivity)),
                    group.Where(item => item.EntryType == ManualJournalEntryTypeDto.ManagementFee).Sum(item => Math.Abs(item.NetCapitalActivity)),
                    group.Sum(item => item.NetCapitalActivity),
                    group.Select(static item => item.FundEventId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    ordered[0].EffectiveDate,
                    ordered[0].FundEventType,
                    group
                        .Select(item => item.FundEventId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray());
            })
            .OrderBy(item => item.CapitalAccountId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.InvestorId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Currency, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> BuildCapitalAccountSubledgerEntries(
        IReadOnlyList<PrivateCapitalCapitalAccountSubledgerSource> sources)
    {
        var entries = new List<PrivateCapitalCapitalAccountSubledgerEntryDto>();
        foreach (var group in sources
            .GroupBy(item => new { item.CapitalAccountId, item.InvestorId, item.Currency }))
        {
            var runningNetActivity = 0m;
            foreach (var item in group
                .OrderBy(item => item.EffectiveDate)
                .ThenBy(item => item.UpdatedAtUtc)
                .ThenBy(item => item.FundEventId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.JournalEntryId))
            {
                runningNetActivity += item.NetCapitalActivity;
                entries.Add(new PrivateCapitalCapitalAccountSubledgerEntryDto(
                    item.SubledgerEntryId,
                    item.CapitalAccountId,
                    item.InvestorId,
                    item.Currency,
                    item.FundEventId,
                    item.FundEventType,
                    item.EntryType,
                    item.ApprovalState,
                    item.JournalEntryId,
                    item.EffectiveDate,
                    item.GrossAmount,
                    item.NetCapitalActivity,
                    runningNetActivity,
                    item.Memo,
                    item.EvidenceLinks,
                    item.ValidationIssues,
                    item.UpdatedAtUtc,
                    item.IsPosted));
            }
        }

        return entries
            .OrderByDescending(item => item.EffectiveDate)
            .ThenByDescending(item => item.UpdatedAtUtc)
            .ThenBy(item => item.CapitalAccountId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.FundEventId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PrivateCapitalCapitalAccountSubledgerSource MapFundEventSubledgerSource(
        PrivateCapitalFundEventDto fundEvent)
        => new(
            $"capital-account-subledger:{fundEvent.CapitalAccountId}:{fundEvent.FundEventId}:{fundEvent.JournalEntryId:D}".ToLowerInvariant(),
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            fundEvent.Currency,
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            fundEvent.EntryType,
            fundEvent.JournalStatus,
            fundEvent.JournalEntryId,
            fundEvent.EffectiveDate,
            fundEvent.GrossAmount,
            fundEvent.NetCapitalActivity,
            fundEvent.Memo,
            fundEvent.EvidenceLinks,
            fundEvent.ValidationIssues,
            fundEvent.UpdatedAtUtc,
            fundEvent.IsPosted);

    private static IEnumerable<PrivateCapitalCapitalAccountSubledgerSource> MapPostedCapitalAccountSubledgerSources(
        PrivateCapitalFundEventLedgerEvent fundEvent,
        IReadOnlyDictionary<Guid, string> journalEntryCurrencies)
    {
        if (fundEvent.CapitalAccountImpacts.Count == 0)
        {
            yield break;
        }

        var entryType = MapPrivateCapitalEntryType(fundEvent.FundEventType);
        var approvalState = MapPostedApprovalState(fundEvent.ApprovalState, fundEvent.HasCriticalIssues);
        var currency = ResolvePostedEventCurrency(fundEvent, journalEntryCurrencies);
        var evidenceLinks = BuildPostedFundEventEvidenceLinks(fundEvent);
        var issues = MapPostedIssues(fundEvent);
        var effectiveDate = fundEvent.EffectiveDate ?? DateOnly.FromDateTime(fundEvent.FirstPostedAt.UtcDateTime);
        var journalEntryId = fundEvent.JournalEntryIds.FirstOrDefault();
        var memo = fundEvent.LedgerImpacts.FirstOrDefault()?.Description ?? fundEvent.FundEventType;

        var groupedImpacts = fundEvent.CapitalAccountImpacts
            .GroupBy(static item => new { item.CapitalAccountId, item.InvestorId })
            .OrderBy(static group => group.Key.CapitalAccountId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static group => group.Key.InvestorId, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groupedImpacts)
        {
            var netCapitalActivity = group.Sum(static item => item.NetCapitalAccountImpact);
            var grossAmount = group.Sum(static item => Math.Abs(item.NetCapitalAccountImpact));
            var ledgerEntryIds = group
                .SelectMany(static item => item.LedgerEntryIds)
                .Distinct()
                .Order()
                .ToArray();
            var impactKey = ledgerEntryIds.Length == 0
                ? journalEntryId.ToString("D")
                : string.Join("-", ledgerEntryIds.Select(static id => id.ToString("D")));

            yield return new PrivateCapitalCapitalAccountSubledgerSource(
                $"capital-account-subledger:{group.Key.CapitalAccountId}:{fundEvent.FundEventId}:{impactKey}".ToLowerInvariant(),
                group.Key.CapitalAccountId,
                NormalizeOptional(group.Key.InvestorId),
                currency,
                fundEvent.FundEventId,
                fundEvent.FundEventType,
                entryType,
                approvalState,
                journalEntryId,
                effectiveDate,
                grossAmount,
                netCapitalActivity,
                memo,
                evidenceLinks,
                issues,
                fundEvent.LastPostedAt,
                true);
        }
    }

    private static PrivateCapitalFundEventDto MapPostedFundEvent(
        PrivateCapitalFundEventLedgerEvent fundEvent,
        IReadOnlyDictionary<Guid, string> journalEntryCurrencies)
    {
        var entryType = MapPrivateCapitalEntryType(fundEvent.FundEventType);
        var currency = ResolvePostedEventCurrency(fundEvent, journalEntryCurrencies);
        var evidenceLinks = BuildPostedFundEventEvidenceLinks(fundEvent);
        var issues = MapPostedIssues(fundEvent);
        var grossAmount = fundEvent.LedgerImpacts.Count > 0
            ? fundEvent.LedgerImpacts.Max(static item => Math.Max(Math.Abs(item.TotalDebits), Math.Abs(item.TotalCredits)))
            : Math.Abs(fundEvent.CapitalAccountImpacts.Sum(static item => item.NetCapitalAccountImpact));
        var netCapitalActivity = fundEvent.CapitalAccountImpacts.Count > 0
            ? fundEvent.CapitalAccountImpacts.Sum(static item => item.NetCapitalAccountImpact)
            : CalculateNetCapitalActivity(entryType, grossAmount);

        return new PrivateCapitalFundEventDto(
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            entryType,
            MapPostedApprovalState(fundEvent.ApprovalState, fundEvent.HasCriticalIssues),
            fundEvent.JournalEntryIds.FirstOrDefault(),
            fundEvent.EffectiveDate ?? DateOnly.FromDateTime(fundEvent.FirstPostedAt.UtcDateTime),
            NormalizeOptional(fundEvent.CapitalAccountId) ?? "capital-account:unassigned",
            NormalizeOptional(fundEvent.InvestorId),
            currency,
            grossAmount,
            netCapitalActivity,
            fundEvent.LedgerImpacts.FirstOrDefault()?.Description ?? fundEvent.FundEventType,
            NormalizeOptional(fundEvent.PaymentIntentId),
            NormalizeOptional(fundEvent.SettlementReference),
            evidenceLinks,
            issues,
            fundEvent.LastPostedAt,
            IsPosted: true,
            ApprovalId: NormalizeOptional(fundEvent.ApprovalId));
    }

    private static IReadOnlyList<string> BuildPostedFundEventEvidenceLinks(
        PrivateCapitalFundEventLedgerEvent fundEvent)
        => fundEvent.EvidenceLinks
            .Concat(fundEvent.ReportOutputs.SelectMany(static item => item.EvidenceLinks))
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<PrivateCapitalLedgerImpactDto> MapPostedLedgerImpacts(
        PrivateCapitalFundEventLedgerEvent fundEvent,
        IReadOnlyDictionary<Guid, string> journalEntryCurrencies)
    {
        var approvalState = MapPostedApprovalState(fundEvent.ApprovalState, fundEvent.HasCriticalIssues);
        var currency = ResolvePostedEventCurrency(fundEvent, journalEntryCurrencies);
        var evidenceLinks = fundEvent.EvidenceLinks
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var issues = MapPostedIssues(fundEvent);
        var effectiveDate = fundEvent.EffectiveDate ?? DateOnly.FromDateTime(fundEvent.FirstPostedAt.UtcDateTime);
        var capitalAccountId = NormalizeOptional(fundEvent.CapitalAccountId) ?? "capital-account:unassigned";

        foreach (var impact in fundEvent.LedgerImpacts)
        {
            var capitalAccountImpact = ResolveLedgerImpactCapitalAccount(fundEvent, impact);
            yield return new PrivateCapitalLedgerImpactDto(
                impact.LedgerImpactId,
                impact.JournalEntryId,
                fundEvent.FundEventId,
                fundEvent.FundEventType,
                capitalAccountImpact?.CapitalAccountId ?? capitalAccountId,
                NormalizeOptional(capitalAccountImpact?.InvestorId) ?? NormalizeOptional(fundEvent.InvestorId),
                approvalState,
                effectiveDate,
                currency,
                impact.TotalDebits,
                impact.TotalCredits,
                impact.Imbalance,
                impact.Lines.Count,
                impact.IsBalanced,
                fundEvent.IsPostingReady,
                evidenceLinks,
                impact.Lines.Select(line => MapPostedLedgerLine(line, currency)).ToArray(),
                issues);
        }
    }

    private static PrivateCapitalFundEventCapitalAccountImpact? ResolveLedgerImpactCapitalAccount(
        PrivateCapitalFundEventLedgerEvent fundEvent,
        PrivateCapitalFundEventLedgerImpact ledgerImpact)
    {
        var ledgerEntryIds = ledgerImpact.Lines
            .Select(static line => line.LedgerEntryId)
            .ToHashSet();
        if (ledgerEntryIds.Count == 0)
        {
            return null;
        }

        var matches = fundEvent.CapitalAccountImpacts
            .Where(impact => impact.LedgerEntryIds.Any(ledgerEntryIds.Contains))
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static PrivateCapitalLedgerLineImpactDto MapPostedLedgerLine(
        PrivateCapitalFundEventLedgerLine line,
        string currency)
    {
        var side = line.Debit > 0m ? AccountingTemplateLineSideDto.Debit : AccountingTemplateLineSideDto.Credit;
        return new PrivateCapitalLedgerLineImpactDto(
            line.LedgerEntryId.ToString("D"),
            line.AccountName,
            side,
            Math.Abs(line.Debit > 0m ? line.Debit : line.Credit),
            currency,
            NormalizeOptional(line.FinancialAccountId),
            null,
            NormalizeOptional(line.Symbol),
            null);
    }

    private static string ResolvePostedEventCurrency(
        PrivateCapitalFundEventLedgerEvent fundEvent,
        IReadOnlyDictionary<Guid, string> journalEntryCurrencies)
    {
        var currencies = fundEvent.JournalEntryIds
            .Select(id => journalEntryCurrencies.TryGetValue(id, out var currency) ? NormalizeCurrency(currency) : null)
            .Where(static currency => !string.IsNullOrWhiteSpace(currency))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return currencies.Length == 0 ? "USD" : currencies[0]!;
    }

    private static IReadOnlyList<PrivateCapitalReportOutputDto> BuildPostedReportOutputs(
        string fundProfileId,
        Guid? ledgerBookId,
        IReadOnlyList<PrivateCapitalFundEventLedgerEvent> postedEvents,
        IReadOnlyDictionary<Guid, string> journalEntryCurrencies,
        IReadOnlyList<ReportPackWorkflowRecordDto> reportPackWorkflowRecords)
    {
        if (postedEvents.Count == 0)
        {
            return [];
        }

        var records = reportPackWorkflowRecords
            .Where(record => string.Equals(record.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (records.Length == 0)
        {
            return postedEvents
                .Select(fundEvent => BuildMissingPostedReportOutput(fundProfileId, ledgerBookId, fundEvent, journalEntryCurrencies))
                .ToArray();
        }

        return postedEvents
            .SelectMany(fundEvent =>
            {
                var matched = records
                    .Where(record => MatchesPostedFundEventReport(record, fundEvent))
                    .Select(record => BuildPostedReportOutput(fundProfileId, ledgerBookId, fundEvent, record, journalEntryCurrencies))
                    .ToArray();

                return matched.Length > 0
                    ? matched
                    : [BuildMissingPostedReportOutput(fundProfileId, ledgerBookId, fundEvent, journalEntryCurrencies)];
            })
            .ToArray();
    }

    private static int CountPublishedReportOutputs(
        string fundProfileId,
        IReadOnlyList<PrivateCapitalFundEventLedgerEvent> postedEvents,
        IReadOnlyList<ReportPackWorkflowRecordDto> reportPackWorkflowRecords)
    {
        var publishedOutputKeys = postedEvents
            .SelectMany(static fundEvent => fundEvent.ReportOutputs
                .Where(static output => output.IsPublished)
                .Select(output => BuildPublishedReportOutputKey(fundEvent.FundEventId, output.ReportId, output.ReportOutputId)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var workflowPublishedOutputKeys = reportPackWorkflowRecords
            .Where(record => string.Equals(record.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            .Where(IsPublishedReportPack)
            .SelectMany(record => postedEvents
                .Where(fundEvent => MatchesPostedFundEventReport(record, fundEvent))
                .Select(fundEvent => BuildPublishedReportOutputKey(fundEvent.FundEventId, record.ReportId.ToString("D"), null)));
        publishedOutputKeys.UnionWith(workflowPublishedOutputKeys);

        return publishedOutputKeys.Count;
    }

    private static string BuildPublishedReportOutputKey(string fundEventId, string? reportPackId, string? reportOutputId)
    {
        var normalizedFundEventId = string.IsNullOrWhiteSpace(fundEventId) ? "unknown-fund-event" : fundEventId.Trim();
        var normalizedReportPackId = NormalizeOptional(reportPackId);
        if (normalizedReportPackId is not null)
        {
            return $"{normalizedFundEventId}:report-pack:{normalizedReportPackId}";
        }

        var normalizedReportOutputId = NormalizeOptional(reportOutputId);
        return normalizedReportOutputId is null
            ? $"{normalizedFundEventId}:report-output:unknown"
            : $"{normalizedFundEventId}:report-output:{normalizedReportOutputId}";
    }

    private static IReadOnlyDictionary<string, bool> BuildPostingReadyByFundEventId(
        IReadOnlyList<PrivateCapitalLedgerImpactDto> ledgerImpacts)
        => ledgerImpacts
            .GroupBy(static item => item.FundEventId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Any() && group.All(static item => item.IsPostingReady),
                StringComparer.OrdinalIgnoreCase);

    private static PrivateCapitalReportOutputDto BuildPostedReportOutput(
        string fundProfileId,
        Guid? ledgerBookId,
        PrivateCapitalFundEventLedgerEvent fundEvent,
        ReportPackWorkflowRecordDto record,
        IReadOnlyDictionary<Guid, string> journalEntryCurrencies)
    {
        var currency = ResolvePostedEventCurrency(fundEvent, journalEntryCurrencies);
        var matchedProvenance = (record.LineProvenance ?? [])
            .Where(line => MatchesPostedFundEventLine(line, fundEvent))
            .ToArray();
        var accountScope = ResolvePostedReportOutputAccountScope(fundEvent, record, matchedProvenance);
        var reportEvidenceLinks = MergeEvidenceLinks(
            record.Publication?.EvidenceLinks
                .Select(link => link.Route ?? link.EvidenceId)
                .ToArray() ?? [],
            matchedProvenance
                .Select(static line => line.EvidenceId)
                .ToArray());
        var evidenceLinks = MergeEvidenceLinks(
            fundEvent.EvidenceLinks,
            record.Publication?.EvidenceLinks
                .Select(link => link.Route ?? link.EvidenceId)
                .ToArray());
        evidenceLinks = MergeEvidenceLinks(
            evidenceLinks,
            matchedProvenance
                .Select(static line => line.EvidenceId)
                .ToArray());
        var validationIssues = new List<AccountingConfigurationValidationIssueDto>();
        if (!IsPublishedReportPack(record))
        {
            validationIssues.Add(Issue(
                "private-capital.report-output-publication-pending",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Posted private-capital fund event has a report-pack workflow that is not published yet.",
                fundEvent.FundEventId,
                "Approve and publish the governed report pack before stakeholder package delivery."));
        }

        if (reportEvidenceLinks.Count == 0)
        {
            validationIssues.Add(Issue(
                "private-capital.report-output-evidence-missing",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Posted private-capital report output is missing retained report evidence links.",
                fundEvent.FundEventId,
                "Attach retained publication evidence before relying on report output."));
        }

        if (!fundEvent.IsPostingReady)
        {
            validationIssues.Add(Issue(
                "private-capital.report-output-posting-not-ready",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Posted private-capital report output is not ready because the linked fund event is not posting-ready.",
                fundEvent.FundEventId,
                "Repair approval, evidence, ledger impact, or capital-account impact before relying on report output."));
        }

        var isReportReady = fundEvent.IsPostingReady && IsReadyReportPack(record) && reportEvidenceLinks.Count > 0;
        var approvalRoute = BuildPostedReportOutputApprovalRoute(fundProfileId, fundEvent);
        var reportOutputId = $"report-output:{fundEvent.FundEventId}:{record.ReportId:D}".ToLowerInvariant();
        var reportOutputRoute = PrivateCapitalActivityRouteBuilder.BuildReportOutputRoute(
            fundProfileId,
            ledgerBookId,
            reportOutputId,
            fundEvent.FundEventId,
            accountScope.CapitalAccountId,
            accountScope.InvestorId);
        var readiness = BuildReportOutputReadiness(
            isReportReady,
            IsPublishedReportPack(record) && record.Publication is not null,
            validationIssues,
            reportOutputRoute,
            PrivateCapitalActivityRouteBuilder.BuildEvidenceRoute(fundEvent.FundEventId),
            approvalRoute);
        return new PrivateCapitalReportOutputDto(
            reportOutputId,
            "GovernedReportPack",
            $"{record.TemplateId.Name} v{record.TemplateId.Version}",
            $"/api/fund-structure/report-packs/{Uri.EscapeDataString(record.ReportId.ToString("D"))}",
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            accountScope.CapitalAccountId,
            accountScope.InvestorId,
            MapPostedWorkflowState(record.State),
            fundEvent.EffectiveDate ?? DateOnly.FromDateTime(fundEvent.FirstPostedAt.UtcDateTime),
            currency,
            accountScope.NetCapitalActivity,
            evidenceLinks.Count,
            evidenceLinks,
            isReportReady,
            validationIssues
                .OrderByDescending(issue => issue.Severity)
                .ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            IsPublished: IsPublishedReportPack(record) && record.Publication is not null,
            ReportPackId: record.ReportId.ToString("D"),
            ReportWorkflowState: record.State.ToString(),
            PublicationManifestId: NormalizeOptional(record.Publication?.ManifestId),
            RetainedManifestPath: NormalizeOptional(record.Publication?.RetainedManifestPath),
            PublicationEvidenceHash: NormalizeOptional(record.Publication?.EvidenceHash),
            PublishedAtUtc: record.Publication?.SignedOffAt,
            PublishedBy: NormalizeOptional(record.Publication?.SignedOffBy),
            ReportLineProvenanceCount: matchedProvenance.Length,
            ReportOutputRoute: reportOutputRoute,
            FundEventRecordRoute: PrivateCapitalActivityRouteBuilder.BuildFundEventRecordRoute(
                fundProfileId,
                ledgerBookId,
                fundEvent.FundEventId),
            CapitalAccountSubledgerRoute: PrivateCapitalActivityRouteBuilder.BuildCapitalAccountSubledgerRoute(
                fundProfileId,
                ledgerBookId,
                accountScope.CapitalAccountId,
                accountScope.InvestorId,
                currency),
            EvidenceRoute: PrivateCapitalActivityRouteBuilder.BuildEvidenceRoute(fundEvent.FundEventId),
            ApprovalRoute: approvalRoute,
            ReadinessLabel: readiness.Label,
            ReadinessReason: readiness.Reason,
            NextAction: readiness.NextAction,
            NextActionRoute: readiness.NextActionRoute);
    }

    private static PrivateCapitalReportOutputDto BuildMissingPostedReportOutput(
        string fundProfileId,
        Guid? ledgerBookId,
        PrivateCapitalFundEventLedgerEvent fundEvent,
        IReadOnlyDictionary<Guid, string> journalEntryCurrencies)
    {
        var currency = ResolvePostedEventCurrency(fundEvent, journalEntryCurrencies);
        var accountScope = ResolveMissingPostedReportOutputAccountScope(fundEvent);
        var evidenceLinks = fundEvent.EvidenceLinks
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var reportOutputId = $"report-output:{fundEvent.FundEventId}:governed-report-pack-pending".ToLowerInvariant();
        var reportOutputRoute = PrivateCapitalActivityRouteBuilder.BuildReportOutputRoute(
            fundProfileId,
            ledgerBookId,
            reportOutputId,
            fundEvent.FundEventId,
            accountScope.CapitalAccountId,
            accountScope.InvestorId);
        var evidenceRoute = PrivateCapitalActivityRouteBuilder.BuildEvidenceRoute(fundEvent.FundEventId);
        var approvalRoute = BuildPostedReportOutputApprovalRoute(fundProfileId, fundEvent);
        var validationIssues = new[]
        {
            Issue(
                "private-capital.report-output-missing",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Posted private-capital fund event is not linked to a governed report-pack workflow.",
                fundEvent.FundEventId,
                "Generate or attach the governed report pack before stakeholder package delivery.")
        };
        var readiness = BuildReportOutputReadiness(
            isReportReady: false,
            isPublished: false,
            validationIssues,
            reportOutputRoute,
            evidenceRoute,
            approvalRoute);
        return new PrivateCapitalReportOutputDto(
            reportOutputId,
            "GovernedReportPack",
            $"Governed report pack for {fundEvent.FundEventType}",
            PrivateCapitalActivityRouteBuilder.Build(
                fundProfileId,
                fundEvent.FundEventId,
                accountScope.CapitalAccountId,
                accountScope.InvestorId),
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            accountScope.CapitalAccountId,
            accountScope.InvestorId,
            MapPostedApprovalState(fundEvent.ApprovalState, fundEvent.HasCriticalIssues),
            fundEvent.EffectiveDate ?? DateOnly.FromDateTime(fundEvent.FirstPostedAt.UtcDateTime),
            currency,
            accountScope.NetCapitalActivity,
            evidenceLinks.Length,
            evidenceLinks,
            false,
            validationIssues,
            ReportWorkflowState: "Missing",
            ReportOutputRoute: reportOutputRoute,
            FundEventRecordRoute: PrivateCapitalActivityRouteBuilder.BuildFundEventRecordRoute(
                fundProfileId,
                ledgerBookId,
                fundEvent.FundEventId),
            CapitalAccountSubledgerRoute: PrivateCapitalActivityRouteBuilder.BuildCapitalAccountSubledgerRoute(
                fundProfileId,
                ledgerBookId,
                accountScope.CapitalAccountId,
                accountScope.InvestorId,
                currency),
            EvidenceRoute: evidenceRoute,
            ApprovalRoute: approvalRoute,
            ReadinessLabel: readiness.Label,
            ReadinessReason: readiness.Reason,
            NextAction: readiness.NextAction,
            NextActionRoute: readiness.NextActionRoute);
    }

    private static string? BuildPostedReportOutputApprovalRoute(
        string fundProfileId,
        PrivateCapitalFundEventLedgerEvent fundEvent)
    {
        if (fundEvent.JournalEntryIds.Count == 0)
        {
            return null;
        }

        return PrivateCapitalActivityRouteBuilder.BuildApprovalRoute(
            fundProfileId,
            fundEvent.JournalEntryIds[0],
            NormalizeOptional(fundEvent.ApprovalId));
    }

    private static PrivateCapitalReportOutputReadinessProjection BuildReportOutputReadiness(
        bool isReportReady,
        bool isPublished,
        IReadOnlyList<AccountingConfigurationValidationIssueDto> validationIssues,
        string reportOutputRoute,
        string evidenceRoute,
        string? approvalRoute)
    {
        if (isPublished && isReportReady)
        {
            return new(
                "Published",
                "The report output is published with retained report evidence and linked posting-ready fund-event impact.",
                "Open published report",
                reportOutputRoute);
        }

        if (isReportReady)
        {
            return new(
                "Ready",
                "The report output has retained evidence and linked posting-ready fund-event impact.",
                "Review report output",
                reportOutputRoute);
        }

        var issue = validationIssues
            .OrderByDescending(static item => item.Severity)
            .ThenBy(static item => item.Code, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return issue?.Code switch
        {
            "manual-je.private-capital-report-evidence-missing" or
            "private-capital.report-output-evidence-missing" => new(
                "Evidence missing",
                issue.Message,
                "Attach retained evidence",
                evidenceRoute),
            "manual-je.private-capital-report-approval-pending" => new(
                "Approval pending",
                issue.Message,
                "Submit or review approval",
                approvalRoute ?? reportOutputRoute),
            "manual-je.private-capital-report-ledger-impact-not-ready" or
            "private-capital.report-output-posting-not-ready" => new(
                "Posting review",
                issue.Message,
                "Review ledger impact",
                reportOutputRoute),
            "private-capital.report-output-publication-pending" => new(
                "Publication pending",
                issue.Message,
                "Approve and publish report pack",
                reportOutputRoute),
            "private-capital.report-output-missing" => new(
                "Report output missing",
                issue.Message,
                "Generate governed report pack",
                reportOutputRoute),
            _ => new(
                "Report review",
                issue?.Message ?? "Report output readiness has not been satisfied.",
                "Prepare report output",
                reportOutputRoute)
        };
    }

    private static PrivateCapitalReportOutputAccountScope ResolvePostedReportOutputAccountScope(
        PrivateCapitalFundEventLedgerEvent fundEvent,
        ReportPackWorkflowRecordDto record,
        IReadOnlyList<ReportPackLineProvenanceDto> matchedProvenance)
    {
        var targetCapitalAccountId = NormalizeOptional(record.FundAccountId);
        if (targetCapitalAccountId is not null)
        {
            var targetMatches = fundEvent.CapitalAccountImpacts
                .Where(impact => string.Equals(impact.CapitalAccountId, targetCapitalAccountId, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            if (targetMatches.Length == 1)
            {
                return BuildReportOutputAccountScope(targetMatches[0]);
            }
        }

        var provenanceLedgerEntryIds = matchedProvenance
            .SelectMany(EnumerateLineLedgerEntryIds)
            .ToHashSet();
        if (provenanceLedgerEntryIds.Count > 0)
        {
            var provenanceMatches = fundEvent.CapitalAccountImpacts
                .Where(impact => impact.LedgerEntryIds.Any(provenanceLedgerEntryIds.Contains))
                .Take(2)
                .ToArray();
            if (provenanceMatches.Length == 1)
            {
                return BuildReportOutputAccountScope(provenanceMatches[0]);
            }
        }

        return ResolveMissingPostedReportOutputAccountScope(fundEvent);
    }

    private static PrivateCapitalReportOutputAccountScope ResolveMissingPostedReportOutputAccountScope(
        PrivateCapitalFundEventLedgerEvent fundEvent)
    {
        if (fundEvent.CapitalAccountImpacts.Count == 1)
        {
            return BuildReportOutputAccountScope(fundEvent.CapitalAccountImpacts[0]);
        }

        var netCapitalActivity = fundEvent.CapitalAccountImpacts.Count > 0
            ? fundEvent.CapitalAccountImpacts.Sum(static item => item.NetCapitalAccountImpact)
            : CalculateNetCapitalActivity(
                MapPrivateCapitalEntryType(fundEvent.FundEventType),
                fundEvent.LedgerImpacts.Sum(static item => Math.Max(Math.Abs(item.TotalDebits), Math.Abs(item.TotalCredits))));
        var hasAmbiguousCapitalAccount = fundEvent.CapitalAccountImpacts.Count > 1 ||
            fundEvent.Issues.Any(static item => string.Equals(item.Code, "private-capital.capital-account-conflict", StringComparison.OrdinalIgnoreCase));
        return new PrivateCapitalReportOutputAccountScope(
            hasAmbiguousCapitalAccount
                ? "capital-account:unassigned"
                : NormalizeOptional(fundEvent.CapitalAccountId) ?? "capital-account:unassigned",
            hasAmbiguousCapitalAccount ? null : NormalizeOptional(fundEvent.InvestorId),
            netCapitalActivity);
    }

    private static PrivateCapitalReportOutputAccountScope BuildReportOutputAccountScope(
        PrivateCapitalFundEventCapitalAccountImpact impact)
        => new(
            impact.CapitalAccountId,
            NormalizeOptional(impact.InvestorId),
            impact.NetCapitalAccountImpact);

    private static IEnumerable<Guid> EnumerateLineLedgerEntryIds(ReportPackLineProvenanceDto line)
    {
        if (Guid.TryParse(line.LedgerEntryId, out var ledgerEntryId))
        {
            yield return ledgerEntryId;
        }

        if (Guid.TryParse(line.SourceId, out var sourceId))
        {
            yield return sourceId;
        }
    }

    private static bool MatchesPostedFundEventReport(
        ReportPackWorkflowRecordDto record,
        PrivateCapitalFundEventLedgerEvent fundEvent)
    {
        if ((record.LineProvenance ?? []).Any(line => MatchesPostedFundEventLine(line, fundEvent)))
        {
            return true;
        }

        return EnumerateReportPackEvidencePointers(record)
            .Any(pointer => MatchesPostedFundEventPointer(pointer, fundEvent));
    }

    private static bool MatchesPostedFundEventLine(
        ReportPackLineProvenanceDto line,
        PrivateCapitalFundEventLedgerEvent fundEvent)
    {
        if (MatchesPostedFundEventPointer(line.SourceId, fundEvent))
        {
            return true;
        }

        if (MatchesPostedFundEventPointer(line.EvidenceId, fundEvent) ||
            MatchesPostedFundEventPointer(line.ApprovalId, fundEvent))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(line.LedgerEntryId) &&
            MatchesAnyGuid(line.LedgerEntryId, fundEvent.LedgerEntryIds))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(line.SourceId) &&
            (MatchesAnyGuid(line.SourceId, fundEvent.LedgerEntryIds) ||
             MatchesAnyGuid(line.SourceId, fundEvent.JournalEntryIds)))
        {
            return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateReportPackEvidencePointers(ReportPackWorkflowRecordDto record)
    {
        foreach (var pointer in EnumerateReportPackEvidenceLinks(record.Publication?.EvidenceLinks))
        {
            yield return pointer;
        }

        foreach (var pointer in EnumerateReportPackEvidenceLinks(record.Restatement?.EvidenceLinks))
        {
            yield return pointer;
        }

        foreach (var pointer in EnumerateReportPackEvidenceLinks(record.Rejection?.EvidenceLinks))
        {
            yield return pointer;
        }

        if (!string.IsNullOrWhiteSpace(record.Publication?.ManifestId))
        {
            yield return record.Publication.ManifestId;
        }

        if (!string.IsNullOrWhiteSpace(record.Publication?.RetainedManifestPath))
        {
            yield return record.Publication.RetainedManifestPath;
        }
    }

    private static IEnumerable<string> EnumerateReportPackEvidenceLinks(
        IReadOnlyList<ReportPackEvidenceLinkDto>? evidenceLinks)
    {
        foreach (var link in evidenceLinks ?? [])
        {
            if (!string.IsNullOrWhiteSpace(link.EvidenceId))
            {
                yield return link.EvidenceId;
            }

            if (!string.IsNullOrWhiteSpace(link.Label))
            {
                yield return link.Label;
            }

            if (!string.IsNullOrWhiteSpace(link.Route))
            {
                yield return link.Route;
            }

            if (!string.IsNullOrWhiteSpace(link.Source))
            {
                yield return link.Source;
            }
        }
    }

    private static bool MatchesPostedFundEventPointer(
        string? value,
        PrivateCapitalFundEventLedgerEvent fundEvent)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var pointer = value.Trim();
        if (pointer.Contains(fundEvent.FundEventId, StringComparison.OrdinalIgnoreCase) ||
            pointer.Contains(Uri.EscapeDataString(fundEvent.FundEventId), StringComparison.OrdinalIgnoreCase) ||
            MatchesAnyGuid(pointer, fundEvent.LedgerEntryIds) ||
            MatchesAnyGuid(pointer, fundEvent.JournalEntryIds))
        {
            return true;
        }

        try
        {
            var unescaped = Uri.UnescapeDataString(pointer);
            return !string.Equals(unescaped, pointer, StringComparison.Ordinal) &&
                (unescaped.Contains(fundEvent.FundEventId, StringComparison.OrdinalIgnoreCase) ||
                 MatchesAnyGuid(unescaped, fundEvent.LedgerEntryIds) ||
                 MatchesAnyGuid(unescaped, fundEvent.JournalEntryIds));
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static bool MatchesAnyGuid(string value, IReadOnlyList<Guid> ids)
    {
        if (ids.Count == 0 || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return ids.Any(id => value.Contains(id.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            value.Contains(id.ToString("N"), StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<AccountingConfigurationValidationIssueDto> MapPostedIssues(
        PrivateCapitalFundEventLedgerEvent fundEvent)
        => fundEvent.Issues
            .Select(issue => Issue(
                issue.Code,
                MapPostedIssueSeverity(issue.Severity),
                issue.Message,
                fundEvent.FundEventId,
                "Review posted journal metadata, retained evidence, approval, and report output before relying on capital-account reporting."))
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static AccountingConfigurationValidationSeverityDto MapPostedIssueSeverity(
        PrivateCapitalFundEventIssueSeverity severity)
        => severity switch
        {
            PrivateCapitalFundEventIssueSeverity.Critical => AccountingConfigurationValidationSeverityDto.Critical,
            PrivateCapitalFundEventIssueSeverity.Warning => AccountingConfigurationValidationSeverityDto.Warning,
            _ => AccountingConfigurationValidationSeverityDto.Info
        };

    private static ManualJournalEntryStatusDto MapPostedApprovalState(
        PrivateCapitalFundEventApprovalState state,
        bool hasCriticalIssues = false)
        => state switch
        {
            PrivateCapitalFundEventApprovalState.Approved or PrivateCapitalFundEventApprovalState.Posted => ManualJournalEntryStatusDto.Approved,
            PrivateCapitalFundEventApprovalState.Submitted => ManualJournalEntryStatusDto.Submitted,
            PrivateCapitalFundEventApprovalState.Rejected => ManualJournalEntryStatusDto.Rejected,
            _ => hasCriticalIssues ? ManualJournalEntryStatusDto.NeedsFix : ManualJournalEntryStatusDto.Draft
        };

    private static ManualJournalEntryStatusDto MapPostedWorkflowState(ReportPackWorkflowStateDto state)
        => state switch
        {
            ReportPackWorkflowStateDto.Approved or ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Restated or ReportPackWorkflowStateDto.Archived => ManualJournalEntryStatusDto.Approved,
            ReportPackWorkflowStateDto.Validated or ReportPackWorkflowStateDto.InReview or ReportPackWorkflowStateDto.PendingApproval => ManualJournalEntryStatusDto.Submitted,
            ReportPackWorkflowStateDto.Rejected => ManualJournalEntryStatusDto.Rejected,
            _ => ManualJournalEntryStatusDto.Draft
        };

    private static bool IsReadyReportPack(ReportPackWorkflowRecordDto record)
        => record.State is ReportPackWorkflowStateDto.Approved or ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Restated;

    private static bool IsPublishedReportPack(ReportPackWorkflowRecordDto record)
        => record.State is ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Restated;

    private static ManualJournalEntryTypeDto MapPrivateCapitalEntryType(string? fundEventType)
        => fundEventType?.Trim().ToLowerInvariant() switch
        {
            "capitalcall" or "capital call" or "capital-call" => ManualJournalEntryTypeDto.CapitalCall,
            "distribution" => ManualJournalEntryTypeDto.Distribution,
            "subscription" => ManualJournalEntryTypeDto.Subscription,
            "redemption" => ManualJournalEntryTypeDto.Redemption,
            "lptransfer" or "lp transfer" or "lp-transfer" or "transfer" => ManualJournalEntryTypeDto.LpTransfer,
            "managementfee" or "management fee" or "management-fee" => ManualJournalEntryTypeDto.ManagementFee,
            _ => ManualJournalEntryTypeDto.General
        };

    private static bool IsPrivateCapitalActivityCandidate(
        ManualJournalEntryTypeDto entryType,
        TreasuryLedgerContextDto? context)
        => RequiresPrivateCapitalTreasuryContext(entryType) ||
            context is not null && (
                !string.IsNullOrWhiteSpace(context.FundEventId) ||
                !string.IsNullOrWhiteSpace(context.FundEventType) ||
                !string.IsNullOrWhiteSpace(context.CapitalAccountId) ||
                !string.IsNullOrWhiteSpace(context.InvestorId));

    private static decimal CalculateNetCapitalActivity(ManualJournalEntryTypeDto entryType, decimal grossAmount)
        => entryType switch
        {
            ManualJournalEntryTypeDto.CapitalCall => grossAmount,
            ManualJournalEntryTypeDto.Subscription => grossAmount,
            ManualJournalEntryTypeDto.Distribution => -grossAmount,
            ManualJournalEntryTypeDto.Redemption => -grossAmount,
            ManualJournalEntryTypeDto.ManagementFee => -grossAmount,
            _ => 0m
        };

    private static PrivateCapitalLedgerImpactDto BuildPrivateCapitalLedgerImpact(
        ManualJournalEntryDraftDto draft,
        TreasuryLedgerContextDto context,
        IReadOnlyList<string> evidenceLinks)
    {
        var currency = string.IsNullOrWhiteSpace(draft.Currency) ? "USD" : draft.Currency.Trim().ToUpperInvariant();
        var lines = draft.Lines
            .Select(line => new PrivateCapitalLedgerLineImpactDto(
                line.LineId,
                line.AccountPath,
                line.Side,
                Math.Abs(line.Amount),
                string.IsNullOrWhiteSpace(line.Currency) ? currency : line.Currency.Trim().ToUpperInvariant(),
                NormalizeOptional(line.EntityId),
                line.SecurityId,
                NormalizeOptional(line.SecurityDisplayName),
                NormalizeOptional(line.EvidenceLink)))
            .ToArray();
        var totalDebits = draft.TotalDebits != 0m
            ? draft.TotalDebits
            : draft.Lines.Where(line => line.Side == AccountingTemplateLineSideDto.Debit).Sum(line => Math.Abs(line.Amount));
        var totalCredits = draft.TotalCredits != 0m
            ? draft.TotalCredits
            : draft.Lines.Where(line => line.Side == AccountingTemplateLineSideDto.Credit).Sum(line => Math.Abs(line.Amount));
        var imbalance = draft.Imbalance != 0m ? draft.Imbalance : totalDebits - totalCredits;
        var isBalanced = Math.Abs(imbalance) <= BalanceTolerance;
        var issues = draft.ValidationIssues.ToList();

        if (!isBalanced)
        {
            issues.Add(Issue(
                "manual-je.private-capital-ledger-impact-unbalanced",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Private-capital ledger impact is not balanced.",
                draft.JournalEntryId.ToString("D"),
                "Balance debit and credit lines before submitting or posting the fund event."));
        }

        if (evidenceLinks.Count == 0)
        {
            issues.Add(Issue(
                "manual-je.private-capital-ledger-impact-evidence-missing",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Private-capital ledger impact is missing retained evidence links.",
                draft.JournalEntryId.ToString("D"),
                "Attach retained source, approval, or settlement evidence before approval or report output."));
        }

        if (draft.Status is not (ManualJournalEntryStatusDto.Submitted or ManualJournalEntryStatusDto.Approved))
        {
            issues.Add(Issue(
                "manual-je.private-capital-ledger-impact-approval-pending",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Private-capital ledger impact is not approval-ready because the journal entry has not been submitted or approved.",
                draft.JournalEntryId.ToString("D"),
                "Submit or approve the fund-event journal before posting or stakeholder package production."));
        }

        var isPostingReady =
            isBalanced &&
            evidenceLinks.Count > 0 &&
            draft.Status is ManualJournalEntryStatusDto.Submitted or ManualJournalEntryStatusDto.Approved &&
            issues.All(issue => issue.Severity != AccountingConfigurationValidationSeverityDto.Critical);

        return new PrivateCapitalLedgerImpactDto(
            $"ledger-impact:{context.FundEventId}:{draft.JournalEntryId:D}".ToLowerInvariant(),
            draft.JournalEntryId,
            context.FundEventId!,
            context.FundEventType!,
            context.CapitalAccountId!,
            NormalizeOptional(context.InvestorId),
            draft.Status,
            context.EffectiveDate!.Value,
            currency,
            totalDebits,
            totalCredits,
            imbalance,
            lines.Length,
            isBalanced,
            isPostingReady,
            evidenceLinks,
            lines,
            issues
                .OrderByDescending(issue => issue.Severity)
                .ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static PrivateCapitalReportOutputDto BuildPrivateCapitalReportOutput(
        string fundProfileId,
        Guid? ledgerBookId,
        PrivateCapitalFundEventDto fundEvent,
        IReadOnlyDictionary<string, bool> postingReadyByFundEventId)
    {
        var reportOutputType = fundEvent.EntryType switch
        {
            ManualJournalEntryTypeDto.CapitalCall => "CapitalCallNotice",
            ManualJournalEntryTypeDto.Distribution => "DistributionNotice",
            ManualJournalEntryTypeDto.Subscription => "SubscriptionStatement",
            ManualJournalEntryTypeDto.Redemption => "RedemptionStatement",
            ManualJournalEntryTypeDto.LpTransfer => "CapitalAccountTransferStatement",
            ManualJournalEntryTypeDto.ManagementFee => "ManagementFeeSupportPackage",
            _ => "PrivateCapitalActivityStatement"
        };
        var isPostingReady = postingReadyByFundEventId.TryGetValue(fundEvent.FundEventId, out var resolvedPostingReady) && resolvedPostingReady;
        var isReportReady =
            isPostingReady &&
            fundEvent.JournalStatus is ManualJournalEntryStatusDto.Submitted or ManualJournalEntryStatusDto.Approved &&
            fundEvent.EvidenceLinks.Count > 0 &&
            fundEvent.ValidationIssues.All(issue => issue.Severity != AccountingConfigurationValidationSeverityDto.Critical);
        var issues = fundEvent.ValidationIssues.ToList();
        if (fundEvent.EvidenceLinks.Count == 0)
        {
            issues.Add(Issue(
                "manual-je.private-capital-report-evidence-missing",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Private-capital report output is missing retained evidence links.",
                fundEvent.FundEventId,
                "Attach retained source, approval, or settlement evidence before stakeholder package publication."));
        }

        if (fundEvent.JournalStatus is not (ManualJournalEntryStatusDto.Submitted or ManualJournalEntryStatusDto.Approved))
        {
            issues.Add(Issue(
                "manual-je.private-capital-report-approval-pending",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Private-capital report output is not ready because the linked journal entry has not been submitted for approval.",
                fundEvent.FundEventId,
                "Submit or approve the fund-event journal before report-package production."));
        }

        if (!isPostingReady)
        {
            issues.Add(Issue(
                "manual-je.private-capital-report-ledger-impact-not-ready",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Private-capital report output is not ready because the linked ledger and capital-account impact is not posting-ready.",
                fundEvent.FundEventId,
                "Resolve ledger-impact readiness before producing or publishing the report output."));
        }

        var reportOutputId = $"report-output:{fundEvent.FundEventId}:{reportOutputType}".ToLowerInvariant();
        var reportRoute = PrivateCapitalActivityRouteBuilder.Build(
            fundProfileId,
            fundEvent.FundEventId,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId);
        var reportOutputRoute = PrivateCapitalActivityRouteBuilder.BuildReportOutputRoute(
            fundProfileId,
            ledgerBookId,
            reportOutputId,
            fundEvent.FundEventId,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId);
        var evidenceRoute = PrivateCapitalActivityRouteBuilder.BuildEvidenceRoute(fundEvent.FundEventId);
        var approvalRoute = PrivateCapitalActivityRouteBuilder.BuildApprovalRoute(
            fundProfileId,
            fundEvent.JournalEntryId,
            fundEvent.ApprovalId);
        var orderedIssues = issues
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var readiness = BuildReportOutputReadiness(
            isReportReady,
            isPublished: false,
            orderedIssues,
            reportOutputRoute,
            evidenceRoute,
            approvalRoute);
        return new PrivateCapitalReportOutputDto(
            reportOutputId,
            reportOutputType,
            $"{reportOutputType} for {fundEvent.FundEventType}",
            reportRoute,
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            fundEvent.JournalStatus,
            fundEvent.EffectiveDate,
            fundEvent.Currency,
            fundEvent.NetCapitalActivity,
            fundEvent.EvidenceLinks.Count,
            fundEvent.EvidenceLinks,
            isReportReady,
            orderedIssues,
            IsPublished: false,
            ReportWorkflowState: fundEvent.JournalStatus.ToString(),
            ReportOutputRoute: reportOutputRoute,
            FundEventRecordRoute: PrivateCapitalActivityRouteBuilder.BuildFundEventRecordRoute(
                fundProfileId,
                ledgerBookId,
                fundEvent.FundEventId),
            CapitalAccountSubledgerRoute: PrivateCapitalActivityRouteBuilder.BuildCapitalAccountSubledgerRoute(
                fundProfileId,
                ledgerBookId,
                fundEvent.CapitalAccountId,
                fundEvent.InvestorId,
                fundEvent.Currency),
            EvidenceRoute: evidenceRoute,
            ApprovalRoute: approvalRoute,
            ReadinessLabel: readiness.Label,
            ReadinessReason: readiness.Reason,
            NextAction: readiness.NextAction,
            NextActionRoute: readiness.NextActionRoute);
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
        EnsureHumanOrigin(request.ActionOrigin, "submit manual journal entries for approval");
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

    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin, string action)
    {
        if (actionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new InvalidOperationException(
                $"Reviewed automation cannot {action}; a human operator approval is required.");
        }
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
        var entryType = Enum.IsDefined(draft.EntryType)
            ? draft.EntryType
            : ManualJournalEntryTypeDto.General;
        var treasuryContext = NormalizeTreasuryContext(draft.TreasuryContext, draft.AccountingDate);

        if (!Enum.IsDefined(draft.EntryType))
        {
            issues.Add(Issue("manual-je.entry-type-invalid", AccountingConfigurationValidationSeverityDto.Critical, "Manual journal entry type is not supported.", "entryType", "Select a supported entry type before submitting approval."));
        }

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

internal sealed record PostedPrivateCapitalActivityProjection(
    PrivateCapitalFundEventLedgerProjection Projection,
    IReadOnlyDictionary<Guid, string> JournalEntryCurrencies);
