using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// Remediation slice 1: <see cref="SecurityMasterService"/> enforces
/// <see cref="SecurityMasterWorkbenchOptions.RequireGovernedTermAmendments"/> itself, so every
/// in-process caller gets the refusal, not only the HTTP routes and the WPF editor. Governed
/// publication and system ingest write through separate typed paths that the setting does not
/// block. Publication must name an approved revision, and system ingest refuses operator source
/// systems.
/// </summary>
public sealed class SecurityMasterGovernedWriteAuthorityTests
{
    private static readonly Guid SecurityId = Guid.Parse("6f0d5a59-3f8a-4b1e-9c51-1f0b8f9b7a11");

    private readonly Mock<ISecurityMasterStore> _projectionStore = new(MockBehavior.Loose);
    private readonly InMemorySecurityMasterRevisionStore _revisions = new();
    private readonly MutableWorkbenchOptions _options = new(new SecurityMasterWorkbenchOptions { RequireGovernedTermAmendments = true });

    public SecurityMasterGovernedWriteAuthorityTests()
    {
        SetBondProjection();
    }

    [Fact]
    public async Task DirectAmend_WithGovernanceOn_IsRefusedBeforeAnyWrite()
    {
        var canonical = Harness();

        var refused = await canonical.Service.Invoking(s => s.AmendTermsAsync(AmendPar(1000m, "desk-tool")))
            .Should().ThrowAsync<GovernedTermAmendmentRequiredException>();

        refused.Which.Title.Should().Be(GovernedTermAmendmentPolicy.RefusalTitle);
        refused.Which.Detail.Should().Be(GovernedTermAmendmentPolicy.RefusalDetail);
        canonical.Appended.Should().BeEmpty();
        canonical.Upserted.Should().BeEmpty();
    }

    [Fact]
    public async Task DirectAmend_WithGovernanceOff_KeepsTheDirectWritePath()
    {
        _options.Set(new SecurityMasterWorkbenchOptions { RequireGovernedTermAmendments = false });
        var canonical = Harness();

        await canonical.Service.AmendTermsAsync(AmendPar(1000m, "desk-tool"));

        canonical.Upserted.Should().ContainSingle();
    }

    [Fact]
    public async Task DirectAmend_WithNoPolicyComposed_KeepsTheDirectWritePath()
    {
        var canonical = new CanonicalAmendHarness(_projectionStore, governedTermAmendmentPolicy: null);

        await canonical.Service.AmendTermsAsync(AmendPar(1000m, "desk-tool"));

        canonical.Upserted.Should().ContainSingle("without a composed policy the default leaves direct amendments permitted");
    }

    [Fact]
    public async Task DirectAmend_FollowsTheSettingAtCallTime()
    {
        _options.Set(new SecurityMasterWorkbenchOptions { RequireGovernedTermAmendments = false });
        var canonical = Harness();
        await canonical.Service.AmendTermsAsync(AmendPar(1000m, "desk-tool"));

        _options.Set(new SecurityMasterWorkbenchOptions { RequireGovernedTermAmendments = true });

        await canonical.Service.Invoking(s => s.AmendTermsAsync(AmendPar(2000m, "desk-tool")))
            .Should().ThrowAsync<GovernedTermAmendmentRequiredException>("the setting hot-reloads");
    }

    [Theory]
    [InlineData("Preferred")]
    [InlineData("Convertible")]
    [InlineData("TermBlock")]
    public async Task DirectSiblingAmendments_WithGovernanceOn_AreRefused(string route)
    {
        var canonical = Harness();
        var service = canonical.Service;

        // The refusal comes before the request is read, so the route's request shape is irrelevant here.
        Func<Task> act = route switch
        {
            "Preferred" => () => service.AmendPreferredEquityTermsAsync(SecurityId, null!),
            "Convertible" => () => service.AmendConvertibleEquityTermsAsync(SecurityId, null!),
            _ => () => service.AmendTermBlockAsync(SecurityId, "callSchedule", null!),
        };

        await act.Should().ThrowAsync<GovernedTermAmendmentRequiredException>();
        canonical.Appended.Should().BeEmpty();
    }

    [Theory]
    [InlineData("edgar")]
    [InlineData("PolygonBackfill")]
    [InlineData("security-master-symbology")]
    public async Task SystemIngest_WithGovernanceOn_StillWrites(string sourceSystem)
    {
        var canonical = Harness();
        ISecurityMasterAmender systemPath = canonical.Service;

        await systemPath.AmendTermsAsync(AmendPar(1000m, sourceSystem));

        canonical.Upserted.Should().ContainSingle("provider ingest is not an operator term correction");
    }

    [Theory]
    [InlineData(SecurityMasterWriteAuthority.WorkbenchSourceSystem)]
    [InlineData(SecurityMasterWriteAuthority.DesktopEditorSourceSystem)]
    [InlineData(SecurityMasterWriteAuthority.WorkstationHttpSourceSystem)]
    [InlineData(" wpf-ui ")]
    public async Task SystemIngest_RefusesOperatorSourceSystems(string sourceSystem)
    {
        _options.Set(new SecurityMasterWorkbenchOptions { RequireGovernedTermAmendments = false });
        var canonical = Harness();
        ISecurityMasterAmender systemPath = canonical.Service;

        await systemPath.Invoking(s => s.AmendTermsAsync(AmendPar(1000m, sourceSystem)))
            .Should().ThrowAsync<SecurityMasterWriteRefusedException>();
        canonical.Appended.Should().BeEmpty();
    }

    [Fact]
    public async Task Publication_OfAnApprovedRevision_WritesWithGovernanceOn()
    {
        var canonical = Harness();
        var revisionId = await SeedRevisionAsync(SecurityMasterRevisionStateDto.Approved);

        await canonical.Service.AmendApprovedRevisionAsync(AmendPar(1000m, SecurityMasterWriteAuthority.WorkbenchSourceSystem, revisionId));

        canonical.Upserted.Should().ContainSingle();
    }

    [Fact]
    public async Task Publication_OfAnAlreadyPublishedRevision_IsAcceptedForARetriedPublish()
    {
        var canonical = Harness();
        var revisionId = await SeedRevisionAsync(SecurityMasterRevisionStateDto.Published);

        await canonical.Service.AmendApprovedRevisionAsync(AmendPar(1000m, SecurityMasterWriteAuthority.WorkbenchSourceSystem, revisionId));

        canonical.Upserted.Should().ContainSingle();
    }

    [Theory]
    [InlineData(SecurityMasterRevisionStateDto.Draft)]
    [InlineData(SecurityMasterRevisionStateDto.Submitted)]
    public async Task Publication_OfAnUnapprovedRevision_IsRefused(SecurityMasterRevisionStateDto state)
    {
        var canonical = Harness();
        var revisionId = await SeedRevisionAsync(state);

        await canonical.Service.Invoking(s => s.AmendApprovedRevisionAsync(
                AmendPar(1000m, SecurityMasterWriteAuthority.WorkbenchSourceSystem, revisionId)))
            .Should().ThrowAsync<SecurityMasterWriteRefusedException>();
        canonical.Appended.Should().BeEmpty();
    }

    [Fact]
    public async Task Publication_NamingAnotherSecuritysRevision_IsRefused()
    {
        var canonical = Harness();
        var otherSecurityDraft = await _revisions.CreateDraftAsync(Guid.NewGuid(), "ops.analyst");
        await _revisions.TransitionAsync(otherSecurityDraft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await _revisions.TransitionAsync(otherSecurityDraft.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");

        await canonical.Service.Invoking(s => s.AmendApprovedRevisionAsync(
                AmendPar(1000m, SecurityMasterWriteAuthority.WorkbenchSourceSystem, otherSecurityDraft.RevisionId)))
            .Should().ThrowAsync<SecurityMasterWriteRefusedException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-revision")]
    [InlineData("8b9b0c8e-0d4f-4f43-9a55-000000000000")]
    public async Task Publication_WithoutAKnownRevision_IsRefused(string? sourceRecordId)
    {
        var canonical = Harness();
        var request = AmendPar(1000m, SecurityMasterWriteAuthority.WorkbenchSourceSystem) with { SourceRecordId = sourceRecordId };

        await canonical.Service.Invoking(s => s.AmendApprovedRevisionAsync(request))
            .Should().ThrowAsync<SecurityMasterWriteRefusedException>();
    }

    [Fact]
    public async Task Publication_WithNoRevisionStore_FailsClosedWhileGovernanceIsOn()
    {
        var canonical = new CanonicalAmendHarness(_projectionStore, governedTermAmendmentPolicy: new GovernedTermAmendmentPolicy(_options));

        await canonical.Service.Invoking(s => s.AmendApprovedRevisionAsync(
                AmendPar(1000m, SecurityMasterWriteAuthority.WorkbenchSourceSystem, Guid.NewGuid())))
            .Should().ThrowAsync<SecurityMasterWriteRefusedException>();
    }

    [Fact]
    public async Task Publication_WithNoRevisionStore_KeepsTodaysBehaviorWhileGovernanceIsOff()
    {
        _options.Set(new SecurityMasterWorkbenchOptions { RequireGovernedTermAmendments = false });
        var canonical = new CanonicalAmendHarness(_projectionStore, governedTermAmendmentPolicy: new GovernedTermAmendmentPolicy(_options));

        await canonical.Service.AmendApprovedRevisionAsync(
            AmendPar(1000m, SecurityMasterWriteAuthority.WorkbenchSourceSystem, Guid.NewGuid()));

        canonical.Upserted.Should().ContainSingle();
    }

    [Fact]
    public async Task ApprovedFieldEditMerge_PublishesWhileDirectAmendmentsAreRefused()
    {
        var canonical = Harness();
        var draft = await _revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow,
            "Trustee statement correction.", fieldValue: new SecurityMasterRevisionFieldValue("1000"));
        await _revisions.TransitionAsync(draft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await _revisions.TransitionAsync(draft.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");
        var merge = new ApprovedFieldEditCanonicalMergeHandler(
            _revisions, NullLogger<ApprovedFieldEditCanonicalMergeHandler>.Instance, _projectionStore.Object, canonical.Service);

        await merge.HandleAsync(new SecurityMasterRevisionPublishedEvent(
            SecurityId: SecurityId,
            RevisionId: draft.RevisionId,
            Version: 3,
            EffectiveFrom: DateTimeOffset.UtcNow,
            ChangedFields: ["assetSpecificTerms.par"],
            DownstreamImpact: new SecurityMasterDownstreamImpactDto(
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
                Links: []),
            AffectedLedgerBookIds: [],
            Actor: "ops.publisher",
            CorrelationId: null));

        var persisted = canonical.Upserted.Should().ContainSingle().Subject;
        persisted.AssetSpecificTerms.GetProperty("par").GetDecimal().Should().Be(1000m);
        await canonical.Service.Invoking(s => s.AmendTermsAsync(AmendPar(2000m, "desk-tool")))
            .Should().ThrowAsync<GovernedTermAmendmentRequiredException>();
    }

    private CanonicalAmendHarness Harness()
        => new(_projectionStore, governedTermAmendmentPolicy: new GovernedTermAmendmentPolicy(_options), revisionStore: _revisions);

    private async Task<Guid> SeedRevisionAsync(SecurityMasterRevisionStateDto state)
    {
        var draft = await _revisions.CreateDraftAsync(SecurityId, "ops.analyst");
        var path = new[]
        {
            SecurityMasterRevisionStateDto.Draft,
            SecurityMasterRevisionStateDto.Submitted,
            SecurityMasterRevisionStateDto.Approved,
            SecurityMasterRevisionStateDto.Published,
        };
        for (var step = 1; step < path.Length && path[step - 1] != state; step++)
        {
            await _revisions.TransitionAsync(draft.RevisionId, path[step - 1], path[step], "ops.reviewer");
        }

        return draft.RevisionId;
    }

    private static AmendSecurityTermsRequest AmendPar(decimal par, string sourceSystem, Guid? revisionId = null)
        => new(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            CommonTerms: null,
            AssetSpecificTermsPatch: JsonSerializer.SerializeToElement(BondTerms(par)),
            IdentifiersToAdd: [],
            IdentifiersToExpire: [],
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: sourceSystem,
            UpdatedBy: "ops.analyst",
            SourceRecordId: revisionId?.ToString("D"),
            Reason: "Par correction.");

    private void SetBondProjection()
    {
        var projection = new SecurityProjectionRecord(
            SecurityId: SecurityId,
            AssetClass: "Bond",
            Status: SecurityStatusDto.Active,
            DisplayName: "Governed bond",
            Currency: "USD",
            PrimaryIdentifierKind: "InternalCode",
            PrimaryIdentifierValue: "GOV-1",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Governed bond", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(BondTerms(par: null)),
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
                new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "GOV-1", true, DateTimeOffset.UtcNow.AddDays(-30))
            ],
            Aliases: Array.Empty<SecurityAliasDto>());
        _projectionStore
            .Setup(s => s.GetProjectionAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projection);
    }

    private static object BondTerms(decimal? par)
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

    private sealed class MutableWorkbenchOptions(SecurityMasterWorkbenchOptions initial) : IOptionsMonitor<SecurityMasterWorkbenchOptions>
    {
        public SecurityMasterWorkbenchOptions CurrentValue { get; private set; } = initial;

        public void Set(SecurityMasterWorkbenchOptions value) => CurrentValue = value;

        public SecurityMasterWorkbenchOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SecurityMasterWorkbenchOptions, string?> listener) => null;
    }
}
