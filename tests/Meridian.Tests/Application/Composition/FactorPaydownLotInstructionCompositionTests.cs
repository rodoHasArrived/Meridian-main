using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Instruments.AssetOperations;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Application.Composition;

/// <summary>
/// Factor-paydown preparation composes like the amortization preview: a host without one of its
/// authorities still validates and starts, and preparation refuses the missing authority before it
/// reads any source record. No operator route calls the service yet.
/// </summary>
public sealed class FactorPaydownLotInstructionCompositionTests
{
    [Theory]
    [InlineData(0, "authoritative ledger journal store")]
    [InlineData(1, "authoritative Security Master store")]
    [InlineData(3, "authoritative book-position store")]
    [InlineData(7, "factor-paydown projector")]
    [InlineData(14, "authoritative ledger journal store")]
    [InlineData(13, "authoritative Security Master store")]
    [InlineData(11, "authoritative book-position store")]
    public async Task Register_WithIncompleteAuthority_PreservesStartupAndRefusesBeforeReading(
        int configuredAuthorities, string missingAuthority)
    {
        var (services, lots, securities, positions, projector) = Services(configuredAuthorities);
        new LedgerFeatureRegistration().Register(services, CompositionOptions.Default);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var preparation = provider.GetRequiredService<FactorPaydownLotInstructionService>();
        preparation.Should().BeSameAs(provider.GetRequiredService<FactorPaydownLotInstructionService>());

        var prepare = () => preparation.PrepareAsync(Request(), "preparer", DateTimeOffset.UtcNow);

        await prepare.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*no {missingAuthority} is configured*");
        lots.ReceivedCalls().Should().BeEmpty();
        securities.ReceivedCalls().Should().BeEmpty();
        positions.ReceivedCalls().Should().BeEmpty();
        projector.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Register_WithCompleteAuthority_ReadsTheRegisteredSecurityMasterStore()
    {
        var (services, lots, securities, positions, projector) = Services(15);
        new LedgerFeatureRegistration().Register(services, CompositionOptions.Default);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var request = Request();

        var prepare = () => provider.GetRequiredService<FactorPaydownLotInstructionService>()
            .PrepareAsync(request, "preparer", DateTimeOffset.UtcNow);

        await prepare.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*authoritative Security Master projection*");
        await securities.Received(1).GetProjectionAsync(request.SecurityId, Arg.Any<CancellationToken>());
        lots.ReceivedCalls().Should().BeEmpty();
        positions.ReceivedCalls().Should().BeEmpty();
        projector.ReceivedCalls().Should().BeEmpty();
    }

    private static (ServiceCollection Services, ILedgerJournalStore Lots, ISecurityMasterStore Securities,
        IInstrumentPositionProjectionStore Positions, IFactorPaydownProjectionService Projector) Services(int configuredAuthorities)
    {
        var lots = Substitute.For<ILedgerJournalStore>();
        var securities = Substitute.For<ISecurityMasterStore>();
        var positions = Substitute.For<IInstrumentPositionProjectionStore>();
        var projector = Substitute.For<IFactorPaydownProjectionService>();
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IAccountingConfigurationService>());
        if ((configuredAuthorities & 1) != 0)
            services.AddSingleton(lots);
        if ((configuredAuthorities & 2) != 0)
            services.AddSingleton(securities);
        if ((configuredAuthorities & 4) != 0)
            services.AddSingleton(positions);
        if ((configuredAuthorities & 8) != 0)
            services.AddSingleton(projector);
        return (services, lots, securities, positions, projector);
    }

    private static FactorPaydownPreparationRequest Request()
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, AccountingBasisKindDto.Gaap,
            "fund-alpha", new DateOnly(2026, 6, 30), 0.95m, 0.90m, "SecurityMaster", "factor-row-2026-06",
            new string('a', 64), DateTimeOffset.Parse("2026-06-30T18:00:00Z"), [], "Assets:Cash",
            "Assets:Investment", "Income:Realized Gain", "Expense:Realized Loss");
}
