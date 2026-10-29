using System.Globalization;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Subscriptions.Services;
using Meridian.Application.UI;
using Meridian.Core.Config;
using Meridian.Core.Subscriptions.Models;

namespace Meridian.Tests.Application.Subscriptions;

public sealed class PortfolioImportReliabilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-import-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public async Task AlpacaImport_ParsesProviderNumbersIndependentlyOfCulture(string culture)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var responses = new List<TrackedContent>();
            using var client = Client((request, _) =>
            {
                var body = request.RequestUri!.AbsolutePath.EndsWith("positions", StringComparison.Ordinal)
                    ? """[{"symbol":"AAPL","qty":"1.5","market_value":"1234.56","avg_entry_price":"800.25","unrealized_pl":"-12.34"}]"""
                    : """{"account_number":"test-account","portfolio_value":"2345.67","cash":"111.22","buying_power":"333.44"}""";
                return Task.FromResult(Response(body, responses));
            });

            var result = await Service(client).ImportFromAlpacaAsync(new PortfolioImportOptions());

            result.Success.Should().BeTrue();
            result.Portfolio!.Positions.Should().ContainSingle();
            var position = result.Portfolio.Positions[0];
            position.Quantity.Should().Be(1.5m);
            position.MarketValue.Should().Be(1234.56m);
            position.AverageCost.Should().Be(800.25m);
            position.UnrealizedPnL.Should().Be(-12.34m);
            result.Portfolio.TotalValue.Should().Be(2345.67m);
            result.Portfolio.CashBalance.Should().Be(111.22m);
            result.Portfolio.BuyingPower.Should().Be(333.44m);
            responses.Should().HaveCount(2).And.OnlyContain(content => content.Disposed);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("alpaca", "http")]
    [InlineData("alpaca", "json")]
    [InlineData("ib", "http")]
    [InlineData("ib", "json")]
    public async Task Import_DisposesResponsesWhenSecondRequestFails(string broker, string failure)
    {
        var responses = new List<TrackedContent>();
        var calls = 0;
        using var client = Client((_, _) =>
        {
            calls++;
            var body = calls == 1
                ? broker == "alpaca" ? "[]" : """[{"accountId":"test-account"}]"""
                : "invalid-json";
            return Task.FromResult(Response(body, responses,
                calls == 2 && failure == "http" ? HttpStatusCode.BadGateway : HttpStatusCode.OK));
        });

        var result = await Service(client).ImportFromBrokerAsync(new(broker, new()));

        result.Success.Should().BeFalse();
        responses.Should().HaveCount(2).And.OnlyContain(content => content.Disposed);
    }

    [Theory]
    [InlineData("alpaca")]
    [InlineData("ib")]
    public async Task Import_PropagatesCallerCancellationDuringRequest(string broker)
    {
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = Client(async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        });
        var import = Service(client).ImportFromBrokerAsync(new(broker, new()), cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => import);
    }

    [Fact]
    public async Task Import_RequestTimeoutRemainsAnOrdinaryFailure()
    {
        using var client = Client((_, _) => throw new TaskCanceledException("provider timeout"));
        var result = await Service(client).ImportFromAlpacaAsync(new());
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task AlpacaImport_PreservesMissingAndMalformedFieldHandling()
    {
        using var client = Client((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("positions", StringComparison.Ordinal)
                ? """[{"symbol":"AAPL","market_value":"bad","avg_entry_price":null,"unrealized_pl":"bad"}]"""
                : """{"account_number":"test-account","portfolio_value":"bad"}""")
        }));

        var result = await Service(client).ImportFromAlpacaAsync(new());

        result.Success.Should().BeTrue();
        result.Portfolio!.Positions[0].Quantity.Should().Be(0);
        result.Portfolio.Positions[0].MarketValue.Should().BeNull();
        result.Portfolio.Positions[0].AverageCost.Should().BeNull();
        result.Portfolio.Positions[0].UnrealizedPnL.Should().BeNull();
        result.Portfolio.TotalValue.Should().BeNull();
        result.Portfolio.CashBalance.Should().BeNull();
    }

    [Fact]
    public async Task AlpacaImport_MalformedQuantityRemainsAnImportFailure()
    {
        using var client = Client((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("positions", StringComparison.Ordinal)
                ? """[{"symbol":"AAPL","qty":"bad"}]""" : "{}")
        }));
        (await Service(client).ImportFromAlpacaAsync(new())).Success.Should().BeFalse();
    }

    private PortfolioImportService Service(HttpClient client)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new AppConfig
        {
            Alpaca = new AlpacaOptions("test-key", "test-secret"),
            IB = new(),
            IBClientPortal = new(Enabled: true, BaseUrl: "https://ib.invalid", AllowSelfSignedCertificates: false)
        }));
        var store = new ConfigStore(path);
        return new PortfolioImportService(store, new WatchlistService(store), client);
    }

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new Handler(send));

    private static HttpResponseMessage Response(string body, List<TrackedContent> responses, HttpStatusCode status = HttpStatusCode.OK)
    {
        var content = new TrackedContent(body);
        responses.Add(content);
        return new HttpResponseMessage(status) { Content = content };
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }

    private sealed class TrackedContent(string body) : StringContent(body)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
