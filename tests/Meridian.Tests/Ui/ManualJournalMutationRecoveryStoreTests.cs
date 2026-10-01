using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class ManualJournalMutationRecoveryStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "manual-receipt-retention-" + Guid.NewGuid().ToString("N"));
    private readonly RetentionClock _clock = new();
    private static readonly ManualJournalMutationRecoveryOptions ArchiveAll = new() { MaxCompletedCount = 0 };
    private FileManualJournalMutationRecoveryStore Store(ManualJournalMutationRecoveryOptions? options = null)
        => new(_root, options, _clock);
    private string Completed(string key) => Path.Combine(_root, "completed", key + ".json");
    private string Archived(string key) => Path.Combine(_root, "archive", key[..2], key[2..4], key + ".json.gz");

    private static ManualJournalMutationIntent Intent(int number)
        => new(number.ToString("x64"), "request-" + number, "scope", Guid.NewGuid(), [], [], [], null,
            JsonSerializer.SerializeToElement(new { Value = number }));

    private async Task<ManualJournalMutationIntent> CompleteAsync(int number)
    {
        var intent = Intent(number);
        await using var session = await Store().OpenSessionAsync();
        await session.RetainAsync(intent, CancellationToken.None);
        await session.CompleteAsync(intent, CancellationToken.None);
        return intent;
    }

    [Fact]
    public async Task Completion_ReusesVerifiedActiveReceiptsUnderItsLease()
    {
        var first = await CompleteAsync(1);
        await using var session = await Store().OpenSessionAsync();
        // A second completion must not reopen every previously verified active receipt.
        using var held = new FileStream(Completed(first.CommandKey), FileMode.Open, FileAccess.Read, FileShare.None);
        var next = first with { CommandKey = new string('b', 64) };
        await session.RetainAsync(next, CancellationToken.None);
        await session.CompleteAsync(next, CancellationToken.None);
        (await session.GetAsync(next.CommandKey, CancellationToken.None))!.Completed.Should().BeTrue();
    }

    [Fact]
    public async Task AgeBoundary_ArchivesOnlyAfterThirtyDays_AndReplaysAfterRestart()
    {
        var intent = await CompleteAsync(1);
        var bytes = await File.ReadAllBytesAsync(Completed(intent.CommandKey));
        _clock.Now += TimeSpan.FromDays(30);
        await using (var session = await Store().OpenSessionAsync())
            File.Exists(Completed(intent.CommandKey)).Should().BeTrue();
        _clock.Now += TimeSpan.FromTicks(1);
        await using (var session = await Store().OpenSessionAsync())
            (await session.GetAsync(intent.CommandKey, CancellationToken.None))!.Result.GetProperty("Value").GetInt32().Should().Be(1);
        File.Exists(Completed(intent.CommandKey)).Should().BeFalse();
        (await ReadGzipAsync(Archived(intent.CommandKey))).Should().Equal(bytes);
    }

    [Fact]
    public async Task CountLimit_ArchivesOldest_AndPreservesEveryReceipt()
    {
        var first = await CompleteAsync(1);
        _clock.Now += TimeSpan.FromMinutes(1);
        var second = await CompleteAsync(2);
        await using var session = await Store(new() { MaxCompletedCount = 1 }).OpenSessionAsync();
        File.Exists(Archived(first.CommandKey)).Should().BeTrue();
        File.Exists(Completed(second.CommandKey)).Should().BeTrue();
        (await session.GetAsync(first.CommandKey, CancellationToken.None))!.Completed.Should().BeTrue();
        (await session.GetAsync(second.CommandKey, CancellationToken.None))!.Completed.Should().BeTrue();
    }

    [Fact]
    public async Task ByteLimit_RespectsExactBoundary()
    {
        var intent = await CompleteAsync(1);
        var size = new FileInfo(Completed(intent.CommandKey)).Length;
        await using (var session = await Store(new() { MaxCompletedBytes = size }).OpenSessionAsync())
            File.Exists(Completed(intent.CommandKey)).Should().BeTrue();
        await using (var session = await Store(new() { MaxCompletedBytes = size - 1 }).OpenSessionAsync())
            File.Exists(Archived(intent.CommandKey)).Should().BeTrue();
    }

    [Fact]
    public async Task Completion_EnforcesLimitWithoutAnotherSession()
    {
        await using var session = await Store(new() { MaxCompletedCount = 2 }).OpenSessionAsync();
        for (var index = 0; index < 12; index++)
        {
            var intent = Intent(index);
            await session.RetainAsync(intent, CancellationToken.None);
            await session.CompleteAsync(intent, CancellationToken.None);
            Directory.GetFiles(Path.Combine(_root, "completed")).Length.Should().BeLessThanOrEqualTo(2);
        }
        Directory.GetFiles(Path.Combine(_root, "archive"), "*.gz", SearchOption.AllDirectories).Should().HaveCount(10);
    }

    [Fact]
    public async Task LegacyReceipt_UsesRetainedAuditTime()
    {
        var intent = await CompleteAsync(1);
        var path = Completed(intent.CommandKey);
        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        var payload = JsonNode.Parse(envelope["Json"]!.GetValue<string>())!;
        payload.AsObject().Remove("CompletedAtUtc");
        payload["AuditEvents"] = JsonSerializer.SerializeToNode(new[]
        {
            new Meridian.Contracts.Ledger.AccountingActionAuditEventDto(Guid.NewGuid(), _clock.Now,
                "operator", "manual-je.save-draft", "fund", Guid.NewGuid(), "correlation", "before", "after", [], [])
        });
        await File.WriteAllBytesAsync(path, Envelope(payload));
        _clock.Now += TimeSpan.FromDays(31);
        await using var session = await Store().OpenSessionAsync();
        (await session.GetAsync(intent.CommandKey, CancellationToken.None))!.CompletedAtUtc.Should().BeNull();
        File.Exists(Archived(intent.CommandKey)).Should().BeTrue();
    }

    [Fact]
    public async Task InterruptedArchive_IdenticalCopiesConvergeWithoutLosingBytes()
    {
        var intent = await CompleteAsync(1);
        var bytes = await File.ReadAllBytesAsync(Completed(intent.CommandKey));
        await WriteGzipAsync(Archived(intent.CommandKey), bytes);
        await using var session = await Store().OpenSessionAsync();
        File.Exists(Completed(intent.CommandKey)).Should().BeFalse();
        (await ReadGzipAsync(Archived(intent.CommandKey))).Should().Equal(bytes);
        (await session.GetAsync(intent.CommandKey, CancellationToken.None))!.Completed.Should().BeTrue();
    }

    [Fact]
    public async Task ConflictingValidCopies_FailClosedAndReleaseLease()
    {
        var intent = await CompleteAsync(1);
        var original = await File.ReadAllBytesAsync(Completed(intent.CommandKey));
        var payload = JsonNode.Parse(JsonNode.Parse(original)!["Json"]!.GetValue<string>())!;
        payload["RequestHash"] = "different-request";
        await WriteGzipAsync(Archived(intent.CommandKey), Envelope(payload));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store().OpenSessionAsync());
        (await File.ReadAllBytesAsync(Completed(intent.CommandKey))).Should().Equal(original);
        await WriteGzipAsync(Archived(intent.CommandKey), original);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var session = await Store().OpenSessionAsync(timeout.Token);
        (await session.GetAsync(intent.CommandKey, timeout.Token)).Should().NotBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CorruptArchive_IsNotTreatedAsMissing(bool invalidCompression)
    {
        var intent = await CompleteAsync(1);
        await using (var session = await Store(ArchiveAll).OpenSessionAsync())
        { }
        if (invalidCompression)
            await File.WriteAllTextAsync(Archived(intent.CommandKey), "corrupt");
        else
            await WriteGzipAsync(Archived(intent.CommandKey), Encoding.UTF8.GetBytes("{\"Digest\":\"wrong\",\"Json\":\"{}\"}"));
        await using var restarted = await Store().OpenSessionAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => restarted.GetAsync(intent.CommandKey, CancellationToken.None));
        File.Exists(Archived(intent.CommandKey)).Should().BeTrue();
    }

    [Fact]
    public async Task PendingCompletionSurvivesMaintenanceAndUsesOriginalCompletionTime()
    {
        var intent = await CompleteAsync(1);
        var completedBytes = await File.ReadAllBytesAsync(Completed(intent.CommandKey));
        var completedAt = _clock.Now;
        await File.WriteAllBytesAsync(Path.Combine(_root, "pending", intent.CommandKey + ".json"), Envelope(JsonSerializer.SerializeToNode(intent)!));
        _clock.Now += TimeSpan.FromDays(31);
        await using var session = await Store(ArchiveAll).OpenSessionAsync();
        (await session.ListPendingAsync(CancellationToken.None)).Should().ContainSingle();
        File.Exists(Completed(intent.CommandKey)).Should().BeFalse();
        (await ReadGzipAsync(Archived(intent.CommandKey))).Should().Equal(completedBytes);
        await session.CompleteAsync(intent, CancellationToken.None);
        (await session.GetAsync(intent.CommandKey, CancellationToken.None))!.CompletedAtUtc.Should().Be(completedAt);
        (await ReadGzipAsync(Archived(intent.CommandKey))).Should().Equal(completedBytes);
    }

    [Fact]
    public async Task ArchiveFailure_PreservesCompletedReceiptAndAllowsRetry()
    {
        var intent = await CompleteAsync(1);
        await File.WriteAllTextAsync(Path.Combine(_root, "archive"), "blocked directory");
        await Assert.ThrowsAnyAsync<IOException>(() => Store(ArchiveAll).OpenSessionAsync());
        File.Exists(Completed(intent.CommandKey)).Should().BeTrue();
        File.Delete(Path.Combine(_root, "archive"));
        await using var session = await Store(ArchiveAll).OpenSessionAsync();
        (await session.GetAsync(intent.CommandKey, CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task Maintenance_UsesSharedLeaseAndHonorsCancellation()
    {
        var intent = await CompleteAsync(1);
        await using (var held = await Store().OpenSessionAsync())
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store(ArchiveAll).OpenSessionAsync(cancellation.Token));
            File.Exists(Completed(intent.CommandKey)).Should().BeTrue();
        }
        await using var acquired = await Store(ArchiveAll).OpenSessionAsync();
        File.Exists(Archived(intent.CommandKey)).Should().BeTrue();
    }

    [Fact]
    public async Task CancellationDuringMaintenance_PreservesActiveCopyAndReleasesLease()
    {
        var intent = await CompleteAsync(1);
        var original = await File.ReadAllBytesAsync(Completed(intent.CommandKey));
        using var cancellation = new CancellationTokenSource();
        _clock.OnRead = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store(ArchiveAll).OpenSessionAsync(cancellation.Token));
        (await File.ReadAllBytesAsync(Completed(intent.CommandKey))).Should().Equal(original);
        _clock.OnRead = null;
        await using var session = await Store(ArchiveAll).OpenSessionAsync();
        (await ReadGzipAsync(Archived(intent.CommandKey))).Should().Equal(original);
    }

    private static byte[] Envelope(JsonNode payload)
    {
        var json = payload.ToJsonString();
        return JsonSerializer.SerializeToUtf8Bytes(new { Digest = Sha256Digest.ComputeUtf8(json), Json = json });
    }

    private static async Task WriteGzipAsync(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        await gzip.WriteAsync(bytes);
    }

    private static async Task<byte[]> ReadGzipAsync(string path)
    {
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var result = new MemoryStream();
        await gzip.CopyToAsync(result);
        return result.ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class RetentionClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
        public Action? OnRead { get; set; }
        public override DateTimeOffset GetUtcNow()
        {
            OnRead?.Invoke();
            return Now;
        }
    }
}
