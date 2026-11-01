using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecurityMasterQueryContract = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Application;

public sealed class SecurityMasterScheduleCompatibilityTests
{
    private static readonly Guid SecurityId = Guid.Parse("33150000-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset AsOf = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);

    // These are the CLR signatures shipped before declared-schedule evidence and calendars.
    private static readonly Type[] ProjectionParameterTypes =
    [
        typeof(Guid),
        typeof(StructuredCashFlowSourceKind),
        typeof(StructuredCashFlowScenario),
        typeof(DateTimeOffset),
        typeof(IReadOnlyList<StructuredCashFlowScheduleEntry>),
        typeof(StructuredCashFlowStaleness),
        typeof(DateTimeOffset?),
        typeof(IReadOnlyList<StructuredFactorScheduleEntry>),
        typeof(StructuredCashFlowTerms),
        typeof(IReadOnlyList<StructuredCashFlowLegSchedule>),
        typeof(string),
        typeof(bool)
    ];

    private static readonly Type[] ServiceParameterTypes =
    [
        typeof(ISecurityMasterCashFlowStore),
        typeof(IEnumerable<IStructuredCashFlowProvider>),
        typeof(SecurityMasterQueryContract),
        typeof(ILogger<SecurityMasterCashFlowService>),
        typeof(IStructuredCashFlowLedgerBridge),
        typeof(ISecurityForwardCurveProvider)
    ];

    [Fact]
    public void Projection_PreservesTheTwelveArgumentClrConstructorAndDeconstructor()
    {
        var constructor = typeof(StructuredCashFlowProjectionDto).GetConstructor(ProjectionParameterTypes);
        constructor.Should().NotBeNull("compiled cash-flow providers bind this exact twelve-argument signature");
        var expected = Projection();
        object?[] arguments =
        [
            expected.SecurityId, expected.SourceKind, expected.Scenario, expected.AsOf,
            expected.Schedule, expected.Staleness, expected.SourceLastUpdatedUtc,
            expected.FactorSchedule, expected.TermsUsed, expected.LegSchedules,
            expected.BlockedReason, expected.IsNormalizedPer100
        ];

        var actual = (StructuredCashFlowProjectionDto)constructor!.Invoke(arguments);
        actual.Should().Be(expected);
        actual.ScheduleEvidence.Should().BeNull();

        var deconstruct = typeof(StructuredCashFlowProjectionDto).GetMethod(
            "Deconstruct", ProjectionParameterTypes.Select(static type => type.MakeByRefType()).ToArray());
        deconstruct.Should().NotBeNull("compiled deconstruction binds twelve by-reference parameters");
        var outputs = new object?[12];
        deconstruct!.Invoke(actual, outputs);
        outputs.Should().Equal(arguments);

        // Source callers must also retain the original deconstruction arity.
        var (id, kind, scenario, asOf, schedule, staleness, updated, factors, terms, legs, reason, normalized) = actual;
        (id, kind, scenario, asOf, schedule, staleness, updated, factors, terms, legs, reason, normalized)
            .Should().Be((expected.SecurityId, expected.SourceKind, expected.Scenario, expected.AsOf,
                expected.Schedule, expected.Staleness, expected.SourceLastUpdatedUtc,
                expected.FactorSchedule, expected.TermsUsed, expected.LegSchedules,
                expected.BlockedReason, expected.IsNormalizedPer100));
    }

    [Fact]
    public void Projection_EvidenceSurvivesRecordCopyAndOnlyPopulatedEvidenceChangesTheWireShape()
    {
        var legacy = Projection();
        var evidence = new StructuredCashFlowScheduleEvidence(
            ScheduleGenerator.KernelVersion,
            Meridian.Contracts.SecurityMaster.ScheduleSpecFactory.DeclaredMappingVersion,
            [], []);
        var declared = legacy with { ScheduleEvidence = evidence };
        var copy = declared with { BlockedReason = "retained refusal" };

        copy.ScheduleEvidence.Should().BeSameAs(evidence);
        copy.Schedule.Should().BeSameAs(legacy.Schedule);
        legacy.ScheduleEvidence.Should().BeNull();

        var options = UiEndpoints.CreateEndpointJsonOptions();
        using var legacyJson = JsonDocument.Parse(JsonSerializer.Serialize(legacy, options));
        using var declaredJson = JsonDocument.Parse(JsonSerializer.Serialize(copy, options));
        legacyJson.RootElement.TryGetProperty("scheduleEvidence", out _).Should().BeFalse();
        declaredJson.RootElement.GetProperty("scheduleEvidence").GetProperty("kernelVersion").GetString()
            .Should().Be(ScheduleGenerator.KernelVersion);
        declaredJson.RootElement.EnumerateObject().Select(static property => property.Name)
            .Should().BeEquivalentTo(legacyJson.RootElement.EnumerateObject()
                .Select(static property => property.Name).Append("scheduleEvidence"));
    }

    [Fact]
    public async Task Service_PreservesTheSixArgumentClrConstructorAndLegacySourceCalls()
    {
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        var query = Substitute.For<SecurityMasterQueryContract>();
        var bridge = Substitute.For<IStructuredCashFlowLedgerBridge>();
        var curves = Substitute.For<ISecurityForwardCurveProvider>();
        var logger = NullLogger<SecurityMasterCashFlowService>.Instance;
        var retained = new SecurityCashFlowSourceDto(SecurityId, StructuredCashFlowSourceKind.CalculatedBullet,
            AsOf, false, null, null);
        store.GetSourceAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(retained);
        var constructor = typeof(SecurityMasterCashFlowService).GetConstructor(ServiceParameterTypes);
        constructor.Should().NotBeNull("compiled composition clients bind this exact six-argument signature");
        var reflected = (SecurityMasterCashFlowService)constructor!.Invoke([store, Array.Empty<IStructuredCashFlowProvider>(), query, logger, bridge, curves]);

        // Positional nulls are valid old calls and must not become ambiguous with the calendar overload.
        SecurityMasterCashFlowService[] services =
        [
            reflected,
            new(store, [], query, logger),
            new(store, [], query, logger, null),
            new(store, [], query, logger, null, null),
            new(store, [], query, logger, bridge, curves)
        ];
        foreach (var service in services)
        {
            service.CalendarSource.Should().BeNull();
            (await service.GetCashFlowSourceAsync(SecurityId)).Should().BeSameAs(retained);
        }

        var calendars = new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]);
        var calendarConstructor = typeof(SecurityMasterCashFlowService).GetConstructor(
            [typeof(IBusinessDayCalendarSource), .. ServiceParameterTypes]);
        calendarConstructor.Should().NotBeNull("compiled calendar-aware hosts bind this exact seven-argument signature");
        var reflectedCalendar = (SecurityMasterCashFlowService)calendarConstructor!.Invoke(
            [calendars, store, Array.Empty<IStructuredCashFlowProvider>(), query, logger, bridge, curves]);
        reflectedCalendar.CalendarSource.Should().BeSameAs(calendars);
        (await reflectedCalendar.GetCashFlowSourceAsync(SecurityId)).Should().BeSameAs(retained);
        var calendarAware = new SecurityMasterCashFlowService(calendars, store, [], query, logger);
        calendarAware.CalendarSource.Should().BeSameAs(calendars);
        (await calendarAware.GetCashFlowSourceAsync(SecurityId)).Should().BeSameAs(retained);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Service_TypeRegistrationSelectsTheCalendarConstructorWithoutAmbiguity(
        bool registerCalendar, bool registerOptionalPorts)
    {
        var services = new ServiceCollection();
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        var query = Substitute.For<SecurityMasterQueryContract>();
        var calendars = new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]);
        var retained = new SecurityCashFlowSourceDto(SecurityId, StructuredCashFlowSourceKind.CalculatedBullet,
            AsOf, false, null, null);
        store.GetSourceAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(retained);
        services.AddSingleton(store);
        services.AddSingleton(query);
        services.AddSingleton<ILogger<SecurityMasterCashFlowService>>(NullLogger<SecurityMasterCashFlowService>.Instance);
        if (registerCalendar)
        {
            services.AddSingleton<IBusinessDayCalendarSource>(calendars);
        }

        if (registerOptionalPorts)
        {
            services.AddSingleton(Substitute.For<IStructuredCashFlowLedgerBridge>());
            services.AddSingleton(Substitute.For<ISecurityForwardCurveProvider>());
        }

        // This is the production type registration, exercising the real DI constructor selector.
        services.AddSingleton<ISecurityMasterCashFlowService, SecurityMasterCashFlowService>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var resolved = provider.GetRequiredService<ISecurityMasterCashFlowService>();
        var actual = resolved.Should().BeOfType<SecurityMasterCashFlowService>().Subject;
        actual.CalendarSource.Should().BeSameAs(registerCalendar ? calendars : null);
        provider.GetRequiredService<ISecurityMasterCashFlowService>().Should().BeSameAs(resolved);
        (await actual.GetCashFlowSourceAsync(SecurityId)).Should().BeSameAs(retained);
    }

    private static StructuredCashFlowProjectionDto Projection()
        => new(SecurityId, StructuredCashFlowSourceKind.CalculatedBullet, StructuredCashFlowScenario.Base, AsOf,
            [new StructuredCashFlowScheduleEntry(AsOf.AddMonths(6), 100m, 3m, 0m)],
            StructuredCashFlowStaleness.Fresh, AsOf.AddHours(-1),
            [new StructuredFactorScheduleEntry(new DateOnly(2026, 10, 28), 1m)],
            StructuredCashFlowTerms.Empty, [], null, false);
}
