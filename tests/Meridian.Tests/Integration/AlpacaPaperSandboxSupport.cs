using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Core.Config;

namespace Meridian.Tests.Integration;

/// <summary>
/// Shared plumbing for the opt-in Alpaca paper sandbox tests: credential presence checks that
/// never print a value, and a transport that cannot reach anything but the paper Trading API.
/// </summary>
internal static class AlpacaPaperSandbox
{
    public const string OptInVariable = "MERIDIAN_RUN_ALPACA_PAPER_SANDBOX";
    public const string OrdersOptInVariable = "MERIDIAN_RUN_ALPACA_PAPER_SANDBOX_ORDERS";
    public const string MarketDataOptInVariable = "MERIDIAN_RUN_ALPACA_MARKET_DATA";
    public const string PaperHost = "paper-api.alpaca.markets";
    public const string MarketDataHost = "data.alpaca.markets";

    /// <summary>Largest order quantity the transport lets an order run submit or amend to.</summary>
    public const decimal MaxOrderQuantity = 2m;

    public static bool IsOptedIn(string variable) =>
        string.Equals(Environment.GetEnvironmentVariable(variable), "1", StringComparison.Ordinal);

    /// <summary>
    /// Returns the configured paper credentials, failing without echoing them when the opted-in
    /// run is missing a credential or does not explicitly select the paper environment.
    /// </summary>
    public static (string KeyId, string SecretKey) RequirePaperCredentials()
    {
        var key = Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.KeyIdName);
        var secret = Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.SecretKeyName);
        var environment = Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.TradingEnvironmentName);
        // Assert presence only: a failure must never print a credential value.
        string.IsNullOrWhiteSpace(key).Should().BeFalse("ALPACA_KEY_ID must be configured for the opted-in sandbox run");
        string.IsNullOrWhiteSpace(secret).Should().BeFalse("ALPACA_SECRET_KEY must be configured for the opted-in sandbox run");
        string.Equals(environment, "paper", StringComparison.Ordinal).Should().BeTrue(
            "ALPACA_TRADING_ENVIRONMENT must explicitly be paper; live or implicit defaults are refused");
        return (key!, secret!);
    }
}

/// <summary>What a sandbox run may send to the paper Trading API, independent of configuration.</summary>
internal enum AlpacaPaperSandboxAccess
{
    /// <summary>GET requests to account, portfolio, order and reference resources only.</summary>
    ReadOnly,

    /// <summary>
    /// <see cref="ReadOnly"/> plus GET requests for option snapshots and an underlying's latest
    /// trade on the market-data host. Nothing on that host is a mutation.
    /// </summary>
    ReadOnlyWithMarketData,

    /// <summary>
    /// Additionally submit, replace and cancel individual orders of at most
    /// <see cref="AlpacaPaperSandbox.MaxOrderQuantity"/> shares sized by quantity. Bulk
    /// cancellation, position liquidation, dollar-sized orders, account configuration and every
    /// other mutation stay refused.
    /// </summary>
    IndividualOrders,
}

/// <summary>
/// <see cref="IHttpClientFactory"/> whose clients refuse, before any credential leaves the
/// process, every request that is not an allowed method on an allowed path of the paper Trading
/// API (or, when the run asks for it, a read of the market-data host) over HTTPS, and that never
/// follows a redirect.
/// </summary>
internal sealed partial class AlpacaPaperSandboxHttpClientFactory(AlpacaPaperSandboxAccess access)
    : IHttpClientFactory, IDisposable
{
    private readonly GuardHandler _handler = new(access);

    public int RequestCount => _handler.RequestCount;

    public int MutationCount => _handler.MutationCount;

    /// <summary>Order submissions (POST) that reached the paper API, whatever the caller then saw.</summary>
    public int SubmissionCount => _handler.SubmissionCount;

    /// <summary>
    /// Lets the next order submission reach the paper API, then drops its response and fails the
    /// call the way a connection lost after broker acceptance does. The broker holds the order;
    /// the caller only knows its dispatch is unresolved.
    /// </summary>
    public void DropNextOrderSubmissionResponse() => _handler.DropNextSubmissionResponse();

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false)
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public void Dispose() => _handler.Dispose();

    [GeneratedRegex(
        @"^/v2/(account|account/activities(/[A-Z_]+)?|positions|orders|orders:by_client_order_id|orders/[0-9a-f-]{36}|assets|assets/[A-Za-z0-9./]+|options/contracts)\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex ReadablePath();

    [GeneratedRegex(@"^/v2/orders/[0-9a-f-]{36}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SingleOrderPath();

    [GeneratedRegex(
        @"^/(v2/stocks/[A-Za-z0-9.]+/trades/latest|v1beta1/options/snapshots(/[A-Za-z0-9.]+)?)\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex MarketDataPath();

    private sealed class GuardHandler(AlpacaPaperSandboxAccess access)
        : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        private int _requestCount;
        private int _mutationCount;
        private int _submissionCount;
        private int _dropNextSubmissionResponse;

        public int RequestCount => Volatile.Read(ref _requestCount);

        public int MutationCount => Volatile.Read(ref _mutationCount);

        public int SubmissionCount => Volatile.Read(ref _submissionCount);

        public void DropNextSubmissionResponse() => Interlocked.Exchange(ref _dropNextSubmissionResponse, 1);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            var isPaperHost = uri is not null && string.Equals(uri.Host, AlpacaPaperSandbox.PaperHost, StringComparison.OrdinalIgnoreCase);
            var isMarketDataHost = uri is not null && access == AlpacaPaperSandboxAccess.ReadOnlyWithMarketData
                && string.Equals(uri.Host, AlpacaPaperSandbox.MarketDataHost, StringComparison.OrdinalIgnoreCase);
            if (uri is null || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || !(isPaperHost || isMarketDataHost))
                throw new InvalidOperationException("Sandbox certification reaches only the hosts this run declared, over HTTPS.");

            var path = uri.AbsolutePath;
            var isRead = request.Method == HttpMethod.Get
                && (isPaperHost ? ReadablePath().IsMatch(path) : MarketDataPath().IsMatch(path));
            // DELETE /v2/orders (cancel everything) and DELETE /v2/positions (liquidate) never match.
            var isOrderMutation = isPaperHost && access == AlpacaPaperSandboxAccess.IndividualOrders
                && (request.Method == HttpMethod.Post && path == "/v2/orders"
                    || (request.Method == HttpMethod.Patch || request.Method == HttpMethod.Delete) && SingleOrderPath().IsMatch(path));
            if (!isRead && !isOrderMutation)
                throw new InvalidOperationException(
                    $"Sandbox certification refuses {request.Method} {path}; it is outside this run's allowed surface.");

            if (isOrderMutation && request.Method != HttpMethod.Delete)
                await RefuseOversizedOrderAsync(request, cancellationToken).ConfigureAwait(false);

            Interlocked.Increment(ref _requestCount);
            if (isOrderMutation)
                Interlocked.Increment(ref _mutationCount);
            var isSubmission = isOrderMutation && request.Method == HttpMethod.Post;
            if (isSubmission)
                Interlocked.Increment(ref _submissionCount);

            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (isSubmission && Interlocked.Exchange(ref _dropNextSubmissionResponse, 0) == 1)
            {
                response.Dispose();
                throw new HttpRequestException("Sandbox fault: connection lost after the paper API received the order submission.");
            }

            return response;
        }

        /// <summary>
        /// Bounds what an order submission or amendment may ask for, whatever built the request:
        /// a quantity no larger than the sandbox ceiling, and never a dollar-sized order.
        /// </summary>
        private static async Task RefuseOversizedOrderAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = document.RootElement;
            var hasQuantity = root.TryGetProperty("qty", out var quantity) && quantity.ValueKind != JsonValueKind.Null;
            var sizedInDollars = root.TryGetProperty("notional", out var notional) && notional.ValueKind != JsonValueKind.Null;
            var quantityText = hasQuantity
                ? quantity.ValueKind == JsonValueKind.String ? quantity.GetString() : quantity.GetRawText()
                : null;
            var withinCeiling = !hasQuantity
                || decimal.TryParse(quantityText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                    && parsed > 0m && parsed <= AlpacaPaperSandbox.MaxOrderQuantity;
            // A submission must state a quantity; an amendment may leave it unchanged.
            if (sizedInDollars || !withinCeiling || request.Method == HttpMethod.Post && !hasQuantity)
                throw new InvalidOperationException(
                    "Sandbox certification refuses an order that is dollar-sized or larger than its share ceiling.");
        }
    }
}
