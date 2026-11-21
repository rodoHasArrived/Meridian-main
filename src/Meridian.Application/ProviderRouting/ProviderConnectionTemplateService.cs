using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.ProviderSdk;
using Catalog = Meridian.Application.ProviderRouting.ProviderConnectionTemplateCatalog;

namespace Meridian.Application.ProviderRouting;

public sealed class ProviderTemplateCompatibilityException(IReadOnlyList<ProviderTemplateImportIssue> issues)
    : InvalidOperationException("The template file is incompatible with this host.")
{
    public IReadOnlyList<ProviderTemplateImportIssue> Issues { get; } = issues;
}

/// <summary>Owns the portable template library and the draft-to-connection transaction.</summary>
public sealed class ProviderConnectionTemplateService(UI.ConfigStore store, IProviderCredentialStore credentialStore)
{
    private IScopedProviderCredentialStore Vault => credentialStore as IScopedProviderCredentialStore
        ?? throw new InvalidOperationException("Template setup requires the scoped credential vault.");

    public ProviderConnectionTemplateOption[] List(string tenantId)
    {
        var section = ProviderRoutingConfigExtensions.GetSection(store.LoadRequired());
        return Catalog.BuiltIns.Select(template => Catalog.Describe(template, true))
            .Concat(OwnedTemplates(section, tenantId)
                .Where(template => Catalog.Validate(template, "template").Count == 0)
                .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
                .Select(template => Catalog.Describe(template, false))).ToArray();
    }

    public ProviderConnectionTemplateFile Export(string tenantId, string name)
    {
        var section = ProviderRoutingConfigExtensions.GetSection(store.LoadRequired());
        var template = FindTemplate(section, tenantId, name);
        RequireCompatible(template);
        return new(Catalog.SchemaVersion, [template]);
    }

    public Task<ProviderConnectionTemplateOption> SaveFromConnectionAsync(string tenantId, SaveProviderConnectionTemplateRequest request,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConnectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        return store.UpdateRequiredAsync(cfg =>
        {
            var section = ProviderRoutingConfigExtensions.GetSection(cfg);
            var connection = FindConnection(section, request.ConnectionId);
            if (connection is null || connection.TenantId != tenantId)
                throw new UnauthorizedAccessException("Select a connection owned by this tenant.");
            var template = Catalog.FromConnection(connection, request.Name, section.Bindings ?? []);
            RequireCompatible(template);
            var next = AddTemplates(section, tenantId, [template], out _);
            return (ReferenceEquals(section, next) ? cfg : cfg with { ProviderConnections = next }, Catalog.Describe(template, false));
        }, ct);
    }

    public Task<int> ImportAsync(string tenantId, JsonElement payload, CancellationToken ct)
    {
        var file = ReadFile(payload);
        var issues = new List<ProviderTemplateImportIssue>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < file.Templates.Length; i++)
        {
            var template = file.Templates[i];
            var path = $"templates[{i}]";
            issues.AddRange(Catalog.Validate(template, path));
            if (template?.Name is { } name && !names.Add(name))
                issues.Add(new(path + ".name", "DuplicateTemplateName", "Use a different name for each template in the file."));
        }

        if (issues.Count > 0)
            throw new ProviderTemplateCompatibilityException(issues);
        return store.UpdateRequiredAsync(cfg =>
        {
            var section = ProviderRoutingConfigExtensions.GetSection(cfg);
            var next = AddTemplates(section, tenantId, file.Templates, out var count);
            return (ReferenceEquals(section, next) ? cfg : cfg with { ProviderConnections = next }, count);
        }, ct);
    }

    public Task<ProviderTemplateSetupDraft> BeginAsync(string tenantId, string actor, BeginProviderTemplateSetupRequest request, CancellationToken ct)
    {
        if (request.SetupId == Guid.Empty || string.IsNullOrWhiteSpace(request.TemplateName) || string.IsNullOrWhiteSpace(request.Revision))
            throw new ArgumentException("Select and review a template before opening the credential vault.");

        return store.UpdateRequiredWithLeaseAsync(async (cfg, token) =>
        {
            var section = ProviderRoutingConfigExtensions.GetSection(cfg);
            var setup = (section.TemplateSetups ?? []).SingleOrDefault(candidate => candidate.SetupId == request.SetupId);
            if (setup is not null && (setup.TenantId != tenantId || setup.Actor != actor))
                throw new UnauthorizedAccessException("The setup draft is unavailable.");
            if ((section.Connections ?? []).Any(connection => connection.TemplateSetupId == request.SetupId))
                throw new InvalidOperationException("This draft identity is already saved. Start a new setup to create another connection.");
            var now = DateTimeOffset.UtcNow;
            if (setup is not null && setup.ExpiresAt <= now)
                throw new InvalidOperationException("This draft expired. Discard it and start a new setup.");

            var template = setup?.Template ?? FindTemplate(section, tenantId, request.TemplateName);
            RequireCompatible(template);
            var revision = Catalog.Revision(template);
            if (request.Revision != revision || !string.Equals(request.TemplateName, template.Name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The template changed after review. Reload the template and review its values again.");

            var account = Catalog.RequiresAccount(template)
                ? request.ExternalAccountId?.Trim() : "provider-data";
            if (string.IsNullOrWhiteSpace(account))
                throw new ArgumentException("Enter the provider account identity for this connection.");
            var scope = new ProviderCredentialScope(tenantId, setup?.ConnectionId ?? Guid.NewGuid().ToString("N"), account, template.Environment);
            if (setup is not null && setup.ExternalAccountId != scope.ExternalAccountId)
                throw new InvalidOperationException("The account identity differs from the reviewed draft. Start a new setup to change it.");
            var schema = ProviderCredentialCatalog.BuildCredentialFields(ProviderCredentialCatalog.Find(template.ProviderKind)!);
            var result = new ProviderTemplateSetupDraft(request.SetupId, template.ProviderKind, template.Environment, schema.ToArray());
            if (setup is not null)
                return (cfg, result);

            var expired = (section.TemplateSetups ?? []).Where(item => item.TenantId == tenantId && item.Actor == actor && item.ExpiresAt <= now).ToArray();
            foreach (var item in expired)
                await DeleteDraftCredentialsAsync(item, token).ConfigureAwait(false);
            var next = new ProviderTemplateSetupConfig(request.SetupId, scope.ConnectionId, tenantId, actor,
                scope.ExternalAccountId, revision, now.AddDays(1), template);
            var drafts = (section.TemplateSetups ?? []).Except(expired).Append(next).ToArray();
            return (cfg with { ProviderConnections = section with { TemplateSetups = drafts } }, result);
        }, ct);
    }

    /// <summary>Called under the workstation configuration lease on every scoped vault operation.</summary>
    public static ProviderCredentialScope RequireDraftScope(AppConfig config, Guid setupId, string tenantId, string actor, string providerId)
    {
        var draft = RequireDraft(ProviderRoutingConfigExtensions.GetSection(config), setupId, tenantId, actor);
        if (draft.Template.ProviderKind != ProviderCredentialCatalog.NormalizeProviderId(providerId))
            throw new UnauthorizedAccessException("The provider setup draft is unavailable.");
        return CredentialScope(draft);
    }

    public async Task<ProviderTemplateSetupTestResult> TestAsync(string tenantId, string actor, Guid setupId,
        Func<string, CancellationToken, Task<ProviderTemplateSetupTestResult>> verify, CancellationToken ct)
    {
        var (draft, generation) = await store.UpdateRequiredWithLeaseAsync(async (cfg, token) =>
        {
            var section = ProviderRoutingConfigExtensions.GetSection(cfg);
            var setup = RequireDraft(section, setupId, tenantId, actor);
            RequireCompatible(setup.Template);
            var generation = await ReadyCredentialGenerationAsync(setup, token).ConfigureAwait(false);
            return (setup.TestedCredentialGeneration is null ? cfg : ReplaceDraft(cfg, section, setup with { TestedCredentialGeneration = null }),
                (setup, generation));
        }, ct).ConfigureAwait(false);

        if (generation is null)
            return new(false, "Enter all required fields in the credential vault before testing the connection.");

        var result = await verify(draft.Template.ProviderKind, ct).ConfigureAwait(false);
        if (!result.CanSave)
            return result;

        return await store.UpdateRequiredWithLeaseAsync(async (cfg, token) =>
        {
            var section = ProviderRoutingConfigExtensions.GetSection(cfg);
            var setup = RequireDraft(section, setupId, tenantId, actor);
            var currentGeneration = await ReadyCredentialGenerationAsync(setup, token).ConfigureAwait(false);
            if (setup.Revision != draft.Revision || setup.ExternalAccountId != draft.ExternalAccountId || currentGeneration != generation)
                return (cfg, new ProviderTemplateSetupTestResult(false, "The credentials changed during testing. Test the current draft again."));
            return (ReplaceDraft(cfg, section, setup with { TestedCredentialGeneration = generation }), result);
        }, ct).ConfigureAwait(false);
    }

    public Task<SavedProviderTemplateConnection> SaveAsync(string tenantId, string actor, Guid setupId, CancellationToken ct)
        => store.UpdateRequiredWithLeaseAsync(async (cfg, token) =>
        {
            var section = ProviderRoutingConfigExtensions.GetSection(cfg);
            var existing = (section.Connections ?? []).SingleOrDefault(connection => connection.TemplateSetupId == setupId);
            if (existing is not null)
            {
                if (existing.TenantId != tenantId || existing.SetupTemplate is null)
                    throw new UnauthorizedAccessException("The provider setup draft is unavailable.");
                return (cfg, ToSaved(existing));
            }

            var draft = RequireDraft(section, setupId, tenantId, actor);
            if ((section.Connections ?? []).Any(connection => string.Equals(connection.ConnectionId, draft.ConnectionId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The reserved connection identity is already in use.");
            RequireCompatible(draft.Template);
            var generation = await ReadyCredentialGenerationAsync(draft, token).ConfigureAwait(false);
            if (generation is null || generation != draft.TestedCredentialGeneration)
                throw new InvalidOperationException("Test the current credentials before saving the connection.");

            var template = draft.Template;
            var next = new ProviderConnectionConfig(draft.ConnectionId, template.ProviderKind, template.Name,
                Enum.Parse<ProviderConnectionType>(template.ConnectionType), Enum.Parse<ProviderConnectionMode>(template.ConnectionMode),
                ExternalAccountId: draft.ExternalAccountId,
                Scope: Catalog.IsWorkspaceScope(template.AllowedScope) ? new(Workspace: template.AllowedScope.ToLowerInvariant()) : null,
                TenantId: tenantId, CredentialEnvironment: template.Environment)
            {
                SetupTemplate = template,
                TemplateSetupId = setupId
            };
            return (cfg with
            {
                ProviderConnections = section with
                {
                    TemplateSetups = (section.TemplateSetups ?? []).Where(item => item.SetupId != setupId).ToArray(),
                    Connections = [.. section.Connections ?? [], next],
                    OwnershipRequiredProviders = (section.OwnershipRequiredProviders ?? []).Append(template.ProviderKind)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                }
            }, ToSaved(next));
        }, ct);

    public Task<bool> DiscardAsync(string tenantId, string actor, Guid setupId, CancellationToken ct)
        => store.UpdateRequiredWithLeaseAsync(async (cfg, token) =>
        {
            var section = ProviderRoutingConfigExtensions.GetSection(cfg);
            var setup = (section.TemplateSetups ?? []).SingleOrDefault(candidate => candidate.SetupId == setupId);
            if (setup is null)
                return (cfg, false);
            if (setup.TenantId != tenantId || setup.Actor != actor)
                throw new UnauthorizedAccessException("The provider setup draft is unavailable.");

            await DeleteDraftCredentialsAsync(setup, token).ConfigureAwait(false);
            return (cfg with
            {
                ProviderConnections = section with
                {
                    TemplateSetups = (section.TemplateSetups ?? []).Where(item => item.SetupId != setupId).ToArray()
                }
            }, true);
        }, ct);

    private async Task<string?> ReadyCredentialGenerationAsync(ProviderTemplateSetupConfig draft, CancellationToken ct)
    {
        var provider = ProviderCredentialCatalog.Find(draft.Template.ProviderKind)!;
        if (!provider.RequiresCredentials)
            return string.Empty;
        var status = await Vault.GetScopedStatusAsync(provider.ProviderId, CredentialScope(draft), ct).ConfigureAwait(false);
        var generation = status.AuditMetadata.GetValueOrDefault("credentialGeneration");
        return status.CredentialState is ProviderCredentialStateDto.Configured or ProviderCredentialStateDto.Verified
            && !string.IsNullOrWhiteSpace(generation) ? generation : null;
    }

    private static ProviderCredentialScope CredentialScope(ProviderTemplateSetupConfig draft)
        => new(draft.TenantId, draft.ConnectionId, draft.ExternalAccountId, draft.Template.Environment);

    private Task DeleteDraftCredentialsAsync(ProviderTemplateSetupConfig draft, CancellationToken ct)
        => ProviderCredentialCatalog.Find(draft.Template.ProviderKind)?.RequiresCredentials == false
            ? Task.CompletedTask : Vault.DeleteScopedAsync(draft.Template.ProviderKind, CredentialScope(draft), draft.Actor, ct);

    private static ProviderTemplateSetupConfig RequireDraft(ProviderConnectionsConfig section, Guid id, string tenant, string actor)
        => (section.TemplateSetups ?? []).SingleOrDefault(draft => draft.SetupId == id && draft.TenantId == tenant && draft.Actor == actor &&
                draft.ExpiresAt > DateTimeOffset.UtcNow)
            ?? throw new UnauthorizedAccessException("The provider setup draft is unavailable.");

    private static AppConfig ReplaceDraft(AppConfig config, ProviderConnectionsConfig section, ProviderTemplateSetupConfig replacement)
        => config with
        {
            ProviderConnections = section with
            {
                TemplateSetups = (section.TemplateSetups ?? []).Select(draft => draft.SetupId == replacement.SetupId ? replacement : draft).ToArray()
            }
        };

    private static ProviderConnectionConfig? FindConnection(ProviderConnectionsConfig section, string connectionId)
    {
        var matches = (section.Connections ?? []).Where(item => string.Equals(item.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static SavedProviderTemplateConnection ToSaved(ProviderConnectionConfig connection)
        => new(connection.ConnectionId, connection.DisplayName, connection.ProviderFamilyId);

    private static IEnumerable<ProviderConnectionTemplateDto> OwnedTemplates(ProviderConnectionsConfig section, string tenant)
        => (section.Templates ?? []).Where(entry => entry.TenantId == tenant).Select(entry => entry.Template);

    private static ProviderConnectionTemplateDto FindTemplate(ProviderConnectionsConfig section, string tenant, string name)
        => Catalog.BuiltIns.Concat(OwnedTemplates(section, tenant))
            .FirstOrDefault(template => string.Equals(template.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The selected connection template is unavailable.");

    private static ProviderConnectionsConfig AddTemplates(ProviderConnectionsConfig section, string tenant,
        ProviderConnectionTemplateDto[] templates, out int count)
    {
        var existing = Catalog.BuiltIns.Concat(OwnedTemplates(section, tenant))
            .ToDictionary(template => template.Name, StringComparer.OrdinalIgnoreCase);
        var additions = new List<OwnedProviderTemplateConfig>();
        var issues = new List<ProviderTemplateImportIssue>();
        for (var i = 0; i < templates.Length; i++)
        {
            var template = templates[i];
            if (!existing.TryGetValue(template.Name, out var prior))
                additions.Add(new(tenant, template));
            else if (Catalog.Revision(prior) != Catalog.Revision(template))
                issues.Add(new($"templates[{i}].name", "TemplateNameConflict", "A different template already uses this name."));
        }
        if (issues.Count > 0)
            throw new ProviderTemplateCompatibilityException(issues);
        count = additions.Count;
        return count == 0 ? section : section with { Templates = [.. section.Templates ?? [], .. additions] };
    }

    private static void RequireCompatible(ProviderConnectionTemplateDto template)
    {
        var issues = Catalog.Validate(template, "template");
        if (issues.Count > 0)
            throw new ProviderTemplateCompatibilityException(issues);
    }

    private static ProviderConnectionTemplateFile ReadFile(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || payload.GetRawText().Length > Catalog.MaxFileLength)
            throw new ArgumentException("Choose a JSON template file no larger than 64 KB.");
        if (!payload.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var schemaVersion) || schemaVersion != Catalog.SchemaVersion)
            throw new ProviderTemplateCompatibilityException([new("schemaVersion", "UnsupportedSchemaVersion", "This host supports template file schema version 1.")]);
        try
        {
            var file = payload.Deserialize<ProviderConnectionTemplateFile>(Catalog.FileJson);
            if (file?.Templates is not { Length: > 0 and <= Catalog.MaxTemplates })
                throw new ArgumentException("A template file must include 1 to 100 templates.");
            return file;
        }
        catch (JsonException)
        {
            // Never reflect arbitrary property names or values from an imported document.
            throw new ArgumentException("The JSON contains an unknown, duplicated, or malformed template field.");
        }
    }
}
