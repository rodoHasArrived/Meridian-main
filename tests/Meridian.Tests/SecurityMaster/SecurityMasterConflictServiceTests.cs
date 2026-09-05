using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Unit tests for <see cref="SecurityMasterConflictService"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterConflictServiceTests
{
    [Theory]
    [InlineData(".", ".")]
    [InlineData("\r\n", " ")]
    [InlineData("\0\t\u001b\u007f\u0085", " ")]
    [InlineData("\u2028\u2029", " ")]
    public async Task ResolveAsync_UntrustedResolver_RendersOneLogLineAndPreservesAuditIdentity(
        string separator, string renderedSeparator)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            MakeProjection(Guid.NewGuid(), "Cusip", "037833100", provider: "provA"),
            MakeProjection(Guid.NewGuid(), "Cusip", "037833100", provider: "provB")
        });
        var logger = new RecordingLogger<SecurityMasterConflictService>();
        var service = new SecurityMasterConflictService(store, logger);
        var conflict = (await service.GetOpenConflictsAsync(timeout.Token)).Single();
        var resolver = $"operator@meridian.test{separator}approval=forged";

        var updated = await service.ResolveAsync(
            new ResolveConflictRequest(conflict.ConflictId, "AcceptA", resolver), timeout.Token);

        updated.Should().NotBeNull();
        updated!.Status.Should().Be("Resolved");
        updated.ResolvedBy.Should().Be(resolver);
        (await service.GetConflictAsync(conflict.ConflictId, timeout.Token))!.ResolvedBy.Should().Be(resolver);
        logger.Entries.Last().Message.Should().Be(
            $"Conflict {conflict.ConflictId} for security {conflict.SecurityId} Resolved by operator@meridian.test{renderedSeparator}approval=forged");
    }

    private static SecurityProjectionRecord MakeProjection(
        Guid securityId,
        string identifierKind,
        string identifierValue,
        string? provider = null)
    {
        var identifier = new SecurityIdentifierDto(
            Enum.Parse<SecurityIdentifierKind>(identifierKind, ignoreCase: true),
            identifierValue,
            IsPrimary: true,
            ValidFrom: DateTimeOffset.UtcNow.AddDays(-30),
            Provider: provider);

        return new SecurityProjectionRecord(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: $"Test Security {securityId:N}",
            Currency: "USD",
            PrimaryIdentifierKind: identifierKind,
            PrimaryIdentifierValue: identifierValue,
            CommonTerms: JsonSerializer.SerializeToElement(new { currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = provider ?? "default" }),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-30),
            EffectiveTo: null,
            Identifiers: new[] { identifier },
            Aliases: Array.Empty<SecurityAliasDto>());
    }

    [Fact]
    public async Task GetOpenConflictsAsync_WhenNoSecurities_ReturnsEmpty()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityProjectionRecord>());

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var conflicts = await service.GetOpenConflictsAsync(CancellationToken.None);

        conflicts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOpenConflictsAsync_WhenSameIdentifierFromTwoProviders_ReturnsConflict()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();

        // Both securities claim the same ISIN from different providers
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                MakeProjection(securityA, "Isin", "US0378331005", provider: "alpaca"),
                MakeProjection(securityB, "Isin", "US0378331005", provider: "polygon")
            });

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var conflicts = await service.GetOpenConflictsAsync(CancellationToken.None);

        conflicts.Should().HaveCount(1);
        var conflict = conflicts[0];
        conflict.ConflictKind.Should().Be("IdentifierAmbiguity");
        conflict.FieldPath.Should().Contain("Isin");
        conflict.Status.Should().Be("Open");
        conflict.ProviderA.Should().NotBeNullOrWhiteSpace();
        conflict.ProviderB.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetOpenConflictsAsync_IssuerScopedIdentifiers_DoNotPairDistinctSecurities()
    {
        // CIK names an EDGAR filer and LEI names an ISO 17442 legal entity, so distinct tradable
        // securities of one issuer legitimately share them with overlapping validity — sharing an
        // issuer-scoped identifier is not an identity ambiguity.
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                MakeProjection(Guid.NewGuid(), "Cik", "0000320193", provider: "edgar"),
                MakeProjection(Guid.NewGuid(), "Cik", "0000320193", provider: "edgar"),
                MakeProjection(Guid.NewGuid(), "Lei", "HWUPKR0MPOU8FGXBT394", provider: "gleif"),
                MakeProjection(Guid.NewGuid(), "Lei", "HWUPKR0MPOU8FGXBT394", provider: "gleif")
            });

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var conflicts = await service.GetOpenConflictsAsync(CancellationToken.None);

        conflicts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOpenConflictsAsync_NormalizesPunctuationBeforeComparing()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            MakeProjection(Guid.NewGuid(), "Isin", "US-0378331005", "provider-a"),
            MakeProjection(Guid.NewGuid(), "Isin", "us 0378331005", "provider-b")
        });
        var service = new SecurityMasterConflictService(store, NullLogger<SecurityMasterConflictService>.Instance);

        var conflicts = await service.GetOpenConflictsAsync(CancellationToken.None);

        conflicts.Should().ContainSingle();
    }

    [Fact]
    public async Task GetOpenConflictsAsync_NonOverlappingValidityWindows_DoNotConflict()
    {
        var boundary = DateTimeOffset.UtcNow.AddDays(-10);
        var expired = MakeProjection(Guid.NewGuid(), "Ticker", "RECYCLED", "XNAS");
        expired = expired with
        {
            Identifiers = [expired.Identifiers[0] with { ValidFrom = boundary.AddYears(-1), ValidTo = boundary }]
        };
        var current = MakeProjection(Guid.NewGuid(), "Ticker", "RECYCLED", "XNAS");
        current = current with
        {
            Identifiers = [current.Identifiers[0] with { ValidFrom = boundary, ValidTo = null }]
        };
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { expired, current });
        var service = new SecurityMasterConflictService(store, NullLogger<SecurityMasterConflictService>.Instance);

        var conflicts = await service.GetOpenConflictsAsync(CancellationToken.None);

        conflicts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOpenConflictsAsync_WhenAClaimExpires_SupersedesPreviouslyOpenConflict()
    {
        var boundary = DateTimeOffset.UtcNow;
        var first = MakeProjection(Guid.NewGuid(), "Ticker", "REUSED", "XNAS");
        var second = MakeProjection(Guid.NewGuid(), "Ticker", "REUSED", "XNAS");
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(
            new[] { first, second },
            new[]
            {
                first with
                {
                    Identifiers =
                    [
                        first.Identifiers[0] with
                        {
                            ValidFrom = boundary.AddYears(-1),
                            ValidTo = boundary
                        }
                    ]
                },
                second with
                {
                    Identifiers =
                    [
                        second.Identifiers[0] with
                        {
                            ValidFrom = boundary,
                            ValidTo = null
                        }
                    ]
                }
            });
        var service = new SecurityMasterConflictService(store, NullLogger<SecurityMasterConflictService>.Instance);

        (await service.GetOpenConflictsAsync(CancellationToken.None)).Should().ContainSingle();
        (await service.GetOpenConflictsAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetOpenConflictsAsync_WhenSupersededClaimsOverlapAgain_ReopensConflict()
    {
        var boundary = DateTimeOffset.UtcNow;
        var first = MakeProjection(Guid.NewGuid(), "Ticker", "REOPEN", "XNAS");
        var second = MakeProjection(Guid.NewGuid(), "Ticker", "REOPEN", "XNAS");
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(
            new[] { first, second },
            new[]
            {
                first with { Identifiers = [first.Identifiers[0] with { ValidTo = boundary }] },
                second with { Identifiers = [second.Identifiers[0] with { ValidFrom = boundary }] }
            },
            new[] { first, second });
        var service = new SecurityMasterConflictService(store, NullLogger<SecurityMasterConflictService>.Instance);

        var original = (await service.GetOpenConflictsAsync(CancellationToken.None)).Single();
        (await service.GetOpenConflictsAsync(CancellationToken.None)).Should().BeEmpty();
        var reopened = (await service.GetOpenConflictsAsync(CancellationToken.None)).Single();

        reopened.ConflictId.Should().Be(original.ConflictId);
        reopened.Status.Should().Be("Open");
        reopened.ResolvedReason.Should().BeNull();
    }

    [Fact]
    public async Task GetOpenConflictsAsync_ProviderSymbolsInDifferentScopes_DoNotConflict()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            MakeProjection(Guid.NewGuid(), "ProviderSymbol", "ABC", "provider-a"),
            MakeProjection(Guid.NewGuid(), "ProviderSymbol", "ABC", "provider-b")
        });
        var service = new SecurityMasterConflictService(store, NullLogger<SecurityMasterConflictService>.Instance);

        var conflicts = await service.GetOpenConflictsAsync(CancellationToken.None);

        conflicts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOpenConflictsAsync_ThreeClaimants_EmitsEveryPair()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            MakeProjection(Guid.NewGuid(), "Figi", "BBG000B9XRY4", "provider-a"),
            MakeProjection(Guid.NewGuid(), "Figi", "BBG000B9XRY4", "provider-b"),
            MakeProjection(Guid.NewGuid(), "Figi", "BBG000B9XRY4", "provider-c")
        });
        var service = new SecurityMasterConflictService(store, NullLogger<SecurityMasterConflictService>.Instance);

        var conflicts = await service.GetOpenConflictsAsync(CancellationToken.None);

        conflicts.Should().HaveCount(3, "three claimants have three distinct claimant pairs");
        conflicts.Select(conflict => conflict.ConflictId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task RecordConflictsForProjectionsAsync_UsesOneCandidateLookupWithoutUniverseLoad()
    {
        var incoming = MakeProjection(Guid.NewGuid(), "Cusip", "037833100", "provider-a");
        var existing = MakeProjection(Guid.NewGuid(), "Cusip", "037-833-100", "provider-b");
        var store = Substitute.For<ISecurityMasterStore>();
        store.FindIdentifierCandidatesAsync(
                Arg.Any<IReadOnlyList<SecurityIdentifierDto>>(),
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(new[] { existing });
        var service = new SecurityMasterConflictService(store, NullLogger<SecurityMasterConflictService>.Instance);

        await service.RecordConflictsForProjectionsAsync([incoming], CancellationToken.None);

        await store.Received(1).FindIdentifierCandidatesAsync(
            Arg.Is<IReadOnlyList<SecurityIdentifierDto>>(identifiers => identifiers.Count == 1),
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { incoming.SecurityId })),
            Arg.Any<CancellationToken>());
        await store.DidNotReceive().LoadAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordConflictsForProjections_SupersedesSubjectConflictsTheScanNoLongerDetects()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                MakeProjection(securityA, "Ticker", "STALE", provider: "XNAS"),
                MakeProjection(securityB, "Ticker", "STALE", provider: "XNAS")
            });
        store.FindIdentifierCandidatesAsync(
                Arg.Any<IReadOnlyList<SecurityIdentifierDto>>(),
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityProjectionRecord>());

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);
        var conflictId = (await service.GetOpenConflictsAsync(CancellationToken.None)).Single().ConflictId;

        // A refresh racing an amendment can retain a conflict the amended claims no longer
        // produce. The ingest scan is authoritative for every pair touching its subjects, so
        // scanning the amended projection must supersede the stale pair rather than leaving it
        // open until the next full refresh.
        await service.RecordConflictsForProjectionAsync(
            MakeProjection(securityA, "Ticker", "AMENDED", provider: "XNAS"),
            CancellationToken.None);

        var superseded = await service.GetConflictAsync(conflictId, CancellationToken.None);
        superseded.Should().NotBeNull();
        superseded!.Status.Should().Be("Superseded");
        superseded.ResolvedReason.Should().Be(SecurityMasterConflictService.IdentifierNoLongerDetectedReason);
    }

    [Fact]
    public async Task GetOpenConflictsAsync_WhenSameIdentifierSameSecurityDifferentProviders_NoConflict()
    {
        var securityId = Guid.NewGuid();
        var store = Substitute.For<ISecurityMasterStore>();

        // Same ISIN, same SecurityId from two providers — NOT a conflict
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                MakeProjection(securityId, "Isin", "US0378331005", provider: "alpaca")
            });

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var conflicts = await service.GetOpenConflictsAsync(CancellationToken.None);

        conflicts.Should().BeEmpty("same identifier on the same security is not a conflict");
    }

    [Fact]
    public async Task GetOpenConflictsAsync_ConflictIdIsStable_SameConflictDetectedTwice()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();

        var projections = new[]
        {
            MakeProjection(securityA, "Isin", "US1234567890", provider: "provA"),
            MakeProjection(securityB, "Isin", "US1234567890", provider: "provB")
        };

        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(projections);

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var first = await service.GetOpenConflictsAsync(CancellationToken.None);
        var second = await service.GetOpenConflictsAsync(CancellationToken.None);

        first.Should().HaveCount(1);
        second.Should().HaveCount(1);
        first[0].ConflictId.Should().Be(second[0].ConflictId,
            "the same conflict pair must yield the same deterministic ID");
    }

    [Fact]
    public async Task ResolveAsync_WhenConflictExists_UpdatesStatus()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();

        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                MakeProjection(securityA, "Cusip", "037833100", provider: "provA"),
                MakeProjection(securityB, "Cusip", "037833100", provider: "provB")
            });

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        // Seed the conflict
        var conflicts = await service.GetOpenConflictsAsync(CancellationToken.None);
        conflicts.Should().HaveCount(1);

        var conflictId = conflicts[0].ConflictId;
        var request = new ResolveConflictRequest(conflictId, "AcceptA", "operator@meridian.test");

        var updated = await service.ResolveAsync(request, CancellationToken.None);

        updated.Should().NotBeNull();
        updated!.Status.Should().Be("Resolved");
    }

    [Fact]
    public async Task ResolveAsync_WhenConflictDismissed_SetsStatusDismissed()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();

        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                MakeProjection(securityA, "Figi", "BBG000B9XRY4", provider: "openfigi"),
                MakeProjection(securityB, "Figi", "BBG000B9XRY4", provider: "polygon")
            });

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var conflicts = await service.GetOpenConflictsAsync(CancellationToken.None);
        var conflictId = conflicts[0].ConflictId;

        var updated = await service.ResolveAsync(
            new ResolveConflictRequest(conflictId, "Dismiss", "qa@meridian.test"),
            CancellationToken.None);

        updated!.Status.Should().Be("Dismissed");
    }

    [Fact]
    public async Task ResolveAsync_WhenConflictNotFound_ReturnsNull()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityProjectionRecord>());

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var result = await service.ResolveAsync(
            new ResolveConflictRequest(Guid.NewGuid(), "AcceptA", "test"),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_WhenAlreadyResolved_ReturnsNull()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();

        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                MakeProjection(securityA, "Cusip", "037833100", provider: "provA"),
                MakeProjection(securityB, "Cusip", "037833100", provider: "provB")
            });

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var conflicts = await service.GetOpenConflictsAsync(CancellationToken.None);
        var conflictId = conflicts[0].ConflictId;

        // First operator wins the governed decision.
        var first = await service.ResolveAsync(
            new ResolveConflictRequest(conflictId, "AcceptA", "operator.a@meridian.test"),
            CancellationToken.None);
        first.Should().NotBeNull();
        first!.Status.Should().Be("Resolved");

        // A second, concurrent operator resolving the same conflict must observe null rather than
        // silently overwriting the first operator's winner.
        var second = await service.ResolveAsync(
            new ResolveConflictRequest(conflictId, "AcceptB", "operator.b@meridian.test"),
            CancellationToken.None);
        second.Should().BeNull("a conflict that is no longer Open cannot be re-resolved");
    }

    [Fact]
    public async Task ResolveAsync_RecordsChosenWinnerAndResolverAtomically()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();

        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                MakeProjection(securityA, "Cusip", "037833100", provider: "provA"),
                MakeProjection(securityB, "Cusip", "037833100", provider: "provB")
            });

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var conflicts = await service.GetOpenConflictsAsync(CancellationToken.None);
        var conflictId = conflicts[0].ConflictId;

        var updated = await service.ResolveAsync(
            new ResolveConflictRequest(conflictId, "Resolve", "operator@meridian.test", "Edgar is golden.", ChosenWinnerSource: "Edgar"),
            CancellationToken.None);

        updated.Should().NotBeNull();
        updated!.Status.Should().Be("Resolved");
        updated.ResolvedWinnerSource.Should().Be("Edgar");
        updated.ResolvedBy.Should().Be("operator@meridian.test");
        updated.ResolvedReason.Should().Be("Edgar is golden.");
        updated.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetConflictAsync_WhenNotFound_ReturnsNull()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityProjectionRecord>());

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var result = await service.GetConflictAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOpenConflictsAsync_OnceResolved_ExcludesFromOpenList()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();

        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                MakeProjection(securityA, "Ticker", "AAPL", provider: "XNAS"),
                MakeProjection(securityB, "Ticker", "AAPL", provider: "XNAS")
            });

        var service = new SecurityMasterConflictService(
            store, NullLogger<SecurityMasterConflictService>.Instance);

        var initial = await service.GetOpenConflictsAsync(CancellationToken.None);
        initial.Should().HaveCount(1);

        await service.ResolveAsync(
            new ResolveConflictRequest(initial[0].ConflictId, "AcceptA", "test"),
            CancellationToken.None);

        var afterResolve = await service.GetOpenConflictsAsync(CancellationToken.None);
        afterResolve.Should().BeEmpty("resolved conflicts must not appear in the open list");
    }
}
