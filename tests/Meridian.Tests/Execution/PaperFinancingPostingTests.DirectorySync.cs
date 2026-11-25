using FluentAssertions;
using Meridian.Storage.Archival;

namespace Meridian.Tests.Execution;

public sealed partial class PaperFinancingPostingTests
{
    [Fact]
    public async Task SnapshotEntryCannotBeMadeDurable_KeepsWalRecordsAndReplaysAfterRestart()
    {
        const int eio = 5;
        var root = TempRoot();
        var target = new RetainedTarget();
        var second = Charge() with
        {
            AccrualDate = new DateOnly(2026, 11, 13),
            SourceReference = "broker-interest-20261113"
        };
        var failing = false;
        // Fail only the snapshot's directory: the WAL's own segment entries stay healthy.
        AtomicFileWriter.DirectorySyncFault.Value = directory =>
            failing && !directory.EndsWith("wal", StringComparison.Ordinal) ? eio : null;

        string walDirectory;
        await using (var service = CreatePostingService(root, Scope, target))
        {
            (await service.PostAsync(Charge())).Should().BeTrue();
            walDirectory = Directory.GetDirectories(root, "wal", SearchOption.AllDirectories).Single();

            failing = true;
            await service.Invoking(s => s.PostAsync(second)).Should().ThrowAsync<IOException>();
        }
        failing = false;

        await using (var reader = new WriteAheadLog(walDirectory, new WalOptions { ArchiveAfterTruncate = false }))
        {
            var retained = 0;
            await foreach (var _ in reader.GetUncommittedRecordsAsync())
                retained++;
            retained.Should().BeGreaterThan(0,
                "the WAL must not be truncated past a snapshot whose rename may not be durable");
        }

        await using var recovered = CreatePostingService(root, Scope, target);
        (await recovered.LoadAndRecoverAsync("paper-1")).Should().Contain(second);
    }
}
