using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class SecurityMasterServiceSnapshotTests
{
    [Fact]
    public async Task CreateAsync_WaitsForFreshCapabilityReadBeforeAnyCanonicalMutation()
    {
        var pending = new TaskCompletionSource<SecurityMasterTermsCapabilityState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var floorStore = Substitute.For<ISecurityMasterTermsCapabilityStore>();
        floorStore.ReadAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            entered.TrySetResult();
            return pending.Task;
        });
        using var provider = CapabilityProvider(floorStore);
        var (service, events, snapshots, store) = CapabilityWriter(provider.GetRequiredService<ISecurityMasterTermsCapabilityVerifier>());
        using var _ = service;
        var command = service.CreateAsync(CapabilityCreateRequest());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        command.IsCompleted.Should().BeFalse();
        await AssertNoCapabilityMutations(events, snapshots, store);

        pending.SetResult(new SecurityMasterTermsCapabilityState(true, SupportedFloor()));
        await command.WaitAsync(TimeSpan.FromSeconds(5));
        await events.Received(1).AppendAsync(Arg.Any<Guid>(), 0,
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Amend")]
    [InlineData("Deactivate")]
    public async Task CanonicalCommand_UnknownFloorRefusesBeforeAnyMutation(string operation)
    {
        var floorStore = Substitute.For<ISecurityMasterTermsCapabilityStore>();
        floorStore.ReadAsync(Arg.Any<CancellationToken>()).Returns(new SecurityMasterTermsCapabilityState(true,
            SupportedFloor() with { MinimumKernelVersion = "future-kernel" }));
        using var provider = CapabilityProvider(floorStore);
        var (service, events, snapshots, store) = CapabilityWriter(provider.GetRequiredService<ISecurityMasterTermsCapabilityVerifier>());
        using var _ = service;
        var request = CapabilityCreateRequest();
        Func<Task> action = () => operation switch
        {
            "Create" => service.CreateAsync(request),
            "Amend" => service.AmendTermsAsync(new AmendSecurityTermsRequest(request.SecurityId, 1,
                null, null, [], [], request.EffectiveFrom, "test", "test", null, "amend")),
            _ => service.DeactivateAsync(new DeactivateSecurityRequest(request.SecurityId, 1,
                request.EffectiveFrom, "test", "test", null, "deactivate"))
        };
        await action.Should().ThrowAsync<StartupRefusedException>().WithMessage("*unknown, newer, or invalid*");
        await AssertNoCapabilityMutations(events, snapshots, store);
    }

    [Fact]
    public async Task CreateAsync_MissingRequiredFloorRefusesBeforeAnyMutation()
    {
        // Simulate the future catalog raise without mutating the global catalog: its required-floor
        // decision is already tested against the real shared policy in the startup floor suite.
        var verifier = Substitute.For<ISecurityMasterTermsCapabilityVerifier>();
        verifier.VerifyAsync(Arg.Any<CancellationToken>()).Returns<Task>(_ =>
        {
            SecurityMasterTermsCapabilityVerifier.EnsureCompatible(
                new SecurityMasterTermsCapabilityState(true, null), true, NullLogger.Instance);
            return Task.CompletedTask;
        });
        var (service, events, snapshots, store) = CapabilityWriter(verifier);
        using var _ = service;
        var action = () => service.CreateAsync(CapabilityCreateRequest());
        await action.Should().ThrowAsync<StartupRefusedException>().WithMessage("*installed and activated*");
        await AssertNoCapabilityMutations(events, snapshots, store);
    }

    [Fact]
    public async Task CreateAsync_RechecksFloorAfterSuccessfulHostStartup()
    {
        var floorStore = Substitute.For<ISecurityMasterTermsCapabilityStore>();
        floorStore.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            new SecurityMasterTermsCapabilityState(true, SupportedFloor()),
            new SecurityMasterTermsCapabilityState(true, SupportedFloor() with { MinimumDeclaredMappingVersion = "future-mapping" }));
        using var provider = CapabilityProvider(floorStore);
        await provider.GetServices<IHostedService>().Single().StartAsync(CancellationToken.None);
        var (service, events, snapshots, store) = CapabilityWriter(provider.GetRequiredService<ISecurityMasterTermsCapabilityVerifier>());
        using var _ = service;
        var action = () => service.CreateAsync(CapabilityCreateRequest());
        await action.Should().ThrowAsync<StartupRefusedException>();
        await floorStore.Received(2).ReadAsync(Arg.Any<CancellationToken>());
        await AssertNoCapabilityMutations(events, snapshots, store);
    }

    [Fact]
    public async Task CreateAsync_ConfiguredManualWriterCannotBypassFloorByOmittingOptionalDependency()
    {
        var (service, events, snapshots, store) = CapabilityWriter(null, "not-a-connection-string");
        using var _ = service;
        var action = () => service.CreateAsync(CapabilityCreateRequest());
        await action.Should().ThrowAsync<StartupRefusedException>().WithMessage("*could not be verified*");
        await AssertNoCapabilityMutations(events, snapshots, store);
    }

    [Fact]
    public async Task SharedDependencyInjection_SuppliesVerifierToTheWriterConstructor()
    {
        var floorStore = Substitute.For<ISecurityMasterTermsCapabilityStore>();
        floorStore.ReadAsync(Arg.Any<CancellationToken>()).Returns(new SecurityMasterTermsCapabilityState(true,
            SupportedFloor() with { MinimumKernelVersion = "future-kernel" }));
        using var provider = CapabilityProvider(floorStore);
        var events = Substitute.For<ISecurityMasterEventStore>();
        var snapshots = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = CreateDurableProjectionStore();
        // The explicit in-memory options would bypass a fallback verifier, so only injection of
        // the graph's configured verifier can make this command refuse.
        using var service = ActivatorUtilities.CreateInstance<SecurityMasterService>(provider, events, snapshots, store,
            new SecurityMasterAggregateRebuilder(events, snapshots), new SecurityMasterOptions(),
            NullLogger<SecurityMasterService>.Instance);
        var action = () => service.CreateAsync(CapabilityCreateRequest());
        await action.Should().ThrowAsync<StartupRefusedException>().WithMessage("*unknown, newer, or invalid*");
        await AssertNoCapabilityMutations(events, snapshots, store);
    }

    private static ServiceProvider CapabilityProvider(ISecurityMasterTermsCapabilityStore floorStore)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SecurityMasterOptions { ConnectionString = "Host=not-contacted" });
        services.AddSingleton(floorStore);
        services.AddSecurityMasterTermsCapability(_ => ".");
        return services.BuildServiceProvider();
    }

    private static (SecurityMasterService Service, ISecurityMasterEventStore Events,
        ISecurityMasterSnapshotStore Snapshots, ISecurityMasterStore Store) CapabilityWriter(ISecurityMasterTermsCapabilityVerifier? verifier, string connectionString = "")
    {
        var events = Substitute.For<ISecurityMasterEventStore>();
        var snapshots = Substitute.For<ISecurityMasterSnapshotStore>();
        var store = CreateDurableProjectionStore();
        var options = new SecurityMasterOptions { SnapshotIntervalVersions = 50, ConnectionString = connectionString };
        return (new SecurityMasterService(events, snapshots, store, new SecurityMasterAggregateRebuilder(events, snapshots),
            options, NullLogger<SecurityMasterService>.Instance, termsCapabilityVerifier: verifier), events, snapshots, store);
    }

    private static async Task AssertNoCapabilityMutations(ISecurityMasterEventStore events,
        ISecurityMasterSnapshotStore snapshots, ISecurityMasterStore store)
    {
        await events.DidNotReceive().AppendAsync(Arg.Any<Guid>(), Arg.Any<long>(),
            Arg.Any<IReadOnlyList<SecurityMasterEventEnvelope>>(), Arg.Any<CancellationToken>());
        await snapshots.DidNotReceive().SaveAsync(Arg.Any<SecuritySnapshotRecord>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().UpsertProjectionAsync(Arg.Any<SecurityProjectionRecord>(), Arg.Any<CancellationToken>());
    }

    private static CreateSecurityRequest CapabilityCreateRequest() => new(
        Guid.NewGuid(), "Deposit",
        JsonSerializer.SerializeToElement(new { displayName = "Capability deposit", currency = "USD", issuerName = "Gate Bank" }),
        JsonSerializer.SerializeToElement(new
        {
            depositType = "TimeDeposit",
            institutionName = "Gate Bank",
            maturity = "2030-01-31",
            interestRate = 0.05m,
            dayCount = "ACT/360",
            isCallable = false
        }),
        [new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "CAPABILITY-TEST", true,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))],
        new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero), "test", "test-operator", null, "create");

    private static SecurityMasterTermsCapabilityFloor SupportedFloor() => new(
        SecurityVersionedTerms.DeclaredScheduleMappingVersion, ScheduleGenerator.KernelVersion,
        "ephemeral-test-change", "test-operator", new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero));
}
