using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportingProviderReceiptAuthentication(
    string Timestamp,
    string Signature);

public sealed record ReportingProviderReceiptAuthenticationRequest(
    string TransportId,
    string JobId,
    SecureReportingDeliveryReceiptCommand Receipt,
    ReportingProviderReceiptAuthentication Authentication);

public interface IReportingProviderReceiptAuthenticator
{
    ValueTask<bool> AuthenticateAsync(
        ReportingProviderReceiptAuthenticationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Default fail-closed authenticator used when no provider receipt secret is configured.</summary>
public sealed class RejectingReportingProviderReceiptAuthenticator : IReportingProviderReceiptAuthenticator
{
    public ValueTask<bool> AuthenticateAsync(
        ReportingProviderReceiptAuthenticationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
}

/// <summary>
/// Verifies relay webhook receipts with an HMAC over every state-bearing field. The key is retained
/// only in process memory and signatures are compared in constant time.
/// </summary>
public sealed class HmacReportingProviderReceiptAuthenticator : IReportingProviderReceiptAuthenticator
{
    private const int Sha256HexLength = 64;
    private readonly byte[] _secret;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _maximumClockSkew;

    public HmacReportingProviderReceiptAuthenticator(
        ReadOnlySpan<byte> secret,
        TimeProvider? timeProvider = null,
        TimeSpan? maximumClockSkew = null)
    {
        if (secret.Length < 32)
        {
            throw new ArgumentException("Reporting receipt HMAC secrets must contain at least 256 bits.", nameof(secret));
        }

        _secret = secret.ToArray();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maximumClockSkew = maximumClockSkew ?? TimeSpan.FromMinutes(5);
        if (_maximumClockSkew <= TimeSpan.Zero || _maximumClockSkew > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumClockSkew));
        }
    }

    public ValueTask<bool> AuthenticateAsync(
        ReportingProviderReceiptAuthenticationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!long.TryParse(
                request.Authentication.Timestamp?.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var unixSeconds))
        {
            return ValueTask.FromResult(false);
        }

        DateTimeOffset timestamp;
        try
        {
            timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return ValueTask.FromResult(false);
        }

        if ((_timeProvider.GetUtcNow() - timestamp).Duration() > _maximumClockSkew)
        {
            return ValueTask.FromResult(false);
        }

        var supplied = NormalizeSignature(request.Authentication.Signature);
        if (supplied is null)
        {
            return ValueTask.FromResult(false);
        }

        var expected = ComputeSignature(_secret, request, unixSeconds);
        return ValueTask.FromResult(CryptographicOperations.FixedTimeEquals(supplied, expected));
    }

    public static string CreateSignature(
        ReadOnlySpan<byte> secret,
        ReportingProviderReceiptAuthenticationRequest request,
        long unixSeconds)
    {
        if (secret.Length < 32)
        {
            throw new ArgumentException("Reporting receipt HMAC secrets must contain at least 256 bits.", nameof(secret));
        }

        return Convert.ToHexString(ComputeSignature(secret, request, unixSeconds)).ToLowerInvariant();
    }

    private static byte[] ComputeSignature(
        ReadOnlySpan<byte> secret,
        ReportingProviderReceiptAuthenticationRequest request,
        long unixSeconds)
    {
        var canonical = new StringBuilder();
        Append(canonical, unixSeconds.ToString(CultureInfo.InvariantCulture));
        Append(canonical, request.TransportId?.Trim());
        Append(canonical, request.JobId?.Trim());
        Append(canonical, request.Receipt.ProviderEventId?.Trim());
        Append(canonical, ((int)request.Receipt.Kind).ToString(CultureInfo.InvariantCulture));
        Append(canonical, request.Receipt.OccurredAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        Append(canonical, request.Receipt.ProviderReference?.Trim());
        Append(canonical, request.Receipt.EvidenceReference?.Trim());
        Append(canonical, request.Receipt.Detail?.Trim());
        return HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(canonical.ToString()));
    }

    private static byte[]? NormalizeSignature(string? signature)
    {
        var normalized = signature?.Trim();
        if (normalized?.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase) == true)
        {
            normalized = normalized["sha256=".Length..];
        }

        if (normalized is not { Length: Sha256HexLength } || !normalized.All(Uri.IsHexDigit))
        {
            return null;
        }

        return Convert.FromHexString(normalized);
    }

    private static void Append(StringBuilder target, string? value)
    {
        if (value is null)
        {
            target.Append("-1:");
            return;
        }

        target.Append(Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture));
        target.Append(':');
        target.Append(value);
    }
}
