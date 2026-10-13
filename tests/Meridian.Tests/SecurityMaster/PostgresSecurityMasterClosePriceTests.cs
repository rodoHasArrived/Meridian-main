using System.Text.Json;
using FluentAssertions;
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
