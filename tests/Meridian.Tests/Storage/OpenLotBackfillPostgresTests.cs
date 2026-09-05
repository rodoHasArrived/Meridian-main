using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Moq;
using Npgsql;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed class OpenLotBackfillPostgresTests
{
    private static readonly Guid BookOwnerNode = Guid.Parse("77777777-7777-7777-7777-777777777777");
    [LedgerDatabaseFact]
    public async Task SurveyRetainReviewApply_RestoresReadinessAndRetainsRestartSafeEvidenceAndIdempotency()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var legacy = await SeedAsync(database);
        var facts = OpenLotBackfillReconciliationTests.Facts(legacy);
        var store = Store(database, facts);
        var queue = await store.SurveyAsync(legacy.LedgerBookId);
        queue.Should().ContainSingle().Which.Issues.Should().Contain("MissingAcquisitionCurrencyFxEvidence");
        (await store.SurveyAsync(legacy.LedgerBookId)).Single().Version.Should().Be(queue.Single().Version);
        var retained = await store.RetainEvidenceAsync(Retention(facts));
        var apply = Apply(legacy, queue.Single(), retained);
        var pending = () => store.ApplyAsync(apply);
        await pending.Should().ThrowAsync<LedgerValidationException>().WithMessage("*not been reviewed*");
        var selfReview = () => store.ReviewEvidenceAsync(Review(retained) with { Actor = retained.RetainedBy });
        await selfReview.Should().ThrowAsync<LedgerValidationException>().WithMessage("*independent*");
        var automation = () => store.ReviewEvidenceAsync(Review(retained) with { ActionOrigin = OperationsActionOriginDto.AutomationAssistant });
        await automation.Should().ThrowAsync<HumanOperatorRequiredException>();

        await store.ReviewEvidenceAsync(Review(retained));
        var mismatchedOwner = Store(database, facts, positionOwner: "other-entity");
        var wrongOwnerApply = () => mismatchedOwner.ApplyAsync(apply);
        await wrongOwnerApply.Should().ThrowAsync<LedgerValidationException>().WithMessage("*book-position mapping*scope*");
        var normalizedOwner = Store(database, facts, positionOwner: " FUND-ALPHA ");
        var receipt = await normalizedOwner.ApplyAsync(apply);
        var restarted = Store(database, facts);
        (await restarted.ListExceptionsAsync(legacy.LedgerBookId)).Should().BeEmpty();
        (await restarted.GetEvidenceAsync(legacy.LedgerBookId, retained.EvidenceRecordId))!
            .RetainedBy.Should().Be("preparer");
        var current = (await restarted.GetTaxLotsByIdsAsync(legacy.LedgerBookId, [legacy.TaxLotRecordId])).Single();
        current.ToOpenLot().OpenFunctionalCostBasis.Should().Be(1100m);
        current.OriginalQuantity.Should().Be(legacy.OriginalQuantity);
        current.OpenQuantity.Should().Be(legacy.OpenQuantity);
        current.Currency.Should().Be(legacy.Currency);
        current.UnitCost.Should().Be(legacy.UnitCost);
        current.Version.Should().Be(legacy.Version + 1);
        (await restarted.ApplyAsync(apply)).Should().BeEquivalentTo(receipt);
        var collision = () => restarted.ApplyAsync(apply with { Actor = "different-operator" });
        await collision.Should().ThrowAsync<LedgerValidationException>().WithMessage("*idempotency key collides*");

        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var rewrite = connection.CreateCommand();
        rewrite.CommandText = $"update \"{database.Options.SchemaName}\".tax_lots set acquisition_terms = null where tax_lot_record_id = @lot";
        rewrite.Parameters.AddWithValue("lot", legacy.TaxLotRecordId);
        var erase = () => rewrite.ExecuteNonQueryAsync();
        await erase.Should().ThrowAsync<PostgresException>().WithMessage("*immutable*");
    }

    [LedgerDatabaseFact]
    public async Task StaleLotAndReferenceVersions_BlockUntilCurrentSourceIsReviewedAndExceptionRefreshed()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var legacy = await SeedAsync(database);
        var facts = OpenLotBackfillReconciliationTests.Facts(legacy);
        var store = Store(database, facts);
        var queue = (await store.SurveyAsync(legacy.LedgerBookId)).Single();
        var evidence = await store.RetainEvidenceAsync(Retention(facts));
        await store.ReviewEvidenceAsync(Review(evidence));
        var apply = Apply(legacy, queue, evidence);

        var current = await database.JournalStore.SaveTaxLotAsync(legacy with { OpenQuantity = 4.25m });
        var stale = () => store.ApplyAsync(apply);
        await stale.Should().ThrowAsync<LedgerValidationException>().WithMessage("*lot version is stale*");
        (await store.ListExceptionsAsync(legacy.LedgerBookId)).Should().ContainSingle();
        var refreshed = (await store.SurveyAsync(legacy.LedgerBookId)).Single();
        refreshed.Version.Should().Be(queue.Version + 1);
        var changedAuthority = Store(database, facts with { SecurityMasterVersion = facts.SecurityMasterVersion + 1 });
        var staleSecurity = () => changedAuthority.ApplyAsync(apply with
        { ExpectedLotVersion = current.Version, ExpectedExceptionVersion = refreshed.Version });
        await staleSecurity.Should().ThrowAsync<LedgerValidationException>().WithMessage("*Security Master*stale*");

        var newFacts = facts with { SecurityMasterVersion = facts.SecurityMasterVersion + 1 };
        var replacement = await changedAuthority.RetainEvidenceAsync(Retention(newFacts));
        await changedAuthority.ReviewEvidenceAsync(Review(replacement));
        var receipt = await changedAuthority.ApplyAsync(Apply(current, refreshed, replacement));
        receipt.Lot.OpenQuantity.Should().Be(4.25m);
        receipt.Lot.OpenTransactionCostBasis.Should().Be(425m);
        receipt.Lot.OpenFunctionalCostBasis.Should().Be(467.5m);
        (await changedAuthority.ListExceptionsAsync(legacy.LedgerBookId)).Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    public async Task ExceptionResolutionFailure_RollsBackLotAndReceiptAndAllowsSafeRetry()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var legacy = await SeedAsync(database);
        var facts = OpenLotBackfillReconciliationTests.Facts(legacy);
        var store = Store(database, facts);
        var queue = (await store.SurveyAsync(legacy.LedgerBookId)).Single();
        var evidence = await store.RetainEvidenceAsync(Retention(facts));
        await store.ReviewEvidenceAsync(Review(evidence));
        var request = Apply(legacy, queue, evidence);
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var injection = connection.CreateCommand();
        injection.CommandText = $"""
            create function "{database.Options.SchemaName}".test_reject_backfill_resolution() returns trigger
            language plpgsql as $$ begin raise exception 'test resolution write failure'; end $$;
            create trigger test_reject_backfill_resolution before update on "{database.Options.SchemaName}".open_lot_backfill_exceptions
            for each row execute function "{database.Options.SchemaName}".test_reject_backfill_resolution();
            """;
        await injection.ExecuteNonQueryAsync();
        var failing = () => store.ApplyAsync(request);
        await failing.Should().ThrowAsync<PostgresException>().WithMessage("*test resolution write failure*");
        var unchanged = (await store.GetTaxLotsByIdsAsync(legacy.LedgerBookId, [legacy.TaxLotRecordId])).Single();
        unchanged.Acquisition.Should().BeNull();
        unchanged.Version.Should().Be(legacy.Version);
        (await store.ListExceptionsAsync(legacy.LedgerBookId)).Should().ContainSingle();
        injection.CommandText = $"select count(*) from \"{database.Options.SchemaName}\".open_lot_backfill_receipts";
        (await injection.ExecuteScalarAsync()).Should().Be(0L);
        injection.CommandText = $"drop trigger test_reject_backfill_resolution on \"{database.Options.SchemaName}\".open_lot_backfill_exceptions";
        await injection.ExecuteNonQueryAsync();
        (await store.ApplyAsync(request)).ResultingLotVersion.Should().Be(legacy.Version + 1);
    }

    [LedgerDatabaseFact]
    public async Task FullyDisposedLegacyRows_RemainResolvableForHistoricalReportingAndSiblingEvidenceCannotCrossScope()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var closed = await SeedAsync(database, closed: true);
        var facts = OpenLotBackfillReconciliationTests.Facts(closed);
        var store = Store(database, facts);
        var queue = (await store.SurveyAsync(closed.LedgerBookId)).Single();
        var wrongScope = Retention(facts) with { TaxLotRecordId = Guid.NewGuid() };
        var retainWrongScope = () => store.RetainEvidenceAsync(wrongScope);
        await retainWrongScope.Should().ThrowAsync<LedgerValidationException>().WithMessage("*exact ledger book and lot*");
        var evidence = await store.RetainEvidenceAsync(Retention(facts));
        var rejected = await store.ReviewEvidenceAsync(Review(evidence) with { Accepted = false });
        rejected.ReviewStatus.Should().Be("Rejected");
        var rejectedApply = () => store.ApplyAsync(Apply(closed, queue, evidence));
        await rejectedApply.Should().ThrowAsync<LedgerValidationException>().WithMessage("*independent accepted review*");
        (await store.ListExceptionsAsync(closed.LedgerBookId)).Should().ContainSingle();

        var replacement = await store.RetainEvidenceAsync(Retention(facts));
        await store.ReviewEvidenceAsync(Review(replacement));
        var receipt = await store.ApplyAsync(Apply(closed, queue, replacement));
        receipt.Lot.OpenQuantity.Should().Be(0m);
        receipt.Lot.Acquisition.FunctionalCostBasis.Should().Be(1100m);
        (await store.ListExceptionsAsync(closed.LedgerBookId)).Should().BeEmpty();
    }

    private static async Task<LedgerTaxLotRecord> SeedAsync(LedgerPostgresTestDatabase database, bool closed = false)
    {
        var legacy = OpenLotBackfillReconciliationTests.Legacy();
        await database.JournalStore.SaveLedgerBookAsync(new LedgerBookRecord(legacy.LedgerBookId,
            "fund-alpha", BookOwnerNode, FundStructureNodeKindDto.Fund, "Backfill book", "USD", legacy.CreatedAt, legacy.UpdatedAt));
        return await database.JournalStore.SaveTaxLotAsync(closed ? legacy with { OpenQuantity = 0m } : legacy);
    }

    private static PostgresLedgerJournalStore Store(LedgerPostgresTestDatabase database, OpenLotBackfillFactsDto facts,
        string positionOwner = "fund-alpha")
    {
        var json = JsonSerializer.SerializeToElement(new { });
        var security = new Mock<ISecurityMasterStore>();
        security.Setup(s => s.GetProjectionAsync(facts.SecurityId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new SecurityProjectionRecord(facts.SecurityId, "Equity", SecurityStatusDto.Active, "Issuer", "EUR",
                "ISIN", "TEST", json, json, json, facts.SecurityMasterVersion,
                DateTimeOffset.Parse("2020-01-01T00:00:00Z"), null, [], []));
        var positions = new Mock<IInstrumentPositionProjectionStore>();
        positions.Setup(p => p.GetBookPositionAsync(facts.BookPositionId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new BookPositionDto(facts.BookPositionId, facts.SecurityId, Guid.NewGuid(),
                new AccountingBookContextDto(facts.LedgerBookId, positionOwner, BookOwnerNode,
                    FundStructureNodeKindDto.Fund, "Book", "USD", AccountingBasisKindDto.Primary, "policy", "1"),
                "Long", "Active", facts.AcquiredDate, Version: facts.BookPositionVersion));
        return new(database.Options, backfillSecurityMaster: () => security.Object, backfillPositions: () => positions.Object);
    }

    private static RetainOpenLotBackfillEvidenceRequest Retention(OpenLotBackfillFactsDto facts)
    {
        var content = JsonSerializer.SerializeToUtf8Bytes(facts);
        return new(Guid.NewGuid(), facts.LedgerBookId, facts.TaxLotRecordId, "custodian", "statement:2026-01",
            "evidence://custodian/acquisition", content, Sha256Digest.Compute(content), "preparer");
    }

    private static ReviewOpenLotBackfillEvidenceRequest Review(OpenLotBackfillEvidenceDto evidence)
        => new(evidence.LedgerBookId, evidence.EvidenceRecordId, 1, true, "reviewer",
            "Reconciled acquisition basis and verified Security Master mapping.", OperationsActionOriginDto.HumanOperator);

    private static ApplyOpenLotBackfillRequest Apply(LedgerTaxLotRecord lot, OpenLotBackfillExceptionDto exception,
        OpenLotBackfillEvidenceDto evidence)
        => new(lot.LedgerBookId, lot.TaxLotRecordId, lot.Version, exception.Version, evidence.EvidenceRecordId,
            2, "backfill:" + evidence.EvidenceRecordId, "operator", OperationsActionOriginDto.HumanOperator);
}
