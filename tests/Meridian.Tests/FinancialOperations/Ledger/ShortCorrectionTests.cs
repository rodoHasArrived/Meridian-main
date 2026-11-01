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
        otherLot.Should().Throw<ArgumentException>().WithMessage("*lot its reversal restored*");
        var drift = () => ShortCorrectionGuard.RequireRebook(rebook, reversal, corrected, Entry(ShortLotJournalProjector.Cover(Liability, 4_000m, 3_250m)));
        drift.Should().Throw<ArgumentException>().WithMessage("*exactly the lines projected*");
        var noReversal = () => ShortCorrectionGuard.RequireRebook(rebook with { FollowsReversalId = null }, reversal, corrected, Entry(corrected));
        noReversal.Should().Throw<ArgumentException>().WithMessage("*must follow a retained reversal*");
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

    private static OpenLotDto Lot()
    {
        var recordId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        var acquired = new DateOnly(2026, 9, 30);
        return new OpenLotDto(recordId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "short-acme-1", acquired, 100m, 100m, 10_000m, 10_000m, 3,
            new OpenLotAcquisitionDto(LotQuantityBasis.Units, "USD", "USD", 1m, 10_000m, 10_000m, acquired, null, []) { Direction = OpenLotDirection.Short });
    }

    private static JournalEntry Entry(IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> lines)
    {
        var id = Guid.NewGuid();
        return new JournalEntry(id, At, "Short correction", lines.Select(line => new LedgerEntry(Guid.NewGuid(), id, At, line.account, line.debit, line.credit,
            "Short correction", new LedgerLineDimensionSet(), new LedgerEntryCurrency("USD", "USD", line.debit, line.credit, 1m))).ToArray());
    }
}
