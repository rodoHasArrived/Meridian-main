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
        var replay = await harness.PostService.PostCandidateAsync(request);

        posted.WasReplay.Should().BeFalse();
        replay.WasReplay.Should().BeTrue();
        replay.PostedJournal.JournalEntryId.Should().Be(posted.PostedJournal.JournalEntryId);
        harness.RebuiltJournals.Count.Should().BeGreaterThanOrEqualTo(3);
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
        harness.CandidateMutation = entry =>
        {
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
            return new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description, lines,
                entry.Metadata with { Tags = tags, FinancialAccountId = mutation == "scope" ? "foreign" : entry.Metadata.FinancialAccountId });
        };

        var retry = () => harness.PostService.PostCandidateAsync(request);

        await retry.Should().ThrowAsync<Exception>().Where(exception =>
            typeof(LedgerValidationException).IsAssignableFrom(exception.GetType())
            || typeof(InvalidOperationException).IsAssignableFrom(exception.GetType()));
        JsonSerializer.Serialize(harness.CommittedCommand).Should().Be(original);
        harness.Retained.Should().HaveCount(5);
        await harness.Ledger.DidNotReceiveWithAnyArgs().AppendAsync(default!, default);
    }
}
