using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Meridian.Application.Composition;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Reporting;

public sealed class ReportingRunReviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Book = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Period = "22222222-2222-2222-2222-222222222222";
    private static readonly ReportingOperationalScope Scope = new("tenant", "org", "company", "fund", Book.ToString(), Period);
    private static readonly ReportingGovernanceCallerContext Caller = new("reviewer", "tenant", "company",
        UserPermission.ApproveReporting, ReportingCommandOrigin.HumanOperator, "review-test");

    [Fact]
    public void Build_RetainsExactLineDeltaSourceAndParametersWithoutInventedMateriality()
    {
        var prior = Run("prior", 1, GovernedReportingState.Approved);
        var current = Run("current", 2, GovernedReportingState.InReview) with { TemplateVersion = "2" };
        var result = ReportingRunReviewEngine.Build(current, Manifest(current, "100.02"), prior, Manifest(prior, "100.01")).Review;
        result.Lines.Should().ContainSingle().Which.Delta.Should().Be("0.01");
        result.SourceChanges.Should().ContainSingle(c => c.Key == "entry · netAmount" && c.Previous == "100.01" && c.Current == "100.02");
        result.Parameters.Should().Contain(c => c.Key == "templateVersion" && c.Previous == "1" && c.Current == "2");
        result.MaterialityOutcome.Should().Be("NotAssessed");
        result.MaterialChangeCount.Should().BeNull();
        result.ReviewId.Should().HaveLength(64);
    }

    [Fact]
    public void Build_ZeroBaselineHasNoPercentageAndMissingEvidenceIsExplicit()
    {
        var prior = Run("prior", 1, GovernedReportingState.Approved);
        var current = Run("current", 2, GovernedReportingState.InReview);
        var result = ReportingRunReviewEngine.Build(current, Manifest(current, "10") with { CertifiedDatasetRows = default },
            prior, Manifest(prior, "0")).Review;
        result.Lines.Should().ContainSingle().Which.Percent.Should().BeNull();
        result.UnavailableReasons.Should().Contain(r => r.Contains("Dataset revisions unavailable"));
        result.SourceChanges.Should().BeEmpty();
    }

    [Fact]
    public void Build_DuplicateRowsAreUnavailableRatherThanComparedUsingFirstRow()
    {
        var prior = Run("prior", 1, GovernedReportingState.Approved);
        var current = Run("current", 2, GovernedReportingState.InReview);
        var manifest = Manifest(current, "10");
        manifest = manifest with
        {
            RenderedReportWriterGrids = [manifest.RenderedReportWriterGrids[0] with {
            Rows = [manifest.RenderedReportWriterGrids[0].Rows[0], manifest.RenderedReportWriterGrids[0].Rows[0]] }]
        };
        var result = ReportingRunReviewEngine.Build(current, manifest, prior, Manifest(prior, "0")).Review;
        result.Lines.Should().BeEmpty();
        result.UnavailableReasons.Should().Contain(r => r.Contains("Duplicate row key"));
    }

    [Fact]
    public void Comparable_RejectsOtherPeriodCurrencyScopeAndUnapprovedBaseline()
    {
        var prior = Run("prior", 1, GovernedReportingState.Approved);
        var current = Run("current", 2, GovernedReportingState.InReview);
        var pm = Manifest(prior, "1");
        var cm = Manifest(current, "2");
        ReportingRunReviewEngine.Comparable(current, cm, prior, pm).Should().BeTrue();
        ReportingRunReviewEngine.Comparable(current, cm, prior with { GovernanceState = GovernedReportingState.Draft }, pm).Should().BeFalse();
        ReportingRunReviewEngine.Comparable(current, cm, prior with { Scope = Scope with { CompanyId = "other" } }, pm).Should().BeFalse();
        ReportingRunReviewEngine.Comparable(current, cm, prior, pm with { AsOfDate = new(2026, 4, 30) }).Should().BeFalse();
        ReportingRunReviewEngine.Comparable(current, cm, prior, pm with { ResolvedParameters = pm.ResolvedParameters! with { PresentationCurrency = "EUR" } }).Should().BeFalse();
    }

    [Fact]
    public async Task Service_DefaultsToLatestApprovedComparableRevisionAndRejectsStaleReview()
    {
        var prior = CertifiedRun(Run("prior", 1, GovernedReportingState.Released), "1");
        var current = CertifiedRun(Run("current", 4, GovernedReportingState.InReview), "4");
        var draft = CertifiedRun(Run("draft", 3, GovernedReportingState.Draft), "3");
        var approved = CertifiedRun(Run("approved", 2, GovernedReportingState.Approved), "2");
        var coordinator = Substitute.For<IReportingGovernanceEndpointCoordinator>();
        coordinator.GetAsync("current", Caller, Arg.Any<CancellationToken>()).Returns(current);
        coordinator.GetSeriesHistoryAsync("series", Caller, Arg.Any<CancellationToken>())
            .Returns(new ReportingGovernanceSeriesHistory("series", [prior, current, draft, approved], []));
        var runs = Substitute.For<IReportingRunStore>();
        foreach (var run in new[] { prior, current, draft, approved })
            runs.GetManifest("tenant", run.RunId).Returns(Manifest(run, run.Revision.ToString()));
        var artifacts = Substitute.For<IReportingArtifactStore>();
        var service = new ReportingRunReviewService(runs, artifacts);
        var review = await service.PrepareAsync(coordinator, "current", Caller);
        review.Review.BaselineRunId.Should().Be("approved");
        var stale = () => service.VerifyAndRetainAsync(coordinator, "current", current.Version + 1, review.Review.ReviewId, Caller);
        await stale.Should().ThrowAsync<ReportingGovernanceConcurrencyException>();
        await artifacts.DidNotReceive().StoreAsync(Arg.Any<ReportingArtifactWriteRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Build_NoBaselineIsHonestAndEvidenceChangesWithVersion()
    {
        var current = CertifiedRun(Run("current", 1, GovernedReportingState.InReview), "1");
        var manifest = Manifest(current, "1");
        var first = ReportingRunReviewEngine.Build(current, manifest, null, null).Review;
        var second = ReportingRunReviewEngine.Build(current with { Version = 2 }, manifest, null, null).Review;
        first.BaselineRunId.Should().BeNull();
        first.UnavailableReasons.Should().Contain(r => r.Contains("No prior approved comparable run"));
        second.ReviewId.Should().NotBe(first.ReviewId);
    }

    [Fact]
    public async Task Service_RetainsVerifiedEvidenceAndReplaysAfterSourceRemoval()
    {
        var current = CertifiedRun(Run("current", 1, GovernedReportingState.InReview), "1");
        var coordinator = Substitute.For<IReportingGovernanceEndpointCoordinator>();
        coordinator.GetAsync("current", Caller, Arg.Any<CancellationToken>()).Returns(current);
        coordinator.GetSeriesHistoryAsync("series", Caller, Arg.Any<CancellationToken>())
            .Returns(new ReportingGovernanceSeriesHistory("series", [current], []));
        var runs = Substitute.For<IReportingRunStore>();
        runs.GetManifest("tenant", "current").Returns(Manifest(current, "1"));
        var artifacts = new MemoryArtifacts();
        var service = new ReportingRunReviewService(runs, artifacts);
        var review = (await service.PrepareAsync(coordinator, "current", Caller)).Review;
        var evidence = await service.VerifyAndRetainAsync(coordinator, "current", current.Version, review.ReviewId, Caller);
        evidence.Should().Contain($"review-evidence:{review.ReviewId}");
        runs.GetManifest("tenant", "current").Returns((ReportingOutputManifest?)null);
        var replay = await service.ReadAsync(coordinator, "current", review.ReviewId, Caller);
        replay.Should().BeEquivalentTo(review);
        artifacts.CorruptRead = true;
        var corrupt = () => service.ReadAsync(coordinator, "current", review.ReviewId, Caller);
        await corrupt.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task Service_RejectsCrossScopeManifestBeforeCreatingReviewEvidence()
    {
        var current = CertifiedRun(Run("current", 1, GovernedReportingState.InReview), "1");
        var coordinator = Substitute.For<IReportingGovernanceEndpointCoordinator>();
        coordinator.GetAsync("current", Caller, Arg.Any<CancellationToken>()).Returns(current);
        var runs = Substitute.For<IReportingRunStore>();
        runs.GetManifest("tenant", "current").Returns(Manifest(current, "1") with { OperationalScope = Scope with { CompanyId = "other" } });
        var service = new ReportingRunReviewService(runs, new MemoryArtifacts());
        var invalid = () => service.PrepareAsync(coordinator, "current", Caller);
        await invalid.Should().ThrowAsync<ReportingGovernanceException>();
    }

    [Theory]
    [InlineData("run")]
    [InlineData("template")]
    public async Task Service_RejectsIncorrectManifestIdentityBeforeComparing(string changedIdentity)
    {
        var current = CertifiedRun(Run("current", 1, GovernedReportingState.InReview), "1");
        var coordinator = Substitute.For<IReportingGovernanceEndpointCoordinator>();
        coordinator.GetAsync("current", Caller, Arg.Any<CancellationToken>()).Returns(current);
        var manifest = Manifest(current, "1");
        manifest = changedIdentity == "run" ? manifest with { RunId = "another-run" } : manifest with { TemplateId = "another-template" };
        var runs = Substitute.For<IReportingRunStore>();
        runs.GetManifest("tenant", "current").Returns(manifest);
        var service = new ReportingRunReviewService(runs, new MemoryArtifacts());
        var invalid = () => service.PrepareAsync(coordinator, "current", Caller);
        await invalid.Should().ThrowAsync<ReportingGovernanceException>().WithMessage("*identity, template*");
    }

    [Fact]
    public async Task Service_RejectsTamperedCertifiedRowsWithUnchangedDeclaredSnapshotHash()
    {
        var current = CertifiedRun(Run("current", 1, GovernedReportingState.InReview), "1");
        var coordinator = Substitute.For<IReportingGovernanceEndpointCoordinator>();
        coordinator.GetAsync("current", Caller, Arg.Any<CancellationToken>()).Returns(current);
        var manifest = Manifest(current, "1");
        ReportingCertifiedManifestValidation.Validate(manifest);
        manifest = manifest with { CertifiedDatasetRows = [new Dictionary<string, string> { ["entryId"] = "entry", ["netAmount"] = "999" }] };
        var runs = Substitute.For<IReportingRunStore>();
        runs.GetManifest("tenant", "current").Returns(manifest);
        var invalid = () => new ReportingRunReviewService(runs, new MemoryArtifacts()).PrepareAsync(coordinator, "current", Caller);
        await invalid.Should().ThrowAsync<InvalidDataException>().WithMessage("*snapshot hash*");
    }

    [Fact]
    public void Build_DecimalOverflowIsUnavailableWithoutDroppingOtherSections()
    {
        var prior = Run("prior", 1, GovernedReportingState.Approved);
        var current = Run("current", 2, GovernedReportingState.InReview);
        var large = decimal.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var result = ReportingRunReviewEngine.Build(current, Manifest(current, large), prior, Manifest(prior, $"-{large}")).Review;
        result.Lines.Should().BeEmpty();
        result.UnavailableReasons.Should().Contain(r => r.Contains("variance exceeds supported decimal range"));
        var tiny = ReportingRunReviewEngine.Build(current, Manifest(current, large), prior, Manifest(prior, "0.0000000000000000000000000001")).Review;
        tiny.Lines.Should().ContainSingle().Which.Percent.Should().BeNull();
        tiny.UnavailableReasons.Should().Contain(r => r.Contains("percentage exceeds supported decimal range"));
    }

    [Theory]
    [InlineData("110", "10", "WithinTolerance", 0)]
    [InlineData("110.01", "10", "MaterialException", 1)]
    [InlineData("89.99", "10", "MaterialException", 1)]
    [InlineData("100", "0", "WithinTolerance", 0)]
    [InlineData("100.00000001", "0.000000005", "MaterialException", 1)]
    public void Materiality_AbsoluteThresholdsUseExactDecimalsAndStrictBoundary(string currentAmount,
        string tolerance, string outcome, int count)
    {
        var prior = Run("prior", 1, GovernedReportingState.Approved);
        var current = Run("current", 2, GovernedReportingState.InReview);
        var threshold = decimal.Parse(tolerance, System.Globalization.CultureInfo.InvariantCulture);
        var result = ReportingRunReviewEngine.Build(current, Manifest(current, currentAmount), prior, Manifest(prior, "100"),
            Policy(current, threshold, null)).Review;
        result.MaterialityOutcome.Should().Be(outcome);
        result.MaterialChangeCount.Should().Be(count);
        result.MaterialityPolicy!.Version.Should().Be("1");
        if (currentAmount == "100.00000001")
            result.Lines.Should().ContainSingle().Which.Delta.Should().Be("0.00000001");
    }

    [Theory]
    [InlineData("100", "110", "WithinTolerance", 0)]
    [InlineData("100", "110.01", "MaterialException", 1)]
    [InlineData("-100", "-110.01", "MaterialException", 1)]
    [InlineData("0", "10", "NotAssessed", null)]
    [InlineData("0", "0", "NotAssessed", null)]
    public void Materiality_RelativeThresholdsRequireNonzeroPrior(string priorAmount, string currentAmount,
        string outcome, int? count)
    {
        var prior = Run("prior", 1, GovernedReportingState.Approved);
        var current = Run("current", 2, GovernedReportingState.InReview);
        var result = ReportingRunReviewEngine.Build(current, Manifest(current, currentAmount), prior, Manifest(prior, priorAmount),
            Policy(current, null, 10m)).Review;
        result.MaterialityOutcome.Should().Be(outcome);
        result.MaterialChangeCount.Should().Be(count);
    }

    [Fact]
    public void Materiality_RelativeComparisonDoesNotRoundAwayABreachAtDecimalPrecision()
    {
        var prior = Run("prior", 1, GovernedReportingState.Approved);
        var current = Run("current", 2, GovernedReportingState.InReview);
        var result = ReportingRunReviewEngine.Build(current, Manifest(current, "4"), prior, Manifest(prior, "3"),
            Policy(current, null, 33.33333333333333333333333333m)).Review;
        result.MaterialityOutcome.Should().Be("MaterialException");
        result.MaterialChangeCount.Should().Be(1);
    }

    [Fact]
    public void Materiality_NullThresholdsAndMissingInputsNeverProduceZeroMaterialCount()
    {
        var prior = Run("prior", 1, GovernedReportingState.Approved);
        var current = Run("current", 2, GovernedReportingState.InReview);
        var result = ReportingRunReviewEngine.Build(current, Manifest(current, "110"), prior, Manifest(prior, "100"),
            Policy(current, null, null)).Review;
        result.MaterialityOutcome.Should().Be("NotAssessed");
        result.MaterialChangeCount.Should().BeNull();
        var unchangedMissing = ReportingRunReviewEngine.Build(current, Manifest(current, "not-recorded"), prior,
            Manifest(prior, "not-recorded"), Policy(current, 10m, null)).Review;
        unchangedMissing.MaterialityOutcome.Should().Be("NotAssessed");
        unchangedMissing.MaterialChangeCount.Should().BeNull();
    }

    [Fact]
    public async Task Materiality_ConfigurationHasNoDefaultsAndMatchesExactPolicyBindings()
    {
        var current = Run("current", 2, GovernedReportingState.InReview);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var provider = new ConfiguredReportingReviewMaterialityPolicyProvider(new CompositionConfiguration(configuration));
        (await provider.ResolveAsync(current.Scope, current.TemplateId, current.TemplateVersion, "USD")).Should().BeNull();
        configuration[ConfiguredReportingReviewMaterialityPolicyProvider.ConfigurationKey] = PolicyJson(Policy(current, 10m, null));
        var selected = await provider.ResolveAsync(current.Scope, current.TemplateId, current.TemplateVersion, "USD");
        selected!.PolicyId.Should().Be("approved-policy");
        (await provider.ResolveAsync(current.Scope, current.TemplateId, current.TemplateVersion, "EUR")).Should().BeNull();
        (await provider.ResolveAsync(current.Scope with { TenantId = "other" }, current.TemplateId, current.TemplateVersion, "USD")).Should().BeNull();
        (await provider.ResolveAsync(current.Scope, current.TemplateId, "other-version", "USD")).Should().BeNull();
    }

    [Fact]
    public async Task Materiality_ConfigurationRejectsNumericTokensThatCouldRoundApprovedThresholds()
    {
        var current = Run("current", 2, GovernedReportingState.InReview);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var json = PolicyJson(Policy(current, 0.1m, null));
        configuration[ConfiguredReportingReviewMaterialityPolicyProvider.ConfigurationKey] =
            json.Replace("\"absoluteVariance\":\"0.1\"", "\"absoluteVariance\":0.10000000000000000000000000001", StringComparison.Ordinal);
        configuration[ConfiguredReportingReviewMaterialityPolicyProvider.ConfigurationKey].Should().NotBe(json);
        var provider = new ConfiguredReportingReviewMaterialityPolicyProvider(new CompositionConfiguration(configuration));
        var invalid = () => provider.ResolveAsync(current.Scope, current.TemplateId, current.TemplateVersion, "USD").AsTask();
        await invalid.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("0.10000000000000000000000000001", false)]
    [InlineData("0.00000000000000000000000000001", false)]
    [InlineData("+000.10000000000000000000000000000", true)]
    [InlineData("-000.00000000000000000000000000000", true)]
    public async Task Materiality_ConfigurationNormalizesOnlyValuePreservingDecimalStrings(string threshold, bool valid)
    {
        var current = Run("current", 2, GovernedReportingState.InReview);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        configuration[ConfiguredReportingReviewMaterialityPolicyProvider.ConfigurationKey] =
            PolicyJson(Policy(current, 0.1m, null)).Replace("\"absoluteVariance\":\"0.1\"", $"\"absoluteVariance\":\"{threshold}\"", StringComparison.Ordinal);
        var provider = new ConfiguredReportingReviewMaterialityPolicyProvider(new CompositionConfiguration(configuration));
        var resolve = () => provider.ResolveAsync(current.Scope, current.TemplateId, current.TemplateVersion, "USD").AsTask();
        if (valid)
        {
            var result = await resolve();
            result!.Metrics[0].AbsoluteVariance.Should().Be(threshold.StartsWith('-') ? 0m : 0.1m);
        }
        else
            await resolve.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Materiality_PolicyVersionChangesInvalidateReviewedApprovalAndReceiptNamesVersion()
    {
        var prior = CertifiedRun(Run("prior", 1, GovernedReportingState.Approved), "100");
        var current = CertifiedRun(Run("current", 2, GovernedReportingState.InReview), "110");
        var coordinator = Substitute.For<IReportingGovernanceEndpointCoordinator>();
        coordinator.GetAsync("current", Caller, Arg.Any<CancellationToken>()).Returns(current);
        coordinator.GetAsync("prior", Caller, Arg.Any<CancellationToken>()).Returns(prior);
        coordinator.GetSeriesHistoryAsync("series", Caller, Arg.Any<CancellationToken>())
            .Returns(new ReportingGovernanceSeriesHistory("series", [prior, current], []));
        var runs = Substitute.For<IReportingRunStore>();
        runs.GetManifest("tenant", "current").Returns(Manifest(current, "110"));
        runs.GetManifest("tenant", "prior").Returns(Manifest(prior, "100"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var provider = new ConfiguredReportingReviewMaterialityPolicyProvider(new CompositionConfiguration(configuration));
        var policy = Policy(current, 10m, null);
        configuration[ConfiguredReportingReviewMaterialityPolicyProvider.ConfigurationKey] = PolicyJson(policy);
        var service = new ReportingRunReviewService(runs, new MemoryArtifacts(), provider);
        var reviewed = (await service.PrepareAsync(coordinator, "current", Caller)).Review;
        reviewed.MaterialChangeCount.Should().Be(0);
        configuration[ConfiguredReportingReviewMaterialityPolicyProvider.ConfigurationKey] = PolicyJson(policy with { Metrics = [new("income", "amount", 5m)] });
        var stale = () => service.VerifyAndRetainAsync(coordinator, "current", current.Version, reviewed.ReviewId, Caller);
        await stale.Should().ThrowAsync<ReportingGovernanceConcurrencyException>();
        configuration[ConfiguredReportingReviewMaterialityPolicyProvider.ConfigurationKey] = PolicyJson(policy with { Version = "2", Metrics = [new("income", "amount", 5m)] });
        await stale.Should().ThrowAsync<ReportingGovernanceConcurrencyException>();
        var updated = (await service.PrepareAsync(coordinator, "current", Caller)).Review;
        updated.ReviewId.Should().NotBe(reviewed.ReviewId);
        updated.MaterialChangeCount.Should().Be(1);
        var receipt = await service.VerifyAndRetainAsync(coordinator, "current", current.Version, updated.ReviewId, Caller);
        receipt.Should().Contain("policy:approved-policy@2").And.Contain("materiality:MaterialException");
    }

    private static ReportingReviewMaterialityPolicy Policy(GovernedReportingRun run, decimal? absolute, decimal? relative) =>
        new("approved-policy", "1", run.Scope, run.TemplateId, run.TemplateVersion, "USD", [new("income", "amount", absolute, relative)]);
    private static string PolicyJson(ReportingReviewMaterialityPolicy policy) =>
        JsonSerializer.Serialize<IReadOnlyList<ReportingReviewMaterialityPolicy>>([policy], ReportingRunReviewJsonContext.Default.MaterialityPolicies);

    private sealed class MemoryArtifacts : IReportingArtifactStore
    {
        private readonly Dictionary<ReportingArtifactIdentity, byte[]> content = [];
        public bool CorruptRead { get; set; }
        public Task<ReportingArtifactWriteResult> StoreAsync(ReportingArtifactWriteRequest request, CancellationToken ct = default)
        {
            var bytes = request.Content.ToArray();
            var identity = new ReportingArtifactIdentity(request.TenantId, Sha256Digest.Compute(bytes));
            content[identity] = bytes;
            return Task.FromResult(new ReportingArtifactWriteResult(identity, bytes.LongLength, Now, false));
        }
        public Task<ReportingArtifactReadResult> ReadAsync(ReportingArtifactIdentity identity, CancellationToken ct = default)
        {
            var bytes = CorruptRead ? new byte[] { 0 } : content[identity];
            return Task.FromResult(new ReportingArtifactReadResult(identity, bytes.LongLength, Now, bytes));
        }
    }

    private static GovernedReportingRun Run(string id, int revision, GovernedReportingState state)
    {
        var access = new ReportingAccessScope("policy", "1", ReportingGovernanceAccessMode.CompanyWide, null, false, [], Sha256Digest.ComputeUtf8("policy"));
        var snapshot = new ReportingCertifiedSnapshotScope("tenant", "org", "company", "fund", Book.ToString(), Period, $"snapshot-{id}", Sha256Digest.ComputeUtf8(id), "reconciled", Now);
        var authority = new ReportingAuthorityScope("maker", "tenant", "org", "company", [], ReportingCommandOrigin.HumanOperator, "test");
        return new(id, "series", revision, "template", "1", Scope, access, snapshot, authority, Now, null,
            GovernedReportingExecutionState.Succeeded, state, revision, null, null, null, []);
    }

    private static GovernedReportingRun CertifiedRun(GovernedReportingRun run, string amount)
        => run with { Snapshot = Manifest(run, amount).CertifiedSnapshot! };

    private static ReportingOutputManifest Manifest(GovernedReportingRun run, string amount)
    {
        var parameters = new ReportingRunParametersDto(new("fund"), Period, new(2026, 5, 31), new(Book), ReportingAccountingBasisDto.Gaap,
            "USD", ReportingConsolidationLevelDto.Fund, ReportingOutputFormatDto.Pdf, ReportingFinalityDto.Final, true, true);
        var template = new VersionedReportTemplateIdDto(run.TemplateId, int.Parse(run.TemplateVersion));
        var parameterJson = ReportingCanonicalParameterSerializer.Serialize(parameters);
        var sourceHash = Sha256Digest.ComputeUtf8($"source-{run.RunId}");
        var source = new ReportingAuthoritativeSourceCheckpoint("ledger", "source", "tenant", "org", "company", "fund",
            Book.ToString(), Period, "Gaap", new(2026, 5, 31), Now, 1, 1, 1, $"source-{run.RunId}", sourceHash, Now,
            [$"reporting-source-checkpoint:source-{run.RunId}:{sourceHash}"]);
        var readiness = new ReportingRunReadinessDto("ready", Now, template, parameters, ReportingRunReadinessStatusDto.Ready,
            true, true, [new("source", "Source", ReportingRunReadinessStatusDto.Ready, "Ready", 0, true, true,
                EvidenceReferences: ["source-evidence"])], [], Sha256Digest.ComputeUtf8("ready"));
        var snapshot = run.Snapshot with
        {
            SourceCheckpointId = source.CheckpointId,
            SourceCheckpointHash = sourceHash,
            ReconciliationCheckpointHash = Sha256Digest.ComputeUtf8("reconciled"),
            ParametersCanonicalJson = parameterJson,
            ParametersHash = Sha256Digest.ComputeUtf8(parameterJson)
        };
        var manifest = new ReportingOutputManifest(run.RunId, run.TemplateId, new(2026, 5, 31), ReportingRunStatus.Draft, [], [], 1, ReportingRunTrigger.AdHoc,
            RenderedReportWriterGrids: [new("income", "Income", ReportWriterGridKindDto.Pivot,
                [new("amount", "Amount", "metric")], [new("coupon", new Dictionary<string, string> { ["amount"] = amount })], [])],
            ResolvedTemplate: template, ResolvedParameters: parameters, Readiness: readiness,
            OperationalScope: run.Scope, ImmutableAccessScope: run.Access, CertifiedSnapshot: snapshot, AuthoritativeSource: source,
            CertifiedDatasetRows: [new Dictionary<string, string> { ["entryId"] = "entry", ["netAmount"] = amount }]);
        return manifest with { CertifiedSnapshot = snapshot with { SnapshotHash = ReportingCertifiedManifestValidation.ComputeSnapshotHash(manifest) } };
    }
}
