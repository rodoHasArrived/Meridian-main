using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage;
using NSubstitute;

namespace Meridian.Tests.Application;

public sealed class ContractualCashFlowPreviewTests
{
    private static readonly DateOnly Issue = new(2026, 1, 1);
    private static readonly DateTimeOffset Cutoff = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ContractualCashFlowRequest Request = new(Issue, Cutoff);
    private static SecurityDetailDto Security(Guid id) => new(id, "Bond", SecurityStatusDto.Active, "Test", "USD",
        JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new
        {
            issueDate = "2026-01-01",
            maturity = "2027-01-01",
            par = 1000m,
            couponRate = 5m,
            paymentFrequency = "Quarterly",
            dayCount = "ACT/365"
        }), [], [], 1, new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null);

    [Fact]
    public async Task Preview_uses_recorded_terms_cutoff_and_preserves_percent_coupon_units()
    {
        var id = Guid.NewGuid();
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(id, Cutoff, Arg.Any<CancellationToken>()).Returns(Security(id));
        var service = new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>());
        var result = await service.PreviewSecurityAsync(id, Request);
        result.Should().NotBeNull();
        result!.Blockers.Should().BeEmpty();
        result.Events.Sum(e => e.CashInterest).Should().BeApproximately(50m, .00000003m);
        await query.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Assigned_provider_is_never_replaced_by_a_calculation_override()
    {
        var id = Guid.NewGuid();
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(id, Cutoff, Arg.Any<CancellationToken>()).Returns(Security(id));
        var sources = Substitute.For<ISecurityMasterCashFlowService>();
        sources.GetCashFlowSourceAsync(id, Arg.Any<CancellationToken>()).Returns(new SecurityCashFlowSourceDto(id, StructuredCashFlowSourceKind.MIAC, Cutoff, false, null, null));
        var service = new ContractualCashFlowPreviewService(query, sources);
        var result = await service.PreviewSecurityAsync(id, Request with { ContractOverride = ContractualCashFlowPreviewService.FromSecurity(Security(id)) });
        result!.Blockers.Should().ContainSingle().Which.Code.Should().Be("AuthoritativeProviderAssigned");
        result.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Pinned_curve_recorded_after_the_cutoff_is_blocked()
    {
        var id = Guid.NewGuid();
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(id, Cutoff, Arg.Any<CancellationToken>()).Returns(Security(id));
        var curves = Substitute.For<ISecurityCurveStore>();
        var curveId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        curves.GetDefinitionAsync(curveId, Arg.Any<CancellationToken>()).Returns(new SecurityCurveDefinitionDto(curveId, "benchmark", SecurityCurveKind.Discount, "USD", null, null,
            "Actual365", SecurityCurveInterpolation.Linear, "test", null, "test", Cutoff));
        curves.GetVersionByIdAsync(curveId, versionId, Arg.Any<CancellationToken>()).Returns(new SecurityCurveVersionDto(versionId, curveId, "benchmark", 1, Issue,
            Cutoff.AddDays(1), null, "test", "test", [new(Issue, 1m)], "fixture"));
        var service = new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>(), curves);
        var result = await service.PreviewSecurityAsync(id, Request with { CurvePins = [new("benchmark", curveId, versionId)] });
        result!.Blockers.Should().ContainSingle().Which.Code.Should().Be("CurveOutsideCutoff");
    }

    [Fact]
    public async Task Missing_contractual_issue_date_is_not_replaced_by_record_effective_date()
    {
        var id = Guid.NewGuid();
        var security = Security(id) with { AssetSpecificTerms = JsonSerializer.SerializeToElement(new { maturity = "2027-01-01", par = 1000, couponRate = 5, paymentFrequency = "Quarterly", dayCount = "ACT/365" }) };
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(id, Cutoff, Arg.Any<CancellationToken>()).Returns(security);
        var result = await new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>()).PreviewSecurityAsync(id, Request);
        result!.Blockers.Should().ContainSingle().Which.Code.Should().Be("ContractInputsUnavailable");
    }

    [Fact]
    public async Task Foreign_currency_swap_leg_is_not_silently_reported_in_the_security_currency()
    {
        var id = Guid.NewGuid();
        var security = Security(id) with
        {
            AssetClass = "Swap",
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                effectiveDate = "2026-01-01",
                maturityDate = "2027-01-01",
                legs = new[] { new { legId = "foreign", currency = "EUR", direction = "Pay", fixedRate = .03m,
                notional = 1000m, paymentFrequency = "Quarterly", dayCount = "ACT/365" } }
            })
        };
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(id, Cutoff, Arg.Any<CancellationToken>()).Returns(security);
        var result = await new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>()).PreviewSecurityAsync(id, Request);
        result!.Blockers.Should().ContainSingle().Which.Message.Should().Contain("Cross-currency");
        result.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Canonical_floating_bond_retains_declared_calendar_cap_and_exercise_stream_with_original_curve_metadata()
    {
        var id = Guid.NewGuid();
        var security = Security(id) with
        {
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                assetClassTermsVersion = 3,
                issueDate = "2026-01-01",
                maturity = "2027-01-01",
                par = 1000,
                couponType = "Floating",
                floatingIndex = "SOFR",
                paymentFrequency = "Quarterly",
                dayCount = "ACT/365",
                resetSchedule = new[] { new { resetDate = "2026-01-01", fixingRate = 1m } },
                capRate = 4m,
                floorRate = 1m,
                scheduleConventions = new { couponRateUnit = "Percent", paymentCalendars = new[] { "WEEKENDS" } },
                callSchedule = new[] { new { callDate = "2026-04-01", exerciseStyle = "European", callPrice = 100m } }
            })
        };
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(id, Cutoff, Arg.Any<CancellationToken>()).Returns(security);
        var curves = Substitute.For<ISecurityCurveStore>();
        var curveId = Guid.NewGuid();
        var curveVersionId = Guid.NewGuid();
        var definition = new SecurityCurveDefinitionDto(curveId, "USD-SOFR", SecurityCurveKind.Forward, "USD", "SOFR", null, "Actual365",
            SecurityCurveInterpolation.Linear, "test", null, "test", Cutoff);
        curves.GetDefinitionAsync(curveId, Arg.Any<CancellationToken>()).Returns(definition);
        curves.GetVersionByIdAsync(curveId, curveVersionId, Arg.Any<CancellationToken>()).Returns(new SecurityCurveVersionDto(curveVersionId, curveId,
            "USD-SOFR", 1, Issue, Cutoff, null, "test", "test", [new(Issue, .08m), new(Issue.AddYears(1), .08m)], "fixture"));
        var calendars = new Meridian.Contracts.Calendars.StaticBusinessDayCalendarSource([Meridian.Contracts.Calendars.BusinessDayCalendars.WeekendsOnly]);
        var result = await new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>(), curves, calendars)
            .PreviewSecurityAsync(id, Request with { CurvePins = [new("SOFR", curveId, curveVersionId)] });
        result!.Blockers.Should().BeEmpty();
        result.Events.Sum(e => e.CashInterest).Should().BeApproximately(1000m * .01m * 90 / 365 + 1000m * .04m * 275 / 365, .00000002m);
        result.Evidence.Curves.Single().Definition.Should().Be(definition);
        result.ResolvedContract!.Streams.Single().Conventions!.PaymentCalendars.Should().Equal("WEEKENDS");
        result.ExerciseEligibility.Single().StreamId.Should().Be(result.ResolvedContract.Streams.Single().StreamId);
    }

    [Fact]
    public async Task Replay_packet_survives_restart_refuses_changed_identity_and_detects_tampering()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-cashflow-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new StorageOptions { RootPath = root };
            var store = new FileContractualCashFlowRunStore(options);
            var contract = ContractualCashFlowPreviewService.FromSecurity(Security(Guid.NewGuid()));
            var result = ContractualCashFlowEngine.Calculate(contract, Request);
            var run = new ContractualCashFlowRun(Guid.NewGuid(), "Security", Guid.NewGuid(), Cutoff, contract, Request, result, "operator");
            await store.SaveAsync(run);
            var restarted = new FileContractualCashFlowRunStore(options);
            var retained = await restarted.GetAsync(run.SubjectKind, run.SubjectId, run.RunId);
            retained!.Result.Evidence.InputHash.Should().Be(run.Result.Evidence.InputHash);
            var retry = await restarted.SaveAsync(run with { RecordedAt = Cutoff.AddDays(1) });
            retry.RecordedAt.Should().Be(Cutoff);
            var changedRequest = Request with { Horizon = Issue.AddMonths(6) };
            var changed = run with { Request = changedRequest, Result = ContractualCashFlowEngine.Calculate(contract, changedRequest) };
            await restarted.Invoking(s => s.SaveAsync(changed)).Should().ThrowAsync<InvalidOperationException>();
            var path = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Single();
            var tampered = run with { Result = result with { Events = result.Events.Select(e => e with { CashInterest = 999m }).ToArray() } };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(tampered, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await restarted.Invoking(s => s.GetAsync(run.SubjectKind, run.SubjectId, run.RunId)).Should().ThrowAsync<InvalidDataException>();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
