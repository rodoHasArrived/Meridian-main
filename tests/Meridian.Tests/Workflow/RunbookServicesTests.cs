using FluentAssertions;
using Meridian.Workflow.Runbooks;

namespace Meridian.Tests.Workflow;

public sealed class RunbookServicesTests : IDisposable
{
    private readonly string _dataRoot;

    public RunbookServicesTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "Meridian.Tests", "Runbooks", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task JsonRunbookStore_PersistsDefinitionsAndListsByName()
    {
        var store = new JsonRunbookStore(_dataRoot);
        var now = DateTimeOffset.Parse("2026-06-06T00:00:00Z");

        await store.SaveAsync(new RunbookDefinition(
            "z-close",
            "Close Package",
            "Prepare close package.",
            [new RunbookStep("close", "fund-1")],
            now,
            now));
        await store.SaveAsync(new RunbookDefinition(
            "a-readiness",
            "Accounting Readiness",
            "Validate retained evidence.",
            [new RunbookStep("readiness", "fund-1")],
            now,
            now.AddMinutes(1)));

        var listed = await store.ListAsync();
        var fetched = await store.GetAsync("z-close");

        listed.Select(x => x.Id).Should().Equal("a-readiness", "z-close");
        fetched.Should().NotBeNull();
        fetched!.Steps.Should().ContainSingle()
            .Which.Should().Be(new RunbookStep("close", "fund-1"));
    }

    [Fact]
    public async Task RunbookExecutor_EmitsDryRunStepMessages()
    {
        var executor = new RunbookExecutor();
        var definition = new RunbookDefinition(
            "readiness",
            "Readiness",
            "Check readiness.",
            [new RunbookStep("readiness", "global")],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var result = await executor.ExecuteAsync(definition, dryRun: true);

        result.Success.Should().BeTrue();
        result.RunbookId.Should().Be("readiness");
        result.Messages.Should().Contain("Runbook 'Readiness' started (dry-run).");
        result.Messages.Should().Contain("Step 1: readiness -> global");
        result.Messages.Should().Contain("Runbook completed.");
    }

    [Fact]
    public async Task RunbookExecutor_HonorsCancellationBeforeExecutingSteps()
    {
        var executor = new RunbookExecutor();
        var definition = new RunbookDefinition(
            "readiness",
            "Readiness",
            "Check readiness.",
            [new RunbookStep("readiness", "global")],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => executor.ExecuteAsync(definition, dryRun: false, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
