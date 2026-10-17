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
    public void Upsert_OlderTermsWithNewerAliases_MergesTheAliasesIntoTheInstalledTerms()
    {
        // A remote alias correction reread at version 1 lands after a local amendment, built from
        // the pre-correction aliases, installed version 2.
        var cache = new SecurityMasterProjectionCache();
        cache.Upsert(Record(version: 2, aliasValue: "SUPERSEDED", stamp: 1));

        cache.Upsert(Record(version: 1, aliasValue: "CORRECTED", stamp: 2)).Should().BeTrue();

        var installed = cache.Get(SecurityId)!;
        installed.Version.Should().Be(2, "the newer terms are kept");
        installed.Aliases.Should().ContainSingle().Which.AliasValue.Should().Be("CORRECTED");
        cache.Upsert(Record(version: 1, aliasValue: "OLDER", stamp: 1)).Should().BeFalse(
            "older terms with older aliases change nothing");
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

    [Fact]
    public async Task ReplaceAllAsync_KeepsAnUpsertAcceptedWhileTheReplacementWasBeingRead()
    {
        var cache = new SecurityMasterProjectionCache();
        var createdDuringRead = SecurityMasterProjectionChangeNotificationTests.CreateProjection(Guid.NewGuid(), "Created", 1);

        await cache.ReplaceAllAsync(_ =>
        {
            // The warm's durable read has completed without the new security; the local create
            // commits and upserts before the swap.
            IReadOnlyList<SecurityProjectionRecord> readBeforeTheCreate = [Record(version: 1, aliasValue: "A", stamp: 1)];
            cache.Upsert(createdDuringRead);
            return Task.FromResult(readBeforeTheCreate);
        });

        cache.Get(createdDuringRead.SecurityId).Should().NotBeNull();
        cache.Get(SecurityId).Should().NotBeNull();
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
