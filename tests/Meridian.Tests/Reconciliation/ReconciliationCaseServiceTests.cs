using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;

namespace Meridian.Tests.Reconciliation;

public sealed class ReconciliationCaseServiceTests
{
    [Fact]
    public async Task Creates_open_cases_and_tracks_status_history()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-case-{Guid.NewGuid():N}");
        var store = new JsonReconciliationCaseStore(root);
        var service = new ReconciliationCaseService(store);
        var created = await service.CreateOpenCasesAsync("imp1", [new MatchOutcome("x", "unmatched", "", 0.2m, "none")]);
        var updated = await service.UpdateStatusAsync(created[0].CaseId, "InReview", "triaged");
        Assert.Equal("InReview", updated.Status);
        Assert.True(updated.History.Count >= 2);
        Assert.Equal("unassigned", updated.Owner);
        Assert.Equal("system", updated.LastUpdatedBy);
        Assert.NotNull(updated.DueAtUtc);

        var reloaded = await store.GetAsync(created[0].CaseId);
        Assert.NotNull(reloaded);
        Assert.Equal("InReview", reloaded!.Status);
        Assert.Equal(updated.History.Count, reloaded.History.Count);

        var caseFileName = $"{Uri.EscapeDataString(created[0].CaseId)}.json";
        Assert.True(File.Exists(Path.Combine(root, "reconciliation", "cases", caseFileName)));
        var auditPath = Path.Combine(root, "reconciliation", "cases", "_audit", "case-history.jsonl");
        Assert.True(File.Exists(auditPath));
        var auditLines = await File.ReadAllLinesAsync(auditPath);
        Assert.True(auditLines.Length >= 2);
        Assert.Contains(auditLines, line => line.Contains("\"status\":\"InReview\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rejects_invalid_or_terminal_status_transitions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-case-{Guid.NewGuid():N}");
        var service = new ReconciliationCaseService(new JsonReconciliationCaseStore(root));
        var created = await service.CreateOpenCasesAsync("imp1", [new MatchOutcome("x", "unmatched", "", 0.2m, "none")]);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateStatusAsync(created[0].CaseId, "Triaged", "unsupported status"));

        var approved = await service.UpdateStatusAsync(created[0].CaseId, "Approved", "approved by controller");
        Assert.Equal("Approved", approved.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateStatusAsync(created[0].CaseId, "Resolved", "terminal transition"));
    }
}
