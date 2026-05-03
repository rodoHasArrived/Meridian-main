using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;

namespace Meridian.Tests.Reconciliation;

public sealed class ReconciliationCaseServiceTests
{
    [Fact]
    public async Task Creates_open_cases_and_tracks_status_history()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-case-{Guid.NewGuid():N}");
        var service = new ReconciliationCaseService(new JsonReconciliationCaseStore(root));
        var created = await service.CreateOpenCasesAsync("imp1", [new MatchOutcome("x", "unmatched", "", 0.2m, "none")]);
        var updated = await service.UpdateStatusAsync(created[0].CaseId, "InReview", "triaged");
        Assert.Equal("InReview", updated.Status);
        Assert.True(updated.History.Count >= 2);
    }
}
