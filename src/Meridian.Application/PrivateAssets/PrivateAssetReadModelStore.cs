using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.PrivateAssets;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.PrivateAssets;

public interface IPrivateAssetReadModelStore
{
    Task UpsertAsync(PrivateAssetDto asset, DateOnly asOfDate, CancellationToken ct = default);
    Task<PrivateAssetReadModelDto?> GetAsync(string privateAssetId, DateOnly asOfDate, CancellationToken ct = default);
    Task<IReadOnlyList<PrivateAssetReadModelDto>> ListAsync(string? owningEntityId = null, DateOnly? asOfDate = null, CancellationToken ct = default);
    Task<bool> DeleteAsync(string privateAssetId, CancellationToken ct = default);
}

public sealed class PrivateAssetReadModelValidator
{
    public const int DefaultStaleNavDays = 120;

    private readonly int _staleNavDays;

    public PrivateAssetReadModelValidator(int staleNavDays = DefaultStaleNavDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(staleNavDays);
        _staleNavDays = staleNavDays;
    }

    public IReadOnlyList<PrivateAssetValidationWarningDto> Validate(PrivateAssetDto asset, DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var warnings = new List<PrivateAssetValidationWarningDto>(capacity: 5);
        if (asset.ValuationDate is null)
        {
            warnings.Add(new PrivateAssetValidationWarningDto(
                PrivateAssetValidationWarningCodeDto.MissingValuationDate,
                "Private asset is missing a valuation date for the latest NAV.",
                "Warning",
                nameof(PrivateAssetEvidenceKindDto.ValuationMemo)));
        }
        else if (asset.ValuationDate.Value.AddDays(_staleNavDays) < asOfDate)
        {
            warnings.Add(new PrivateAssetValidationWarningDto(
                PrivateAssetValidationWarningCodeDto.StaleNav,
                $"Latest NAV is older than {_staleNavDays} days relative to {asOfDate:yyyy-MM-dd}.",
                "Warning",
                nameof(PrivateAssetEvidenceKindDto.ValuationMemo)));
        }

        if (string.IsNullOrWhiteSpace(asset.OwningEntityId))
        {
            warnings.Add(new PrivateAssetValidationWarningDto(
                PrivateAssetValidationWarningCodeDto.MissingOwnerEntity,
                "Private asset is missing an owning entity id.",
                "Warning"));
        }

        if (asset.CommitmentSchedule is not { Count: > 0 })
        {
            warnings.Add(new PrivateAssetValidationWarningDto(
                PrivateAssetValidationWarningCodeDto.MissingCommitmentSchedule,
                "Private asset is missing a commitment schedule.",
                "Warning",
                nameof(PrivateAssetEvidenceKindDto.CommitmentSchedule)));
        }

        if (asset.EvidenceLinks is not { Count: > 0 })
        {
            warnings.Add(new PrivateAssetValidationWarningDto(
                PrivateAssetValidationWarningCodeDto.MissingEvidence,
                "Private asset is missing retained evidence links for statements, notices, tax, subscription, side-letter, or valuation documents.",
                "Warning"));
        }

        return warnings;
    }

    public PrivateAssetReadModelDto Project(PrivateAssetDto asset, DateOnly asOfDate, DateTimeOffset refreshedAtUtc)
        => new(asset, Validate(asset, asOfDate), refreshedAtUtc);
}

public sealed class InMemoryPrivateAssetReadModelStore : IPrivateAssetReadModelStore
{
    private readonly Dictionary<string, PrivateAssetDto> _assets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private readonly PrivateAssetReadModelValidator _validator;
    private readonly TimeProvider _clock;

    public InMemoryPrivateAssetReadModelStore(PrivateAssetReadModelValidator? validator = null, TimeProvider? clock = null)
    {
        _validator = validator ?? new PrivateAssetReadModelValidator();
        _clock = clock ?? TimeProvider.System;
    }

    public Task UpsertAsync(PrivateAssetDto asset, DateOnly asOfDate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(asset.PrivateAssetId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _assets[asset.PrivateAssetId] = asset;
        }

        return Task.CompletedTask;
    }

    public Task<PrivateAssetReadModelDto?> GetAsync(string privateAssetId, DateOnly asOfDate, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateAssetId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            return Task.FromResult(_assets.TryGetValue(privateAssetId, out var asset)
                ? _validator.Project(asset, asOfDate, _clock.GetUtcNow())
                : null);
        }
    }

    public Task<IReadOnlyList<PrivateAssetReadModelDto>> ListAsync(string? owningEntityId = null, DateOnly? asOfDate = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var effectiveAsOfDate = asOfDate ?? DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);

        lock (_lock)
        {
            var rows = _assets.Values
                .Where(asset => string.IsNullOrWhiteSpace(owningEntityId) || string.Equals(asset.OwningEntityId, owningEntityId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static asset => asset.AssetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static asset => asset.PrivateAssetId, StringComparer.OrdinalIgnoreCase)
                .Select(asset => _validator.Project(asset, effectiveAsOfDate, _clock.GetUtcNow()))
                .ToArray();
            return Task.FromResult<IReadOnlyList<PrivateAssetReadModelDto>>(rows);
        }
    }

    public Task<bool> DeleteAsync(string privateAssetId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateAssetId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            return Task.FromResult(_assets.Remove(privateAssetId));
        }
    }
}

public sealed class FilePrivateAssetReadModelStore : IPrivateAssetReadModelStore
{
    private readonly string _directory;
    private readonly ILogger<FilePrivateAssetReadModelStore> _logger;
    private readonly PrivateAssetReadModelValidator _validator;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public FilePrivateAssetReadModelStore(
        string dataDirectory,
        ILogger<FilePrivateAssetReadModelStore> logger,
        PrivateAssetReadModelValidator? validator = null,
        TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _validator = validator ?? new PrivateAssetReadModelValidator();
        _clock = clock ?? TimeProvider.System;
        _directory = Path.Combine(dataDirectory, "private-assets", "read-models");
        Directory.CreateDirectory(_directory);
    }

    public async Task UpsertAsync(PrivateAssetDto asset, DateOnly asOfDate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(asset.PrivateAssetId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(asset, _jsonOptions);
            await AtomicFileWriter.WriteAsync(GetPath(asset.PrivateAssetId), json, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PrivateAssetReadModelDto?> GetAsync(string privateAssetId, DateOnly asOfDate, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateAssetId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var asset = await ReadAssetAsync(GetPath(privateAssetId), ct).ConfigureAwait(false);
            return asset is null ? null : _validator.Project(asset, asOfDate, _clock.GetUtcNow());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PrivateAssetReadModelDto>> ListAsync(string? owningEntityId = null, DateOnly? asOfDate = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var effectiveAsOfDate = asOfDate ?? DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
            var rows = new List<PrivateAssetReadModelDto>();
            foreach (var path in Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                var asset = await ReadAssetAsync(path, ct).ConfigureAwait(false);
                if (asset is null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(owningEntityId) && !string.Equals(asset.OwningEntityId, owningEntityId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                rows.Add(_validator.Project(asset, effectiveAsOfDate, _clock.GetUtcNow()));
            }

            return rows
                .OrderBy(static row => row.Asset.AssetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.Asset.PrivateAssetId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string privateAssetId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateAssetId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = GetPath(privateAssetId);
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PrivateAssetDto?> ReadAssetAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<PrivateAssetDto>(stream, _jsonOptions, ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Skipping corrupt private asset read model {Path}", path);
            return null;
        }
    }

    private string GetPath(string privateAssetId) => Path.Combine(_directory, $"{SanitizeFileName(privateAssetId)}.json");

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var buffer = value.Select(character => invalid.Contains(character) ? '_' : character).ToArray();
        return new string(buffer);
    }
}
