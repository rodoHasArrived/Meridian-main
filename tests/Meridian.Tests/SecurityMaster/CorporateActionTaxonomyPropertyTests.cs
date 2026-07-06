using FluentAssertions;
using FsCheck.Xunit;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Property-based invariants over the corporate-action taxonomy, lifecycle, and
/// effective-state projector. FsCheck supplies integer seeds; deterministic builders derive
/// the domain values so counterexamples stay reproducible.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CorporateActionTaxonomyPropertyTests
{
    private static readonly char[] Separators = [' ', '-', '.', '_', '/'];

    [Fact]
    public void EveryCanonicalNameAndAlias_NormalizesToItsOwnDescriptor()
    {
        foreach (var descriptor in CorporateActionTypeDescriptorCatalog.All)
        {
            CorporateActionTypeDescriptorCatalog.TryNormalize(descriptor.CanonicalName, out var byName)
                .Should().BeTrue("canonical name '{0}' must resolve", descriptor.CanonicalName);
            byName.CanonicalName.Should().Be(descriptor.CanonicalName);

            foreach (var alias in descriptor.Aliases)
            {
                CorporateActionTypeDescriptorCatalog.TryNormalize(alias, out var byAlias)
                    .Should().BeTrue("alias '{0}' must resolve", alias);
                byAlias.CanonicalName.Should().Be(descriptor.CanonicalName);
            }
        }
    }

    [Property(MaxTest = 200)]
    public void AliasResolution_IsInsensitiveToCasingAndSeparatorNoise(int descriptorSeed, int aliasSeed, int noiseSeed)
    {
        var descriptor = CorporateActionTypeDescriptorCatalog.All[
            Bounded(descriptorSeed, 0, CorporateActionTypeDescriptorCatalog.All.Count - 1)];
        var candidates = descriptor.Aliases.Prepend(descriptor.CanonicalName).ToArray();
        var alias = candidates[Bounded(aliasSeed, 0, candidates.Length - 1)];

        var noisy = ApplyNoise(alias, noiseSeed);

        CorporateActionTypeDescriptorCatalog.TryNormalize(noisy, out var resolved).Should().BeTrue(
            "'{0}' (noised from '{1}') must still resolve", noisy, alias);
        resolved.CanonicalName.Should().Be(descriptor.CanonicalName);
        CorporateActionEventTypes.Normalize(noisy).Should().Be(descriptor.CanonicalName);
    }

    [Property(MaxTest = 200)]
    public void UnknownTokens_NeverResolveAndNeverThrow(int seed)
    {
        // The ZZZQ prefix guarantees the compact token cannot collide with any registered alias.
        var unknown = $"ZZZQ{Math.Abs((long)seed)}";

        CorporateActionTypeDescriptorCatalog.TryNormalize(unknown, out _).Should().BeFalse();
        CorporateActionEventTypes.IsKnown(unknown).Should().BeFalse();
        CorporateActionEventTypes.Normalize(unknown).Should().Be(unknown, "unknown types surface verbatim for rejection reasons");
    }

    [Property(MaxTest = 200)]
    public void LifecycleResolve_NeverMovesBackwards(int stateSeed, int exOffsetSeed, int payOffsetSeed, int asOfSeed)
    {
        string?[] writeableStates =
            [null, CorporateActionLifecycleStates.Announced, CorporateActionLifecycleStates.Confirmed, CorporateActionLifecycleStates.Cancelled];
        var stored = writeableStates[Bounded(stateSeed, 0, writeableStates.Length - 1)];

        var exDate = new DateOnly(2026, 1, 1).AddDays(Bounded(exOffsetSeed, 0, 365));
        DateOnly? payDate = payOffsetSeed % 3 == 0 ? null : exDate.AddDays(Bounded(payOffsetSeed, 1, 30));
        var asOf = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(Bounded(asOfSeed, 0, 500));

        var resolved = CorporateActionLifecycleStates.Resolve(stored, exDate, payDate, asOf);

        if (stored == CorporateActionLifecycleStates.Cancelled)
        {
            resolved.Should().Be(CorporateActionLifecycleStates.Cancelled, "Cancelled is absorbing");
        }
        else
        {
            CorporateActionLifecycleStates.Rank(resolved).Should().BeGreaterThanOrEqualTo(
                CorporateActionLifecycleStates.Rank(stored ?? CorporateActionLifecycleStates.Confirmed),
                "derived lifecycle state must never rank below the stored state");
        }
    }

    [Property(MaxTest = 200)]
    public void SupersedeChains_FoldToTheTip(int chainSeed, int amountSeed)
    {
        var chainLength = Bounded(chainSeed, 2, 5);
        var actions = BuildDividendChain(chainLength, amountSeed, cancelTip: false);
        var asOf = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        var states = CorporateActionEffectiveStateProjector.Project(actions, asOf);

        states.Should().HaveCount(1, "one supersede chain folds to one effective state");
        states[0].Timeline.Should().HaveCount(chainLength, "the timeline retains the full amendment history");
        states[0].Effective.CorpActId.Should().Be(actions[^1].CorpActId, "the chain tip carries the effective terms");
        states[0].Effective.DividendPerShare.Should().Be(actions[^1].DividendPerShare);
    }

    [Property(MaxTest = 200)]
    public void CancelledChains_VanishFromEffectiveActions(int chainSeed, int amountSeed)
    {
        var chainLength = Bounded(chainSeed, 2, 5);
        var actions = BuildDividendChain(chainLength, amountSeed, cancelTip: true);
        var asOf = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        CorporateActionEffectiveStateProjector.ProjectEffectiveActions(actions, asOf)
            .Should().BeEmpty("a cancelled chain contributes no economic effects");
    }

    // ── Deterministic builders ────────────────────────────────────────────────

    private static int Bounded(int seed, int lowInclusive, int highInclusive)
    {
        var span = highInclusive - lowInclusive + 1;
        var normalized = seed % span;
        if (normalized < 0)
            normalized += span;
        return lowInclusive + normalized;
    }

    private static string ApplyNoise(string alias, int noiseSeed)
    {
        var characters = new List<char>(alias.Length * 2);
        for (var i = 0; i < alias.Length; i++)
        {
            var c = alias[i];
            characters.Add(Bounded(noiseSeed + i, 0, 1) == 0 ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c));
            if (Bounded(noiseSeed + (i * 31), 0, 3) == 0)
                characters.Add(Separators[Bounded(noiseSeed + i, 0, Separators.Length - 1)]);
        }

        return new string(characters.ToArray());
    }

    private static IReadOnlyList<CorporateActionDto> BuildDividendChain(int length, int amountSeed, bool cancelTip)
    {
        var securityId = Guid.Parse("88888888-8888-4888-8888-000000000001");
        var exDate = new DateOnly(2026, 5, 12);
        var actions = new List<CorporateActionDto>(length);
        Guid? previousId = null;

        for (var i = 0; i < length; i++)
        {
            var id = DeterministicGuid(amountSeed, i);
            var isTip = i == length - 1;
            actions.Add(new CorporateActionDto(
                CorpActId: id,
                SecurityId: securityId,
                EventType: CorporateActionEventTypes.Dividend,
                ExDate: exDate,
                PayDate: exDate.AddDays(14),
                DividendPerShare: 0.50m + (0.01m * Bounded(amountSeed + i, 0, 50)),
                Currency: "USD",
                SplitRatio: null,
                NewSecurityId: null,
                DistributionRatio: null,
                AcquirerSecurityId: null,
                ExchangeRatio: null,
                SubscriptionPricePerShare: null,
                RightsPerShare: null,
                RecordDate: exDate.AddDays(-1),
                LifecycleState: isTip && cancelTip
                    ? CorporateActionLifecycleStates.Cancelled
                    : i == 0 ? CorporateActionLifecycleStates.Announced : CorporateActionLifecycleStates.Confirmed,
                SupersedesCorpActId: previousId));
            previousId = id;
        }

        return actions;
    }

    private static Guid DeterministicGuid(int seed, int index)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(seed).CopyTo(bytes, 0);
        BitConverter.GetBytes(index).CopyTo(bytes, 4);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
