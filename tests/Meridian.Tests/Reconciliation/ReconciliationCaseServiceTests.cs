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
        Assert.Equal("Open", updated.Disposition);
        Assert.True(updated.AgingDays >= 0);

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
            var actor = audit.RootElement.GetProperty("actor").GetString();
            Assert.True(actor is "system" or "alice");
            Assert.True(audit.RootElement.TryGetProperty("latestHistory", out _));
        });
    }

    [Fact]
    public async Task Store_preserves_durable_casework_metadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-case-{Guid.NewGuid():N}");
        var store = new JsonReconciliationCaseStore(root);
        var now = DateTimeOffset.UtcNow;
        var reconciliationCase = new ReconciliationCase(
            "case-with-evidence",
            "import-1",
            "Open",
            "Broker statement dividend break",
            0.35m,
            "External dividend activity has no deterministic ledger income match.",
            now,
            [new ReconciliationCaseHistoryEntry(now, "None", "Open", "Case created from statement row") { EvidenceId = "statement-row:import-1:2" }])
        {
            Owner = "fund-ops",
            Priority = "High",
            DueAtUtc = now.AddDays(2),
            Disposition = "NeedsInvestigation",
            AgingDays = 0,
            Attachments =
            [
                new ReconciliationCaseAttachment(
                    "statement-row:import-1:2",
                    "ExternalStatementRow",
                    "broker",
                    "import-1:2",
                    "abc123",
                    "/api/workstation/reconciliation/statement-runs/import-1#row-2",
                    now)
            ],
            BreakExplanation = new ReconciliationBreakExplanation(
                "Dividend break from broker statement row 2.",
                ["broker", "Meridian ledger", "Meridian positions"],
                "External dividend activity has no deterministic ledger income or receivable match.",
                "Dividend income or receivable postings may be missing.",
                "Review corporate-action evidence and broker activity before resolving.",
                "Controller",
                ["/api/workstation/reconciliation/statement-runs/import-1#row-2", "statement-row:import-1:2", "statement-hash:abc123"])
        };

        await store.SaveAsync(reconciliationCase);
        var reloaded = await store.GetAsync(reconciliationCase.CaseId);

        Assert.NotNull(reloaded);
        Assert.Equal("fund-ops", reloaded!.Owner);
        Assert.Equal("NeedsInvestigation", reloaded.Disposition);
        Assert.Equal(0, reloaded.AgingDays);
        var attachment = Assert.Single(reloaded.Attachments);
        Assert.Equal("ExternalStatementRow", attachment.EvidenceKind);
        Assert.Equal("abc123", attachment.ContentHash);
        Assert.NotNull(reloaded.BreakExplanation);
        Assert.Equal("Controller", reloaded.BreakExplanation.RequiredSignoffRole);
        Assert.Contains("Meridian ledger", reloaded.BreakExplanation.SourceSystems);
        Assert.Contains("statement-hash:abc123", reloaded.BreakExplanation.EvidenceLinks);
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
