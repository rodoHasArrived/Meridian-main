using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

/// <summary>
/// Provides the loaded fund-profile catalog and selection, with operations to load, save, and delete profiles.
/// </summary>
public interface IFundProfileCatalog
{
    IReadOnlyList<FundProfileDetail> Profiles { get; }

    FundProfileDetail? CurrentFundProfile { get; }

    string? LastSelectedFundProfileId { get; }

    Task LoadAsync(CancellationToken ct = default);

    Task<FundProfileDetail> UpsertProfileAsync(FundProfileDetail profile, CancellationToken ct = default);

    Task DeleteProfileAsync(string fundProfileId, CancellationToken ct = default);
}
