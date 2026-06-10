using Meridian.Contracts.Extensibility;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Workflows;

namespace Meridian.Ui.Shared.Extensibility;

public sealed class WorkflowExtensibilityCatalogProvider : IExtensibilityCatalogProvider
{
    private static readonly ExtensibilityScopeDto GlobalScope = new(ExtensibilityScopeKindDto.Global, null, "Global workflow catalog");

    private static readonly IReadOnlyList<GovernedFoundationKindDto> WorkflowFoundations =
    [
        GovernedFoundationKindDto.AuditTrail,
        GovernedFoundationKindDto.SecurityModelFoundation,
        GovernedFoundationKindDto.DataLineageModel,
        GovernedFoundationKindDto.ApprovalEvidenceModel,
        GovernedFoundationKindDto.ImmutableRecordPreservation
    ];

    private static readonly IReadOnlyList<string> WorkflowGuardrails =
    [
        "Configures review sequence, task routing, launch targets, and evidence expectations only.",
        "Core object identity, lineage, approvals, audit trail, ledger integrity, and domain writes remain owned by governed services."
    ];

    private static readonly IReadOnlyList<string> ActionGuardrails =
    [
        "Configures an operator launch or review action only.",
        "Workflow actions must route to governed services and must not become domain write authority."
    ];

    private readonly IReadOnlyList<IWorkflowDefinitionProvider> _workflowProviders;

    public WorkflowExtensibilityCatalogProvider(IEnumerable<IWorkflowDefinitionProvider> workflowProviders)
    {
        var providers = workflowProviders?.ToArray() ?? [];
        _workflowProviders = providers.Length == 0 ? [new BuiltInWorkflowDefinitionProvider()] : providers;
    }

    public IReadOnlyList<ExtensibilityRegistrationDto> GetRegistrations()
    {
        var workflows = _workflowProviders
            .SelectMany(static provider => provider.GetWorkflowDefinitions())
            .GroupBy(static workflow => workflow.WorkflowId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last())
            .OrderBy(static workflow => workflow.WorkspaceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static workflow => workflow.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var registrations = new List<ExtensibilityRegistrationDto>(workflows.Length * 4);
        var seenActionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var workflow in workflows)
        {
            registrations.Add(CreateWorkflowRegistration(workflow));

            foreach (var action in workflow.Actions)
            {
                if (seenActionIds.Add(action.ActionId))
                {
                    registrations.Add(CreateActionRegistration(workflow, action));
                }
            }
        }

        return registrations;
    }

    private static ExtensibilityRegistrationDto CreateWorkflowRegistration(WorkflowDefinitionDto workflow)
        => new(
            $"workflow:{workflow.WorkflowId}",
            ExtensibilityConfigurationAreaDto.Workflow,
            ExtensibilityConfigurationStatusDto.Active,
            workflow.Title,
            workflow.Summary,
            ResolveOwningContext(workflow.WorkspaceId, workflow.EntryPageTag),
            GlobalScope,
            ResolveTargetCoreObjects(workflow.WorkspaceId, workflow.EntryPageTag, workflow.Actions.Select(static action => action.TargetPageTag)),
            WorkflowFoundations,
            [workflow.WorkflowId],
            MergeTags(workflow.EvidenceTags, workflow.MarketPatternTags),
            WorkflowGuardrails);

    private static ExtensibilityRegistrationDto CreateActionRegistration(WorkflowDefinitionDto workflow, WorkflowActionDto action)
        => new(
            $"workflow-action:{action.ActionId}",
            ExtensibilityConfigurationAreaDto.Workflow,
            ExtensibilityConfigurationStatusDto.Active,
            action.Label,
            action.Detail,
            ResolveOwningContext(workflow.WorkspaceId, action.TargetPageTag),
            GlobalScope,
            ResolveTargetCoreObjects(workflow.WorkspaceId, action.TargetPageTag, action.RoutePrefixes.Concat(action.RouteContains)),
            WorkflowFoundations,
            [workflow.WorkflowId, action.ActionId],
            workflow.EvidenceTags,
            ActionGuardrails);

    private static string ResolveOwningContext(string workspaceId, string targetPageTag)
    {
        var text = $"{workspaceId} {targetPageTag}";
        if (ContainsAny(text, "report")) return "Reporting";
        if (ContainsAny(text, "accounting", "ledger", "trialbalance", "capitalaccount")) return "Accounting";
        if (ContainsAny(text, "reconciliation", "operations", "close")) return "Operations Continuity";
        if (ContainsAny(text, "portfolio", "position", "exposure")) return "Portfolio";
        if (ContainsAny(text, "security", "instrument", "provider", "data", "backfill", "import")) return "Data";
        if (ContainsAny(text, "trading", "execution")) return "Trading";
        if (ContainsAny(text, "strategy", "backtest")) return "Strategy";
        if (ContainsAny(text, "setting", "access", "permission")) return "Settings";
        return string.IsNullOrWhiteSpace(workspaceId) ? "Workstation" : workspaceId;
    }

    private static IReadOnlyList<CoreFinancialObjectKindDto> ResolveTargetCoreObjects(string workspaceId, string targetPageTag, IEnumerable<string> routeHints)
    {
        var kinds = new HashSet<CoreFinancialObjectKindDto>();
        var text = string.Join(" ", new[] { workspaceId, targetPageTag }.Concat(routeHints));

        if (ContainsAny(text, "data", "provider", "backfill", "import", "source"))
        {
            Add(kinds, CoreFinancialObjectKindDto.Document, CoreFinancialObjectKindDto.Instrument, CoreFinancialObjectKindDto.Transaction);
        }

        if (ContainsAny(text, "portfolio", "position", "exposure", "holding"))
        {
            Add(kinds, CoreFinancialObjectKindDto.Position, CoreFinancialObjectKindDto.Valuation, CoreFinancialObjectKindDto.Account, CoreFinancialObjectKindDto.Instrument);
        }

        if (ContainsAny(text, "accounting", "ledger", "journal", "trialbalance", "capitalaccount", "fundevent"))
        {
            Add(kinds, CoreFinancialObjectKindDto.LedgerAccount, CoreFinancialObjectKindDto.JournalEntry, CoreFinancialObjectKindDto.CapitalAccount, CoreFinancialObjectKindDto.FundEvent);
        }

        if (ContainsAny(text, "reconciliation", "break", "exception", "case"))
        {
            Add(kinds, CoreFinancialObjectKindDto.Reconciliation, CoreFinancialObjectKindDto.Exception);
        }

        if (ContainsAny(text, "operation", "close", "approval", "task", "workflow"))
        {
            Add(kinds, CoreFinancialObjectKindDto.Task, CoreFinancialObjectKindDto.Reconciliation, CoreFinancialObjectKindDto.Exception);
        }

        if (ContainsAny(text, "report", "package", "delivery", "statement", "export"))
        {
            Add(kinds, CoreFinancialObjectKindDto.ReportPackage, CoreFinancialObjectKindDto.Document);
        }

        if (ContainsAny(text, "evidence", "audit", "manifest"))
        {
            Add(kinds, CoreFinancialObjectKindDto.AuditEvent, CoreFinancialObjectKindDto.Document);
        }

        if (ContainsAny(text, "trading", "execution", "order"))
        {
            Add(kinds, CoreFinancialObjectKindDto.Transaction, CoreFinancialObjectKindDto.Position, CoreFinancialObjectKindDto.Instrument);
        }

        if (ContainsAny(text, "setting", "access", "permission", "tenant", "entity"))
        {
            Add(kinds, CoreFinancialObjectKindDto.Tenant, CoreFinancialObjectKindDto.Entity, CoreFinancialObjectKindDto.Relationship, CoreFinancialObjectKindDto.Account);
        }

        if (kinds.Count == 0)
        {
            Add(kinds, CoreFinancialObjectKindDto.Task, CoreFinancialObjectKindDto.AuditEvent);
        }

        return kinds.OrderBy(static kind => kind).ToArray();
    }

    private static IReadOnlyList<string> MergeTags(IReadOnlyList<string> first, IReadOnlyList<string> second)
        => first
            .Concat(second)
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool ContainsAny(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static void Add(HashSet<CoreFinancialObjectKindDto> target, params CoreFinancialObjectKindDto[] kinds)
    {
        foreach (var kind in kinds)
        {
            target.Add(kind);
        }
    }
}
