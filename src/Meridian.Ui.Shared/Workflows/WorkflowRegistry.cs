using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Workflows;

/// <summary>
/// Indexes workflow definitions and resolves actions from ids, inbox kinds, and API routes.
/// </summary>
public sealed class WorkflowRegistry : IWorkflowActionCatalog
{
    private readonly IReadOnlyList<WorkflowDefinitionDto> _definitions;
    private readonly IReadOnlyList<WorkflowActionDto> _actions;
    private readonly Dictionary<string, WorkflowActionDto> _actionsById;
    private readonly Dictionary<OperatorWorkItemKindDto, WorkflowActionDto> _actionsByWorkItemKind;

    public WorkflowRegistry(IEnumerable<IWorkflowDefinitionProvider> providers)
    {
        var providerList = providers?.ToArray() ?? [];
        if (providerList.Length == 0)
        {
            providerList = [new BuiltInWorkflowDefinitionProvider()];
        }

        _definitions = providerList
            .SelectMany(static provider => provider.GetWorkflowDefinitions())
            .GroupBy(static definition => definition.WorkflowId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last())
            .OrderBy(static definition => definition.WorkspaceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static definition => definition.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _actions = _definitions
            .SelectMany(static definition => definition.Actions)
            .GroupBy(static action => action.ActionId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last())
            .OrderBy(static action => action.ActionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _actionsById = new Dictionary<string, WorkflowActionDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in _actions)
        {
            _actionsById[action.ActionId] = action;
            foreach (var alias in action.Aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    _actionsById[alias] = action;
                }
            }
        }

        _actionsByWorkItemKind = _actions
            .Where(static action => action.WorkItemKind.HasValue)
            .GroupBy(static action => action.WorkItemKind!.Value)
            .ToDictionary(static group => group.Key, static group => group.Last());
    }

    public static WorkflowRegistry CreateDefault()
        => new([new BuiltInWorkflowDefinitionProvider()]);

    public IReadOnlyList<WorkflowDefinitionDto> GetWorkflowDefinitions() => _definitions;

    public IReadOnlyList<WorkflowActionDto> GetActions() => _actions;

    public WorkflowActionDto? ResolveAction(string? actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return null;
        }

        return _actionsById.TryGetValue(actionId, out var action) ? action : null;
    }

    public WorkflowActionDto? ResolveOperatorWorkItem(OperatorWorkItemDto? workItem)
    {
        if (workItem is null)
        {
            return null;
        }

        var explicitTarget = ResolveRoute(workItem.TargetRoute);
        if (explicitTarget is not null)
        {
            return explicitTarget;
        }

        if (_actionsByWorkItemKind.TryGetValue(workItem.Kind, out var kindTarget))
        {
            return kindTarget;
        }

        return null;
    }

    public WorkflowActionDto? ResolveRoute(string? targetRoute)
    {
        if (string.IsNullOrWhiteSpace(targetRoute))
        {
            return null;
        }

        var normalizedRoute = NormalizeRoute(targetRoute);
        foreach (var action in _actions)
        {
            if (action.RoutePrefixes.Any(prefix => RouteEqualsOrStartsWith(normalizedRoute, prefix)) ||
                action.RouteContains.Any(fragment => normalizedRoute.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                return action;
            }
        }

        return null;
    }

    public string ResolveTargetPageTag(string? actionId, string fallbackPageTag)
    {
        var action = ResolveAction(actionId);
        return string.IsNullOrWhiteSpace(action?.TargetPageTag)
            ? fallbackPageTag
            : action.TargetPageTag;
    }

    private static string NormalizeRoute(string targetRoute)
        => targetRoute.Split('?', 2)[0].TrimEnd('/');

    private static bool RouteEqualsOrStartsWith(string route, string knownRoute)
    {
        if (string.IsNullOrWhiteSpace(knownRoute))
        {
            return false;
        }

        var normalizedKnownRoute = knownRoute.TrimEnd('/');
        return string.Equals(route, normalizedKnownRoute, StringComparison.OrdinalIgnoreCase) ||
               route.StartsWith($"{normalizedKnownRoute}/", StringComparison.OrdinalIgnoreCase);
    }
}
