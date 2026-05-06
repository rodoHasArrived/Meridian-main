using Meridian.Application.Reconciliation;
using Meridian.Domain.Reconciliation;

namespace Meridian.Tests;

public sealed class StatementReconciliationServiceTests
{
    [Fact]
    public void Fingerprint_IsDeterministic()
    {
        var a = DeterministicFingerprint.Compute("abc");
        var b = DeterministicFingerprint.Compute("abc");
        Assert.Equal(a, b);
    }

    [Fact]
    public void MatchRows_ProducesMatchAndCaseBranches()
    {
        var svc = new StatementReconciliationService();
        var rows = new[]
        {
            new NormalizedStatementRow("1", StatementRowKind.Position, "AAPL", 10, 0, DateTimeOffset.UtcNow, "USD", "f1", new Dictionary<string,string>()),
            new NormalizedStatementRow("2", StatementRowKind.CashBalance, string.Empty, 0, 100, DateTimeOffset.UtcNow, "USD", "f2", new Dictionary<string,string>())
        };

        var result = svc.MatchRows(rows);
        Assert.Single(result.Matches);
        Assert.Single(result.Cases);
        Assert.Equal("case:2", result.Cases[0].CaseId);
    }

    [Fact]
    public async Task ValidateAsync_ThrowsWhenLocalFileMissing()
    {
        var svc = new StatementReconciliationService();

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            svc.ValidateAsync("local", "/tmp/does-not-exist.csv", CancellationToken.None));
    }

    [Fact]
    public async Task ImportAsync_CountsRowsForLocalFile()
    {
        var svc = new StatementReconciliationService();
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath, ["header", "row1", "row2"]);
            var result = await svc.ImportAsync("local", filePath, CancellationToken.None);
            Assert.Equal(3, result.RowCount);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
