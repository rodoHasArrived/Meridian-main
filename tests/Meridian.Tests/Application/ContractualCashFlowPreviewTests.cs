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

    [Theory]
    [InlineData("swapLegs")]
    [InlineData("cashFlowLegs")]
    [InlineData("LEGS")]
    public async Task Foreign_currency_swap_leg_alias_cannot_bypass_the_currency_refusal(string legContainer)
    {
        var id = Guid.NewGuid();
        var security = Security(id) with
        {
            AssetClass = "Swap",
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["assetClassTermsVersion"] = 2,
                ["effectiveDate"] = "2026-01-01",
                ["maturityDate"] = "2027-01-01",
                [legContainer] = new[]
                {
                    new { legId = "foreign", currency = "EUR", direction = "Receive", fixedRate = .05m,
                        notional = 1000m, paymentFrequency = "Quarterly", dayCount = "ACT/365" }
                }
            })
        };
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(id, Cutoff, Arg.Any<CancellationToken>()).Returns(security);
        var result = await new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>())
            .PreviewSecurityAsync(id, Request);

        result!.Blockers.Should().ContainSingle().Which.Code.Should().Be("ContractInputsUnavailable");
        result.Events.Should().BeEmpty("foreign leg cash cannot be relabeled as the security currency");
        result.ResolvedContract.Should().BeNull();
    }

    [Theory]
    [InlineData(true, "EUR")]
    [InlineData(true, "USD")]
    [InlineData(false, "USD")]
    public async Task Malformed_swap_leg_rows_cannot_shift_the_retained_currency_or_conventions(bool nullBeforeLeg, string currency)
    {
        var id = Guid.NewGuid();
        var leg = new
        {
            legId = "retained",
            currency,
            direction = "Receive",
            fixedRate = .05m,
            notional = 1000m,
            paymentFrequency = "Quarterly",
            dayCount = "ACT/365",
            scheduleConventions = new { couponRateUnit = "Percent", accrualAdjusted = false }
        };
        var security = Security(id) with
        {
            AssetClass = "Swap",
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                assetClassTermsVersion = 2,
                effectiveDate = "2026-01-01",
                maturityDate = "2027-01-01",
                legs = nullBeforeLeg ? new object?[] { null, leg } : new object?[] { leg, null }
            })
        };
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(id, Cutoff, Arg.Any<CancellationToken>()).Returns(security);
        var result = await new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>())
            .PreviewSecurityAsync(id, Request);

        result!.Blockers.Should().ContainSingle().Which.Code.Should().Be("ContractInputsUnavailable");
        result.Events.Should().BeEmpty("malformed retained legs cannot be skipped before pairing currency and rate conventions");
        result.ResolvedContract.Should().BeNull();
    }

    [Theory]
    [InlineData("swapLegs", 1, 50)]
    [InlineData("swapLegs", 2, .5)]
    [InlineData("cashFlowLegs", 1, 50)]
    [InlineData("cashFlowLegs", 2, .5)]
    [InlineData("LEGS", 1, 50)]
    [InlineData("LEGS", 2, .5)]
    public async Task Same_currency_swap_leg_aliases_preserve_the_declared_rate_unit(string legContainer, int version, decimal interest)
    {
        var id = Guid.NewGuid();
        var security = Security(id) with
        {
            AssetClass = "Swap",
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["assetClassTermsVersion"] = version,
                ["effectiveDate"] = "2026-01-01",
                ["maturityDate"] = "2027-01-01",
                [legContainer] = new[]
                {
                    new { legId = "retained", currency = "USD", direction = "Receive", fixedRate = .05m,
                        notional = 1000m, paymentFrequency = "Quarterly", dayCount = "ACT/365",
                        scheduleConventions = new { couponRateUnit = "Percent", accrualAdjusted = false } }
                }
            })
        };
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(id, Cutoff, Arg.Any<CancellationToken>()).Returns(security);
        var result = await new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>())
            .PreviewSecurityAsync(id, Request);

        result!.Blockers.Should().BeEmpty();
        result.ResolvedContract!.Currency.Should().Be("USD");
        result.Events.Sum(static item => item.CashInterest).Should().BeApproximately(interest, .00000003m);
    }

    [Fact]
    public async Task A_retained_leg_array_with_no_object_rows_cannot_fall_back_to_a_scalar_stream()
    {
        var id = Guid.NewGuid();
        var security = Security(id) with
        {
            AssetClass = "Swap",
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                assetClassTermsVersion = 2,
                effectiveDate = "2026-01-01",
                maturityDate = "2027-01-01",
                par = 1000m,
                couponRate = .05m,
                paymentFrequency = "Quarterly",
                dayCount = "ACT/365",
                legs = new object?[] { null, "invalid" }
            })
        };
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(id, Cutoff, Arg.Any<CancellationToken>()).Returns(security);
        var result = await new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>())
            .PreviewSecurityAsync(id, Request);

        result!.Blockers.Should().ContainSingle().Which.Code.Should().Be("ContractInputsUnavailable");
        result.Events.Should().BeEmpty();
        result.ResolvedContract.Should().BeNull();
    }

    [Theory]
    [InlineData("profile", "EUR")]
    [InlineData("profile", "USD")]
    [InlineData("root", "EUR")]
    [InlineData("root", "USD")]
    [InlineData("common", "EUR")]
    [InlineData("common", "USD")]
    public async Task Retained_leg_currency_and_conventions_follow_the_resolvers_source_authority(string source, string currency)
    {
        var id = Guid.NewGuid();
        object Leg(string legId, string legCurrency, decimal fixedRate) => new
        {
            legId,
            currency = legCurrency,
            fixedRate,
            direction = "Receive",
            notional = 1000m,
            paymentFrequency = "Quarterly",
            dayCount = "ACT/365",
            scheduleConventions = new { couponRateUnit = "Percent", accrualAdjusted = false }
        };
        var selected = Leg("selected", currency, .05m);
        var shadow = Leg("shadow", currency == "USD" ? "EUR" : "USD", .08m);
        var terms = new Dictionary<string, object?>
        {
            ["assetClassTermsVersion"] = 2,
            ["effectiveDate"] = "2026-01-01",
            ["maturityDate"] = "2027-01-01",
            // An empty higher-priority array still allows the first populated alias/source.
            ["legs"] = source == "profile" ? new[] { shadow } : Array.Empty<object>()
        };
        if (source == "profile")
            terms["profileFields"] = new { swapLegs = new[] { selected } };
        else if (source == "root")
            terms["swapLegs"] = new[] { selected };
        var security = Security(id) with
        {
            AssetClass = "Swap",
            AssetSpecificTerms = JsonSerializer.SerializeToElement(terms),
            CommonTerms = JsonSerializer.SerializeToElement(new { cashFlowLegs = new[] { source == "common" ? selected : shadow } })
        };
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(id, Cutoff, Arg.Any<CancellationToken>()).Returns(security);
        var result = await new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>())
            .PreviewSecurityAsync(id, Request);

        if (currency == "EUR")
        {
            result!.Blockers.Should().ContainSingle().Which.Code.Should().Be("ContractInputsUnavailable");
            result.Events.Should().BeEmpty();
            result.ResolvedContract.Should().BeNull();
        }
        else
        {
            result!.Blockers.Should().BeEmpty();
            result.ResolvedContract!.Streams.Should().ContainSingle().Which.StreamId.Should().Be("selected");
            result.Events.Sum(static item => item.CashInterest).Should().BeApproximately(.5m, .00000003m);
        }
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

    [Theory]
    [InlineData(null, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public async Task Swap_leg_conventions_change_economics_only_at_their_declaring_version(int? termsVersion, bool declared)
    {
        var id = Guid.NewGuid();
        var security = Security(id) with
        {
            AssetClass = "Swap",
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                assetClassTermsVersion = termsVersion,
                effectiveDate = "2026-01-01",
                maturityDate = "2027-01-01",
                legs = new[]
                {
                    new
                    {
                        legId = "fixed",
                        currency = "USD",
                        direction = "Receive",
                        fixedRate = .05m,
                        notional = 1000m,
                        paymentFrequency = "Quarterly",
                        dayCount = "ACT/365",
                        scheduleConventions = new { couponRateUnit = "Percent", accrualAdjusted = false }
                    }
                }
            })
        };
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(id, Cutoff, Arg.Any<CancellationToken>()).Returns(security);
        var result = await new ContractualCashFlowPreviewService(query, Substitute.For<ISecurityMasterCashFlowService>())
            .PreviewSecurityAsync(id, Request);

        result!.Blockers.Should().BeEmpty();
        var stream = result.ResolvedContract!.Streams.Should().ContainSingle().Which;
        stream.FixedRate.Should().Be(declared ? .0005m : .05m);
        stream.Conventions!.IsEmpty.Should().Be(!declared,
            "a stray leg block on an unstamped or version-1 swap cannot change its retained economics");
        result.Events.Sum(static item => item.CashInterest).Should().BeApproximately(declared ? .5m : 50m, .00000003m);
    }

    [Fact]
    public async Task Retained_swap_packet_replays_its_normalized_contract_after_the_source_mapping_is_corrected()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-cashflow-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // A packet calculated before the source-version gate retained the already-converted
            // .05 percent rate. Replay must use that contract, never remap the source row as .05.
            var contract = new ContractualCashFlowContract(Issue, Issue.AddYears(1), "USD",
                [new("fixed", 1000m, 3, DayCountConvention.Actual365, .0005m,
                    Conventions: ScheduleConventionTerms.Empty with { CouponRateUnit = "Percent", AccrualAdjusted = false })]);
            var result = ContractualCashFlowEngine.Calculate(contract, Request, sourceVersion: "security:retained:version:1");
            var run = new ContractualCashFlowRun(Guid.NewGuid(), "Security", Guid.NewGuid(), Cutoff, contract, Request, result, "operator");
            var options = new StorageOptions { RootPath = root };
            await new FileContractualCashFlowRunStore(options).SaveAsync(run);

            var retained = await new FileContractualCashFlowRunStore(options).GetAsync(run.SubjectKind, run.SubjectId, run.RunId);
            ContractualCashFlowEngine.Verify(retained!).Should().BeTrue();
            var replayed = ContractualCashFlowEngine.Replay(retained!);
            replayed.Evidence.Should().BeEquivalentTo(result.Evidence);
            replayed.Events.Should().Equal(result.Events);
            replayed.Events.Sum(static item => item.CashInterest).Should().BeApproximately(.5m, .00000003m);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
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
