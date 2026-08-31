using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;
using Xunit;

namespace Meridian.Tests.Reconciliation;

public sealed class FileStatementToleranceProfileProviderTests : IDisposable
{
    private readonly string _root;

    public FileStatementToleranceProfileProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"meridian-tol-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the per-test temp directory.
        }
    }

    [Fact]
    public async Task Load_WithoutFile_ResolvesDefaultOnly()
    {
        var provider = FileStatementToleranceProfileProvider.Load(_root);

        (await provider.GetProfileAsync(StatementToleranceProfile.DefaultProfileId)).ProfileId
            .Should().Be(StatementToleranceProfile.DefaultProfileId);
        await Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await provider.GetProfileAsync("statement-loose"));
    }

    [Fact]
    public async Task Load_WithOperatorProfile_ResolvesItAndKeepsDefault()
    {
        WriteTable("""
            {
              "profiles": [
                {
                  "profileId": "statement-loose", "version": 2,
                  "cashRules": [ { "ruleId": "c1", "absoluteCashTolerance": 1.00, "basisPointCashTolerance": null, "settlementDateToleranceDays": 5 } ],
                  "positionRules": [ { "ruleId": "p1", "quantityTolerance": 0.0001, "marketValueTolerance": 0, "priceTolerance": 0 } ],
                  "transactionRules": [ { "ruleId": "t1", "absoluteCashTolerance": 1.00, "settlementDateToleranceDays": 5, "priceTolerance": 0 } ]
                }
              ]
            }
            """);

        var provider = FileStatementToleranceProfileProvider.Load(_root);

        var loose = await provider.GetProfileAsync("statement-loose");
        loose.CashRules.Should().ContainSingle();
        loose.CashRules[0].AbsoluteCashTolerance.Should().Be(1.00m);
        loose.CashRules[0].SettlementDateTolerance.Should().Be(TimeSpan.FromDays(5));

        (await provider.GetProfileAsync(StatementToleranceProfile.DefaultProfileId)).ProfileId
            .Should().Be(StatementToleranceProfile.DefaultProfileId);
    }

    [Fact]
    public async Task Load_WithMalformedFile_ResolvesDefaultOnly()
    {
        WriteTable("{ not valid json");

        var provider = FileStatementToleranceProfileProvider.Load(_root);

        (await provider.GetProfileAsync(StatementToleranceProfile.DefaultProfileId)).Should().NotBeNull();
        await Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await provider.GetProfileAsync("statement-loose"));
    }

    private void WriteTable(string json)
    {
        var path = Path.Combine(_root, "reconciliation", "tolerance-profiles.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}
