using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Execution.Services;
using Meridian.Platform.Scheduling;
using Meridian.Testing;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class PaperSessionDailyReconciliationTests
{
    [Fact]
    public void CutoffsResumeAfterRetainedEvidenceAndIncludeEarlyClose()
    {
        var calendar = new TradingCalendar();
        var openedAt = new DateTimeOffset(2025, 11, 26, 15, 0, 0, TimeSpan.Zero);
        var priorCutoff = new DateTimeOffset(2025, 11, 26, 21, 0, 0, TimeSpan.Zero);
        var blackFridayClose = new DateTimeOffset(2025, 11, 28, 18, 0, 0, TimeSpan.Zero);
        var saturday = new DateTimeOffset(2025, 11, 29, 23, 0, 0, TimeSpan.Zero);

        PaperSessionDailyReconciliation.Cutoffs(calendar, openedAt, null, priorCutoff.AddMinutes(-1)).Should().BeEmpty();
        PaperSessionDailyReconciliation.Cutoffs(calendar, openedAt, null, saturday)
            .Should().Equal(priorCutoff, blackFridayClose);
        PaperSessionDailyReconciliation.Cutoffs(calendar, openedAt, priorCutoff, blackFridayClose.AddTicks(-1))
            .Should().BeEmpty();
        PaperSessionDailyReconciliation.Cutoffs(calendar, openedAt, priorCutoff, saturday)
            .Should().Equal(blackFridayClose);
    }

    [Fact]
    public async Task ExactReceiptRetryRecoversSameCutoffAcrossInstancesAndOffsets()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(ExactReceiptRetryRecoversSameCutoffAcrossInstancesAndOffsets));
        var store = new PaperSessionDailyReconciliationStore(artifacts.RootPath);
        var cutoff = new DateTimeOffset(2025, 11, 28, 18, 0, 0, TimeSpan.Zero);
        var evidence = new PaperSessionCutoffEvidence(
            "simulated-session", "owned-account", "USD", cutoff, 64m, 63m,
            [new("owned-account", "CashAtCutoff", 63m, 64m)], null);

        await store.RecordAsync(evidence);
        var restarted = new PaperSessionDailyReconciliationStore(artifacts.RootPath);
        await restarted.RecordAsync(evidence with { Cutoff = cutoff.ToOffset(TimeSpan.FromHours(-5)) });
        var retained = await restarted.ReadAsync(evidence.SessionId, evidence.AccountId, cutoff);

        retained.Should().BeEquivalentTo(evidence);
        (await restarted.ReadLatestAsync(evidence.SessionId, evidence.AccountId, CancellationToken.None))
            .Should().BeEquivalentTo(evidence);
        (await restarted.ReadAsync(evidence.SessionId, "different-account", cutoff)).Should().BeNull();
        await Assert.ThrowsAsync<InvalidDataException>(() => restarted.RecordAsync(evidence with { JournalCash = 66m }));
        (await restarted.ReadAsync(evidence.SessionId, evidence.AccountId, cutoff)).Should().BeEquivalentTo(evidence);
    }

    [Fact]
    public void BackdatedActivityInvalidatesTheViewEvenWhenCashAndBreaksAgree()
    {
        var retained = new PaperSessionCutoffEvidence(
            "simulated-session", "owned-account", "USD", new DateTimeOffset(2025, 11, 28, 18, 0, 0, TimeSpan.Zero),
            100m, 100m, [], null)
        { SourceHashSha256 = new string('a', 64) };
        var current = retained with { SourceHashSha256 = new string('b', 64) };

        PaperSessionDailyReconciliationStore.AssessFreshness(retained, retained).Should().BeSameAs(retained);
        PaperSessionDailyReconciliationStore.AssessFreshness(retained, current).CoverageBlocker
            .Should().Contain("changed the cutoff evidence");
        retained.CoverageBlocker.Should().BeNull();
    }

    [Fact]
    public async Task PaperOnlyWorkerRetainsMissingJournalCoverageAsAnExplicitBreak()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(PaperOnlyWorkerRetainsMissingJournalCoverageAsAnExplicitBreak));
        var cutoff = new DateTimeOffset(2025, 11, 28, 18, 0, 0, TimeSpan.Zero);
        var legacy = new PersistedSessionRecord("PAPER-LEGACY", "synthetic-strategy", "Legacy paper session",
            100m, cutoff.AddHours(-3), null, true, ["ABC"]);
        var sessions = new JsonlFilePaperSessionStore(Path.Combine(artifacts.RootPath, "sessions"),
            NullLogger<JsonlFilePaperSessionStore>.Instance);
        await sessions.SaveSessionMetadataAsync(legacy);
        var receipts = new PaperSessionDailyReconciliationStore(Path.Combine(artifacts.RootPath, "receipts"));
        await using var services = new ServiceCollection()
            .AddSingleton(receipts)
            .AddSingleton(_ => new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance, sessions))
            .BuildServiceProvider();

        var before = await PaperSessionDailyReconciliation.RunAsync(
            services, cutoff.AddMinutes(-1), NullLogger.Instance, CancellationToken.None);
        var completed = await PaperSessionDailyReconciliation.RunAsync(
            services, cutoff, NullLogger.Instance, CancellationToken.None);
        var retained = await receipts.ReadAsync(legacy.SessionId, PaperTradingPortfolio.DefaultAccountId, cutoff);

        before.Should().Be(OperationTerminalState.Unknown);
        completed.Should().Be(OperationTerminalState.CompletedWithWarnings);
        retained.Should().NotBeNull();
        retained!.CoverageBlocker.Should().Contain("no verified journal coverage");
        retained.JournalCash.Should().BeNull();
        retained.PortfolioCash.Should().BeNull();

        var retried = await PaperSessionDailyReconciliation.RunAsync(
            services, cutoff, NullLogger.Instance, CancellationToken.None);
        retried.Should().Be(OperationTerminalState.CompletedWithWarnings);
        (await receipts.ReadAsync(legacy.SessionId, PaperTradingPortfolio.DefaultAccountId, cutoff))
            .Should().BeEquivalentTo(retained);
    }
}
