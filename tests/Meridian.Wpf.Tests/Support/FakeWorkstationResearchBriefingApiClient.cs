using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Support;

internal sealed class FakeWorkstationResearchBriefingApiClient : IWorkstationResearchBriefingApiClient
{
    public ResearchBriefingDto? Briefing { get; init; }

    public Task<ResearchBriefingDto?> GetBriefingAsync(CancellationToken ct = default)
        => Task.FromResult(Briefing);
}
