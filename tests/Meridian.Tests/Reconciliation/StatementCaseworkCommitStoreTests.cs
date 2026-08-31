using FluentAssertions;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Meridian.Tests.Reconciliation;

/// <summary>
/// Guards source-side statement casework authority when concurrent operators or a process restart
/// race to retain the same immutable commit envelope.
/// </summary>
public sealed class StatementCaseworkCommitStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-statement-casework-commit-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Scenario_ConcurrentSameCommand_OneImmutableEnvelopeAndCompletionConverge()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var envelope = BuildEnvelope("command-concurrent", new string('a', 64));
        var first = new FileStatementCaseworkCommitStore(_root);
        var second = new FileStatementCaseworkCommitStore(_root);

        var retained = await Task.WhenAll(
            first.PrepareAsync(envelope, timeout.Token),
            second.PrepareAsync(envelope, timeout.Token));
        await Task.WhenAll(
            first.CompleteAsync(envelope.CommandId, envelope.InputHashSha256, timeout.Token),
            second.CompleteAsync(envelope.CommandId, envelope.InputHashSha256, timeout.Token));

        retained[0].Should().BeEquivalentTo(envelope);
        retained[1].Should().BeEquivalentTo(envelope);
        (await first.IsCompletedAsync(
            envelope.CommandId,
            envelope.InputHashSha256,
            timeout.Token)).Should().BeTrue();
        Directory.EnumerateFiles(
                Path.Combine(_root, "reconciliation", "statement-casework-commits", "envelopes"),
                "*.json")
            .Should().ContainSingle();
        Directory.EnumerateFiles(
                Path.Combine(_root, "reconciliation", "statement-casework-commits", "completed"),
                "*.json")
            .Should().ContainSingle();
    }

    [Fact]
    public async Task Scenario_CommandIdReusedForDifferentInput_SourceCommitFailsClosed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var store = new FileStatementCaseworkCommitStore(_root);
        await store.PrepareAsync(BuildEnvelope("command-conflict", new string('a', 64)), timeout.Token);

        var act = async () => await store.PrepareAsync(
            BuildEnvelope("command-conflict", new string('b', 64)),
            timeout.Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different input*");
    }

    [Fact]
    public async Task Scenario_PreparedSourceCommit_ListByRunExposesRecoveryAuthorityBeforeCompletion()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var store = new FileStatementCaseworkCommitStore(_root);
        var envelope = BuildEnvelope("command-prepared", new string('c', 64));
        await store.PrepareAsync(envelope, timeout.Token);

        var retained = await store.ListByRunAsync(envelope.ImportId, timeout.Token);

        retained.Should().ContainSingle().Which.Should().BeEquivalentTo(envelope);
        (await store.IsCompletedAsync(
            envelope.CommandId,
            envelope.InputHashSha256,
            timeout.Token)).Should().BeFalse();
        (await store.ListByRunAsync("another-import", timeout.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task Scenario_LegacyBreakReceipt_OnlyExactInputFingerprintCanBeAdopted()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var breakStore = new JsonReconciliationBreakStore(_root);
        var original = BuildBreak("legacy-break", "legacy-import", "Open");
        await breakStore.WriteAsync([original], timeout.Token);
        var update = new StatementBreakCaseworkUpdate(
            original.BreakId,
            original.ImportId,
            "Resolved",
            "fund-ops",
            "Resolve",
            "legacy-command",
            "legacy-correlation",
            "Reviewed.",
            "Resolved",
            "controller",
            "approval://legacy",
            null,
            ["evidence://legacy"],
            new DateTimeOffset(2026, 7, 26, 10, 0, 0, TimeSpan.Zero));
        var inputHash = StatementBreakCaseworkFingerprint.Compute(update);
        var legacyCanonical = JsonSerializer.Serialize(
            update,
            new JsonSerializerOptions { WriteIndented = true });
        var legacyInputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"meridian.statement-break-casework.v1\n{legacyCanonical}"))).ToLowerInvariant();
        inputHash.Should().Be(legacyInputHash,
            "the source-generated fingerprint must preserve the exact legacy JSON contract");
        var next = original with { Status = update.Status };
        var audit = new StatementBreakCaseworkAuditEvent(
            $"statement-casework:{inputHash[..24]}",
            original.BreakId,
            original.ImportId,
            original.Status,
            next.Status,
            update.Actor,
            update.Action,
            update.CommandId,
            update.CorrelationId,
            update.Reason,
            update.Disposition,
            update.ApprovalActor,
            update.ApprovalReference,
            update.SupersedingBreakId,
            update.EvidenceLinks,
            update.OccurredAtUtc,
            inputHash);
        var legacyReceiptFixture = new StatementCaseworkLegacyReceipt(
            update.CommandId,
            update.BreakId,
            inputHash,
            next,
            audit);
        var receiptRoot = Path.Combine(
            _root,
            "reconciliation",
            "statement-breaks",
            "_casework",
            "receipts");
        Directory.CreateDirectory(receiptRoot);
        var receiptName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(update.CommandId)))
            .ToLowerInvariant();
        await File.WriteAllTextAsync(
            Path.Combine(receiptRoot, $"{receiptName}.json"),
            JsonSerializer.Serialize(
                legacyReceiptFixture,
                StatementLegacyCaseworkJsonContext.Default.StatementCaseworkLegacyReceipt),
            timeout.Token);
        var store = new FileStatementCaseworkCommitStore(_root);

        var receipt = await store.GetLegacyReceiptAsync(update.CommandId, inputHash, timeout.Token);

        receipt.Should().NotBeNull();
        receipt!.Audit.PreviousStatus.Should().Be("Open");
        receipt.Record.Status.Should().Be("Resolved");
        var conflict = async () => await store.GetLegacyReceiptAsync(
            update.CommandId,
            new string('f', 64),
            timeout.Token);
        await conflict.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Scenario_DirectLegacyCaseworkMutation_IsUnavailableAndLeavesBreakUnchanged()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var breakStore = new JsonReconciliationBreakStore(_root);
        var original = BuildBreak("direct-break", "direct-import", "Open");
        await breakStore.WriteAsync([original], timeout.Token);
        var update = new StatementBreakCaseworkUpdate(
            original.BreakId,
            original.ImportId,
            "Resolved",
            "fund-ops",
            "Resolve",
            "direct-command",
            "direct-correlation",
            "Reviewed.",
            "Resolved",
            "controller",
            "approval://direct",
            null,
            ["evidence://direct"],
            new DateTimeOffset(2026, 7, 26, 10, 0, 0, TimeSpan.Zero));

        var act = async () => await breakStore.ApplyCaseworkAsync(update, timeout.Token);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*immutable statement casework commit*");
        (await breakStore.GetAsync(original.BreakId, timeout.Token)).Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task Scenario_DirectProjectionWithoutPreparedCommit_IsRejectedAndLeavesBreakUnchanged()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var breakStore = new JsonReconciliationBreakStore(_root);
        var original = BuildBreak("unprepared-break", "unprepared-import", "Open");
        await breakStore.WriteAsync([original], timeout.Token);
        var inputHash = new string('a', 64);
        var commitStore = new FileStatementCaseworkCommitStore(_root);

        var act = async () => await breakStore.MaterializeCaseworkBreakAsync(
            commitStore,
            "unprepared-command",
            inputHash,
            timeout.Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot project before*");
        (await breakStore.GetAsync(original.BreakId, timeout.Token)).Should().BeEquivalentTo(original);
    }

    private static StatementCaseworkCommitEnvelope BuildEnvelope(string commandId, string inputHash)
    {
        var original = BuildBreak("break-1", "import-1", "Open");
        var next = original with { Status = "Resolved" };
        var occurredAt = new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero);
        var breakAudit = new StatementBreakCaseworkAuditEvent(
            "statement-casework:audit",
            original.BreakId,
            original.ImportId,
            original.Status,
            next.Status,
            "fund-ops",
            "Resolve",
            commandId,
            "correlation-1",
            "Reviewed.",
            "Resolved",
            "controller",
            "approval://1",
            null,
            ["evidence://1"],
            occurredAt,
            inputHash);
        var caseAudit = new ReconciliationCaseAuditEvent(
            "statement-casework:case-audit",
            "StatementBreakDisposed",
            occurredAt,
            "fund-ops",
            "Resolved from retained statement evidence.");
        var originalCase = new ReconciliationCase(
            $"case:{original.BreakId}",
            original.ImportId,
            "Open",
            "Reviewed.",
            0.9m,
            "Review pending.",
            occurredAt.AddDays(-1),
            []);
        var nextCase = originalCase with
        {
            Status = "Resolved",
            Rationale = "Resolved.",
            History = [new ReconciliationCaseHistoryEntry(occurredAt, "Open", "Resolved", "Reviewed.")],
            AuditEvents = [caseAudit],
            DecisionNotes =
            [
                new ReconciliationCaseDecisionNote(
                    "decision-1",
                    "fund-ops",
                    occurredAt,
                    "Reviewed.",
                    ["evidence://1"])
            ]
        };
        return new StatementCaseworkCommitEnvelope(
            StatementCaseworkCommitEnvelope.CurrentSchemaVersion,
            commandId,
            inputHash,
            original.ImportId,
            original,
            next,
            originalCase,
            nextCase,
            breakAudit,
            caseAudit,
            occurredAt,
            AdoptedLegacyReceipt: false);
    }

    private static ReconciliationBreakRecord BuildBreak(string breakId, string importId, string status)
        => new(
            breakId,
            importId,
            importId,
            $"{importId}:1",
            "AMOUNT_MISMATCH",
            "cash",
            125m,
            1m,
            true,
            new DateTimeOffset(2026, 7, 1, 8, 5, 0, TimeSpan.Zero),
            status);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
