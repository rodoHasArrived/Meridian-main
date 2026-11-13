using System.Text.Json;
using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AssetAccountingEventSpineServiceTests
{
    [Theory]
    [InlineData("agreement")]
    [InlineData("agreement-revision")]
    [InlineData("account-node")]
    [InlineData("binding-revision")]
    [InlineData("transaction")]
    [InlineData("legal-agreement")]
    [InlineData("legal-version")]
    public async Task FactorPaydown_ReplayRefusesChangedRetainedIntercompanyDimensionBeforeReceiptOrAtomicReplay(string field)
    {
        var harness = BuildFactorPaydownSpineHarness(regenerateCashFlowWrites: true);
        await harness.Service.ProjectAsync(harness.Fixture.Request);
        var drafted = await harness.Service.BuildPostingCandidateAsync(BuildCandidateRequest(harness.Fixture) with
        { LotMutation = FactorPaydownLotMutation(harness.Instruction) });
        var request = harness.ApprovedPostRequest(drafted.Spine.DraftedCandidate!, drafted.Spine.DraftedCandidateFingerprint!);
        await harness.PostService.PostCandidateAsync(request);
        var committed = JsonSerializer.Serialize(harness.CommittedCommand);
        harness.CommittedJournalMutation = record =>
        {
            var entry = record.Entry;
            var lines = entry.Lines.Select(line =>
            {
                var dimensions = line.Dimensions!;
                dimensions = field switch
                {
                    "legal-agreement" => dimensions with { IntercompanyAgreementId = Guid.NewGuid() },
                    "legal-version" => dimensions with { IntercompanyAgreementVersion = 2 },
                    "agreement" => dimensions with { AccountingArrangementId = Guid.NewGuid().ToString("D") },
                    "agreement-revision" => dimensions with { AccountingArrangementRevisionId = Guid.NewGuid().ToString("D") },
                    "account-node" => dimensions with { ChartAccountNodeId = "another-account-node" },
                    "binding-revision" => dimensions with { AccountBindingRevisionId = Guid.NewGuid().ToString("D") },
                    _ => dimensions with { IntercompanyTransactionId = "another-equal-amount-transaction" }
                };
                return new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp, line.Account,
                    line.Debit, line.Credit, line.Description, dimensions, line.Currency);
            }).ToArray();
            return record with { Entry = new(entry.JournalEntryId, entry.Timestamp, entry.Description, lines, entry.Metadata) };
        };

        var retry = () => harness.PostService.PostCandidateAsync(request);

        await retry.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Existing durable journal line * does not match the retained Drafted account, side, amount, currency, or dimensions.");
        await harness.Atomic.Received(1).AppendAssetPostingAsync(Arg.Any<AtomicTaxLotJournalCommand>(), Arg.Any<CancellationToken>());
        JsonSerializer.Serialize(harness.CommittedCommand).Should().Be(committed);
        harness.Retained.Should().HaveCount(5);
    }
}
