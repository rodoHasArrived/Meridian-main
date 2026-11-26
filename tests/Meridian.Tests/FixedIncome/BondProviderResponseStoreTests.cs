using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.FixedIncome;

[Trait("Category", "Unit")]
public sealed class BondProviderResponseStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-provider-responses-" + Guid.NewGuid().ToString("N"));
    private FileBondProviderResponseStore Store => new(_root);
    private const string Json = " {\n\"observations\": [{\"date\":\"2026-11-09\",\"value\":\"4.2\"}]\n}\n";

    [Fact]
    public async Task ConcurrentExactRetries_PreserveExactResponseAndReopenAcrossStoreInstances()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Store.RetainAsync("tenant", "company", "fred", Json)));
        responses.Should().OnlyContain(row => row.ContentSha256 == Sha256Digest.ComputeUtf8(Json) && row.RawResponseJson == Json);
        responses.Distinct().Should().ContainSingle();
        (await Store.GetAsync("tenant", "company", responses[0].ContentSha256)).Should().Be(responses[0]);
        Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories).Should().ContainSingle();
    }

    [Fact]
    public async Task TenantAndCompanyScopes_CannotReadEachOthersResponse()
    {
        var response = await Store.RetainAsync("tenant", "company", "fred", Json);
        (await Store.GetAsync("other-tenant", "company", response.ContentSha256)).Should().BeNull();
        (await Store.GetAsync("tenant", "other-company", response.ContentSha256)).Should().BeNull();
        var second = await Store.RetainAsync("tenant", "other-company", "fred", Json);
        second.ContentSha256.Should().Be(response.ContentSha256);
        Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories).Should().HaveCount(2);
    }

    [Theory]
    [InlineData("4.2", "4.3")]
    [InlineData("fred", "tampered-provider")]
    public async Task ChangedContentOrMetadata_FailsClosedOnReadAndRetry(string before, string after)
    {
        var response = await Store.RetainAsync("tenant", "company", "fred", Json);
        var path = Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories).Single();
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace(before, after, StringComparison.Ordinal));
        Func<Task> read = () => Store.GetAsync("tenant", "company", response.ContentSha256);
        Func<Task> retry = () => Store.RetainAsync("tenant", "company", "fred", Json);
        await read.Should().ThrowAsync<InvalidDataException>();
        await retry.Should().ThrowAsync<InvalidDataException>();
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
