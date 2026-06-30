using FluentAssertions;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

/// <summary>
/// The fund-profile tenancy registry is the authoritative fund→tenant ownership source: first owner wins,
/// fund ids are keyed case-insensitively, and ownership survives a process restart (file persistence).
/// </summary>
public sealed class FileFundProfileTenancyRegistryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"fund-tenancy-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task Bind_ClaimsUnboundFund_ForCaller()
    {
        var registry = new FileFundProfileTenancyRegistry(_path);

        var owner = await registry.BindAsync("fund-x", "tenant-acme", "company-acme");

        owner.TenantId.Should().Be("tenant-acme");
        owner.IsHeldBy("tenant-acme").Should().BeTrue();
    }

    [Fact]
    public async Task Bind_IsFirstOwnerWins_SecondTenantGetsExistingOwner()
    {
        var registry = new FileFundProfileTenancyRegistry(_path);

        await registry.BindAsync("fund-x", "tenant-acme");
        var second = await registry.BindAsync("fund-x", "tenant-other");

        second.TenantId.Should().Be("tenant-acme", "the first owner wins");
        second.IsHeldBy("tenant-other").Should().BeFalse();
    }

    [Fact]
    public async Task Bind_RebindBySameTenant_IsIdempotent()
    {
        var registry = new FileFundProfileTenancyRegistry(_path);

        await registry.BindAsync("fund-x", "tenant-acme");
        var again = await registry.BindAsync("fund-x", "tenant-acme");

        again.IsHeldBy("tenant-acme").Should().BeTrue();
    }

    [Fact]
    public async Task FundKeying_IsCaseInsensitive()
    {
        var registry = new FileFundProfileTenancyRegistry(_path);
        await registry.BindAsync("Fund-X", "tenant-acme");

        var owner = await registry.ResolveAsync("fund-x");

        owner.Should().NotBeNull();
        owner!.IsHeldBy("tenant-acme").Should().BeTrue();
        (await registry.IsAccessibleAsync("FUND-X", "tenant-other")).Should().BeFalse("a foreign tenant cannot use the fund regardless of casing");
    }

    [Fact]
    public async Task Resolve_ReturnsNull_ForUnboundFund()
        => (await new FileFundProfileTenancyRegistry(_path).ResolveAsync("never-bound")).Should().BeNull();

    [Fact]
    public async Task IsAccessible_AllowsUnboundAndOwner_DeniesForeign()
    {
        var registry = new FileFundProfileTenancyRegistry(_path);

        (await registry.IsAccessibleAsync("unbound", "tenant-acme")).Should().BeTrue();
        await registry.BindAsync("fund-x", "tenant-acme");
        (await registry.IsAccessibleAsync("fund-x", "tenant-acme")).Should().BeTrue();
        (await registry.IsAccessibleAsync("fund-x", "tenant-other")).Should().BeFalse();
    }

    [Fact]
    public async Task Bindings_PersistAcrossInstances()
    {
        await new FileFundProfileTenancyRegistry(_path).BindAsync("fund-x", "tenant-acme");

        var reopened = new FileFundProfileTenancyRegistry(_path);

        (await reopened.ResolveAsync("fund-x"))!.IsHeldBy("tenant-acme").Should().BeTrue();
    }

    [Fact]
    public async Task Bind_BlankFund_Throws()
    {
        var registry = new FileFundProfileTenancyRegistry(_path);

        await registry.Invoking(r => r.BindAsync("   ", "tenant-acme"))
            .Should().ThrowAsync<ArgumentException>();
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
