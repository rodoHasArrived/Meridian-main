using System.Data;
using FluentAssertions;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CouponHistory_DividendProducerCoexistsWithRetainedBookReader_AndStillBlocksDirectBookMutation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var ct = timeout.Token;
        await using var fixture = await CouponSinkFixture.CreateAsync();
        await using var readerConnection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await readerConnection.OpenAsync(ct);
        await using var reader = await readerConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await using (var read = readerConnection.CreateCommand())
        {
            read.Transaction = reader;
            read.CommandText = $"select base_currency from \"{fixture.Options.SchemaName}\".ledger_books where ledger_book_id=@book for share";
            read.Parameters.AddWithValue("book", fixture.BookId);
            (await read.ExecuteScalarAsync(ct)).Should().Be("USD");
        }
        await using var producerConnection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await producerConnection.OpenAsync(ct);
        await using var producer = await producerConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await fixture.Store.LockCashDividendBookAsync(producerConnection, producer, fixture.BookId, ct)
            .WaitAsync(TimeSpan.FromSeconds(5), ct);
        // The native producer must complete while the existing caller-owned book reader remains
        // held; FOR UPDATE would invert its subsequent journal boundary and deadlock publication.
        await reader.RollbackAsync(ct);
        var application = "dividend-direct-book-update-" + Guid.NewGuid().ToString("N");
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.Options.ConnectionString)
        { ApplicationName = application }.ConnectionString;
        await using var updateConnection = new NpgsqlConnection(connectionString);
        await updateConnection.OpenAsync(ct);
        await using var updater = await updateConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await using var update = updateConnection.CreateCommand();
        update.Transaction = updater;
        update.CommandText = $"update \"{fixture.Options.SchemaName}\".ledger_books set display_name=display_name where ledger_book_id=@book";
        update.Parameters.AddWithValue("book", fixture.BookId);
        var pending = update.ExecuteNonQueryAsync(ct);
        try
        {
            await using var observer = new NpgsqlConnection(fixture.Options.ConnectionString);
            await observer.OpenAsync(ct);
            while (true)
            {
                await using var waiting = observer.CreateCommand();
                waiting.CommandText = "select exists(select 1 from pg_catalog.pg_stat_activity where application_name=@application and wait_event_type='Lock')";
                waiting.Parameters.AddWithValue("application", application);
                if (await waiting.ExecuteScalarAsync(ct) is true)
                    break;
                pending.IsCompleted.Should().BeFalse("the producer must continue freezing direct book mutation after the other reader releases");
                await Task.Delay(25, ct);
            }
            pending.IsCompleted.Should().BeFalse();
            await producer.RollbackAsync(ct);
            (await pending).Should().Be(1);
            await updater.RollbackAsync(ct);
        }
        finally
        {
            if (!pending.IsCompleted)
            {
                await timeout.CancelAsync();
                try
                { await pending; }
                catch (OperationCanceledException) { }
            }
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task CouponHistory_DividendProducerWaitsBeforeOrdinaryAppendAndNestedBookReader()
        => AssertDividendProducerHistoryOrderAsync(typedCoupon: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task CouponHistory_DividendProducerWaitsBeforeTypedCouponAndNestedBookReader()
        => AssertDividendProducerHistoryOrderAsync(typedCoupon: true);

    private static async Task AssertDividendProducerHistoryOrderAsync(bool typedCoupon)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var ct = timeout.Token;
        await using var fixture = await CouponSinkFixture.CreateAsync();
        await using var appendConnection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await appendConnection.OpenAsync(ct);
        await using var append = await appendConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var write = typedCoupon ? fixture.Write(fixture.Instruction(0, "dividend-boundary-coupon", 45m, 50m, 3m, 2m))
            : CouponHistoryWrite(fixture.Period.PeriodId, DateTimeOffset.Parse("2026-05-12T12:00:00Z"), fixture.BookId);
        await fixture.Store.AppendAsync(appendConnection, append, write, ct);

        var application = "dividend-coupon-boundary-" + Guid.NewGuid().ToString("N");
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.Options.ConnectionString)
        { ApplicationName = application }.ConnectionString;
        await using var dividendConnection = new NpgsqlConnection(connectionString);
        await dividendConnection.OpenAsync(ct);
        await using var dividend = await dividendConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var pending = fixture.Store.LockCashDividendBookAsync(dividendConnection, dividend, fixture.BookId, ct);
        try
        {
            // Wait for the real producer at its table gate, rather than allowing it to enqueue
            // exclusive dividend authority behind this transaction's shared book authority.
            await WaitForCouponHistoryRelationLockAsync(fixture.Options.ConnectionString, application, ct);
            pending.IsCompleted.Should().BeFalse();
            var birth = fixture.Lots[0].OriginatingMutationBatchId!.Value;
            var nested = await fixture.Store.GetAtomicTaxLotPostingAsync(birth, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
            nested.Should().NotBeNull("canonical separate-connection readers must complete while the producer waits");
            pending.IsCompleted.Should().BeFalse();
            await append.CommitAsync(ct);
            await pending;
            await dividend.RollbackAsync(ct);
            (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId, ct)).Should()
                .Contain(row => row.Entry.JournalEntryId == write.Entry.JournalEntryId);
        }
        finally
        {
            if (!pending.IsCompleted)
            {
                await timeout.CancelAsync();
                try
                { await pending; }
                catch (OperationCanceledException) { }
            }
        }
    }
}
