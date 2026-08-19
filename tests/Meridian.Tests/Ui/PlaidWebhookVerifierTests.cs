using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Plaid;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Covers the two independent checks a Plaid webhook must pass: the token is signed by the
/// configured key, and the body actually received hashes to what the signed token claims. Each
/// test breaks exactly one of them, because a verifier that only enforces one is no verifier at
/// all — the caller supplies both the body and the hash.
/// </summary>
public sealed class PlaidWebhookVerifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Body = Encoding.UTF8.GetBytes(
        """{"webhook_type":"TRANSACTIONS","webhook_code":"SYNC_UPDATES_AVAILABLE","item_id":"item-1"}""");

    [Fact]
    public void Verify_WithSignedTokenMatchingBody_Verifies()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = OptionsFor(key);
        var token = SignToken(key, Sha256Digest.Compute(Body), Now);

        PlaidWebhookVerifier.Verify(options, token, Body, Now)
            .Should().Be(PlaidWebhookVerifier.VerificationOutcome.Verified);
    }

    [Fact]
    public void Verify_WhenBodyChangedAfterSigning_ReportsBodyHashMismatch()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = OptionsFor(key);
        var token = SignToken(key, Sha256Digest.Compute(Body), Now);
        var tampered = Encoding.UTF8.GetBytes(
            """{"webhook_type":"TRANSACTIONS","webhook_code":"SYNC_UPDATES_AVAILABLE","item_id":"item-2"}""");

        PlaidWebhookVerifier.Verify(options, token, tampered, Now)
            .Should().Be(PlaidWebhookVerifier.VerificationOutcome.BodyHashMismatch);
    }

    [Fact]
    public void Verify_WhenSignedByAnotherKey_ReportsSignatureMismatch()
    {
        using var configuredKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = OptionsFor(configuredKey);

        // The attacker states the correct body hash; only the signing key differs.
        var token = SignToken(attackerKey, Sha256Digest.Compute(Body), Now);

        PlaidWebhookVerifier.Verify(options, token, Body, Now)
            .Should().Be(PlaidWebhookVerifier.VerificationOutcome.SignatureMismatch);
    }

    [Fact]
    public void Verify_WithUnsignedNoneAlgorithm_IsRefusedBeforeAnyHashComparison()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = OptionsFor(key);
        var header = Base64Url(Encoding.UTF8.GetBytes("""{"alg":"none","typ":"JWT"}"""));
        var payload = Base64Url(Encoding.UTF8.GetBytes(
            $$"""{"request_body_sha256":"{{Sha256Digest.Compute(Body)}}","iat":{{Now.ToUnixTimeSeconds()}}}"""));

        PlaidWebhookVerifier.Verify(options, $"{header}.{payload}.", Body, Now)
            .Should().Be(PlaidWebhookVerifier.VerificationOutcome.UnsupportedAlgorithm);
    }

    [Fact]
    public void Verify_WithStaleIssuedAt_IsRefusedEvenThoughTheSignatureIsSound()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = OptionsFor(key);
        var token = SignToken(key, Sha256Digest.Compute(Body), Now.AddMinutes(-30));

        PlaidWebhookVerifier.Verify(options, token, Body, Now)
            .Should().Be(PlaidWebhookVerifier.VerificationOutcome.Expired);
    }

    [Fact]
    public void Verify_WithMismatchedKeyId_IsRefused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = OptionsFor(key) with { WebhookVerificationKeyId = "expected-kid" };
        var token = SignToken(key, Sha256Digest.Compute(Body), Now, keyId: "rotated-kid");

        PlaidWebhookVerifier.Verify(options, token, Body, Now)
            .Should().Be(PlaidWebhookVerifier.VerificationOutcome.UnknownKeyId);
    }

    [Fact]
    public void Verify_WithNoConfiguredKey_FailsClosed()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var token = SignToken(key, Sha256Digest.Compute(Body), Now);

        PlaidWebhookVerifier.Verify(PlaidOptions.Default, token, Body, Now)
            .Should().Be(PlaidWebhookVerifier.VerificationOutcome.NotConfigured);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    public void Verify_WithMissingOrMalformedHeader_IsRefused(string? header)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        PlaidWebhookVerifier.Verify(OptionsFor(key), header, Body, Now)
            .Should().BeOneOf(
                PlaidWebhookVerifier.VerificationOutcome.MissingHeader,
                PlaidWebhookVerifier.VerificationOutcome.MalformedToken);
    }

    private static PlaidOptions OptionsFor(ECDsa key)
        => PlaidOptions.Default with { WebhookVerificationKeyPem = key.ExportSubjectPublicKeyInfoPem() };

    private static string SignToken(ECDsa key, string bodySha256, DateTimeOffset issuedAt, string? keyId = null)
    {
        var header = keyId is null
            ? """{"alg":"ES256","typ":"JWT"}"""
            : $$"""{"alg":"ES256","typ":"JWT","kid":"{{keyId}}"}""";
        var payload = $$"""{"request_body_sha256":"{{bodySha256}}","iat":{{issuedAt.ToUnixTimeSeconds()}}}""";
        var signingInput = $"{Base64Url(Encoding.UTF8.GetBytes(header))}.{Base64Url(Encoding.UTF8.GetBytes(payload))}";
        var signature = key.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
