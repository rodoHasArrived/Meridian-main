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
            record: "{\"timestamp\":\"2026-10-20T00:00:00+00:00\",\"providerId\":\"alpaca\",\"action\":\"save\",\"actor\":\"ghost\"}",
            publicationId: Guid.NewGuid().ToString("N"));
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
            record: "{\"timestamp\":\"2026-10-20T00:00:00+00:00\",\"providerId\":\"alpaca\",\"action\":\"save\",\"actor\":\"recovered\"}",
            publicationId: await LastPublicationIdAsync());
        await File.WriteAllTextAsync(_pendingPath, intent + "\n");

        var replayed = await new FileProviderCredentialStore(_root).ReplayPendingAuditAsync();

        replayed.Should().Be(1);
        var audit = await File.ReadAllLinesAsync(_auditPath);
        audit.Should().HaveCount(auditBefore.Length + 1);
        audit[^1].Should().Contain("\"actor\":\"recovered\"").And.Contain("replayedAt");
    }

    [Fact]
    public async Task Replay_DeleteIntent_IsProvenOnlyByItsOwnPublication_NeverByPreexistingAbsence()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(SaveRequest("alpaca", "k", "s"));
        var record = "{\"timestamp\":\"2026-10-20T00:00:00+00:00\",\"providerId\":\"alpaca\",\"action\":\"delete\",\"actor\":\"x\"}";

        // polygon never existed: its absence pre-dates the intent, so an intent whose publication never
        // landed must be discarded even though the "key absent" state happens to hold.
        var polygonRecord = record.Replace("alpaca", "polygon");
        await File.WriteAllTextAsync(_pendingPath,
            JournalLine("intent", "provider-delete", "polygon", null, polygonRecord, Guid.NewGuid().ToString("N")) + "\n");
        (await new FileProviderCredentialStore(_root).ReplayPendingAuditAsync()).Should().Be(0);

        // A delete that did publish stamps the vault with its own id; an intent carrying that id is proven.
        await store.DeleteAsync("alpaca", "operator");
        var auditBefore = await File.ReadAllLinesAsync(_auditPath);
        await File.WriteAllTextAsync(_pendingPath,
            JournalLine("intent", "provider-delete", "alpaca", null, record, await LastPublicationIdAsync()) + "\n");
        (await new FileProviderCredentialStore(_root).ReplayPendingAuditAsync()).Should().Be(1);
        (await File.ReadAllLinesAsync(_auditPath)).Should().HaveCount(auditBefore.Length + 1);
    }

    [Fact]
    public async Task Replay_IntentWithoutPublicationId_IsNeverTakenAsProof()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(SaveRequest("alpaca", "k", "s"));
        var generation = (await store.GetStatusAsync("alpaca")).AuditMetadata["credentialGeneration"];
        var record = "{\"timestamp\":\"2026-10-20T00:00:00+00:00\",\"providerId\":\"alpaca\",\"action\":\"save\",\"actor\":\"x\"}";
        await File.WriteAllTextAsync(_pendingPath, JournalLine("intent", "provider-save", "alpaca", generation, record, publicationId: null) + "\n");

        (await new FileProviderCredentialStore(_root).ReplayPendingAuditAsync()).Should().Be(0,
            "matching state alone is not proof; the intent must name the publication it caused");
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

    [Fact]
    public async Task Replay_CommittedEntryAlreadyInAuditLog_IsNotAppendedTwice()
    {
        // Crash after the audit append but before the journal line was removed: the record is already
        // durable, carries its operation id, and must not be appended again.
        var store = new FileProviderCredentialStore(_root);
        BlockAuditLog();
        await store.SaveAsync(SaveRequest("alpaca", "k", "s"));
        var journalLine = (await File.ReadAllLinesAsync(_pendingPath)).Single();
        var record = System.Text.Json.JsonDocument.Parse(journalLine).RootElement.GetProperty("record").GetRawText();
        UnblockAuditLog();
        await File.WriteAllTextAsync(_auditPath, record + "\n");

        var replayed = await new FileProviderCredentialStore(_root).ReplayPendingAuditAsync();

        replayed.Should().Be(0);
        File.Exists(_pendingPath).Should().BeFalse();
        var audit = await File.ReadAllLinesAsync(_auditPath);
        audit.Should().ContainSingle();
        audit[0].Should().Contain("\"operationId\":");
    }

    [Fact]
    public async Task Replay_MalformedJournalLines_AreQuarantinedNotAppendedAndDoNotThrow()
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(SaveRequest("alpaca", "k", "s"));
        var auditBefore = await File.ReadAllLinesAsync(_auditPath);
        await File.WriteAllTextAsync(_pendingPath,
            "{\"phase\":\"committed\",\"providerId\":{\"x\":1},\"action\":\"save\",\"check\":\"nope\",\"record\":\"not-an-object\"}\n" +
            "{\n" +
            "[1,2,3]\n");

        var replayed = await new FileProviderCredentialStore(_root).ReplayPendingAuditAsync();

        replayed.Should().Be(0);
        File.Exists(_pendingPath).Should().BeFalse();
        (await File.ReadAllLinesAsync(_pendingPath + ".rejected")).Should().HaveCount(3);
        (await File.ReadAllLinesAsync(_auditPath)).Should().Equal(auditBefore);
    }

    [Fact]
    public async Task ReplayHostedService_NeverRefusesStartup()
    {
        var store = new FileProviderCredentialStore(_root);
        Directory.CreateDirectory(Path.GetDirectoryName(_pendingPath)!);
        Directory.CreateDirectory(_pendingPath); // the journal path itself is unreadable as a file
        var service = new Meridian.Application.Config.Credentials.ProviderCredentialAuditReplayHostedService(
            store, Microsoft.Extensions.Logging.Abstractions.NullLogger<Meridian.Application.Config.Credentials.ProviderCredentialAuditReplayHostedService>.Instance);

        var act = () => service.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Replay_OAuthSaveIntent_IsProvenOnlyByTheReplacementTokenDigest()
    {
        // A token already exists under the key. An intent for a rotation whose replacement never
        // landed must not be satisfied by that older token; the digest of the replacement is the proof.
        var store = new FileProviderCredentialStore(_root);
        var current = new OAuthToken("current-access", "Bearer", DateTimeOffset.UtcNow.AddHours(1), "current-refresh");
        await store.SaveOAuthTokenAsync("provider", current);
        var auditBefore = await File.ReadAllLinesAsync(_auditPath);
        var replacement = new OAuthToken("replacement-access", "Bearer", DateTimeOffset.UtcNow.AddHours(2), "replacement-refresh");
        var record = "{\"timestamp\":\"2026-10-20T00:00:00+00:00\",\"providerId\":\"provider\",\"action\":\"oauth-save\"}";

        var publication = await LastPublicationIdAsync();
        await File.WriteAllTextAsync(_pendingPath, OAuthJournalLine("provider", FileProviderCredentialStore.ComputeTokenDigest(replacement), record, publication) + "\n");
        (await new FileProviderCredentialStore(_root).ReplayPendingAuditAsync()).Should().Be(0, "the replacement never landed");
        (await File.ReadAllLinesAsync(_auditPath)).Should().Equal(auditBefore);

        await File.WriteAllTextAsync(_pendingPath, OAuthJournalLine("provider", FileProviderCredentialStore.ComputeTokenDigest(current), record, publication) + "\n");
        (await new FileProviderCredentialStore(_root).ReplayPendingAuditAsync()).Should().Be(1, "the vault holds exactly this token");
        (await File.ReadAllTextAsync(_auditPath)).Should().NotContain("current-access").And.NotContain("replacement-access");
        (await File.ReadAllTextAsync(_auditPath)).Should().NotContain("current-refresh");
    }

    [Fact]
    public void ComputeTokenDigest_IsStableAndContainsNoTokenMaterial()
    {
        var token = new OAuthToken("secret-access", "Bearer", new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), "secret-refresh");

        var digest = FileProviderCredentialStore.ComputeTokenDigest(token);

        digest.Should().Be(FileProviderCredentialStore.ComputeTokenDigest(token)).And.HaveLength(64).And.MatchRegex("^[0-9a-f]+$");
        digest.Should().NotContain("secret");
        FileProviderCredentialStore.ComputeTokenDigest(token with { AccessToken = "other" }).Should().NotBe(digest);
    }

    [Fact]
    public async Task SaveRotated_BackupWriteFailureAfterPrimaryPublished_KeepsTheCommittedAuditOwed()
    {
        // WriteVaultAsync publishes the primary, then the recovery generation. When the backup write
        // fails the mutation is already live, so the journal must keep a committed record, not drop it.
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(SaveRequest("alpaca", "k1", "s1"));
        var backupPath = Path.Combine(_root, ".mdc", "provider-credentials.vault.bak");
        var auditBefore = await File.ReadAllLinesAsync(_auditPath);
        File.Delete(backupPath);
        Directory.CreateDirectory(backupPath);

        var act = () => store.SaveRotatedCredentialsAsync(SaveRequest("alpaca", "k2", "rotated-secret"));

        var failure = await Record.ExceptionAsync(act);
        failure.Should().NotBeNull("the recovery-generation write fails");
        (await store.ReadForProviderAsync("alpaca"))!.Get("SecretKey").Should().Be("rotated-secret", "the primary was published before the failure");
        var journal = await File.ReadAllLinesAsync(_pendingPath);
        journal.Should().ContainSingle();
        System.Text.Json.JsonDocument.Parse(journal[0]).RootElement.GetProperty("phase").GetString().Should().Be("committed");
        (await File.ReadAllLinesAsync(_auditPath)).Should().Equal(auditBefore, "the audit append is owed, not fabricated");

        Directory.Delete(backupPath);
        (await new FileProviderCredentialStore(_root).ReplayPendingAuditAsync()).Should().Be(1);
        (await File.ReadAllLinesAsync(_auditPath)).Should().HaveCount(auditBefore.Length + 1);
    }

    [Fact]
    public async Task Replay_EarlierDeferredIntent_SurvivesALaterPublication_WhileTheAuditLogIsUnavailable()
    {
        // An intent whose publication landed but whose audit append and commit marker both failed stays
        // journaled as an intent. A later mutation while the audit log is still unavailable must not
        // replace the only proof that intent needs; once the log recovers, both records are owed.
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(SaveRequest("alpaca", "k", "s"));
        var generation = (await store.GetStatusAsync("alpaca")).AuditMetadata["credentialGeneration"];
        var auditBefore = await File.ReadAllLinesAsync(_auditPath);
        await File.WriteAllTextAsync(_pendingPath, JournalLine("intent", "provider-save", "alpaca", generation,
            "{\"timestamp\":\"2026-10-20T00:00:00+00:00\",\"providerId\":\"alpaca\",\"action\":\"save\",\"actor\":\"recovered\"}",
            await LastPublicationIdAsync()) + "\n");

        BlockAuditLog();
        await store.SaveAsync(SaveRequest("polygon", "k2", "s2"));
        UnblockAuditLog();
        await File.WriteAllLinesAsync(_auditPath, auditBefore);

        var replayed = await new FileProviderCredentialStore(_root).ReplayPendingAuditAsync();

        replayed.Should().Be(2, "the earlier proven intent and the later committed record are both owed");
        var audit = await File.ReadAllLinesAsync(_auditPath);
        audit.Should().HaveCount(auditBefore.Length + 2);
        audit.Should().ContainSingle(line => line.Contains("\"actor\":\"recovered\""));
        audit.Should().ContainSingle(line => line.Contains("\"providerId\":\"polygon\""));
        File.Exists(_pendingPath).Should().BeFalse();
    }

    [Fact]
    public async Task Replay_UnprovenIntent_GainsNothingFromALaterPublication()
    {
        // A later publication carries forward only receipts the vault already held, so an intent whose
        // own write never landed is still discarded after other mutations commit around it.
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(SaveRequest("alpaca", "k", "s"));
        var auditBefore = await File.ReadAllLinesAsync(_auditPath);
        await File.WriteAllTextAsync(_pendingPath, JournalLine("intent", "provider-save", "alpaca", "never-committed-generation",
            "{\"timestamp\":\"2026-10-20T00:00:00+00:00\",\"providerId\":\"alpaca\",\"action\":\"save\",\"actor\":\"ghost\"}",
            Guid.NewGuid().ToString("N")) + "\n");

        BlockAuditLog();
        await store.SaveAsync(SaveRequest("polygon", "k2", "s2"));
        UnblockAuditLog();
        await File.WriteAllLinesAsync(_auditPath, auditBefore);

        var replayed = await new FileProviderCredentialStore(_root).ReplayPendingAuditAsync();

        replayed.Should().Be(1, "only the later committed record is owed");
        var audit = await File.ReadAllLinesAsync(_auditPath);
        audit.Should().NotContain(line => line.Contains("\"actor\":\"ghost\""));
        File.Exists(_pendingPath).Should().BeFalse();
    }

    private static string OAuthJournalLine(string provider, string digest, string record, string publicationId)
        => $"{{\"phase\":\"intent\",\"providerId\":\"{provider}\",\"action\":\"oauth-save\",\"check\":{{\"kind\":\"oauth-save\",\"key\":\"{provider}\",\"credentialGeneration\":null,\"verifiedAt\":null,\"tokenDigest\":\"{digest}\",\"publicationId\":\"{publicationId}\"}},\"record\":{record}}}";

    /// <summary>The operation id of the last appended audit record, which is also the vault's publication marker.</summary>
    private async Task<string> LastPublicationIdAsync()
    {
        var last = (await File.ReadAllLinesAsync(_auditPath)).Last(static line => !string.IsNullOrWhiteSpace(line));
        return System.Text.Json.JsonDocument.Parse(last).RootElement.GetProperty("operationId").GetString()!;
    }

    private static string JournalLine(string phase, string kind, string key, string? generation, string record, string? publicationId)
    {
        var generationJson = generation is null ? "null" : $"\"{generation}\"";
        var publicationJson = publicationId is null ? "null" : $"\"{publicationId}\"";
        return $"{{\"phase\":\"{phase}\",\"providerId\":\"{key}\",\"action\":\"{kind}\",\"check\":{{\"kind\":\"{kind}\",\"key\":\"{key}\",\"credentialGeneration\":{generationJson},\"verifiedAt\":null,\"publicationId\":{publicationJson}}},\"record\":{record}}}";
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
