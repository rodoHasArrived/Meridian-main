using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class SecurityTradePositionEvidenceTests
{
    private static readonly DateTimeOffset Time = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CrossingTrade_RetainsSeparateLongAndShortChanges_AndExactBasis()
    {
        SecurityTradePosting[] legs = [new(SecurityTradePostingKind.CloseLong, 10m, 1100m, 1000m),
            new(SecurityTradePostingKind.OpenShort, 5m, 550m)];
        var journal = Build(legs, SecurityLotUnitKind.Shares, 1m);
        SecurityTradePositionEvidence.TryRead(journal, out var effect, out var failure).Should().BeTrue(failure);
        effect.Should().Be(new SecurityTradePositionEffect(-10m, 5m, -1000m, 550m, SecurityLotUnitKind.Shares, 1m));
    }

    [Theory]
    [InlineData(SecurityLotUnitKind.Contracts, 100, 2, 2600)]
    [InlineData(SecurityLotUnitKind.FaceValue, 1, 100000, 101250)]
    public void NativeUnits_AreRetainedWithoutDerivingFromMoney(SecurityLotUnitKind unit, int multiplier, int quantity, int basis)
    {
        var journal = Build([new(SecurityTradePostingKind.OpenLong, quantity, basis)], unit, multiplier);
        SecurityTradePositionEvidence.TryRead(journal, out var effect, out _).Should().BeTrue();
        effect!.QuantityUnit.Should().Be(unit);
        effect.Multiplier.Should().Be(multiplier);
        effect.LongQuantityChange.Should().Be(quantity);
        effect.LongBasisChange.Should().Be(basis);
    }

    [Fact]
    public void ReversalAndSecondReversal_NegateThenRestoreEffects_AndKeepCurrency()
    {
        var journal = Build([new(SecurityTradePostingKind.OpenLong, 2m, 2600m)], SecurityLotUnitKind.Contracts, 100m);
        var reversed = LedgerJournalReversal.Reverse(journal, Guid.NewGuid(), Time.AddMinutes(1));
        var restored = LedgerJournalReversal.Reverse(reversed, Guid.NewGuid(), Time.AddMinutes(2));
        SecurityTradePositionEvidence.TryRead(reversed, out var negative, out _).Should().BeTrue();
        SecurityTradePositionEvidence.TryRead(restored, out var positive, out _).Should().BeTrue();
        negative!.LongQuantityChange.Should().Be(-2m);
        negative.LongBasisChange.Should().Be(-2600m);
        positive!.LongQuantityChange.Should().Be(2m);
        reversed.Lines.Should().OnlyContain(line => line.Currency != null && line.Currency.FunctionalCurrency == "USD");
    }

    [Fact]
    public void AlteredBasisEvidence_CannotMatchActualPostedInventoryMovement()
    {
        var journal = Build([new(SecurityTradePostingKind.OpenLong, 10m, 1000m)], SecurityLotUnitKind.Shares, 1m);
        var tags = new Dictionary<string, string>(journal.Metadata.Tags!) { ["positionEvidence.longBasis"] = "999" };
        var alteredId = Guid.NewGuid();
        var altered = new JournalEntry(alteredId, journal.Timestamp, journal.Description,
            journal.Lines.Select(line => new LedgerEntry(Guid.NewGuid(), alteredId, line.Timestamp, line.Account,
                line.Debit, line.Credit, line.Description)).ToArray(), journal.Metadata with { Tags = tags });
        // Journal line identities are irrelevant to the effect reader; its monetary check is exact.
        SecurityTradePositionEvidence.TryRead(altered, out _, out var reason).Should().BeFalse();
        reason.Should().Contain("carrying");
    }

    [Fact]
    public void LegacyAndCommissionJournals_DoNotInventNativeUnits()
    {
        var ledger = new Meridian.Ledger.Ledger(LedgerPolicy.Trading());
        ledger.PostLines(Time, "fee", SecurityTradePostingProjector.Commission(1m, "account"));
        SecurityTradePositionEvidence.TryRead(ledger.Journal.Single(), out var effect, out _).Should().BeFalse();
        effect.Should().BeNull();
    }

    private static JournalEntry Build(IReadOnlyList<SecurityTradePosting> legs, SecurityLotUnitKind unit, decimal multiplier)
    {
        var metadata = SecurityTradePositionEvidence.WithPostings(new JournalEntryMetadata(Symbol: "TEST", FinancialAccountId: "account"), legs, unit, multiplier);
        var ledger = new Meridian.Ledger.Ledger(LedgerPolicy.Trading());
        ledger.PostLines(Time, "fill", legs.SelectMany(leg => SecurityTradePostingProjector.Project(leg, "TEST", "account")).ToArray(), metadata);
        return ledger.Journal.Single();
    }
}
