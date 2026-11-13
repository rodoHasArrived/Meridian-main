using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Http;

namespace Meridian.Infrastructure.Adapters.OpenFigi;

/// <summary>Reads the selected credential at dispatch rather than retaining a startup secret.</summary>
internal sealed class OpenFigiRequestCredentials(
    string? configuredKey,
    IProviderCredentialResolver? resolver,
    bool useEnvironmentFallback)
{
    public async ValueTask<HttpRequestMessage> CreateRequestAsync<T>(string uri, T payload, CancellationToken ct)
    {
        string? key;
        if (resolver is null)
        {
            key = configuredKey ?? (useEnvironmentFallback ? Environment.GetEnvironmentVariable("OPENFIGI_API_KEY") : null);
        }
        else
        {
            var prepared = await resolver.PrepareAsync([typeof(OpenFigiClient)], ct).ConfigureAwait(false);
            using var scope = prepared.Activate();
            var context = resolver.CreateContext(typeof(OpenFigiClient),
                new Dictionary<string, string?> { ["OPENFIGI_API_KEY"] = configuredKey });
            key = context.Get("OPENFIGI_API_KEY");
            if (context.IsAuthoritative && string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("The selected OpenFIGI credential is unavailable.");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(payload, options: new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            })
        };
        // A retry of this message would retain its old owner/key. Each dispatch must reread it.
        request.Options.Set(SharedResiliencePolicies.ProviderOwnsRetries, true);
        if (!string.IsNullOrWhiteSpace(key))
            request.Headers.Add("X-OPENFIGI-APIKEY", key);
        return request;
    }
}
