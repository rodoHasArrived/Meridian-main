using FluentAssertions;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Storage;
using Meridian.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Meridian.Tests.Identity;

/// <summary>
/// Operator scenario: every file-backed Identity governance store must fail safe on a corrupt file
/// while surfacing the data-integrity problem through its injected logger, rather than silently
/// masking corruption as an empty (unconfigured) surface.
/// </summary>
public sealed class GovernanceStoreDataIntegrityTests
{
    [Fact]
    public async Task RolePermissionProfileStore_WithCorruptFile_LogsDataIntegrityError()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(RolePermissionProfileStore_WithCorruptFile_LogsDataIntegrityError));
        WriteGovernanceFile(artifacts.RootPath, "role-permission-profiles.json", "not-json");
        var logger = new CapturingLogger<FileRolePermissionProfileStore>();
        var store = new FileRolePermissionProfileStore(new StorageOptions { RootPath = artifacts.RootPath }, logger);

        var catalog = await store.GetCatalogAsync();

        // Built-in roles still resolve; only custom profiles are suppressed.
        catalog.Roles.Should().OnlyContain(role => role.IsBuiltIn);
        logger.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains("Corrupt role-permission-profile", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScopedAccessAssignmentStore_WithCorruptFile_LogsDataIntegrityError()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(ScopedAccessAssignmentStore_WithCorruptFile_LogsDataIntegrityError));
        var path = Path.Combine(artifacts.RootPath, "user-access-assignments.json");
        File.WriteAllText(path, "{ broken json");
        var logger = new CapturingLogger<FileScopedAccessAssignmentStore>();
        var store = new FileScopedAccessAssignmentStore(path, logger);

        var assignments = await store.ListAsync();

        assignments.Should().BeEmpty();
        logger.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains("Corrupt scoped-access-assignment", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScopedAccessAssignmentStore_CreateWithCorruptFile_RefusesAndLeavesFileUntouched()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(ScopedAccessAssignmentStore_CreateWithCorruptFile_RefusesAndLeavesFileUntouched));
        var path = Path.Combine(artifacts.RootPath, "user-access-assignments.json");
        const string corrupt = "{ \"assignments\": [ { broken";
        File.WriteAllText(path, corrupt);
        var store = new FileScopedAccessAssignmentStore(path, new CapturingLogger<FileScopedAccessAssignmentStore>());
        var now = DateTimeOffset.UtcNow;

        var create = () => store.CreateAsync(new UserAccessAssignmentDto(
            Guid.NewGuid(), "ops-user", AccessPrincipalKindDto.User, AccessScopeKindDto.Global, null,
            "Operator", null, [nameof(UserPermission.ViewTrades)], (long)UserPermission.ViewTrades,
            now, null, "ops-admin", "Corrupt-file regression", "corrupt-file-test", 1, now, now));

        await create.Should().ThrowAsync<InvalidOperationException>().WithMessage("*corrupt*");
        File.ReadAllText(path).Should().Be(corrupt,
            "a write must not replace every stored assignment with the one being created");
    }

    [Fact]
    public async Task RolePermissionProfileStore_UpsertWithCorruptFile_RefusesAndLeavesFileUntouched()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(RolePermissionProfileStore_UpsertWithCorruptFile_RefusesAndLeavesFileUntouched));
        WriteGovernanceFile(artifacts.RootPath, "role-permission-profiles.json", "not-json");
        var path = Path.Combine(artifacts.RootPath, "governance", "role-permission-profiles.json");
        var store = new FileRolePermissionProfileStore(
            new StorageOptions { RootPath = artifacts.RootPath },
            new CapturingLogger<FileRolePermissionProfileStore>());

        var upsert = () => store.UpsertAsync(
            new RolePermissionProfileUpsertRequestDto(
                ProfileName: "Close Reviewer",
                DisplayName: "Close Reviewer",
                Description: "Corrupt-file regression.",
                BaseRole: nameof(UserRole.Accounting),
                PermissionNames: [nameof(UserPermission.ViewTrades)],
                RequestedBy: "ops-admin",
                Rationale: "Corrupt-file regression."),
            actor: "ops-admin");

        await upsert.Should().ThrowAsync<InvalidOperationException>().WithMessage("*corrupt*");
        File.ReadAllText(path).Should().Be("not-json");
    }

    private static void WriteGovernanceFile(string rootPath, string fileName, string content)
    {
        var directory = Path.Combine(rootPath, "governance");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), content);
    }
}
