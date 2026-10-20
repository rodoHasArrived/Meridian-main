using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.DataIntegration.Credentials;
using Xunit;

namespace Meridian.Tests.Application.Config;

/// <summary>
/// PRD-002 credential/audit commit ambiguity: a vault mutation whose audit append fails is no longer
/// reported as a failed save. The audit record is journaled ahead of the vault write and stays
/// journaled as the durable marker until it is appended, by the next mutation or at restart.
/// </summary>
[Collection("Sequential")]
public sealed class ProviderCredentialAuditDurabilityTests : IDisposable
{
    private readonly string _root;
    private readonly string _auditPath;
    private readonly string _pendingPath;

    public ProviderCredentialAuditDurabilityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "credential-audit-durability", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _auditPath = Path.Combine(_root, ".mdc", "provider-credentials.audit.jsonl");
        _pendingPath = Path.Combine(_root, ".mdc", "provider-credentials.audit.pending.jsonl");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task SaveAsync_AuditFailureAfterVaultCommit_SucceedsAndRetainsPendingAuditJournal()
    {
        var store = new FileProviderCredentialStore(_root);
        BlockAuditLog();

        await store.SaveAsync(SaveRequest("alpaca", "committed-key", "committed-secret"));

        var read = await store.ReadForProviderAsync("alpaca");
        read!.Get("SecretKey").Should().Be("committed-secret", "the vault mutation committed");
        File.Exists(_pendingPath).Should().BeTrue("the owed audit record is the durable marker");
        var journal = await File.ReadAllLinesAsync(_pendingPath);
        journal.Should().ContainSingle().Which.Should().Contain("\"action\":\"save\"")
            .And.NotContain("committed-key").And.NotContain("committed-secret");
        var status = await store.GetStatusAsync("alpaca");
        status.AuditMetadata.Should().Contain("auditPending", "1");
    }

    [Fact]
    public async Task DeleteAsync_AuditFailureAfterVaultCommit_SucceedsAndRetainsPendingAuditJournal()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(SaveRequest("alpaca", "to-delete-key", "to-delete-secret"));
        BlockAuditLog();

        await store.DeleteAsync("alpaca", "operator-delete");

        (await store.ReadForProviderAsync("alpaca")).Should().BeNull();
        var journal = await File.ReadAllLinesAsync(_pendingPath);
        journal.Should().ContainSingle().Which.Should().Contain("\"action\":\"delete\"");
    }

    [Fact]
    public async Task Restart_ReplayPendingAuditAsync_AppendsOrphanedRecordOnceAndRemovesJournal()
    {
        var store = new FileProviderCredentialStore(_root);
        BlockAuditLog();
        await store.SaveAsync(SaveRequest("alpaca", "k", "s"));
        var journaled = (await File.ReadAllLinesAsync(_pendingPath)).Single();
        UnblockAuditLog();

        var reopened = new FileProviderCredentialStore(_root);
        var replayed = await reopened.ReplayPendingAuditAsync();

        replayed.Should().Be(1);
        File.Exists(_pendingPath).Should().BeFalse();
        var audit = await File.ReadAllLinesAsync(_auditPath);
        audit.Should().ContainSingle();
        var originalTimestamp = System.Text.Json.JsonDocument.Parse(journaled).RootElement.GetProperty("record").GetProperty("timestamp").GetString();
        var replayedRecord = System.Text.Json.JsonDocument.Parse(audit[0]).RootElement;
        replayedRecord.GetProperty("timestamp").GetString().Should().Be(originalTimestamp, "replay keeps the mutation's own time");
        replayedRecord.TryGetProperty("replayedAt", out _).Should().BeTrue();
        (await reopened.ReplayPendingAuditAsync()).Should().Be(0, "replay is idempotent");
    }

    [Fact]
    public async Task NextMutation_ReplaysBacklogBeforeAppendingItsOwnRecord()
    {
        var store = new FileProviderCredentialStore(_root);
        BlockAuditLog();
        await store.SaveAsync(SaveRequest("alpaca", "first-key", "first-secret"));
        UnblockAuditLog();

        await store.SaveAsync(SaveRequest("polygon", "second-key", "second-secret"));

        File.Exists(_pendingPath).Should().BeFalse();
        var audit = await File.ReadAllLinesAsync(_auditPath);
        audit.Should().HaveCount(2);
        audit[0].Should().Contain("\"providerId\":\"alpaca\"").And.Contain("replayedAt");
        audit[1].Should().Contain("\"providerId\":\"polygon\"").And.NotContain("replayedAt");
    }

    [Fact]
    public async Task AuditJournal_IsRemovedAfterASuccessfulCommit()
    {
        var store = new FileProviderCredentialStore(_root);

        await store.SaveAsync(SaveRequest("alpaca", "k", "s"));
        await store.RecordVerificationAsync(new ProviderCredentialVerificationUpdate("alpaca", true, ExternalAccountId: "acct"));

        File.Exists(_pendingPath).Should().BeFalse();
        (await File.ReadAllLinesAsync(_auditPath)).Should().HaveCount(2, "exactly one audit line per mutation, no intent lines");
        (await store.GetStatusAsync("alpaca")).AuditMetadata.Should().NotContainKey("auditPending");
    }

    [Fact]
    public async Task SaveAsync_JournalWriteFailure_DoesNotTouchVault()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(SaveRequest("alpaca", "original-key", "original-secret"));
        var vaultBefore = await File.ReadAllBytesAsync(store.VaultPath);
        var auditBefore = await File.ReadAllBytesAsync(_auditPath);
        Directory.CreateDirectory(_pendingPath);

        var act = () => store.SaveAsync(SaveRequest("alpaca", "replacement-key", "replacement-secret"));

        var failure = await Record.ExceptionAsync(act);
        (failure is IOException or UnauthorizedAccessException).Should().BeTrue("the journal is a directory");
        (await File.ReadAllBytesAsync(store.VaultPath)).Should().Equal(vaultBefore, "fail closed before the vault is written");
        (await File.ReadAllBytesAsync(_auditPath)).Should().Equal(auditBefore);
        (await store.ReadForProviderAsync("alpaca"))!.Get("SecretKey").Should().Be("original-secret");
    }

    [Fact]
    public async Task SaveOAuthTokenAsync_AuditFailure_ThrowsAuditDeferredExceptionThatIsAnIOException()
    {
        var store = new FileProviderCredentialStore(_root);
        BlockAuditLog();

        var act = () => store.SaveOAuthTokenAsync("provider", new OAuthToken("access", "Bearer", DateTimeOffset.UtcNow.AddHours(1), "refresh"));

        var deferred = (await act.Should().ThrowAsync<ProviderCredentialAuditDeferredException>()).Which;
        deferred.Should().BeAssignableTo<IOException>("OAuth callers already reconcile on IOException");
        deferred.VaultCommitted.Should().BeTrue();
        deferred.ProviderId.Should().Be("provider");
        deferred.Action.Should().Be("oauth-save");
        File.Exists(deferred.PendingAuditPath).Should().BeTrue();
        (await store.ReadOAuthTokensAsync())["provider"].AccessToken.Should().Be("access", "the token was persisted");
    }

    [Fact]
    public async Task PendingAudit_AuditPathStillBlocked_BacklogAccumulatesAcrossMutations()
    {
        var store = new FileProviderCredentialStore(_root);
        BlockAuditLog();

        await store.SaveAsync(SaveRequest("alpaca", "k1", "s1"));
        await store.SaveAsync(SaveRequest("polygon", "k2", "s2"));

        var journal = await File.ReadAllLinesAsync(_pendingPath);
        journal.Should().HaveCount(2);
        journal[0].Should().Contain("\"providerId\":\"alpaca\"");
        journal[1].Should().Contain("\"providerId\":\"polygon\"");
        (await store.GetStatusAsync("alpaca")).AuditMetadata.Should().Contain("auditPending", "2");

        UnblockAuditLog();
        (await store.ReplayPendingAuditAsync()).Should().Be(2);
        var audit = await File.ReadAllLinesAsync(_auditPath);
        audit.Should().HaveCount(2);
        audit[0].Should().Contain("alpaca");
        audit[1].Should().Contain("polygon");
    }

    [Fact]
    public async Task Replay_IntentWhoseVaultWriteNeverCommitted_IsDiscardedNotAppended()
    {
        // Simulates a crash between the journal write and the vault write: the journal holds an
        // "intent" for a save whose credential generation the vault never received. Replaying it as a
        // completed save would fabricate an audit record.
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(SaveRequest("alpaca", "k", "s"));
        var auditBefore = await File.ReadAllLinesAsync(_auditPath);
        var intent = JournalLine(phase: "intent", kind: "provider-save", key: "alpaca", generation: "never-committed-generation",
            record: "{\"timestamp\":\"2026-10-20T00:00:00+00:00\",\"providerId\":\"alpaca\",\"action\":\"save\",\"actor\":\"ghost\"}");
        await File.WriteAllTextAsync(_pendingPath, intent + "\n");

        var replayed = await new FileProviderCredentialStore(_root).ReplayPendingAuditAsync();

        replayed.Should().Be(0);
        File.Exists(_pendingPath).Should().BeFalse("the aborted intent is discarded, not retained as owed audit");
        (await File.ReadAllLinesAsync(_auditPath)).Should().Equal(auditBefore);
    }

    [Fact]
    public async Task Replay_IntentWhoseVaultWriteDidCommit_IsAppendedWithReplayMarker()
    {
        // Crash between the vault write and the commit marker: the vault holds the saved generation, so
        // the intent is proven and must be appended rather than lost.
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(SaveRequest("alpaca", "k", "s"));
        var generation = (await store.GetStatusAsync("alpaca")).AuditMetadata["credentialGeneration"];
        var auditBefore = await File.ReadAllLinesAsync(_auditPath);
        var intent = JournalLine(phase: "intent", kind: "provider-save", key: "alpaca", generation: generation,
            record: "{\"timestamp\":\"2026-10-20T00:00:00+00:00\",\"providerId\":\"alpaca\",\"action\":\"save\",\"actor\":\"recovered\"}");
        await File.WriteAllTextAsync(_pendingPath, intent + "\n");

        var replayed = await new FileProviderCredentialStore(_root).ReplayPendingAuditAsync();

        replayed.Should().Be(1);
        var audit = await File.ReadAllLinesAsync(_auditPath);
        audit.Should().HaveCount(auditBefore.Length + 1);
        audit[^1].Should().Contain("\"actor\":\"recovered\"").And.Contain("replayedAt");
    }

    [Fact]
    public async Task Replay_DeleteIntentIsProvenByAbsence_AndStaleDeleteIntentIsDiscarded()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(SaveRequest("alpaca", "k", "s"));
        var record = "{\"timestamp\":\"2026-10-20T00:00:00+00:00\",\"providerId\":\"alpaca\",\"action\":\"delete\",\"actor\":\"x\"}";
        // alpaca still exists, so a delete intent for it never committed.
        await File.WriteAllTextAsync(_pendingPath, JournalLine("intent", "provider-delete", "alpaca", null, record) + "\n");
        (await new FileProviderCredentialStore(_root).ReplayPendingAuditAsync()).Should().Be(0);

        // polygon never existed, so a delete intent for it is consistent with the vault and is appended.
        var polygonRecord = record.Replace("alpaca", "polygon");
        await File.WriteAllTextAsync(_pendingPath, JournalLine("intent", "provider-delete", "polygon", null, polygonRecord) + "\n");
        (await new FileProviderCredentialStore(_root).ReplayPendingAuditAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SaveAsync_VaultWriteFailure_RemovesTheJournaledIntent()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(SaveRequest("alpaca", "original-key", "original-secret"));
        var auditBefore = await File.ReadAllLinesAsync(_auditPath);
        // Make the primary vault path unwritable: the backup still loads, the atomic replace fails.
        File.Delete(store.VaultPath);
        Directory.CreateDirectory(store.VaultPath);

        var act = () => store.SaveAsync(SaveRequest("alpaca", "replacement-key", "replacement-secret"));

        var failure = await Record.ExceptionAsync(act);
        failure.Should().NotBeNull();
        File.Exists(_pendingPath).Should().BeFalse("a failed vault write must not leave an intent behind");
        (await File.ReadAllLinesAsync(_auditPath)).Should().Equal(auditBefore);
    }

    [Fact]
    public async Task PendingJournal_RecordsCommittedPhaseWhenAuditAppendFails()
    {
        var store = new FileProviderCredentialStore(_root);
        BlockAuditLog();

        await store.SaveAsync(SaveRequest("alpaca", "k", "s"));

        var line = (await File.ReadAllLinesAsync(_pendingPath)).Single();
        var journal = System.Text.Json.JsonDocument.Parse(line).RootElement;
        journal.GetProperty("phase").GetString().Should().Be("committed");
        journal.GetProperty("check").GetProperty("kind").GetString().Should().Be("provider-save");
        journal.GetProperty("record").GetProperty("action").GetString().Should().Be("save");
    }

    private static string JournalLine(string phase, string kind, string key, string? generation, string record)
    {
        var generationJson = generation is null ? "null" : $"\"{generation}\"";
        return $"{{\"phase\":\"{phase}\",\"providerId\":\"{key}\",\"action\":\"{kind}\",\"check\":{{\"kind\":\"{kind}\",\"key\":\"{key}\",\"credentialGeneration\":{generationJson},\"verifiedAt\":null}},\"record\":{record}}}";
    }

    private void BlockAuditLog()
    {
        if (File.Exists(_auditPath))
            File.Delete(_auditPath);
        Directory.CreateDirectory(_auditPath);
    }

    private void UnblockAuditLog() => Directory.Delete(_auditPath);

    private static ProviderCredentialSaveRequest SaveRequest(string providerId, string key, string secret)
        => providerId == "polygon"
            ? new ProviderCredentialSaveRequest(providerId, new Dictionary<string, string?> { ["ApiKey"] = secret }, Actor: "test-operator")
            : new ProviderCredentialSaveRequest(
                providerId,
                new Dictionary<string, string?> { ["KeyId"] = key, ["SecretKey"] = secret },
                Environment: "paper",
                Actor: "test-operator");
}
