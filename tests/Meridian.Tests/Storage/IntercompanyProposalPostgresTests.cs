using System.Data;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Tests.FinancialOperations.Intercompany;
using Npgsql;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed partial class IntercompanyProposalPostgresTests
{
    [LedgerDatabaseFact]
    public async Task NativeSourceRefusesCallerOwnedTransaction_ThenStandaloneCommitsJournalAndOutboxTogether()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var fixture = await SetupAsync(database);
        var write = Write(fixture.Period, "invoice-atomic");
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await using var authority = await fixture.Journals.AcquirePostingAuthorityAsync(connection, transaction, [write]);
            var refused = await Assert.ThrowsAsync<LedgerValidationException>(() => fixture.Journals.AppendAsync(connection, transaction, write));
            Assert.Contains("standalone append transaction", refused.Message);
            Assert.Empty(await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookA));
            await transaction.RollbackAsync();
        }
        Assert.Empty(await fixture.Journals.GetByPeriodAsync(fixture.Period.PeriodId));
        Assert.Empty(await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookA));

        await fixture.Journals.AppendAsync(write);

        Assert.Single(await fixture.Journals.GetByPeriodAsync(fixture.Period.PeriodId));
        var source = Assert.Single(await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookA));
        Assert.Equal(write.Entry.JournalEntryId, source.Source.SourceJournalId);
        Assert.Equal("invoice-atomic", source.Source.TransactionId);
        Assert.Null(await fixture.Proposals.GetAsync("tenant-b", source.Source.ProposalId));
    }

    [LedgerDatabaseFact]
    public async Task IntercompanyDimensionQuery_ConjoinsAllFiveExactIdentitiesInPostgres()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var fixture = await SetupAsync(database);
        var first = Write(fixture.Period, "equal-amount-dimension-one");
        var second = Write(fixture.Period, "equal-amount-dimension-two");
        await fixture.Journals.AppendAsync(first);
        await fixture.Journals.AppendAsync(second);
        var source = first.Entry.Lines[0].Dimensions!;
        var exact = new LedgerLineDimensionSet
        {
            IntercompanyAgreementId = source.IntercompanyAgreementId,
            IntercompanyAgreementVersion = source.IntercompanyAgreementVersion,
            AccountingArrangementId = source.AccountingArrangementId,
            AccountingArrangementRevisionId = source.AccountingArrangementRevisionId,
            ChartAccountNodeId = source.ChartAccountNodeId,
            AccountBindingRevisionId = source.AccountBindingRevisionId,
            IntercompanyTransactionId = source.IntercompanyTransactionId
        };

        var selected = await fixture.Journals.QueryAsync(new(LedgerBookId: IntercompanyProposalTestData.BookA,
            LineDimensions: exact));

        Assert.Equal(first.Entry.JournalEntryId, Assert.Single(selected).Entry.JournalEntryId);
        foreach (var unmatched in new[]
        {
            exact with { IntercompanyAgreementId = Guid.NewGuid() },
            exact with { IntercompanyAgreementVersion = 999 },
            exact with { AccountingArrangementId = Guid.NewGuid().ToString("D") },
            exact with { AccountingArrangementRevisionId = Guid.NewGuid().ToString("D") },
            exact with { ChartAccountNodeId = "nonexistent-chart-node" },
            exact with { AccountBindingRevisionId = Guid.NewGuid().ToString("D") },
            exact with { IntercompanyTransactionId = "nonexistent-transaction" },
            exact with { AccountBindingRevisionId = second.Entry.Lines[0].Dimensions!.AccountBindingRevisionId }
        })
            Assert.Empty(await fixture.Journals.QueryAsync(new(LedgerBookId: IntercompanyProposalTestData.BookA,
                LineDimensions: unmatched)));
    }

    [LedgerDatabaseFact]
    public async Task MultipleEventGroups_CannotReuseSharedUnattributedContraLegs()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var fixture = await SetupAsync(database);
        var write = Write(fixture.Period, "loan-one");
        var first = write.Entry.Lines[0];
        var second = new LedgerEntry(Guid.NewGuid(), first.JournalEntryId, first.Timestamp, first.Account,
            100m, 0m, first.Description, dimensions: first.Dimensions! with { IntercompanyTransactionId = "loan-two" });
        var unrelated = new LedgerEntry(Guid.NewGuid(), first.JournalEntryId, first.Timestamp,
            new("Income:Unrelated", LedgerAccountType.Revenue), 0m, 100m, first.Description);
        write = write with
        {
            Entry = new JournalEntry(write.Entry.JournalEntryId, write.Entry.Timestamp,
            write.Entry.Description, [first, second, write.Entry.Lines[1], unrelated], write.Entry.Metadata)
        };

        await fixture.Journals.AppendAsync(write);

        var sources = await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookA);
        Assert.Equal(2, sources.Count);
        Assert.All(sources, proposal => Assert.True(proposal.Source.RequiresSourceAllocation));
        Assert.Equal(2, sources.Select(proposal => proposal.Source.TransactionId).Distinct().Count());
    }

    [LedgerDatabaseFact]
    public async Task ConcurrentClaims_AreDisjoint_AndOnlyCurrentTenantClaimCanComplete()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var fixture = await SetupAsync(database);
        await fixture.Journals.AppendAsync(Write(fixture.Period, "equal-amount-one"));
        await fixture.Journals.AppendAsync(Write(fixture.Period, "equal-amount-two"));

        var claims = await Task.WhenAll(fixture.Proposals.ClaimAsync("worker-one", 1, TimeSpan.FromMinutes(1)),
            fixture.Proposals.ClaimAsync("worker-two", 1, TimeSpan.FromMinutes(1)));
        var first = Assert.Single(claims[0]);
        var second = Assert.Single(claims[1]);
        Assert.NotEqual(first.Source.ProposalId, second.Source.ProposalId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Proposals.CompleteAsync("tenant-b", first.Source.ProposalId,
            first.ClaimToken!, IntercompanyProposalTestData.BookB, Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Proposals.CompleteAsync("tenant-a", first.Source.ProposalId,
            second.ClaimToken!, IntercompanyProposalTestData.BookB, Guid.NewGuid()));
        var destinationDraft = Guid.NewGuid();
        await fixture.Proposals.CompleteAsync("tenant-a", first.Source.ProposalId, first.ClaimToken!,
            IntercompanyProposalTestData.BookB, destinationDraft);
        var retained = await fixture.Proposals.GetAsync("tenant-a", first.Source.ProposalId);
        Assert.Equal(IntercompanyProposalStatusDto.Drafted, retained!.Status);
        Assert.Equal(destinationDraft, retained.TargetJournalId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Proposals.FailAsync("tenant-a", first.Source.ProposalId,
            first.ClaimToken!, "Cannot overwrite a delivered receipt"));
        Assert.Equal(destinationDraft, (await fixture.Proposals.GetAsync("tenant-a", first.Source.ProposalId))!.TargetJournalId);
    }

    [LedgerDatabaseFact]
    public async Task ExplicitRetry_IsIdempotentAndContentValidating_SourceFactsRemainImmutable()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var fixture = await SetupAsync(database);
        await fixture.Journals.AppendAsync(Write(fixture.Period, "retry-source"));
        var claimed = Assert.Single(await fixture.Proposals.ClaimAsync("worker", 1, TimeSpan.FromMinutes(1)));
        await fixture.Proposals.FailAsync("tenant-a", claimed.Source.ProposalId, claimed.ClaimToken!, "Destination period is closed");
        var retry = new RetryIntercompanyProposalRequest(Guid.NewGuid(), "Destination period is now available");
        await fixture.Proposals.RetryAsync("tenant-a", claimed.Source.ProposalId, retry, "operator");
        await fixture.Proposals.RetryAsync("tenant-a", claimed.Source.ProposalId, retry, "operator");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Proposals.RetryAsync("tenant-a", claimed.Source.ProposalId,
            retry with { Reason = "Different decision" }, "operator"));
        var retained = await fixture.Proposals.GetAsync("tenant-a", claimed.Source.ProposalId);
        Assert.Equal(IntercompanyProposalStatusDto.Pending, retained!.Status);
        Assert.Equal(claimed.SourceDigest, retained.SourceDigest);
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        var schema = new NpgsqlCommandBuilder().QuoteIdentifier(database.Options.SchemaName);
        await using var mutate = new NpgsqlCommand($"update {schema}.intercompany_proposals set source_payload='changed' where proposal_id=@id", connection);
        mutate.Parameters.AddWithValue("id", claimed.Source.ProposalId);
        var error = await Assert.ThrowsAsync<PostgresException>(() => mutate.ExecuteNonQueryAsync());
        Assert.Equal("P0001", error.SqlState);
        Assert.Equal(claimed.SourceDigest, (await fixture.Proposals.GetAsync("tenant-a", claimed.Source.ProposalId))!.SourceDigest);
    }

    [LedgerDatabaseFact]
    public async Task TerminalReceiptAndLinkEvidence_RejectUpdateDeleteAndTruncate()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var fixture = await SetupAsync(database);
        var write = Write(fixture.Period, "manual-link");
        await fixture.Journals.AppendAsync(write);
        var source = Assert.Single(await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookA));
        var link = new LinkIntercompanyCounterpartRequest(Guid.NewGuid(), IntercompanyProposalTestData.BookB, Guid.NewGuid(), "Reviewed counterpart");
        await fixture.Proposals.LinkAsync("tenant-a", source.Source.ProposalId, link, "reviewer");
        await fixture.Proposals.LinkAsync("tenant-a", source.Source.ProposalId, link, "reviewer");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Proposals.LinkAsync("tenant-a", source.Source.ProposalId,
            link with { TargetJournalId = Guid.NewGuid() }, "reviewer"));
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        var schema = new NpgsqlCommandBuilder().QuoteIdentifier(database.Options.SchemaName);
        foreach (var sql in new[] { $"update {schema}.intercompany_proposals set target_journal_id=null",
            $"delete from {schema}.intercompany_proposal_links", $"truncate {schema}.intercompany_proposal_links",
            $"truncate {schema}.intercompany_proposal_retries", $"truncate {schema}.intercompany_proposals cascade" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal("P0001", error.SqlState);
        }
    }

    [LedgerDatabaseFact]
    public async Task EqualAmountSources_CannotReuseOneCounterpartJournalToClearBoth()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var fixture = await SetupAsync(database);
        await fixture.Journals.AppendAsync(Write(fixture.Period, "separate-invoice-one"));
        await fixture.Journals.AppendAsync(Write(fixture.Period, "separate-invoice-two"));
        var sources = await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookA);
        var targetJournal = Guid.NewGuid();
        await fixture.Proposals.LinkAsync("tenant-a", sources[0].Source.ProposalId,
            new(Guid.NewGuid(), IntercompanyProposalTestData.BookB, targetJournal, "First reviewed source"), "reviewer");

        var error = await Record.ExceptionAsync(() => fixture.Proposals.LinkAsync("tenant-a", sources[1].Source.ProposalId,
            new(Guid.NewGuid(), IntercompanyProposalTestData.BookB, targetJournal, "Cannot reuse posted amount"), "reviewer"));

        Assert.True(error is InvalidOperationException or PostgresException { SqlState: "23505" });
        var second = await fixture.Proposals.GetAsync("tenant-a", sources[1].Source.ProposalId);
        Assert.Equal(IntercompanyProposalStatusDto.Pending, second!.Status);
        Assert.Null(second.TargetJournalId);
    }

    [LedgerDatabaseFact]
    public async Task RejectedDelivery_ManualResolutionAppendsHistoryAndPreservesOriginalTerminalReceipt()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var fixture = await SetupAsync(database);
        await fixture.Journals.AppendAsync(Write(fixture.Period, "rejected-delivery"));
        var claimed = Assert.Single(await fixture.Proposals.ClaimAsync("worker", 1, TimeSpan.FromMinutes(1)));
        var originalDraft = claimed.Source.ProposalId;
        await fixture.Proposals.CompleteAsync("tenant-a", claimed.Source.ProposalId, claimed.ClaimToken!, IntercompanyProposalTestData.BookB, originalDraft);
        var before = await fixture.Proposals.GetAsync("tenant-a", claimed.Source.ProposalId);
        var manual = Guid.NewGuid();
        var request = new LinkIntercompanyCounterpartRequest(Guid.NewGuid(), IntercompanyProposalTestData.BookB, manual, "Rejected generated draft; reviewed posted manual counterpart");
        var evidence = new RejectedIntercompanyDraftEvidenceDto(IntercompanyProposalTestData.BookB, originalDraft, 4, claimed.Source.ProposalId, claimed.SourceDigest);

        await fixture.Proposals.LinkRejectedDraftAsync("tenant-a", claimed.Source.ProposalId, request, "reviewer", evidence);
        await fixture.Proposals.LinkAsync("tenant-a", claimed.Source.ProposalId, request, "reviewer");

        var after = await fixture.Proposals.GetAsync("tenant-a", claimed.Source.ProposalId);
        Assert.Equal(IntercompanyProposalStatusDto.Drafted, after!.Status);
        Assert.Equal(before!.TargetJournalId, after.TargetJournalId);
        Assert.Equal(before.TargetBookId, after.TargetBookId);
        Assert.Equal(before.RecordedAt, after.RecordedAt);
        Assert.Equal(before.SourceDigest, after.SourceDigest);
        Assert.Equal(manual, after.ResolvedTargetJournalId);
        Assert.Equal(originalDraft, after.LatestLink!.RejectedDraftId);
        Assert.Equal(4, after.LatestLink.RejectedDraftVersion);
        Assert.Equal(manual, Assert.Single(await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookA)).ResolvedTargetJournalId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Proposals.LinkAsync("tenant-a", claimed.Source.ProposalId,
            request with { Reason = "Changed replay decision" }, "reviewer"));
    }

    [LedgerDatabaseFact]
    public async Task AppendOnlyManualResolution_ReservesTargetAgainstAnotherDeliveryAndManualLink()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var fixture = await SetupAsync(database);
        foreach (var id in new[] { "first-source", "second-source", "third-source" })
            await fixture.Journals.AppendAsync(Write(fixture.Period, id));
        var first = Assert.Single(await fixture.Proposals.ClaimAsync("first-worker", 1, TimeSpan.FromMinutes(1)));
        await fixture.Proposals.CompleteAsync("tenant-a", first.Source.ProposalId, first.ClaimToken!, IntercompanyProposalTestData.BookB, first.Source.ProposalId);
        var manual = Guid.NewGuid();
        await fixture.Proposals.LinkRejectedDraftAsync("tenant-a", first.Source.ProposalId,
            new(Guid.NewGuid(), IntercompanyProposalTestData.BookB, manual, "Reviewed replacement"), "reviewer",
            new(IntercompanyProposalTestData.BookB, first.Source.ProposalId, 2, first.Source.ProposalId, first.SourceDigest));
        var others = (await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookA)).Where(x => x.Source.ProposalId != first.Source.ProposalId).ToArray();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Proposals.LinkAsync("tenant-a", others[0].Source.ProposalId,
            new(Guid.NewGuid(), IntercompanyProposalTestData.BookB, manual, "Cannot consume same target twice"), "reviewer"));
        var claims = await fixture.Proposals.ClaimAsync("other-worker", 10, TimeSpan.FromMinutes(1));
        Assert.Equal(2, claims.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Proposals.CompleteAsync("tenant-a", claims[0].Source.ProposalId,
            claims[0].ClaimToken!, IntercompanyProposalTestData.BookB, manual));
        Assert.Null((await fixture.Proposals.GetAsync("tenant-a", claims[0].Source.ProposalId))!.TargetJournalId);
        Assert.Equal(manual, (await fixture.Proposals.GetAsync("tenant-a", first.Source.ProposalId))!.ResolvedTargetJournalId);
    }

    [LedgerDatabaseFact]
    public async Task ConcurrentReplacementAndDelivery_CannotReserveOneCounterpartForTwoSources()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var fixture = await SetupAsync(database);
        await fixture.Journals.AppendAsync(Write(fixture.Period, "replacement-source"));
        await fixture.Journals.AppendAsync(Write(fixture.Period, "delivery-source"));
        var first = Assert.Single(await fixture.Proposals.ClaimAsync("first", 1, TimeSpan.FromMinutes(1)));
        await fixture.Proposals.CompleteAsync("tenant-a", first.Source.ProposalId, first.ClaimToken!, IntercompanyProposalTestData.BookB, first.Source.ProposalId);
        var second = Assert.Single(await fixture.Proposals.ClaimAsync("second", 1, TimeSpan.FromMinutes(1)));
        var shared = Guid.NewGuid();

        var results = await Task.WhenAll(
            Record.ExceptionAsync(() => fixture.Proposals.LinkRejectedDraftAsync("tenant-a", first.Source.ProposalId,
                new(Guid.NewGuid(), IntercompanyProposalTestData.BookB, shared, "Reviewed replacement"), "reviewer",
                new(IntercompanyProposalTestData.BookB, first.Source.ProposalId, 2, first.Source.ProposalId, first.SourceDigest))),
            Record.ExceptionAsync(() => fixture.Proposals.CompleteAsync("tenant-a", second.Source.ProposalId, second.ClaimToken!, IntercompanyProposalTestData.BookB, shared)));

        Assert.Single(results.Where(error => error is null));
        Assert.IsType<InvalidOperationException>(Assert.Single(results.Where(error => error is not null)));
        Assert.Single((await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookA)).Where(x => x.ResolvedTargetJournalId == shared));
    }

    [LedgerDatabaseFact]
    public async Task GeneratedCounterpart_ExactReversalKeepsRetainedOriginWithoutMirrorPingPong()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var fixture = await SetupAsync(database);
        var (proposal, counterpart) = await PostGeneratedCounterpartAsync(fixture);
        var correction = Correction(fixture.TargetPeriod, counterpart, AccountingPostingIntentDto.Reversal);

        await fixture.Journals.AppendAsync(correction);

        var entries = await fixture.Journals.GetByPeriodAsync(fixture.TargetPeriod.PeriodId);
        var retained = Assert.Single(entries.Where(entry => entry.Entry.JournalEntryId == correction.Entry.JournalEntryId));
        Assert.Equal(counterpart.JournalEntryId, retained.SourceJournalEntryId);
        Assert.Equal(proposal.Source.ProposalId.ToString("D"), retained.Entry.Metadata.Tags!["intercompany.proposalId"]);
        Assert.True(IntercompanyJournalCorrectionEvidence.IsExactInverse(retained.Entry, counterpart));
        Assert.Equal(2, entries.Count);
        Assert.Single(await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookA));
        Assert.Empty(await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookB));
    }

    [LedgerDatabaseFact]
    public async Task GeneratedCounterpart_ReviewedRebookMayChangeAmountsWhileRetainingActualSource()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var fixture = await SetupAsync(database);
        var (_, counterpart) = await PostGeneratedCounterpartAsync(fixture);
        var correction = Correction(fixture.TargetPeriod, counterpart, AccountingPostingIntentDto.Rebook, scale: 1.25m);

        await fixture.Journals.AppendAsync(correction);

        var retained = Assert.Single((await fixture.Journals.GetByPeriodAsync(fixture.TargetPeriod.PeriodId))
            .Where(entry => entry.Entry.JournalEntryId == correction.Entry.JournalEntryId));
        Assert.Equal(counterpart.JournalEntryId, retained.SourceJournalEntryId);
        Assert.Equal(125m, retained.Entry.Lines.Sum(line => line.Debit));
        Assert.Equal("governed-rebook", retained.AdjustmentApproval!.ReasonCode);
        Assert.Single(await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookA));
        Assert.Empty(await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookB));
    }

    [LedgerDatabaseFact]
    public async Task GeneratedSourceReversal_NewProposalMarkerProvesBothSourceInverseAndPriorCounterpart()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var fixture = await SetupAsync(database);
        var (originalProposal, counterpart) = await PostGeneratedCounterpartAsync(fixture);
        var source = Assert.Single(await fixture.Journals.GetByPeriodAsync(fixture.Period.PeriodId)).Entry;
        var sourceInverse = Correction(fixture.Period, source, AccountingPostingIntentDto.Reversal);
        await fixture.Journals.AppendAsync(sourceInverse);
        var proposal = Assert.Single(await fixture.Proposals.ClaimAsync("reversal-worker", 1, TimeSpan.FromMinutes(1)));
        Assert.Equal(originalProposal.Source.SourceJournalId, proposal.Source.ReversesJournalId);
        await fixture.Proposals.CompleteAsync("tenant-a", proposal.Source.ProposalId, proposal.ClaimToken!,
            IntercompanyProposalTestData.BookB, proposal.Source.ProposalId);
        var delivery = Correction(fixture.TargetPeriod, counterpart, AccountingPostingIntentDto.Reversal,
            journalId: proposal.Source.ProposalId);
        delivery = delivery with
        {
            Entry = new(delivery.Entry.JournalEntryId, delivery.Entry.Timestamp,
            delivery.Entry.Description, delivery.Entry.Lines, delivery.Entry.Metadata with
            {
                Tags = new Dictionary<string, string>(delivery.Entry.Metadata.Tags!)
                { ["intercompany.proposalId"] = proposal.Source.ProposalId.ToString("D"), ["intercompany.sourceDigest"] = proposal.SourceDigest }
            })
        };

        await fixture.Journals.AppendAsync(delivery);

        Assert.Equal(2, (await fixture.Journals.GetByPeriodAsync(fixture.TargetPeriod.PeriodId)).Count);
        Assert.Equal(2, (await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookA)).Count);
        Assert.Empty(await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookB));
    }

    [LedgerDatabaseFact]
    public async Task PreparedSourceReversal_ManualCounterpartCorrectionWins_AppendRefusesDuplicateInverse()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var fixture = await SetupAsync(database);
        var (_, counterpart) = await PostGeneratedCounterpartAsync(fixture);
        var source = Assert.Single(await fixture.Journals.GetByPeriodAsync(fixture.Period.PeriodId)).Entry;
        await fixture.Journals.AppendAsync(Correction(fixture.Period, source, AccountingPostingIntentDto.Reversal));
        var proposal = Assert.Single(await fixture.Proposals.ClaimAsync("reversal-worker", 1, TimeSpan.FromMinutes(1)));
        await fixture.Proposals.CompleteAsync("tenant-a", proposal.Source.ProposalId, proposal.ClaimToken!,
            IntercompanyProposalTestData.BookB, proposal.Source.ProposalId);
        var prepared = Correction(fixture.TargetPeriod, counterpart, AccountingPostingIntentDto.Reversal,
            journalId: proposal.Source.ProposalId);
        prepared = prepared with
        {
            Entry = new(prepared.Entry.JournalEntryId, prepared.Entry.Timestamp,
            prepared.Entry.Description, prepared.Entry.Lines, prepared.Entry.Metadata with
            {
                Tags = new Dictionary<string, string>(prepared.Entry.Metadata.Tags!)
                { ["intercompany.proposalId"] = proposal.Source.ProposalId.ToString("D"), ["intercompany.sourceDigest"] = proposal.SourceDigest }
            })
        };
        // The generated inverse is already prepared and delivered when a reviewer posts a manual
        // correction. Only the append-time check can prevent a second inverse of the same target.
        var manual = Correction(fixture.TargetPeriod, counterpart, AccountingPostingIntentDto.Reversal);
        await fixture.Journals.AppendAsync(manual);

        var error = await Assert.ThrowsAsync<LedgerValidationException>(() => fixture.Journals.AppendAsync(prepared));

        Assert.Equal("The immutable source journal already has a committed reversal.", error.Message);
        var retained = await fixture.Journals.GetByPeriodAsync(fixture.TargetPeriod.PeriodId);
        Assert.Equal(2, retained.Count);
        Assert.Contains(retained, entry => entry.Entry.JournalEntryId == manual.Entry.JournalEntryId);
        Assert.DoesNotContain(retained, entry => entry.Entry.JournalEntryId == prepared.Entry.JournalEntryId);
        Assert.Empty(await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookB));
    }

    [LedgerDatabaseFact]
    public async Task GeneratedCounterpart_ForgedCorrectionCannotUseMarkerToSuppressProposals()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var fixture = await SetupAsync(database);
        var (proposal, counterpart) = await PostGeneratedCounterpartAsync(fixture);
        var unrelatedId = Guid.NewGuid();
        var unrelated = new JournalEntry(unrelatedId, counterpart.Timestamp, "Unrelated same-book transfer", counterpart.Lines
            .Select(line => new LedgerEntry(Guid.NewGuid(), unrelatedId, line.Timestamp, line.Account, line.Debit, line.Credit,
                "Unrelated same-book transfer")).ToArray());
        var unrelatedCommand = new AccountingPostingCommandDto(Guid.NewGuid(), IntercompanyProposalTestData.BookB,
            fixture.TargetPeriod.PeriodId, IntercompanyProposalTestData.Date, counterpart.Timestamp, unrelatedId.ToString("D"),
            ApprovalState: AccountingPostingApprovalStateDto.Approved, OperatorRationale: "Unrelated transfer fixture",
            LedgerBookId: IntercompanyProposalTestData.BookB)
        { Actor = "reviewer" };
        await fixture.Journals.AppendAsync(new(unrelated, IntercompanyProposalTestData.BookB, fixture.TargetPeriod.PeriodId,
            unrelatedCommand.CommandId, PostingCommand: unrelatedCommand, LedgerBookId: IntercompanyProposalTestData.BookB));
        foreach (var changed in new[] { "originating", "missing-source", "foreign-source", "unmarked-source", "conflicting-source", "digest",
            "transaction", "unreviewed", "amount", "contra-account", "project" })
        {
            var sourceId = changed == "missing-source" ? Guid.NewGuid()
                : changed == "foreign-source" ? proposal.Source.SourceJournalId
                : changed == "unmarked-source" ? unrelatedId : counterpart.JournalEntryId;
            var correction = Correction(fixture.TargetPeriod, counterpart, AccountingPostingIntentDto.Reversal,
                scale: changed == "amount" ? 0.5m : 1m, sourceId: sourceId);
            if (changed == "originating")
                correction = correction with
                { PostingCommand = correction.PostingCommand! with { Intent = AccountingPostingIntentDto.Originating } };
            if (changed == "conflicting-source")
                correction = correction with
                { PostingCommand = correction.PostingCommand! with { SourceJournalEntryId = Guid.NewGuid() } };
            if (changed == "unreviewed")
                correction = correction with
                { PostingCommand = correction.PostingCommand! with { ApprovalState = AccountingPostingApprovalStateDto.NotRequired }, AdjustmentApproval = null };
            if (changed is "digest" or "project")
                correction = correction with
                {
                    Entry = new(correction.Entry.JournalEntryId,
                correction.Entry.Timestamp, correction.Entry.Description, correction.Entry.Lines,
                correction.Entry.Metadata with
                {
                    ProjectId = changed == "project" ? "another-project" : correction.Entry.Metadata.ProjectId,
                    Tags = new Dictionary<string, string>(correction.Entry.Metadata.Tags!)
                    { ["intercompany.sourceDigest"] = changed == "digest" ? new string('0', 64) : proposal.SourceDigest }
                })
                };
            if (changed is "transaction" or "contra-account")
                correction = correction with
                {
                    Entry = new(correction.Entry.JournalEntryId,
                correction.Entry.Timestamp, correction.Entry.Description, correction.Entry.Lines.Select(line => new LedgerEntry(
                    line.EntryId, line.JournalEntryId, line.Timestamp,
                    changed == "contra-account" && line.Dimensions?.AccountingArrangementId is null
                        ? line.Account with { FinancialAccountId = "different-bank-account" } : line.Account,
                    line.Debit, line.Credit, line.Description,
                    changed == "transaction" && line.Dimensions?.AccountingArrangementId is not null
                        ? line.Dimensions with { IntercompanyTransactionId = "another-occurrence" } : line.Dimensions, line.Currency)).ToArray(),
                correction.Entry.Metadata)
                };

            var error = await Record.ExceptionAsync(() => fixture.Journals.AppendAsync(correction));
            Assert.True(error is LedgerValidationException, $"{changed}: {error}");
        }
        Assert.Equal(2, (await fixture.Journals.GetByPeriodAsync(fixture.TargetPeriod.PeriodId)).Count);
        Assert.Empty(await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookB));
    }

    private static async Task<(IntercompanyProposalDto Proposal, JournalEntry Counterpart)> PostGeneratedCounterpartAsync(Fixture fixture)
    {
        await fixture.Journals.AppendAsync(Write(fixture.Period, "governed-counterpart-correction"));
        var proposal = Assert.Single(await fixture.Proposals.ClaimAsync("worker", 1, TimeSpan.FromMinutes(1)));
        await fixture.Proposals.CompleteAsync("tenant-a", proposal.Source.ProposalId, proposal.ClaimToken!,
            IntercompanyProposalTestData.BookB, proposal.Source.ProposalId);
        var id = proposal.Source.ProposalId;
        const string description = "Generated counterpart";
        var now = IntercompanyProposalTestData.Now;
        var dimensions = new LedgerLineDimensionSet(EntityId: IntercompanyProposalTestData.EntityB.ToString("D"),
            BookId: IntercompanyProposalTestData.BookB.ToString("D"), CounterpartyId: IntercompanyProposalTestData.EntityA.ToString("D"))
        {
            IntercompanyAgreementId = proposal.Source.IntercompanyAgreementId,
            IntercompanyAgreementVersion = proposal.Source.IntercompanyAgreementVersion,
            AccountingArrangementId = proposal.Source.AccountingArrangementId.ToString("D"),
            AccountingArrangementRevisionId = proposal.Source.AccountingArrangementRevisionId.ToString("D"),
            IntercompanyTransactionId = proposal.Source.TransactionId,
            ChartAccountNodeId = "b-payable",
            AccountBindingRevisionId = Guid.NewGuid().ToString("D")
        };
        var entry = new JournalEntry(id, now, description, [
            new(Guid.NewGuid(), id, now, new("Liabilities:Due to A", LedgerAccountType.Liability), 0m, 100m, description, dimensions),
            new(Guid.NewGuid(), id, now, new("Assets:Cash", LedgerAccountType.Asset), 100m, 0m, description)],
            new(ProjectId: "target-fund", Tags: new Dictionary<string, string>
            { ["companyId"] = "company-a", ["intercompany.proposalId"] = id.ToString("D"), ["intercompany.sourceDigest"] = proposal.SourceDigest }));
        var command = new AccountingPostingCommandDto(Guid.NewGuid(), IntercompanyProposalTestData.BookB, fixture.TargetPeriod.PeriodId,
            IntercompanyProposalTestData.Date, now, id.ToString("D"), ApprovalState: AccountingPostingApprovalStateDto.Approved,
            OperatorRationale: "Reviewed generated counterpart", LedgerBookId: IntercompanyProposalTestData.BookB)
        { Actor = "reviewer" };
        await fixture.Journals.AppendAsync(new(entry, IntercompanyProposalTestData.BookB, fixture.TargetPeriod.PeriodId,
            command.CommandId, PostingCommand: command, LedgerBookId: IntercompanyProposalTestData.BookB));
        return (proposal, Assert.Single(await fixture.Journals.GetByPeriodAsync(fixture.TargetPeriod.PeriodId)).Entry);
    }

    private static LedgerJournalEntryWrite Correction(LedgerAccountingPeriod period, JournalEntry source,
        AccountingPostingIntentDto intent, decimal scale = 1m, Guid? sourceId = null, Guid? journalId = null)
    {
        var id = journalId ?? Guid.NewGuid();
        var bookId = period.LedgerBookId!.Value;
        var now = IntercompanyProposalTestData.Now;
        var originalId = sourceId ?? source.JournalEntryId;
        var reverse = intent == AccountingPostingIntentDto.Reversal;
        var description = $"Reviewed {intent}";
        var entry = new JournalEntry(id, now, description, source.Lines.Select(line => new LedgerEntry(Guid.NewGuid(), id,
            now, line.Account, (reverse ? line.Credit : line.Debit) * scale, (reverse ? line.Debit : line.Credit) * scale,
            description, line.Dimensions, line.Currency is { } currency ? new(currency.TransactionCurrency, currency.FunctionalCurrency,
                (reverse ? currency.TransactionCredit : currency.TransactionDebit) * scale,
                (reverse ? currency.TransactionDebit : currency.TransactionCredit) * scale, currency.FxRateToFunctional) : null)).ToArray(),
            source.Metadata with
            {
                IdempotencyKey = id.ToString("D"),
                Tags = source.Metadata.Tags!
                .Where(pair => pair.Key is "companyId" or "intercompany.proposalId" or "intercompany.sourceDigest")
                .ToDictionary(pair => pair.Key, pair => pair.Value)
            });
        var approvalId = $"approval:{id:D}";
        var command = new AccountingPostingCommandDto(Guid.NewGuid(), bookId, period.PeriodId,
            IntercompanyProposalTestData.Date, now, id.ToString("D"), intent, SourceJournalEntryId: originalId,
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: approvalId,
            OperatorRationale: "Reviewed correction with retained original", LedgerBookId: bookId)
        { Actor = "reviewer" };
        return new(entry, bookId, period.PeriodId, command.CommandId,
            SourceJournalEntryId: originalId, PostingKind: LedgerPostingKindDto.Adjustment,
            AdjustmentApproval: new(approvalId, LedgerAdjustmentApprovalStatusDto.Approved, "reviewer", now,
                reverse ? "period-close-reversal" : "governed-rebook", $"journal-correction:{originalId:D}", "retained:review"),
            PostingCommand: command, LedgerBookId: bookId);
    }

    private static async Task<Fixture> SetupAsync(LedgerPostgresTestDatabase database)
    {
        var owner = new PostgresFundProfileTenancyRegistry(database.Options);
        await owner.BindAsync("source-fund", "tenant-a", "company-a");
        await owner.BindAsync("target-fund", "tenant-a", "company-a");
        // Synthetic controls isolate durable outbox tests from lifecycle/agreement policy tests.
        // Both required control names are explicit; production never uses these participants.
        var journals = new PostgresLedgerJournalStore(database.Options, new FixtureTenant(),
            postingAuthorities: () => [new FixtureAuthority("entity-lifecycle"), new FixtureAuthority("intercompany")],
            intercompanyAgreementAuthority: () => new FixtureNativeAuthority());
        await journals.SaveLedgerBookAsync(new(IntercompanyProposalTestData.BookA, "source-fund", IntercompanyProposalTestData.EntityA,
            FundStructureNodeKindDto.Entity, "Source", "USD", IntercompanyProposalTestData.Now, IntercompanyProposalTestData.Now));
        await journals.SaveLedgerBookAsync(IntercompanyProposalTestData.TargetBook());
        var period = await journals.SavePeriodAsync(new(Guid.NewGuid(), IntercompanyProposalTestData.BookA, 2026, 6,
            "June", new(2026, 6, 1), new(2026, 6, 30), "Open", IntercompanyProposalTestData.Now, null, 0), 0);
        var targetPeriod = await journals.SavePeriodAsync(new(Guid.NewGuid(), IntercompanyProposalTestData.BookB, 2026, 6,
            "June", new(2026, 6, 1), new(2026, 6, 30), "Open", IntercompanyProposalTestData.Now, null, 0), 0);
        return new(journals, new(database.Options), period, targetPeriod);
    }

    private static LedgerJournalEntryWrite Write(LedgerAccountingPeriod period, string transaction)
    {
        var journalId = Guid.NewGuid();
        var now = IntercompanyProposalTestData.Now;
        var dimensions = new LedgerLineDimensionSet(EntityId: IntercompanyProposalTestData.EntityA.ToString("D"),
            BookId: IntercompanyProposalTestData.BookA.ToString("D"), CounterpartyId: IntercompanyProposalTestData.EntityB.ToString("D"))
        {
            IntercompanyAgreementId = IntercompanyProposalTestData.Agreement().CanonicalAgreementId,
            IntercompanyAgreementVersion = IntercompanyProposalTestData.Agreement().CanonicalAgreementVersion,
            AccountingArrangementId = IntercompanyProposalTestData.AccountingArrangementId.ToString("D"),
            AccountingArrangementRevisionId = IntercompanyProposalTestData.RevisionId.ToString("D"),
            IntercompanyTransactionId = transaction,
            ChartAccountNodeId = "a-receivable",
            AccountBindingRevisionId = Guid.NewGuid().ToString("D")
        };
        var entry = new JournalEntry(journalId, now, "Synthetic intercompany source", [
            new(Guid.NewGuid(), journalId, now, new("Assets:Due from B", LedgerAccountType.Asset), 100m, 0m, "Synthetic intercompany source", dimensions: dimensions),
            new(Guid.NewGuid(), journalId, now, new("Assets:Cash", LedgerAccountType.Asset), 0m, 100m, "Synthetic intercompany source")],
            new JournalEntryMetadata(Tags: new Dictionary<string, string> { ["companyId"] = "company-a" }));
        var command = new AccountingPostingCommandDto(Guid.NewGuid(), IntercompanyProposalTestData.BookA, period.PeriodId,
            IntercompanyProposalTestData.Date, now, journalId.ToString("D"), ApprovalState: AccountingPostingApprovalStateDto.Approved,
            OperatorRationale: "Synthetic fixture posting", LedgerBookId: IntercompanyProposalTestData.BookA)
        { Actor = "synthetic-reviewer" };
        return new(entry, IntercompanyProposalTestData.BookA, period.PeriodId, command.CommandId, null,
            PostingCommand: command, LedgerBookId: IntercompanyProposalTestData.BookA);
    }

    private sealed record Fixture(PostgresLedgerJournalStore Journals, PostgresIntercompanyProposalStore Proposals,
        LedgerAccountingPeriod Period, LedgerAccountingPeriod TargetPeriod);
    private sealed class FixtureTenant : IFundScopeTenantAccessor { public string? ResolveCallerTenant() => "tenant-a"; }
    // This isolated persistence fixture supplies precise synthetic approved legal state.
    // The durable ledger still executes its real shape, source-inverse and lease-scope guards.
    private sealed class FixtureNativeAuthority : IIntercompanyAgreementPostingAuthority
    {
        public Task<IAsyncDisposable> AcquireValidatedLeaseAsync(IntercompanyAgreementPostingRequest request, CancellationToken ct = default)
        {
            var revision = IntercompanyProposalTestData.Agreement().LegalRevision;
            Assert.Equal("tenant-a", request.TenantId);
            Assert.Contains(request.BookEntityId, new[] { IntercompanyProposalTestData.EntityA, IntercompanyProposalTestData.EntityB });
            Assert.All(request.Lines, line =>
            {
                Assert.Equal(revision.AgreementId, line.AgreementId);
                Assert.Equal(revision.AgreementVersion, line.AgreementVersion);
                Assert.Equal(request.BookEntityId, line.PostingEntityId);
                Assert.Equal(line.PostingEntityId == IntercompanyProposalTestData.EntityA
                    ? IntercompanyProposalTestData.EntityB : IntercompanyProposalTestData.EntityA, line.CounterpartyId);
                Assert.Equal(revision.Terms.Currency, line.TransactionCurrency);
            });
            return Task.FromResult<IAsyncDisposable>(new FixtureNativeLease(revision));
        }
    }
    private sealed class FixtureNativeLease(IntercompanyAgreementRevisionDto revision) : IIntercompanyAgreementPostingLease
    {
        public IReadOnlyList<IntercompanyAgreementStoreState> Agreements { get; } = [new(revision.AgreementId, 1, [], [revision], [], [])];
        public Task<LegalEntitySummaryDto?> GetLegalEntityAsync(Guid entityId, CancellationToken ct = default)
            => Task.FromResult<LegalEntitySummaryDto?>(null);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FixtureAuthority(string name) : ILedgerPostingAuthority
    {
        public string ControlName => name;
        public Task<IAsyncDisposable> AcquireAsync(IReadOnlyList<LedgerPostingAuthorityContext> contexts, CancellationToken ct = default)
        {
            foreach (var context in contexts)
            {
                Assert.Equal("tenant-a", context.TenantId);
                Assert.Contains(context.Book.LedgerBookId, new[] { IntercompanyProposalTestData.BookA, IntercompanyProposalTestData.BookB });
                context.Decisions.Add(new(name, "synthetic-fixture", true, "Synthetic authority for outbox persistence test."));
            }
            return Task.FromResult<IAsyncDisposable>(EmptyLedgerAuthorityLease.Instance);
        }
    }
}
