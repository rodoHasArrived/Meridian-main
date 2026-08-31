using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Plaid;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Verifies the <c>Plaid-Verification</c> header a Plaid webhook carries, against a configured
/// ES256 public key.
/// <para>
/// The header is a JWS whose payload states the SHA-256 of the request body. Verification is
/// therefore two independent checks, and both must hold: the token must be signed by the
/// configured key, and the body actually received must hash to the value the signed token claims.
/// Checking only the hash would authenticate nothing, because the caller supplies both the body
/// and the hash.
/// </para>
/// <para>
/// Plaid's own documentation resolves the key by <c>kid</c> from its key-rotation endpoint. This
/// implementation verifies against a key supplied in configuration instead, which keeps an inbound
/// callback from depending on an outbound call to the same provider; when a key id is configured
/// as well, a token minted under a different key is refused rather than checked against the wrong
/// key.
/// </para>
/// </summary>
public static class PlaidWebhookVerifier
{
    /// <summary>How far the token's issued-at may drift from now before it is refused.</summary>
    private static readonly TimeSpan MaxIssuedAtSkew = TimeSpan.FromMinutes(5);

    public enum VerificationOutcome
    {
        Verified,
        NotConfigured,
        MissingHeader,
        MalformedToken,
        UnsupportedAlgorithm,
        UnknownKeyId,
        SignatureMismatch,
        BodyHashMismatch,
        Expired
    }

    /// <summary>
    /// Verifies <paramref name="verificationHeader"/> over <paramref name="body"/>.
    /// </summary>
    /// <param name="options">Plaid configuration carrying the verification key.</param>
    /// <param name="verificationHeader">Raw <c>Plaid-Verification</c> header value.</param>
    /// <param name="body">The exact bytes read from the request.</param>
    /// <param name="utcNow">Current time, injected so the skew check is testable.</param>
    public static VerificationOutcome Verify(
        PlaidOptions options,
        string? verificationHeader,
        ReadOnlySpan<byte> body,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.CanVerifyWebhooks)
        {
            return VerificationOutcome.NotConfigured;
        }

        if (string.IsNullOrWhiteSpace(verificationHeader))
        {
            return VerificationOutcome.MissingHeader;
        }

        var parts = verificationHeader.Split('.');
        if (parts.Length != 3)
        {
            return VerificationOutcome.MalformedToken;
        }

        byte[] headerBytes;
        byte[] payloadBytes;
        byte[] signature;
        try
        {
            headerBytes = Base64UrlDecode(parts[0]);
            payloadBytes = Base64UrlDecode(parts[1]);
            signature = Base64UrlDecode(parts[2]);
        }
        catch (FormatException)
        {
            return VerificationOutcome.MalformedToken;
        }

        string? algorithm;
        string? keyId;
        try
        {
            using var header = JsonDocument.Parse(headerBytes);
            algorithm = ReadString(header.RootElement, "alg");
            keyId = ReadString(header.RootElement, "kid");
        }
        catch (JsonException)
        {
            return VerificationOutcome.MalformedToken;
        }

        // ES256 only. Accepting whatever the token names would let a caller downgrade to "none".
        if (!string.Equals(algorithm, "ES256", StringComparison.Ordinal))
        {
            return VerificationOutcome.UnsupportedAlgorithm;
        }

        if (!string.IsNullOrWhiteSpace(options.WebhookVerificationKeyId) &&
            !string.Equals(keyId, options.WebhookVerificationKeyId, StringComparison.Ordinal))
        {
            return VerificationOutcome.UnknownKeyId;
        }

        var signingInput = Encoding.ASCII.GetBytes(string.Concat(parts[0], ".", parts[1]));
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(options.WebhookVerificationKeyPem);

            // JWS carries the signature as raw r||s, not as the DER sequence VerifyData defaults to.
            if (!ecdsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return VerificationOutcome.SignatureMismatch;
            }
        }
        catch (ArgumentException)
        {
            return VerificationOutcome.SignatureMismatch;
        }
        catch (CryptographicException)
        {
            return VerificationOutcome.SignatureMismatch;
        }

        string? claimedBodyHash;
        long? issuedAt;
        try
        {
            using var payload = JsonDocument.Parse(payloadBytes);
            claimedBodyHash = ReadString(payload.RootElement, "request_body_sha256");
            issuedAt = payload.RootElement.TryGetProperty("iat", out var iat) && iat.TryGetInt64(out var value)
                ? value
                : null;
        }
        catch (JsonException)
        {
            return VerificationOutcome.MalformedToken;
        }

        if (string.IsNullOrWhiteSpace(claimedBodyHash))
        {
            return VerificationOutcome.MalformedToken;
        }

        // A signed-but-stale token is a replay of a genuine delivery, so it is refused even though
        // its signature is sound.
        if (issuedAt is { } issued &&
            (utcNow - DateTimeOffset.FromUnixTimeSeconds(issued)).Duration() > MaxIssuedAtSkew)
        {
            return VerificationOutcome.Expired;
        }

        var actualBodyHash = Sha256Digest.Compute(body);
        return Sha256Digest.FixedEquals(actualBodyHash, claimedBodyHash)
            ? VerificationOutcome.Verified
            : VerificationOutcome.BodyHashMismatch;
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(normalized.PadRight(normalized.Length + ((4 - (normalized.Length % 4)) % 4), '='));
    }
}
