using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Support;

internal sealed class FakeWorkstationStrategyBriefingApiClient : IWorkstationStrategyBriefingApiClient
{
    public StrategyBriefingDto? Briefing { get; init; }

    public Task<StrategyBriefingDto?> GetBriefingAsync(CancellationToken ct = default)
        => Task.FromResult(Briefing);
}
