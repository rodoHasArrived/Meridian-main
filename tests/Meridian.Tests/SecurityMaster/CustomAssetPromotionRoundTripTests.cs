using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// CustomAsset promotion (ADR-022, plan slice E): an explicit envelope amendment promotes a
/// CustomAsset to the class its pinned profile reclassifies to, and a write that carries no envelope
/// (a no-patch amendment or a deactivation) keeps the stored CustomAsset class (R1).
/// </summary>
[Trait("Category", "Unit")]
public sealed class CustomAssetPromotionRoundTripTests
{
    [Fact]
    public async Task CoInvestSpv_RepinnedToPrivateFundInterest_PromotesAndKeepsItsIdentity()
    {
        var harness = new Harness();
        var securityId = Guid.NewGuid();
        var stored = CustomAssetRecord(securityId, CoInvestSpvEnvelope(), version: 2);
        harness.Seed(stored);

        var detail = await harness.Service.AmendTermsAsync(EnvelopeAmend(securityId, expectedVersion: 2, PrivateFundInterestEnvelope()));

        detail.AssetClass.Should().Be("PrivateFundInterest");
        detail.Version.Should().Be(3, "a promotion is one amendment and advances the version by one");
        detail.Identifiers.Select(static identifier => (identifier.Kind, identifier.Value, identifier.IsPrimary))
            .Should().BeEquivalentTo(stored.Identifiers.Select(static identifier => (identifier.Kind, identifier.Value, identifier.IsPrimary)),
                "the record keeps its Security Master id and identifiers");
        detail.SecurityId.Should().Be(securityId);

        var persisted = harness.LastPersisted!;
        persisted.AssetClass.Should().Be("PrivateFundInterest");
        SecurityTermsEnvelope.GetDeclaredFormat(persisted.AssetSpecificTerms).Should().Be(SecurityTermsFormats.CustomProfile,
            "the promoted record still carries the profile envelope it was pinned with");
        persisted.AssetSpecificTerms.GetProperty("customProfileId").GetString().Should().Be("private-fund-interest");

        var appended = harness.Appended.Should().ContainSingle().Subject;
        appended.EventType.Should().Be("TermsAmended");
        SecurityMasterMapping.FromEventEnvelope(appended).LegacyAssetClass.Should().Be("PrivateFundInterest",
            "the event records the promoted class, so replay rebuilds the promoted record");
    }

    [Fact]
    public async Task NoPatchAmend_OfACustomAssetPinnedToAReclassifyingProfile_KeepsCustomAsset()
    {
        // R1: a legacy CustomAsset already pinned to private-fund-interest is not promoted by an
        // amendment that only changes common terms. Promotion needs the envelope resubmitted.
        var harness = new Harness();
        var securityId = Guid.NewGuid();
        harness.Seed(CustomAssetRecord(securityId, PrivateFundInterestEnvelope(), version: 4));

        var detail = await harness.Service.AmendTermsAsync(CommonTermsAmend(securityId, expectedVersion: 4, "Fund interest renamed"));

        detail.AssetClass.Should().Be("CustomAsset");
        detail.DisplayName.Should().Be("Fund interest renamed");
        harness.LastPersisted!.AssetClass.Should().Be("CustomAsset");
        SecurityMasterMapping.FromEventEnvelope(harness.Appended.Single()).LegacyAssetClass.Should().Be("CustomAsset");

        // The explicit envelope amendment is still the way to promote it.
        var promoted = await harness.Service.AmendTermsAsync(EnvelopeAmend(securityId, expectedVersion: 5, PrivateFundInterestEnvelope()));
        promoted.AssetClass.Should().Be("PrivateFundInterest");
        promoted.Version.Should().Be(6);
    }

    [Fact]
    public async Task Deactivation_OfACustomAssetPinnedToAReclassifyingProfile_KeepsCustomAsset()
    {
        var harness = new Harness();
        var securityId = Guid.NewGuid();
        harness.Seed(CustomAssetRecord(securityId, PrivateFundInterestEnvelope(), version: 2));

        await harness.Service.DeactivateAsync(new DeactivateSecurityRequest(
            securityId, 2, DateTimeOffset.UtcNow, "test", "codex", null, "retire"));

        harness.LastPersisted!.AssetClass.Should().Be("CustomAsset", "a deactivation carries no envelope and never promotes");
        harness.LastPersisted.Status.Should().Be(SecurityStatusDto.Inactive);
        SecurityMasterMapping.FromEventEnvelope(harness.Appended.Single()).LegacyAssetClass.Should().Be("CustomAsset");
    }

    [Fact]
    public async Task NoPatchAmend_OfAnAlreadyPromotedRecord_KeepsItsFirstClassClass()
    {
        // The guard is about the stored CustomAsset class only; a promoted record resolves as before.
        var harness = new Harness();
        var securityId = Guid.NewGuid();
        harness.Seed(CustomAssetRecord(securityId, PrivateFundInterestEnvelope(), version: 2) with { AssetClass = "PrivateFundInterest" });

        var detail = await harness.Service.AmendTermsAsync(CommonTermsAmend(securityId, expectedVersion: 2, "Promoted fund renamed"));

        detail.AssetClass.Should().Be("PrivateFundInterest");
    }

    [Fact]
    public void EveryReclassifyingProfile_TargetsAProfileBackedClassWithARegistryProjection()
    {
        // A class that declares a reclassifying profile id must accept profile-backed terms (or the
        // promoted envelope would not decode) and must have a registry descriptor (or the promoted
        // record would have no relational projection to move to).
        var declaring = SecurityAssetClassCatalog.All.Where(static descriptor => descriptor.ReclassifyingProfileIds.Count > 0).ToArray();
        declaring.Should().NotBeEmpty();

        foreach (var descriptor in declaring)
        {
            descriptor.SupportsProfileBackedTerms.Should().BeTrue($"{descriptor.AssetClass} declares reclassifying profiles");
            SecurityTermsProjectionRegistry.TryGet(descriptor.AssetClass, out _).Should().BeTrue(
                $"{descriptor.AssetClass} declares reclassifying profiles and needs a registry descriptor");
            foreach (var profileId in descriptor.ReclassifyingProfileIds)
                SecurityAssetPackRegistry.ProfileAssetClasses[profileId].Should().Be(descriptor.AssetClass);
        }
    }

    internal static JsonElement CoInvestSpvEnvelope()
        => JsonSerializer.SerializeToElement(new
        {
            customProfileId = "co-invest-spv",
            profileVersion = 1,
            profileFields = new
            {
                vehicle = "Meridian Co-Invest I",
                underlyingCompanyOrSecurity = "Acme Holdings",
                sponsor = "Meridian Growth Partners",
                commitment = 1_000_000m,
                economics = "1/10 over 8",
                reportingCadence = "Quarterly"
            },
            profileApproval = SeedApproval()
        });

    internal static JsonElement PrivateFundInterestEnvelope()
        => JsonSerializer.SerializeToElement(new
        {
            customProfileId = "private-fund-interest",
            profileVersion = 1,
            profileFields = new
            {
                gpSponsor = "Meridian Growth Partners",
                strategy = "Buyout",
                vintage = 2024,
                commitment = 5_000_000m,
                fundedAmount = 0m,
                unfundedAmount = 5_000_000m,
                navDate = "2026-06-30"
            },
            profileApproval = SeedApproval()
        });

    private static object SeedApproval() => new
    {
        approvedBy = "Meridian",
        approvedAtUtc = "2026-05-29T00:00:00Z",
        approvalReference = "MERIDIAN-SEED-APPROVAL"
    };

    internal static SecurityProjectionRecord CustomAssetRecord(Guid securityId, JsonElement envelope, long version)
        => new(
            securityId,
            "CustomAsset",
            SecurityStatusDto.Active,
            "SPV becoming fund interest",
            "USD",
            "InternalCode",
            "SPV-PROMOTE",
            CommonTerms("SPV becoming fund interest"),
            SecurityTermsEnvelope.StampDeclaredFormat(envelope),
            JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "provA",
                asOf = DateTimeOffset.UtcNow.AddDays(-1),
                updatedBy = "feed"
            }),
            version,
            DateTimeOffset.UtcNow.AddDays(-30),
            null,
            new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "SPV-PROMOTE", true, DateTimeOffset.UtcNow.AddDays(-30), null, null)
            },
            Array.Empty<SecurityAliasDto>());

    private static JsonElement CommonTerms(string displayName)
        => JsonSerializer.SerializeToElement(new
        {
            displayName,
            currency = "USD",
            exchange = "XNYS",
            lotSize = 1,
            tickSize = 0.01m
        });

    internal static AmendSecurityTermsRequest EnvelopeAmend(Guid securityId, long expectedVersion, JsonElement envelope)
        => new(
            securityId,
            expectedVersion,
            CommonTerms: null,
            AssetSpecificTermsPatch: envelope,
            IdentifiersToAdd: [],
            IdentifiersToExpire: [],
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "provA",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "promote to fund interest");

    private static AmendSecurityTermsRequest CommonTermsAmend(Guid securityId, long expectedVersion, string displayName)
        => new(
            securityId,
            expectedVersion,
            CommonTerms: CommonTerms(displayName),
            AssetSpecificTermsPatch: null,
            IdentifiersToAdd: [],
            IdentifiersToExpire: [],
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "provA",
            UpdatedBy: "codex",
            SourceRecordId: null,
            Reason: "rename");

    /// <summary>A service over substitute stores that keeps the last persisted projection and captures appended events.</summary>
    private sealed class Harness
    {
        public Harness()
        {
            var eventStore = Substitute.For<ISecurityMasterEventStore>();
            eventStore.AppendAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<long>(),
                    Arg.Do<IReadOnlyList<SecurityMasterEventEnvelope>>(events => Appended.AddRange(events)),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
            Store = Substitute.For<ISecurityMasterStore>();
            Store.UpsertProjectionAsync(Arg.Any<SecurityProjectionRecord>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Seed(call.Arg<SecurityProjectionRecord>());
                    return Task.CompletedTask;
                });

            Service = new SecurityMasterService(
                eventStore,
                snapshotStore,
                Store,
                new SecurityMasterAggregateRebuilder(eventStore, snapshotStore),
                new SecurityMasterOptions { SnapshotIntervalVersions = 50, ResolveInactiveByDefault = true },
                NullLogger<SecurityMasterService>.Instance,
                assetProfileCatalog: StaticSecurityAssetProfileCatalog.CreateDefault());
        }

        public ISecurityMasterStore Store { get; }

        public SecurityMasterService Service { get; }

        public List<SecurityMasterEventEnvelope> Appended { get; } = [];

        public SecurityProjectionRecord? LastPersisted { get; private set; }

        public void Seed(SecurityProjectionRecord record)
        {
            LastPersisted = record;
            Store.GetProjectionAsync(record.SecurityId, Arg.Any<CancellationToken>()).Returns(record);
        }
    }
}
