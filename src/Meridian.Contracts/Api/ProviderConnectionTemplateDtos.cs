using System.Text.Json.Serialization;
using Meridian.Contracts.Configuration;

namespace Meridian.Contracts.Api;

/// <summary>The portable allowlist. Semantics and checklist entries are catalog codes.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProviderConnectionTemplateDto(
    string Name,
    string ProviderKind,
    int ProviderVersion,
    string Environment,
    string ConnectionType,
    string ConnectionMode,
    string AllowedScope,
    ProviderTemplateCapabilityDto[] RequestedCapabilities,
    ProviderTemplateBindingDto SuggestedBinding,
    string ExpectedDataSemantics,
    string[] VerificationChecklist);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProviderTemplateCapabilityDto(string Capability, int Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProviderTemplateBindingDto(string Capability, int Priority);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProviderConnectionTemplateFile(int SchemaVersion, ProviderConnectionTemplateDto[] Templates);

public sealed record ProviderTemplateReviewRow(string Field, string Value);

public sealed record ProviderConnectionTemplateOption(
    ProviderConnectionTemplateDto Template,
    string Revision,
    bool IsBuiltIn,
    bool RequiresExternalAccount,
    ProviderTemplateReviewRow[] ReviewRows,
    string[] VerificationChecks);

public sealed record SaveProviderConnectionTemplateRequest(string ConnectionId, string Name);

/// <summary>The account identity, when required, belongs to this instance alone.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BeginProviderTemplateSetupRequest(Guid SetupId, string TemplateName, string Revision, string? ExternalAccountId = null);

public sealed record ProviderTemplateSetupDraft(Guid SetupId, string ProviderKind, string Environment,
    ProviderCredentialFieldMetadataDto[] CredentialFields);

public sealed record ProviderTemplateSetupTestResult(bool CanSave, string Detail);

public sealed record SavedProviderTemplateConnection(string ConnectionId, string DisplayName, string ProviderKind);

public sealed record ProviderTemplateImportIssue(string Field, string Code, string Message);
