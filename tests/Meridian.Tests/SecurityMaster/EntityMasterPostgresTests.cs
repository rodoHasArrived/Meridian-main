using FluentAssertions;
using Meridian.Contracts.EntityMaster;
using Meridian.Storage.EntityMaster;
using Meridian.TestSupport;

namespace Meridian.Tests.SecurityMaster;

/// <summary>ADR-033 entity master: LEI/CIK identity, valid-dated hierarchy and issuer links.</summary>
[Trait("Category", "Integration")]
public sealed class EntityMasterPostgresTests
{
    [SecurityMasterDatabaseFact]
    public async Task Hierarchy_And_IssuerLinks_AreValidDated()
    {
        await using var server = await PostgresTestServer.CreateAsync("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING");
        IEntityMasterStore store = new PostgresEntityMasterStore(new EntityMasterOptions
        {
            ConnectionString = server.ConnectionString,
            Schema = server.CreateSchemaName("em")
        });
        await AssertHierarchyAndIssuerAsync(store);
    }

    [Fact]
    public async Task InMemoryStore_HasTheSameSemantics()
        => await AssertHierarchyAndIssuerAsync(new InMemoryEntityMasterStore());

    [Fact]
    public void Rules_NormalizeAndValidateIdentifiers()
    {
        EntityMasterRules.NormalizeCik("320193").Should().Be("0000320193");
        EntityMasterRules.NormalizeLei(" hwupkr0mpou8fgxbt394 ").Should().Be("HWUPKR0MPOU8FGXBT394");
        FluentActions.Invoking(() => EntityMasterRules.NormalizeLei("short")).Should().Throw<ArgumentException>();
    }

    private static async Task AssertHierarchyAndIssuerAsync(IEntityMasterStore store)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var group = await store.CreateEntityAsync(new CreateLegalEntityRequest("Group Holdings", $"GRP{suffix}000000000", null, "US"), "ops");
        var oldParent = await store.CreateEntityAsync(new CreateLegalEntityRequest("Old Parent", null, null, "US"), "ops");
        var issuer = await store.CreateEntityAsync(new CreateLegalEntityRequest("Issuer Inc", null, $"{Random.Shared.Next(1, 999999)}", "US"), "ops");

        await store.SetParentAsync(issuer.EntityId, oldParent.EntityId, new DateOnly(2020, 1, 1), "ops");
        await store.SetParentAsync(issuer.EntityId, group.EntityId, new DateOnly(2024, 1, 1), "ops");

        (await store.GetParentAsync(issuer.EntityId, new DateOnly(2021, 6, 1)))!.ParentEntityId.Should().Be(oldParent.EntityId);
        (await store.GetUltimateParentAsync(issuer.EntityId, new DateOnly(2025, 6, 1)))!.EntityId.Should().Be(group.EntityId);
        (await store.FindByLeiAsync(group.Lei!))!.EntityId.Should().Be(group.EntityId);

        var duplicateLei = () => store.CreateEntityAsync(new CreateLegalEntityRequest("Dup", group.Lei, null, null), "ops");
        await duplicateLei.Should().ThrowAsync<InvalidOperationException>();

        var securityId = Guid.NewGuid();
        await store.LinkIssuerAsync(securityId, issuer.EntityId, new DateOnly(2022, 1, 1), "ops");
        (await store.GetIssuerAsync(securityId, new DateOnly(2023, 1, 1)))!.IssuerEntityId.Should().Be(issuer.EntityId);
        (await store.GetIssuerAsync(securityId, new DateOnly(2021, 1, 1))).Should().BeNull();
    }
}
