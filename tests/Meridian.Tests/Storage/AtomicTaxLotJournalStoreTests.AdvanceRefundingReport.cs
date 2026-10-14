using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task AdvanceRefunding_PostgresReceiptFeedsSignedReportAndExactReplayAfterClose()
        => AssertAdvanceRefundingReportAsync(amortizeFirst: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task AdvanceRefunding_AmortizedPostgresReceiptRetainsBothBasesInSignedReport()
        => AssertAdvanceRefundingReportAsync(amortizeFirst: true);

    private static async Task AssertAdvanceRefundingReportAsync(bool amortizeFirst)
    {
        var organizationId = Guid.NewGuid();
        await using var fixture = await AmortFixture.CreateAsync(premium: true,
            accountingBasis: AccountingBasisKindDto.Statutory, organizationId: organizationId);
        if (amortizeFirst)
            await fixture.Store.AppendAssetPostingAsync(fixture.Command());

        // Acquisition, the earlier partial disposal, amortization and the reviewed action
        // use one persisted book/basis/organization scope. Reporting cannot repair it on read.
        var command = await SuccessorCommandAsync(fixture, advanceRefunding: true);
        var posted = await fixture.Store.AppendAssetPostingAsync(command);
        posted.IsExactReplay.Should().BeFalse();
        var instruction = command.CorporateAction!;
        instruction.Projection.SourceCorporateActionId.Should().NotBeNull();
        await fixture.LockPeriodAsync();

        var restarted = fixture.Restart();
        var period = (await restarted.GetPeriodAsync(fixture.Period.PeriodId))!;
        var capturedAt = period.ClosedAt!.Value.AddSeconds(1);
        var source = await CreateAdvanceRefundingReportSourceAsync(restarted, fixture.BookId, organizationId, capturedAt);
        var parameters = new ReportingRunParametersDto(
            new ReportingRunScopeDto("amort-fund"), period.PeriodId.ToString("D"),
            instruction.Projection.EconomicEvent!.EffectiveDate,
            new ReportingLedgerBookSelectionDto(fixture.BookId), ReportingAccountingBasisDto.Statutory,
            "USD", ReportingConsolidationLevelDto.Fund, ReportingOutputFormatDto.Pdf, ReportingFinalityDto.Final,
            IncludeSupportingSchedules: true, IncludeEvidenceAppendix: true);
        var access = new ReportAccessQueryContext("reporting-reviewer", CompanyId: "company-alpha",
            TenantId: "tenant-alpha", RequireBoundScope: true);
        var intent = new ReportingAuthoritativeSourceCaptureIntent("capital-account-statement");

        var capture = await source.CaptureAsync(parameters, access, intent);

        var actionRows = capture.DatasetRows.Where(row => row.ContainsKey("openLotSuccessorEvidenceHash")).ToArray();
        actionRows.Should().HaveCount(3, "one parent credit and two child debits must remain separate monetary rows");
        actionRows.Single(row => row["openLotSuccessorRole"] == "Refunded")["openLotScheduleD"].Should().Be("true");
        actionRows.Where(row => row["openLotSuccessorRole"] != "Refunded")
            .Should().OnlyContain(row => row["openLotScheduleD"] == "false");

        capture.CertifiedLedgerPresentation.Should().NotBeNull();
        var pack = capture.CertifiedLedgerPresentation!.ReportPack;
        var artifact = pack.Artifacts.Single(item => item.Name == "corporate-action-lot-evidence.json");
        artifact.ChecksumSha256.Should().Be(Sha256Digest.ComputeUtf8(artifact.Content));
        var reports = JsonSerializer.Deserialize<CanonicalCorporateActionLotReport[]>(artifact.Content)!;
        reports.Should().ContainSingle();
        var report = reports.Single();
        report.JournalEntryId.Should().Be(posted.Journal.Entry.JournalEntryId);
        report.CorporateActionId.Should().Be(command.SourceEventId);
        report.PredecessorBefore.TaxLotRecordId.Should().Be(instruction.ExpectedLot.TaxLotRecordId);
        report.PredecessorAfter.OpenQuantity.Should().Be(0m);
        report.Successors.Should().HaveCount(2);
        report.Successors.Sum(lot => lot.OpenQuantity).Should().Be(6_000m);
        report.Successors.Sum(lot => lot.OpenTransactionCostBasis).Should().Be(amortizeFirst ? 6_300m : 6_600m);
        report.Successors.Sum(lot => lot.OpenFunctionalCostBasis).Should().Be(amortizeFirst ? 6_930m : 7_260m);
        report.Successors.Sum(lot => lot.Acquisition.TransactionCostBasis).Should().Be(6_600m);
        report.Successors.Sum(lot => lot.Acquisition.FunctionalCostBasis).Should().Be(7_260m);
        foreach (var lot in report.Successors)
        {
            lot.AcquiredDate.Should().Be(AmortAcquired);
            lot.Acquisition.HoldingPeriodStartDate.Should().Be(AmortAcquired);
            lot.Acquisition.AcquisitionCurrency.Should().Be("EUR");
            lot.Acquisition.FunctionalCurrency.Should().Be("USD");
            lot.Acquisition.AcquisitionFxRateToFunctional.Should().Be(1.1m);
            lot.Acquisition.CorporateActionLineage.Should().NotBeNull();
            var lineage = lot.Acquisition.CorporateActionLineage!;
            lineage.PredecessorTaxLotRecordId.Should().Be(instruction.ExpectedLot.TaxLotRecordId);
            lineage.SourceCorporateActionId.Should().Be(instruction.Projection.SourceCorporateActionId);
            lineage.ReportingTags.Contains("ScheduleD").Should().Be(lineage.Role == CorporateActionSuccessorRoleDto.Refunded);
            var fraction = lineage.Role == CorporateActionSuccessorRoleDto.Refunded ? 0.6m : 0.4m;
            lot.OpenQuantity.Should().Be(6_000m * fraction);
            lot.OpenTransactionCostBasis.Should().Be((amortizeFirst ? 6_300m : 6_600m) * fraction);
            lot.OpenFunctionalCostBasis.Should().Be((amortizeFirst ? 6_930m : 7_260m) * fraction);
            lot.Acquisition.TransactionCostBasis.Should().Be(6_600m * fraction);
            lot.Acquisition.FunctionalCostBasis.Should().Be(7_260m * fraction);
        }
        var signedPayload = string.Join("\n", pack.Artifacts.OrderBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => $"{item.Name}:{item.ChecksumSha256}"));
        pack.Signature.PayloadChecksumSha256.Should().Be(Sha256Digest.ComputeUtf8(signedPayload));

        // Reconstruct both durable dependencies and Reporting. Exact replay after period close
        // must not append another journal, change the checkpoint or change signed lot evidence.
        var replayStore = fixture.Restart();
        (await replayStore.AppendAssetPostingAsync(command)).IsExactReplay.Should().BeTrue();
        var replaySource = await CreateAdvanceRefundingReportSourceAsync(replayStore, fixture.BookId, organizationId, capturedAt);
        var replayCapture = await replaySource.CaptureAsync(parameters, access, intent);
        JsonElement.DeepEquals(JsonSerializer.SerializeToElement(replayCapture), JsonSerializer.SerializeToElement(capture))
            .Should().BeTrue("restart and exact posting replay must preserve the complete retained report checkpoint and artifacts");
        (await replayStore.GetByPeriodAsync(period.PeriodId)).Should().HaveCount(amortizeFirst ? 2 : 1);
    }

    private static async Task<LedgerReportingAuthoritativeSource> CreateAdvanceRefundingReportSourceAsync(
        PostgresLedgerJournalStore store, Guid bookId, Guid organizationId, DateTimeOffset capturedAt)
    {
        var book = (await store.GetLedgerBookAsync(bookId))!;
        var tenancy = Substitute.For<IFundProfileTenancyRegistry>();
        tenancy.ResolveAsync(book.FundProfileId, Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership(book.FundProfileId, "tenant-alpha", "company-alpha"));
        var structure = Substitute.For<IFundStructureService>();
        var effectiveFrom = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var graph = new OrganizationStructureGraphDto(
            [new OrganizationSummaryDto(organizationId, "REFUND", "Advance refunding owner", "USD", true, effectiveFrom, null, [])],
            [], [], [], [], [], [], [], [],
            [
                new FundStructureNodeDto(organizationId, FundStructureNodeKindDto.Organization,
                    "REFUND", "Advance refunding owner", null, true, effectiveFrom, null),
                new FundStructureNodeDto(book.FundStructureNodeId, FundStructureNodeKindDto.Fund,
                    "FUND", "Advance refunding fund", null, true, effectiveFrom, null)
            ],
            [new OwnershipLinkDto(Guid.NewGuid(), organizationId, book.FundStructureNodeId,
                OwnershipRelationshipTypeDto.Owns, 100m, true, effectiveFrom, null, null)], []);
        structure.GetOrganizationStructureAsync(Arg.Any<OrganizationStructureQuery>(), Arg.Any<CancellationToken>()).Returns(graph);

        // Only tenancy and organization configuration are fixtures. Journals, periods, books,
        // relief history and successor receipts all come from this newly constructed PG store.
        return new LedgerReportingAuthoritativeSource(store, tenancy, structure, new AdvanceRefundingReportClock(capturedAt));
    }

    private sealed class AdvanceRefundingReportClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
