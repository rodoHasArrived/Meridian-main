using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed class ConsolidationPostingLineageTests
{
    [Theory]
    [InlineData(AccountingPostingIntentDto.Adjustment)]
    [InlineData(AccountingPostingIntentDto.Reversal)]
    [InlineData(AccountingPostingIntentDto.Rebook)]
    [InlineData(AccountingPostingIntentDto.Restatement)]
    [InlineData(AccountingPostingIntentDto.AutomatedDraft)]
    public void InitialElimination_RequiresOriginatingIntent(AccountingPostingIntentDto intent)
    {
        var write = ConsolidationStorageFixture.ReviewedWrite();
        write = write with { PostingCommand = write.PostingCommand! with { Intent = intent } };
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        validate.Should().Throw<LedgerValidationException>().WithMessage("*Initial consolidation*originating*");
    }

    [Theory]
    [InlineData("adjustment-kind")]
    [InlineData("closing-kind")]
    [InlineData("command-source")]
    [InlineData("write-source")]
    [InlineData("adjustment-metadata")]
    public void InitialElimination_RejectsCorrectionFields(string changed)
    {
        var write = ConsolidationStorageFixture.ReviewedWrite();
        var source = Guid.NewGuid();
        write = changed switch
        {
            "adjustment-kind" => write with { PostingKind = LedgerPostingKindDto.Adjustment },
            "closing-kind" => write with { PostingKind = LedgerPostingKindDto.ClosingEntry },
            "command-source" => write with { PostingCommand = write.PostingCommand! with { SourceJournalEntryId = source } },
            "write-source" => write with { SourceJournalEntryId = source },
            "adjustment-metadata" => write with
            {
                AdjustmentApproval = ConsolidationStorageFixture.AsReviewedCorrection(write, source).AdjustmentApproval
            },
            _ => throw new ArgumentOutOfRangeException(nameof(changed))
        };
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        validate.Should().Throw<LedgerValidationException>().WithMessage("*Initial consolidation*originating*");
    }

    [Theory]
    [InlineData(AccountingPostingIntentDto.Reversal)]
    [InlineData(AccountingPostingIntentDto.Restatement)]
    [InlineData(AccountingPostingIntentDto.AutomatedDraft)]
    public void Correction_RejectsUnsupportedIntents(AccountingPostingIntentDto intent)
    {
        var write = CorrectionWrite();
        write = write with { PostingCommand = write.PostingCommand! with { Intent = intent } };
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        validate.Should().Throw<LedgerValidationException>().WithMessage("*adjustment posting semantics*");
    }

    [Theory]
    [InlineData(AccountingPostingIntentDto.Rebook, LedgerPostingKindDto.Originating, false)]
    [InlineData(AccountingPostingIntentDto.Rebook, LedgerPostingKindDto.Adjustment, true)]
    [InlineData(AccountingPostingIntentDto.Rebook, LedgerPostingKindDto.ClosingEntry, false)]
    [InlineData(AccountingPostingIntentDto.Adjustment, LedgerPostingKindDto.Originating, true)]
    [InlineData(AccountingPostingIntentDto.Adjustment, LedgerPostingKindDto.Adjustment, true)]
    [InlineData(AccountingPostingIntentDto.Adjustment, LedgerPostingKindDto.ClosingEntry, true)]
    public void Correction_RequiresAdjustmentKindAfterNormalization(
        AccountingPostingIntentDto intent, LedgerPostingKindDto kind, bool accepted)
    {
        var write = CorrectionWrite();
        write = write with { PostingKind = kind, PostingCommand = write.PostingCommand! with { Intent = intent } };
        if (accepted)
        {
            var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(write);
            normalized.PostingKind.Should().Be(LedgerPostingKindDto.Adjustment);
            normalized.SourceJournalEntryId.Should().Be(write.PostingCommand!.SourceJournalEntryId);
        }
        else
        {
            var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
            validate.Should().Throw<LedgerValidationException>().WithMessage("*adjustment posting semantics*");
        }
    }

    [Fact]
    public void Correction_RejectsSourceOutsideReviewedPriorEliminations()
    {
        var write = CorrectionWrite();
        write = write with { PostingCommand = write.PostingCommand! with { SourceJournalEntryId = Guid.NewGuid() } };
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        validate.Should().Throw<LedgerValidationException>().WithMessage("*linked prior posted elimination*");
    }

    [Fact]
    public void Correction_RejectsConflictingWriteSourceDuringNormalization()
    {
        var write = CorrectionWrite() with { SourceJournalEntryId = Guid.NewGuid() };
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        validate.Should().Throw<LedgerValidationException>().WithMessage("*source journal entry id conflicts*");
    }

    [Theory]
    [InlineData(false, "sourceJournalEntryId", "unrelated")]
    [InlineData(false, "SOURCEJOURNALENTRYID", "unrelated")]
    [InlineData(false, "sourceJournalEntryId", "empty")]
    [InlineData(true, "sourceJournalEntryId", "unrelated")]
    [InlineData(true, "SOURCEJOURNALENTRYID", "unrelated")]
    [InlineData(true, "sourceJournalEntryId", "empty")]
    [InlineData(true, "sourceJournalEntryId", "malformed")]
    public void SourceMetadata_RejectsUnreviewedLineage(bool correction, string key, string valueKind)
    {
        var write = correction ? CorrectionWrite() : ConsolidationStorageFixture.ReviewedWrite();
        var value = valueKind == "empty" ? "" : valueKind == "malformed" ? "not-a-guid" : Guid.NewGuid().ToString("D");
        write = WithSourceTag(write, key, value);
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        validate.Should().Throw<LedgerValidationException>().WithMessage("*source journal metadata*");
    }

    [Theory]
    [InlineData("sourceJournalEntryId")]
    [InlineData("SOURCEJOURNALENTRYID")]
    public void Correction_RetainsMatchingSourceMetadata(string key)
    {
        var write = CorrectionWrite();
        var source = write.PostingCommand!.SourceJournalEntryId!.Value;
        write = WithSourceTag(write with { SourceJournalEntryId = source }, key, source.ToString("D"));
        var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(write);
        normalized.SourceJournalEntryId.Should().Be(source);
        normalized.Entry.Metadata.Tags![key].Should().Be(source.ToString("D"));
    }

    private static LedgerJournalEntryWrite CorrectionWrite()
    {
        var write = ConsolidationStorageFixture.ReviewedWrite();
        var prior = Guid.NewGuid();
        write = ConsolidationStorageFixture.WithEvidence(write,
            ConsolidationStorageFixture.ReadEvidence(write) with { PriorPostedJournalIds = [prior] });
        return ConsolidationStorageFixture.AsReviewedCorrection(write, prior);
    }

    private static LedgerJournalEntryWrite WithSourceTag(LedgerJournalEntryWrite write, string key, string value)
    {
        var tags = new Dictionary<string, string>(write.Entry.Metadata.Tags!) { [key] = value };
        return write with
        {
            Entry = ConsolidationStorageFixture.WithMetadata(write.Entry, write.Entry.Metadata with { Tags = tags })
        };
    }
}
