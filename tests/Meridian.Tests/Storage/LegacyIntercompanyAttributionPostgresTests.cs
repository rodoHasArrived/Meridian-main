using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

/// <summary>
/// An entity book carries an intercompany receivable posted before agreement attribution existed.
/// A reviewer attributes that exact retained line to the approved arrangement without rewriting the
/// journal. Guards against an attribution being retained for a different or altered source line,
/// history being rewritten instead of superseded, a retried request creating a second assertion,
/// and another tenant or book reading or extending the evidence.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LegacyIntercompanyAttributionPostgresTests
{
    [LedgerDatabaseFact]
    public async Task ReviewedAttribution_IsRetainedAgainstTheExactSourceLineDigest_WithoutChangingThePostedJournal()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = timeout.Token;
        await using var fixture = await LegacyIntercompanyControlsFixture.CreateAsync(ct);
        var (record, line) = await fixture.PostUnattributedReceivableAsync(ct);
        var fact = fixture.Attribution(record, line);

        var saved = await fixture.Attributions.AppendAsync(fact, null, ct);

        saved.Should().Be(fact);
        saved.SourceLineDigest.Should().Be(LegacyIntercompanyAttributionRules.SourceDigest(fixture.Book, record, line));
        // A new store instance over the same schema stands for a process restart.
        var restarted = fixture.AttributionStoreFor(LegacyIntercompanyControlsFixture.Tenant);
        (await restarted.ListAsync(fixture.Book.LedgerBookId, ct)).Should().ContainSingle().Which.Should().Be(fact);
        (await restarted.HistoryAsync(fixture.Book.LedgerBookId, ct)).Should().ContainSingle().Which.Should().Be(fact);
        await using (var connection = new NpgsqlConnection(fixture.Database.Options.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand($"""
                select attribution_id, revision, ledger_book_id, journal_entry_id, line_id, tenant_id, company_id
                from "{fixture.Database.Options.SchemaName}".legacy_intercompany_attributions;
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(ct);
            (await reader.ReadAsync(ct)).Should().BeTrue();
            reader.GetGuid(0).Should().Be(fact.AttributionId);
            reader.GetInt64(1).Should().Be(1);
            reader.GetGuid(2).Should().Be(fixture.Book.LedgerBookId);
            reader.GetGuid(3).Should().Be(record.Entry.JournalEntryId);
            reader.GetGuid(4).Should().Be(line.EntryId);
            reader.GetString(5).Should().Be(LegacyIntercompanyControlsFixture.Tenant);
            reader.GetString(6).Should().Be(LegacyIntercompanyControlsFixture.Company);
            (await reader.ReadAsync(ct)).Should().BeFalse();
        }
        // The attribution is sidecar evidence: the immutable source line still has no agreement identity.
        var retained = (await fixture.ReadJournalsAsync(ct)).Should().ContainSingle().Subject;
        retained.GlobalSequence.Should().Be(record.GlobalSequence);
        retained.Entry.Lines.Should().OnlyContain(item => item.Dimensions == null || item.Dimensions.AccountingArrangementId == null);
        LegacyIntercompanyAttributionRules.SourceDigest(fixture.Book, retained, retained.Entry.Lines.Single(item => item.EntryId == line.EntryId))
            .Should().Be(fact.SourceLineDigest);
    }

    [LedgerDatabaseFact]
    public async Task ExactRequestReplay_ReturnsTheRetainedAttributionWithoutASecondRow_AndAReusedIdentityIsRefused()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = timeout.Token;
        await using var fixture = await LegacyIntercompanyControlsFixture.CreateAsync(ct);
        var (record, line) = await fixture.PostUnattributedReceivableAsync(ct);
        var fact = fixture.Attribution(record, line);
        var saved = await fixture.Attributions.AppendAsync(fact, null, ct);

        var replayed = await fixture.Attributions.AppendAsync(fact, null, ct);
        var replayedAfterRestart = await fixture.AttributionStoreFor(LegacyIntercompanyControlsFixture.Tenant).AppendAsync(fact, null, ct);

        replayed.Should().Be(saved);
        replayedAfterRestart.Should().Be(saved);
        (await fixture.CountAsync("legacy_intercompany_attributions", ct)).Should().Be(1);

        var differentAssertion = () => fixture.Attributions.AppendAsync(fact with { IntercompanyTransactionId = "another-transaction" }, null, ct);
        await differentAssertion.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already used for a different reviewed assertion*");
        var differentPredecessor = () => fixture.Attributions.AppendAsync(fact, Guid.NewGuid(), ct);
        await differentPredecessor.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already used for a different reviewed assertion*");
        (await fixture.Attributions.HistoryAsync(fixture.Book.LedgerBookId, ct)).Should().ContainSingle().Which.Should().Be(fact);
    }

    [LedgerDatabaseFact]
    public async Task AttributionPinnedToADifferentSourceDigest_IsRefusedAndNothingIsRetained()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = timeout.Token;
        await using var fixture = await LegacyIntercompanyControlsFixture.CreateAsync(ct);
        var (record, line) = await fixture.PostUnattributedReceivableAsync(ct);
        var fact = fixture.Attribution(record, line);
        var contraLine = record.Entry.Lines.Single(item => item.EntryId != line.EntryId);

        var arbitrary = () => fixture.Attributions.AppendAsync(fact with { SourceLineDigest = new string('a', 64) }, null, ct);
        await arbitrary.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exact immutable source digest*");
        // A genuine digest of another line in the same journal still does not describe the attributed line.
        var otherLine = () => fixture.Attributions.AppendAsync(fact with
        { SourceLineDigest = LegacyIntercompanyAttributionRules.SourceDigest(fixture.Book, record, contraLine) }, null, ct);
        await otherLine.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exact immutable source digest*");

        (await fixture.CountAsync("legacy_intercompany_attributions", ct)).Should().Be(0);
        (await fixture.Attributions.HistoryAsync(fixture.Book.LedgerBookId, ct)).Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    public async Task ReviewedSuccessor_IsANewRevisionNamingItsExactPredecessor_AndEarlierRevisionsStayRetained()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = timeout.Token;
        await using var fixture = await LegacyIntercompanyControlsFixture.CreateAsync(ct);
        var (record, line) = await fixture.PostUnattributedReceivableAsync(ct);
        var first = fixture.Attribution(record, line);
        await fixture.Attributions.AppendAsync(first, null, ct);
        var corrected = first with
        {
            AttributionId = Guid.NewGuid(),
            Revision = 2,
            PreviousAttributionId = first.AttributionId,
            IntercompanyTransactionId = "legacy-loan-drawdown-corrected"
        };

        var withoutPredecessor = () => fixture.Attributions.AppendAsync(corrected with { PreviousAttributionId = null }, null, ct);
        await withoutPredecessor.Should().ThrowAsync<InvalidOperationException>().WithMessage("*reload before appending*");
        var staleRevisionNumber = () => fixture.Attributions.AppendAsync(corrected with { Revision = 1 }, first.AttributionId, ct);
        await staleRevisionNumber.Should().ThrowAsync<InvalidOperationException>().WithMessage("*reload before appending*");
        var secondFirstRevision = () => fixture.Attributions.AppendAsync(fixture.Attribution(record, line), null, ct);
        await secondFirstRevision.Should().ThrowAsync<InvalidOperationException>().WithMessage("*reload before appending*");
        (await fixture.CountAsync("legacy_intercompany_attributions", ct)).Should().Be(1);

        (await fixture.Attributions.AppendAsync(corrected, first.AttributionId, ct)).Should().Be(corrected);

        (await fixture.Attributions.ListAsync(fixture.Book.LedgerBookId, ct)).Should().ContainSingle().Which.Should().Be(corrected);
        (await fixture.Attributions.HistoryAsync(fixture.Book.LedgerBookId, ct)).Should().Equal(first, corrected);
    }

    [LedgerDatabaseFact]
    public async Task ReviewedRevocation_IsAppendedAsANewRevision_AndTheRetractedAssertionStaysInHistory()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = timeout.Token;
        await using var fixture = await LegacyIntercompanyControlsFixture.CreateAsync(ct);
        var (record, line) = await fixture.PostUnattributedReceivableAsync(ct);
        var first = fixture.Attribution(record, line);
        await fixture.Attributions.AppendAsync(first, null, ct);
        var revoked = LegacyIntercompanyControlsFixture.Revoke(first);

        var changedPin = () => fixture.Attributions.AppendAsync(revoked with { IntercompanyTransactionId = "another-transaction" }, first.AttributionId, ct);
        await changedPin.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exact prior source, legal, configuration and transaction pins*");
        var withoutRationale = () => fixture.Attributions.AppendAsync(revoked with { ReviewRationale = null }, first.AttributionId, ct);
        await withoutRationale.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Attributions.ListAsync(fixture.Book.LedgerBookId, ct)).Should().ContainSingle().Which.Should().Be(first);

        var saved = await fixture.Attributions.AppendAsync(revoked, first.AttributionId, ct);
        var replayed = await fixture.Attributions.AppendAsync(revoked, first.AttributionId, ct);

        saved.Should().Be(revoked);
        replayed.Should().Be(revoked);
        var restarted = fixture.AttributionStoreFor(LegacyIntercompanyControlsFixture.Tenant);
        (await restarted.ListAsync(fixture.Book.LedgerBookId, ct)).Should().BeEmpty();
        (await restarted.HistoryAsync(fixture.Book.LedgerBookId, ct)).Should().Equal(first, revoked);
        (await fixture.CountAsync("legacy_intercompany_attributions", ct)).Should().Be(2);
        var revokeAgain = () => restarted.AppendAsync(LegacyIntercompanyControlsFixture.Revoke(revoked), revoked.AttributionId, ct);
        await revokeAgain.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Only the latest active attribution can be revoked*");
        (await fixture.CountAsync("legacy_intercompany_attributions", ct)).Should().Be(2);
    }

    [LedgerDatabaseFact]
    public async Task RetainedAttributionRows_CannotBeUpdatedDeletedOrTruncated()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = timeout.Token;
        await using var fixture = await LegacyIntercompanyControlsFixture.CreateAsync(ct);
        var (record, line) = await fixture.PostUnattributedReceivableAsync(ct);
        var fact = fixture.Attribution(record, line);
        await fixture.Attributions.AppendAsync(fact, null, ct);
        var table = $"\"{fixture.Database.Options.SchemaName}\".legacy_intercompany_attributions";

        var update = () => fixture.ExecuteAsync($"update {table} set payload = '{{}}'::jsonb where attribution_id = @id", fact.AttributionId, ct);
        await update.Should().ThrowAsync<PostgresException>().WithMessage("*append-only*");
        var repoint = () => fixture.ExecuteAsync($"update {table} set tenant_id = 'tenant-b' where attribution_id = @id", fact.AttributionId, ct);
        await repoint.Should().ThrowAsync<PostgresException>().WithMessage("*append-only*");
        var delete = () => fixture.ExecuteAsync($"delete from {table} where attribution_id = @id", fact.AttributionId, ct);
        await delete.Should().ThrowAsync<PostgresException>().WithMessage("*append-only*");
        var truncate = () => fixture.ExecuteAsync($"truncate table {table}", ct: ct);
        await truncate.Should().ThrowAsync<PostgresException>().WithMessage("*append-only*");

        (await fixture.Attributions.HistoryAsync(fixture.Book.LedgerBookId, ct)).Should().ContainSingle().Which.Should().Be(fact);
    }

    [LedgerDatabaseFact]
    public async Task AnotherTenantOrAnUnresolvedCaller_CanNeitherReadNorExtendTheBooksAttributions()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = timeout.Token;
        await using var fixture = await LegacyIntercompanyControlsFixture.CreateAsync(ct);
        var (record, line) = await fixture.PostUnattributedReceivableAsync(ct);
        var first = fixture.Attribution(record, line);
        await fixture.Attributions.AppendAsync(first, null, ct);
        var successor = first with
        {
            AttributionId = Guid.NewGuid(),
            Revision = 2,
            PreviousAttributionId = first.AttributionId,
            IntercompanyTransactionId = "foreign-rewrite"
        };

        foreach (var caller in new[] { "tenant-b", null })
        {
            // The source validation is the owning tenant's; only the calling tenant differs.
            var foreign = fixture.AttributionStoreFor(caller);
            var list = () => foreign.ListAsync(fixture.Book.LedgerBookId, ct);
            await list.Should().ThrowAsync<TenantScopeRejectedException>($"caller '{caller ?? "<none>"}' does not own the book");
            var history = () => foreign.HistoryAsync(fixture.Book.LedgerBookId, ct);
            await history.Should().ThrowAsync<TenantScopeRejectedException>();
            var append = () => foreign.AppendAsync(successor, first.AttributionId, ct);
            await append.Should().ThrowAsync<TenantScopeRejectedException>();
            var revoke = () => foreign.AppendAsync(LegacyIntercompanyControlsFixture.Revoke(first), first.AttributionId, ct);
            await revoke.Should().ThrowAsync<TenantScopeRejectedException>();
        }
        var unresolvedLease = () => fixture.AttributionStoreFor(null).AcquireAuthorityLeaseAsync(ct);
        await unresolvedLease.Should().ThrowAsync<TenantScopeRejectedException>();

        // The owning tenant cannot retain an assertion that claims another owner either.
        var claimsOtherTenant = () => fixture.Attributions.AppendAsync(successor with { TenantId = "tenant-b" }, first.AttributionId, ct);
        await claimsOtherTenant.Should().ThrowAsync<InvalidOperationException>();
        var claimsOtherCompany = () => fixture.Attributions.AppendAsync(successor with { CompanyId = "company-b" }, first.AttributionId, ct);
        await claimsOtherCompany.Should().ThrowAsync<InvalidOperationException>();

        (await fixture.CountAsync("legacy_intercompany_attributions", ct)).Should().Be(1);
        (await fixture.Attributions.ListAsync(fixture.Book.LedgerBookId, ct)).Should().ContainSingle().Which.Should().Be(first);
    }

    [LedgerDatabaseFact]
    public async Task AttributionBelongsToItsBook_AnotherBookOfTheSameTenantNeitherShowsNorAcceptsIt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = timeout.Token;
        await using var fixture = await LegacyIntercompanyControlsFixture.CreateAsync(ct);
        var (record, line) = await fixture.PostUnattributedReceivableAsync(ct);
        var fact = fixture.Attribution(record, line);
        await fixture.Attributions.AppendAsync(fact, null, ct);
        var sibling = await fixture.Journals.SaveLedgerBookAsync(new(Guid.NewGuid(), LegacyIntercompanyControlsFixture.Fund,
            Guid.NewGuid(), Meridian.Contracts.FundStructure.FundStructureNodeKindDto.Entity, "Sibling entity book", "USD",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), ct);

        (await fixture.Attributions.ListAsync(sibling.LedgerBookId, ct)).Should().BeEmpty();
        (await fixture.Attributions.HistoryAsync(sibling.LedgerBookId, ct)).Should().BeEmpty();
        var movedToSibling = () => fixture.Attributions.AppendAsync(fact with { AttributionId = Guid.NewGuid(), LedgerBookId = sibling.LedgerBookId }, null, ct);
        await movedToSibling.Should().ThrowAsync<InvalidOperationException>();
        var unknownBook = () => fixture.Attributions.ListAsync(Guid.NewGuid(), ct);
        await unknownBook.Should().ThrowAsync<TenantScopeRejectedException>();

        (await fixture.Attributions.HistoryAsync(sibling.LedgerBookId, ct)).Should().BeEmpty();
        (await fixture.Attributions.HistoryAsync(fixture.Book.LedgerBookId, ct)).Should().ContainSingle().Which.Should().Be(fact);
        (await fixture.CountAsync("legacy_intercompany_attributions", ct)).Should().Be(1);
    }

    [LedgerDatabaseFact]
    public async Task ConcurrentFirstAttributionsOfOneLine_RetainExactlyOneRevision()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = timeout.Token;
        await using var fixture = await LegacyIntercompanyControlsFixture.CreateAsync(ct);
        var (record, line) = await fixture.PostUnattributedReceivableAsync(ct);
        var left = fixture.Attribution(record, line);
        var right = fixture.Attribution(record, line) with { IntercompanyTransactionId = "competing-review" };

        var outcomes = await Task.WhenAll(
            TryAppendAsync(fixture.Attributions, left),
            TryAppendAsync(fixture.AttributionStoreFor(LegacyIntercompanyControlsFixture.Tenant), right));

        outcomes.Count(outcome => outcome is null).Should().Be(1);
        outcomes.OfType<InvalidOperationException>().Should().ContainSingle().Which.Message.Should().Contain("reload before appending");
        var retained = (await fixture.Attributions.HistoryAsync(fixture.Book.LedgerBookId, ct)).Should().ContainSingle().Subject;
        retained.Should().Be(outcomes[0] is null ? left : right);
        (await fixture.CountAsync("legacy_intercompany_attributions", ct)).Should().Be(1);

        async Task<Exception?> TryAppendAsync(PostgresLegacyIntercompanyAttributionStore store, LegacyIntercompanyAttributionDto attribution)
        {
            try
            { await store.AppendAsync(attribution, null, ct); return null; }
            catch (Exception exception) { return exception; }
        }
    }

    [LedgerDatabaseFact]
    public async Task AuthorityLease_HoldsOffANewAttributionUntilTheConsumerReleasesIt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = timeout.Token;
        await using var fixture = await LegacyIntercompanyControlsFixture.CreateAsync(ct);
        var (record, line) = await fixture.PostUnattributedReceivableAsync(ct);
        var fact = fixture.Attribution(record, line);
        var lease = await fixture.Attributions.AcquireAuthorityLeaseAsync(ct);
        Task<LegacyIntercompanyAttributionDto>? append = null;
        try
        {
            append = fixture.Attributions.AppendAsync(fact, null, ct);
            await WaitForAttributionLockAsync(fixture, ct);
            append.IsCompleted.Should().BeFalse();
            // Readers that hold the lease keep seeing the population they are deciding on.
            (await fixture.Attributions.HistoryAsync(fixture.Book.LedgerBookId, ct)).Should().BeEmpty();
        }
        finally { await lease.DisposeAsync(); }
        (await append!).Should().Be(fact);
        (await fixture.Attributions.HistoryAsync(fixture.Book.LedgerBookId, ct)).Should().ContainSingle();
    }

    private static async Task WaitForAttributionLockAsync(LegacyIntercompanyControlsFixture fixture, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.Database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                select exists(select 1 from pg_stat_activity
                    where wait_event_type = 'Lock' and query like '%' || @schema || '%legacy_intercompany_attributions%'
                    and pid <> pg_backend_pid());
                """;
            command.Parameters.AddWithValue("schema", fixture.Database.Options.SchemaName);
            if ((bool)(await command.ExecuteScalarAsync(ct))!)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(30), ct);
        }
        throw new TimeoutException("The attribution append did not wait for the retained authority lease.");
    }
}
