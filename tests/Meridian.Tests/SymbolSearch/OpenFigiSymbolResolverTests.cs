using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Catalog;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Meridian.Infrastructure.Adapters.OpenFigi;
using Meridian.Tests.TestHelpers;
using Moq;

namespace Meridian.Tests.SymbolSearch;

[Trait("Category", "Unit")]
public sealed class OpenFigiSymbolResolverTests
{
    [Fact]
    public async Task ResolveAsync_AmbiguousListings_DoNotCacheOrRegisterEitherResponseOrder()
    {
        var first = Mapping("BBG000B9XRY4", "UW");
        var second = Mapping("BBG000QNH748", "US");
        var responses = new Queue<HttpResponseMessage>([Response(first, second), Response(second, first)]);
        using var handler = new StubHttpMessageHandler(_ => responses.Dequeue());
        using var http = new HttpClient(handler);
        var registry = new Mock<ICanonicalSymbolRegistry>();
        using var resolver = new CanonicalRegistrySymbolResolver(registry.Object, new OpenFigiSymbolResolver("test-key", http));

        (await resolver.ResolveAsync("AAPL")).Should().BeNull();
        (await resolver.ResolveAsync("AAPL")).Should().BeNull();

        handler.CallCount.Should().Be(2, "an ambiguous result must not be cached as an identity");
        registry.Verify(r => r.RegisterAsync(It.IsAny<CanonicalSymbolDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_ExchangeEvidence_SelectsSameListingAndLearnsItRegardlessOfOrder(bool reverse)
    {
        var selected = Mapping("BBG000B9XRY4", "UW");
        var other = Mapping("BBG000QNH748", "US");
        string? requestBody = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return reverse ? Response(other, selected) : Response(selected, other);
        });
        using var http = new HttpClient(handler);
        var registry = new Mock<ICanonicalSymbolRegistry>();
        registry.Setup(r => r.RegisterAsync(It.IsAny<CanonicalSymbolDefinition>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        using var resolver = new CanonicalRegistrySymbolResolver(registry.Object, new OpenFigiSymbolResolver("test-key", http));

        var resolution = await resolver.ResolveAsync(" aapl ", " uw ");

        resolution.Should().NotBeNull();
        resolution!.Figi.Should().Be("BBG000B9XRY4");
        resolution.ExchangeCode.Should().Be("UW");
        resolution.SecurityType.Should().Be("Common Stock", "securityType remains instrument evidence when securityType2 is absent");
        requestBody.Should().Contain("\"idValue\":\"AAPL\"").And.Contain("\"exchCode\":\"UW\"");
        registry.Verify(r => r.RegisterAsync(It.Is<CanonicalSymbolDefinition>(definition =>
            definition.Canonical == "AAPL" && definition.Figi == "BBG000B9XRY4" && definition.Exchange == "UW"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("NASDAQ", "UW")]
    [InlineData("NASDAQ", "UQ")]
    [InlineData("NASDAQ", "UR")]
    [InlineData("XNAS", "UW")]
    [InlineData("XNAS", "UQ")]
    [InlineData("XNAS", "UR")]
    public async Task ResolveAsync_BroadNasdaqScope_ResolvesUniqueListingAcrossMarketTiers(string exchange, string venue)
    {
        string? requestBody = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Response(Mapping("BBG000QNH748", "UN"), Mapping("BBG000B9XRY4", venue));
        });
        using var http = new HttpClient(handler);
        using var resolver = new OpenFigiSymbolResolver("test-key", http);

        var resolution = await resolver.ResolveAsync("AAPL", exchange);

        resolution.Should().NotBeNull();
        resolution!.Figi.Should().Be("BBG000B9XRY4");
        resolution.ExchangeCode.Should().Be(venue);
        using var request = JsonDocument.Parse(requestBody!);
        request.RootElement[0].TryGetProperty("exchCode", out _).Should().BeFalse(
            "a broad NASDAQ request must not exclude its Global or Capital Market listings");
    }

    [Theory]
    [InlineData("NASDAQ", false)]
    [InlineData("NASDAQ", true)]
    [InlineData("XNAS", false)]
    [InlineData("XNAS", true)]
    public async Task ResolveAsync_MultipleNasdaqVenues_RemainAmbiguousAndCannotBeLearned(string exchange, bool reverse)
    {
        var candidates = new[]
        {
            Mapping("BBG000B9XRY4", "UW"),
            Mapping("BBG000QNH748", "UQ"),
            Mapping("BBG000B9Y5X2", "UR")
        };
        if (reverse)
            Array.Reverse(candidates);
        using var handler = new StubHttpMessageHandler(_ => Response(candidates));
        using var http = new HttpClient(handler);
        var registry = new Mock<ICanonicalSymbolRegistry>();
        using var resolver = new CanonicalRegistrySymbolResolver(registry.Object, new OpenFigiSymbolResolver("test-key", http));

        (await resolver.ResolveAsync("AAPL", exchange)).Should().BeNull();

        registry.Verify(r => r.RegisterAsync(It.IsAny<CanonicalSymbolDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_ExactVenueCache_DoesNotSatisfyBroaderNasdaqScope()
    {
        var responses = new Queue<HttpResponseMessage>([
            Response(Mapping("BBG000B9XRY4", "UW")),
            Response(Mapping("BBG000B9XRY4", "UW"), Mapping("BBG000QNH748", "UQ"))
        ]);
        using var handler = new StubHttpMessageHandler(_ => responses.Dequeue());
        using var http = new HttpClient(handler);
        using var resolver = new OpenFigiSymbolResolver("test-key", http);

        (await resolver.ResolveAsync("AAPL", "UW")).Should().NotBeNull();
        (await resolver.ResolveAsync("AAPL", "NASDAQ")).Should().BeNull();

        handler.CallCount.Should().Be(2, "an exact venue and all NASDAQ tiers are different resolution scopes");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_DistinctInstrumentsOnSameExchange_RemainAmbiguous(bool reverse)
    {
        var stock = Mapping("BBG000B9XRY4", "UW");
        var fund = Mapping("BBG000QNH748", "UW");
        fund["securityType"] = "ETF";
        using var handler = new StubHttpMessageHandler(_ => reverse ? Response(fund, stock) : Response(stock, fund));
        using var http = new HttpClient(handler);
        using var resolver = new OpenFigiSymbolResolver("test-key", http);

        (await resolver.ResolveAsync("AAPL", "UW")).Should().BeNull();
    }

    [Theory]
    [InlineData("figi", null)]
    [InlineData("figi", " ")]
    [InlineData("exchCode", null)]
    [InlineData("exchCode", "UN")]
    [InlineData("marketSector", null)]
    [InlineData("marketSector", "Govt")]
    [InlineData("securityType", null)]
    [InlineData("ticker", "OTHER")]
    public async Task ResolveAsync_InsufficientOrMismatchedIdentityEvidence_DoesNotRegister(string field, string? value)
    {
        var candidate = Mapping("BBG000B9XRY4", "UW");
        candidate[field] = value;
        using var handler = new StubHttpMessageHandler(_ => Response(candidate));
        using var http = new HttpClient(handler);
        var registry = new Mock<ICanonicalSymbolRegistry>();
        using var resolver = new CanonicalRegistrySymbolResolver(registry.Object, new OpenFigiSymbolResolver("test-key", http));

        (await resolver.ResolveAsync("AAPL", "UW")).Should().BeNull();

        registry.Verify(r => r.RegisterAsync(It.IsAny<CanonicalSymbolDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("exchCode")]
    [InlineData("marketSector")]
    [InlineData("securityType")]
    public async Task ResolveAsync_IncompleteCompetingCandidate_DoesNotMakeCompleteCandidateUnique(string missingField)
    {
        var complete = Mapping("BBG000B9XRY4", "UW");
        var incomplete = Mapping("BBG000QNH748", "UW");
        incomplete[missingField] = null;
        using var handler = new StubHttpMessageHandler(_ => Response(complete, incomplete));
        using var http = new HttpClient(handler);
        using var resolver = new OpenFigiSymbolResolver("test-key", http);

        (await resolver.ResolveAsync("AAPL", "UW")).Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ConflictingMetadataForSameFigi_DoesNotSelectByResponseOrder()
    {
        var first = Mapping("BBG000B9XRY4", "UW");
        var conflicting = Mapping("BBG000B9XRY4", "UW");
        conflicting["securityType"] = "ETF";
        using var handler = new StubHttpMessageHandler(_ => Response(first, conflicting));
        using var http = new HttpClient(handler);
        using var resolver = new OpenFigiSymbolResolver("test-key", http);

        (await resolver.ResolveAsync("AAPL", "UW")).Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_RepeatedIdenticalCandidate_ResolvesAndCachesWithNormalizedScope()
    {
        var candidate = Mapping("BBG000B9XRY4", "UW");
        using var handler = new StubHttpMessageHandler(_ => Response(candidate, candidate));
        using var http = new HttpClient(handler);
        using var resolver = new OpenFigiSymbolResolver("test-key", http);

        var first = await resolver.ResolveAsync("AAPL", "NASDAQ");
        var second = await resolver.ResolveAsync(" aapl ", "XNAS");

        first.Should().NotBeNull();
        first!.Figi.Should().Be("BBG000B9XRY4");
        second.Should().BeSameAs(first);
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ResolveAsync_UniqueCompleteCandidate_CanResolveWithoutExplicitExchange()
    {
        using var handler = new StubHttpMessageHandler(_ => Response(Mapping("BBG000B9XRY4", "UW")));
        using var http = new HttpClient(handler);
        using var resolver = new OpenFigiSymbolResolver("test-key", http);

        var resolution = await resolver.ResolveAsync("AAPL");

        resolution.Should().NotBeNull();
        resolution!.Figi.Should().Be("BBG000B9XRY4");
    }

    [Fact]
    public async Task MapSymbolAsync_AmbiguousCandidates_CannotEnterRegistryViaProviderMappingFallback()
    {
        using var handler = new StubHttpMessageHandler(_ => Response(Mapping("BBG000B9XRY4", "UW"), Mapping("BBG000QNH748", "US")));
        using var http = new HttpClient(handler);
        var registry = new Mock<ICanonicalSymbolRegistry>();
        using var resolver = new CanonicalRegistrySymbolResolver(registry.Object, new OpenFigiSymbolResolver("test-key", http));

        (await resolver.MapSymbolAsync("AAPL", "alpaca", "stooq")).Should().BeNull();

        registry.Verify(r => r.RegisterAsync(It.IsAny<CanonicalSymbolDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_AmbiguousCandidates_RemainAvailableForDiscovery()
    {
        using var handler = new StubHttpMessageHandler(_ => Response(Mapping("BBG000B9XRY4", "UW"), Mapping("BBG000QNH748", "US")));
        using var http = new HttpClient(handler);
        using var resolver = new OpenFigiSymbolResolver("test-key", http);

        var results = await resolver.SearchAsync("AAPL");

        results.Select(result => result.Figi).Should().Equal("BBG000B9XRY4", "BBG000QNH748");
    }

    private static Dictionary<string, string?> Mapping(string figi, string exchange) => new()
    {
        ["figi"] = figi,
        ["compositeFIGI"] = "BBG000B9Y5X2",
        ["shareClassFIGI"] = "BBG001S5N8V8",
        ["ticker"] = "AAPL",
        ["name"] = "APPLE INC",
        ["exchCode"] = exchange,
        ["marketSector"] = "Equity",
        ["securityType"] = "Common Stock"
    };

    private static HttpResponseMessage Response(params Dictionary<string, string?>[] mappings) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new[] { new { data = mappings } }), Encoding.UTF8, "application/json")
    };
}
