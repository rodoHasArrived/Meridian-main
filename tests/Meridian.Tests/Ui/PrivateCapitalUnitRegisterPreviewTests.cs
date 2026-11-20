using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class PrivateCapitalUnitRegisterPreviewTests
{
    private static readonly Guid BookId = new("2be424e9-9fb6-4bb2-9d2e-df123c299e80");
    private static readonly Guid EntityId = new("e1e87591-0d7b-4bbc-9f89-0b0fce3d3aa9");
    private static readonly Guid PeriodId = new("6845ac04-b0bb-4997-bff9-c7b3c01a602b");
    private static readonly DateOnly Inception = new(2026, 1, 1);
    private static readonly DateOnly Cutoff = new(2026, 1, 31);
    private static readonly PrivateCapitalScopeDto Scope = new("tenant-a", "company-a", EntityId.ToString("D"),
        "fund-a", BookId, "USD");
    private static readonly LedgerAccountingPeriod Period = new(PeriodId, BookId, 2026, 1, "January",
        Inception, Cutoff, "HardClosed", At(Inception), At(Cutoff), 3);

    [Fact]
    public void TiesPostedDealingAndNetAssetsWithPartialAndFullRedemptionBeforeResubscription()
    {
        var subscribed = CashJournal(1, "Subscription", "investor-a", 1000m);
        var gained = Record(2, (LedgerAccounts.Cash, 200m, 0m), (LedgerAccounts.RealizedGain, 0m, 200m));
        var partial = CashJournal(3, "Redemption", "investor-a", -240m);
        var full = CashJournal(4, "Redemption", "investor-a", -960m);
        var later = CashJournal(5, "Subscription", "investor-b", 600m);
        var journals = new[] { subscribed, gained, partial, full, later };
        var source = Source(600m, Move(later, 10m, 600m, 60m), Move(full, 12m, 960m, 80m),
            Move(partial, 12m, 240m, 20m), Move(subscribed, 10m, 1000m, 100m));

        var register = Preview(journals.Reverse().ToArray(), source);

        register.IsTied.Should().BeTrue();
        register.UnitsOutstanding.Should().Be(60m);
        register.ClassNav.Should().Be(600m);
        register.NavPerUnit.Should().Be(10m);
        register.HighWaterNavPerUnit.Should().Be(12m);
        register.ValuationRoundingResidual.Should().Be(0m);
        register.Holdings.Single(x => x.InvestorId == "investor-a").Units.Should().Be(0m);
        register.Holdings.Single(x => x.InvestorId == "investor-b").Value.Should().Be(600m);
        register.Movements.Select(x => x.TransactionId).Should().Equal(
            subscribed.Entry.JournalEntryId, partial.Entry.JournalEntryId, full.Entry.JournalEntryId,
            later.Entry.JournalEntryId);
        register.SourceHashSha256.Should().Be("source-hash");
        register.CloseSnapshotHashSha256.Should().Be("close-hash");
    }

    [Fact]
    public void FlagsMissingJournalSettlementAndNavSeparately()
    {
        var missing = CashJournal(1, "Subscription", "investor-a", 100m);
        var other = CashJournal(2, "Subscription", "investor-b", 100m);
        var fake = CashJournal(3, "Subscription", "investor-c", 100m);
        var source = Source(95m, Move(other, 10m, 100m, 10m) with { CapitalAccountId = "foreign-capital" },
            Move(fake, 10m, 100m, 10m));

        var register = Preview([missing, other], source);

        register.IsTied.Should().BeFalse();
        register.Movements.Should().BeEmpty();
        register.Issues.Select(issue => issue.Code).Should().Contain([
            "private-capital.unit-register.missing-movement", "private-capital.unit-register.unsettled-or-mismatched",
            "private-capital.unit-register.unposted-movement", "private-capital.unit-register.net-assets"
        ]);
    }

    [Fact]
    public void MatchingNetAssetsDoNotHidePostedFeeActivity()
    {
        var subscription = CashJournal(1, "Subscription", "investor-a", 100m);
        var fee = Record(2, (LedgerAccounts.ManagementFeeExpenseFor(Scope.FundProfileId), 10m, 0m),
            (LedgerAccounts.ManagementFeePayableFor(Scope.FundProfileId), 0m, 10m));

        var register = Preview([subscription, fee], Source(90m, Move(subscription, 10m, 100m, 10m)));

        register.ClassNav.Should().Be(90m);
        register.IsTied.Should().BeFalse();
        var blocker = register.Issues.Should().ContainSingle().Which;
        blocker.Code.Should().Be("private-capital.unit-register.unsupported-fees");
        blocker.TargetId.Should().Be(fee.Entry.JournalEntryId.ToString("D"));
    }

    [Fact]
    public void RejectsAnotherExportScopeAndDuplicateJournalReferences()
    {
        var journal = CashJournal(1, "Subscription", "investor-a", 100m);
        var movement = Move(journal, 10m, 100m, 10m);
        var scope = () => Preview([journal], Source(100m, movement) with { EntityId = Guid.NewGuid() });
        var replay = () => Preview([journal], Source(100m, movement, movement));

        scope.Should().Throw<ArgumentException>().WithMessage("*identify the selected book*");
        replay.Should().Throw<ArgumentException>().WithMessage("*unique posted journal*");
    }

    [Theory]
    [InlineData("accepted", true)]
    [InlineData("unreviewed", false)]
    [InlineData("different-bytes", false)]
    [InlineData("foreign-company", false)]
    public async Task ReadsOnlyAcceptedExactSourceAndHardCloseVersion(string state, bool canRead)
    {
        const string vaultId = "ev-0123456789abcdef01234567";
        const string documentId = "unit-register";
        var journal = CashJournal(1, "Subscription", "investor-a", 100m);
        var request = new PreviewPrivateCapitalUnitRegisterRequest(PeriodId, EvidenceVaultReference.CanonicalPrefix + vaultId, documentId);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Source(100m, Move(journal, 10m, 100m, 10m)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var hash = Sha256Digest.Compute(bytes);
        var document = new EvidenceDocumentDto(documentId, "unit-register.json", EvidenceDocumentClassificationDto.AdminPackage,
            state == "different-bytes" ? new string('0', 64) : hash, At(Cutoff), "synthetic-upload", "source-operator",
            Scope.TenantId, Scope.CompanyId, EvidenceExtractionStatusDto.Accepted, [],
            new(state == "unreviewed" ? EvidenceDocumentReviewStatusDto.Unreviewed : EvidenceDocumentReviewStatusDto.Accepted,
                "source-reviewer", At(Cutoff)), []) { ContentType = "application/json" };
        var identity = new EvidenceVaultIdentityDto(vaultId, "fund", Scope.FundProfileId, "retained.json",
            "/api/evidence", At(Cutoff), hash, 1, "file-bundle")
        {
            TenantId = Scope.TenantId,
            Scope = state == "foreign-company" ? "foreign" : Scope.CompanyId,
            Documents = [document]
        };
        var vault = Substitute.For<IEvidenceArtifactStore>();
        vault.TryGetVaultIdentityAsync(vaultId, Scope.TenantId, Scope.CompanyId, Arg.Any<CancellationToken>()).Returns(identity);
        vault.VerifyRetainedContentAsync(vaultId, Scope.TenantId, Scope.CompanyId, Arg.Any<CancellationToken>()).Returns(true);
        vault.TryOpenDocumentContentAsync(vaultId, documentId, Scope.TenantId, Scope.CompanyId, Arg.Any<CancellationToken>())
            .Returns(_ => new EvidenceManifestFile(new MemoryStream(bytes), "application/json", "unit-register.json", At(Cutoff)));
        var journalStore = Substitute.For<ILedgerJournalStore>();
        journalStore.GetLedgerBookAsync(BookId, Arg.Any<CancellationToken>()).Returns(new LedgerBookRecord(
            BookId, Scope.FundProfileId, EntityId, FundStructureNodeKindDto.Entity, "Entity A", "USD",
            At(Inception), At(Inception), AccountingBasis: AccountingBasisKindDto.Gaap));
        journalStore.GetPeriodAsync(PeriodId, Arg.Any<CancellationToken>()).Returns(Period);
        var snapshot = LedgerPeriodCloseSnapshot.Capture(Period, new PeriodCloseEventRecord(Guid.NewGuid(),
            PeriodId, "SoftClosed", "HardClosed", "controller", "Synthetic close", At(Cutoff)), [journal]);
        var close = Substitute.For<IAtomicLedgerPeriodCloseStore>();
        close.GetCloseSnapshotAsync(PeriodId, Period.Version, Arg.Any<CancellationToken>()).Returns(snapshot);

        var read = () => PrivateCapitalUnitRegisterPreview.BuildAsync(vault, journalStore, close, Scope, request, CancellationToken.None);

        if (canRead)
        {
            var register = await read();
            register.IsTied.Should().BeTrue();
            register.SourceHashSha256.Should().Be(hash);
            register.CloseSnapshotHashSha256.Should().Be(snapshot.ContentHashSha256);
            register.Movements.Should().ContainSingle().Which.TransactionId.Should().Be(journal.Entry.JournalEntryId);
        }
        else
        {
            await read.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    private static PrivateCapitalUnitRegisterPreviewDto Preview(IReadOnlyList<LedgerJournalEntryRecord> journals,
        PrivateCapitalUnitRegisterSourceDto source) => PrivateCapitalUnitRegisterPreview.Build(Scope,
        AccountingBasisKindDto.Gaap, Period, journals, source, "evidence://evidence-vault/synthetic",
        "document", "source-hash", "close-hash");

    private static PrivateCapitalUnitRegisterSourceDto Source(decimal nav, params PrivateCapitalUnitRegisterSourceMovementDto[] moves)
        => new(BookId, PeriodId, EntityId, "fund-a", "single-class", "USD", PrivateCapitalUnitRegisterPreview.PolicyVersion,
            Inception, Cutoff, 10m, nav, moves);

    private static PrivateCapitalUnitRegisterSourceMovementDto Move(
        LedgerJournalEntryRecord journal, decimal price, decimal amount, decimal units)
        => new(journal.Entry.JournalEntryId, journal.Entry.Metadata.InvestorId!, journal.Entry.Metadata.CapitalAccountId!,
            journal.Entry.Metadata.EffectiveDate!.Value, journal.Entry.Metadata.FundEventType!, price, amount, units);

    private static LedgerJournalEntryRecord CashJournal(int sequence, string type, string investorId, decimal signedAmount)
    {
        var positive = Math.Max(signedAmount, 0m);
        var negative = Math.Max(-signedAmount, 0m);
        var record = Record(sequence, (LedgerAccounts.Cash, positive, negative),
            (LedgerAccounts.InvestorCapitalFor(investorId), negative, positive));
        return record with
        {
            Entry = new JournalEntry(record.Entry.JournalEntryId, record.Entry.Timestamp, record.Entry.Description, record.Entry.Lines,
                new JournalEntryMetadata(EffectiveDate: Inception.AddDays(sequence), FundEventId: $"dealing-{sequence}",
                    FundEventType: type, CapitalAccountId: $"capital-{investorId}", InvestorId: investorId))
        };
    }

    private static LedgerJournalEntryRecord Record(int sequence,
        params (LedgerAccount Account, decimal Debit, decimal Credit)[] entries)
    {
        var id = Guid.NewGuid();
        var timestamp = At(Inception.AddDays(sequence));
        var lines = entries.Select(line => new LedgerEntry(Guid.NewGuid(), id, timestamp, line.Account,
            line.Debit, line.Credit, "Retained January transaction")).ToArray();
        return new(new JournalEntry(id, timestamp, "Retained January transaction", lines), BookId, PeriodId,
            null, null, sequence, timestamp, AccountingBasisKindDto.Gaap);
    }

    private static DateTimeOffset At(DateOnly date) => new(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
}
