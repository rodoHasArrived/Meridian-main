using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Extensibility;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Extensibility;

public interface IExtensibilityConfigurationStore
{
    Task<IReadOnlyList<TenantTemplateConfigurationBundleDto>> ListTenantTemplatesAsync(CancellationToken ct = default);

    Task<TenantTemplateConfigurationBundleDto?> GetTenantTemplateAsync(string tenantTemplateId, CancellationToken ct = default);

    Task SaveTenantTemplateAsync(TenantTemplateConfigurationBundleDto tenantTemplate, CancellationToken ct = default);

    Task<IReadOnlyList<TenantTemplateActivationResultDto>> ListActivationHistoryAsync(string? tenantTemplateId = null, CancellationToken ct = default);

    Task RecordActivationResultAsync(TenantTemplateActivationResultDto result, CancellationToken ct = default);
}

public sealed class InMemoryExtensibilityConfigurationStore : IExtensibilityConfigurationStore
{
    private readonly Dictionary<string, TenantTemplateConfigurationBundleDto> _tenantTemplates = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TenantTemplateActivationResultDto> _activationHistory = [];

    public Task<IReadOnlyList<TenantTemplateConfigurationBundleDto>> ListTenantTemplatesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_tenantTemplates)
        {
            return Task.FromResult<IReadOnlyList<TenantTemplateConfigurationBundleDto>>(
                _tenantTemplates.Values
                    .OrderBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static item => item.TenantTemplateId, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }

    public Task<TenantTemplateConfigurationBundleDto?> GetTenantTemplateAsync(string tenantTemplateId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_tenantTemplates)
        {
            _tenantTemplates.TryGetValue(NormalizeTenantTemplateId(tenantTemplateId), out var tenantTemplate);
            return Task.FromResult(tenantTemplate);
        }
    }

    public Task SaveTenantTemplateAsync(TenantTemplateConfigurationBundleDto tenantTemplate, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(tenantTemplate);
        lock (_tenantTemplates)
        {
            _tenantTemplates[NormalizeTenantTemplateId(tenantTemplate.TenantTemplateId)] = tenantTemplate;
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<TenantTemplateActivationResultDto>> ListActivationHistoryAsync(string? tenantTemplateId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedId = NormalizeOptionalTenantTemplateId(tenantTemplateId);
        lock (_activationHistory)
        {
            return Task.FromResult<IReadOnlyList<TenantTemplateActivationResultDto>>(
                _activationHistory
                    .Where(item => normalizedId is null || string.Equals(item.TenantTemplateId, normalizedId, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(static item => item.EvaluatedAt)
                    .ThenBy(static item => item.TenantTemplateId, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }

    public Task RecordActivationResultAsync(TenantTemplateActivationResultDto result, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(result);
        lock (_activationHistory)
        {
            _activationHistory.Add(result);
            return Task.CompletedTask;
        }
    }

    private static string NormalizeTenantTemplateId(string value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Tenant template id is required.", nameof(value))
            : value.Trim();

    private static string? NormalizeOptionalTenantTemplateId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class FileExtensibilityConfigurationStore : IExtensibilityConfigurationStore
{
    private const int SnapshotVersion = 1;

    private readonly string _snapshotPath;
    private readonly ILogger<FileExtensibilityConfigurationStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileExtensibilityConfigurationStore(
        string workstationDataRoot,
        ILogger<FileExtensibilityConfigurationStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workstationDataRoot);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var extensibilityDirectory = Path.Combine(workstationDataRoot, "extensibility");
        Directory.CreateDirectory(extensibilityDirectory);
        _snapshotPath = Path.Combine(extensibilityDirectory, "configuration-bundles.json");
    }

    public async Task<IReadOnlyList<TenantTemplateConfigurationBundleDto>> ListTenantTemplatesAsync(CancellationToken ct = default)
    {
        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.TenantTemplates
            .OrderBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.TenantTemplateId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<TenantTemplateConfigurationBundleDto?> GetTenantTemplateAsync(string tenantTemplateId, CancellationToken ct = default)
    {
        var normalizedId = NormalizeTenantTemplateId(tenantTemplateId);
        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.TenantTemplates.FirstOrDefault(item =>
            string.Equals(item.TenantTemplateId, normalizedId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SaveTenantTemplateAsync(TenantTemplateConfigurationBundleDto tenantTemplate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tenantTemplate);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var normalizedId = NormalizeTenantTemplateId(tenantTemplate.TenantTemplateId);
            var snapshot = await ReadSnapshotCoreAsync(ct).ConfigureAwait(false);
            var tenantTemplates = snapshot.TenantTemplates
                .Where(item => !string.Equals(item.TenantTemplateId, normalizedId, StringComparison.OrdinalIgnoreCase))
                .Append(tenantTemplate with { TenantTemplateId = normalizedId })
                .OrderBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.TenantTemplateId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            await PersistAsync(snapshot with { TenantTemplates = tenantTemplates }, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<TenantTemplateActivationResultDto>> ListActivationHistoryAsync(string? tenantTemplateId = null, CancellationToken ct = default)
    {
        var normalizedId = NormalizeOptionalTenantTemplateId(tenantTemplateId);
        var snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.ActivationHistory
            .Where(item => normalizedId is null || string.Equals(item.TenantTemplateId, normalizedId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static item => item.EvaluatedAt)
            .ThenBy(static item => item.TenantTemplateId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task RecordActivationResultAsync(TenantTemplateActivationResultDto result, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await ReadSnapshotCoreAsync(ct).ConfigureAwait(false);
            var history = snapshot.ActivationHistory
                .Append(result)
                .OrderByDescending(static item => item.EvaluatedAt)
                .ThenBy(static item => item.TenantTemplateId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            await PersistAsync(snapshot with { ActivationHistory = history }, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ExtensibilityConfigurationSnapshot> ReadSnapshotAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadSnapshotCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ExtensibilityConfigurationSnapshot> ReadSnapshotCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(_snapshotPath))
        {
            return ExtensibilityConfigurationSnapshot.Empty;
        }

        try
        {
            await using var stream = File.OpenRead(_snapshotPath);
            var snapshot = await JsonSerializer.DeserializeAsync(
                    stream,
                    ExtensibilityConfigurationJsonContext.Default.ExtensibilityConfigurationSnapshot,
                    ct)
                .ConfigureAwait(false);
            if (snapshot is null)
            {
                return ExtensibilityConfigurationSnapshot.Empty;
            }

            if (snapshot.Version != SnapshotVersion)
            {
                throw new InvalidOperationException(
                    $"Extensibility configuration snapshot version {snapshot.Version} is not supported. Expected {SnapshotVersion}: {_snapshotPath}");
            }

            return snapshot;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Extensibility configuration snapshot is not valid JSON: {Path}", _snapshotPath);
            throw new InvalidOperationException($"Extensibility configuration snapshot is invalid: {_snapshotPath}", ex);
        }
    }

    private Task PersistAsync(ExtensibilityConfigurationSnapshot snapshot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(
            snapshot,
            ExtensibilityConfigurationJsonContext.Default.ExtensibilityConfigurationSnapshot);
        return AtomicFileWriter.WriteAsync(_snapshotPath, json, ct);
    }

    private static string NormalizeTenantTemplateId(string value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Tenant template id is required.", nameof(value))
            : value.Trim();

    private static string? NormalizeOptionalTenantTemplateId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

}

internal sealed record ExtensibilityConfigurationSnapshot(
    int Version,
    IReadOnlyList<TenantTemplateConfigurationBundleDto> TenantTemplates,
    IReadOnlyList<TenantTemplateActivationResultDto> ActivationHistory)
{
    public static ExtensibilityConfigurationSnapshot Empty { get; } = new(1, [], []);
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(ExtensibilityConfigurationSnapshot))]
internal sealed partial class ExtensibilityConfigurationJsonContext : JsonSerializerContext;

public sealed class ExtensibilityConfigurationService
{
    private const string DefaultActivationReason = "Tenant template activation requested.";

    private readonly IExtensibilityConfigurationStore _store;

    public ExtensibilityConfigurationService(IExtensibilityConfigurationStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<IReadOnlyList<TenantTemplateConfigurationBundleDto>> ListTenantTemplatesAsync(CancellationToken ct = default)
        => _store.ListTenantTemplatesAsync(ct);

    public Task<TenantTemplateConfigurationBundleDto?> GetTenantTemplateAsync(string tenantTemplateId, CancellationToken ct = default)
        => _store.GetTenantTemplateAsync(tenantTemplateId, ct);

    public async Task<TenantTemplateConfigurationBundleDto> UpsertTenantTemplateAsync(
        TenantTemplateConfigurationBundleDto tenantTemplate,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tenantTemplate);
        var normalized = NormalizeTenantTemplate(tenantTemplate);
        await _store.SaveTenantTemplateAsync(normalized, ct).ConfigureAwait(false);
        return normalized;
    }

    public async Task<ExtensibilityActivationReadinessDto> EvaluateTenantTemplateActivationAsync(
        string tenantTemplateId,
        CancellationToken ct = default)
    {
        var tenantTemplate = await _store.GetTenantTemplateAsync(tenantTemplateId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Tenant template '{tenantTemplateId}' was not found.");
        return EvaluateReadiness(tenantTemplate);
    }

    public async Task<TenantTemplateActivationResultDto> ActivateTenantTemplateAsync(
        string tenantTemplateId,
        string actor,
        TenantTemplateActivationRequestDto? request,
        DateTimeOffset evaluatedAt,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var tenantTemplate = await _store.GetTenantTemplateAsync(tenantTemplateId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Tenant template '{tenantTemplateId}' was not found.");

        var normalizedRequest = NormalizeActivationRequest(request);
        var readiness = EvaluateReadiness(tenantTemplate);
        if (!readiness.IsReady)
        {
            var blockedResult = new TenantTemplateActivationResultDto(
                tenantTemplate.TenantTemplateId,
                IsActivated: false,
                ResultingStatus: ExtensibilityConfigurationStatusDto.Reviewed,
                evaluatedAt,
                actor.Trim(),
                normalizedRequest.ChangeReason,
                normalizedRequest.LinkedAuditEventId,
                readiness,
                tenantTemplate);
            await _store.RecordActivationResultAsync(blockedResult, ct).ConfigureAwait(false);
            return blockedResult;
        }

        var activatedTemplate = tenantTemplate with
        {
            Configurations = tenantTemplate.Configurations
                .Select(configuration => configuration with
                {
                    Status = ExtensibilityConfigurationStatusDto.Active,
                    ApprovedBy = actor.Trim(),
                    ApprovedAt = evaluatedAt,
                    ChangeReason = normalizedRequest.ChangeReason,
                    LinkedAuditEventId = normalizedRequest.LinkedAuditEventId ?? configuration.LinkedAuditEventId
                })
                .ToArray()
        };

        await _store.SaveTenantTemplateAsync(activatedTemplate, ct).ConfigureAwait(false);

        var result = new TenantTemplateActivationResultDto(
            activatedTemplate.TenantTemplateId,
            IsActivated: true,
            ResultingStatus: ExtensibilityConfigurationStatusDto.Active,
            evaluatedAt,
            actor.Trim(),
            normalizedRequest.ChangeReason,
            normalizedRequest.LinkedAuditEventId,
            readiness,
            activatedTemplate);
        await _store.RecordActivationResultAsync(result, ct).ConfigureAwait(false);
        return result;
    }

    public Task<IReadOnlyList<TenantTemplateActivationResultDto>> ListActivationHistoryAsync(
        string? tenantTemplateId = null,
        CancellationToken ct = default)
        => _store.ListActivationHistoryAsync(tenantTemplateId, ct);

    private static TenantTemplateConfigurationBundleDto NormalizeTenantTemplate(TenantTemplateConfigurationBundleDto tenantTemplate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantTemplate.TenantTemplateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantTemplate.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantTemplate.Profile);

        return tenantTemplate with
        {
            TenantTemplateId = tenantTemplate.TenantTemplateId.Trim(),
            DisplayName = tenantTemplate.DisplayName.Trim(),
            Profile = tenantTemplate.Profile.Trim(),
            Configurations = tenantTemplate.Configurations
                .OrderBy(static item => item.Area)
                .ThenBy(static item => item.ConfigurationId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            DomainExtensions = tenantTemplate.DomainExtensions
                .OrderBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.ExtensionId, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static TenantTemplateActivationRequestDto NormalizeActivationRequest(TenantTemplateActivationRequestDto? request)
        => new(
            string.IsNullOrWhiteSpace(request?.ChangeReason) ? DefaultActivationReason : request.ChangeReason.Trim(),
            string.IsNullOrWhiteSpace(request?.LinkedAuditEventId) ? null : request.LinkedAuditEventId.Trim());

    private static ExtensibilityActivationReadinessDto EvaluateReadiness(TenantTemplateConfigurationBundleDto tenantTemplate)
    {
        var issues = new List<ExtensibilityValidationIssueDto>();

        if (tenantTemplate.Configurations.Count == 0 && tenantTemplate.DomainExtensions.Count == 0)
        {
            issues.Add(Critical(
                "tenant-template.empty",
                "Tenant template activation requires at least one configuration or domain extension.",
                GovernedFoundationKindDto.ApprovalEvidenceModel));
        }

        if (tenantTemplate.AllowsCoreObjectIdentityOverrides)
        {
            issues.Add(Critical(
                "tenant-template.core-object-identity-override",
                "Tenant templates cannot override stable core object identity.",
                GovernedFoundationKindDto.CoreObjectIdentity));
        }

        if (tenantTemplate.AllowsAuditTrailOverrides)
        {
            issues.Add(Critical(
                "tenant-template.audit-trail-override",
                "Tenant templates cannot weaken or bypass the audit trail.",
                GovernedFoundationKindDto.AuditTrail));
        }

        if (tenantTemplate.AllowsCalculationOverrides)
        {
            issues.Add(Critical(
                "tenant-template.calculation-override",
                "Tenant templates cannot override governed financial calculation integrity.",
                GovernedFoundationKindDto.FinancialCalculationIntegrity));
        }

        foreach (var extension in tenantTemplate.DomainExtensions)
        {
            if (extension.CanIntroduceCoreObjectIdentity)
            {
                issues.Add(Critical(
                    $"domain-extension.{extension.ExtensionId}.core-object-identity",
                    $"Domain extension '{extension.DisplayName}' cannot introduce core object identity.",
                    GovernedFoundationKindDto.CoreObjectIdentity));
            }

            if (extension.CanBypassAuditTrail)
            {
                issues.Add(Critical(
                    $"domain-extension.{extension.ExtensionId}.audit-trail",
                    $"Domain extension '{extension.DisplayName}' cannot bypass audit trail requirements.",
                    GovernedFoundationKindDto.AuditTrail));
            }

            if (extension.CanOverrideFinancialCalculations)
            {
                issues.Add(Critical(
                    $"domain-extension.{extension.ExtensionId}.calculation-integrity",
                    $"Domain extension '{extension.DisplayName}' cannot override governed financial calculations.",
                    GovernedFoundationKindDto.FinancialCalculationIntegrity));
            }
        }

        foreach (var configuration in tenantTemplate.Configurations)
        {
            var configurationId = string.IsNullOrWhiteSpace(configuration.ConfigurationId)
                ? "unknown"
                : configuration.ConfigurationId.Trim();

            if (configuration.Status is not ExtensibilityConfigurationStatusDto.Approved and not ExtensibilityConfigurationStatusDto.Active)
            {
                issues.Add(Critical(
                    $"configuration.{configurationId}.approval-state",
                    $"Configuration '{configurationId}' must be Approved before tenant-template activation.",
                    GovernedFoundationKindDto.ApprovalEvidenceModel));
            }

            if ((configuration.Status is ExtensibilityConfigurationStatusDto.Approved or ExtensibilityConfigurationStatusDto.Active)
                && (string.IsNullOrWhiteSpace(configuration.ApprovedBy) || configuration.ApprovedAt is null))
            {
                issues.Add(Critical(
                    $"configuration.{configurationId}.approval-evidence",
                    $"Configuration '{configurationId}' must retain approval actor and timestamp before tenant-template activation.",
                    GovernedFoundationKindDto.ApprovalEvidenceModel));
            }

            foreach (var issue in configuration.ValidationIssues.Where(static issue => issue.Severity == ExtensibilityValidationSeverityDto.Critical))
            {
                issues.Add(issue with
                {
                    Code = string.IsNullOrWhiteSpace(issue.Code)
                        ? $"configuration.{configurationId}.critical"
                        : issue.Code,
                    Message = string.IsNullOrWhiteSpace(issue.Message)
                        ? $"Configuration '{configurationId}' has a critical validation issue."
                        : issue.Message
                });
            }
        }

        return new ExtensibilityActivationReadinessDto(
            !issues.Any(static issue => issue.Severity == ExtensibilityValidationSeverityDto.Critical),
            issues
                .OrderBy(static issue => issue.BlockedFoundation)
                .ThenBy(static issue => issue.Code, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            CoreExtensibilityCatalog.GovernedFoundations
                .Select(static foundation => foundation.Kind)
                .ToArray());
    }

    private static ExtensibilityValidationIssueDto Critical(
        string code,
        string message,
        GovernedFoundationKindDto foundation)
        => new(code, ExtensibilityValidationSeverityDto.Critical, message, foundation);
}
