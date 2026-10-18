using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
public sealed class PostgresSecurityMasterClosePriceTests(SecurityMasterDatabaseFixture fixture)
    : IClassFixture<SecurityMasterDatabaseFixture>
{
    private static readonly DateTimeOffset Close = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);

    [SecurityMasterDatabaseFact]
    public async Task ApprovedSetSurvivesRestartLaterQuotesAndRetainsLinkedCorrectionInExactScope()
    {
        var id = await SeedSecurityAsync();
        var account = $"account-{Guid.NewGuid():N}";
        var store = new PostgresSecurityMasterPricingStore(fixture.Options);
        var service = Service(store, id);
        await SeedPricingAsync(store, id, account, Close.AddHours(-2));
        var first = await service.CreateClosePriceSetAsync(Request(id, account));
        var approved = await service.ApproveClosePriceSetAsync(Approve(first));
        await store.RecordRawPriceAsync(new(id, "source", 101m, Close.AddHours(-1), "corrector", SecurityPriceUnit.PercentOfPar));

        var restartedStore = new PostgresSecurityMasterPricingStore(fixture.Options);
        var restarted = Service(restartedStore, id);
        var replay = await restarted.GetClosePriceSetAsync(account, "book-a", first.Snapshot.ValuationDate, first.Snapshot.SnapshotId);
        replay.Should().BeEquivalentTo(approved);
        replay!.Snapshot.Lines.Single().ClosePrice.Should().Be(100m);
        (await restarted.GetClosePriceSetAsync("other-account", "book-a", first.Snapshot.ValuationDate, first.Snapshot.SnapshotId)).Should().BeNull();
        (await restarted.GetClosePriceSetAsync(account, "other-book", first.Snapshot.ValuationDate, first.Snapshot.SnapshotId)).Should().BeNull();
        (await restarted.GetClosePriceSetAsync(account, "book-a", first.Snapshot.ValuationDate.AddDays(1), first.Snapshot.SnapshotId)).Should().BeNull();

        var correctionRequest = Request(id, account) with
        { CorrectsSnapshotId = first.Snapshot.SnapshotId, CorrectionRationale = "Corrected source observation after review" };
        var corrected = await restarted.CreateClosePriceSetAsync(correctionRequest);
        corrected.Snapshot.Version.Should().Be(2);
        corrected.Snapshot.CorrectsSnapshotId.Should().Be(first.Snapshot.SnapshotId);
        corrected.Snapshot.Lines.Single().ClosePrice.Should().Be(101m);
        var correctedApproved = await restarted.ApproveClosePriceSetAsync(Approve(corrected));
        correctedApproved.Approval!.SnapshotSha256.Should().NotBe(approved.Approval!.SnapshotSha256);
        (await restarted.GetClosePriceSetAsync(account, "book-a", first.Snapshot.ValuationDate, first.Snapshot.SnapshotId)).Should().BeEquivalentTo(approved);
        await restarted.Invoking(s => s.CreateClosePriceSetAsync(correctionRequest)).Should().ThrowAsync<InvalidOperationException>();
        await restartedStore.RetainClosePriceSnapshotAsync(first.Snapshot); // exact retry is harmless
        await restartedStore.Invoking(s => s.RetainClosePriceSnapshotAsync(first.Snapshot with { CreatedBy = "replacement-maker" }))
            .Should().ThrowAsync<InvalidOperationException>();
        await restartedStore.RetainClosePriceApprovalAsync(account, "book-a", first.Snapshot.ValuationDate, approved.Approval);
        await restartedStore.Invoking(s => s.RetainClosePriceApprovalAsync(account, "book-a", first.Snapshot.ValuationDate,
            approved.Approval with { Rationale = "Replace prior review" })).Should().ThrowAsync<InvalidOperationException>();

        await AssertImmutableAsync(first.Snapshot.SnapshotId);
        (await restarted.GetClosePriceSetAsync(account, "book-a", first.Snapshot.ValuationDate, first.Snapshot.SnapshotId)).Should().BeEquivalentTo(approved);
    }

    [SecurityMasterDatabaseFact]
    public async Task StaleAndUnsupportedInputsRetainBlockersUntilLinkedVersionHasSupportedSourceEvidence()
    {
        var id = await SeedSecurityAsync();
        var account = $"account-{Guid.NewGuid():N}";
        var store = new PostgresSecurityMasterPricingStore(fixture.Options);
        var service = Service(store, id);
        await SeedPricingAsync(store, id, account, Close.AddDays(-5));
        var stale = await service.CreateClosePriceSetAsync(Request(id, account));
        stale.Snapshot.Blockers.Select(b => b.Code).Should().Contain("StalePrice");
        await service.Invoking(s => s.ApproveClosePriceSetAsync(Approve(stale))).Should().ThrowAsync<InvalidOperationException>();
        var forcedApproval = new SecurityClosePriceApprovalDto(stale.Snapshot.SnapshotId, SecurityClosePriceEvidence.Digest(stale.Snapshot),
            "checker", "Attempted bypass", DateTimeOffset.UtcNow);
        await store.Invoking(s => s.RetainClosePriceApprovalAsync(account, "book-a", stale.Snapshot.ValuationDate, forcedApproval))
            .Should().ThrowAsync<InvalidOperationException>();
        await store.RecordRawPriceAsync(new(id, "source", 100m, Close.AddHours(-1), "corrector", SecurityPriceUnit.PercentOfPar));
        var corrected = await service.CreateClosePriceSetAsync(Request(id, account) with
        { CorrectsSnapshotId = stale.Snapshot.SnapshotId, CorrectionRationale = "Fresh quote received" });
        corrected.Snapshot.Blockers.Should().BeEmpty();
        (await service.ApproveClosePriceSetAsync(Approve(corrected))).Approval.Should().NotBeNull();
        (await service.GetClosePriceSetAsync(account, "book-a", stale.Snapshot.ValuationDate, stale.Snapshot.SnapshotId))!
            .Snapshot.Blockers.Select(b => b.Code).Should().Contain("StalePrice");

        var unsupportedAccount = $"account-{Guid.NewGuid():N}";
        var unsupported = await service.CreateClosePriceSetAsync(Request(id, unsupportedAccount));
        unsupported.Snapshot.Blockers.Select(b => b.Code).Should().Contain("UnsupportedPrice");
        unsupported.Snapshot.Lines.Single().Observations.Should().NotBeEmpty();
        await service.Invoking(s => s.ApproveClosePriceSetAsync(Approve(unsupported))).Should().ThrowAsync<InvalidOperationException>();
    }

    [SecurityMasterDatabaseFact]
    public async Task StoreRefusesForgedSelectionAndConcurrentCorrectionBranches()
    {
        var id = await SeedSecurityAsync();
        var account = $"account-{Guid.NewGuid():N}";
        var store = new PostgresSecurityMasterPricingStore(fixture.Options);
        await SeedPricingAsync(store, id, account, Close.AddHours(-2));
        var first = await Service(store, id).CreateClosePriceSetAsync(Request(id, account));
        var line = first.Snapshot.Lines.Single();
        var forgedLine = line with
        {
            Selection = line.Selection! with { SelectedSource = "fabricated" },
            Observations = [new("fabricated", 100m, line.Selection!.PriceAsOf, SecurityPriceUnit.PercentOfPar)]
        };
        var forged = first.Snapshot with { SnapshotId = Guid.NewGuid(), Lines = [forgedLine] };
        await store.Invoking(s => s.RetainClosePriceSnapshotAsync(forged)).Should().ThrowAsync<InvalidOperationException>();

        var correction = Request(id, account) with
        { CorrectsSnapshotId = first.Snapshot.SnapshotId, CorrectionRationale = "Concurrent correction" };
        async Task<bool> AttemptAsync()
        {
            try
            { await Service(new PostgresSecurityMasterPricingStore(fixture.Options), id).CreateClosePriceSetAsync(correction); return true; }
            catch (InvalidOperationException) { return false; }
        }
        (await Task.WhenAll(AttemptAsync(), AttemptAsync())).Count(success => success).Should().Be(1);
        await Service(store, id).Invoking(s => s.ApproveClosePriceSetAsync(Approve(first))).Should().ThrowAsync<InvalidOperationException>();
    }

    [SecurityMasterDatabaseFact]
    public async Task ConcurrentIdenticalReviewsReturnTheSameDurableApprovalAndRefuseChangedDecisions()
    {
        var id = await SeedSecurityAsync();
        var account = $"account-{Guid.NewGuid():N}";
        var store = new PostgresSecurityMasterPricingStore(fixture.Options);
        await SeedPricingAsync(store, id, account, Close.AddHours(-2));
        var draft = await Service(store, id).CreateClosePriceSetAsync(Request(id, account));
        var request = Approve(draft);
        var bothInitialReads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readCount = 0;
        var forwardingStore = Substitute.For<ISecurityMasterPricingStore>();
        forwardingStore.GetClosePriceSetAsync(account, "book-a", draft.Snapshot.ValuationDate,
            draft.Snapshot.SnapshotId, Arg.Any<CancellationToken>()).Returns(async call =>
            {
                var cancellation = call.ArgAt<CancellationToken>(4);
                var retained = await store.GetClosePriceSetAsync(account, "book-a", draft.Snapshot.ValuationDate,
                    draft.Snapshot.SnapshotId, cancellation);
                var read = Interlocked.Increment(ref readCount);
                if (read <= 2)
                {
                    retained!.Approval.Should().BeNull();
                    if (read == 2)
                        bothInitialReads.TrySetResult();
                    await bothInitialReads.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellation);
                }
                return retained;
            });
        forwardingStore.RetainClosePriceApprovalAsync(account, "book-a", draft.Snapshot.ValuationDate,
            Arg.Any<SecurityClosePriceApprovalDto>(), Arg.Any<CancellationToken>()).Returns(call =>
                store.RetainClosePriceApprovalAsync(account, "book-a", draft.Snapshot.ValuationDate,
                    call.ArgAt<SecurityClosePriceApprovalDto>(3), call.ArgAt<CancellationToken>(4)));

        var results = await Task.WhenAll(
            Service(forwardingStore, id).ApproveClosePriceSetAsync(request),
            Service(forwardingStore, id).ApproveClosePriceSetAsync(request));
        var durable = await new PostgresSecurityMasterPricingStore(fixture.Options).GetClosePriceSetAsync(
            account, "book-a", draft.Snapshot.ValuationDate, draft.Snapshot.SnapshotId);
        durable!.Approval.Should().NotBeNull();
        results.Should().OnlyContain(result => result.Approval == durable.Approval);
        await forwardingStore.Received(2).RetainClosePriceApprovalAsync(account, "book-a", draft.Snapshot.ValuationDate,
            Arg.Any<SecurityClosePriceApprovalDto>(), Arg.Any<CancellationToken>());
        await Service(store, id).Invoking(s => s.ApproveClosePriceSetAsync(request with { Reviewer = "another-checker" }))
            .Should().ThrowAsync<InvalidOperationException>();
        await Service(store, id).Invoking(s => s.ApproveClosePriceSetAsync(request with { Rationale = "Changed review decision" }))
            .Should().ThrowAsync<InvalidOperationException>();
        (await Service(store, id).GetClosePriceSetAsync(account, "book-a", draft.Snapshot.ValuationDate,
            draft.Snapshot.SnapshotId)).Should().BeEquivalentTo(durable);
    }

    [SecurityMasterDatabaseFact]
    public async Task PriceHistoryAfterRestartRetainsOrderedVersionsAndReviewsInExactAccountBookDateScope()
    {
        var id = await SeedSecurityAsync();
        var account = $"account-{Guid.NewGuid():N}";
        var store = new PostgresSecurityMasterPricingStore(fixture.Options);
        var service = Service(store, id);
        await SeedPricingAsync(store, id, account, Close.AddHours(-2));
        var original = await service.CreateClosePriceSetAsync(Request(id, account));
        var approved = await service.ApproveClosePriceSetAsync(Approve(original));
        await store.RecordRawPriceAsync(new(id, "source", 101m, Close.AddHours(-1), "corrector", SecurityPriceUnit.PercentOfPar));
        var corrected = await service.CreateClosePriceSetAsync(Request(id, account) with
        { CorrectsSnapshotId = original.Snapshot.SnapshotId, CorrectionRationale = "Corrected source observation" });
        var otherAccount = await service.CreateClosePriceSetAsync(Request(id, $"other-{account}"));
        var otherBook = await service.CreateClosePriceSetAsync(Request(id, account) with { BookId = "book-b" });
        var otherDate = await service.CreateClosePriceSetAsync(Request(id, account) with
        { ValuationDate = original.Snapshot.ValuationDate.AddDays(1), EvaluatedAsOf = Close.AddDays(1) });

        var restarted = Service(new PostgresSecurityMasterPricingStore(fixture.Options), id);
        var history = await restarted.GetClosePriceSetsAsync(account, "book-a", original.Snapshot.ValuationDate);
        history.Select(set => set.Snapshot.Version).Should().Equal(1, 2);
        history[0].Should().BeEquivalentTo(approved);
        history[1].Should().BeEquivalentTo(corrected);
        history[0].Snapshot.Lines.Single().ClosePrice.Should().Be(100m);
        history[1].Snapshot.Lines.Single().ClosePrice.Should().Be(101m);
        history[1].Approval.Should().BeNull();
        history[1].Snapshot.CorrectsSnapshotId.Should().Be(history[0].Snapshot.SnapshotId);

        (await restarted.GetClosePriceSetsAsync(otherAccount.Snapshot.AccountId, "book-a", original.Snapshot.ValuationDate))
            .Select(set => set.Snapshot.SnapshotId).Should().Equal(otherAccount.Snapshot.SnapshotId);
        (await restarted.GetClosePriceSetsAsync(account, "book-b", original.Snapshot.ValuationDate))
            .Select(set => set.Snapshot.SnapshotId).Should().Equal(otherBook.Snapshot.SnapshotId);
        (await restarted.GetClosePriceSetsAsync(account, "book-a", otherDate.Snapshot.ValuationDate))
            .Select(set => set.Snapshot.SnapshotId).Should().Equal(otherDate.Snapshot.SnapshotId);
        (await restarted.GetClosePriceSetsAsync(account.ToUpperInvariant(), "book-a", original.Snapshot.ValuationDate)).Should().BeEmpty();

        var correctedApproval = await restarted.ApproveClosePriceSetAsync(Approve(corrected));
        var reloaded = await Service(new PostgresSecurityMasterPricingStore(fixture.Options), id)
            .GetClosePriceSetsAsync(account, "book-a", original.Snapshot.ValuationDate);
        reloaded[0].Should().BeEquivalentTo(approved);
        reloaded[1].Should().BeEquivalentTo(correctedApproval);
    }

    [SecurityMasterDatabaseFact]
    public async Task IdentifiedCaptureRecoversAfterLostResponseRestartAndLaterQuotesWithoutChangingReviewedEvidence()
    {
        var id = await SeedSecurityAsync();
        var account = $"account-{Guid.NewGuid():N}";
        var store = new PostgresSecurityMasterPricingStore(fixture.Options);
        var service = Service(store, id);
        await SeedPricingAsync(store, id, account, Close.AddHours(-2));
        var request = Request(id, account) with { RequestId = Guid.NewGuid() };
        var captured = await service.CreateClosePriceSetAsync(request); // The client loses this response.
        await store.RecordRawPriceAsync(new(id, "source", 101m, Close.AddHours(-1), "corrector", SecurityPriceUnit.PercentOfPar));

        var restarted = Service(new PostgresSecurityMasterPricingStore(fixture.Options), id);
        var recovered = await restarted.CreateClosePriceSetAsync(request);
        recovered.Should().BeEquivalentTo(captured);
        recovered.Snapshot.SnapshotId.Should().Be(request.RequestId!.Value);
        recovered.Snapshot.Lines.Single().ClosePrice.Should().Be(100m);
        recovered.Snapshot.CreationCommandDigest.Should().Be(SecurityClosePriceEvidence.CreationCommandDigest(request));
        var approved = await restarted.ApproveClosePriceSetAsync(Approve(recovered));
        (await Service(new PostgresSecurityMasterPricingStore(fixture.Options), id).CreateClosePriceSetAsync(request))
            .Should().BeEquivalentTo(approved);
        await restarted.Invoking(s => s.CreateClosePriceSetAsync(request with { MaxSourceDispersionPercent = 3m }))
            .Should().ThrowAsync<InvalidOperationException>();
        await restarted.Invoking(s => s.CreateClosePriceSetAsync(request with { AccountId = $"other-{account}" }))
            .Should().ThrowAsync<InvalidOperationException>();
        await restarted.Invoking(s => s.CreateClosePriceSetAsync(request with { BookId = "book-b" }))
            .Should().ThrowAsync<InvalidOperationException>();
        await restarted.Invoking(s => s.CreateClosePriceSetAsync(request with
        { ValuationDate = captured.Snapshot.ValuationDate.AddDays(1), EvaluatedAsOf = Close.AddDays(1) }))
            .Should().ThrowAsync<InvalidOperationException>();
        (await restarted.GetClosePriceSetsAsync(account, "book-a", captured.Snapshot.ValuationDate)).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(approved);
        (await restarted.GetClosePriceSetsAsync($"other-{account}", "book-a", captured.Snapshot.ValuationDate)).Should().BeEmpty();
        (await restarted.GetClosePriceSetsAsync(account, "book-b", captured.Snapshot.ValuationDate)).Should().BeEmpty();
        (await restarted.GetClosePriceSetsAsync(account, "book-a", captured.Snapshot.ValuationDate.AddDays(1))).Should().BeEmpty();

        var correction = request with
        { RequestId = Guid.NewGuid(), CorrectsSnapshotId = captured.Snapshot.SnapshotId, CorrectionRationale = "Corrected vendor observation" };
        var corrected = await restarted.CreateClosePriceSetAsync(correction);
        corrected.Snapshot.Version.Should().Be(2);
        corrected.Snapshot.Lines.Single().ClosePrice.Should().Be(101m);
        (await Service(new PostgresSecurityMasterPricingStore(fixture.Options), id).CreateClosePriceSetAsync(correction))
            .Should().BeEquivalentTo(corrected);
    }

    [SecurityMasterDatabaseFact]
    public async Task ConcurrentIdentifiedCapturesReturnOneCanonicalSnapshot()
    {
        var id = await SeedSecurityAsync();
        var account = $"account-{Guid.NewGuid():N}";
        var store = new PostgresSecurityMasterPricingStore(fixture.Options);
        await SeedPricingAsync(store, id, account, Close.AddHours(-2));
        var request = Request(id, account) with { RequestId = Guid.NewGuid() };
        var bothInitialReads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readCount = 0;
        var forwardingStore = Substitute.For<ISecurityMasterPricingStore>();
        forwardingStore.GetClosePriceSetAsync(account, "book-a", request.ValuationDate,
            request.RequestId!.Value, Arg.Any<CancellationToken>()).Returns(async call =>
            {
                var cancellation = call.ArgAt<CancellationToken>(4);
                var retained = await store.GetClosePriceSetAsync(account, "book-a", request.ValuationDate, request.RequestId.Value, cancellation);
                var read = Interlocked.Increment(ref readCount);
                if (read <= 2)
                {
                    retained.Should().BeNull();
                    if (read == 2)
                        bothInitialReads.TrySetResult();
                    await bothInitialReads.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellation);
                }
                return retained;
            });
        forwardingStore.GetHierarchyAsOfAsync(id, account, Close, Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
            .Returns(call => store.GetHierarchyAsOfAsync(id, account, Close, call.ArgAt<CancellationToken>(3), call.ArgAt<DateTimeOffset?>(4)));
        forwardingStore.GetRawPricesAsync(id, Close, Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
            .Returns(call => store.GetRawPricesAsync(id, Close, call.ArgAt<CancellationToken>(2), call.ArgAt<DateTimeOffset?>(3)));
        forwardingStore.RetainPriceSelectionAsync(Arg.Any<SecurityPriceGoldenCopyDto>(), account, Arg.Any<CancellationToken>())
            .Returns(call => store.RetainPriceSelectionAsync(call.ArgAt<SecurityPriceGoldenCopyDto>(0), account, call.ArgAt<CancellationToken>(2)));
        forwardingStore.RetainClosePriceSnapshotAsync(Arg.Any<SecurityClosePriceSnapshotDto>(), Arg.Any<CancellationToken>())
            .Returns(call => store.RetainClosePriceSnapshotAsync(call.ArgAt<SecurityClosePriceSnapshotDto>(0), call.ArgAt<CancellationToken>(1)));

        var results = await Task.WhenAll(
            Service(forwardingStore, id).CreateClosePriceSetAsync(request),
            Service(forwardingStore, id).CreateClosePriceSetAsync(request));
        results[0].Should().BeEquivalentTo(results[1]);
        var history = await Service(new PostgresSecurityMasterPricingStore(fixture.Options), id)
            .GetClosePriceSetsAsync(account, "book-a", request.ValuationDate);
        history.Should().ContainSingle().Which.Should().BeEquivalentTo(results[0]);
        await forwardingStore.Received(2).RetainClosePriceSnapshotAsync(Arg.Any<SecurityClosePriceSnapshotDto>(), Arg.Any<CancellationToken>());
    }

    [SecurityMasterDatabaseFact]
    public async Task ApprovalGuardRejectsRawSnapshotsMissingBlockersOrCreatedByAndAcceptsWellFormedReview()
    {
        // Migration 043: the database guard must fail closed even when a payload bypasses the C# store
        // and omits the evidence the SQL conditions inspect. A missing "Blockers" array or a missing
        // "CreatedBy" value used to make the OR chain evaluate NULL, so the approval was accepted.
        var id = await SeedSecurityAsync();
        var account = $"account-{Guid.NewGuid():N}";
        var store = new PostgresSecurityMasterPricingStore(fixture.Options);
        await SeedPricingAsync(store, id, account, Close.AddHours(-2));
        var template = (await Service(store, id).CreateClosePriceSetAsync(Request(id, account))).Snapshot;
        template.Blockers.Should().BeEmpty();

        var missingBlockers = await InsertRawSnapshotAsync(template, node => node.Remove("Blockers"));
        await AssertRawApprovalRejectedAsync(missingBlockers);
        var nullBlockers = await InsertRawSnapshotAsync(template, node => node["Blockers"] = null);
        await AssertRawApprovalRejectedAsync(nullBlockers);
        var missingCreatedBy = await InsertRawSnapshotAsync(template, node => node.Remove("CreatedBy"));
        await AssertRawApprovalRejectedAsync(missingCreatedBy);
        var nullCreatedBy = await InsertRawSnapshotAsync(template, node => node["CreatedBy"] = null);
        await AssertRawApprovalRejectedAsync(nullCreatedBy);
        var blankCreatedBy = await InsertRawSnapshotAsync(template, node => node["CreatedBy"] = "  ");
        await AssertRawApprovalRejectedAsync(blankCreatedBy);

        // The same raw path with complete evidence and an independent reviewer still succeeds, so the
        // rejections above come from the hardened conditions rather than from the raw insert itself.
        var wellFormed = await InsertRawSnapshotAsync(template, _ => { });
        await ExecuteRawApprovalAsync(wellFormed, "checker");
        (await CountAsync("security_close_price_approvals", wellFormed.SnapshotId)).Should().Be(1);
        var selfReviewed = await InsertRawSnapshotAsync(template, _ => { });
        await AssertRawApprovalRejectedAsync(selfReviewed, reviewer: $"  {template.CreatedBy.ToUpperInvariant()} ");
        foreach (var rejected in new[] { missingBlockers, nullBlockers, missingCreatedBy, nullCreatedBy, blankCreatedBy, selfReviewed })
            (await CountAsync("security_close_price_approvals", rejected.SnapshotId)).Should().Be(0);
    }

    private sealed record RawSnapshot(Guid SnapshotId, string PayloadSha256);

    private async Task<RawSnapshot> InsertRawSnapshotAsync(SecurityClosePriceSnapshotDto template, Action<JsonObject> mutate)
    {
        var snapshotId = Guid.NewGuid();
        var account = $"raw-{Guid.NewGuid():N}";
        var node = JsonNode.Parse(JsonSerializer.Serialize(template, SecurityMasterClosePricesJsonContext.Default.SecurityClosePriceSnapshotDto))!.AsObject();
        node["SnapshotId"] = snapshotId;
        node["AccountId"] = account;
        mutate(node);
        var payload = node.ToJsonString();
        var digest = Sha256Digest.ComputeUtf8(payload); // The store hashes the exact UTF-8 payload text.
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            insert into {fixture.Options.Schema}.security_close_price_snapshots
                (snapshot_id, account_id, book_id, valuation_date, version, corrects_snapshot_id, payload, payload_sha256)
            values (@id, @account, @book, @date, 1, null, @payload, @digest);
            """;
        command.Parameters.AddWithValue("id", snapshotId);
        command.Parameters.AddWithValue("account", account);
        command.Parameters.AddWithValue("book", template.BookId);
        command.Parameters.AddWithValue("date", template.ValuationDate);
        command.Parameters.AddWithValue("payload", payload);
        command.Parameters.AddWithValue("digest", digest);
        await command.ExecuteNonQueryAsync();
        return new(snapshotId, digest);
    }

    private async Task AssertRawApprovalRejectedAsync(RawSnapshot snapshot, string reviewer = "checker")
    {
        var rejection = await FluentActions.Invoking(() => ExecuteRawApprovalAsync(snapshot, reviewer)).Should().ThrowAsync<PostgresException>();
        rejection.Which.SqlState.Should().Be("23514");
        rejection.Which.MessageText.Should().Be("Close approval requires independent review of an unblocked current immutable snapshot");
    }

    private async Task ExecuteRawApprovalAsync(RawSnapshot snapshot, string reviewer)
    {
        var approval = new SecurityClosePriceApprovalDto(snapshot.SnapshotId, snapshot.PayloadSha256, reviewer,
            "Independently reviewed close source evidence", DateTimeOffset.UtcNow);
        var payload = JsonSerializer.Serialize(approval, SecurityMasterClosePricesJsonContext.Default.SecurityClosePriceApprovalDto);
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            insert into {fixture.Options.Schema}.security_close_price_approvals (snapshot_id, payload, payload_sha256)
            values (@id, @payload, @digest);
            """;
        command.Parameters.AddWithValue("id", snapshot.SnapshotId);
        command.Parameters.AddWithValue("payload", payload);
        command.Parameters.AddWithValue("digest", Sha256Digest.ComputeUtf8(payload));
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(string table, Guid snapshotId)
    {
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"select count(*) from {fixture.Options.Schema}.{table} where snapshot_id = @id";
        command.Parameters.AddWithValue("id", snapshotId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task AssertImmutableAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        foreach (var table in new[] { "security_close_price_snapshots", "security_close_price_approvals" })
            foreach (var sql in new[] { $"update {fixture.Options.Schema}.{table} set payload = payload where snapshot_id = @id",
            $"delete from {fixture.Options.Schema}.{table} where snapshot_id = @id", $"truncate {fixture.Options.Schema}.{table}" })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("id", id);
                await command.Invoking(c => c.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>();
            }
    }

    private static CreateSecurityClosePriceSetRequest Request(Guid id, string account)
        => new(account, "book-a", DateOnly.FromDateTime(Close.UtcDateTime), Close, [new(id)], 2m, "maker");
    private static ApproveSecurityClosePriceSetRequest Approve(SecurityClosePriceSetDto set)
        => new(set.Snapshot.AccountId, set.Snapshot.BookId, set.Snapshot.ValuationDate, set.Snapshot.SnapshotId,
            "checker", "Independently reviewed close source evidence");
    private static SecurityMasterPricingService Service(ISecurityMasterPricingStore store, Guid id)
    {
        var query = Substitute.For<Meridian.Application.SecurityMaster.ISecurityMasterQueryService>();
        query.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new SecurityDetailDto(
            id, "Bond", SecurityStatusDto.Active, "Close test", "USD", JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(new { }), [], [], 1, Close.AddDays(-10), null));
        return new(store, query, NullLogger<SecurityMasterPricingService>.Instance);
    }
    private static async Task SeedPricingAsync(ISecurityMasterPricingStore store, Guid id, string account, DateTimeOffset quoteAsOf)
    {
        await store.UpsertHierarchyAsync(new(id, account, [new(1, "source", "Source", 1)], Close.AddDays(-10), "operator"));
        await store.RecordRawPriceAsync(new(id, "source", 100m, quoteAsOf, "operator", SecurityPriceUnit.PercentOfPar));
    }
    private async Task<Guid> SeedSecurityAsync()
    {
        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            insert into {fixture.Options.Schema}.securities (
                security_id, asset_class, status, display_name, currency, primary_identifier_kind,
                primary_identifier_value, normalized_primary_identifier_value, common_terms,
                asset_specific_terms, provenance, version, effective_from)
            values (@id, 'Bond', 'Active', 'Close test', 'USD', 'InternalCode', @identifier,
                @identifier, jsonb_build_object(), jsonb_build_object(), jsonb_build_object(), 1, now());
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("identifier", id.ToString("N"));
        await command.ExecuteNonQueryAsync();
        return id;
    }
}
