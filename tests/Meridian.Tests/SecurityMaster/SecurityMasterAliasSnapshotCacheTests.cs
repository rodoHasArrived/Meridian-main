using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The projection cache keeps an alias list read at a newer alias-revision stamp when a projection
/// writer installs a record carrying aliases it read earlier (an amendment or rebuild that raced an
/// alias correction).
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterAliasSnapshotCacheTests
{
    private static readonly Guid SecurityId = Guid.NewGuid();

    [Fact]
    public void Upsert_WithAliasesReadBeforeACorrection_KeepsTheCorrectedAliases()
    {
        var cache = new SecurityMasterProjectionCache();
        cache.Upsert(Record(version: 1, aliasValue: "CORRECTED", stamp: 2));

        cache.Upsert(Record(version: 2, aliasValue: "ORIGINAL", stamp: 1));

        var installed = cache.Get(SecurityId)!;
        installed.Version.Should().Be(2, "the newer terms still install");
        installed.Aliases.Should().ContainSingle().Which.AliasValue.Should().Be("CORRECTED");
    }

    [Fact]
    public void Upsert_WithNewerOrUnstampedAliases_TakesTheIncomingAliases()
    {
        var cache = new SecurityMasterProjectionCache();
        cache.Upsert(Record(version: 1, aliasValue: "OLD", stamp: 1));

        cache.Upsert(Record(version: 1, aliasValue: "NEWER", stamp: 2));
        cache.Get(SecurityId)!.Aliases.Should().ContainSingle().Which.AliasValue.Should().Be("NEWER");

        cache.Upsert(Record(version: 1, aliasValue: "EXPLICIT", stamp: null));
        cache.Get(SecurityId)!.Aliases.Should().ContainSingle().Which.AliasValue.Should().Be("EXPLICIT");
    }

    [Fact]
    public void ReplaceAll_WithAliasesReadBeforeACorrection_KeepsTheCorrectedAliases()
    {
        var cache = new SecurityMasterProjectionCache();
        cache.Upsert(Record(version: 1, aliasValue: "CORRECTED", stamp: 2));

        cache.ReplaceAll([Record(version: 1, aliasValue: "ORIGINAL", stamp: 1)]);

        cache.Get(SecurityId)!.Aliases.Should().ContainSingle().Which.AliasValue.Should().Be("CORRECTED");
    }

    private static SecurityProjectionRecord Record(long version, string aliasValue, long? stamp)
    {
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        IReadOnlyList<SecurityAliasDto> aliases =
        [
            new SecurityAliasDto(
                Guid.Empty, SecurityId, "Ticker", aliasValue, null, SecurityAliasScope.Operations, null,
                "tester", effectiveFrom, effectiveFrom, null, true)
        ];
        return SecurityMasterProjectionChangeNotificationTests.CreateProjection(SecurityId, "Alias", version) with
        {
            Aliases = stamp is { } revisionStamp ? new SecurityAliasSnapshot(aliases, revisionStamp) : aliases
        };
    }
}
