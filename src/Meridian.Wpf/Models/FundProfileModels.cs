using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Models;

/// <summary>
/// Lightweight summary shown in the fund selection surface.
/// </summary>
public sealed record FundProfileSummary(
    string FundProfileId,
    string DisplayName,
    string LegalEntityName,
    string BaseCurrency,
    string DefaultWorkspaceId,
    string DefaultLandingPageTag,
    FundLedgerScope DefaultLedgerScope,
    bool IsDefault,
    DateTimeOffset? LastOpenedAt = null);

/// <summary>
/// Desktop-local v1 fund profile used to bootstrap the active workstation context.
/// </summary>
public sealed record FundProfileDetail(
    string FundProfileId,
    string DisplayName,
    string LegalEntityName,
    string BaseCurrency,
    string DefaultWorkspaceId,
    string DefaultLandingPageTag,
    FundLedgerScope DefaultLedgerScope,
    IReadOnlyList<string>? EntityIds = null,
    IReadOnlyList<string>? SleeveIds = null,
    IReadOnlyList<string>? VehicleIds = null,
    bool IsDefault = false,
    DateTimeOffset? LastOpenedAt = null)
{
    public FundProfileSummary ToSummary() =>
        new(
            FundProfileId,
            DisplayName,
            LegalEntityName,
            BaseCurrency,
            DefaultWorkspaceId,
            DefaultLandingPageTag,
            DefaultLedgerScope,
            IsDefault,
            LastOpenedAt);
}
