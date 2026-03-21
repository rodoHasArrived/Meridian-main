namespace Meridian.Application.Config;

/// <summary>
/// Alpaca Market Data configuration.
/// </summary>
/// <remarks>
/// <para><b>Authentication:</b> Uses Trading API keys with WebSocket message authentication.</para>
/// <para><b>Security Best Practices:</b></para>
/// <list type="bullet">
/// <item><description>Use environment variables: <c>ALPACA_KEY_ID</c>, <c>ALPACA_SECRET_KEY</c></description></item>
/// <item><description>Use a secure vault service (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault)</description></item>
/// <item><description>Use .NET User Secrets for local development: <c>dotnet user-secrets</c></description></item>
/// <item><description>Ensure <c>appsettings.json</c> with real credentials is in <c>.gitignore</c></description></item>
/// </list>
/// <para>See <see href="https://docs.alpaca.markets/docs/real-time-stock-pricing-data">Alpaca Real-Time Data Docs</see></para>
/// </remarks>
public sealed record AlpacaOptions(
    string KeyId = "",
    string SecretKey = "",
    string Feed = "iex",            // v2/{feed}: iex, sip, delayed_sip
    bool UseSandbox = false,        // stream.data.sandbox.alpaca.markets
    bool SubscribeQuotes = false    // if true, subscribes to quotes too (currently not wired to L2 collector)
);
