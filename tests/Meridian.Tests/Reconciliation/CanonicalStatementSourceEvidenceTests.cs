using System.Text;
using FluentAssertions;
using Meridian.Core.IO;
using Meridian.DataIntegration.Reconciliation;
using Meridian.Domain.Reconciliation;
using Meridian.Storage.Archival;
using Meridian.Tests.Reconciliation.Connectors;
using NSubstitute;

namespace Meridian.Tests.Reconciliation;

public sealed class CanonicalStatementSourceEvidenceTests : IDisposable
{
    private readonly string _root = StatementConnectorTestData.CreateTempRoot("statement-source-evidence");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectImport_RetainsExactReplayBytesAcrossRemovalRestartAndConcurrentDuplicates(bool flex)
    {
        var source = flex
            ? StatementConnectorTestData.ReadFixture("ib-flex-sample.xml")
            : Encoding.UTF8.GetBytes("account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency\nA1,SPY,1,10,-10,trade,2026-06-02,,USD\n");
        var path = Path.Combine(_root, flex ? "statement.xml" : "statement.csv");
        await File.WriteAllBytesAsync(path, source);
        var request = new BrokerStatementImportRequest(flex ? "ib-flex" : "samplebroker", path, new DateOnly(2026, 6, 30))
        {
            ExternalAccountId = flex ? "U1234567" : "A1"
        };
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 4).Select(async _ =>
        {
            await start.Task;
            IBrokerStatementService service = flex
                ? new IbFlexBrokerStatementService(Store())
                : new CsvBrokerStatementService(Store());
            try
            { return await service.ImportAsync(request); }
            catch (StatementAlreadyImportedException) { return null; }
        }).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(attempts);
        var imported = results.Where(result => result is not null).Should().ContainSingle().Which!;
        File.Delete(path);

        var restarted = Store();
        var replay = await restarted.ReadSourceEvidenceAsync(imported.Import.ImportId);
        replay.Source.Should().Equal(source);
        replay.ParserInput.Should().Equal(source);
        Directory.GetFiles(Path.Combine(_root, "reconciliation", "statement-imports", "source-evidence"), "*.bin")
            .Should().ContainSingle("identical source and parser input are retained once by hash");
        (await restarted.GetImportAsync(imported.Import.ImportId))!.Rows.Should().BeEquivalentTo(imported.Rows);
        (await restarted.ListImportsAsync()).Should().ContainSingle();

        var replayPath = Path.Combine(_root, flex ? "replay.xml" : "replay.csv");
        await File.WriteAllBytesAsync(replayPath, replay.ParserInput);
        IBrokerStatementService replayService = flex
            ? new IbFlexBrokerStatementService(restarted)
            : new CsvBrokerStatementService(restarted);
        (await replayService.ValidateAsync(request with { SourcePath = replayPath })).IsValid.Should().BeTrue();
        await Assert.ThrowsAsync<StatementAlreadyImportedException>(() => replayService.ImportAsync(request with { SourcePath = replayPath }));

        var replayStore = new JsonCanonicalStatementStore(Path.Combine(_root, "independent-replay"), new AtomicFileWriterAdapter());
        IBrokerStatementService independentReplay = flex
            ? new IbFlexBrokerStatementService(replayStore)
            : new CsvBrokerStatementService(replayStore);
        var replayed = await independentReplay.ImportAsync(request with { SourcePath = replayPath });
        replayed.Rows.Should().BeEquivalentTo(imported.Rows,
            "reprocessing retained parser bytes in a fresh store must reproduce the original economics and row hashes");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedSourceRetention_DoesNotPublishImportAndRetryReusesDurablePrefix(bool cancel)
    {
        var sourcePath = Path.Combine(_root, "raw.dat");
        var parserPath = Path.Combine(_root, "canonical.csv");
        await File.WriteAllTextAsync(sourcePath, "original provider evidence");
        await File.WriteAllTextAsync(parserPath,
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency\nA1,SPY,1,10,-10,trade,2026-06-02,,USD\n");
        var request = new BrokerStatementImportRequest("samplebroker", sourcePath, new DateOnly(2026, 6, 30))
        {
            ExternalAccountId = "A1",
            CanonicalSourcePath = parserPath
        };
        var writer = Substitute.For<IAtomicFileWriter>();
        var writes = 0;
        using var cancellation = new CancellationTokenSource();
        writer.WriteAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (++writes != 2)
                    return AtomicFileWriter.WriteAsync(call.ArgAt<string>(0), call.ArgAt<byte[]>(1), call.ArgAt<CancellationToken>(2));
                if (!cancel)
                    return Task.FromException(new IOException("synthetic interruption before parser input publication"));
                cancellation.Cancel();
                return Task.FromCanceled(cancellation.Token);
            });
        var interrupted = new JsonCanonicalStatementStore(_root, writer);

        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CsvBrokerStatementService(interrupted).ImportAsync(request, cancellation.Token));
        else
            await Assert.ThrowsAsync<IOException>(() => new CsvBrokerStatementService(interrupted).ImportAsync(request));
        (await interrupted.ListImportsAsync()).Should().BeEmpty();
        Directory.GetFiles(Path.Combine(_root, "reconciliation", "statement-imports"), "*.json").Should().BeEmpty();

        var imported = await new CsvBrokerStatementService(Store()).ImportAsync(request);
        File.Delete(sourcePath);
        File.Delete(parserPath);
        var evidence = await Store().ReadSourceEvidenceAsync(imported.Import.ImportId);
        Encoding.UTF8.GetString(evidence.Source).Should().Be("original provider evidence");
        Encoding.UTF8.GetString(evidence.ParserInput).Should().Contain("A1,SPY,1,10,-10");
        (await Store().ListImportsAsync()).Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrCorruptRetainedSource_FailsReadsRatherThanReportingCompleteImport(bool remove)
    {
        var path = Path.Combine(_root, "statement.csv");
        await File.WriteAllTextAsync(path,
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate,settlementDate,currency\nA1,SPY,1,10,-10,trade,2026-06-02,,USD\n");
        var request = new BrokerStatementImportRequest("samplebroker", path, new DateOnly(2026, 6, 30)) { ExternalAccountId = "A1" };
        var imported = await new CsvBrokerStatementService(Store()).ImportAsync(request);
        var retainedPath = Path.Combine(_root, "reconciliation", "statement-imports", "source-evidence", imported.Import.SourceFileHash.ToLowerInvariant() + ".bin");
        if (remove)
            File.Delete(retainedPath);
        else
            await File.WriteAllTextAsync(retainedPath, "truncated or substituted source");

        await AssertEvidenceUnavailable(() => Store().GetImportAsync(imported.Import.ImportId));
        await AssertEvidenceUnavailable(() => Store().ListImportsAsync());
        await AssertEvidenceUnavailable(() => Store().ReadSourceEvidenceAsync(imported.Import.ImportId));
    }

    private static async Task AssertEvidenceUnavailable(Func<Task> read)
    {
        var failure = await Record.ExceptionAsync(read);
        Assert.True(failure is IOException or InvalidDataException, $"Expected unavailable or corrupt evidence, got {failure?.GetType().Name ?? "success"}.");
    }

    private JsonCanonicalStatementStore Store() => new(_root, new AtomicFileWriterAdapter());

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
