using System.Text.Json.Nodes;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.PortfolioRecords.FundAccounts;
using Xunit;

namespace Meridian.FundStructure.Tests;

/// <summary>
/// Regression coverage for unreadable fund-structure snapshots.
/// <para>
/// The service follows a load-mutate-save pattern, so a swallowed load failure that fell back to an
/// empty working set would let the next save atomically overwrite the governance graph with nothing.
/// These tests pin the quarantine copy that makes such a reset recoverable.
/// </para>
/// </summary>
public sealed class FundStructurePersistenceRecoveryTests : IDisposable
{
    private const string CorruptJson = "{ this is not valid fund structure json";

    private readonly string _tempRoot;
    private readonly string _snapshotPath;

    public FundStructurePersistenceRecoveryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MeridianFundStructureRecoveryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _snapshotPath = Path.Combine(_tempRoot, "fund-structure.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup of temp files.
        }
    }

    private string[] QuarantineFiles() =>
        Directory.GetFiles(_tempRoot, $"{Path.GetFileName(_snapshotPath)}.corrupt-*");

    private InMemoryFundStructureService CreateService() =>
        new(new InMemoryFundAccountService(), _snapshotPath);

    private async Task<int> CountOrganizationsAsync(InMemoryFundStructureService service)
    {
        var graph = await service.GetOrganizationStructureAsync(new OrganizationStructureQuery());
        return graph.Organizations.Count;
    }

    private static CreateOrganizationRequest NewOrganization(Guid id, string code, string name) =>
        new(id, code, name, "USD", new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero), "test");

    [Fact]
    public async Task MissingSnapshot_StartsEmptyWithoutQuarantine()
    {
        var service = CreateService();

        Assert.Equal(0, await CountOrganizationsAsync(service));
        Assert.Empty(QuarantineFiles());
    }

    [Fact]
    public async Task CorruptSnapshot_StartsEmptyAndPreservesOriginal()
    {
        File.WriteAllText(_snapshotPath, CorruptJson);

        var service = CreateService();

        Assert.Equal(0, await CountOrganizationsAsync(service));

        var quarantined = Assert.Single(QuarantineFiles());
        Assert.Equal(CorruptJson, File.ReadAllText(quarantined));
        Assert.True(File.Exists(_snapshotPath), "loading must never delete the original snapshot");
    }

    /// <summary>
    /// The regression this whole change exists for: before the fix, the save below overwrote the
    /// unreadable snapshot atomically and the original bytes were gone for good.
    /// </summary>
    [Fact]
    public async Task MutationAfterCorruptLoad_KeepsOriginalContentInQuarantine()
    {
        File.WriteAllText(_snapshotPath, CorruptJson);
        var service = CreateService();

        await service.CreateOrganizationAsync(NewOrganization(Guid.NewGuid(), "ORG-RECOVERED", "Recovered"));

        var quarantined = Assert.Single(QuarantineFiles());
        Assert.Equal(CorruptJson, File.ReadAllText(quarantined));

        // The save really did happen — the quarantine is not just an artefact of a failed write.
        Assert.NotEqual(CorruptJson, File.ReadAllText(_snapshotPath));
        Assert.Equal(1, await CountOrganizationsAsync(CreateService()));
    }

    /// <summary>
    /// A snapshot can deserialize cleanly and still fail partway through materialisation. The
    /// earlier collections are already in the dictionaries by then, and a half-loaded governance
    /// graph is worse than an empty one because it still looks valid.
    /// </summary>
    [Fact]
    public async Task SnapshotThatFailsPartWayThrough_DiscardsTheAlreadyLoadedCollections()
    {
        var seed = CreateService();
        await seed.CreateOrganizationAsync(NewOrganization(Guid.NewGuid(), "ORG-001", "Seeded"));
        await seed.CreateBusinessAsync(new CreateBusinessRequest(
            Guid.NewGuid(),
            (await seed.GetOrganizationStructureAsync(new OrganizationStructureQuery())).Organizations[0].OrganizationId,
            BusinessKindDto.FundManager,
            "FUND-OPS",
            "Fund Operations",
            "USD",
            new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero),
            "test"));

        // Organizations still parse; businesses blow up during materialisation.
        var snapshot = JsonNode.Parse(File.ReadAllText(_snapshotPath))!.AsObject();
        Assert.NotEmpty(snapshot["organizations"]!.AsArray());
        snapshot["businesses"] = new JsonArray(null);
        File.WriteAllText(_snapshotPath, snapshot.ToJsonString());
        var corrupted = File.ReadAllText(_snapshotPath);

        var reloaded = CreateService();

        Assert.Equal(0, await CountOrganizationsAsync(reloaded));
        var quarantined = Assert.Single(QuarantineFiles());
        Assert.Equal(corrupted, File.ReadAllText(quarantined));
    }

    /// <summary>
    /// Continuing with an empty working set is only safe once the original bytes are preserved, so
    /// a quarantine that cannot be taken has to fail loudly rather than arm a destructive save.
    /// </summary>
    [Fact]
    public void UnquarantinableSnapshot_FailsInsteadOfArmingADestructiveSave()
    {
        File.WriteAllText(_snapshotPath, CorruptJson);

        // An exclusive handle makes both the read and the quarantine copy fail.
        using var exclusive = new FileStream(
            _snapshotPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Throws<InvalidOperationException>(() => CreateService());
        Assert.Empty(QuarantineFiles());
    }
}
