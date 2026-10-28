using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Application.Composition;

public sealed class SecurityMasterTermsCapabilityFloorTests
{
    [Fact]
    public void CurrentRelease_RequiresAnActiveFloorForDeclaredTermWrites()
        => SecurityMasterTermsCapabilityHostedService.WritesDeclaredTerms().Should().BeTrue(
            "the 5b-2 writer must refuse an unactivated deployment floor");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingTableOrUnactivatedFloor_PermitsOnlyPreContractWriters(bool tableExists)
    {
        var state = new SecurityMasterTermsCapabilityState(tableExists, null);
        var legacy = () => SecurityMasterTermsCapabilityHostedService.EnsureCompatible(state, false, NullLogger.Instance);
        var declaring = () => SecurityMasterTermsCapabilityHostedService.EnsureCompatible(state, true, NullLogger.Instance);
        legacy.Should().NotThrow();
        declaring.Should().Throw<StartupRefusedException>().WithMessage("*installed and activated*refusing*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SupportedActivatedFloor_AllowsTheFrozenCapability(bool writesDeclaredTerms)
    {
        var action = () => SecurityMasterTermsCapabilityHostedService.EnsureCompatible(
            new SecurityMasterTermsCapabilityState(true, Floor()), writesDeclaredTerms, NullLogger.Instance);
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData("future-mapping", "contractual-schedule-kernel-v1")]
    [InlineData("security-master-declared-schedule-v1", "future-kernel")]
    [InlineData("", "contractual-schedule-kernel-v1")]
    [InlineData("SECURITY-MASTER-DECLARED-SCHEDULE-V1", "contractual-schedule-kernel-v1")]
    public void UnknownNewerOrMalformedFloor_RefusesEvenLegacyWriters(string mapping, string kernel)
    {
        var action = () => SecurityMasterTermsCapabilityHostedService.EnsureCompatible(
            new SecurityMasterTermsCapabilityState(true, Floor() with
            {
                MinimumDeclaredMappingVersion = mapping,
                MinimumKernelVersion = kernel
            }), false, NullLogger.Instance);
        var refusal = action.Should().Throw<StartupRefusedException>().WithMessage("*unknown, newer, or invalid*refusing*").Which;
        HostStartupEscalation.IsRefusal(refusal).Should().BeTrue();
        HostStartupEscalation.IsRefusal(new AggregateException(new InvalidOperationException("ordinary worker failure"), refusal))
            .Should().BeTrue("the WPF hosted-services path must not degrade past a floor refusal");
    }

    [Fact]
    public void CorruptFloorState_RefusesInsteadOfTreatingItAsUnactivated()
    {
        var inconsistent = () => SecurityMasterTermsCapabilityHostedService.EnsureCompatible(
            new SecurityMasterTermsCapabilityState(false, Floor()), false, NullLogger.Instance);
        var missingEvidence = () => SecurityMasterTermsCapabilityHostedService.EnsureCompatible(
            new SecurityMasterTermsCapabilityState(true, Floor() with { ChangeRecord = " " }), false, NullLogger.Instance);
        inconsistent.Should().Throw<StartupRefusedException>();
        missingEvidence.Should().Throw<StartupRefusedException>();
    }

    [Fact]
    public async Task UnconfiguredHost_DoesNotResolveThePersistenceReader()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBusinessDayCalendarSource>(new StaticBusinessDayCalendarSource([]));
        services.AddSingleton<ISecurityMasterTermsCapabilityStore>(_ => throw new InvalidOperationException("Must remain lazy."));
        services.AddSecurityMasterTermsCapability(_ => throw new InvalidOperationException("Custom calendar source wins."));
        await using var provider = services.BuildServiceProvider();
        await provider.GetServices<IHostedService>().Single().StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ConfiguredHost_ReadsAndEnforcesFloorBeforeAnnouncingCapability()
    {
        var store = Substitute.For<ISecurityMasterTermsCapabilityStore>();
        store.ReadAsync(Arg.Any<CancellationToken>()).Returns(new SecurityMasterTermsCapabilityState(true,
            Floor() with { MinimumKernelVersion = "newer-kernel" }));
        await using var provider = ConfiguredHost(store);
        var action = () => provider.GetServices<IHostedService>().Single().StartAsync(CancellationToken.None);
        var refusal = await action.Should().ThrowAsync<StartupRefusedException>().WithMessage("*unknown, newer, or invalid*");
        HostStartupEscalation.IsRefusal(refusal.Which).Should().BeTrue();
        provider.GetServices<IHostedService>().Single().Should().NotBeAssignableTo<IStartupRefusalGuard>(
            "floor verification performs database I/O only during ordinary hosted-service start");
        await store.Received(1).ReadAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistenceFailure_RefusesStartupWithBoundedReason(bool providerThrowsRefusal)
    {
        const string privateProviderMessage = "ConnectionString=Host=test;Password=synthetic-test-password;";
        Exception providerFailure = providerThrowsRefusal
            ? new StartupRefusedException(privateProviderMessage)
            : new InvalidDataException(privateProviderMessage);
        var store = Substitute.For<ISecurityMasterTermsCapabilityStore>();
        store.ReadAsync(Arg.Any<CancellationToken>()).Returns<Task<SecurityMasterTermsCapabilityState>>(
            _ => throw providerFailure);
        await using var provider = ConfiguredHost(store);
        var action = () => provider.GetServices<IHostedService>().Single().StartAsync(CancellationToken.None);
        var refusal = await action.Should().ThrowAsync<StartupRefusedException>()
            .WithMessage("Security Master minimum terms capability could not be verified; refusing configured writer startup. Verify connectivity, permissions and migration 057.");
        HostStartupEscalation.IsRefusal(refusal.Which).Should().BeTrue();
        refusal.Which.ToString().Should().NotContain("synthetic-test-password");
        refusal.Which.InnerException.Should().BeNull("provider exception text may contain credentials");
    }

    [Fact]
    public async Task FloorReadCancellation_PropagatesWithoutBecomingARefusal()
    {
        using var cancellation = new CancellationTokenSource();
        var store = Substitute.For<ISecurityMasterTermsCapabilityStore>();
        store.ReadAsync(Arg.Any<CancellationToken>()).Returns<Task<SecurityMasterTermsCapabilityState>>(
            _ =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            });
        await using var provider = ConfiguredHost(store);
        var action = () => provider.GetServices<IHostedService>().Single().StartAsync(cancellation.Token);
        var thrown = await action.Should().ThrowAsync<OperationCanceledException>();
        HostStartupEscalation.IsRefusal(thrown.Which).Should().BeFalse();
    }

    [Fact]
    public async Task UnexpectedProviderCancellation_RefusesInsteadOfDegradingPastVerification()
    {
        var store = Substitute.For<ISecurityMasterTermsCapabilityStore>();
        store.ReadAsync(Arg.Any<CancellationToken>()).Returns<Task<SecurityMasterTermsCapabilityState>>(
            _ => throw new OperationCanceledException("Provider cancelled without a host cancellation request."));
        await using var provider = ConfiguredHost(store);
        var action = () => provider.GetServices<IHostedService>().Single().StartAsync(CancellationToken.None);
        var refusal = await action.Should().ThrowAsync<StartupRefusedException>();
        HostStartupEscalation.IsRefusal(refusal.Which).Should().BeTrue();
    }

    [Fact]
    public void SharedAndDesktopExtension_LazilyRegisterTheSamePostgresReader()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SecurityMasterOptions { ConnectionString = "Host=not-contacted" });
        services.AddSecurityMasterTermsCapability(_ => ".");
        services.AddSecurityMasterTermsCapability(_ => ".");
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISecurityMasterTermsCapabilityStore>()
            .Should().BeOfType<PostgresSecurityMasterTermsCapabilityStore>();
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(ISecurityMasterTermsCapabilityStore));
    }

    private static ServiceProvider ConfiguredHost(ISecurityMasterTermsCapabilityStore store)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SecurityMasterOptions { ConnectionString = "Host=not-contacted" });
        services.AddSingleton<IBusinessDayCalendarSource>(new StaticBusinessDayCalendarSource([]));
        services.AddSingleton(store);
        services.AddSecurityMasterTermsCapability(_ => ".");
        return services.BuildServiceProvider();
    }

    private static SecurityMasterTermsCapabilityFloor Floor() => new(
        SecurityVersionedTerms.DeclaredScheduleMappingVersion, ScheduleGenerator.KernelVersion,
        "test-change-record", "test-operator", new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero));
}
