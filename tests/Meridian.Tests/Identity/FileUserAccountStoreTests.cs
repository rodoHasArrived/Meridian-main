using FluentAssertions;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Storage;
using Meridian.Testing;
using Microsoft.Extensions.Logging;
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

    private static void WriteGovernanceFile(string rootPath, string fileName, string content)
    {
        var directory = Path.Combine(rootPath, "governance");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), content);
    }
}
