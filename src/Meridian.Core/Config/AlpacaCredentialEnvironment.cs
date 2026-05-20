namespace Meridian.Application.Config;

/// <summary>
/// Resolves Alpaca Trading API credentials from the canonical local environment contract.
/// </summary>
public static class AlpacaCredentialEnvironment
{
    public const string KeyIdName = "ALPACA_KEY_ID";
    public const string SecretKeyName = "ALPACA_SECRET_KEY";
    public const string TradingEnvironmentName = "ALPACA_TRADING_ENVIRONMENT";
    public const string PaperEnvironment = "paper";
    public const string LiveEnvironment = "live";

    public static readonly IReadOnlyList<string> KeyIdAliases =
    [
        "APCA_API_KEY_ID",
        "ALPACA__KEYID"
    ];

    public static readonly IReadOnlyList<string> SecretKeyAliases =
    [
        "APCA_API_SECRET_KEY",
        "ALPACA__SECRETKEY"
    ];

    public static AlpacaCredentialSnapshot Resolve(AlpacaOptions? options = null)
    {
        var keyId = FirstNonBlank(ReadEnvironmentValue(KeyIdName), ReadAliases(KeyIdAliases), options?.KeyId);
        var secretKey = FirstNonBlank(ReadEnvironmentValue(SecretKeyName), ReadAliases(SecretKeyAliases), options?.SecretKey);
        var environment = ResolveTradingEnvironment(options);

        return new AlpacaCredentialSnapshot(
            KeyId: keyId ?? string.Empty,
            SecretKey: secretKey ?? string.Empty,
            Environment: environment,
            UseSandbox: !string.Equals(environment, LiveEnvironment, StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeTradingEnvironment(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return PaperEnvironment;
        }

        if (trimmed.Contains("paper-api.alpaca.markets", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, PaperEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            return PaperEnvironment;
        }

        if (trimmed.Contains("api.alpaca.markets", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, LiveEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            return LiveEnvironment;
        }

        return PaperEnvironment;
    }

    public static string MaskKeyId(string? keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId))
        {
            return string.Empty;
        }

        var trimmed = keyId.Trim();
        if (trimmed.Length <= 4)
        {
            return new string('*', trimmed.Length);
        }

        var suffixLength = Math.Min(4, trimmed.Length);
        return string.Concat(new string('*', Math.Min(trimmed.Length - suffixLength, 12)), trimmed.AsSpan(trimmed.Length - suffixLength));
    }

    public static string? ReadEnvironmentValue(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        try
        {
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static string ResolveTradingEnvironment(AlpacaOptions? options)
    {
        var configured = ReadEnvironmentValue(TradingEnvironmentName);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return NormalizeTradingEnvironment(configured);
        }

        return PaperEnvironment;
    }

    private static string? ReadAliases(IReadOnlyList<string> aliases)
    {
        foreach (var alias in aliases)
        {
            var value = ReadEnvironmentValue(alias);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

public sealed record AlpacaCredentialSnapshot(
    string KeyId,
    string SecretKey,
    string Environment,
    bool UseSandbox)
{
    public bool HasCredentials
        => !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(SecretKey);
}
