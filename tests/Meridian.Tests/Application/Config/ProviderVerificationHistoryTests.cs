using System.Text.Json;
using FluentAssertions;
using Meridian.DataIntegration.Credentials;
using Xunit;

namespace Meridian.Tests.Application.Config;

public sealed class ProviderVerificationHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-provider-history", Guid.NewGuid().ToString("N"));
    private static readonly ProviderCredentialScope Scope = new("tenant-1", "connection-1", "account-1", "paper");

    [Fact]
    public async Task Read_UsesExactOwnedScopeAndReturnsOnlyNonSecretVerificationObservations()
    {
        var store = new FileProviderCredentialStore(_root);
        await WriteAuditAsync(
            Entry("2026-11-01T00:00:00Z", "verify-success", Scope),
            Entry("2026-11-02T00:00:00Z", "verify-failure", new("tenant-2", "connection-1", "account-1", "paper")),
            Entry("2026-11-03T00:00:00Z", "verify-success", null),
            Entry("2026-11-04T00:00:00Z", "verify-failure", new("tenant-1", "connection-1", "account-2", "paper")),
            Entry("2026-11-05T00:00:00Z", "verify-failure", Scope));

        var history = await store.GetVerificationHistoryAsync("alpaca", Scope);
        history.Observations.Should().HaveCount(2);
        history.Observations.Select(item => item.Succeeded).Should().Equal(true, false);
        history.IsTruncated.Should().BeFalse();
        JsonSerializer.Serialize(history).Should().NotContain("Actor").And.NotContain("ExternalAccountId");
    }

    [Fact]
    public async Task Read_BoundsByNewestRecordedTimeEvenWhenAppendOrderDiffers()
    {
        var store = new FileProviderCredentialStore(_root);
        await WriteAuditAsync(Entry("2026-11-05T00:00:00Z", "verify-success", Scope),
            Entry("2026-11-01T00:00:00Z", "verify-failure", Scope),
            Entry("2026-11-03T00:00:00Z", "verify-failure", Scope));
        var history = await store.GetVerificationHistoryAsync("alpaca", Scope, 2);
        history.Observations.Select(item => item.RecordedAtUtc.Day).Should().Equal(3, 5);
        history.IsTruncated.Should().BeTrue();
    }

    [Fact]
    public async Task Read_EmptyStoreHasNoInventedHistoryAndMalformedAuditRefusesPartialSuccess()
    {
        var store = new FileProviderCredentialStore(_root);
        (await store.GetVerificationHistoryAsync("alpaca", Scope)).Observations.Should().BeEmpty();
        await WriteAuditAsync(Entry("2026-11-01T00:00:00Z", "verify-success", Scope), "{broken");
        await ((Func<Task>)(() => store.GetVerificationHistoryAsync("alpaca", Scope))).Should().ThrowAsync<JsonException>();
    }

    private Task WriteAuditAsync(params string[] entries)
    {
        Directory.CreateDirectory(Path.Combine(_root, ".mdc"));
        return File.WriteAllLinesAsync(Path.Combine(_root, ".mdc", "provider-credentials.audit.jsonl"), entries);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"action\":5}")]
    public async Task Read_MalformedEnvelopeRefusesACompleteHistory(string malformed)
    {
        var store = new FileProviderCredentialStore(_root);
        await WriteAuditAsync(malformed);
        await ((Func<Task>)(() => store.GetVerificationHistoryAsync("alpaca", Scope))).Should().ThrowAsync<InvalidDataException>();
    }

    private static string Entry(string time, string action, ProviderCredentialScope? scope) => JsonSerializer.Serialize(new
    {
        timestamp = DateTimeOffset.Parse(time), providerId = "alpaca", action, actor = "synthetic-operator",
        credentialState = 4, credentialSource = 1, verificationState = 1, fieldNames = Array.Empty<string>(),
        environment = "paper", externalAccountId = "account-1", scope
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
