using Meridian.Contracts.Extensibility;
using Meridian.Identity.Auth;

namespace Meridian.Ui.Shared.Extensibility;

public sealed class PermissionExtensibilityCatalogProvider : IExtensibilityCatalogProvider
{
    private static readonly ExtensibilityScopeDto GlobalScope = new(
        ExtensibilityScopeKindDto.Global,
        ScopeId: null,
        DisplayName: "Global role and permission catalog");

    private static readonly IReadOnlyList<GovernedFoundationKindDto> PermissionFoundations =
    [
        GovernedFoundationKindDto.AuditTrail,
        GovernedFoundationKindDto.SecurityModelFoundation,
        GovernedFoundationKindDto.ApprovalEvidenceModel,
        GovernedFoundationKindDto.ImmutableRecordPreservation
    ];

    private static readonly IReadOnlyList<string> RoleGuardrails =
    [
        "Role profiles configure permission bundles only; effective authority is still resolved through session and scoped-access checks.",
        "Role and permission changes must preserve audit evidence and cannot disable the security model foundation."
    ];

    private static readonly IReadOnlyList<string> PermissionGuardrails =
    [
        "Permission flags define authority vocabulary only; tenant, entity, account, and report scopes are evaluated by scoped-access services.",
        "Permission configuration cannot bypass approval authority, segregation of duties, or audit-event retention."
    ];

    public IReadOnlyList<ExtensibilityRegistrationDto> GetRegistrations()
    {
        var catalog = RolePermissions.GetCatalog();
        var registrations = new List<ExtensibilityRegistrationDto>(catalog.Roles.Count + catalog.Permissions.Count);

        registrations.AddRange(catalog.Roles.Select(CreateRoleRegistration));
        registrations.AddRange(catalog.Permissions.Select(CreatePermissionRegistration));

        return registrations
            .OrderBy(static registration => registration.RegistrationId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ExtensibilityRegistrationDto CreateRoleRegistration(RolePermissionProfileDto role)
        => new(
            RegistrationId: $"role-profile:{role.Role}",
            Area: ExtensibilityConfigurationAreaDto.Permission,
            Status: ExtensibilityConfigurationStatusDto.Active,
            DisplayName: role.DisplayName,
            Summary: role.Description,
            OwningContext: "Identity",
            Scope: GlobalScope,
            TargetCoreObjects:
            [
                CoreFinancialObjectKindDto.Tenant,
                CoreFinancialObjectKindDto.Entity,
                CoreFinancialObjectKindDto.Account,
                CoreFinancialObjectKindDto.Task,
                CoreFinancialObjectKindDto.ReportPackage,
                CoreFinancialObjectKindDto.AuditEvent
            ],
            GovernedFoundations: PermissionFoundations,
            TemplateIds: [role.Role],
            EvidenceTags: role.Permissions.Select(static permission => $"permission:{permission}").ToArray(),
            Guardrails: RoleGuardrails);

    private static ExtensibilityRegistrationDto CreatePermissionRegistration(PermissionCatalogItemDto permission)
        => new(
            RegistrationId: $"permission:{permission.Name}",
            Area: ExtensibilityConfigurationAreaDto.Permission,
            Status: ExtensibilityConfigurationStatusDto.Active,
            DisplayName: permission.Name,
            Summary: permission.Description,
            OwningContext: "Identity",
            Scope: GlobalScope,
            TargetCoreObjects: ResolveTargets(permission.Group),
            GovernedFoundations: PermissionFoundations,
            TemplateIds: [permission.Name, permission.Value.ToString(), permission.Group],
            EvidenceTags: [$"permission-group:{permission.Group}"],
            Guardrails: PermissionGuardrails);

    private static IReadOnlyList<CoreFinancialObjectKindDto> ResolveTargets(string group)
    {
        if (group.Contains("Provider", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("ingestion", StringComparison.OrdinalIgnoreCase))
        {
            return [CoreFinancialObjectKindDto.Account, CoreFinancialObjectKindDto.Document, CoreFinancialObjectKindDto.Transaction, CoreFinancialObjectKindDto.AuditEvent];
        }

        if (group.Contains("Trading", StringComparison.OrdinalIgnoreCase))
        {
            return [CoreFinancialObjectKindDto.Transaction, CoreFinancialObjectKindDto.Position, CoreFinancialObjectKindDto.Instrument, CoreFinancialObjectKindDto.AuditEvent];
        }

        if (group.Contains("Security Master", StringComparison.OrdinalIgnoreCase))
        {
            return [CoreFinancialObjectKindDto.Instrument, CoreFinancialObjectKindDto.Valuation, CoreFinancialObjectKindDto.AuditEvent];
        }

        if (group.Contains("Fund structure", StringComparison.OrdinalIgnoreCase))
        {
            return [CoreFinancialObjectKindDto.Entity, CoreFinancialObjectKindDto.Relationship, CoreFinancialObjectKindDto.Account, CoreFinancialObjectKindDto.AuditEvent];
        }

        if (group.Contains("Administration", StringComparison.OrdinalIgnoreCase) ||
            group.Contains("Configuration", StringComparison.OrdinalIgnoreCase))
        {
            return [CoreFinancialObjectKindDto.Tenant, CoreFinancialObjectKindDto.Task, CoreFinancialObjectKindDto.Document, CoreFinancialObjectKindDto.AuditEvent];
        }

        return [CoreFinancialObjectKindDto.Task, CoreFinancialObjectKindDto.ReportPackage, CoreFinancialObjectKindDto.Document, CoreFinancialObjectKindDto.AuditEvent];
    }
}
