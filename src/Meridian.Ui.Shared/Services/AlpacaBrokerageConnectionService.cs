using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Meridian.Core.Config;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Manages Alpaca Trading API-key connectivity for one tenant-owned provider connection.
/// </summary>
/// <remarks>
/// Every operation is bound to a retained connection (tenant, connection, external account and
/// environment) through <see cref="ProviderConnectionLifecycleService.ForConnection"/>, so
/// credentials are saved, read, verified and deleted only in that connection's vault scope. There
/// is deliberately no provider-wide path. A caller either names a connection its tenant owns, or
/// omits the id and gets the tenant's single owned paper connection; anything else is refused.
/// </remarks>
public sealed class AlpacaBrokerageConnectionService
{
    private const string ProviderId = "alpaca";
    private const string PaperTradingApiEndpoint = "https://paper-api.alpaca.markets/v2";
    private const string LiveTradingApiEndpoint = "https://api.alpaca.markets/v2";
    private const string DefaultActor = "brokerage-connection-service";

    private static readonly IReadOnlyList<string> Scopes =
    [
        "trading:account",
        "brokerage-sync:read"
    ];

    private readonly ILogger<AlpacaBrokerageConnectionService> _logger;
    private readonly ProviderConnectionLifecycleService _lifecycle;
    private readonly IHttpClientFactory? _httpClientFactory;

    public AlpacaBrokerageConnectionService(
        ILogger<AlpacaBrokerageConnectionService> logger,
        ProviderConnectionLifecycleService lifecycle,
        IHttpClientFactory? httpClientFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Reads the status of one Alpaca connection owned by <paramref name="tenantId"/>.</summary>
    /// <exception cref="UnauthorizedAccessException">The connection is not an Alpaca connection owned by the tenant.</exception>
    /// <remarks>
    /// With no <paramref name="connectionId"/> and no single owned paper connection to select, the
    /// result is a not-configured status that says so; it never falls back to another record.
    /// </remarks>
    public async Task<BrokerageConnectionStatusDto> GetStatusAsync(
        string? connectionId,
        string tenantId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (connectionId is null)
        {
            var selection = SelectDefaultPaperConnection(tenantId);
            if (selection.ConnectionId is null)
                return BuildUnselectedStatus(selection.Reason!);
            connectionId = selection.ConnectionId;
        }

        BindOwnedConnection(connectionId, tenantId);
        return BuildStatus(await ReadRowAsync(connectionId, tenantId, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Verifies the submitted keys against the connection's retained account and environment, then
    /// stores them in that connection's vault scope. Keys that cannot be shown to belong to the
    /// retained account are never stored, so they cannot replace a working credential.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The connection is not an Alpaca connection owned by the tenant.</exception>
    /// <exception cref="InvalidDataException">
    /// The requested environment or the verified account differs from the retained connection, or
    /// no connection was named and the tenant has no single owned paper connection.
    /// </exception>
    public async Task<BrokerageConnectionStatusDto> ConnectAsync(
        string? connectionId,
        string tenantId,
        AlpacaBrokerageConnectionRequestDto request,
        CancellationToken ct = default,
        string? actor = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        connectionId ??= RequireDefaultPaperConnection(tenantId);
        var selected = BindOwnedConnection(connectionId, tenantId);
        var owned = await ReadRowAsync(connectionId, tenantId, ct).ConfigureAwait(false);
        var environment = AlpacaCredentialEnvironment.NormalizeTradingEnvironment(owned.Environment);

        // An omitted environment means "this connection's environment"; a stated one must agree.
        if (!string.IsNullOrWhiteSpace(request.Environment) &&
            !string.Equals(AlpacaCredentialEnvironment.NormalizeTradingEnvironment(request.Environment), environment, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The requested Alpaca environment does not match the selected connection.");
        }

        var keyId = request.KeyId?.Trim();
        var secretKey = request.SecretKey?.Trim();
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secretKey))
            return BuildStatus(owned, "Alpaca API key id and secret key are required.");

        string verifiedAccountId;
        try
        {
            verifiedAccountId = await ReadAccountIdAsync(keyId, secretKey, environment, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Provider reason phrases, JSON paths and transport exceptions can echo credentials.
            _logger.LogWarning("Alpaca account verification failed for {Environment} ({FailureType}); submitted keys were not stored",
                environment, ex.GetType().Name);
            return BuildStatus(owned, ex is HttpRequestException { StatusCode: { } statusCode }
                ? $"Alpaca account verification failed: HTTP {(int)statusCode}."
                : "Alpaca account verification failed.");
        }

        if (!string.Equals(verifiedAccountId, owned.ExternalAccountId, StringComparison.Ordinal))
        {
            _logger.LogWarning("Alpaca keys submitted for connection {ConnectionId} belong to a different account; they were not stored",
                connectionId);
            throw new InvalidDataException("The verified Alpaca account does not match the selected connection.");
        }

        await selected.SaveCredentialsAsync(
            ProviderId,
            new ProviderCredentialUpsertRequestDto(
                new Dictionary<string, string?> { ["KeyId"] = keyId, ["SecretKey"] = secretKey },
                environment,
                actor ?? DefaultActor),
            ct).ConfigureAwait(false);

        // The lifecycle records the result only against the credential generation it tested, so a
        // verification that returns after a newer save cannot mark the replacement as verified.
        var verification = await selected.VerifyAsync(ProviderId, ct, actor ?? DefaultActor).ConfigureAwait(false);
        var current = await ReadRowAsync(connectionId, tenantId, ct).ConfigureAwait(false);
        return BuildStatus(current, verification.Success ? null : verification.LastError);
    }

    /// <summary>Deletes the credentials of one owned connection; other connections are untouched.</summary>
    /// <exception cref="UnauthorizedAccessException">The connection is not an Alpaca connection owned by the tenant.</exception>
    /// <exception cref="InvalidDataException">No connection was named and the tenant has no single owned paper connection.</exception>
    public async Task<BrokerageConnectionStatusDto> RevokeAsync(
        string? connectionId,
        string tenantId,
        CancellationToken ct = default,
        string? actor = null)
    {
        ct.ThrowIfCancellationRequested();
        connectionId ??= RequireDefaultPaperConnection(tenantId);
        var selected = BindOwnedConnection(connectionId, tenantId);
        await selected.DeleteCredentialsAsync(ProviderId, actor ?? DefaultActor, ct).ConfigureAwait(false);
        return BuildStatus(await ReadRowAsync(connectionId, tenantId, ct).ConfigureAwait(false));
    }

    private ProviderConnectionLifecycleService BindOwnedConnection(string connectionId, string tenantId)
    {
        if (string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(tenantId))
            throw new UnauthorizedAccessException("Credential connection ownership could not be established.");
        try
        {
            // Also refuses a connection the tenant owns for a different provider family.
            return _lifecycle.ForConnection(connectionId, tenantId, ProviderId);
        }
        catch (ArgumentException)
        {
            throw new UnauthorizedAccessException("Credential connection ownership could not be established.");
        }
    }

    /// <summary>
    /// The tenant's only owned paper connection, when exactly one exists. Live connections are
    /// never selected implicitly; they must be named.
    /// </summary>
    private (string? ConnectionId, string? Reason) SelectDefaultPaperConnection(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new UnauthorizedAccessException("Credential connection ownership could not be established.");
        var paper = _lifecycle.GetOwnedConnectionsForTenant(tenantId, ProviderId)
            .Where(static connection => string.Equals(
                AlpacaCredentialEnvironment.NormalizeTradingEnvironment(connection.CredentialEnvironment),
                AlpacaCredentialEnvironment.PaperEnvironment, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(connection.CredentialEnvironment, AlpacaCredentialEnvironment.LiveEnvironment, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return paper.Length switch
        {
            1 => (paper[0].ConnectionId, null),
            0 => (null, "No owned Alpaca paper connection exists for this tenant. Create one with its Alpaca account number under Settings provider connections, then enter its keys."),
            _ => (null, "This tenant owns more than one Alpaca paper connection. Select the connection to use.")
        };
    }

    private string RequireDefaultPaperConnection(string tenantId)
    {
        var selection = SelectDefaultPaperConnection(tenantId);
        return selection.ConnectionId ?? throw new InvalidDataException(selection.Reason);
    }

    private Task<ProviderConnectionRowDto> ReadRowAsync(string connectionId, string tenantId, CancellationToken ct)
        => _lifecycle.GetConnectionStatusForTenantAsync(connectionId, tenantId, ct);

    private async Task<string> ReadAccountIdAsync(
        string keyId,
        string secretKey,
        string environment,
        CancellationToken ct)
    {
        using var client = (_httpClientFactory ?? throw new InvalidOperationException(
            "An HTTP client factory is required to verify a brokerage connection."))
            .CreateClient(nameof(AlpacaBrokerageConnectionService));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{TradingApiEndpoint(environment)}/account");
        request.Headers.TryAddWithoutValidation("APCA-API-KEY-ID", keyId);
        request.Headers.TryAddWithoutValidation("APCA-API-SECRET-KEY", secretKey);

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("Provider verification returned an unsuccessful status.", null, response.StatusCode);
        }

        var account = await response.Content.ReadFromJsonAsync<AlpacaAccountVerificationResponse>(cancellationToken: ct)
            .ConfigureAwait(false);
        var accountId = FirstNonBlank(account?.AccountNumber, account?.Id);
        if (string.IsNullOrWhiteSpace(accountId))
            throw new InvalidOperationException("Provider account identity is missing.");
        return accountId;
    }

    private static BrokerageConnectionStatusDto BuildStatus(ProviderConnectionRowDto row, string? lastErrorOverride = null)
    {
        var lastError = lastErrorOverride ?? row.LastError;
        var environment = AlpacaCredentialEnvironment.NormalizeTradingEnvironment(row.Environment);
        var isLive = string.Equals(environment, AlpacaCredentialEnvironment.LiveEnvironment, StringComparison.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var state = BrokerageConnectionStateDto.NotConfigured;
        var isConfigured = row.CredentialState is
            ProviderCredentialStateDto.Configured
            or ProviderCredentialStateDto.Verified
            or ProviderCredentialStateDto.Invalid;

        if (!isConfigured)
        {
            if (!string.IsNullOrWhiteSpace(lastError))
                warnings.Add(lastError);
            warnings.Add("Enter Alpaca paper Trading API keys to verify /v2/account before account discovery or sync.");
        }
        else if (!string.IsNullOrWhiteSpace(lastError))
        {
            state = BrokerageConnectionStateDto.Degraded;
            warnings.Add(lastError);
        }
        else if (row.VerificationState == ProviderVerificationStateDto.Verified)
        {
            state = BrokerageConnectionStateDto.Connected;
        }
        else
        {
            state = BrokerageConnectionStateDto.Disconnected;
            warnings.Add("Alpaca Trading API keys are present but /v2/account has not been verified for this connection.");
        }

        if (isLive)
        {
            warnings.Add("Live Alpaca endpoint is selected. Paper remains the default and order placement is still governed by execution controls.");
        }

        return new BrokerageConnectionStatusDto(
            ProviderId: ProviderId,
            DisplayName: isLive ? "Alpaca live" : "Alpaca paper",
            State: state,
            IsConfigured: isConfigured,
            IsConnected: state == BrokerageConnectionStateDto.Connected,
            AuthorizationUrl: null,
            ConnectedAt: isConfigured ? row.LastSuccessfulAt : null,
            ExpiresAt: null,
            LastError: lastError,
            Warnings: warnings,
            Scopes: Scopes,
            Environment: environment,
            ExternalAccountId: string.IsNullOrWhiteSpace(row.ExternalAccountId) ? null : row.ExternalAccountId,
            VerifiedAt: isConfigured ? row.LastSuccessfulAt : null,
            MaskedKeyId: row.MaskedKeyPreview);
    }

    private static BrokerageConnectionStatusDto BuildUnselectedStatus(string reason)
        => new(
            ProviderId: ProviderId,
            DisplayName: "Alpaca paper",
            State: BrokerageConnectionStateDto.NotConfigured,
            IsConfigured: false,
            IsConnected: false,
            AuthorizationUrl: null,
            ConnectedAt: null,
            ExpiresAt: null,
            LastError: null,
            Warnings: [reason],
            Scopes: Scopes,
            Environment: AlpacaCredentialEnvironment.PaperEnvironment);

    private static string TradingApiEndpoint(string environment)
        => string.Equals(environment, AlpacaCredentialEnvironment.LiveEnvironment, StringComparison.OrdinalIgnoreCase)
            ? LiveTradingApiEndpoint
            : PaperTradingApiEndpoint;

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record AlpacaAccountVerificationResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("account_number")] string? AccountNumber);
}
