using System.Text.Json;
using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage.Ledger;

public sealed partial class RetainedPostingEquivalenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Matches_RebuiltGrossCashFlowPreservesExactAttributionWithoutRewritingIdentities(bool classified)
    {
        var retained = BuildRecord(BuildGrossCashFlowEntry(classified));
        var candidate = BuildWrite(BuildGrossCashFlowEntry(classified));
        var retainedBytes = JsonSerializer.Serialize(retained);
        var candidateBytes = JsonSerializer.Serialize(candidate);
        retained.Entry.JournalEntryId.Should().NotBe(candidate.Entry.JournalEntryId);
        retained.Entry.Lines.Select(line => line.EntryId)
            .Should().NotIntersectWith(candidate.Entry.Lines.Select(line => line.EntryId));

        RetainedPostingEquivalence.Matches(retained, candidate, out var difference).Should().BeTrue();

        difference.Should().BeEmpty();
        JsonSerializer.Serialize(retained).Should().Be(retainedBytes);
        JsonSerializer.Serialize(candidate).Should().Be(candidateBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Matches_RebuiltCashFlowStillRequiresExactMembershipNamesAndAccounting(bool classified)
    {
        foreach (var defect in new[] { "membership", "group-name", "group-case", "line-order", "money", "aggregate", "period", "financial-account", "fund", "unrelated-tag", "missing-evidence" })
        {
            var retained = BuildRecord(BuildGrossCashFlowEntry(classified));
            var candidateEntry = BuildGrossCashFlowEntry(classified,
                changedAmount: defect == "money", changedFund: defect == "fund");
            if (defect == "membership")
                candidateEntry = BuildGrossCashFlowEntry(classified, groups: ["Sale", "Purchase", "Sale", "Purchase"]);
            if (defect is "group-name" or "group-case")
            {
                candidateEntry = EditCashFlowTags(candidateEntry, tags =>
                {
                    foreach (var key in tags.Keys.Where(key => key.StartsWith("cash-flow.group.") && tags[key] == "Sale").ToArray())
                        tags[key] = defect == "group-case" ? "sale" : "Different sale";
                });
            }
            if (defect == "line-order")
                candidateEntry = CopyCashFlowEntry(candidateEntry, candidateEntry.Lines.Reverse().ToArray());
            if (defect == "financial-account")
                candidateEntry = new JournalEntry(candidateEntry.JournalEntryId, candidateEntry.Timestamp,
                    candidateEntry.Description, candidateEntry.Lines,
                    candidateEntry.Metadata with { FinancialAccountId = "broker-other" });
            if (defect == "unrelated-tag")
                candidateEntry = EditCashFlowTags(candidateEntry, tags => tags["sourceEventContentHash"] = "changed");
            if (defect == "missing-evidence")
                candidateEntry = EditCashFlowTags(candidateEntry, tags =>
                {
                    foreach (var key in tags.Keys.Where(IsCashFlowComparisonEvidence).ToArray())
                        tags.Remove(key);
                });
            var candidate = BuildWrite(candidateEntry);
            if (defect == "aggregate")
                candidate = candidate with { AggregateId = Guid.NewGuid() };
            if (defect == "period")
                candidate = candidate with { PeriodId = Guid.NewGuid() };
            var retainedBytes = JsonSerializer.Serialize(retained);
            var candidateBytes = JsonSerializer.Serialize(candidate);

            RetainedPostingEquivalence.Matches(retained, candidate, out var difference)
                .Should().BeFalse("{0} changes the retained cash-flow posting (classified={1})", defect, classified);

            difference.Should().NotBeEmpty();
            JsonSerializer.Serialize(retained).Should().Be(retainedBytes);
            JsonSerializer.Serialize(candidate).Should().Be(candidateBytes);
        }
    }

    [Fact]
    public void Matches_GenericGroupsAndClassifiedInvestingAreDifferentEvidenceVersions()
    {
        var generic = BuildGrossCashFlowEntry(classified: false);
        var investing = BuildGrossCashFlowEntry(classified: true);

        RetainedPostingEquivalence.Matches(BuildRecord(generic), BuildWrite(investing), out _).Should().BeFalse();
        RetainedPostingEquivalence.Matches(BuildRecord(investing), BuildWrite(generic), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("partial-group")]
    [InlineData("unknown-group-version")]
    [InlineData("unknown-namespace")]
    [InlineData("foreign-line")]
    [InlineData("comparison-token-spoof")]
    [InlineData("unbalanced-group")]
    [InlineData("category-only")]
    [InlineData("missing-category")]
    [InlineData("unknown-category-version")]
    [InlineData("unsupported-category")]
    [InlineData("category-case")]
    [InlineData("mixed-group-versions")]
    public void Matches_MalformedCashFlowEvidenceCannotBeHiddenOnEitherOrBothSides(string defect)
    {
        foreach (var malformedSide in new[] { "retained", "candidate", "both" })
        {
            var retainedEntry = BuildGrossCashFlowEntry(classified: true);
            var candidateEntry = BuildGrossCashFlowEntry(classified: true);
            if (malformedSide is "retained" or "both")
                retainedEntry = CorruptCashFlowEvidence(retainedEntry, defect);
            if (malformedSide is "candidate" or "both")
                candidateEntry = CorruptCashFlowEvidence(candidateEntry, defect);
            var retained = BuildRecord(retainedEntry);
            var candidate = BuildWrite(candidateEntry);
            var retainedBytes = JsonSerializer.Serialize(retained);
            var candidateBytes = JsonSerializer.Serialize(candidate);

            RetainedPostingEquivalence.Matches(retained, candidate, out var difference)
                .Should().BeFalse("{0} attribution on {1} is not valid comparison evidence", defect, malformedSide);

            difference.Should().NotBeEmpty();
            JsonSerializer.Serialize(retained).Should().Be(retainedBytes);
            JsonSerializer.Serialize(candidate).Should().Be(candidateBytes);
        }
    }

    private static JournalEntry BuildGrossCashFlowEntry(bool classified, bool changedAmount = false,
        bool changedFund = false, string[]? groups = null)
    {
        var journalId = Guid.NewGuid();
        const string description = "Gross sale and purchase with equal net cash";
        var dimensions = new LedgerLineDimensionSet(FundId: changedFund ? "fund-beta" : "fund-alpha", EntityId: "entity-master");
        var saleAmount = changedAmount ? 101m : 100m;
        LedgerEntry Line(LedgerAccount account, decimal debit, decimal credit)
            => new(Guid.NewGuid(), journalId, Timestamp, account, debit, credit, description, dimensions);
        var entry = new JournalEntry(journalId, Timestamp, description,
        [
            Line(new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "broker-one"), saleAmount, 0m),
            Line(new LedgerAccount("Sale Revenue", LedgerAccountType.Revenue, FinancialAccountId: "broker-one"), 0m, saleAmount),
            Line(new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "broker-one"), 0m, 100m),
            Line(new LedgerAccount("Securities", LedgerAccountType.Asset, FinancialAccountId: "broker-one"), 100m, 0m)
        ], new JournalEntryMetadata
        {
            ActivityType = "compound-cash-flow",
            FinancialAccountId = "broker-one",
            IdempotencyKey = "retained-gross-cash-flow",
            Tags = new Dictionary<string, string> { ["sourceEventContentHash"] = "original" }
        });
        var attributed = LedgerCashFlowAttribution.WithGroups(entry, groups ?? ["Sale", "Sale", "Purchase", "Purchase"]);
        if (!classified)
            return attributed;
        return EditCashFlowTags(attributed, tags =>
        {
            foreach (var line in attributed.Lines)
            {
                var id = line.EntryId.ToString("D");
                tags["cash-flow.group.v2." + id] = tags["cash-flow.group.v1." + id];
                tags.Remove("cash-flow.group.v1." + id);
                tags["cash-flow.category.v1." + id] = "Investing";
            }
        });
    }

    private static JournalEntry CorruptCashFlowEvidence(JournalEntry entry, string defect)
        => EditCashFlowTags(entry, tags =>
        {
            var id = entry.Lines[0].EntryId.ToString("D");
            var groupKey = "cash-flow.group.v2." + id;
            var categoryKey = "cash-flow.category.v1." + id;
            switch (defect)
            {
                case "partial-group":
                    tags.Remove(groupKey);
                    break;
                case "unknown-group-version":
                    tags["cash-flow.group.v9." + id] = tags[groupKey];
                    tags.Remove(groupKey);
                    break;
                case "unknown-namespace":
                    tags["CASH-FLOW.unrecognized.v1"] = "cannot-ignore";
                    break;
                case "foreign-line":
                    tags["cash-flow.group.v2." + Guid.NewGuid().ToString("D")] = tags[groupKey];
                    tags.Remove(groupKey);
                    break;
                case "comparison-token-spoof":
                    tags["cash-flow.group.v2.line:0"] = tags[groupKey];
                    tags.Remove(groupKey);
                    break;
                case "unbalanced-group":
                    tags[groupKey] = "Detached cash";
                    break;
                case "category-only":
                    foreach (var key in tags.Keys.Where(key => key.StartsWith("cash-flow.group.")).ToArray())
                        tags.Remove(key);
                    break;
                case "missing-category":
                    tags.Remove(categoryKey);
                    break;
                case "unknown-category-version":
                    tags["cash-flow.category.v9." + id] = tags[categoryKey];
                    tags.Remove(categoryKey);
                    break;
                case "unsupported-category":
                    tags[categoryKey] = "Operating";
                    break;
                case "category-case":
                    tags[categoryKey] = "investing";
                    break;
                case "mixed-group-versions":
                    tags["cash-flow.group.v1." + id] = tags[groupKey];
                    tags.Remove(groupKey);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(defect), defect, null);
            }
        });

    private static bool IsCashFlowComparisonEvidence(string key)
        => key.StartsWith("cash-flow.", StringComparison.OrdinalIgnoreCase);

    private static JournalEntry EditCashFlowTags(JournalEntry entry, Action<Dictionary<string, string>> edit)
    {
        var tags = entry.Metadata.Tags!.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        edit(tags);
        return new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description, entry.Lines,
            entry.Metadata with { Tags = tags });
    }

    private static JournalEntry CopyCashFlowEntry(JournalEntry entry, IReadOnlyList<LedgerEntry> lines)
        => new(entry.JournalEntryId, entry.Timestamp, entry.Description, lines, entry.Metadata);
}
