using System.Text.Json;
using FluentAssertions;
using Meridian.Ledger;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AssetAccountingEventSpineServiceTests
{
    [Fact]
    public async Task FactorPaydown_RegeneratedAttributedDraftIds_PostAndRetryRetainOneCanonicalJournal()
    {
        var harness = BuildFactorPaydownSpineHarness(regenerateCashFlowWrites: true);
        await harness.Service.ProjectAsync(harness.Fixture.Request);
        var drafted = await harness.Service.BuildPostingCandidateAsync(BuildCandidateRequest(harness.Fixture) with
        {
            LotMutation = FactorPaydownLotMutation(harness.Instruction)
        });
        var request = harness.ApprovedPostRequest(drafted.Spine.DraftedCandidate!, drafted.Spine.DraftedCandidateFingerprint!);

        var posted = await harness.PostService.PostCandidateAsync(request);
        var original = JsonSerializer.Serialize(harness.CommittedCommand);
        harness.CandidateMutation = _ => throw new InvalidOperationException("The completed posting must not rebuild from changed live source state.");
        var replay = await harness.PostService.PostCandidateAsync(request);

        posted.WasReplay.Should().BeFalse();
        replay.WasReplay.Should().BeTrue();
        replay.PostedJournal.JournalEntryId.Should().Be(posted.PostedJournal.JournalEntryId);
        replay.JournalImpact.Should().BeEquivalentTo(posted.JournalImpact);
        harness.RebuiltJournals.Should().HaveCount(2, "draft and first posting build authority; exact replay reads immutable posting authority");
        harness.RebuiltJournals.Select(entry => entry.JournalEntryId).Distinct().Should().HaveCount(harness.RebuiltJournals.Count);
        harness.RebuiltJournals.SelectMany(entry => entry.Lines).Select(line => line.EntryId).Distinct()
            .Should().HaveCount(harness.RebuiltJournals.Sum(entry => entry.Lines.Count));
        LedgerCashFlowAttribution.GetComparisonTokens(harness.CommittedCommand!.Journal.Entry).Should().HaveCount(6);
        JsonSerializer.Serialize(harness.CommittedCommand).Should().Be(original);
        harness.Retained.Should().HaveCount(5, "replay must not append another lifecycle fact or journal");
        await harness.Ledger.DidNotReceiveWithAnyArgs().AppendAsync(default!, default);
    }

    [Theory]
    [InlineData("group")]
    [InlineData("category")]
    [InlineData("line-order")]
    [InlineData("money")]
    [InlineData("scope")]
    [InlineData("dimensions")]
    [InlineData("timestamp")]
    public async Task FactorPaydown_ChangedAttributedRetryCannotReinterpretTheRetainedPosting(string mutation)
    {
        var harness = BuildFactorPaydownSpineHarness(regenerateCashFlowWrites: true);
        await harness.Service.ProjectAsync(harness.Fixture.Request);
        var drafted = await harness.Service.BuildPostingCandidateAsync(BuildCandidateRequest(harness.Fixture) with
        {
            LotMutation = FactorPaydownLotMutation(harness.Instruction)
        });
        var request = harness.ApprovedPostRequest(drafted.Spine.DraftedCandidate!, drafted.Spine.DraftedCandidateFingerprint!);
        await harness.PostService.PostCandidateAsync(request);
        var original = JsonSerializer.Serialize(harness.CommittedCommand);
        harness.CommittedJournalMutation = retained =>
        {
            var entry = retained.Entry;
            var tags = entry.Metadata.Tags!.ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (var key in tags.Keys.ToArray())
            {
                if (mutation == "group" && key.StartsWith("cash-flow.group."))
                    tags[key] = "Changed activity";
                if (mutation == "category" && key.StartsWith("cash-flow.category."))
                    tags[key] = "Operating";
            }
            var lines = mutation == "line-order" ? entry.Lines.Reverse().ToArray() : entry.Lines.ToArray();
            if (mutation == "money")
                lines = lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp, line.Account,
                    line.Debit + (line.Account.Name == "Assets:Cash" ? 1m : 0m),
                    line.Credit + (line.Credit > 0m ? 1m : 0m), line.Description, line.Dimensions, line.Currency)).ToArray();
            if (mutation == "dimensions")
                lines = lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp, line.Account,
                    line.Debit, line.Credit, line.Description, line.Dimensions! with { FundId = "foreign-fund" }, line.Currency)).ToArray();
            var timestamp = mutation == "timestamp" ? entry.Timestamp.AddTicks(10) : entry.Timestamp;
            if (mutation == "timestamp")
                lines = lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, timestamp, line.Account,
                    line.Debit, line.Credit, line.Description, line.Dimensions, line.Currency)).ToArray();
            return retained with
            {
                Entry = new JournalEntry(entry.JournalEntryId, timestamp, entry.Description, lines,
                    entry.Metadata with { Tags = tags, FinancialAccountId = mutation == "scope" ? "foreign" : entry.Metadata.FinancialAccountId })
            };
        };

        var retry = () => harness.PostService.PostCandidateAsync(request);

        await retry.Should().ThrowAsync<Exception>().Where(exception =>
            typeof(LedgerValidationException).IsAssignableFrom(exception.GetType())
            || typeof(InvalidOperationException).IsAssignableFrom(exception.GetType()));
        JsonSerializer.Serialize(harness.CommittedCommand).Should().Be(original);
        harness.Retained.Should().HaveCount(5);
        await harness.Ledger.DidNotReceiveWithAnyArgs().AppendAsync(default!, default);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("fingerprint")]
    [InlineData("evidence")]
    public async Task FactorPaydown_CompletedRetryRequiresTheExactDurableAtomicReceipt(string mutation)
    {
        var harness = BuildFactorPaydownSpineHarness(regenerateCashFlowWrites: true);
        await harness.Service.ProjectAsync(harness.Fixture.Request);
        var drafted = await harness.Service.BuildPostingCandidateAsync(BuildCandidateRequest(harness.Fixture) with
        {
            LotMutation = FactorPaydownLotMutation(harness.Instruction)
        });
        var request = harness.ApprovedPostRequest(drafted.Spine.DraftedCandidate!, drafted.Spine.DraftedCandidateFingerprint!);
        await harness.PostService.PostCandidateAsync(request);
        var original = JsonSerializer.Serialize(harness.CommittedCommand);
        harness.MissingAtomicReceipt = mutation == "missing";
        harness.CommittedReceiptMutation = receipt => mutation switch
        {
            "fingerprint" => receipt with { CanonicalFingerprint = "sha256:" + new string('f', 64) },
            "evidence" => receipt with { RetainedEvidence = [] },
            _ => receipt
        };

        var retry = () => harness.PostService.PostCandidateAsync(request);

        await retry.Should().ThrowAsync<Exception>().Where(exception =>
            typeof(LedgerValidationException).IsAssignableFrom(exception.GetType())
            || typeof(InvalidOperationException).IsAssignableFrom(exception.GetType()));
        JsonSerializer.Serialize(harness.CommittedCommand).Should().Be(original);
        harness.RebuiltJournals.Should().HaveCount(2);
        harness.Retained.Should().HaveCount(5);
        await harness.Ledger.DidNotReceiveWithAnyArgs().AppendAsync(default!, default);
    }
}
