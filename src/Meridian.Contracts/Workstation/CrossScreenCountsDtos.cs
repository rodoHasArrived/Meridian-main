namespace Meridian.Contracts.Workstation;

/// <summary>
/// Counts that appear on more than one workstation screen (Trading, Data, Settings, Accounting and
/// the WPF desktop), each computed once with the definition its <c>Definition</c> text states, so
/// every surface shows the same number for the same quantity. A count is <c>null</c> when its source
/// is not registered or the caller may not read it; clients then show "—", never a substitute number.
/// </summary>
public sealed record WorkstationCrossScreenCountsDto(
    DateTimeOffset AsOf,
    WorkstationCrossScreenCount? OpenOrders,
    WorkstationProviderHealthCount? Providers,
    WorkstationCrossScreenCount? OpenBreaks);

/// <summary>A single cross-screen count and the plain-language definition it was counted with.</summary>
public sealed record WorkstationCrossScreenCount(int Count, string Definition);

/// <summary>Healthy providers out of every provider the Data workspace lists.</summary>
public sealed record WorkstationProviderHealthCount(int Healthy, int Total, string Definition);
