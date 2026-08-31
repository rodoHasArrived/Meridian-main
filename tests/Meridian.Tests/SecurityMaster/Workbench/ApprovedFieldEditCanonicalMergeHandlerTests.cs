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
/// The publish fan-out's canonical-merge handler (Order=5): an approved, schema-validated
/// <c>assetSpecificTerms.*</c> field edit must be merged into the CANONICAL security terms as a
/// complete economic-definition amendment — the missing link that previously left the whole
/// approval lifecycle governing a side-table annotation. Clears, annotation paths, and legacy
/// value-less revisions stay overlay-only; a retried publish whose merge already landed detects the
/// no-op instead of appending a duplicate event.
/// </summary>
public sealed class ApprovedFieldEditCanonicalMergeHandlerTests
{
    private static readonly Guid SecurityId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly InMemorySecurityMasterRevisionStore _revisions = new();
    private readonly Mock<ISecurityMasterStore> _projectionStore = new(MockBehavior.Loose);
    private readonly Mock<ISecurityMasterAmender> _amender = new(MockBehavior.Loose);

    [Fact]
    public void Order_RunsBeforeProjectionRebuild()
    {
        CreateHandler().Order.Should().BeLessThan(
            new Meridian.Application.SecurityMaster.Rebuild.SecurityProjectionRebuildHandler(
                NullLogger<Meridian.Application.SecurityMaster.Rebuild.SecurityProjectionRebuildHandler>.Instance).Order,
            "the projection rebuild must observe the merged canonical terms");
    }

    [Fact]
    public async Task NoBackendConfigured_IsNoOp()
    {
        var handler = new ApprovedFieldEditCanonicalMergeHandler(
            _revisions, NullLogger<ApprovedFieldEditCanonicalMergeHandler>.Instance);

        await handler.Invoking(h => h.HandleAsync(Event(Guid.NewGuid(), "assetSpecificTerms.par")))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task AnnotationPath_StaysOverlayOnly()
    {
        var revisionId = await SeedApprovedFieldRevisionAsync("marketData.rating", "AA+");
        SetProjection("Bond", BondTerms());

        await CreateHandler().HandleAsync(Event(revisionId, "marketData.rating"));

        _amender.Verify(
            a => a.AmendTermsAsync(It.IsAny<AmendSecurityTermsRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Clear_StaysOverlayOnly()
    {
        var revisionId = await SeedApprovedFieldRevisionAsync("assetSpecificTerms.par", value: null);
        SetProjection("Bond", BondTerms());

        await CreateHandler().HandleAsync(Event(revisionId, "assetSpecificTerms.par"));

        _amender.Verify(
            a => a.AmendTermsAsync(It.IsAny<AmendSecurityTermsRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LegacyRevisionWithoutRecordedValue_StaysOverlayOnly()
    {
        var draft = await _revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "justified",
            fundProfileId: null, fieldValue: null);
        SetProjection("Bond", BondTerms());

        await CreateHandler().HandleAsync(Event(draft.RevisionId, "assetSpecificTerms.par"));

        _amender.Verify(
            a => a.AmendTermsAsync(It.IsAny<AmendSecurityTermsRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeclaredDecimalTerm_MergesTypedValueIntoCompleteDocument()
    {
        var revisionId = await SeedApprovedFieldRevisionAsync("assetSpecificTerms.par", "1000");
        SetProjection("Bond", BondTerms());
        AmendSecurityTermsRequest? captured = null;
        _amender
            .Setup(a => a.AmendTermsAsync(It.IsAny<AmendSecurityTermsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AmendSecurityTermsRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(Detail());

        await CreateHandler().HandleAsync(Event(revisionId, "assetSpecificTerms.par"));

        captured.Should().NotBeNull("an approved asset-terms correction must reach the canonical record");
        captured!.SecurityId.Should().Be(SecurityId);
        captured.ExpectedVersion.Should().Be(3, "the amendment guards against a concurrently advanced record");
        captured.SourceSystem.Should().Be("operator-workbench");
        captured.SourceRecordId.Should().Be(revisionId.ToString("D"));
        captured.CommonTerms.Should().BeNull();

        var patch = captured.AssetSpecificTermsPatch!.Value;
        patch.GetProperty("par").ValueKind.Should().Be(JsonValueKind.Number, "par is declared Decimal");
        patch.GetProperty("par").GetDecimal().Should().Be(1000m);
        patch.GetProperty("maturity").GetString().Should().Be("2031-06-30", "untouched terms must survive — the event is a COMPLETE definition");
        patch.GetProperty("subclass").GetString().Should().Be("Corporate");
        patch.GetProperty("isCallable").ValueKind.Should().Be(JsonValueKind.False);
    }

    [Fact]
    public async Task DeclaredDateTerm_StaysString_EvenWhenValueParsesAsJson()
    {
        var revisionId = await SeedApprovedFieldRevisionAsync("assetSpecificTerms.maturity", "2032-01-15");
        SetProjection("Bond", BondTerms());
        AmendSecurityTermsRequest? captured = null;
        _amender
            .Setup(a => a.AmendTermsAsync(It.IsAny<AmendSecurityTermsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AmendSecurityTermsRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(Detail());

        await CreateHandler().HandleAsync(Event(revisionId, "assetSpecificTerms.maturity"));

        captured!.AssetSpecificTermsPatch!.Value.GetProperty("maturity").ValueKind
            .Should().Be(JsonValueKind.String, "Date-typed terms serialize as strings");
        captured.AssetSpecificTermsPatch!.Value.GetProperty("maturity").GetString().Should().Be("2032-01-15");
    }

    [Fact]
    public async Task ValueAlreadyCanonical_SkipsAmendment()
    {
        // The retried-publish path: the first attempt merged par=500 durably, then a later handler
        // failed and the publish was retried. The rebuilt patch equals the stored document, so no
        // second amendment (and no duplicate event) may be appended.
        var revisionId = await SeedApprovedFieldRevisionAsync("assetSpecificTerms.par", "500");
        SetProjection("Bond", BondTerms(par: 500m));

        await CreateHandler().HandleAsync(Event(revisionId, "assetSpecificTerms.par"));

        _amender.Verify(
            a => a.AmendTermsAsync(It.IsAny<AmendSecurityTermsRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProfileField_MergesInsideProfileFieldsEnvelope()
    {
        var revisionId = await SeedApprovedFieldRevisionAsync("assetSpecificTerms.profileFields.currentFactor", "0.25");
        SetProjection("CustomAsset", new
        {
            schemaVersion = 3,
            customProfileId = "structured-credit-io-po",
            profileVersion = 1,
            profileFields = new { currentFactor = 0.5m, trancheName = "A1" },
        });
        AmendSecurityTermsRequest? captured = null;
        _amender
            .Setup(a => a.AmendTermsAsync(It.IsAny<AmendSecurityTermsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AmendSecurityTermsRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(Detail());

        await CreateHandler().HandleAsync(Event(revisionId, "assetSpecificTerms.profileFields.currentFactor"));

        var patch = captured!.AssetSpecificTermsPatch!.Value;
        patch.GetProperty("customProfileId").GetString().Should().Be(
            "structured-credit-io-po", "the merged patch must remain a COMPLETE profile envelope");
        var profileFields = patch.GetProperty("profileFields");
        profileFields.GetProperty("currentFactor").GetDecimal().Should().Be(0.25m);
        profileFields.GetProperty("trancheName").GetString().Should().Be("A1", "sibling profile fields must survive");
    }

    [Fact]
    public async Task DeeplyNestedPath_StaysOverlayOnly()
    {
        var revisionId = await SeedApprovedFieldRevisionAsync("assetSpecificTerms.principalSchedule.0.amount", "10");
        SetProjection("Bond", BondTerms());

        await CreateHandler().HandleAsync(Event(revisionId, "assetSpecificTerms.principalSchedule.0.amount"));

        _amender.Verify(
            a => a.AmendTermsAsync(It.IsAny<AmendSecurityTermsRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AmendFailure_PropagatesSoPublishStaysRetryable()
    {
        var revisionId = await SeedApprovedFieldRevisionAsync("assetSpecificTerms.par", "1000");
        SetProjection("Bond", BondTerms());
        _amender
            .Setup(a => a.AmendTermsAsync(It.IsAny<AmendSecurityTermsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("event store unavailable"));

        await CreateHandler().Invoking(h => h.HandleAsync(Event(revisionId, "assetSpecificTerms.par")))
            .Should().ThrowAsync<InvalidOperationException>(
                "a failed merge must fail the publish so the revision stays Approved and retryable");
    }

    [Fact]
    public async Task FreshWorkbenchChallengerConflict_IsAutoResolvedInOperatorsFavor()
    {
        var revisionId = await SeedApprovedFieldRevisionAsync("assetSpecificTerms.couponRate", "4.5");
        SetProjection("Bond", BondTerms());
        _amender
            .Setup(a => a.AmendTermsAsync(It.IsAny<AmendSecurityTermsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Detail());

        var conflict = new SecurityMasterConflict(
            ConflictId: Guid.NewGuid(),
            SecurityId: SecurityId,
            ConflictKind: "EconomicTermMismatch",
            FieldPath: "EconomicTerms.couponRate",
            ProviderA: "Bloomberg",
            ValueA: "4.25",
            ProviderB: "operator-workbench",
            ValueB: "4.5",
            DetectedAt: DateTimeOffset.UtcNow.AddMinutes(1),
            Status: "Open");
        var conflictService = new Mock<ISecurityMasterConflictService>(MockBehavior.Loose);
        conflictService
            .Setup(c => c.GetOpenConflictsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([conflict]);
        conflictService
            .Setup(c => c.ResolveAsync(It.IsAny<ResolveConflictRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(conflict with { Status = "Resolved" });

        await CreateHandler(conflictService.Object).HandleAsync(Event(revisionId, "assetSpecificTerms.couponRate"));

        conflictService.Verify(
            c => c.ResolveAsync(
                It.Is<ResolveConflictRequest>(r =>
                    r.ConflictId == conflict.ConflictId
                    && r.ChosenWinnerSource == "operator-workbench"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the maker-checker approval already adjudicated the operator value, so the merge-created conflict must not queue a second decision");
    }

    // ---- helpers ------------------------------------------------------------------------------

    private ApprovedFieldEditCanonicalMergeHandler CreateHandler(ISecurityMasterConflictService? conflictService = null)
        => new(
            _revisions,
            NullLogger<ApprovedFieldEditCanonicalMergeHandler>.Instance,
            _projectionStore.Object,
            _amender.Object,
            conflictService);

    private async Task<Guid> SeedApprovedFieldRevisionAsync(string fieldPath, string? value)
    {
        var draft = await _revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", fieldPath, new DateTimeOffset(2026, 03, 15, 0, 0, 0, TimeSpan.Zero),
            "Trustee statement correction.", fundProfileId: null,
            fieldValue: new SecurityMasterRevisionFieldValue(value));
        await _revisions.TransitionAsync(
            draft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await _revisions.TransitionAsync(
            draft.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");
        return draft.RevisionId;
    }

    private void SetProjection(string assetClass, object assetSpecificTerms)
    {
        var projection = new SecurityProjectionRecord(
            SecurityId: SecurityId,
            AssetClass: assetClass,
            Status: SecurityStatusDto.Active,
            DisplayName: "Merge target",
            Currency: "USD",
            PrimaryIdentifierKind: "InternalCode",
            PrimaryIdentifierValue: "MRG-1",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Merge target", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(assetSpecificTerms),
            Provenance: JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "Bloomberg",
                asOf = "2026-01-01T00:00:00+00:00",
                updatedBy = "ingest",
            }),
            Version: 3,
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

    private static object BondTerms(decimal? par = null)
        => new
        {
            schemaVersion = 1,
            maturity = "2031-06-30",
            couponType = "Fixed",
            couponRate = 4.25m,
            isCallable = false,
            subclass = "Corporate",
            par,
        };

    private static SecurityDetailDto Detail()
        => new(
            SecurityId,
            "Bond",
            SecurityStatusDto.Active,
            "Merge target",
            "USD",
            JsonSerializer.SerializeToElement(new { displayName = "Merge target", currency = "USD" }),
            JsonSerializer.SerializeToElement(new { }),
            Array.Empty<SecurityIdentifierDto>(),
            Array.Empty<SecurityAliasDto>(),
            4,
            DateTimeOffset.UtcNow.AddDays(-30),
            null);

    private static SecurityMasterRevisionPublishedEvent Event(Guid revisionId, string fieldPath)
        => new(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Version: 3,
            EffectiveFrom: new DateTimeOffset(2026, 03, 15, 0, 0, 0, TimeSpan.Zero),
            ChangedFields: [fieldPath],
            DownstreamImpact: EmptyImpact(),
            AffectedLedgerBookIds: [],
            Actor: "ops.analyst",
            CorrelationId: null);

    private static SecurityMasterDownstreamImpactDto EmptyImpact()
        => new(
            FundProfileId: null,
            IsScoped: false,
            Severity: SecurityMasterImpactSeverity.None,
            Summary: "n/a",
            PortfolioExposureSummary: string.Empty,
            LedgerExposureSummary: string.Empty,
            ReconciliationExposureSummary: string.Empty,
            ReportPackExposureSummary: string.Empty,
            MatchedRunCount: 0,
            PortfolioExposureCount: 0,
            LedgerExposureCount: 0,
            ReconciliationExposureCount: 0,
            ReportPackExposureCount: 0,
            Links: []);
}
