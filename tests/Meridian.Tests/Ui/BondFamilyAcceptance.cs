using System.Text.Json;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.Ui;

/// <summary>Synthetic source ports around the production workflow calculator, never replacement cash arithmetic.</summary>
internal static class BondFamilyAcceptance
{
    internal static BondWorkflowService Service(Guid securityId, DateTimeOffset asOf, string family,
        bool missingForward = false)
    {
        var date = DateOnly.FromDateTime(asOf.UtcDateTime);
        var overnight = family == "Overnight";
        var inflation = family == "Inflation";
        var floating = family is "SimpleFloater" or "Overnight";
        var holiday = date.AddDays(1);
        while (holiday.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            holiday = holiday.AddDays(1);
        var calendar = new HolidayBusinessDayCalendar("SYNTHETIC-HOLIDAY", 1, [holiday]);
        var terms = new Dictionary<string, object?>
        {
            ["assetClassTermsVersion"] = overnight || inflation ? 5 : 2,
            ["issueDate"] = date.ToString("yyyy-MM-dd"),
            ["maturity"] = (overnight ? date.AddDays(7) : date.AddYears(1)).ToString("yyyy-MM-dd"),
            ["couponType"] = floating ? "Floating" : inflation ? "InflationLinked" : family == "ZeroCoupon" ? "ZeroCoupon" : "Fixed",
            ["couponRate"] = family == "SignedCoupon" ? -2m : family == "Amortizer" ? 10m : family == "ZeroCoupon" ? 0m : 5m,
            ["dayCount"] = overnight ? "ACT/360" : "30/360",
            ["par"] = 100m,
            ["paymentFrequency"] = family is "SimpleFloater" or "Amortizer" ? "SemiAnnual" : "Annual",
            ["isCallable"] = false,
            ["scheduleConventions"] = new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = overnight ? "DayCount" : "Level" }
        };
        if (floating)
        { terms["floatingIndex"] = "SOFR"; terms["spreadBps"] = 100m; }
        if (overnight)
            terms["bondWorkflow"] = new
            {
                floatingMethod = "CompoundedOvernight",
                fixingCalendarId = calendar.CalendarId,
                lookbackBusinessDays = 0,
                observationMethod = "LookbackWithoutShift",
                observationWeighting = "InterestPeriod",
                lockoutBusinessDays = 0
            };
        if (inflation)
        {
            terms["inflationIndex"] = "CPI";
            terms["inflationBaseIndexValue"] = 100m;
            terms["inflationIndexRatio"] = 1m;
            terms["bondWorkflow"] = new { lagMonths = 0, interpolateMonthly = false, redemptionFloorRatio = 1m };
        }
        if (family == "Amortizer")
            terms["principalSchedule"] = new[] { new { paymentDate = date.AddMonths(6).ToString("yyyy-MM-dd"), amount = 40m } };
        var security = new SecurityDetailDto(securityId, "Bond", SecurityStatusDto.Active, "Synthetic " + family, "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(terms), [], [], 9, asOf, null);
        var securities = Substitute.For<ISecurityMasterQueryService>();
        securities.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(security);
        securities.GetRecordedByIdAsOfAsync(securityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(security);

        var definitions = new List<SecurityCurveDefinitionDto>();
        var versions = new Dictionary<Guid, SecurityCurveVersionDto>();
        if (!missingForward)
            AddCurve(SecurityCurveKind.Forward, "SOFR", .02m);
        AddCurve(SecurityCurveKind.Inflation, "CPI", 110m);
        var curves = Substitute.For<ISecurityCurveStore>();
        curves.ListDefinitionsAsync(Arg.Any<SecurityCurveKind>(), "USD", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<SecurityCurveDefinitionDto>>(definitions.Where(row =>
                row.Kind == call.ArgAt<SecurityCurveKind>(0) && row.IndexName == call.ArgAt<string?>(2)).ToArray()));
        curves.GetLatestVersionAsOfAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<SecurityCurveVersionDto?>(versions.GetValueOrDefault(call.ArgAt<Guid>(0))));
        var observations = Substitute.For<IBondWorkflowTypedObservationSource>();
        observations.GetAsync(Arg.Any<string>(), Arg.Any<BondMarketObservationKind>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var kind = call.ArgAt<BondMarketObservationKind>(1);
                var from = call.ArgAt<DateOnly>(2);
                var end = call.ArgAt<DateOnly>(3) < date ? call.ArgAt<DateOnly>(3) : date;
                var rows = new List<BondWorkflowRateObservation>();
                for (var day = from; day <= end; day = day.AddDays(1))
                    rows.Add(new(call.ArgAt<string>(0), day, kind == BondMarketObservationKind.Fixing ? .03m : 100m,
                        "synthetic-" + kind, asOf, asOf, Sha256Digest.ComputeUtf8(kind + ":" + day)));
                return Task.FromResult<IReadOnlyList<BondWorkflowRateObservation>>(rows);
            });
        return new(securities, new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly, calendar]), curves, observations);

        void AddCurve(SecurityCurveKind kind, string index, decimal value)
        {
            var id = Guid.NewGuid();
            var definition = new SecurityCurveDefinitionDto(id, "synthetic-" + kind, kind, "USD", index, null,
                "ACT/365", SecurityCurveInterpolation.Linear, "synthetic", null, "synthetic", asOf);
            definitions.Add(definition);
            versions.Add(id, new(Guid.NewGuid(), id, definition.CurveKey, 1, date, asOf, asOf, "synthetic", "synthetic",
                [new(date, value), new(date.AddYears(2), value)], Sha256Digest.ComputeUtf8(kind + ":" + index)));
        }
    }
}
