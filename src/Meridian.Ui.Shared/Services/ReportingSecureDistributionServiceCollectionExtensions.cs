using Meridian.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Registers the application-level secure distribution graph. Durable governance, artifact,
/// catalog, audit, grant, and delivery stores remain deployment-owned prerequisites.
/// </summary>
public static class ReportingSecureDistributionServiceCollectionExtensions
{
    public static IServiceCollection AddSecureReportingDistribution(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = BuildOptions();
        services.TryAddSingleton(options);
        services.TryAddSingleton<ReportingReleasedArtifactIntegrityGate>();
        services.TryAddSingleton<IReportingReleaseAuthorizationVerifier,
            GovernanceReportingReleaseAuthorizationVerifier>();
        services.TryAddSingleton<ReportingAccessGrantService>();
        services.TryAddSingleton<IReportingRecipientDestinationResolver>(
            static _ => BuildRecipientDestinationResolver());
        ConfigureProviderIntegration(services);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IReportingDeliveryTransport, SecurePortalReportingDeliveryTransport>());
        services.TryAddSingleton<ReportingDeliveryDispatcher>();
        services.TryAddSingleton<ReportingSecureDistributionApplicationService>();
        services.AddHostedService<ReportingSecureDistributionHostedService>();
        return services;
    }

    private static SecureReportingDistributionOptions BuildOptions()
    {
        var defaults = SecureReportingDistributionOptions.Default;
        return defaults with
        {
            ExternalAccessBaseUri = NormalizeOptional(
                Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_EXTERNAL_ACCESS_BASE_URI")),
            WorkerId = NormalizeOptional(Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_DELIVERY_WORKER_ID"))
                       ?? defaults.WorkerId,
            WorkerPollInterval = ResolvePollInterval(defaults.WorkerPollInterval)
        };
    }

    private static IReportingRecipientDestinationResolver BuildRecipientDestinationResolver()
    {
        var json = NormalizeOptional(
            Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_RECIPIENT_DESTINATIONS_JSON"));
        if (json is null)
        {
            return new RejectingReportingRecipientDestinationResolver();
        }

        try
        {
            var bindings = JsonSerializer.Deserialize<ReportingRecipientDestinationBinding[]>(
                               json,
                               new JsonSerializerOptions(JsonSerializerDefaults.Web))
                           ?? throw new InvalidOperationException(
                               "Secure reporting recipient destination configuration cannot be null.");
            return new ConfiguredReportingRecipientDestinationResolver(bindings);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "MERIDIAN_REPORTING_RECIPIENT_DESTINATIONS_JSON must contain a valid JSON array of exact-scope recipient bindings.",
                ex);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                "MERIDIAN_REPORTING_RECIPIENT_DESTINATIONS_JSON contains an invalid or ambiguous recipient binding.",
                ex);
        }
    }

    private static void ConfigureProviderIntegration(IServiceCollection services)
    {
        var relayEndpoint = NormalizeOptional(
            Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_HTTP_RELAY_ENDPOINT"));
        var relayCredential = NormalizeOptional(
            Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_HTTP_RELAY_BEARER_TOKEN"));
        var receiptSecretText = NormalizeOptional(
            Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_RELAY_RECEIPT_HMAC_SECRET"));
        var grantSecretText = NormalizeOptional(
            Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_DELIVERY_GRANT_HMAC_SECRET"));

        if (relayEndpoint is null
            && relayCredential is null
            && receiptSecretText is null
            && grantSecretText is null)
        {
            services.TryAddSingleton<IReportingProviderReceiptAuthenticator,
                RejectingReportingProviderReceiptAuthenticator>();
            return;
        }

        if (relayEndpoint is null
            || relayCredential is null
            || receiptSecretText is null
            || grantSecretText is null)
        {
            throw new InvalidOperationException(
                "Secure reporting relay requires endpoint, bearer credential, receipt HMAC secret, and delivery-grant HMAC secret together.");
        }

        if (!Uri.TryCreate(relayEndpoint, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("Secure reporting relay endpoint is not an absolute URI.");
        }

        byte[] receiptSecret;
        try
        {
            receiptSecret = Convert.FromBase64String(receiptSecretText);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "Secure reporting receipt HMAC secret must be base64-encoded.",
                ex);
        }

        if (receiptSecret.Length < 32)
        {
            throw new InvalidOperationException(
                "Secure reporting receipt HMAC secret must decode to at least 256 bits.");
        }

        byte[] grantSecret;
        try
        {
            grantSecret = Convert.FromBase64String(grantSecretText);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "Secure reporting delivery-grant HMAC secret must be base64-encoded.",
                ex);
        }

        if (grantSecret.Length < 32)
        {
            throw new InvalidOperationException(
                "Secure reporting delivery-grant HMAC secret must decode to at least 256 bits.");
        }

        var relayOptions = new ReportingHttpRelayClientOptions(
            endpoint,
            relayCredential,
            TimeSpan.FromSeconds(30));
        services.TryAddSingleton(relayOptions);
        services.AddHttpClient(ConfiguredReportingHttpRelayClient.ClientName, client =>
            {
                client.Timeout = relayOptions.Timeout;
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler
            {
                // A redirect would replay the notification body, including its one-time fragment
                // bearer, to a URI outside the explicitly configured relay trust boundary.
                AllowAutoRedirect = false
            });
        services.TryAddSingleton<IReportingHttpRelayClient, ConfiguredReportingHttpRelayClient>();
        services.TryAddSingleton<IReportingProviderReceiptAuthenticator>(_ =>
            new HmacReportingProviderReceiptAuthenticator(receiptSecret));
        services.TryAddSingleton<IReportingDeliveryGrantCredentialDeriver>(_ =>
            new HmacReportingDeliveryGrantCredentialDeriver(grantSecret));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IReportingDeliveryTransport, HttpRelayReportingDeliveryTransport>());
    }

    private static TimeSpan ResolvePollInterval(TimeSpan fallback)
    {
        var configured = NormalizeOptional(
            Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_DELIVERY_POLL_SECONDS"));
        if (configured is null)
        {
            return fallback;
        }

        if (!double.TryParse(
                configured,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var seconds)
            || seconds < 0.25
            || seconds > 300)
        {
            throw new InvalidOperationException(
                "MERIDIAN_REPORTING_DELIVERY_POLL_SECONDS must be between 0.25 and 300.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
