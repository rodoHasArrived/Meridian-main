using Meridian.Application.Reconciliation;

namespace Meridian.Tests.Application.Reconciliation;

public sealed class StatementReconciliationContextAdapterTests
{
    [Fact]
    public async Task IngestAsync_Uses_StatementImport_Path()
    {
        var service = new StatementReconciliationService();
        var sut = new StatementReconciliationContextAdapter(service);
        var path = Path.GetTempFileName();
        await File.WriteAllLinesAsync(path,
        [
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
            "A1,SPY,10,500,0,position,2026-05-29"
        ]);

        try
        {
            var result = await sut.IngestAsync(
                new DataIntegrationIngestionRequest("broker", path, MappingProfileId: null),
                CancellationToken.None);

            Assert.Equal("broker", result.SourceKind);
            Assert.Equal(path, result.SourcePath);
            Assert.Equal(1, result.ImportedRowCount);
            Assert.False(string.IsNullOrWhiteSpace(result.ImportId));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task IntakeAsync_Uses_Reconciliation_Case_Intake_Path()
    {
        var service = new StatementReconciliationService();
        var sut = new StatementReconciliationContextAdapter(service);
        var path = Path.GetTempFileName();
        await File.WriteAllLinesAsync(path,
        [
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
            "A1,SPY,10,500,0,position,2026-05-29",
            "A1,SPY,0,0,125.25,dividend,2026-05-30"
        ]);

        try
        {
            var result = await sut.IntakeAsync(
                new ReconciliationCaseIntakeRequest("broker", path, MappingProfileId: null),
                CancellationToken.None);

            Assert.Equal("broker", result.SourceKind);
            Assert.Equal(path, result.SourcePath);
            Assert.Equal(2, result.RowCount);
            Assert.Equal(1, result.MatchCount);
            Assert.Single(result.Cases);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
