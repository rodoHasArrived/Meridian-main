using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster.Workbench;

public sealed class SecurityMasterImpactPreviewTests
{
    [Fact]
    public async Task UnavailablePriorCashFlows_AreRetainedWithoutInventingZeroDelta_AndPreviewDoesNotPublish()
    {
        using var fixture = new Fixture();
        var receipt = await fixture.CreateAsync();
        receipt.Before.Should().BeNull();
        receipt.After.Should().BeNull();
        receipt.Issues.Should().Contain(issue => issue.Code == "CashFlowSourceMissing");
        receipt.Books.Completeness.Should().Be(SecurityImpactCompleteness.Unavailable);
        receipt.Reports.Completeness.Should().Be(SecurityImpactCompleteness.Unavailable);
        (await fixture.Revisions.GetAsync(fixture.Revision.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
        await fixture.Securities.DidNotReceiveWithAnyArgs().UpsertProjectionAsync(default!);
        var retained = await fixture.Store.GetAsync(fixture.Scope, receipt.ContentSha256);
        retained!.ContentSha256.Should().Be(receipt.ContentSha256);
    }

    [Fact]
    public async Task PassiveFullProjection_DoesNotRetainOrReassignCalculationEvidence()
    {
        using var fixture = new Fixture();
        var receiptStore = Substitute.For<ISecurityCashFlowReceiptStore>();
        var sourceStore = Substitute.For<ISecurityMasterCashFlowStore>();
        var service = new SecurityMasterCashFlowService(sourceStore, [], Substitute.For<ISecurityMasterQueryService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SecurityMasterCashFlowService>.Instance, receiptStore: receiptStore);
        var selected = SecurityMasterMapping.ToDetail(fixture.Current);
        var source = new SecurityCashFlowSourceDto(selected.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), false, null, null);
        var result = await service.EvaluateProjectionAsync(selected, source, StructuredCashFlowScenario.Base,
            new Meridian.Contracts.FixedIncome.BondCalculationContext(DateTimeOffset.Parse("2026-08-01T00:00:00Z")));
        result.ContentSha256.Should().HaveLength(64);
        await receiptStore.DidNotReceiveWithAnyArgs().RetainAsync(default!);
        await sourceStore.DidNotReceiveWithAnyArgs().UpsertSourceAsync(default!);
    }

    [Fact]
    public async Task ReceiptIsolationAndIntegrity_AreEnforced_AndExactRetryIsIdempotent()
    {
        using var fixture = new Fixture();
        var receipt = await fixture.CreateAsync();
        await fixture.Store.RetainAsync(receipt);
        (await fixture.Store.GetAsync(new("another-tenant"), receipt.ContentSha256)).Should().BeNull();
        var tampered = receipt with { CanonicalVersion = receipt.CanonicalVersion + 1 };
        await FluentActions.Invoking(() => fixture.Store.RetainAsync(tampered)).Should().ThrowAsync<InvalidDataException>();
        await FluentActions.Invoking(() => fixture.Store.GetAsync(fixture.Scope, "../evidence")).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RetainedLegacyProof_KeepsUntaggedTermsAndOriginalDigest_WithoutChangingLiveReadRules()
    {
        using var fixture = new Fixture();
        var current = await fixture.CreateAsync();
        var legacyTerms = JsonSerializer.SerializeToElement(new { baseCurrency = "EUR", quoteCurrency = "USD" });
        var legacy = current with
        {
            PreviousTerms = current.PreviousTerms with { AssetSpecificTerms = legacyTerms },
            ProposedTerms = current.ProposedTerms with { AssetSpecificTerms = JsonSerializer.SerializeToElement(new { baseCurrency = "GBP", quoteCurrency = "USD" }) },
            FundProfileId = null,
            ContentSha256 = string.Empty
        };
        legacy = legacy with { ContentSha256 = FileSecurityMasterImpactPreviewStore.ReceiptHash(legacy) };
        await fixture.Store.RetainAsync(legacy);

        var retained = await fixture.Service.GetAsync(fixture.Scope, legacy.ContentSha256);
        retained!.ContentSha256.Should().Be(legacy.ContentSha256);
        retained.FundProfileId.Should().BeNull();
        retained.PreviousTerms.AssetSpecificTerms.GetRawText().Should().Be(legacyTerms.GetRawText());
        retained.PreviousTerms.AssetSpecificTerms.TryGetProperty(SecurityTermsEnvelope.FormatKey, out _).Should().BeFalse();
        FluentActions.Invoking(() => SecurityTermsEnvelope.ResolveFormat(retained.PreviousTerms.AssetSpecificTerms))
            .Should().Throw<SecurityTermsFormatUndeclaredException>();
    }

    [Fact]
    public async Task ChangedCanonicalTerms_RefuseOldReceipt_AndRetainTheApprovedBinding()
    {
        using var fixture = new Fixture();
        var receipt = await fixture.CreateAsync();
        await fixture.Revisions.TransitionAsync(fixture.Revision.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "submitter");
        await fixture.Revisions.BindImpactPreviewAsync(fixture.Revision.RevisionId, fixture.Revision.SecurityId,
            SecurityMasterRevisionStateDto.Submitted, receipt.ContentSha256, receipt.CanonicalVersion);
        var approved = await fixture.Revisions.TransitionAsync(fixture.Revision.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "reviewer");
        fixture.Current = fixture.Current with { Version = fixture.Current.Version + 1, AssetSpecificTerms = JsonSerializer.SerializeToElement(new { termsFormat = SecurityTermsFormats.Flat, baseCurrency = "GBP", quoteCurrency = "USD" }) };
        await FluentActions.Invoking(() => fixture.Service.ValidateAsync(approved, fixture.Scope, receipt.ContentSha256))
            .Should().ThrowAsync<SecurityMasterImpactPreviewException>().Where(error => error.Code == "ImpactSecurityChanged");
        await FluentActions.Invoking(() => fixture.CreateAsync()).Should().ThrowAsync<SecurityMasterImpactPreviewException>()
            .Where(error => error.Code == "ImpactApprovalImmutable");
        (await fixture.Revisions.GetAsync(approved.RevisionId))!.ReviewedImpactReceiptId.Should().Be(receipt.ContentSha256);
        (await fixture.Revisions.GetAsync(approved.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
    }

    [Fact]
    public async Task CanonicalRetry_RequiresTheExactOwnPublicationVersionAndProvenance()
    {
        using var fixture = new Fixture();
        var receipt = await fixture.CreateAsync();
        fixture.Current = fixture.Current with
        {
            Version = receipt.CanonicalVersion + 1,
            AssetSpecificTerms = receipt.ProposedTerms.AssetSpecificTerms,
            EffectiveFrom = receipt.ProposedTerms.EffectiveFrom,
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = SecurityMasterWriteAuthority.WorkbenchSourceSystem, sourceRecordId = fixture.Revision.RevisionId.ToString("D") })
        };
        (await fixture.Service.ValidateAsync(fixture.Revision, fixture.Scope, receipt.ContentSha256, canonicalMergeApplied: true))
            .ContentSha256.Should().Be(receipt.ContentSha256);
        fixture.Current = fixture.Current with { Version = fixture.Current.Version + 1 };
        await FluentActions.Invoking(() => fixture.Service.ValidateAsync(fixture.Revision, fixture.Scope,
            receipt.ContentSha256, canonicalMergeApplied: true)).Should().ThrowAsync<SecurityMasterImpactPreviewException>()
            .Where(error => error.Code == "ImpactSecurityChanged");
    }

    [Fact]
    public async Task ChangedPeriodVersionOrLock_RefusesTheReviewedReceipt()
    {
        using var fixture = new Fixture(withLedgerDependencies: true);
        var receipt = await fixture.CreateAsync();
        fixture.Period = fixture.Period with { Status = "HardClosed", Version = fixture.Period.Version + 1 };
        await FluentActions.Invoking(() => fixture.Service.ValidateAsync(fixture.Revision, fixture.Scope, receipt.ContentSha256))
            .Should().ThrowAsync<SecurityMasterImpactPreviewException>().Where(error => error.Code == "ImpactDependenciesChanged");
    }

    [Fact]
    public async Task AnotherCompanyWithinTheTenant_CannotReadFundImpact()
    {
        using var fixture = new Fixture();
        await FluentActions.Invoking(() => fixture.Service.CreateAsync(fixture.Current.SecurityId, fixture.Revision.RevisionId,
            fixture.Scope with { CompanyId = "foreign-company" }, DateTimeOffset.Parse("2026-08-01T00:00:00Z")))
            .Should().ThrowAsync<SecurityMasterImpactPreviewException>().Where(error => error.Code == "ImpactScopeDenied");
    }

    [Fact]
    public async Task FinalizationWithoutAuthoritativeDependencyGuard_Refuses()
    {
        using var fixture = new Fixture();
        await FluentActions.Invoking(() => fixture.Service.AcquireFinalizationLeaseAsync(fixture.Revision, fixture.Scope))
            .Should().ThrowAsync<SecurityMasterImpactPreviewException>().Where(error => error.Code == "ImpactGuardUnavailable");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "impact-tests-" + Guid.NewGuid().ToString("N"));
        internal readonly ISecurityMasterStore Securities = Substitute.For<ISecurityMasterStore>();
        internal readonly InMemorySecurityMasterRevisionStore Revisions = new();
        internal readonly SecurityMasterImpactScope Scope = new("tenant-impact");
        internal readonly FileSecurityMasterImpactPreviewStore Store;
        internal readonly SecurityMasterImpactPreviewService Service;
        internal readonly SecurityMasterRevisionRecord Revision;
        internal SecurityProjectionRecord Current;
        internal LedgerAccountingPeriod Period = new(Guid.NewGuid(), Guid.NewGuid(), 2026, 8, "2026-08",
            new(2026, 8, 1), new(2026, 8, 31), "Open", DateTimeOffset.Parse("2026-01-01T00:00:00Z"), null, 1);
        internal Fixture(bool withLedgerDependencies = false)
        {
            Current = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("FxSpot", JsonSerializer.SerializeToElement(new { termsFormat = SecurityTermsFormats.Flat, baseCurrency = "EUR", quoteCurrency = "USD" }));
            Securities.GetProjectionAsync(Current.SecurityId, Arg.Any<CancellationToken>()).Returns(_ => Current);
            Revision = Revisions.CreateDraftAsync(Current.SecurityId, "maker", "assetSpecificTerms.baseCurrency",
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"), "Source evidence", fundProfileId: "fund-impact", fieldValue: new("GBP")).GetAwaiter().GetResult();
            Store = new(_directory);
            var ownership = Substitute.For<IFundProfileTenancyRegistry>();
            ownership.IsAccessibleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(true);
            ownership.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new FundProfileOwnership("fund-impact", Scope.TenantId, Scope.CompanyId));
            var workbench = Substitute.For<ISecurityMasterWorkbenchQueryService>();
            var books = Substitute.For<IAffectedLedgerBookResolver>();
            ILedgerJournalStore? ledger = null;
            if (withLedgerDependencies)
            {
                var impact = new SecurityMasterDownstreamImpactDto("fund-impact", true, SecurityMasterImpactSeverity.Unknown,
                    "Unknown", "", "", "", "", 0, 0, 0, 0, 0, []);
                workbench.GetTrustSnapshotAsync(Current.SecurityId, "fund-impact", Arg.Any<CancellationToken>())
                    .Returns(new SecurityMasterTrustSnapshotDto(Current.SecurityId, null!, null!, null!, null!, [], [], impact, [], [], [], DateTimeOffset.UtcNow));
                books.ResolveWithEvidenceAsync(Arg.Any<SecurityMasterDownstreamImpactDto>(), Arg.Any<CancellationToken>())
                    .Returns(new SecurityAffectedLedgerBooksResult(SecurityImpactCompleteness.Partial, [Period.LedgerBookId!.Value], "Conservative test books", []));
                ledger = Substitute.For<ILedgerJournalStore>();
                ledger.ListPeriodsAsync(Period.LedgerBookId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                    .Returns(_ => new[] { Period });
                ledger.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<LedgerJournalEntryRecord>());
            }
            Service = new(Revisions, Securities, workbench, books, Substitute.For<ISecurityMasterCashFlowService>(), Store, ownership, ledger);
        }
        internal Task<SecurityMasterImpactPreviewReceipt> CreateAsync() => Service.CreateAsync(Current.SecurityId, Revision.RevisionId,
            Scope, DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}
