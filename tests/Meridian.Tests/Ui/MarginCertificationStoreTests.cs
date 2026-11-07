using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Guards retained margin certification evidence: an unreadable store must never be treated as
/// empty and then overwritten by the next certification.
/// </summary>
public sealed class MarginCertificationStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meridian-margin-cert-store-{Guid.NewGuid():N}");

    private string StorePath => Path.Combine(_root, "accounting", "margin-certifications.json");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static MarginCertificationResultDto Certification(string accountId) => new(
        ProviderId: "prime-a",
        AccountId: accountId,
        AsOf: new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero),
        EvidencePath: "evidence.json",
        CertifiedBy: "operator",
        CertifiedAtUtc: new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero),
        Note: "ok",
        Status: "Certified");

    [Fact]
    public async Task UpsertAsync_UnreadableStore_ThrowsAndLeavesExistingEvidenceUntouched()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        const string corrupt = "[{\"providerId\":\"prime-a\", truncated";
        await File.WriteAllTextAsync(StorePath, corrupt);
        var store = new MarginCertificationStore(_root);

        var act = () => store.UpsertAsync(Certification("ACC-2"));

        await act.Should().ThrowAsync<InvalidDataException>();
        (await File.ReadAllTextAsync(StorePath)).Should().Be(corrupt,
            "the retained evidence must not be replaced by a one-record file");
    }

    [Fact]
    public async Task UpsertAsync_ReadableStore_AppendsWithoutLosingPriorRecords()
    {
        var store = new MarginCertificationStore(_root);

        await store.UpsertAsync(Certification("ACC-1"));
        await store.UpsertAsync(Certification("ACC-2"));

        (await store.ListAsync()).Select(item => item.AccountId).Should().BeEquivalentTo(["ACC-1", "ACC-2"]);
    }
}
