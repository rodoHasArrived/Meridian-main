using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Shared.Services;

public interface IAccountingConfigurationStore
{
    Task<AccountingConfigurationWorkspaceDto?> GetAsync(string fundProfileId, CancellationToken ct = default);

    Task SaveAsync(AccountingConfigurationWorkspaceDto workspace, CancellationToken ct = default);
}

public interface IAccountingActionAuditStore
{
    Task AppendAsync(AccountingActionAuditEventDto auditEvent, CancellationToken ct = default);

    Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default);
}

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

        return await SaveWithAuditAsync(workspace, beforeHash, request.Actor, "chart-node-upserted", null, request.CorrelationId, request.EvidenceLinks, ct).ConfigureAwait(false);
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

        return await SaveWithAuditAsync(workspace, beforeHash, request.Actor, "journal-template-upserted", null, request.CorrelationId, request.EvidenceLinks, ct).ConfigureAwait(false);
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

        return await SaveWithAuditAsync(workspace, beforeHash, request.Actor, "posting-rule-upserted", null, request.CorrelationId, request.EvidenceLinks, ct).ConfigureAwait(false);
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

        return await SaveWithAuditAsync(workspace, beforeHash, request.Actor, "configuration-activated", request.LedgerBookId, request.CorrelationId, request.EvidenceLinks, ct).ConfigureAwait(false);
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
