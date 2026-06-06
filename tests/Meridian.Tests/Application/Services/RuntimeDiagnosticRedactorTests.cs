using FluentAssertions;
using Meridian.Core.Diagnostics;
using Xunit;

namespace Meridian.Tests.Application.Services;

public sealed class RuntimeDiagnosticRedactorTests
{
    [Theory]
    [InlineData("Authorization: Bearer liveBearerToken123", "liveBearerToken123")]
    [InlineData("secretKey=paper-secret-value", "paper-secret-value")]
    [InlineData("connectionString=Server=db;Password=pwd123", "pwd123")]
    [InlineData("request=/v2/orders?api_key=query-secret&accountId=ACCT-123456", "query-secret")]
    [InlineData("callback=https://operator:url-secret@example.invalid/orders?client_secret=query-secret", "url-secret")]
    [InlineData("callback=https://operator:url-secret@example.invalid/orders?client_secret=query-secret", "query-secret")]
    [InlineData("{\"accountNumber\":\"ACCT-654321\",\"token\":\"json-token\"}", "ACCT-654321")]
    public void SanitizeText_RedactsSecretsAndAccountIdentifiers(string input, string sensitiveValue)
    {
        var sanitized = RuntimeDiagnosticRedactor.SanitizeText(input);

        sanitized.Should().Contain("[REDACTED]");
        sanitized.Should().NotContain(sensitiveValue);
    }

    [Fact]
    public void SanitizeEnvValue_RedactsSensitiveEnvironmentKeys()
    {
        var sanitized = RuntimeDiagnosticRedactor.SanitizeEnvValue("ALPACA_SECRET_KEY", "live-secret");

        sanitized.Should().Be("[REDACTED]");
    }

    [Fact]
    public void SanitizeEnvValue_OmitsPathValueForBrevity()
    {
        var sanitized = RuntimeDiagnosticRedactor.SanitizeEnvValue("PATH", "C:\\tools;C:\\Windows");

        sanitized.Should().Be("[PATH variable - omitted for brevity]");
    }
}
