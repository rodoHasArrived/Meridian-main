using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Ledger;
using Meridian.Strategies.Models;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// A report pack states its own trustworthiness. These cover the case that used to slip through:
/// a NAV priced entirely off fabricated marks, citing no simulated strategy run at all, deriving
/// provenance "real" and validating as an approvable deliverable. Provenance is derived from every
/// evidence lane a pack rests on, not just the one that happened to be wired first.
/// </summary>
public sealed class ReportPackProvenanceResolverTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 7, 3, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NavBuiltOnSyntheticMarks_DerivesSimulatedEvenWithNoSimulatedRuns()
    {
        var token = ReportPackProvenanceResolver.ResolveDerivedToken(
            [Run(provenanceToken: "real")],
            ValuationEntries(DataProvenance.Simulated));

        token.Should().Be(
            "simulated",
            "a pack whose balances rest on fabricated marks is not real, however its strategy runs are marked");
    }

    [Fact]
    public void PackBuiltEntirelyOnRealEvidence_StaysReal()
    {
        var token = ReportPackProvenanceResolver.ResolveDerivedToken(
            [Run(provenanceToken: "real")],
            ValuationEntries(DataProvenance.Real));

        token.Should().BeNull("a null token is how this surface says \"real\"");
    }

    [Fact]
    public void SimulatedRunOutranksSeededValuation()
    {
        var token = ReportPackProvenanceResolver.ResolveDerivedToken(
            [Run(provenanceToken: "simulated")],
            ValuationEntries(DataProvenance.Seeded));

        token.Should().Be("simulated", "the pack inherits the strongest non-real claim among its inputs");
    }

    [Fact]
    public void UntaggedValuationEntries_DoNotFabricateANonRealMark()
    {
        // Entries that are not valuation drafts carry no tag and must not be read as simulated.
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(AsOf, "Buy 100 AAPL @ 150",
        [
            (LedgerAccounts.Securities("AAPL"), 15_000m, 0m),
            (LedgerAccounts.Cash, 0m, 15_000m)
        ]);

        ReportPackProvenanceResolver
            .ResolveDerivedToken([Run(provenanceToken: "real")], ledger.GetJournalEntries(new LedgerQuery()))
            .Should().BeNull();
    }

    private static IReadOnlyList<JournalEntry> ValuationEntries(DataProvenance provenance)
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(
            AsOf,
            "Daily fair-value mark for AAPL",
            [
                (LedgerAccounts.Securities("AAPL"), 1_000m, 0m),
                (LedgerAccounts.UnrealizedGain, 0m, 1_000m)
            ],
            new JournalEntryMetadata(
                ActivityType: "fair-value-mark",
                Symbol: "AAPL",
                Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [ValuationProvenanceTag.Key] = provenance.Token()
                }));

        return ledger.GetJournalEntries(new LedgerQuery());
    }

    private static StrategyRunEntry Run(string provenanceToken) => new(
        RunId: "run-1",
        StrategyId: "strategy-1",
        StrategyName: "Strategy One",
        RunType: RunType.Backtest,
        StartedAt: AsOf.AddHours(-1),
        EndedAt: AsOf,
        Metrics: null)
    {
        DataProvenanceToken = provenanceToken
    };
}
