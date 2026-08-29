using FluentAssertions;
using Meridian.Infrastructure.Adapters.Polygon;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Infrastructure.Providers;

[Collection("Sequential")]
public sealed class PolygonSecurityMasterIngestProviderTests
{
    [Fact]
    public async Task FetchAllAsync_WhenHttpFetchIsCancelled_PropagatesCancellation()
    {
        using var environment = new EnvironmentVariableScope()
            .Set("POLYGON_API_KEY", "test-key")
            .Set("POLYGON__APIKEY", null);
        using var cts = new CancellationTokenSource();
        using var handler = new CancelingHttpMessageHandler(cts);
        using var httpClient = new HttpClient(handler);
        using var provider = new PolygonSecurityMasterIngestProvider(
            NullLogger<PolygonSecurityMasterIngestProvider>.Instance,
            httpClient);

        var fetch = async () => await provider.FetchAllAsync(ct: cts.Token);

        await fetch.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().Be(1);
    }

    private sealed class CancelingHttpMessageHandler(CancellationTokenSource cancellation) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellation.Token);
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope Set(string name, string? value)
        {
            _originalValues.TryAdd(name, Environment.GetEnvironmentVariable(name));
            Environment.SetEnvironmentVariable(name, value);
            return this;
        }

        public void Dispose()
        {
            foreach (var (name, value) in _originalValues)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
