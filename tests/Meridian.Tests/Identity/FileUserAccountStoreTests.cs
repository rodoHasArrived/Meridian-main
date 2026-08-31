using FluentAssertions;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Storage;
using Meridian.Testing;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Meridian.Tests.Identity;

/// <summary>
/// Operator scenario: the governed user-account file is the authentication source of record. A
/// corrupt file must fail safe (no accounts) yet surface a data-integrity signal through the
/// injected logger, so a truncated or hand-edited governance file is not silently indistinguishable
/// from "no accounts exist". These tests also cover the create/audit round-trip.
/// </summary>
public sealed class FileUserAccountStoreTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [Fact]
    public void LoadAccounts_WithCorruptFile_ReturnsEmptyAndLogsDataIntegrityError()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(LoadAccounts_WithCorruptFile_ReturnsEmptyAndLogsDataIntegrityError));
        WriteGovernanceFile(artifacts.RootPath, "user-accounts.json", "{ not valid json ]");
        var logger = new CapturingLogger<FileUserAccountStore>();
        var store = new FileUserAccountStore(new StorageOptions { RootPath = artifacts.RootPath }, logger);

        var accounts = store.LoadAccounts();

        accounts.Should().BeEmpty();
        store.HasAccounts.Should().BeFalse();
        logger.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains("Corrupt user-account", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadAccounts_WithNoFile_ReturnsEmptyWithoutLoggingError()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(LoadAccounts_WithNoFile_ReturnsEmptyWithoutLoggingError));
        var logger = new CapturingLogger<FileUserAccountStore>();
        var store = new FileUserAccountStore(new StorageOptions { RootPath = artifacts.RootPath }, logger);

        store.LoadAccounts().Should().BeEmpty();

        logger.Entries.Should().NotContain(entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task UpsertAsync_PersistsAccountAndAppendsAuditEvent()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(UpsertAsync_PersistsAccountAndAppendsAuditEvent));
        var store = new FileUserAccountStore(new StorageOptions { RootPath = artifacts.RootPath });

        var result = await store.UpsertAsync(
            new UserAccountUpsertRequestDto(
                Username: "fund-admin",
                Role: nameof(UserRole.Admin),
                RoleProfileName: null,
                PermissionNames: null,
                NewPassword: "initial-pw",
                PasswordHash: null,
                IsDisabled: null,
                PasswordResetRequired: false,
                RequestedBy: "provisioner",
                Rationale: "Provision initial fund administrator."),
            actor: "provisioner");

        result.Account.Username.Should().Be("fund-admin");
        result.AuditEvent.EventType.Should().Be("user-account-created");
        result.AuditEvent.Actor.Should().Be("provisioner");
        result.AuditEvent.AuditId.Should().NotBeNullOrWhiteSpace();

        var reloaded = new FileUserAccountStore(new StorageOptions { RootPath = artifacts.RootPath });
        var loaded = reloaded.LoadAccounts();
        loaded.Should().ContainSingle().Which.Username.Should().Be("fund-admin");
        PasswordHashing.VerifyPassword("initial-pw", loaded[0].PasswordHash).Should().BeTrue();

        var audit = await reloaded.GetAuditEventsAsync();
        audit.Should().ContainSingle(item => item.AuditId == result.AuditEvent.AuditId);
    }

    [Fact]
    public async Task UpsertAsync_WithoutPasswordMaterial_Throws()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(UpsertAsync_WithoutPasswordMaterial_Throws));
        var store = new FileUserAccountStore(new StorageOptions { RootPath = artifacts.RootPath });

        var act = () => store.UpsertAsync(
            new UserAccountUpsertRequestDto(
                Username: "no-secret",
                Role: nameof(UserRole.ReadOnly),
                RoleProfileName: null,
                PermissionNames: null,
                NewPassword: null,
                PasswordHash: null,
                IsDisabled: null,
                PasswordResetRequired: false,
                RequestedBy: "provisioner",
                Rationale: "Missing password material."),
            actor: "provisioner");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("intent-only")]
    [InlineData("audit-written")]
    [InlineData("state-written")]
    public async Task CommittedMutationIntent_RestartRecoversEveryAuditAndStateBoundary(string boundary)
    {
        using var artifacts = TestArtifactDirectory.Create(
            $"{nameof(CommittedMutationIntent_RestartRecoversEveryAuditAndStateBoundary)}-{boundary}");
        var store = new FileUserAccountStore(new StorageOptions { RootPath = artifacts.RootPath });
        var created = await store.UpsertAsync(
            new UserAccountUpsertRequestDto(
                Username: "recovery-user",
                Role: nameof(UserRole.Accounting),
                RoleProfileName: null,
                PermissionNames: null,
                NewPassword: "initial-pw",
                PasswordHash: null,
                IsDisabled: false,
                PasswordResetRequired: false,
                RequestedBy: "identity-admin",
                Rationale: "Seed recovery boundary."),
            actor: "identity-admin");
        var pending = WritePendingDisableIntent(artifacts.RootPath, created.Account);

        if (boundary is "audit-written" or "state-written")
        {
            File.AppendAllText(pending.AuditPath, pending.AuditLine + Environment.NewLine);
        }

        if (boundary == "state-written")
        {
            File.WriteAllText(pending.AccountPath, pending.TargetSnapshotJson);
        }

        var restarted = new FileUserAccountStore(new StorageOptions { RootPath = artifacts.RootPath });

        var accounts = await restarted.GetAccountsAsync();
        var account = accounts.Should().ContainSingle().Which;
        account.IsDisabled.Should().BeTrue();
        account.LastAuditId.Should().Be(pending.AuditId);
        var audits = await restarted.GetAuditEventsAsync(limit: 100);
        audits.Count(item => item.AuditId == pending.AuditId).Should().Be(1);
        File.Exists(pending.IntentPath).Should().BeFalse();
    }

    [Fact]
    public void CorruptCommittedMutationIntent_BlocksIdentityStoreStartup()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(CorruptCommittedMutationIntent_BlocksIdentityStoreStartup));
        var governance = Path.Combine(artifacts.RootPath, "governance");
        Directory.CreateDirectory(governance);
        File.WriteAllText(Path.Combine(governance, "user-account-mutation-intent.json"), "{ not-json ]");

        var act = () => new FileUserAccountStore(new StorageOptions { RootPath = artifacts.RootPath });

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*mutation intent*corrupt*");
    }

    private static PendingMutationFixture WritePendingDisableIntent(
        string rootPath,
        UserAccountDto account)
    {
        var governance = Path.Combine(rootPath, "governance");
        var accountPath = Path.Combine(governance, "user-accounts.json");
        var auditPath = Path.Combine(governance, "user-account-audit.jsonl");
        var intentPath = Path.Combine(governance, "user-account-mutation-intent.json");
        var snapshot = JsonNode.Parse(File.ReadAllText(accountPath))!.AsObject();
        var persistedAccount = snapshot["accounts"]!.AsArray().Single()!.AsObject();
        var now = DateTimeOffset.UtcNow;
        var auditId = "audit-recovery-" + Guid.NewGuid().ToString("N");
        persistedAccount["isDisabled"] = true;
        persistedAccount["updatedAtUtc"] = now;
        persistedAccount["updatedBy"] = "identity-reviewer";
        persistedAccount["disabledAtUtc"] = now;
        persistedAccount["disabledBy"] = "identity-reviewer";
        persistedAccount["lastAuditId"] = auditId;

        var audit = new UserAccountAuditEventDto(
            AuditId: auditId,
            EventType: "user-account-disabled",
            OccurredAtUtc: now,
            Actor: "identity-reviewer",
            Username: account.Username,
            Rationale: "Recover a committed disable operation.",
            CorrelationId: "identity-recovery-test",
            Role: account.Role,
            PermissionNames: account.PermissionNames,
            PermissionMask: account.PermissionMask,
            IsDisabled: true,
            PasswordResetRequired: account.PasswordResetRequired,
            CompanyId: account.CompanyId);
        var intent = new JsonObject
        {
            ["version"] = 1,
            ["mutationId"] = auditId,
            ["createdAtUtc"] = now,
            ["snapshot"] = snapshot.DeepClone(),
            ["auditEvent"] = JsonSerializer.SerializeToNode(audit, WebJson)
        };
        var targetSnapshotJson = snapshot.ToJsonString(WebJson);
        var auditLine = JsonSerializer.Serialize(
            audit,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        File.WriteAllText(intentPath, intent.ToJsonString(WebJson));
        return new PendingMutationFixture(
            auditId,
            intentPath,
            accountPath,
            auditPath,
            targetSnapshotJson,
            auditLine);
    }

    private sealed record PendingMutationFixture(
        string AuditId,
        string IntentPath,
        string AccountPath,
        string AuditPath,
        string TargetSnapshotJson,
        string AuditLine);

    private static void WriteGovernanceFile(string rootPath, string fileName, string content)
    {
        var directory = Path.Combine(rootPath, "governance");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), content);
    }
}
