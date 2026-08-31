using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;

namespace Meridian.Tests.Application.Reconciliation;

/// <summary>
/// Coverage for the deterministic matching kernel the reconciliation floor (and the future
/// W9-INGEST-009 sided matcher) is built on: stable assignment, bounded same-sign split search,
/// and content-derived identifiers.
/// </summary>
public sealed class ReconciliationMatchKernelTests
{
    private sealed record Pair(string Left, string Right, decimal Score);

    [Fact]
    public void SelectDeterministicAssignment_ConsumesMembersBestFirst()
    {
        var pairs = new[]
        {
            new Pair("a1", "b1", 0.1m),
            new Pair("a1", "b2", 0.2m),
            new Pair("a2", "b1", 0.3m),
            new Pair("a2", "b2", 0.4m)
        };

        var assigned = ReconciliationMatchKernel.SelectDeterministicAssignment(
            pairs.OrderBy(static p => p.Score),
            static p => new[] { p.Left, p.Right });

        assigned.Should().HaveCount(2);
        assigned[0].Should().Be(new Pair("a1", "b1", 0.1m));
        assigned[1].Should().Be(new Pair("a2", "b2", 0.4m), "a2-b1 and a1-b2 overlap already-consumed members");
    }

    [Fact]
    public void SelectDeterministicAssignment_MultiMemberKeysBlockOverlaps()
    {
        var groups = new[]
        {
            (Id: "g1", Members: new[] { "x", "y", "z" }),
            (Id: "g2", Members: new[] { "z", "w" }),
            (Id: "g3", Members: new[] { "w", "v" })
        };

        var assigned = ReconciliationMatchKernel.SelectDeterministicAssignment(groups, static g => g.Members);

        assigned.Select(static g => g.Id).Should().Equal("g1", "g3");
    }

    [Fact]
    public void TryFindSplit_FindsExactSubsetAmongNoise()
    {
        var candidates = new ReconciliationMatchKernel.SplitCandidate[]
        {
            new("legA", 400m),
            new("legB", 601m),
            new("legC", 600m),
            new("legD", 55m)
        };

        var found = ReconciliationMatchKernel.TryFindSplit(1000m, candidates, tolerance: 0m, maxLegs: 4, out var legs, out var residual);

        found.Should().BeTrue();
        legs.Select(static l => l.Id).Should().BeEquivalentTo(["legA", "legC"]);
        residual.Should().Be(0m);
    }

    [Fact]
    public void TryFindSplit_PrefersSmallerResidualThenFewerLegs()
    {
        var candidates = new ReconciliationMatchKernel.SplitCandidate[]
        {
            new("legA", 500m),
            new("legB", 499m),
            new("legC", 300m),
            new("legD", 200m)
        };

        // {A,B} = 999 (residual 1) vs {A,C,D} = 1000 (residual 0): smaller residual wins even with
        // more legs.
        var found = ReconciliationMatchKernel.TryFindSplit(1000m, candidates, tolerance: 1m, maxLegs: 4, out var legs, out var residual);

        found.Should().BeTrue();
        legs.Select(static l => l.Id).Should().BeEquivalentTo(["legA", "legC", "legD"]);
        residual.Should().Be(0m);
    }

    [Fact]
    public void TryFindSplit_NegativeTargetUsesNegativeLegsOnly()
    {
        var candidates = new ReconciliationMatchKernel.SplitCandidate[]
        {
            new("legA", -400m),
            new("legB", -600m),
            new("legC", 1000m)
        };

        var found = ReconciliationMatchKernel.TryFindSplit(-1000m, candidates, tolerance: 0m, maxLegs: 4, out var legs, out var residual);

        found.Should().BeTrue();
        legs.Select(static l => l.Id).Should().BeEquivalentTo(["legA", "legB"]);
        residual.Should().Be(0m);
    }

    [Fact]
    public void TryFindSplit_RejectsSingleLegAndZeroTarget()
    {
        var candidates = new ReconciliationMatchKernel.SplitCandidate[] { new("legA", 1000m), new("legB", 5m) };

        ReconciliationMatchKernel.TryFindSplit(1000m, candidates, tolerance: 0m, maxLegs: 4, out _, out _)
            .Should().BeFalse("a single-leg 'split' is a pair match, not a split");
        ReconciliationMatchKernel.TryFindSplit(0m, candidates, tolerance: 10m, maxLegs: 4, out _, out _)
            .Should().BeFalse("a zero target has no meaningful split");
    }

    [Fact]
    public void TryFindSplit_HonorsMaxLegBudget()
    {
        var candidates = Enumerable.Range(1, 5)
            .Select(static i => new ReconciliationMatchKernel.SplitCandidate($"leg{i}", 200m))
            .ToArray();

        ReconciliationMatchKernel.TryFindSplit(1000m, candidates, tolerance: 0m, maxLegs: 4, out _, out _)
            .Should().BeFalse("reaching 1000 needs five legs but the budget is four");
        ReconciliationMatchKernel.TryFindSplit(800m, candidates, tolerance: 0m, maxLegs: 4, out var legs, out _)
            .Should().BeTrue();
        legs.Should().HaveCount(4);
    }

    [Fact]
    public void TryFindSplit_OnlyLargestCandidatesByMagnitudeParticipate()
    {
        // 24 large legs crowd out the two small ones that would be needed to hit the target.
        var candidates = Enumerable.Range(1, ReconciliationMatchKernel.MaxSplitSearchCandidates)
            .Select(static i => new ReconciliationMatchKernel.SplitCandidate($"big{i:D2}", 100m))
            .Append(new ReconciliationMatchKernel.SplitCandidate("small1", 1m))
            .Append(new ReconciliationMatchKernel.SplitCandidate("small2", 1m))
            .ToArray();

        ReconciliationMatchKernel.TryFindSplit(2m, candidates, tolerance: 0m, maxLegs: 4, out _, out _)
            .Should().BeFalse("the bounded search only considers the largest candidates by magnitude");
    }

    [Fact]
    public void TryFindSplit_TieBreaksOnSortedLegIdSequences()
    {
        var candidates = new ReconciliationMatchKernel.SplitCandidate[]
        {
            new("z", 6m),
            new("a", 4m),
            new("b", 5m),
            new("c", 5m)
        };

        var found = ReconciliationMatchKernel.TryFindSplit(10m, candidates, tolerance: 0m, maxLegs: 2, out var legs, out _);

        found.Should().BeTrue();
        legs.Select(static l => l.Id).Should().BeEquivalentTo(["a", "z"],
            "equal residual and leg count must tie-break on the sorted id sequence, and [a,z] sorts before [b,c]");
    }

    [Fact]
    public void TryFindSplit_AcceptValidator_SelectsBestValidSubset()
    {
        var candidates = new ReconciliationMatchKernel.SplitCandidate[]
        {
            new("p", 5.5m),
            new("q", 4.5m),
            new("r", 6m),
            new("s", 5m)
        };

        var unconstrained = ReconciliationMatchKernel.TryFindSplit(10m, candidates, tolerance: 1m, maxLegs: 2, out var bestByAmount, out _);
        unconstrained.Should().BeTrue();
        bestByAmount.Select(static l => l.Id).Should().BeEquivalentTo(["p", "q"]);

        var found = ReconciliationMatchKernel.TryFindSplit(
            10m,
            candidates,
            tolerance: 1m,
            maxLegs: 2,
            accept: legs => legs.All(static l => l.Id != "p"),
            out var legs,
            out var residual);

        found.Should().BeTrue();
        legs.Select(static l => l.Id).Should().BeEquivalentTo(["q", "r"],
            "a subset rejected by the validator must not shadow the best valid subset (q+r = 10.5 is the closest valid sum)");
        residual.Should().Be(-0.5m);
    }

    [Fact]
    public void CreateDeterministicId_IsStableAndOrderSensitive()
    {
        var first = ReconciliationMatchKernel.CreateDeterministicId("mg", ["seed", "rule", "a", "b"]);
        var again = ReconciliationMatchKernel.CreateDeterministicId("mg", ["seed", "rule", "a", "b"]);
        var reordered = ReconciliationMatchKernel.CreateDeterministicId("mg", ["seed", "rule", "b", "a"]);
        var otherPrefix = ReconciliationMatchKernel.CreateDeterministicId("ev", ["seed", "rule", "a", "b"]);

        again.Should().Be(first);
        reordered.Should().NotBe(first);
        otherPrefix.Should().NotBe(first);
        first.Should().StartWith("mg-");
    }
}
