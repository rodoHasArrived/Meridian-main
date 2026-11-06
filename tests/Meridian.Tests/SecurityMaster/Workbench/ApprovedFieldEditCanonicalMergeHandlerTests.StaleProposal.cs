using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// Remediation slice 2. An approved field edit records the canonical base it was drafted against.
/// At publish, a proposal whose field changed after the draft is refused instead of overwriting the
/// newer value. A change to another field still merges, an unchanged retry converges, and a revision
/// recorded before the base existed publishes as before. Each publish reports whether it changed
/// the golden record.
/// </summary>
public sealed partial class ApprovedFieldEditCanonicalMergeHandlerTests
{
    [Fact]
    public async Task SameFieldChangedAfterTheDraft_IsRefusedAsStale()
    {
        SetProjectionAt(3, BondTerms(par: 900m));
        var revisionId = await SeedApprovedRevisionWithBaseAsync("assetSpecificTerms.par", "1000", baseVersion: 3);
        SetProjectionAt(4, BondTerms(par: 950m));
        var canonical = new CanonicalAmendHarness(_projectionStore);

        var refused = await CreateCanonicalMergeHandler(canonical.Service)
            .Invoking(h => h.HandleAsync(Event(revisionId, "assetSpecificTerms.par") with { EffectiveFrom = DateTimeOffset.UtcNow }))
            .Should().ThrowAsync<StaleFieldProposalException>();

        refused.Which.BaseVersion.Should().Be(3);
        refused.Which.CurrentVersion.Should().Be(4);
        refused.Which.FieldPath.Should().Be("assetSpecificTerms.par");
        canonical.Upserted.Should().BeEmpty("the newer canonical value must not be overwritten");
    }

    [Fact]
    public async Task SameFieldChangedThroughAnAliasSpelling_IsRefusedAsStale()
    {
        // The merge removes every spelling of the declared key, so a change stored under another
        // spelling is a change to the same field.
        SetProjectionAt(3, BondTerms(par: 900m));
        var revisionId = await SeedApprovedRevisionWithBaseAsync("assetSpecificTerms.par", "1000", baseVersion: 3);
        var renamed = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1,
            maturity = "2031-06-30",
            couponType = "Fixed",
            couponRate = 4.25m,
            isCallable = false,
            subclass = "Corporate",
            Par = 950m,
        });
        SetProjectionAt(4, renamed);

        await CreateCanonicalMergeHandler(new CanonicalAmendHarness(_projectionStore).Service)
            .Invoking(h => h.HandleAsync(Event(revisionId, "assetSpecificTerms.par") with { EffectiveFrom = DateTimeOffset.UtcNow }))
            .Should().ThrowAsync<StaleFieldProposalException>();
    }

    [Fact]
    public async Task UnrelatedFieldChangedAfterTheDraft_StillMergesAndKeepsTheOtherChange()
    {
        SetProjectionAt(3, BondTerms(par: 900m));
        var revisionId = await SeedApprovedRevisionWithBaseAsync("assetSpecificTerms.par", "1000", baseVersion: 3);
        SetProjectionAt(4, BondTerms(par: 900m, couponRate: 4.75m));
        var canonical = new CanonicalAmendHarness(_projectionStore);
        var handler = CreateCanonicalMergeHandler(canonical.Service);

        await handler.HandleAsync(Event(revisionId, "assetSpecificTerms.par") with { EffectiveFrom = DateTimeOffset.UtcNow });

        var persisted = canonical.Upserted.Should().ContainSingle().Subject.AssetSpecificTerms;
        persisted.GetProperty("par").GetDecimal().Should().Be(1000m);
        persisted.GetProperty("couponRate").GetDecimal().Should().Be(4.75m, "the concurrent change to another field survives");
        var outcome = handler.TakeOutcome(revisionId);
        outcome!.Outcome.Should().Be(SecurityMasterCanonicalMergeOutcomeKind.Merged);
        outcome.CanonicalVersion.Should().NotBeNull();
        handler.TakeOutcome(revisionId).Should().BeNull("an outcome is reported once");
    }

    [Fact]
    public async Task FieldAlreadyHoldingTheApprovedValue_ConvergesEvenWhenTheVersionMoved()
    {
        // A retried publish whose merge already landed: the field differs from the base but equals
        // the proposal, so nothing is appended and nothing is refused.
        SetProjectionAt(3, BondTerms(par: 900m));
        var revisionId = await SeedApprovedRevisionWithBaseAsync("assetSpecificTerms.par", "1000", baseVersion: 3);
        SetProjectionAt(4, BondTerms(par: 1000m));
        var canonical = new CanonicalAmendHarness(_projectionStore);
        var handler = CreateCanonicalMergeHandler(canonical.Service);

        await handler.HandleAsync(Event(revisionId, "assetSpecificTerms.par") with { EffectiveFrom = DateTimeOffset.UtcNow });

        canonical.Upserted.Should().BeEmpty();
        handler.TakeOutcome(revisionId)!.Outcome.Should().Be(SecurityMasterCanonicalMergeOutcomeKind.AlreadyCanonical);
    }

    [Fact]
    public async Task RevisionWithoutARecordedBase_MergesAsBefore()
    {
        SetProjectionAt(4, BondTerms(par: 950m));
        var revisionId = await SeedApprovedFieldRevisionAsync("assetSpecificTerms.par", "1000");
        var canonical = new CanonicalAmendHarness(_projectionStore);

        await CreateCanonicalMergeHandler(canonical.Service)
            .HandleAsync(Event(revisionId, "assetSpecificTerms.par") with { EffectiveFrom = DateTimeOffset.UtcNow });

        canonical.Upserted.Should().ContainSingle().Subject.AssetSpecificTerms.GetProperty("par").GetDecimal().Should().Be(1000m);
    }

    [Fact]
    public async Task OverlayOnlyAndClearPublishes_ReportThatTheGoldenRecordDidNotChange()
    {
        SetProjection("Bond", BondTerms());
        var annotation = await SeedApprovedFieldRevisionAsync("marketData.rating", "AA+");
        var clear = await SeedApprovedFieldRevisionAsync("assetSpecificTerms.par", null);
        var handler = CreateHandler();

        await handler.HandleAsync(Event(annotation, "marketData.rating"));
        await handler.HandleAsync(Event(clear, "assetSpecificTerms.par"));

        handler.TakeOutcome(annotation)!.Outcome.Should().Be(SecurityMasterCanonicalMergeOutcomeKind.NotApplicable);
        handler.TakeOutcome(clear)!.Outcome.Should().Be(SecurityMasterCanonicalMergeOutcomeKind.NotApplicable);
    }

    [Fact]
    public void Snapshot_CoversEveryStoredSpellingOfTheField_AndIgnoresOtherFields()
    {
        var terms = JsonDocument.Parse("""{"par":900,"Par":901,"couponRate":4.25}""").RootElement;
        var same = JsonDocument.Parse("""{"Par":901,"par":900,"couponRate":9.99}""").RootElement;

        var snapshot = SecurityAssetTermsFieldSnapshot.Capture("Bond", terms, "assetSpecificTerms.par");

        snapshot.Should().Be("""{"Par":901,"par":900}""");
        SecurityAssetTermsFieldSnapshot.Capture("Bond", same, "assetSpecificTerms.par").Should().Be(snapshot);
        SecurityAssetTermsFieldSnapshot.Capture("Bond", JsonSerializer.SerializeToElement(new { couponRate = 1m }), "assetSpecificTerms.par")
            .Should().Be("{}", "an absent field snapshots as empty, so adding it later counts as a change");
    }

    [Fact]
    public void Snapshot_OfAProfileField_ReadsInsideTheEnvelope()
    {
        var terms = JsonDocument.Parse(
            """{"customProfileId":"custom-private-credit","profileVersion":1,"profileFields":{"navDate":"2026-06-30","NAVDATE":"2026-07-01","other":1}}""").RootElement;

        SecurityAssetTermsFieldSnapshot.Capture("CustomAsset", terms, "assetSpecificTerms.profileFields.navDate")
            .Should().Be("""{"profileFields.NAVDATE":"2026-07-01","profileFields.navDate":"2026-06-30"}""");
        SecurityAssetTermsFieldSnapshot.Capture("CustomAsset", terms, "marketData.rating").Should().BeNull();
        SecurityAssetTermsFieldSnapshot.Capture("CustomAsset", terms, "assetSpecificTerms.profileFields.a.b").Should().BeNull();
    }

    private async Task<Guid> SeedApprovedRevisionWithBaseAsync(string fieldPath, string value, long baseVersion)
    {
        var projection = await _projectionStore.Object.GetProjectionAsync(SecurityId);
        var snapshot = SecurityAssetTermsFieldSnapshot.Capture(projection!.AssetClass, projection.AssetSpecificTerms, fieldPath)!;
        var draft = await _revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", fieldPath, new DateTimeOffset(2026, 03, 15, 0, 0, 0, TimeSpan.Zero),
            "Trustee statement correction.", fundProfileId: null,
            fieldValue: new SecurityMasterRevisionFieldValue(value),
            fieldBase: new SecurityMasterRevisionFieldBase(baseVersion, snapshot));
        draft.FieldBaseRecorded.Should().BeTrue();
        await _revisions.TransitionAsync(
            draft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await _revisions.TransitionAsync(
            draft.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");
        return draft.RevisionId;
    }

    private void SetProjectionAt(long version, object assetSpecificTerms)
    {
        var projection = new SecurityProjectionRecord(
            SecurityId: SecurityId,
            AssetClass: "Bond",
            Status: SecurityStatusDto.Active,
            DisplayName: "Merge target",
            Currency: "USD",
            PrimaryIdentifierKind: "InternalCode",
            PrimaryIdentifierValue: "MRG-1",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Merge target", currency = "USD" }),
            AssetSpecificTerms: assetSpecificTerms is JsonElement element ? element : JsonSerializer.SerializeToElement(assetSpecificTerms),
            Provenance: JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "Bloomberg",
                asOf = "2026-01-01T00:00:00+00:00",
                updatedBy = "ingest",
            }),
            Version: version,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-30),
            EffectiveTo: null,
            Identifiers:
            [
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "MRG-1", true, DateTimeOffset.UtcNow.AddDays(-30))
            ],
            Aliases: Array.Empty<SecurityAliasDto>());
        _projectionStore
            .Setup(s => s.GetProjectionAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);
    }

    private static object BondTerms(decimal? par, decimal couponRate)
        => new
        {
            schemaVersion = 1,
            maturity = "2031-06-30",
            couponType = "Fixed",
            couponRate,
            isCallable = false,
            subclass = "Corporate",
            par,
        };
}
