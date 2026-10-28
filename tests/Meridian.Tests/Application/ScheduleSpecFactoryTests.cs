using FluentAssertions;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.FSharp.Domain;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Meridian.Tests.Application;

public sealed class ScheduleSpecFactoryTests
{
    private static readonly DateOnly Start = new(2026, 1, 31);
    private static readonly DateOnly End = new(2026, 7, 31);
    private static readonly IBusinessDayCalendarSource Calendars = new StaticBusinessDayCalendarSource(
    [
        new HolidayBusinessDayCalendar("PAY", 1, [new DateOnly(2026, 5, 1)]),
        new HolidayBusinessDayCalendar("OTHER", 1, [new DateOnly(2026, 5, 4)])
    ]);

    [Fact]
    public void EmptyBlockRequiresTheLegacyPath()
    {
        var ok = Create(ScheduleConventionsModule.Empty, out var spec, out var refusal);
        ok.Should().BeFalse();
        spec.Should().BeNull();
        refusal.Should().Contain("legacy");
    }

    [Fact]
    public void PaymentLagDoesNotExtendAccrual()
    {
        var conventions = Terms(businessDay: "Following", calendars: ["PAY"], lag: 1);
        Create(conventions, out var spec, out var refusal).Should().BeTrue(refusal);
        var schedule = ScheduleGenerator.Generate(spec!);
        var first = schedule.Periods[0];
        first.AccrualStart.Should().Be(Start);
        first.AccrualEnd.Should().Be(new DateOnly(2026, 2, 28));
        first.PaymentDate.Should().Be(new DateOnly(2026, 3, 2));
    }

    [Fact]
    public void AccrualAdjustmentIsOptIn()
    {
        var conventions = Terms(businessDay: "MF", calendars: ["PAY"], accrualAdjusted: true);
        Create(conventions, out var spec, out var refusal).Should().BeTrue(refusal);
        var first = ScheduleGenerator.Generate(spec!).Periods[0];
        first.AccrualStart.Should().Be(new DateOnly(2026, 1, 30));
        first.AccrualEnd.Should().Be(new DateOnly(2026, 2, 27));
        first.PaymentDate.Should().Be(new DateOnly(2026, 2, 27));
    }

    [Fact]
    public void MultiplePaymentCalendarsAreJoined()
    {
        var conventions = Terms(businessDay: "Following", calendars: ["PAY", "OTHER"], lag: 1);
        Create(conventions, out var spec, out var refusal).Should().BeTrue(refusal);
        var april = ScheduleGenerator.Generate(spec!).Periods[2];
        april.PaymentDate.Should().Be(new DateOnly(2026, 5, 5));
    }

    [Fact]
    public void CommonCalendarIsUsedOnlyWhenNoPaymentCalendarsAreNamed()
    {
        var conventions = Terms(businessDay: "Following", lag: 1);
        Create(conventions, out var spec, out var refusal, commonCalendarId: "PAY").Should().BeTrue(refusal);
        spec!.PaymentAdjustment!.Calendar!.CalendarId.Should().Be("PAY");

        conventions = Terms(businessDay: "Following", calendars: ["OTHER"]);
        Create(conventions, out spec, out refusal, commonCalendarId: "MISSING").Should().BeTrue(refusal);
        spec!.PaymentAdjustment!.Calendar!.CalendarId.Should().Be("OTHER");
    }

    [Fact]
    public void ExplicitUnknownCalendarNeverFallsBack()
    {
        var conventions = Terms(calendars: ["MISSING"]);
        Create(conventions, out var spec, out var refusal, commonCalendarId: "PAY").Should().BeFalse();
        spec.Should().BeNull();
        refusal.Should().Contain("could not be resolved");
    }

    [Theory]
    [InlineData("Following", 0)]
    [InlineData(null, 1)]
    public void CalendarRequiredForAdjustmentOrLag(string? businessDay, int lag)
    {
        Create(Terms(businessDay: businessDay, lag: lag), out var spec, out var refusal).Should().BeFalse();
        spec.Should().BeNull();
        refusal.Should().Contain("calendar");
    }

    [Fact]
    public void CalendarFreeUnadjustedDeclarationWorks()
    {
        Create(Terms(lag: 0), out var spec, out var refusal).Should().BeTrue(refusal);
        spec!.PaymentAdjustment!.Calendar.Should().BeNull();
    }

    [Theory]
    [InlineData("Daily", ScheduleTenorUnit.Day, 1)]
    [InlineData("Weekly", ScheduleTenorUnit.Week, 1)]
    [InlineData("BiWeekly", ScheduleTenorUnit.Week, 2)]
    [InlineData("Monthly", ScheduleTenorUnit.Month, 1)]
    [InlineData("BiMonthly", ScheduleTenorUnit.Month, 2)]
    [InlineData("Quarterly", ScheduleTenorUnit.Month, 3)]
    [InlineData("SemiAnnual", ScheduleTenorUnit.Month, 6)]
    [InlineData("Annual", ScheduleTenorUnit.Month, 12)]
    [InlineData("AtMaturity", ScheduleTenorUnit.Term, 1)]
    public void CadenceUsesTheDeclaredTenor(string frequency, ScheduleTenorUnit unit, int count)
    {
        Create(Terms(lag: 0), out var spec, out var refusal, frequency).Should().BeTrue(refusal);
        spec!.Tenor.Unit.Should().Be(unit);
        spec.Tenor.Count.Should().Be(count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not a frequency")]
    [InlineData("Irregular")]
    public void UnresolvedCadenceDoesNotBecomeSemiAnnual(string? frequency)
    {
        Create(Terms(lag: 0), out var spec, out var refusal, frequency).Should().BeFalse();
        spec.Should().BeNull();
        refusal.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void BackwardEndOfMonthAndLastRegularDateReachTheKernel()
    {
        var conventions = Terms(direction: "Backward", eom: true,
            lastRegularDate: new DateOnly(2026, 6, 30));
        Create(conventions, out var spec, out var refusal).Should().BeTrue(refusal);
        spec!.Direction.Should().Be(ScheduleDirection.Backward);
        spec.Roll.Should().Be(ScheduleRoll.EndOfMonth);
        var periods = ScheduleGenerator.Generate(spec).Periods;
        periods[0].UnadjustedEnd.Should().Be(new DateOnly(2026, 2, 28));
        periods[^1].UnadjustedStart.Should().Be(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public void ForwardFirstAndLastRegularDatesReachTheKernel()
    {
        var conventions = Terms(direction: "Forward", eom: true,
            firstRegularDate: new DateOnly(2026, 2, 28), lastRegularDate: new DateOnly(2026, 6, 30));
        Create(conventions, out var spec, out var refusal).Should().BeTrue(refusal);
        spec!.FirstRegularDate.Should().Be(new DateOnly(2026, 2, 28));
        spec.LastRegularDate.Should().Be(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public void FirstRegularDateWithBackwardDirectionRetainsTheKernelRefusal()
    {
        var conventions = Terms(direction: "Backward", firstRegularDate: new DateOnly(2026, 2, 28));
        Create(conventions, out var spec, out var refusal).Should().BeFalse();
        spec.Should().BeNull();
        refusal.Should().Contain("first regular date requires forward generation");
    }

    [Theory]
    [InlineData("15", true)]
    [InlineData("AnchorDay", true)]
    [InlineData("EOM", false)]
    public void ContradictoryMonthEndTermsRefuse(string roll, bool eom)
    {
        Create(Terms(roll: roll, eom: eom), out var spec, out var refusal).Should().BeFalse();
        spec.Should().BeNull();
        refusal.Should().Contain("conflicts");
    }

    [Theory]
    [InlineData("unknown", null, null, null)]
    [InlineData(null, "unknown", null, null)]
    [InlineData(null, null, "unknown", null)]
    [InlineData(null, null, null, "0")]
    public void UnknownVocabularyIsNotDefaulted(string? businessDay, string? roll, string? stub, string? direction)
    {
        Create(Terms(businessDay: businessDay, roll: roll, stub: stub, direction: direction),
            out var spec, out var refusal).Should().BeFalse();
        spec.Should().BeNull();
        refusal.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void InvalidGridAndNegativeLagRefuse()
    {
        Create(Terms(firstRegularDate: Start.AddDays(-1)), out var spec, out var refusal).Should().BeFalse();
        spec.Should().BeNull();
        refusal.Should().NotBeNullOrWhiteSpace();
        Create(Terms(lag: -1), out spec, out refusal).Should().BeFalse();
        spec.Should().BeNull();
        refusal.Should().Contain("negative");
    }

    [Fact]
    public void BlankCalendarIsNotDiscarded()
    {
        Create(Terms(calendars: [" "]), out var spec, out var refusal).Should().BeFalse();
        spec.Should().BeNull();
        refusal.Should().Contain("blank");
    }

    [Fact]
    public void EqualOrReversedDatesRefuse()
    {
        ScheduleSpecFactory.TryCreate(Terms(lag: 0), End, Start, "Monthly", Calendars, null,
            out var spec, out var refusal).Should().BeFalse();
        spec.Should().BeNull();
        refusal.Should().Contain("termination");
        ScheduleSpecFactory.TryCreate(Terms(lag: 0), Start, Start, "Monthly", Calendars, null,
            out spec, out refusal).Should().BeFalse();
    }

    private static bool Create(ScheduleConventions terms, out ScheduleSpec? spec, out string? refusal,
        string? frequency = "Monthly", string? commonCalendarId = null)
        => ScheduleSpecFactory.TryCreate(terms, Start, End, frequency, Calendars, commonCalendarId,
            out spec, out refusal);

    private static ScheduleConventions Terms(string? businessDay = null, string[]? calendars = null,
        int? lag = null, bool? accrualAdjusted = null, string? roll = null, bool? eom = null,
        string? direction = null, string? stub = null, DateOnly? firstRegularDate = null,
        DateOnly? lastRegularDate = null)
        => new(Text(businessDay), Option(accrualAdjusted), ListModule.OfSeq(calendars ?? []), Option(lag),
            Text(roll), Option(eom), Option(firstRegularDate), Option(lastRegularDate), Text(direction),
            Text(stub), null, null);

    private static FSharpOption<string>? Text(string? value)
        => value is null ? null : FSharpOption<string>.Some(value);

    private static FSharpOption<T>? Option<T>(T? value) where T : struct
        => value.HasValue ? FSharpOption<T>.Some(value.Value) : null;
}
