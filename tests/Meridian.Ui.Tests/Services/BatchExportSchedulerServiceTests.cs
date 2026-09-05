using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

public sealed class BatchExportSchedulerServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "batch-export-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "source");
    private string Destination => Path.Combine(_root, "output.gz");
    private string Store => Path.Combine(_root, "jobs.json");

    public BatchExportSchedulerServiceTests() => Directory.CreateDirectory(Source);

    [Fact]
    public async Task QueueJob_ConcurrentRequestsAndCancelledEntry_ExecuteOnlyNewestEntry()
    {
        await using var service = new BatchExportSchedulerService(jobStorePath: Store, queuePollIntervalMs: 5);
        var job = service.CreateJob(Request());
        var duplicateResults = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => service.QueueJob(job.Id))));
        duplicateResults.Should().OnlyContain(result => !result);
        service.CancelJob(job.Id).Should().BeTrue();
        service.QueueJob(job.Id).Should().BeTrue();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.JobCompleted += (_, _) => completed.TrySetResult();
        await service.StartAsync();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync();
        service.GetJobHistory(job.Id).Should().ContainSingle(run => run.Success);
    }

    [Fact]
    public async Task CancelledQueuedJob_IsSkippedWhileFollowingJobCompletes()
    {
        await using var service = new BatchExportSchedulerService(maxConcurrentJobs: 1, jobStorePath: Store, queuePollIntervalMs: 5);
        var cancelled = service.CreateJob(Request());
        service.CancelJob(cancelled.Id);
        var next = service.CreateJob(Request());
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.JobCompleted += (_, args) => { if (args.Job.Id == next.Id) completed.TrySetResult(); };
        await service.StartAsync();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync();
        cancelled.Status.Should().Be(ExportJobStatus.Cancelled);
        service.GetJobHistory(cancelled.Id).Should().BeEmpty();
        service.GetJobHistory(next.Id).Should().ContainSingle();
    }

    [Fact]
    public async Task JsonLines_DecompressesToActualArtifactAndCountsItsBytes()
    {
        var input = Path.Combine(Source, "trades.jsonl.gz");
        await using (var gzip = new GZipStream(File.Create(input), CompressionMode.Compress))
        await using (var writer = new StreamWriter(gzip))
            await writer.WriteLineAsync("{\"symbol\":\"EUR\"}");
        var job = await RunAsync(ExportFormat.JsonLines);
        var artifact = Path.Combine(Destination, "trades.jsonl");
        job.Status.Should().Be(ExportJobStatus.Completed);
        job.TotalBytesExported.Should().Be(new FileInfo(artifact).Length);
        File.Exists(Path.Combine(Destination, "trades.jsonl.gz")).Should().BeFalse();
    }

    [Fact]
    public async Task Parquet_IsRejectedWithoutCreatingAJobOrSubstituteArtifact()
    {
        await using var service = new BatchExportSchedulerService(jobStorePath: Store);
        var act = () => service.CreateJob(Request(ExportFormat.Parquet));
        act.Should().Throw<NotSupportedException>();
        service.Jobs.Should().BeEmpty();
        Directory.Exists(Destination).Should().BeFalse();
    }

    [Fact]
    public async Task Csv_UsesUnionSchemaAndEscapesHeadersAndNestedValues()
    {
        await File.WriteAllLinesAsync(Path.Combine(Source, "trades.jsonl"),
            ["{\"symbol\":\"AAPL\",\"nested\":{\"bid\":1,\"ask\":2}}", "{\"symbol\":\"A,\\\"B\",\"extra,field\":[1,2]}"]);
        var job = await RunAsync(ExportFormat.Csv);
        job.Status.Should().Be(ExportJobStatus.Completed);
        var csv = await File.ReadAllTextAsync(Path.Combine(Destination, "trades.csv"));
        csv.Should().Be("\"symbol\",\"nested\",\"extra,field\"" + Environment.NewLine +
            "\"AAPL\",\"{\"\"bid\"\":1,\"\"ask\"\":2}\",\"\"" + Environment.NewLine +
            "\"A,\"\"B\",\"\",\"[1,2]\"");
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[1,2]")]
    [InlineData("{\"a\":1,\"a\":2}")]
    public async Task Csv_RejectedRowFailsJobAndPreservesExistingArtifact(string rejected)
    {
        await File.WriteAllLinesAsync(Path.Combine(Source, "trades.jsonl"), ["{\"a\":1}", rejected]);
        Directory.CreateDirectory(Destination);
        var artifact = Path.Combine(Destination, "trades.csv");
        await File.WriteAllTextAsync(artifact, "previous artifact");
        var job = await RunAsync(ExportFormat.Csv);
        job.Status.Should().Be(ExportJobStatus.Failed);
        job.RunHistory.Should().ContainSingle(run => !run.Success && run.ErrorMessage!.Contains("rejected 1 row(s): 2"));
        (await File.ReadAllTextAsync(artifact)).Should().Be("previous artifact");
    }

    [Fact]
    public async Task ConcurrentCreationAndSaves_PersistEveryJobAndExposeWriteFailure()
    {
        await using var service = new BatchExportSchedulerService(jobStorePath: Store);
        var jobs = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => service.CreateJob(Request()))));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => service.SaveJobsAsync()));
        var persisted = JsonSerializer.Deserialize<List<ExportJob>>(await File.ReadAllTextAsync(Store), DesktopJsonOptions.PrettyPrint);
        persisted!.Select(job => job.Id).Should().BeEquivalentTo(jobs.Select(job => job.Id));
        (await service.ReadPersistedJobsAsync()).Select(job => job.Id).Should().BeEquivalentTo(jobs.Select(job => job.Id));
        File.Delete(Store);
        Directory.CreateDirectory(Store);
        try
        {
            var create = () => service.CreateJob(Request());
            create.Should().Throw<Exception>();
            service.LastPersistenceError.Should().NotBeNull();
            var save = () => service.SaveJobsAsync();
            await save.Should().ThrowAsync<Exception>();
        }
        finally
        {
            Directory.Delete(Store);
        }
        await service.SaveJobsAsync();
        service.LastPersistenceError.Should().BeNull();
    }

    private ExportJobRequest Request(ExportFormat format = ExportFormat.Raw) => new()
    {
        SourcePath = Source,
        DestinationPath = Destination,
        Format = format
    };

    private async Task<ExportJob> RunAsync(ExportFormat format)
    {
        await using var service = new BatchExportSchedulerService(jobStorePath: Store, queuePollIntervalMs: 5);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.JobCompleted += (_, _) => finished.TrySetResult();
        service.JobFailed += (_, _) => finished.TrySetResult();
        var job = service.CreateJob(Request(format));
        await service.StartAsync();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync();
        return job;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
