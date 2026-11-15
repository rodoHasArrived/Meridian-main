using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.SecurityMaster;

/// <summary>Real Security Master mutations and per-record approval flow; ledger/impact inputs are synthetic.</summary>
[Trait("Category", "Integration")]
public sealed class SecurityRateRepairWorkflowPostgresTests(SecurityMasterDatabaseFixture fixture)
    : IClassFixture<SecurityMasterDatabaseFixture>
{
    [SecurityMasterDatabaseFact]
    public async Task MixedStaleCohort_RepairsRemainingRecordThroughIndependentReceiptWithoutChangingItsNumber()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = timeout.Token;
        var options = fixture.Options;
        var events = new PostgresSecurityMasterEventStore(options);
        var snapshots = new PostgresSecurityMasterSnapshotStore(options);
        var securities = new PostgresSecurityMasterStore(options);
        var revisions = new PostgresSecurityMasterRevisionStore(options);
        var batches = new PostgresSecurityRateRepairStore(options);
        using var canonical = new SecurityMasterService(events, snapshots, securities, new(events, snapshots), options,
            NullLogger<SecurityMasterService>.Instance, canonicalWriteScope: new PostgresSecurityMasterCanonicalWriteScope(options), revisionStore: revisions);
        var effective = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        JsonElement Terms(decimal coupon) => SecurityTermsEnvelope.StampDeclaredFormat(JsonSerializer.SerializeToElement(new
        {
            issuerName = "Synthetic issuer",
            maturity = "2027-01-01",
            issueDate = "2026-01-01",
            principal = 1000m,
            couponRate = coupon,
            paymentFrequency = "AtMaturity",
            dayCount = "Actual365Fixed"
        }));
        async Task<SecurityDetailDto> Create(decimal coupon) => await canonical.CreateAsync(new(Guid.NewGuid(), "CertificateOfDeposit",
            JsonSerializer.SerializeToElement(new { displayName = "Synthetic repair CD", currency = "USD" }), Terms(coupon),
            [new(SecurityIdentifierKind.InternalCode, "REPAIR-" + Guid.NewGuid().ToString("N"), true, effective)],
            effective, "synthetic-vendor", "maker", null, "Synthetic source definition"), ct);
        var stale = await Create(5.25m);
        var repaired = await Create(0.875m);
        var bookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var ledger = new Mock<ILedgerJournalStore>();
        ledger.Setup(ledger => ledger.GetLedgerBookAsync(bookId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new LedgerBookRecord(bookId, "synthetic-fund", Guid.NewGuid(), (FundStructureNodeKindDto)0, "Book", "USD", effective, effective));
        ledger.Setup(ledger => ledger.GetPeriodAsync(periodId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new LedgerAccountingPeriod(periodId, bookId, 2026, 10, "October", new(2026, 10, 1), new(2026, 10, 31), "Open", effective, null, 1));
        var scope = new SecurityMasterImpactScope("repair-tenant", "repair-company");
        var scopeAccessor = new Mock<ISecurityMasterImpactScopeAccessor>();
        scopeAccessor.Setup(scope => scope.GetRequired()).Returns(scope);
        var authority = new SecurityRateRepairWorkflowAuthority(revisions, batches, scopeAccessor.Object, ledger.Object);
        var workflows = new OperationsContinuityWorkflowService(new InMemoryOperationsContinuityRepository(new OperationsStatusDerivationService()),
            new InMemoryOperationsWorkflowAuditStore(), new OperationsStatusDerivationService(), revisionWorkflowAuthority: authority);
        var impact = new Mock<ISecurityMasterImpactPreviewService>();
        var retained = new Dictionary<string, SecurityMasterImpactPreviewReceipt>();
        impact.Setup(impact => impact.CreateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), scope, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid securityId, Guid revisionId, SecurityMasterImpactScope _, DateTimeOffset asOf, CancellationToken token) =>
            {
                var revision = (await revisions.GetAsync(revisionId, token))!;
                var projection = (await securities.GetProjectionAsync(securityId, token))!;
                var before = SecurityMasterMapping.ToDetail(projection);
                var after = before with { AssetSpecificTerms = new SecurityMasterProposedTermsBuilder().Build(projection, revision) };
                var digest = Sha256Digest.ComputeUtf8(revisionId.ToString("D"));
                var receipt = new SecurityMasterImpactPreviewReceipt("synthetic", scope, securityId, revisionId, before.Version,
                    "", "", before, after, asOf, asOf,
                    new(SecurityImpactCompleteness.Complete, [bookId], "The isolated fixture has exactly one declared book.", []),
                    [new(bookId, periodId, 1, new(2026, 10, 1), new(2026, 10, 31), "Open")],
                    new(SecurityImpactCompleteness.Complete, [], "The isolated fixture has no released reports.", []), null, null, [], "", digest)
                {
                    FundProfileId = "synthetic-fund"
                };
                retained[digest] = receipt;
                return receipt;
            });
        impact.Setup(impact => impact.GetAsync(scope, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SecurityMasterImpactScope _, string digest, CancellationToken _) =>
                retained.GetValueOrDefault(digest));
        impact.Setup(impact => impact.ValidateAsync(It.IsAny<SecurityMasterRevisionRecord>(), scope, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SecurityMasterRevisionRecord revision, SecurityMasterImpactScope _, string digest, bool _, CancellationToken _) =>
            {
                retained[digest].RevisionId.Should().Be(revision.RevisionId);
                return retained[digest];
            });
        impact.Setup(impact => impact.AcquireFinalizationLeaseAsync(It.IsAny<SecurityMasterRevisionRecord>(), scope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoopLease());
        var restatement = new Mock<IPeriodAwareRestatementResolver>();
        restatement.Setup(resolver => resolver.ResolveAsync(It.IsAny<SecurityMasterRevisionPublishedEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityMasterRestatementDecision(false, []));
        var books = new Mock<IAffectedLedgerBookResolver>();
        books.Setup(resolver => resolver.ResolveAsync(It.IsAny<SecurityMasterDownstreamImpactDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());
        var workbenchQuery = new Mock<ISecurityMasterWorkbenchQueryService>();
        workbenchQuery.Setup(query => query.GetInstrumentPassportAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid securityId, string? _, CancellationToken _) => new InstrumentPassportDto(
                securityId, Identity: null!, EconomicDefinition: new SecurityMasterEconomicDefinitionDrillInDto(
                    securityId, "CertificateOfDeposit", "USD", 1, effective, null,
                    null, null, null, null, null, null, null, null, null),
                IdentifierSummary: null!, ProviderMappings: [], LifecycleEvents: [], CorporateActions: [],
                Pricing: null!, Usage: null!, TrustPosture: null!, RetrievedAtUtc: effective));
        var workbench = new SecurityMasterWorkbenchCommandService(events, new PostgresOperatorOverridesStore(options),
            Mock.Of<ISecurityMasterConflictAuthorityPolicy>(), Mock.Of<ISecurityMasterConflictService>(), workbenchQuery.Object,
            workflows, revisions, restatement.Object, books.Object,
            [new ApprovedFieldEditCanonicalMergeHandler(revisions, NullLogger<ApprovedFieldEditCanonicalMergeHandler>.Instance, securities, canonical)],
            NullLogger<SecurityMasterWorkbenchCommandService>.Instance, projectionStore: securities, impactPreview: impact.Object, impactScope: scopeAccessor.Object);
        var evidence = new Mock<ISecurityRateRepairEvidenceResolver>();
        evidence.Setup(evidence => evidence.ResolveAsync("synthetic-evidence", scope.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityRateRepairEvidence("synthetic-evidence", new string('b', 64)));
        var service = new SecurityRateRepairService(batches, Mock.Of<ISecurityMasterQueryService>(), securities, revisions, workbench,
            workflows, evidence.Object, impact.Object, ledger.Object);
        var maker = new SecurityRateRepairActor(scope.TenantId, scope.CompanyId, "maker", true);
        var checker = maker with { Actor = "checker" };
        var created = await service.CreateAsync(new(Guid.NewGuid(), new(accountId, bookId, "synthetic-fund", periodId.ToString("D")),
            new("synthetic-vendor", "assetSpecificTerms.couponRate", "vendor-v1", "AnnualRate"),
            [new(stale.SecurityId, stale.Version), new(repaired.SecurityId, repaired.Version)], "Percent", effective,
            "synthetic-evidence", "Vendor field definition applies to the frozen roster", true), maker, ct);
        await canonical.AmendTermsAsync(new(stale.SecurityId, stale.Version, null, Terms(5.5m),
            [], [], effective, "synthetic-vendor", "another-maker", null, "Synthetic competing correction"), ct);
        var staged = await service.ExecuteAsync(created.BatchId, "Stage", new(created.Version), maker, ct);
        staged.Items.Single(item => item.SecurityId == stale.SecurityId).State.Should().Be("Conflicted");
        staged.Items.Single(item => item.SecurityId == repaired.SecurityId).State.Should().Be("Draft");
        var submitted = await service.ExecuteAsync(created.BatchId, "Submit", new(staged.Version, "checker", "Review source scope and unit"), maker, ct);
        submitted.Items.Single(item => item.SecurityId == repaired.SecurityId).ErrorCode.Should().BeNull(
            "the fixture submission should succeed: {0}", submitted.Items.Single(item => item.SecurityId == repaired.SecurityId).Error);
        await FluentActions.Invoking(() => service.ExecuteAsync(created.BatchId, "Approve", new(submitted.Version, Rationale: "Self review"), maker, ct))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        var approved = await service.ExecuteAsync(created.BatchId, "Approve", new(submitted.Version, Rationale: "Source and impact reviewed"), checker, ct);
        approved.Items.Single(item => item.SecurityId == repaired.SecurityId).State.Should().Be("Approved",
            "the fixture supplies complete synthetic impact authority; refusal was {0}: {1}",
            approved.Items.Single(item => item.SecurityId == repaired.SecurityId).ErrorCode,
            approved.Items.Single(item => item.SecurityId == repaired.SecurityId).Error);
        var published = await service.ExecuteAsync(created.BatchId, "Publish", new(approved.Version), checker, ct);
        var success = published.Items.Single(item => item.SecurityId == repaired.SecurityId);
        success.State.Should().Be("Published", "the fixture must retrieve its retained impact authority; refusal was {0}: {1}",
            success.ErrorCode, success.Error);
        success.ApprovalReceiptId.Should().NotBeNullOrWhiteSpace();
        published.Items.Single(item => item.SecurityId == stale.SecurityId).State.Should().Be("Conflicted");
        var final = (await securities.GetProjectionAsync(repaired.SecurityId, ct))!;
        SecurityTermsEnvelope.GetDeclaredFormat(final.AssetSpecificTerms).Should().Be(SecurityTermsFormats.Flat,
            "the repair must preserve the canonical document's declared family");
        final.AssetSpecificTerms.GetProperty("couponRate").GetDecimal().Should().Be(0.875m, "no magnitude-based rescaling is permitted");
        SecurityRecordRateUnit.Read(final.AssetClass, final.AssetSpecificTerms).Should().Be(RateUnit.Percent);
        var revisionFinal = (await revisions.GetAsync(success.RevisionId!.Value, ct))!;
        revisionFinal.DraftedBy.Should().Be("maker");
        revisionFinal.SubmittedBy.Should().Be("maker");
        revisionFinal.Actor.Should().Be("checker");
        revisionFinal.ReviewedImpactReceiptId.Should().Be(success.ImpactReceiptId);
    }

    private sealed class NoopLease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
}
