using System.Security.Cryptography;
using FluentAssertions;
using Meridian.Contracts.Integrations;
using Meridian.Storage.Integrations;

namespace Meridian.Tests.Storage.Integrations;

public sealed partial class FileProviderIntegrationManifestStoreTests
{
    [Fact]
    public async Task ScanStoredActorValues_ReadsActorFieldsAcrossCategories_WithoutTouchingRecords()
    {
        var root = new FileProviderIntegrationManifestStore(_testRoot);
        var tenant = (FileProviderIntegrationManifestStore)((IProviderIntegrationTenantManifestStoreFactory)root).ForTenant("tenant-a");
        await tenant.SaveManifestAsync(CreateManifest() with { ApprovedBy = "Meridian Operator" });
        await tenant.SaveConnectionAsync(CreateConnection() with { OwnerUserId = "Momentum Alpha Desk" });
        var before = HashFiles();

        var values = await tenant.ScanStoredActorValuesAsync();

        // The current-manifest file is only a pointer; the actor lives in the retained version's envelope.
        values.Should().Contain(value => value.Field == "approvedBy" && value.Value == "Meridian Operator"
            && value.FieldPath == "manifest.approvedBy" && value.SubjectId == "manifest-custodian-abc-v1"
            && value.RecordKind == "manifest-versions");
        values.Should().NotContain(value => value.RecordKind == "manifests");
        values.Should().Contain(value => value.Field == "ownerUserId" && value.Value == "Momentum Alpha Desk"
            && value.RecordKind == "connections" && value.SubjectId == "connection-alpha");
        HashFiles().Should().BeEquivalentTo(before, "a scan only reads");
    }

    [Fact]
    public async Task TryAppendActorFlag_IsWriteOnce_AndNeverChangesTheFlaggedRecord()
    {
        var tenant = (FileProviderIntegrationManifestStore)((IProviderIntegrationTenantManifestStoreFactory)new FileProviderIntegrationManifestStore(_testRoot)).ForTenant("tenant-a");
        await tenant.SaveManifestAsync(CreateManifest() with { ApprovedBy = "Meridian Operator" });
        var manifestFiles = HashFiles().Where(pair => pair.Key.Contains("manifest", StringComparison.Ordinal)).ToDictionary();
        var value = (await tenant.ScanStoredActorValuesAsync()).Single(v => v.Field == "approvedBy");
        var flag = Flag(value, "ops-user", DateTimeOffset.Parse("2026-11-25T18:00:00Z"));

        (await tenant.TryAppendActorFlagAsync(flag)).Should().BeTrue();
        (await tenant.TryAppendActorFlagAsync(flag with { FlaggedBy = "someone-else", FlaggedAt = flag.FlaggedAt.AddDays(1) }))
            .Should().BeFalse("an existing flag is never replaced");

        var listed = await tenant.ListActorFlagsAsync();
        listed.Should().ContainSingle().Which.Should().BeEquivalentTo(flag, "the first scan's evidence stands");
        HashFiles().Where(pair => manifestFiles.ContainsKey(pair.Key)).ToDictionary()
            .Should().BeEquivalentTo(manifestFiles, "the flagged manifest is untouched");
        await tenant.Invoking(store => store.TryAppendActorFlagAsync(flag with { FlagId = "not-derived" }))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ScanStoredActorValues_StaysInsideTheTenantPartition()
    {
        var root = new FileProviderIntegrationManifestStore(_testRoot);
        var factory = (IProviderIntegrationTenantManifestStoreFactory)root;
        await factory.ForTenant("tenant-a").SaveManifestAsync(CreateManifest() with { ApprovedBy = "Meridian Operator" });
        var tenantB = (FileProviderIntegrationManifestStore)factory.ForTenant("tenant-b");

        (await tenantB.ScanStoredActorValuesAsync()).Should().BeEmpty("another tenant's records are not this tenant's");
        (await root.ScanStoredActorValuesAsync()).Should().BeEmpty("the root store skips tenant partitions");
    }

    [Theory]
    [InlineData("Meridian Operator", "session desk label")]
    [InlineData("meridian operator", "session desk label")]
    [InlineData("Momentum Alpha Desk", "strategy desk label")]
    [InlineData("settings-operator", "settings placeholder")]
    [InlineData("settings-screen", "settings placeholder")]
    [InlineData("  ", "blank actor")]
    [InlineData("dana.ops", null)]
    [InlineData("desk", null)]
    [InlineData("helpdesk", null)]
    [InlineData(null, null)]
    public void ActorLabelClassifier_MatchesTheAuditScript(string? value, string? expected)
        => ProviderIntegrationActorLabelClassifier.Classify(value).Should().Be(expected);

    private static ProviderIntegrationActorFlagDto Flag(ProviderIntegrationStoredActorValue value, string flaggedBy, DateTimeOffset at)
        => new(
            ProviderIntegrationActorFlagDto.ComputeFlagId(value.RecordPath, value.FieldPath, value.Value),
            value.RecordPath,
            value.RecordKind,
            value.SubjectId,
            value.FieldPath,
            value.Value,
            ProviderIntegrationActorLabelClassifier.Classify(value.Value)!,
            value.RecordedAt,
            flaggedBy,
            at);

    private Dictionary<string, string> HashFiles()
        => Directory.EnumerateFiles(_testRoot, "*.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}actor-flags{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
}
