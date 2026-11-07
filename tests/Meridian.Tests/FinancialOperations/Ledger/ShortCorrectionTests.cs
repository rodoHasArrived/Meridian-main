using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class ShortCorrectionTests
{
    private static readonly LedgerAccount Liability = LedgerAccounts.ShortSecuritiesPayable("ACME", "broker-1");
    private static readonly DateTimeOffset At = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Mutation = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    [Fact]
    public void Reversal_IsTheExactInverse_AndRefusesRepricedOrPartialInverses()
    {
        var posted = Entry(ShortLotJournalProjector.Cover(Liability, 4_000m, 3_200m));
        var receipt = Receipt(ShortCorrectionKind.Reversal, posted.JournalEntryId);

        var inverse = ShortLotJournalProjector.Reverse(posted.Lines);
        inverse.Should().BeEquivalentTo(new[]
        {
            (Liability, 0m, 4_000m),
            (LedgerAccounts.CashFor("broker-1"), 3_200m, 0m),
            (LedgerAccounts.RealizedGainOrDefault("broker-1"), 800m, 0m)
        });
        ShortCorrectionGuard.RequireReversal(receipt, posted, Entry(inverse));

        var repriced = () => ShortCorrectionGuard.RequireReversal(receipt, posted, Entry(ShortLotJournalProjector.Reverse(ShortLotJournalProjector.Cover(Liability, 4_000m, 3_300m))));
        repriced.Should().Throw<ArgumentException>().WithMessage("*exact inverse*");
        var partial = () => ShortCorrectionGuard.RequireReversal(receipt, posted, Entry(inverse.Take(2).ToArray()));
        partial.Should().Throw<ArgumentException>().WithMessage("*exact inverse*");
        var sameJournal = () => ShortCorrectionGuard.RequireReversal(receipt, posted, posted);
        sameJournal.Should().Throw<ArgumentException>().WithMessage("*is a new journal*");
        var otherJournal = () => ShortCorrectionGuard.RequireReversal(receipt with { CorrectsJournalEntryId = Guid.NewGuid() }, posted, Entry(inverse));
        otherJournal.Should().Throw<ArgumentException>().WithMessage("*exact posted journal*");
    }

    [Fact]
    public void Reversal_RefusesRedatedInverses_AndInversesThatDropDimensionsOrCurrencyDetail()
    {
        var posted = Entry(ShortLotJournalProjector.Cover(Liability, 4_000m, 3_200m), dimensions: new LedgerLineDimensionSet { PositionId = Guid.NewGuid() });
        var receipt = Receipt(ShortCorrectionKind.Reversal, posted.JournalEntryId);
        var inverse = ShortLotJournalProjector.Reverse(posted.Lines);
        var dimensions = posted.Lines[0].Dimensions;
        ShortCorrectionGuard.RequireReversal(receipt, posted, Entry(inverse, at: At.AddDays(3), dimensions: dimensions));

        var otherPeriod = () => ShortCorrectionGuard.RequireReversal(receipt, posted, Entry(inverse, effectiveDate: new DateOnly(2026, 12, 1), dimensions: dimensions));
        otherPeriod.Should().Throw<ArgumentException>().WithMessage("*re-dated reversals are refused*");
        var backdated = () => ShortCorrectionGuard.RequireReversal(receipt, posted, Entry(inverse, at: At.AddDays(-1), dimensions: dimensions));
        backdated.Should().Throw<ArgumentException>().WithMessage("*re-dated reversals are refused*");
        var droppedDimensions = () => ShortCorrectionGuard.RequireReversal(receipt, posted, Entry(inverse));
        droppedDimensions.Should().Throw<ArgumentException>().WithMessage("*exact inverse*");
        var otherCurrency = () => ShortCorrectionGuard.RequireReversal(receipt, posted,
            Entry(inverse, dimensions: dimensions, currency: line => new LedgerEntryCurrency("USD", "GBP", line.debit, line.credit, 1m)));
        otherCurrency.Should().Throw<ArgumentException>().WithMessage("*exact inverse*");
        var noCurrency = () => ShortCorrectionGuard.RequireReversal(receipt, posted, Entry(inverse, dimensions: dimensions, currency: _ => null));
        noCurrency.Should().Throw<ArgumentException>().WithMessage("*exact inverse*");
    }

    [Fact]
    public void Reversal_RejectsBalancedDuplicateLineSubstitution_WithoutChangingPostedJournal()
    {
        // Two distinct posted liability credits have the same economics. The old count-based
        // check incorrectly reused one reversal liability debit to satisfy both credits.
        var cash = LedgerAccounts.CashFor("broker-1");
        var posted = Entry([(Liability, 0m, 100m), (Liability, 0m, 100m), (cash, 200m, 0m)]);
        var originalLines = posted.Lines.ToArray();
        var receipt = Receipt(ShortCorrectionKind.Reversal, posted.JournalEntryId);
        var wrong = Entry([
            (Liability, 100m, 0m),
            (LedgerAccounts.Securities("ACME", "broker-1"), 100m, 0m),
            (cash, 0m, 200m)
        ]);

        posted.IsBalanced.Should().BeTrue();
        wrong.IsBalanced.Should().BeTrue();
        var attempt = () => ShortCorrectionGuard.RequireReversal(receipt, posted, wrong);
        attempt.Should().Throw<ArgumentException>().WithMessage("*exact inverse*");
        posted.Lines.Should().Equal(originalLines);
    }

    [Fact]
    public void Reversal_AcceptsBalancedReorderedDistinctInversesOfDuplicateLines()
    {
        var cash = LedgerAccounts.CashFor("broker-1");
        var posted = Entry([(Liability, 0m, 100m), (Liability, 0m, 100m), (cash, 200m, 0m)]);
        var originalLines = posted.Lines.ToArray();
        var inverse = ShortLotJournalProjector.Reverse(posted.Lines);
        var reordered = Entry([inverse[2], inverse[1], inverse[0]]);

        posted.IsBalanced.Should().BeTrue();
        reordered.IsBalanced.Should().BeTrue();
        ShortCorrectionGuard.RequireReversal(Receipt(ShortCorrectionKind.Reversal, posted.JournalEntryId), posted, reordered);
        posted.Lines.Should().Equal(originalLines);
    }

    [Fact]
    public void Reversal_RefusesUnmatchedAmountsAndIncorrectAccountsEvenWhenBalanced()
    {
        var cash = LedgerAccounts.CashFor("broker-1");
        var posted = Entry([(Liability, 0m, 100m), (Liability, 0m, 150m), (cash, 250m, 0m)]);
        var receipt = Receipt(ShortCorrectionKind.Reversal, posted.JournalEntryId);
        var unmatched = Entry([(Liability, 100m, 0m), (Liability, 140m, 0m), (cash, 0m, 240m)]);
        var wrongAccount = Entry([
            (Liability, 100m, 0m),
            (LedgerAccounts.Securities("ACME", "broker-1"), 150m, 0m),
            (cash, 0m, 250m)
        ]);

        posted.IsBalanced.Should().BeTrue();
        unmatched.IsBalanced.Should().BeTrue();
        wrongAccount.IsBalanced.Should().BeTrue();
        Action unmatchedAttempt = () => ShortCorrectionGuard.RequireReversal(receipt, posted, unmatched);
        Action wrongAccountAttempt = () => ShortCorrectionGuard.RequireReversal(receipt, posted, wrongAccount);
        unmatchedAttempt.Should().Throw<ArgumentException>().WithMessage("*exact inverse*");
        wrongAccountAttempt.Should().Throw<ArgumentException>().WithMessage("*exact inverse*");
    }

    [Fact]
    public void Reversal_RequiresIdenticalTransactionCurrencyAmountsFxAndDimensions()
    {
        var cash = LedgerAccounts.CashFor("broker-1");
        var dimensions = new LedgerLineDimensionSet { PositionId = PositionId };
        var lines = new[] { (Liability, 0m, 120m), (cash, 120m, 0m) };
        var posted = Entry(lines, dimensions: dimensions,
            currency: line => new LedgerEntryCurrency("EUR", "USD", line.debit / 1.2m, line.credit / 1.2m, 1.2m));
        var receipt = Receipt(ShortCorrectionKind.Reversal, posted.JournalEntryId);
        var inverse = ShortLotJournalProjector.Reverse(posted.Lines);
        var correct = Entry(inverse, dimensions: dimensions,
            currency: line => new LedgerEntryCurrency("EUR", "USD", line.debit / 1.2m, line.credit / 1.2m, 1.2m));
        ShortCorrectionGuard.RequireReversal(receipt, posted, correct);

        var wrongCurrency = Entry(inverse, dimensions: dimensions,
            currency: line => new LedgerEntryCurrency("GBP", "USD", line.debit / 1.2m, line.credit / 1.2m, 1.2m));
        // Small FX and transaction-amount drifts are within the ledger's functional rounding
        // tolerance; a reversal must still reject them because the evidence is not identical.
        var wrongFx = Entry(inverse, dimensions: dimensions,
            currency: line => new LedgerEntryCurrency("EUR", "USD", line.debit / 1.2m, line.credit / 1.2m, 1.20001m));
        var wrongTransactionAmount = Entry(inverse, dimensions: dimensions,
            currency: line => new LedgerEntryCurrency("EUR", "USD",
                line.debit > 0m ? line.debit / 1.2m + 0.001m : 0m,
                line.credit > 0m ? line.credit / 1.2m + 0.001m : 0m, 1.2m));
        var wrongPosition = Entry(inverse, dimensions: new LedgerLineDimensionSet { PositionId = Guid.NewGuid() },
            currency: line => new LedgerEntryCurrency("EUR", "USD", line.debit / 1.2m, line.credit / 1.2m, 1.2m));

        posted.IsBalanced.Should().BeTrue();
        correct.IsBalanced.Should().BeTrue();
        foreach (var invalid in new[] { wrongCurrency, wrongFx, wrongTransactionAmount, wrongPosition })
        {
            invalid.IsBalanced.Should().BeTrue();
            Action attempt = () => ShortCorrectionGuard.RequireReversal(receipt, posted, invalid);
            attempt.Should().Throw<ArgumentException>().WithMessage("*exact inverse*");
        }
    }

    [Fact]
    public void Receipts_RequireAValidRestoredLot()
    {
        var receipt = Receipt(ShortCorrectionKind.Reversal, Guid.NewGuid());
        (receipt with { RestoresLot = receipt.RestoresLot with { LotId = " " } }).Validate().Should().Contain(i => i.Contains("not a valid canonical open lot"));
        (receipt with { RestoresLot = receipt.RestoresLot with { OpenQuantity = 500m } }).Validate().Should().Contain(i => i.Contains("not a valid canonical open lot"));
        (receipt with { RestoresLot = receipt.RestoresLot with { Acquisition = receipt.RestoresLot.Acquisition with { Evidence = [] } } }).Validate()
            .Should().Contain(i => i.Contains("not a valid canonical open lot"));
        (receipt with { RestoresLot = receipt.RestoresLot with { Acquisition = null! } }).Validate().Should().Contain(i => i.Contains("Short lots only"));
    }

    [Fact]
    public void Rebook_FollowsItsReversal_AndCarriesExactlyTheCorrectedProjection()
    {
        var posted = Entry(ShortLotJournalProjector.Cover(Liability, 4_000m, 3_200m));
        var reversal = Receipt(ShortCorrectionKind.Reversal, posted.JournalEntryId);
        var corrected = ShortLotJournalProjector.Cover(Liability, 4_000m, 3_240m);
        var rebook = Receipt(ShortCorrectionKind.Rebook, posted.JournalEntryId) with { FollowsReversalId = reversal.CorrectionId };

        ShortCorrectionGuard.RequireRebook(rebook, reversal, corrected, Entry(corrected));

        var unlinked = () => ShortCorrectionGuard.RequireRebook(rebook with { FollowsReversalId = Guid.NewGuid() }, reversal, corrected, Entry(corrected));
        unlinked.Should().Throw<ArgumentException>().WithMessage("*follow the reversal of the same corrected mutation*");
        var otherLot = () => ShortCorrectionGuard.RequireRebook(rebook with { RestoresLot = rebook.RestoresLot with { TaxLotRecordId = Guid.NewGuid() } }, reversal, corrected, Entry(corrected));
        // Evidence binds the durable lot identity, so a different record id is no longer a valid canonical lot at all.
        otherLot.Should().Throw<ArgumentException>().WithMessage("*not a valid canonical open lot*");
        var drift = () => ShortCorrectionGuard.RequireRebook(rebook, reversal, corrected, Entry(ShortLotJournalProjector.Cover(Liability, 4_000m, 3_250m)));
        drift.Should().Throw<ArgumentException>().WithMessage("*exactly the lines projected*");
        var noReversal = () => ShortCorrectionGuard.RequireRebook(rebook with { FollowsReversalId = null }, reversal, corrected, Entry(corrected));
        noReversal.Should().Throw<ArgumentException>().WithMessage("*must follow a retained reversal*");

        var otherJournal = () => ShortCorrectionGuard.RequireRebook(rebook with { CorrectsJournalEntryId = Guid.NewGuid() }, reversal, corrected, Entry(corrected));
        otherJournal.Should().Throw<ArgumentException>().WithMessage("*same corrected mutation and journal*");
        var reusedIdentity = () => ShortCorrectionGuard.RequireRebook(rebook with { CorrectionId = reversal.CorrectionId }, reversal, corrected, Entry(corrected));
        reusedIdentity.Should().Throw<ArgumentException>().WithMessage("*cannot reuse the reversal's identity*");
        var intoTheOriginal = () => ShortCorrectionGuard.RequireRebook(rebook, reversal, corrected, posted);
        intoTheOriginal.Should().Throw<ArgumentException>().WithMessage("*cannot reuse the reversal's identity or the corrected journal*");
        var changedSnapshot = () => ShortCorrectionGuard.RequireRebook(rebook with { RestoresLot = rebook.RestoresLot with { OpenQuantity = 99m } }, reversal, corrected, Entry(corrected));
        changedSnapshot.Should().Throw<ArgumentException>().WithMessage("*complete restored snapshot unchanged*");
    }

    [Fact]
    public void Receipts_RefuseSelfCorrection_LongLots_AndMissingAttribution()
    {
        Receipt(ShortCorrectionKind.Reversal, Guid.NewGuid()).Validate().Should().BeEmpty();
        (Receipt(ShortCorrectionKind.Reversal, Guid.NewGuid()) with { CorrectionId = Mutation }).Validate().Should().Contain(i => i.Contains("cannot correct itself"));
        (Receipt(ShortCorrectionKind.Reversal, Guid.NewGuid()) with { Actor = " " }).Validate().Should().Contain(i => i.Contains("attributed actor"));
        var longLot = Receipt(ShortCorrectionKind.Reversal, Guid.NewGuid());
        (longLot with { RestoresLot = longLot.RestoresLot with { Acquisition = longLot.RestoresLot.Acquisition with { Direction = OpenLotDirection.Long } } })
            .Validate().Should().Contain(i => i.Contains("Short lots only"));
        (Receipt(ShortCorrectionKind.Reversal, Guid.NewGuid()) with { FollowsReversalId = Guid.NewGuid() }).Validate().Should().Contain(i => i.Contains("follows no other"));
    }

    [Fact]
    public void TaxCharacter_IsAnExplicitAttestation_NeverInferredFromLotAge()
    {
        var absent = () => ShortCorrectionGuard.RequireCharacter(null);
        absent.Should().Throw<ArgumentException>().WithMessage("*opening-lot age cannot establish short-sale tax character*");

        var attestation = new ShortSaleCharacterAttestation(ShortSaleTaxCharacter.ShortTerm, "tax-reviewer", At,
            "Substantially identical property held fewer than twelve months on the cover date; broker 1099-B review.", [Evidence("tax-memo")]);
        ShortCorrectionGuard.RequireCharacter(attestation).Character.Should().Be(ShortSaleTaxCharacter.ShortTerm);

        (attestation with { Justification = "inferred" }).Validate().Should().ContainSingle().Which.Should().Contain("substantive justification");
        (attestation with { Evidence = [] }).Validate().Should().ContainSingle().Which.Should().Contain("complete retained evidence");
        (attestation with { ReviewedAtUtc = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-5)) }).Validate().Should().ContainSingle().Which.Should().Contain("UTC");
        var incomplete = () => ShortCorrectionGuard.RequireCharacter(attestation with { ReviewedBy = "" });
        incomplete.Should().Throw<ArgumentException>().WithMessage("*attestation is incomplete*");
    }

    private static ShortCorrectionReceipt Receipt(ShortCorrectionKind kind, Guid journalEntryId) => new(
        Guid.NewGuid(), kind, Mutation, journalEntryId, Lot(), "Execution price keyed from the wrong fill; reviewed against the broker confirm.", "controller", At);

    private static RetainedEvidenceIdentityDto Evidence(string name) => new("ev-" + name, "evidence://short/" + name, new string('b', 64), "Broker",
        "doc-" + name, "Accepted", "controller", At, new DateOnly(2026, 10, 15), 1, At, "controller", "ShortSaleCharacter", name);

    private static readonly Guid SecurityId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid PositionId = Guid.Parse("dddddddd-0000-0000-0000-000000000004");
    private static readonly Guid BookId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000005");

    private static OpenLotDto Lot()
    {
        var recordId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        var acquired = new DateOnly(2026, 9, 30);
        var at = new DateTimeOffset(acquired.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var evidence = new RetainedEvidenceIdentityDto("ev-open", "evidence://short/open", new string('a', 64), "Broker", "fill-open",
            "Accepted", "controller", at, acquired, 1, at, "controller", "OpenLotAcquisition", recordId.ToString("D"));
        return new OpenLotDto(recordId, SecurityId, PositionId, BookId, "short-acme-1", acquired, 100m, 100m, 10_000m, 10_000m, 3,
            new OpenLotAcquisitionDto(LotQuantityBasis.Units, "USD", "USD", 1m, 10_000m, 10_000m, acquired, null, [evidence]) { Direction = OpenLotDirection.Short });
    }

    private static JournalEntry Entry(IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> lines, DateOnly? effectiveDate = null,
        DateTimeOffset? at = null, LedgerLineDimensionSet? dimensions = null, Func<(LedgerAccount account, decimal debit, decimal credit), LedgerEntryCurrency?>? currency = null)
    {
        var id = Guid.NewGuid();
        var timestamp = at ?? At;
        currency ??= line => new LedgerEntryCurrency("USD", "USD", line.debit, line.credit, 1m);
        return new JournalEntry(id, timestamp, "Short correction", lines.Select(line => new LedgerEntry(Guid.NewGuid(), id, timestamp, line.account, line.debit, line.credit,
            "Short correction", dimensions ?? new LedgerLineDimensionSet(), currency(line))).ToArray(),
            new JournalEntryMetadata(EffectiveDate: effectiveDate ?? new DateOnly(2026, 11, 1)));
    }
}
