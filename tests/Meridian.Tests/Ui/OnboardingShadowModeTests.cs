using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Onboarding;
using Meridian.FinancialOperations.Onboarding.OpeningBalances;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Ui.Shared.Services;
using Fixture = Meridian.Tests.Ui.OnboardingWorkspaceServiceTests.Fixture;

namespace Meridian.Tests.Ui;

/// <summary>
/// W11-SHADOW-001: a shadow book runs beside the incumbent, reconciles opening balances and every period,
/// and becomes the book of record only through an independent, certificate-backed promotion decision.
/// </summary>
public sealed class OnboardingShadowModeTests
{
    private const string AdminAccount = "ADMIN-001";
    private static readonly IReadOnlyList<OnboardingOpeningAccountMappingDto> Mappings = [new(AdminAccount, Fixture.Account)];

    [Fact]
    public async Task ParallelRun_OpeningExplainedTimingBreakCertificateAndPromotion_MakesTheBookAuthoritative()
    {
        using var fixture = new Fixture();
        var harness = new Harness(fixture);
        var service = harness.Service();
        var guard = new OnboardingBookOfRecordGuard(new FileOnboardingWorkspaceStore(fixture.StorePath));
        var workspace = await service.CreateAsync(fixture.Request(), Fixture.Owner);
        (await guard.GetStatusAsync(Fixture.Tenant, Fixture.Company, fixture.Scope.LedgerBookId)).IsBookOfRecord.Should().BeTrue();

        workspace = await service.DesignateShadowAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, "Parallel run against the incumbent administrator."), Fixture.Owner);
        workspace.ShadowDesignation!.State.Should().Be(OnboardingShadowStates.Shadow);
        var shadowStatus = await guard.GetStatusAsync(Fixture.Tenant, Fixture.Company, fixture.Scope.LedgerBookId);
        shadowStatus.IsBookOfRecord.Should().BeFalse();
        shadowStatus.ShadowState.Should().Be(OnboardingShadowStates.Shadow);
        workspace.Readiness.Blockers.Should().Contain(blocker => blocker.Contains("opening balances", StringComparison.Ordinal));

        workspace = await service.ImportOpeningBalancesAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            harness.AdminPackRequest(workspace.Version), Fixture.Owner);
        var opening = workspace.Readiness.Opening!;
        opening.IsReconciled.Should().BeTrue();
        opening.SourceKind.Should().Be(OnboardingOpeningSourceKinds.AdministratorPack);
        opening.Lines.Should().HaveCount(3).And.OnlyContain(line => line.Status == "Matched");
        opening.SourceContentHash.Should().HaveLength(64);
        workspace.Comparisons.Single().IsOpening.Should().BeTrue();
        workspace.CurrentDifferences.Should().BeEmpty("opening comparisons never feed period lineage");

        // January and March reconcile; February carries a cash timing difference that reverses by March.
        fixture.Source.Set("jan", fixture.Capture(Fixture.January, "jan", 100m, 10m, 100m));
        workspace = await fixture.Compare(service, workspace, Fixture.January, "jan");
        fixture.Source.Set("feb", fixture.Capture(Fixture.February, "feb", 97m, 10m, 100m));
        workspace = await fixture.Compare(service, workspace, Fixture.February, "feb");
        fixture.Source.Set("mar", fixture.Capture(Fixture.March, "mar", 100m, 10m, 100m));
        workspace = await fixture.Compare(service, workspace, Fixture.March, "mar");
        workspace.Readiness.UnresolvedDifferenceCount.Should().Be(1);
        var february = workspace.Comparisons.Single(run => run.AsOfDate == Fixture.February && !run.IsOpening);
        var cashBreak = february.Differences.Single(row => row.Kind == "Balance");

        workspace = await service.ExplainDifferenceAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId, cashBreak.DifferenceKey,
            new(workspace.Version, february.ComparisonId, OnboardingDifferenceClassifications.Timing,
                "Wire booked by the administrator on settlement date; Meridian books on trade date.", ["evidence:wire-confirmation"],
                Fixture.March), Fixture.Owner);
        workspace.Readiness.UnresolvedDifferenceCount.Should().Be(0);
        workspace.Readiness.ExplainedDifferenceCount.Should().Be(1);
        var periods = workspace.Readiness.Periods!;
        periods.Select(period => period.AsOfDate).Should().Equal(Fixture.January, Fixture.February, Fixture.March);
        periods.Should().OnlyContain(period => period.IsClean);
        periods.Single(period => period.AsOfDate == Fixture.February).ExplainedByClassification
            .Should().ContainSingle(count => count.Classification == OnboardingDifferenceClassifications.Timing && count.Count == 1);

        var notReady = () => service.IssueCertificateAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version), Fixture.Owner);
        await notReady.Should().ThrowAsync<OnboardingValidationException>("independent review of the final revision is still required");

        workspace = await service.ReviewAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, workspace.DataRevision, "Approved", "Opening and all periods agree; timing break evidenced.",
                ["review:parallel-run"]), Fixture.Reviewer);
        workspace.Readiness.IsReady.Should().BeTrue();
        var certificate = await service.IssueCertificateAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version), Fixture.Owner);
        certificate.ContentHash.Should().Be(OnboardingWorkspaceService.HashCertificateContent(certificate.Content));
        certificate.Content.CleanPeriods.Should().Equal(Fixture.January, Fixture.February, Fixture.March);
        certificate.Content.Explanations.Should().ContainSingle();
        certificate.Content.Opening.IsReconciled.Should().BeTrue();
        workspace = (await service.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))!;
        workspace.Packets.Should().ContainSingle(packet => packet.ContentHash == certificate.Content.PacketContentHash);
        (await guard.GetStatusAsync(Fixture.Tenant, Fixture.Company, fixture.Scope.LedgerBookId)).IsBookOfRecord
            .Should().BeFalse("a certificate is evidence, not a promotion");

        var ownerPromotion = () => service.PromoteAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, certificate.CertificateId, "Cut over.", []), Fixture.Owner);
        await ownerPromotion.Should().ThrowAsync<UnauthorizedAccessException>();

        workspace = await service.PromoteAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, certificate.CertificateId, "Zero unexplained breaks across the parallel run.", []), Fixture.Reviewer);
        workspace.ShadowDesignation!.State.Should().Be(OnboardingShadowStates.Promoted);
        workspace.Promotion!.CertificateContentHash.Should().Be(certificate.ContentHash);
        workspace.ShadowAudit!.Select(entry => entry.Action).Should().Equal(
            "ShadowDesignated", "OpeningBalancesImported", "DifferenceExplained", "CertificateIssued", "ShadowPromoted");
        OnboardingWorkspaceService.VerifyShadowAudit(workspace.ShadowAudit).Should().BeTrue();
        var promoted = await guard.GetStatusAsync(Fixture.Tenant, Fixture.Company, fixture.Scope.LedgerBookId);
        promoted.IsBookOfRecord.Should().BeTrue();
        promoted.ShadowState.Should().Be(OnboardingShadowStates.Promoted);

        // Retained history, certificate and audit chain all verify after an independent reload and replay.
        var restarted = harness.Service();
        (await restarted.GetCertificateAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId, certificate.CertificateId))
            .Should().NotBeNull();
        foreach (var run in workspace.Comparisons)
            (await restarted.ReplayComparisonAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId, run.ComparisonId))!
                .ContentHash.Should().Be(run.ContentHash);
    }

    [Fact]
    public async Task Explanations_MeridianErrorNeverClears_TimingReRaisesAfterReversal_ChangedAmountsInvalidate()
    {
        using var fixture = new Fixture();
        var service = new Harness(fixture).Service();
        var workspace = await service.CreateAsync(fixture.Request(), Fixture.Owner);
        fixture.Source.Set("jan", fixture.Capture(Fixture.January, "jan", 97m, 10m, 100m));
        workspace = await fixture.Compare(service, workspace, Fixture.January, "jan");
        var january = workspace.Comparisons.Single();
        var key = january.Differences.Single(row => row.Kind == "Balance").DifferenceKey;

        workspace = await service.ExplainDifferenceAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId, key,
            new(workspace.Version, january.ComparisonId, OnboardingDifferenceClassifications.MeridianError,
                "Meridian missed a fee accrual.", ["evidence:fee"]), Fixture.Owner);
        workspace.Readiness.UnresolvedDifferenceCount.Should().Be(1, "a Meridian error must be fixed, not explained away");

        workspace = await service.ExplainDifferenceAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId, key,
            new(workspace.Version, january.ComparisonId, OnboardingDifferenceClassifications.Timing,
                "Settlement lag.", ["evidence:lag"], Fixture.January), Fixture.Owner);
        workspace.Readiness.UnresolvedDifferenceCount.Should().Be(0);

        // Still open on the same amounts after the expected reversal date: the timing explanation lapses.
        fixture.Source.Set("feb", fixture.Capture(Fixture.February, "feb", 97m, 10m, 100m));
        workspace = await fixture.Compare(service, workspace, Fixture.February, "feb");
        workspace.Readiness.UnresolvedDifferences.Should().ContainSingle(row => row.AsOfDate == Fixture.February);

        // A pricing explanation recorded against February's amounts does not carry to a changed March amount.
        var february = workspace.Comparisons.Last();
        workspace = await service.ExplainDifferenceAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId, key,
            new(workspace.Version, february.ComparisonId, OnboardingDifferenceClassifications.Pricing,
                "Administrator uses a different FX fixing.", ["evidence:fx"]), Fixture.Owner);
        fixture.Source.Set("mar", fixture.Capture(Fixture.March, "mar", 96m, 10m, 100m));
        workspace = await fixture.Compare(service, workspace, Fixture.March, "mar");
        workspace.Readiness.UnresolvedDifferences.Should().ContainSingle(row => row.AsOfDate == Fixture.March);
        workspace.Readiness.Periods!.Single(period => period.AsOfDate == Fixture.February).IsClean.Should().BeTrue();

        var resolvedExplanation = () => service.ExplainDifferenceAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId, key,
            new(workspace.Version, february.ComparisonId, OnboardingDifferenceClassifications.Timing, "No date.", ["evidence:x"]), Fixture.Owner);
        await resolvedExplanation.Should().ThrowAsync<OnboardingValidationException>("timing requires an expected reversal date");
    }

    [Fact]
    public async Task OpeningImport_ReportsBreaksAndMissingRows_AndRefusesUnmappedOrDuplicateRows()
    {
        using var fixture = new Fixture();
        var harness = new Harness(fixture);
        var service = harness.Service();
        var workspace = await service.CreateAsync(fixture.Request(), Fixture.Owner);
        var withoutDesignation = () => service.ImportOpeningBalancesAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            harness.AdminPackRequest(workspace.Version), Fixture.Owner);
        await withoutDesignation.Should().ThrowAsync<OnboardingValidationException>();
        workspace = await service.DesignateShadowAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, "Trial."), Fixture.Owner);

        var unmapped = () => service.ImportOpeningBalancesAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            harness.AdminPackRequest(workspace.Version) with { AccountMappings = [] }, Fixture.Owner);
        (await unmapped.Should().ThrowAsync<OnboardingValidationException>()).WithMessage("*ADMIN-001*");

        var duplicate = () => service.ImportOpeningBalancesAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            harness.AdminPackRequest(workspace.Version, Pack("CASH,ADMIN-001,USD,,,,100.00,2026-01-31",
                "CASH,ADMIN-001,USD,,,,5.00,2026-01-31")), Fixture.Owner);
        (await duplicate.Should().ThrowAsync<OnboardingValidationException>()).WithMessage("*cannot be netted*");

        workspace = await service.ImportOpeningBalancesAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            harness.AdminPackRequest(workspace.Version, Pack("CASH,ADMIN-001,USD,,,,95.00,2026-01-31",
                "NAV,ADMIN-001,USD,,,,,2026-01-31")), Fixture.Owner);
        var report = workspace.Readiness.Opening!;
        report.IsReconciled.Should().BeFalse();
        report.Lines.Single(line => line.Kind == "Balance").Should().Match<OnboardingOpeningBalanceLineDto>(line =>
            line.Status == "Break" && line.Difference == 5m && line.IncumbentAmount == 95m);
        report.Lines.Single(line => line.Kind == "Nav").Status.Should().Be("Missing", "an empty incumbent amount is never zero");
        // This pack supplies no positions, so Position is out of its scope: it is left to a complementary
        // opening source and reported as an uncovered kind rather than a missing line.
        report.Lines.Should().NotContain(line => line.Kind == "Position");
        report.UncoveredKinds.Should().Contain("Position");
        report.BreakCount.Should().Be(1);
        report.MissingCount.Should().Be(1);
        workspace.Readiness.Blockers.Should().Contain(blocker => blocker.Contains("opening-balance difference", StringComparison.Ordinal));
        (await service.GetOpeningReportAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))
            .Should().BeEquivalentTo(report);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("kind")]
    [InlineData("source")]
    public async Task OpeningImport_MissingCoverageOnBothSidesOrMissingSource_BlocksCertificationAfterIndependentReview(string gap)
    {
        using var fixture = new Fixture();
        var harness = new Harness(fixture);
        var request = fixture.Request();
        if (gap == "account")
        {
            request = request with { Scope = request.Scope with { AccountIds = [Fixture.Account, "d8ca0d85-d433-4bed-bbc8-98b48225cc93"] } };
        }
        var workspace = await harness.Service().CreateAsync(request, Fixture.Owner);
        var capture = await new OpeningState(fixture).CaptureOpeningAsync(workspace);
        byte[]? pack = null;
        if (gap == "kind")
        {
            capture = capture with { Observations = capture.Observations.Where(row => row.Kind != "Nav").ToArray() };
            pack = Pack("CASH,ADMIN-001,USD,,,,100,2026-01-31", "POSITION,ADMIN-001,USD,security-a,10,,,2026-01-31");
        }
        else if (gap == "source")
        {
            capture = capture with
            {
                MissingSources = [new("unverified-opening-source", "Position", Fixture.Account, "The retained opening source is incomplete.")]
            };
        }
        var payload = JsonSerializer.Serialize(new { workspace.Scope, capture.Observations, capture.MissingSources });
        capture = capture with
        {
            Snapshots = capture.Snapshots.Select(snapshot => snapshot with
            {
                PayloadJson = payload,
                ContentHash = OnboardingWorkspaceService.HashSourcePayload(payload)
            }).ToArray()
        };
        var service = harness.Service(new CapturedOpeningState(capture));
        workspace = await service.DesignateShadowAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, "Check the complete opening population."), Fixture.Owner);
        workspace = await service.ImportOpeningBalancesAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            harness.AdminPackRequest(workspace.Version, pack), Fixture.Owner);

        workspace.Readiness.Opening!.Lines.Should().NotBeEmpty().And.OnlyContain(line => line.Status == "Matched",
            "these omissions occur on both sides or only in source completeness, outside the present matched rows");
        workspace.Readiness.Opening.IsReconciled.Should().BeFalse();
        // A kind that no source supplies (here NAV, absent on both sides) is an uncovered kind; the other gaps
        // are opening missing-source requirements.
        if (gap == "kind")
            workspace.Readiness.Opening.UncoveredKinds.Should().Equal("Nav");
        else
            workspace.Readiness.MissingSources.Should().Contain(source => source.Message.StartsWith("Opening ", StringComparison.Ordinal));

        workspace = await ReviewCleanPeriodsAsync(fixture, service, workspace);

        workspace.Readiness.Periods.Should().OnlyContain(period => period.IsClean);
        workspace.Readiness.ApprovedReviewers.Should().Be(1);
        workspace.Readiness.IsReady.Should().BeFalse();
        workspace.Readiness.Blockers.Should().Contain(blocker => blocker.Contains(
            gap == "kind" ? "opening source covering Nav" : "missing opening source", StringComparison.Ordinal));
        var certify = () => service.IssueCertificateAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version), Fixture.Owner);
        await certify.Should().ThrowAsync<OnboardingValidationException>();
        var guard = new OnboardingBookOfRecordGuard(new FileOnboardingWorkspaceStore(fixture.StorePath));
        (await guard.GetStatusAsync(Fixture.Tenant, Fixture.Company, workspace.Scope.LedgerBookId)).IsBookOfRecord.Should().BeFalse();
    }

    [Fact]
    public async Task OpeningImport_ChangedCriteriaRequiresReimportAndFreshIndependentReviewBeforeCertificationAndPromotion()
    {
        using var fixture = new Fixture();
        var harness = new Harness(fixture);
        var service = harness.Service();
        var workspace = await service.CreateAsync(fixture.Request() with
        {
            Criteria = fixture.Criteria with { BalanceTolerance = 10m }
        }, Fixture.Owner);
        workspace = await service.DesignateShadowAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, "Parallel run."), Fixture.Owner);
        var loosePack = Pack("CASH,ADMIN-001,USD,,,,95,2026-01-31", "NAV,ADMIN-001,USD,,,,100,2026-01-31",
            "POSITION,ADMIN-001,USD,security-a,10,,,2026-01-31");
        workspace = await service.ImportOpeningBalancesAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            harness.AdminPackRequest(workspace.Version, loosePack), Fixture.Owner);
        workspace.Readiness.Opening!.IsReconciled.Should().BeTrue();
        workspace = await ReviewCleanPeriodsAsync(fixture, service, workspace);
        var certificate = await service.IssueCertificateAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version), Fixture.Owner);
        workspace = (await service.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))!;

        workspace = await service.UpdateCriteriaAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, fixture.Criteria), Fixture.Owner);
        workspace = await ReviewCleanPeriodsAsync(fixture, service, workspace);

        workspace.Readiness.Periods.Should().OnlyContain(period => period.IsClean);
        workspace.Readiness.ApprovedReviewers.Should().Be(1);
        workspace.Readiness.Opening!.IsReconciled.Should().BeFalse();
        workspace.Readiness.Opening.Lines.Single(line => line.Kind == "Balance").Tolerance.Should().Be(10m,
            "the retained opening comparison must display the criteria it actually used");
        workspace.Readiness.Blockers.Should().Contain("Reimport the incumbent opening balances against the current owner criteria.");
        workspace.Readiness.Blockers.Should().NotContain(blocker => blocker.Contains("Reconcile 0", StringComparison.Ordinal));
        var staleCertify = () => service.IssueCertificateAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version), Fixture.Owner);
        await staleCertify.Should().ThrowAsync<OnboardingValidationException>();
        var stalePromote = () => service.PromoteAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, certificate.CertificateId, "Use the stale opening.", []), Fixture.Reviewer);
        (await stalePromote.Should().ThrowAsync<OnboardingValidationException>()).WithMessage("*changed after the certificate*");

        workspace = await service.ImportOpeningBalancesAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            harness.AdminPackRequest(workspace.Version, loosePack), Fixture.Owner);
        workspace.Readiness.Opening!.IsReconciled.Should().BeFalse();
        workspace.Readiness.Opening.Lines.Single(line => line.Kind == "Balance").Should()
            .Match<OnboardingOpeningBalanceLineDto>(line => line.Status == "Break" && line.Difference == 5m && line.Tolerance == fixture.Criteria.BalanceTolerance);

        workspace = await service.ImportOpeningBalancesAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            harness.AdminPackRequest(workspace.Version), Fixture.Owner);
        workspace.Readiness.Opening!.IsReconciled.Should().BeTrue();
        workspace.Readiness.ApprovedReviewers.Should().Be(0);
        var unreviewedCertify = () => service.IssueCertificateAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version), Fixture.Owner);
        await unreviewedCertify.Should().ThrowAsync<OnboardingValidationException>();
        var ownerReview = () => service.ReviewAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, workspace.DataRevision, "Approved", "Owner cannot review.", ["review:owner"]), Fixture.Owner);
        await ownerReview.Should().ThrowAsync<UnauthorizedAccessException>();
        workspace = await service.ReviewAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, workspace.DataRevision, "Approved", "Reimport agrees under the current criteria.", ["review:reimport"]), Fixture.Reviewer);
        workspace.Readiness.IsReady.Should().BeTrue();
        var currentCertificate = await service.IssueCertificateAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version), Fixture.Owner);
        workspace = (await service.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))!;
        var ownerPromote = () => service.PromoteAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, currentCertificate.CertificateId, "Owner cannot promote.", []), Fixture.Owner);
        await ownerPromote.Should().ThrowAsync<UnauthorizedAccessException>();
        workspace = await service.PromoteAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, currentCertificate.CertificateId, "Complete current opening and reviewed periods.", []), Fixture.Reviewer);
        workspace.ShadowDesignation!.State.Should().Be(OnboardingShadowStates.Promoted);
    }

    [Fact]
    public async Task Promotion_RefusesStaleCertificate_AndWithdrawnTrialNeverBecomesBookOfRecord()
    {
        using var fixture = new Fixture();
        var harness = new Harness(fixture);
        var service = harness.Service();
        var guard = new OnboardingBookOfRecordGuard(new FileOnboardingWorkspaceStore(fixture.StorePath));
        var workspace = await harness.ReadyShadowWorkspaceAsync(service);
        var certificate = await service.IssueCertificateAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version), Fixture.Owner);
        workspace = (await service.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))!;

        // Any data change after issue (here a corrected March capture) makes the certificate stale.
        fixture.Source.Set("mar-2", fixture.Capture(Fixture.March, "mar-2", 100m, 10m, 100m));
        workspace = await fixture.Compare(service, workspace, Fixture.March, "mar-2");
        var stale = () => service.PromoteAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, certificate.CertificateId, "Cut over.", []), Fixture.Reviewer);
        (await stale.Should().ThrowAsync<OnboardingValidationException>()).WithMessage("*Issue a new certificate*");

        workspace = await service.WithdrawShadowAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, "Client chose to stay with the incumbent this year."), Fixture.Owner);
        var status = await guard.GetStatusAsync(Fixture.Tenant, Fixture.Company, fixture.Scope.LedgerBookId);
        status.IsBookOfRecord.Should().BeFalse();
        status.ShadowState.Should().Be(OnboardingShadowStates.Withdrawn);
        var afterWithdrawal = () => service.PromoteAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, certificate.CertificateId, "Cut over.", []), Fixture.Reviewer);
        await afterWithdrawal.Should().ThrowAsync<OnboardingValidationException>();

        // A fresh trial on the same book is allowed once the earlier one is withdrawn; a concurrent one is not.
        var second = await service.CreateAsync(fixture.Request() with { Name = "Second trial" }, Fixture.Owner);
        second = await service.DesignateShadowAsync(Fixture.Tenant, Fixture.Company, second.WorkspaceId,
            new(second.Version, "Retry."), Fixture.Owner);
        var third = await service.CreateAsync(fixture.Request() with { Name = "Third trial" }, Fixture.Owner);
        var concurrent = () => service.DesignateShadowAsync(Fixture.Tenant, Fixture.Company, third.WorkspaceId,
            new(third.Version, "Concurrent."), Fixture.Owner);
        await concurrent.Should().ThrowAsync<OnboardingValidationException>();
        (await guard.GetStatusAsync(Fixture.Tenant, Fixture.Company, fixture.Scope.LedgerBookId)).ShadowWorkspaceId
            .Should().Be(second.WorkspaceId);
    }

    [Fact]
    public void AdministratorPack_ParsesCashNavAndPositions_AndRefusesWrongDatesOrHeaders()
    {
        var records = AdministratorOpeningPackParser.Parse(File.ReadAllBytes(GoldenPath("administrator-opening-pack.csv")),
            Fixture.January, StatementIngressLimits.Default);
        records.Select(record => (record.Kind, record.InstrumentId, record.Amount)).Should().Equal(
            ("Balance", null, 100.00m), ("Nav", null, 100.00m), ("Position", "position-quantity:security-a", 10m));

        var both = AdministratorOpeningPackParser.Parse(Pack("POSITION,A,USD,\"sec,b\",5,50.5,,2026-01-31"),
            Fixture.January, StatementIngressLimits.Default);
        both.Select(record => record.InstrumentId).Should().Equal("position-quantity:sec,b", "position-market-value:sec,b");

        var wrongDate = () => AdministratorOpeningPackParser.Parse(Pack("CASH,A,USD,,,,1,2026-02-01"),
            Fixture.January, StatementIngressLimits.Default);
        wrongDate.Should().Throw<OnboardingValidationException>().WithMessage("*start date*");
        var noHeader = () => AdministratorOpeningPackParser.Parse(Encoding.UTF8.GetBytes("account,amount\nA,1"),
            Fixture.January, StatementIngressLimits.Default);
        noHeader.Should().Throw<OnboardingValidationException>().WithMessage("*missing column*");
    }

    private static byte[] Pack(params string[] rows)
        => Encoding.UTF8.GetBytes("record_type,account,currency,instrument,quantity,market_value,amount,as_of_date\n" + string.Join("\n", rows));

    private static string GoldenPath(string fileName)
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "tests", "Meridian.Tests", "TestData", "Golden", "onboarding", fileName);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(fileName);
    }

    private const string OpeningIban = "DE89370400440532013000";

    /// <summary>A camt.053 statement whose only balance is the opening booked balance on the workspace start date.</summary>
    private static ImportOnboardingOpeningBalancesRequestDto CashStatementRequest(int version, decimal amount = 100m)
    {
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
              <BkToCstmrStmt>
                <GrpHdr><MsgId>OPENING-1</MsgId><CreDtTm>2026-01-31T23:59:00</CreDtTm></GrpHdr>
                <Stmt>
                  <Id>STMT-OPENING</Id>
                  <Acct><Id><IBAN>{OpeningIban}</IBAN></Id><Ccy>USD</Ccy></Acct>
                  <Bal>
                    <Tp><CdOrPrtry><Cd>OPBD</Cd></CdOrPrtry></Tp>
                    <Amt Ccy="USD">{amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}</Amt>
                    <CdtDbtInd>CRDT</CdtDbtInd>
                    <Dt><Dt>2026-01-31</Dt></Dt>
                  </Bal>
                </Stmt>
              </BkToCstmrStmt>
            </Document>
            """;
        return new(version, OnboardingOpeningSourceKinds.StatementOpening, "bank-map", "mapping-v1",
            [new(OpeningIban, Fixture.Account)], "opening.xml", Convert.ToBase64String(Encoding.UTF8.GetBytes(xml)));
    }

    [Fact]
    public async Task OpeningSources_CashOnlyStatementCombinesWithAdministratorPack_ToReachCertification()
    {
        using var fixture = new Fixture();
        var harness = new Harness(fixture);
        var service = harness.Service();
        var workspace = await service.CreateAsync(fixture.Request(), Fixture.Owner);
        workspace = await service.DesignateShadowAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, "Bank cash plus administrator positions and NAV."), Fixture.Owner);

        // A cash-only statement compares only Balance: no NAV or Position line is reported as missing.
        workspace = await service.ImportOpeningBalancesAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            CashStatementRequest(workspace.Version), Fixture.Owner);
        var cashOnly = workspace.Readiness.Opening!;
        cashOnly.Lines.Should().ContainSingle().Which.Should().Match<OnboardingOpeningBalanceLineDto>(line =>
            line.Kind == "Balance" && line.Status == "Matched" && line.SourceKind == OnboardingOpeningSourceKinds.StatementOpening);
        cashOnly.UncoveredKinds.Should().Equal("Position", "Nav");
        cashOnly.IsReconciled.Should().BeFalse();
        workspace.Comparisons.Single().OpeningKinds.Should().Equal("Balance");
        workspace.Readiness.Blockers.Should().Contain(blocker => blocker.Contains("opening source covering Position, Nav", StringComparison.Ordinal));

        // The administrator pack supplies the complementary kinds; together the sources reconcile.
        workspace = await service.ImportOpeningBalancesAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            harness.AdminPackRequest(workspace.Version, Pack("NAV,ADMIN-001,USD,,,,100.00,2026-01-31",
                "POSITION,ADMIN-001,USD,security-a,10,,,2026-01-31")), Fixture.Owner);
        var combined = workspace.Readiness.Opening!;
        combined.IsReconciled.Should().BeTrue();
        combined.UncoveredKinds.Should().BeNull();
        combined.Sources!.Select(source => source.SourceKind).Should().Equal(
            OnboardingOpeningSourceKinds.StatementOpening, OnboardingOpeningSourceKinds.AdministratorPack);
        combined.Sources!.Last().Kinds.Should().Equal("Position", "Nav");
        combined.Lines.Should().HaveCount(3).And.OnlyContain(line => line.Status == "Matched");

        // Re-importing the statement keeps a per-source lineage: the pack's rows are not marked as disappeared.
        workspace = await service.ImportOpeningBalancesAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            CashStatementRequest(workspace.Version), Fixture.Owner);
        workspace.Comparisons.Last().Differences.Should().BeEmpty();
        workspace.Readiness.Opening!.IsReconciled.Should().BeTrue();

        workspace = await ReviewCleanPeriodsAsync(fixture, service, workspace);
        workspace.Readiness.IsReady.Should().BeTrue();
        var certificate = await service.IssueCertificateAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version), Fixture.Owner);
        certificate.Content.Opening.Sources.Should().HaveCount(2);

        var restarted = harness.Service();
        foreach (var run in workspace.Comparisons.Where(run => run.IsOpening))
            (await restarted.ReplayComparisonAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId, run.ComparisonId))!
                .ContentHash.Should().Be(run.ContentHash);
    }

    [Fact]
    public async Task OpeningBreak_IsExplainableThroughItsOpeningComparison()
    {
        using var fixture = new Fixture();
        var harness = new Harness(fixture);
        var service = harness.Service();
        var workspace = await service.CreateAsync(fixture.Request(), Fixture.Owner);
        workspace = await service.DesignateShadowAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, "Trial."), Fixture.Owner);
        workspace = await service.ImportOpeningBalancesAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            harness.AdminPackRequest(workspace.Version, Pack("CASH,ADMIN-001,USD,,,,97.00,2026-01-31",
                "NAV,ADMIN-001,USD,,,,100.00,2026-01-31", "POSITION,ADMIN-001,USD,security-a,10,,,2026-01-31")), Fixture.Owner);
        var line = workspace.Readiness.Opening!.Lines.Single(item => item.Kind == "Balance");
        line.Status.Should().Be("Break");
        line.ComparisonId.Should().Be(workspace.Comparisons.Single(run => run.IsOpening).ComparisonId);

        workspace = await service.ExplainDifferenceAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId, line.DifferenceKey,
            new(workspace.Version, line.ComparisonId!, OnboardingDifferenceClassifications.Treatment,
                "Administrator nets an unsettled fee against opening cash.", ["evidence:fee-netting"]), Fixture.Owner);

        var opening = workspace.Readiness.Opening!;
        opening.Lines.Single(item => item.Kind == "Balance").Should().Match<OnboardingOpeningBalanceLineDto>(item =>
            item.Status == "Explained" && item.Classification == OnboardingDifferenceClassifications.Treatment);
        opening.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task ShadowDesignation_IsExclusivePerLedgerBook_EvenWhenTheServicePreCheckIsBypassed()
    {
        using var fixture = new Fixture();
        var harness = new Harness(fixture);
        var service = harness.Service();
        var first = await service.CreateAsync(fixture.Request() with { Name = "First trial" }, Fixture.Owner);
        var second = await service.CreateAsync(fixture.Request() with { Name = "Second trial" }, Fixture.Owner);
        first = await service.DesignateShadowAsync(Fixture.Tenant, Fixture.Company, first.WorkspaceId,
            new(first.Version, "First."), Fixture.Owner);

        // Simulate the losing side of a race: it read the book before the first designation was saved.
        var store = new FileOnboardingWorkspaceStore(fixture.StorePath);
        var stale = second with
        {
            Version = second.Version + 1,
            ShadowDesignation = new(OnboardingShadowStates.Shadow, second.Scope.LedgerBookId, Fixture.Owner,
                Fixture.Now, "Concurrent.")
        };
        var save = () => store.SaveAsync(stale, second.Version);
        await save.Should().ThrowAsync<OnboardingConcurrencyException>().WithMessage("*already holds this ledger book*");

        // Concurrent designations through separate service instances: exactly one wins.
        var third = await service.CreateAsync(fixture.Request() with { Name = "Third trial" }, Fixture.Owner);
        await service.WithdrawShadowAsync(Fixture.Tenant, Fixture.Company, first.WorkspaceId,
            new((await service.GetAsync(Fixture.Tenant, Fixture.Company, first.WorkspaceId))!.Version, "Restart."), Fixture.Owner);
        var attempts = new[] { second, third }.Select(async workspace =>
        {
            try
            {
                await harness.Service().DesignateShadowAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
                    new(workspace.Version, "Race."), Fixture.Owner);
                return true;
            }
            catch (Exception ex) when (ex is OnboardingConcurrencyException or OnboardingValidationException)
            {
                return false;
            }
        });
        (await Task.WhenAll(attempts)).Count(won => won).Should().Be(1);
    }

    private sealed class Harness(Fixture fixture)
    {
        internal OnboardingWorkspaceService Service(IOnboardingOpeningStateProvider? state = null) => new(new FileOnboardingWorkspaceStore(fixture.StorePath), fixture.Source,
            new FixedClock(), new OnboardingOpeningSourceReader(), state ?? new OpeningState(fixture));

        internal ImportOnboardingOpeningBalancesRequestDto AdminPackRequest(int version, byte[]? content = null)
            => new(version, OnboardingOpeningSourceKinds.AdministratorPack, "admin-map", "mapping-v1", Mappings,
                "opening.csv", Convert.ToBase64String(content ?? File.ReadAllBytes(GoldenPath("administrator-opening-pack.csv"))));

        internal async Task<OnboardingWorkspaceDto> ReadyShadowWorkspaceAsync(OnboardingWorkspaceService service)
        {
            var workspace = await service.CreateAsync(fixture.Request(), Fixture.Owner);
            workspace = await service.DesignateShadowAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
                new(workspace.Version, "Trial."), Fixture.Owner);
            workspace = await service.ImportOpeningBalancesAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
                AdminPackRequest(workspace.Version), Fixture.Owner);
            foreach (var (date, id) in new[] { (Fixture.January, "jan"), (Fixture.February, "feb"), (Fixture.March, "mar") })
            {
                fixture.Source.Set(id, fixture.Capture(date, id, 100m, 10m, 100m));
                workspace = await fixture.Compare(service, workspace, date, id);
            }
            workspace = await service.ReviewAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
                new(workspace.Version, workspace.DataRevision, "Approved", "Clean parallel run.", ["review:clean"]), Fixture.Reviewer);
            workspace.Readiness.IsReady.Should().BeTrue();
            return workspace;
        }
    }

    private static async Task<OnboardingWorkspaceDto> ReviewCleanPeriodsAsync(
        Fixture fixture, OnboardingWorkspaceService service, OnboardingWorkspaceDto workspace)
    {
        foreach (var date in fixture.Criteria.RequiredDates)
        {
            var id = $"clean:{date:yyyy-MM-dd}:{workspace.Version}";
            var capture = fixture.Capture(date, id, 100m, 10m, 100m);
            var observations = workspace.Scope.AccountIds.SelectMany(accountId =>
                capture.Observations.Select(row => row with { AccountId = accountId })).ToArray();
            var payload = JsonSerializer.Serialize(new { workspace.Scope, AsOfDate = date, Observations = observations });
            capture = capture with
            {
                Observations = observations,
                Snapshots = capture.Snapshots.Select(snapshot => snapshot with
                {
                    PayloadJson = payload,
                    ContentHash = OnboardingWorkspaceService.HashSourcePayload(payload)
                }).ToArray()
            };
            fixture.Source.Set(id, capture);
            workspace = await fixture.Compare(service, workspace, date, id);
        }
        return await service.ReviewAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId,
            new(workspace.Version, workspace.DataRevision, "Approved", "Every required period reconciles.", ["review:clean-periods"]), Fixture.Reviewer);
    }

    private sealed class CapturedOpeningState(OnboardingSourceCaptureDto capture) : IOnboardingOpeningStateProvider
    {
        public Task<OnboardingSourceCaptureDto> CaptureOpeningAsync(OnboardingWorkspaceDto workspace, CancellationToken ct = default)
            => Task.FromResult(capture);
    }

    /// <summary>Meridian's opening records: cash 100, NAV 100 and ten units of security-a.</summary>
    private sealed class OpeningState(Fixture fixture) : IOnboardingOpeningStateProvider
    {
        public Task<OnboardingSourceCaptureDto> CaptureOpeningAsync(OnboardingWorkspaceDto workspace, CancellationToken ct = default)
        {
            const string payload = "{\"cash\":100,\"nav\":100,\"security-a\":10}";
            var snapshot = new OnboardingSourceSnapshotDto("meridian-opening", "MeridianOpeningState", Fixture.Account, "v1",
                OnboardingWorkspaceService.HashSourcePayload(payload), Fixture.Now, fixture.Scope.StartDate, "", payload, []);
            return Task.FromResult(new OnboardingSourceCaptureDto([snapshot],
            [
                new("Balance", Fixture.Account, "USD", null, 100m, null, ["meridian-opening"], []),
                new("Nav", Fixture.Account, "USD", null, 100m, null, ["meridian-opening"], []),
                new("Position", Fixture.Account, "USD", "position-quantity:security-a", 10m, null, ["meridian-opening"], [])
            ], [], null));
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Fixture.Now;
    }
}
