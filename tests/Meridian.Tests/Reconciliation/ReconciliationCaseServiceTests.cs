using System.Text.Json;
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
        var assigned = await service.AssignAsync(created[0].CaseId, "alice", "primary owner");
        var updated = await service.UpdateStatusAsync(created[0].CaseId, "Investigating", "triaged");
        var commented = await service.AddCommentAsync(created[0].CaseId, "Evidence", "Need broker confirms", "alice");
        Assert.Equal("alice", assigned.Owner);
        Assert.Equal("Investigating", updated.Status);
        Assert.Single(commented.CommentThreads);
        Assert.True(updated.History.Count >= 2);
        Assert.Equal("alice", commented.Owner);
        Assert.Equal("system", updated.LastUpdatedBy);
        Assert.NotNull(updated.DueAtUtc);

        var reloaded = await store.GetAsync(created[0].CaseId);
        Assert.NotNull(reloaded);
        Assert.Equal("Investigating", reloaded!.Status);
        Assert.Equal(updated.History.Count, reloaded.History.Count);

        var caseFileName = $"{Uri.EscapeDataString(created[0].CaseId)}.json";
        Assert.True(File.Exists(Path.Combine(root, "reconciliation", "cases", caseFileName)));
        var auditPath = Path.Combine(root, "reconciliation", "cases", "_audit", "case-history.jsonl");
        Assert.True(File.Exists(auditPath));
        var auditLines = await File.ReadAllLinesAsync(auditPath);
        Assert.True(auditLines.Length >= 2);
        Assert.Contains(auditLines, line => line.Contains("\"status\":\"Investigating\"", StringComparison.Ordinal));
        Assert.All(auditLines, line =>
        {
            using var audit = JsonDocument.Parse(line);
            Assert.Equal(created[0].CaseId, audit.RootElement.GetProperty("caseId").GetString());
            Assert.Equal("system", audit.RootElement.GetProperty("actor").GetString());
            Assert.True(audit.RootElement.TryGetProperty("latestHistory", out _));
        });
    }

    [Fact]
    public async Task Rejects_invalid_or_terminal_status_transitions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-case-{Guid.NewGuid():N}");
        var service = new ReconciliationCaseService(new JsonReconciliationCaseStore(root));
        var created = await service.CreateOpenCasesAsync("imp1", [new MatchOutcome("x", "unmatched", "", 0.2m, "none")]);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateStatusAsync(created[0].CaseId, "Triaged", "unsupported status"));

        var investigating = await service.UpdateStatusAsync(created[0].CaseId, "Investigating", "investigate");
        var resolved = await service.UpdateStatusAsync(created[0].CaseId, "Resolved", "fixed");
        Assert.Equal("Resolved", resolved.Status);
        var signedOff = await service.UpdateStatusAsync(created[0].CaseId, "SignedOff", "signed");
        Assert.Equal("SignedOff", signedOff.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateStatusAsync(created[0].CaseId, "Open", "terminal transition"));
    }

    [Fact]
    public async Task Terminal_decisions_retain_evidence_references_and_decision_notes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-case-{Guid.NewGuid():N}");
        var store = new JsonReconciliationCaseStore(root);
        var service = new ReconciliationCaseService(store);
        var created = await service.CreateOpenCasesAsync("imp1", [new MatchOutcome("row-hash-1", "unmatched", "", 0.2m, "none")]);

        var investigating = await service.UpdateStatusAsync(created[0].CaseId, "Investigating", "triaged with broker statement evidence");
        var resolved = await service.UpdateStatusAsync(investigating.CaseId, "Resolved", "broker correction posted");

        Assert.Contains("statement-row:row-hash-1", resolved.EvidenceReferences);
        Assert.Contains(resolved.History, entry => entry.ToStatus == "Resolved" && entry.EvidenceId == "statement-row:row-hash-1");
        var note = Assert.Single(resolved.DecisionNotes);
        Assert.Equal("broker correction posted", note.Note);
        Assert.Contains("statement-row:row-hash-1", note.EvidenceReferences);

        var reloaded = await store.GetAsync(created[0].CaseId);
        Assert.NotNull(reloaded);
        Assert.Contains("statement-row:row-hash-1", reloaded!.EvidenceReferences);
        Assert.Single(reloaded.DecisionNotes);

        var dismissed = await service.CreateOpenCasesAsync("imp1", [new MatchOutcome("row-hash-2", "unmatched", "", 0.2m, "none")]);
        var dismissedDecision = await service.UpdateStatusAsync(dismissed[0].CaseId, "Dismissed", "custodian memo accepted");
        Assert.Equal("Dismissed", dismissedDecision.Status);
        Assert.Equal("dismissed", dismissedDecision.Resolution!.ResolutionCode);
        Assert.Contains("statement-row:row-hash-2", dismissedDecision.DecisionNotes.Single().EvidenceReferences);
    }

    [Fact]
    public async Task SaveAsync_WhenCancelledBeforeWrite_DoesNotCreateCaseOrAuditFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-case-{Guid.NewGuid():N}");
        var store = new JsonReconciliationCaseStore(root);
        var now = DateTimeOffset.UtcNow;
        var reconciliationCase = new ReconciliationCase(
            "case-cancelled",
            "imp-cancelled",
            "Open",
            "Unmatched statement row",
            0.2m,
            "none",
            now,
            [new ReconciliationCaseHistoryEntry(now, "None", "Open", "Case created from matcher outcome")]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SaveAsync(reconciliationCase, cts.Token));

        Assert.False(Directory.Exists(Path.Combine(root, "reconciliation", "cases")));
    }
}
