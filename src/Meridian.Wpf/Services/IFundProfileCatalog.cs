using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

public interface IFundProfileCatalog
{
    IReadOnlyList<FundProfileDetail> Profiles { get; }

    FundProfileDetail? CurrentFundProfile { get; }

    string? LastSelectedFundProfileId { get; }

    Task LoadAsync(CancellationToken ct = default);

    Task<FundProfileDetail> UpsertProfileAsync(FundProfileDetail profile, CancellationToken ct = default);

    Task DeleteProfileAsync(string fundProfileId, CancellationToken ct = default);
}
