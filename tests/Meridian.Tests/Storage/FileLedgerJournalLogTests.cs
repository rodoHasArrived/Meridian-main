using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Xunit;
using LedgerBook = Meridian.Ledger.Ledger;

namespace Meridian.Tests.Storage;

/// <summary>
/// The durable run journal is append-only and hash-chained: books survive a restart, and any edit,
/// reorder, removal or truncation of stored records is reported and fails a reload closed.
/// </summary>
public sealed class FileLedgerJournalLogTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 5, 14, 30, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-ledger-journal-" + Guid.NewGuid().ToString("N"));
    private readonly IChartOfAccounts _chart = StandardChartOfAccounts.Instance;

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Append_ThenReopen_ReloadsTheSameBooksAndVerifiesIntact()
    {
        var factory = new FileLedgerJournalLogFactory(_root);
        var source = new LedgerBook(LedgerPolicy.Trading());
        await using (var stream = LedgerJournalStream.Attach(source, factory.Open("run-1", source.Policy)))
        {
            PostTradingDay(source);
            await stream.CompleteAsync();
        }

        var verification = await factory.VerifyAsync("run-1");
        var reloaded = await factory.LoadAsync("run-1");

        verification.Should().NotBeNull();
        verification!.IsIntact.Should().BeTrue();
        verification.EntryCount.Should().Be(3);
        verification.FunctionalCurrency.Should().Be("USD");
        reloaded.Should().NotBeNull();
        reloaded!.Journal.Select(entry => entry.JournalEntryId)
            .Should().Equal(source.Journal.Select(entry => entry.JournalEntryId));
        reloaded.TrialBalance().Should().BeEquivalentTo(source.TrialBalance());
        reloaded.GetBalance(_chart.Resolve(LedgerAccountRole.Cash, "broker-1"))
            .Should().Be(100_000m - 19_000m - 1m);
    }

    [Fact]
    public async Task Reopen_ForAppend_ContinuesTheSameChain()
    {
        var directory = Path.Combine(_root, "continue");
        var policy = LedgerPolicy.Trading();
        string firstHead;
        await using (var log = new FileLedgerJournalLog(directory, "run-2", policy))
        {
            await log.AppendAsync(Capital(100m));
            firstHead = (await log.VerifyAsync()).HeadHash;
        }

        await using (var log = new FileLedgerJournalLog(directory, "run-2", policy))
        {
            await log.AppendAsync(Capital(50m, Start.AddDays(1)));
            var verification = await log.VerifyAsync();

            verification.IsIntact.Should().BeTrue();
            verification.EntryCount.Should().Be(2);
            verification.HeadHash.Should().NotBe(firstHead);
            (await log.LoadLedgerAsync()).GetBalance(_chart.Resolve(LedgerAccountRole.Cash)).Should().Be(150m);
        }
    }

    [Fact]
    public async Task Append_SameEntryTwice_IsIdempotent_ButChangedContentUnderTheSameIdIsRejected()
    {
        var entry = Capital(100m);
        await using var log = new FileLedgerJournalLog(Path.Combine(_root, "idempotent"), "run-3", LedgerPolicy.Trading());

        await log.AppendAsync(entry);
        await log.AppendAsync(entry);
        var conflicting = new JournalEntry(entry.JournalEntryId, entry.Timestamp, "Restated capital",
            entry.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp, line.Account,
                line.Debit, line.Credit, "Restated capital", line.Dimensions, line.Currency)).ToArray());
        var rewrite = () => log.AppendAsync(conflicting);

        await rewrite.Should().ThrowAsync<InvalidDataException>().WithMessage("*different content*cannot be changed*");
        (await log.VerifyAsync()).EntryCount.Should().Be(1);
    }

    [Fact]
    public async Task Append_EntryThatBreaksThePolicy_NeverReachesDisk()
    {
        var directory = Path.Combine(_root, "policy");
        await using (var log = new FileLedgerJournalLog(directory, "run-4", LedgerPolicy.Trading()))
        {
            var journalId = Guid.NewGuid();
            var unknownAccount = new JournalEntry(journalId, Start, "Unregistered", [
                new LedgerEntry(Guid.NewGuid(), journalId, Start, new LedgerAccount("Plug", LedgerAccountType.Asset), 5m, 0m,
                    "Unregistered", currency: new LedgerEntryCurrency("USD", "USD", 5m, 0m, 1m)),
                new LedgerEntry(Guid.NewGuid(), journalId, Start, _chart.Resolve(LedgerAccountRole.Cash), 0m, 5m,
                    "Unregistered", currency: new LedgerEntryCurrency("USD", "USD", 0m, 5m, 1m))
            ]);

            var append = () => log.AppendAsync(unknownAccount);

            await append.Should().ThrowAsync<LedgerValidationException>().WithMessage("*not registered*");
        }

        var verification = await FileLedgerJournalLog.VerifyDirectoryAsync(directory, "run-4", _chart);
        verification!.IsIntact.Should().BeTrue();
        verification.EntryCount.Should().Be(0);
    }

    [Fact]
    public async Task Reopen_UnderADifferentFunctionalCurrency_IsRefused()
    {
        var directory = Path.Combine(_root, "currency");
        await using (var log = new FileLedgerJournalLog(directory, "run-5", LedgerPolicy.Trading("USD")))
            await log.AppendAsync(Capital(10m));

        await using var reopened = new FileLedgerJournalLog(directory, "run-5", LedgerPolicy.Trading("EUR"));
        var load = () => reopened.LoadLedgerAsync();

        await load.Should().ThrowAsync<InvalidDataException>().WithMessage("*functional currency 'USD'*'EUR'*");
    }

    [Fact]
    public async Task EditedEntry_WithRecomputedWalChecksum_IsStillDetectedByTheHashChain()
    {
        var directory = await WriteJournalAsync("tamper", "run-6", entries: 3);
        // Change an amount inside record 2's stored JSON and re-seal the WAL line with a valid checksum,
        // as an editor with the WAL source would. Only the hash chain can catch this.
        RewriteWalRecord(directory, sequenceOfEntry: 2, payload => payload.Replace("19000", "19500", StringComparison.Ordinal));

        var verification = await FileLedgerJournalLog.VerifyDirectoryAsync(directory, "run-6", _chart);

        verification!.IsIntact.Should().BeFalse();
        verification.FirstBreakSequence.Should().Be(2);
        verification.EntryCount.Should().Be(1);
        verification.Reason.Should().Contain("record 2");
    }

    [Fact]
    public async Task CorruptedWalBytes_AreReportedAsABreak_AndReloadFailsClosed()
    {
        var directory = await WriteJournalAsync("bytes", "run-7", entries: 2);
        var wal = Directory.GetFiles(directory, "*.wal").Single(file => new FileInfo(file).Length > 200);
        var text = File.ReadAllText(wal);
        File.WriteAllText(wal, text.Replace("Capital", "Kapital", StringComparison.Ordinal));

        var verification = await FileLedgerJournalLog.VerifyDirectoryAsync(directory, "run-7", _chart);
        await using var reader = FileLedgerJournalLog.OpenReadOnly(directory, "run-7", _chart);
        var load = () => reader!.LoadLedgerAsync();

        verification!.IsIntact.Should().BeFalse();
        await load.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task RecordsCutOffTheEnd_AreDetectedAgainstTheChainHead()
    {
        var directory = await WriteJournalAsync("truncate", "run-8", entries: 3);
        RemoveWalRecords(directory, keepEntries: 1);

        var verification = await FileLedgerJournalLog.VerifyDirectoryAsync(directory, "run-8", _chart);

        verification!.IsIntact.Should().BeFalse();
        verification.Reason.Should().Contain("records were removed");
    }

    [Fact]
    public async Task ReorderedRecords_BreakTheChain()
    {
        var directory = await WriteJournalAsync("reorder", "run-9", entries: 3);
        SwapWalRecords(directory, firstEntry: 2, secondEntry: 3);

        var verification = await FileLedgerJournalLog.VerifyDirectoryAsync(directory, "run-9", _chart);

        verification!.IsIntact.Should().BeFalse();
        verification.FirstBreakSequence.Should().Be(2);
    }

    [Fact]
    public async Task CrashBeforeTheChainHeadIsReplaced_RecoversTheDurableRecord()
    {
        var directory = await WriteJournalAsync("crash", "run-10", entries: 2);
        // Simulate a crash after record 3 was fsynced but before its chain head was replaced.
        var headPath = Path.Combine(directory, "chain-head.json");
        var headAfterTwo = File.ReadAllText(headPath);
        await using (var log = new FileLedgerJournalLog(directory, "run-10", LedgerPolicy.Trading()))
            await log.AppendAsync(Capital(7m, Start.AddDays(3)));
        File.WriteAllText(headPath, headAfterTwo);

        await using var reopened = new FileLedgerJournalLog(directory, "run-10", LedgerPolicy.Trading());
        var verification = await reopened.VerifyAsync();

        verification.IsIntact.Should().BeTrue();
        verification.EntryCount.Should().Be(3);
    }

    [Fact]
    public async Task ReadOnlyVerification_DoesNotWriteToTheJournal()
    {
        var directory = await WriteJournalAsync("readonly", "run-11", entries: 2);
        var before = Directory.GetFiles(directory).Select(file => (file, File.ReadAllBytes(file))).ToArray();

        _ = await FileLedgerJournalLog.VerifyDirectoryAsync(directory, "run-11", _chart);

        Directory.GetFiles(directory).Should().BeEquivalentTo(before.Select(item => item.file));
        foreach (var (file, bytes) in before)
            File.ReadAllBytes(file).Should().Equal(bytes);
    }

    [Fact]
    public async Task MissingJournal_VerifiesAsNull()
    {
        var factory = new FileLedgerJournalLogFactory(_root);

        (await factory.VerifyAsync("never-written")).Should().BeNull();
        (await factory.LoadAsync("never-written")).Should().BeNull();
    }

    [Fact]
    public async Task Stream_WriteFailure_FailsCompletionAndLaterPosts()
    {
        var ledger = new LedgerBook(LedgerPolicy.Trading());
        var stream = LedgerJournalStream.Attach(ledger, new FailingLog());
        ledger.PostLines(Start, "Capital", [
            (_chart.Resolve(LedgerAccountRole.Cash), 1m, 0m),
            (_chart.Resolve(LedgerAccountRole.Capital), 0m, 1m)
        ]);

        var complete = () => stream.CompleteAsync();

        await complete.Should().ThrowAsync<IOException>().WithMessage("disk full");
        var postAfterFailure = () => ledger.PostLines(Start.AddDays(1), "More capital", [
            (_chart.Resolve(LedgerAccountRole.Cash), 1m, 0m),
            (_chart.Resolve(LedgerAccountRole.Capital), 0m, 1m)
        ]);
        postAfterFailure.Should().Throw<Exception>();
        await stream.DisposeAsync();
    }

    [Fact]
    public void Stream_MustAttachBeforeTheFirstPosting()
    {
        var ledger = new LedgerBook(LedgerPolicy.Trading());
        ledger.PostLines(Start, "Capital", [
            (_chart.Resolve(LedgerAccountRole.Cash), 1m, 0m),
            (_chart.Resolve(LedgerAccountRole.Capital), 0m, 1m)
        ]);

        var attach = () => LedgerJournalStream.Attach(ledger, new FailingLog());

        attach.Should().Throw<InvalidOperationException>().WithMessage("*before the first posting*");
    }

    private async Task<string> WriteJournalAsync(string name, string scopeId, int entries)
    {
        var directory = Path.Combine(_root, name);
        await using var log = new FileLedgerJournalLog(directory, scopeId, LedgerPolicy.Trading());
        var ledger = new LedgerBook(LedgerPolicy.Trading());
        PostTradingDay(ledger);
        foreach (var entry in ledger.Journal.Take(entries))
            await log.AppendAsync(entry);
        return directory;
    }

    private void PostTradingDay(LedgerBook ledger)
    {
        var cash = _chart.Resolve(LedgerAccountRole.Cash, "broker-1");
        ledger.PostLines(Start, "Initial capital", [
            (cash, 100_000m, 0m),
            (_chart.Resolve(LedgerAccountRole.Capital, "broker-1"), 0m, 100_000m)
        ]);
        ledger.PostLines(Start.AddHours(1), "Buy 100 AAPL @ 190", [
            (_chart.Resolve(LedgerAccountRole.Securities, "broker-1", "AAPL"), 19000m, 0m),
            (cash, 0m, 19000m)
        ]);
        ledger.PostLines(Start.AddHours(1), "Commission AAPL", [
            (_chart.Resolve(LedgerAccountRole.CommissionExpense, "broker-1"), 1m, 0m),
            (cash, 0m, 1m)
        ]);
    }

    private JournalEntry Capital(decimal amount, DateTimeOffset? at = null)
    {
        var timestamp = at ?? Start;
        var journalId = Guid.NewGuid();
        return new JournalEntry(journalId, timestamp, "Capital", [
            new LedgerEntry(Guid.NewGuid(), journalId, timestamp, _chart.Resolve(LedgerAccountRole.Cash), amount, 0m,
                "Capital", currency: new LedgerEntryCurrency("USD", "USD", amount, 0m, 1m)),
            new LedgerEntry(Guid.NewGuid(), journalId, timestamp, _chart.Resolve(LedgerAccountRole.Capital), 0m, amount,
                "Capital", currency: new LedgerEntryCurrency("USD", "USD", 0m, amount, 1m))
        ]);
    }

    // WAL lines are "Sequence|Timestamp|RecordType|Checksum|Payload"; record 1 is the journal header,
    // so journal entry N is the (N + 1)th data line across the segment files.
    private static List<(string File, int Line)> EntryLines(string directory)
    {
        var lines = new List<(string, int)>();
        foreach (var file in Directory.GetFiles(directory, "*.wal").OrderBy(file => file, StringComparer.Ordinal))
        {
            var content = File.ReadAllLines(file);
            for (var i = 0; i < content.Length; i++)
            {
                if (content[i].Contains("|LedgerJournalEntryV1|", StringComparison.Ordinal))
                    lines.Add((file, i));
            }
        }

        return lines;
    }

    private static void RewriteWalRecord(string directory, int sequenceOfEntry, Func<string, string> edit)
    {
        var (file, index) = EntryLines(directory)[sequenceOfEntry - 1];
        var lines = File.ReadAllLines(file);
        var parts = lines[index].Split('|', 5);
        var sequence = long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        var timestamp = DateTime.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        var payload = edit(parts[4]);
        parts[3] = Meridian.Storage.Archival.WalChecksum.Compute(sequence, timestamp, parts[2], payload);
        parts[4] = payload;
        lines[index] = string.Join('|', parts);
        File.WriteAllLines(file, lines);
    }

    private static void RemoveWalRecords(string directory, int keepEntries)
    {
        foreach (var (file, index) in EntryLines(directory).Skip(keepEntries).Reverse())
        {
            var lines = File.ReadAllLines(file).ToList();
            lines.RemoveAt(index);
            File.WriteAllLines(file, lines);
        }
    }

    private static void SwapWalRecords(string directory, int firstEntry, int secondEntry)
    {
        var entries = EntryLines(directory);
        var (firstFile, firstIndex) = entries[firstEntry - 1];
        var (secondFile, secondIndex) = entries[secondEntry - 1];
        firstFile.Should().Be(secondFile, "the test journal fits in one WAL segment");
        var lines = File.ReadAllLines(firstFile);
        (lines[firstIndex], lines[secondIndex]) = (lines[secondIndex], lines[firstIndex]);
        File.WriteAllLines(firstFile, lines);
    }

    private sealed class FailingLog : ILedgerJournalLog
    {
        public string ScopeId => "failing";
        public Task AppendAsync(JournalEntry entry, CancellationToken ct = default) => throw new IOException("disk full");
        public Task<LedgerBook> LoadLedgerAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LedgerJournalChainVerification> VerifyAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
