using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Options;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Instruments.Options;
using Meridian.Identity.Auth;
using Meridian.ProviderSdk;
using Meridian.Tests.TestHelpers;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class OptionScreeningServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T12:00:00Z");
    [Fact]
    public async Task BothRights_PreserveSignedDeltaAndAllScreenReasonsWithoutClaimingPermission()
    {
        var expiry = new DateOnly(2026, 12, 18);
        var provider = new Mock<IOptionsChainProvider>();
        provider.Setup(value => value.GetExpirationsAsync("XYZ", It.IsAny<CancellationToken>())).ReturnsAsync([expiry]);
        provider.Setup(value => value.GetChainSnapshotAsync("XYZ", expiry, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(expiry));
        var result = await Service(provider.Object).ScreenAsync(new(" xyz ", MaxSpreadFraction: 0.05m, MinOpenInterest: 100));
        Assert.Equal(2, result.Candidates.Count);
        var put = result.Candidates.Single(candidate => candidate.Right == "Put");
        Assert.Equal("-0.3", put.Delta);
        Assert.False(put.PassesScreen);
        Assert.Contains(put.RejectionReasons, reason => reason.Contains("Spread"));
        Assert.Contains(put.RejectionReasons, reason => reason.Contains("Open interest"));
        Assert.Contains("not account permission", result.AuthorityNote);
        Assert.All(result.Candidates, row => Assert.Equal("synthetic-source", row.Source));
    }

    [Fact]
    public async Task MissingGreeks_RemainInTableWithExplicitReason()
    {
        var expiry = new DateOnly(2026, 12, 18);
        var provider = new Mock<IOptionsChainProvider>();
        provider.Setup(value => value.GetExpirationsAsync("XYZ", It.IsAny<CancellationToken>())).ReturnsAsync([expiry]);
        var snapshot = Snapshot(expiry);
        var call = snapshot.Calls.Single();
        var noGreek = new OptionQuote(Now, call.Symbol, call.Contract, 1m, 10, 1.1m, 10, 100m, Delta: null, Source: "synthetic-source");
        provider.Setup(value => value.GetChainSnapshotAsync("XYZ", expiry, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OptionChainSnapshot(Now, "XYZ", 100m, expiry, [100m], [noGreek], []));
        var row = Assert.Single((await Service(provider.Object).ScreenAsync(new("XYZ"))).Candidates);
        Assert.Null(row.Delta);
        Assert.Contains("Delta unavailable", row.RejectionReasons);
    }

    [Fact]
    public async Task ChildContractScopeMismatch_CannotPassAndTinyDecimalsStayFixedPoint()
    {
        var expiry = new DateOnly(2026, 12, 18);
        var provider = new Mock<IOptionsChainProvider>();
        provider.Setup(value => value.GetExpirationsAsync("XYZ", It.IsAny<CancellationToken>())).ReturnsAsync([expiry]);
        var bad = new OptionQuote(Now, "OTHER-C", new("OTHER", 0.00000001m, expiry, OptionRight.Call),
            0.00000001m, 10, 0.00000002m, 10, 100m, Delta: 0.00000001m, Source: "synthetic-source");
        provider.Setup(value => value.GetChainSnapshotAsync("XYZ", expiry, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OptionChainSnapshot(Now, "XYZ", 100m, expiry, [100m], [bad], []));
        var row = Assert.Single((await Service(provider.Object).ScreenAsync(new("XYZ"))).Candidates);
        Assert.False(row.PassesScreen);
        Assert.Contains(row.RejectionReasons, value => value.Contains("does not match"));
        Assert.Equal("0.00000001", row.Strike);
        Assert.Equal("0.00000001", row.Delta);
    }

    [Fact]
    public async Task PartialCoverage_ExposesFailedAndOmittedExpirations()
    {
        var expirations = Enumerable.Range(1, 13).Select(day => new DateOnly(2026, 11, 12).AddDays(day)).ToArray();
        var provider = new Mock<IOptionsChainProvider>();
        provider.Setup(value => value.GetExpirationsAsync("XYZ", It.IsAny<CancellationToken>())).ReturnsAsync(expirations);
        provider.Setup(value => value.GetChainSnapshotAsync("XYZ", It.IsAny<DateOnly>(), 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, DateOnly expiry, int? _, CancellationToken _) => expiry == expirations[1] ? null : Snapshot(expiry));
        var result = await Service(provider.Object).ScreenAsync(new("XYZ"));
        Assert.Equal(11, result.LoadedExpirations.Count);
        Assert.Single(result.FailedExpirations);
        Assert.Single(result.OmittedExpirations);
        provider.Verify(value => value.GetChainSnapshotAsync("XYZ", expirations[12], It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoProvider_AndInvalidCriteria_DoNotReturnFixtureSuccess()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(null).ScreenAsync(new("XYZ")));
        await Assert.ThrowsAsync<ArgumentException>(() => Service(null).ScreenAsync(new("XYZ", MinDelta: 1m, MaxDelta: -1m)));
    }

    [Fact]
    public async Task ExpirationProviderTimeout_IsUnavailableRatherThanAValidEmptyScreen()
    {
        var provider = new Mock<IOptionsChainProvider>();
        provider.Setup(value => value.GetExpirationsAsync("XYZ", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Synthetic expiration discovery timeout"));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(provider.Object).ScreenAsync(new("XYZ")));
        Assert.Contains("expiration discovery is unavailable", failure.Message);
        provider.Verify(value => value.GetChainSnapshotAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnhealthyExpirationProvider_IsUnavailableWithoutQueryingItsCoverage()
    {
        var provider = new Mock<IOptionsChainProvider>();
        var health = new Mock<IProviderConnectionHealthSource>();
        health.Setup(value => value.GetHealthAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderConnectionHealthSnapshot("synthetic", "synthetic", IsHealthy: false, Status: "unhealthy"));
        var chains = new OptionsChainService(new OptionDataCollector(new TestMarketEventPublisher()),
            NullLogger<OptionsChainService>.Instance, health.Object, [provider.Object]);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => new OptionScreeningService(chains, new Clock()).ScreenAsync(new("XYZ")));
        Assert.Contains("expiration discovery is unavailable", failure.Message);
        provider.Verify(value => value.GetExpirationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SuccessfulExpirationDiscovery_OutsideRequestedDte_IsAValidEmptyScreen()
    {
        var provider = new Mock<IOptionsChainProvider>();
        provider.Setup(value => value.GetExpirationsAsync("XYZ", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DateOnly(2027, 1, 15)]);
        var result = await Service(provider.Object).ScreenAsync(new("XYZ", MaxDte: 7));
        Assert.Empty(result.Candidates);
        Assert.Empty(result.LoadedExpirations);
        Assert.Empty(result.FailedExpirations);
        provider.Verify(value => value.GetChainSnapshotAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmptyExpirationPayload_DoesNotProveCoverageBecauseAdaptersAlsoReturnItOnFailure()
    {
        var provider = new Mock<IOptionsChainProvider>();
        provider.Setup(value => value.GetExpirationsAsync("XYZ", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DateOnly>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(provider.Object).ScreenAsync(new("XYZ")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ScreenEndpoint_FailedDiscoveryIsUnavailable_WhileNoEligibleDatesIsSuccessful(bool discoveryFails)
    {
        var provider = new Mock<IOptionsChainProvider>();
        var discovery = provider.Setup(value => value.GetExpirationsAsync("XYZ", It.IsAny<CancellationToken>()));
        if (discoveryFails)
            discovery.ThrowsAsync(new TimeoutException("Synthetic expiration discovery timeout"));
        else
            discovery.ReturnsAsync([new DateOnly(2027, 1, 15)]);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMutationRateLimiter();
        builder.Services.AddSingleton(Service(provider.Object));
        await using var app = builder.Build();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "operator";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewMarketData;
            await next(context);
        });
        app.MapOptionVisualizationEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsJsonAsync("/api/options/screen", new OptionScreenRequest("XYZ", MaxDte: 7));

        Assert.Equal(discoveryFails ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK, response.StatusCode);
        if (discoveryFails)
            Assert.Contains("expiration discovery is unavailable", await response.Content.ReadAsStringAsync());
        else
            Assert.Empty((await response.Content.ReadFromJsonAsync<OptionScreenResult>())!.Candidates);
    }

    private static OptionScreeningService Service(IOptionsChainProvider? provider) => new(new OptionsChainService(
        new OptionDataCollector(new TestMarketEventPublisher()), NullLogger<OptionsChainService>.Instance, provider), new Clock());
    private static OptionChainSnapshot Snapshot(DateOnly expiry) => new(Now, "XYZ", 100m, expiry, [100m],
        [new(Now, $"XYZ-{expiry}-C", new("XYZ", 100m, expiry, OptionRight.Call), 1m, 10, 1.02m, 10, 100m,
            Delta: 0.3m, OpenInterest: 1000, Volume: 500, Source: "synthetic-source")],
        [new(Now, $"XYZ-{expiry}-P", new("XYZ", 100m, expiry, OptionRight.Put), 1m, 10, 2m, 10, 100m,
            Delta: -0.3m, OpenInterest: null, Volume: null, Source: "synthetic-source")]);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
