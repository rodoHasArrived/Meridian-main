using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

/// <summary>
/// Owns the desktop-local v1 fund profile catalog and the currently selected fund context.
/// </summary>
public sealed partial class FundContextService : IFundProfileCatalog
{
    private static readonly Lazy<FundContextService> _instance = new(() => new FundContextService());

    private readonly List<FundProfileDetail> _profiles = new();
    private readonly string _storagePath;
    private bool _loaded;

    public static FundContextService Instance => _instance.Value;

    public FundContextService(string? storagePath = null)
    {
        _storagePath = storagePath ?? GetDefaultStoragePath();
    }

    public IReadOnlyList<FundProfileDetail> Profiles => _profiles.AsReadOnly();

    public FundProfileDetail? CurrentFundProfile { get; private set; }

    public string? LastSelectedFundProfileId { get; private set; }

    public event EventHandler<FundProfileChangedEventArgs>? ActiveFundProfileChanged;

    public event EventHandler? FundSwitchRequested;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!File.Exists(_storagePath))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_storagePath, ct).ConfigureAwait(false);
            var data = JsonSerializer.Deserialize(json, FundProfileStorageJsonContext.Default.FundProfileStorageModel);
            if (data is null)
            {
                return;
            }

            _profiles.Clear();
            if (data.Profiles is { Count: > 0 })
            {
                _profiles.AddRange(data.Profiles);
            }

            LastSelectedFundProfileId = data.LastSelectedFundProfileId;
        }
        catch
        {
            _profiles.Clear();
            LastSelectedFundProfileId = null;
        }
    }

    public async Task<FundProfileDetail> UpsertProfileAsync(FundProfileDetail profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await LoadAsync(ct).ConfigureAwait(false);

        var normalized = Normalize(profile);
        var existingIndex = _profiles.FindIndex(item =>
            string.Equals(item.FundProfileId, normalized.FundProfileId, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            _profiles[existingIndex] = normalized;
        }
        else
        {
            _profiles.Add(normalized);
        }

        await SaveAsync(ct).ConfigureAwait(false);
        return normalized;
    }

    public async Task DeleteProfileAsync(string fundProfileId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        await LoadAsync(ct).ConfigureAwait(false);

        _profiles.RemoveAll(profile =>
            string.Equals(profile.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase));

        if (string.Equals(CurrentFundProfile?.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
        {
            CurrentFundProfile = null;
        }

        if (string.Equals(LastSelectedFundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
        {
            LastSelectedFundProfileId = null;
        }

        await SaveAsync(ct).ConfigureAwait(false);
    }

    public async Task<FundProfileDetail?> SelectFundProfileAsync(string fundProfileId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        await LoadAsync(ct).ConfigureAwait(false);

        var profile = _profiles.FirstOrDefault(item =>
            string.Equals(item.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return null;
        }

        var updated = profile with { LastOpenedAt = DateTimeOffset.UtcNow };
        await UpsertProfileAsync(updated, ct).ConfigureAwait(false);

        CurrentFundProfile = updated;
        LastSelectedFundProfileId = updated.FundProfileId;
        await SaveAsync(ct).ConfigureAwait(false);

        ActiveFundProfileChanged?.Invoke(this, new FundProfileChangedEventArgs(updated));
        return updated;
    }

    public async Task SetLastSelectedFundProfileIdAsync(string? fundProfileId, CancellationToken ct = default)
    {
        await LoadAsync(ct).ConfigureAwait(false);
        LastSelectedFundProfileId = string.IsNullOrWhiteSpace(fundProfileId) ? null : fundProfileId.Trim();
        await SaveAsync(ct).ConfigureAwait(false);
    }

    public void RequestSwitchFund()
    {
        FundSwitchRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ClearCurrentFund()
    {
        CurrentFundProfile = null;
    }

    private async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var model = new FundProfileStorageModel
            {
                LastSelectedFundProfileId = LastSelectedFundProfileId,
                Profiles = _profiles.OrderByDescending(profile => profile.IsDefault)
                    .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            var json = JsonSerializer.Serialize(model, FundProfileStorageJsonContext.Default.FundProfileStorageModel);
            await File.WriteAllTextAsync(_storagePath, json, ct).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static string GetDefaultStoragePath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian");
        return Path.Combine(directory, "fund-profiles.json");
    }

    private static FundProfileDetail Normalize(FundProfileDetail profile)
    {
        var fundProfileId = string.IsNullOrWhiteSpace(profile.FundProfileId)
            ? Guid.NewGuid().ToString("N")
            : profile.FundProfileId.Trim();

        var displayName = string.IsNullOrWhiteSpace(profile.DisplayName)
            ? "Unnamed Fund"
            : profile.DisplayName.Trim();

        var legalEntityName = string.IsNullOrWhiteSpace(profile.LegalEntityName)
            ? displayName
            : profile.LegalEntityName.Trim();

        var baseCurrency = string.IsNullOrWhiteSpace(profile.BaseCurrency)
            ? "USD"
            : profile.BaseCurrency.Trim().ToUpperInvariant();

        var defaultWorkspaceId = string.IsNullOrWhiteSpace(profile.DefaultWorkspaceId)
            ? "governance"
            : profile.DefaultWorkspaceId.Trim();

        var defaultLandingPageTag = string.IsNullOrWhiteSpace(profile.DefaultLandingPageTag)
            ? "GovernanceShell"
            : profile.DefaultLandingPageTag.Trim();

        return profile with
        {
            FundProfileId = fundProfileId,
            DisplayName = displayName,
            LegalEntityName = legalEntityName,
            BaseCurrency = baseCurrency,
            DefaultWorkspaceId = defaultWorkspaceId,
            DefaultLandingPageTag = defaultLandingPageTag,
            EntityIds = NormalizeIds(profile.EntityIds),
            SleeveIds = NormalizeIds(profile.SleeveIds),
            VehicleIds = NormalizeIds(profile.VehicleIds)
        };
    }

    private static IReadOnlyList<string> NormalizeIds(IReadOnlyList<string>? ids) =>
        ids?.Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? Array.Empty<string>();

    private sealed class FundProfileStorageModel
    {
        public string? LastSelectedFundProfileId { get; set; }

        public List<FundProfileDetail> Profiles { get; set; } = new();
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(FundProfileStorageModel))]
    [JsonSerializable(typeof(List<FundProfileDetail>))]
    [JsonSerializable(typeof(FundProfileDetail))]
    private sealed partial class FundProfileStorageJsonContext : JsonSerializerContext;
}

public sealed class FundProfileChangedEventArgs : EventArgs
{
    public FundProfileChangedEventArgs(FundProfileDetail profile)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public FundProfileDetail Profile { get; }
}
