using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Verifies the security-master-backed timeline resolver: the current ticker of a renamed
/// security resolves to the era ticker for pre-rename dates, stays unchanged for post-rename
/// dates, and unknown symbols pass through untouched.
/// </summary>
public sealed class SecurityMasterHistoricalSymbolTimelineResolverTests
{
    private static readonly DateTimeOffset RenameAt = new(2022, 6, 9, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ListedAt = new(2012, 5, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ResolveTickerForDate_PreRenameDate_ReturnsEraTicker()
    {
        var securityId = Guid.NewGuid();
        var resolver = CreateResolver(securityId);

        var ticker = await resolver.ResolveTickerForDateAsync("META", new DateOnly(2022, 1, 3));

        ticker.Should().Be("FB");
    }

    [Fact]
    public async Task ResolveTickerForDate_PostRenameDate_ReturnsRequestedSymbol()
    {
        var securityId = Guid.NewGuid();
        var resolver = CreateResolver(securityId);

        var ticker = await resolver.ResolveTickerForDateAsync("META", new DateOnly(2023, 1, 3));

        ticker.Should().Be("META");
    }

    [Fact]
    public async Task ResolveTickerForDate_RetiredTickerOnPreRenameDate_ResolvesDirectly()
    {
        var securityId = Guid.NewGuid();
        var resolver = CreateResolver(securityId);

        var ticker = await resolver.ResolveTickerForDateAsync("FB", new DateOnly(2022, 1, 3));

        ticker.Should().Be("FB");
    }

    [Fact]
    public async Task ResolveTickerForDate_UnknownSymbol_PassesThrough()
    {
        var securityResolver = Substitute.For<ISecurityResolver>();
        securityResolver.ResolveAsync(Arg.Any<ResolveSecurityRequest>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
        var resolver = new SecurityMasterHistoricalSymbolTimelineResolver(
            securityResolver,
            Substitute.For<ISecurityMasterStore>(),
            NullLogger<SecurityMasterHistoricalSymbolTimelineResolver>.Instance);

        var ticker = await resolver.ResolveTickerForDateAsync("ZZZQ", new DateOnly(2022, 1, 3));

        ticker.Should().Be("ZZZQ");
    }

    private static SecurityMasterHistoricalSymbolTimelineResolver CreateResolver(Guid securityId)
    {
        // Identifier timeline: FB valid [listed, rename), META valid [rename, open).
        var securityResolver = Substitute.For<ISecurityResolver>();
        securityResolver.ResolveAsync(Arg.Any<ResolveSecurityRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ResolveSecurityRequest>();
                var asOf = request.AsOfUtc ?? DateTimeOffset.UtcNow;
                var valid = request.IdentifierValue.ToUpperInvariant() switch
                {
                    "FB" => asOf >= ListedAt && asOf < RenameAt,
                    "META" => asOf >= RenameAt,
                    _ => false
                };
                return Task.FromResult<Guid?>(valid ? securityId : null);
            });

        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityProjectionRecord(
                SecurityId: securityId,
                AssetClass: "Equity",
                Status: SecurityStatusDto.Active,
                DisplayName: "Meta Platforms",
                Currency: "USD",
                PrimaryIdentifierKind: "Ticker",
                PrimaryIdentifierValue: "META",
                CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Meta Platforms", currency = "USD" }),
                AssetSpecificTerms: JsonSerializer.SerializeToElement(new { schemaVersion = 1 }),
                Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = "test", updatedBy = "codex" }),
                Version: 9,
                EffectiveFrom: ListedAt,
                EffectiveTo: null,
                Identifiers: new[]
                {
                    new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "FB", false, ListedAt, RenameAt, null),
                    new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "META", true, RenameAt, null, null)
                },
                Aliases: Array.Empty<SecurityAliasDto>()));

        return new SecurityMasterHistoricalSymbolTimelineResolver(
            securityResolver,
            store,
            NullLogger<SecurityMasterHistoricalSymbolTimelineResolver>.Instance);
    }
}
