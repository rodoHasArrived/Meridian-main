using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.AccountingSystem;

public sealed partial class AccountingSystemIntegrationService
{
    private readonly ConcurrentDictionary<string, AccountingSystemImportDetailDto> _latestImports = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ScopedExternalGlMappingProfile> _mappingProfiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ExternalGlExportPackageDto> _exportPackages = new(StringComparer.OrdinalIgnoreCase);
    private readonly AccountingSystemIntegrationStateStore? _stateStore;
    private readonly object _memoryStateGate = new();
    private const string ImportsCollection = "latest-imports";
    private const string MappingProfilesCollection = "mapping-profiles";
    private const string ExportPackagesCollection = "export-packages";

    /// <summary>
    /// Retains a record durably before publishing it when a state store is configured; without one
    /// the record lives for the process lifetime only.
    /// </summary>
    private Task RetainAsync<T>(
        string collection, ConcurrentDictionary<string, T> rows, string key, T value, CancellationToken ct,
        AccountingSystemStateExpectation? expected = null,
        IReadOnlyList<AccountingSystemStateDependency>? dependencies = null)
    {
        if (_stateStore is not null)
            return _stateStore.RetainAsync(collection, rows, key, value, JsonOptions, ct, expected, dependencies);
        lock (_memoryStateGate)
        {
            if (expected is not null && !AccountingSystemIntegrationStateStore.Same(expected,
                    AccountingSystemIntegrationStateStore.Capture(rows, key, JsonOptions)))
                throw new InvalidOperationException("External GL review state changed; reload before retrying this update.");
            foreach (var dependency in dependencies ?? [])
                if (!AccountingSystemIntegrationStateStore.Same(dependency.Expected, CaptureLocalDependency(dependency.Collection, dependency.Key)))
                    throw new InvalidOperationException("External GL review dependency changed; prepare a new review package.");
            rows[key] = value;
        }
        return Task.CompletedTask;
    }

    private void RefreshRetainedState()
    {
        _stateStore?.Refresh(ImportsCollection, _latestImports, JsonOptions);
        _stateStore?.Refresh(MappingProfilesCollection, _mappingProfiles, JsonOptions);
        _stateStore?.Refresh(ExportPackagesCollection, _exportPackages, JsonOptions);
    }

    private AccountingSystemStateExpectation CaptureLocalDependency(string collection, string key) => collection switch
    {
        ImportsCollection => AccountingSystemIntegrationStateStore.Capture(_latestImports, key, JsonOptions),
        MappingProfilesCollection => AccountingSystemIntegrationStateStore.Capture(_mappingProfiles, key, JsonOptions),
        ExportPackagesCollection => AccountingSystemIntegrationStateStore.Capture(_exportPackages, key, JsonOptions),
        _ => throw new ArgumentException("Unknown external GL review dependency.", nameof(collection))
    };

    private AccountingSystemStateDependency[] CaptureReviewDependencies(ExternalGlExportPackageDto package,
        ScopedExternalGlMappingProfile? mapping, AccountingSystemReconciliationSummaryDto? usedReconciliation = null)
    {
        var importKey = ImportKey(package.ProviderId, package.FundProfileId, package.LedgerBookId, package.TenantId, package.CompanyId);
        if (usedReconciliation is not null && _latestImports.TryGetValue(importKey, out var retainedImport)
            && (retainedImport.Summary.ImportId != usedReconciliation.ImportId
                || retainedImport.Summary.ContentHash != usedReconciliation.ImportContentHash))
            throw new InvalidOperationException("External GL review import changed during preparation; retry using the current retained source.");
        var dependencies = new List<AccountingSystemStateDependency>
        {
            new(ImportsCollection, importKey, CaptureLocalDependency(ImportsCollection, importKey))
        };
        if (mapping is not null)
            dependencies.Add(new(MappingProfilesCollection,
                MappingProfileKey(mapping.ProviderId, mapping.FundProfileId, mapping.LedgerBookId,
                    mapping.Profile.ProfileId, mapping.TenantId, mapping.CompanyId),
                new(JsonSerializer.Serialize(mapping, JsonOptions))));
        return dependencies.ToArray();
    }

    private sealed record ScopedExternalGlMappingProfile(
        string ProviderId,
        string FundProfileId,
        Guid? LedgerBookId,
        ExternalGlMappingProfileDto Profile,
        string Actor,
        IReadOnlyList<string> EvidenceLinks,
        string? TenantId = null,
        string? CompanyId = null);
}
