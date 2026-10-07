using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed class ConsolidationPostingGuardTests
{
    [Fact]
    public void ApprovedElimination_RetainsExactReviewedEvidence()
    {
        var write = ConsolidationStorageFixture.ReviewedWrite();
        var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(write);
        normalized.Entry.Metadata.Tags!["consolidation.evidence"]
            .Should().Be(write.Entry.Metadata.Tags!["consolidation.evidence"]);
    }

    [Theory]
    [InlineData(AccountingPostingApprovalStateDto.Pending)]
    [InlineData(AccountingPostingApprovalStateDto.NotRequired)]
    [InlineData(AccountingPostingApprovalStateDto.Rejected)]
    public void ReservedElimination_RequiresExplicitApproval(AccountingPostingApprovalStateDto approval)
    {
        var write = ConsolidationStorageFixture.ReviewedWrite();
        write = write with { PostingCommand = write.PostingCommand! with { ApprovalState = approval } };
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        validate.Should().Throw<LedgerValidationException>().WithMessage("*explicit accounting approval*");
    }

    [Theory]
    [InlineData("evidence")]
    [InlineData("digest")]
    [InlineData("command")]
    [InlineData("approval-id")]
    public void ReservedElimination_CannotLoseRequiredReviewEvidence(string removed)
    {
        var write = ConsolidationStorageFixture.ReviewedWrite();
        var tags = new Dictionary<string, string>(write.Entry.Metadata.Tags!);
        tags.Remove($"consolidation.{removed}");
        write = write with
        {
            Entry = ConsolidationStorageFixture.WithMetadata(write.Entry, write.Entry.Metadata with { Tags = tags }),
            PostingCommand = removed == "command" ? null : removed == "approval-id"
                ? write.PostingCommand! with { ApprovalId = null } : write.PostingCommand
        };
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        validate.Should().Throw<LedgerValidationException>();
    }

    [Fact]
    public void ChangedEvidenceWithoutNewDigest_IsRefused()
    {
        var write = ConsolidationStorageFixture.ReviewedWrite();
        var tags = new Dictionary<string, string>(write.Entry.Metadata.Tags!)
        {
            ["consolidation.evidence"] = write.Entry.Metadata.Tags!["consolidation.evidence"].Replace("reviewed-source", "changed-source", StringComparison.Ordinal)
        };
        write = write with { Entry = ConsolidationStorageFixture.WithMetadata(write.Entry, write.Entry.Metadata with { Tags = tags }) };
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        validate.Should().Throw<LedgerValidationException>().WithMessage("*digest*");
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("account")]
    [InlineData("account-type")]
    [InlineData("dimensions")]
    public void ChangedJournalLines_AreRefusedEvenWithValidEvidenceDigest(string changed)
    {
        var write = ConsolidationStorageFixture.ReviewedWrite();
        var lines = write.Entry.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId,
            line.Timestamp, changed == "account" ? new("Unreviewed account", line.Account.AccountType)
                : changed == "account-type" ? new(line.Account.Name, LedgerAccountType.Equity) : line.Account,
            line.Debit * (changed == "amount" ? 2m : 1m), line.Credit * (changed == "amount" ? 2m : 1m),
            line.Description, changed == "dimensions" ? new(EntityId: "another-entity") : line.Dimensions)).ToArray();
        write = write with { Entry = new(write.Entry.JournalEntryId, write.Entry.Timestamp, write.Entry.Description, lines, write.Entry.Metadata) };
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        validate.Should().Throw<LedgerValidationException>().WithMessage("*lines differ*");
    }

    [Fact]
    public void MissingSourceBookVersion_IsRefused()
    {
        var write = ConsolidationStorageFixture.ReviewedWrite();
        var evidence = ConsolidationStorageFixture.ReadEvidence(write);
        write = ConsolidationStorageFixture.WithEvidence(write, evidence with { BookVersions = evidence.BookVersions.Take(2).ToArray() });
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        validate.Should().Throw<LedgerValidationException>().WithMessage("*both source books and the elimination book*");
    }

    [Fact]
    public void JournalMetadataCannotMoveEliminationOutsideReviewedAsOf()
    {
        var write = ConsolidationStorageFixture.ReviewedWrite();
        write = write with
        {
            Entry = ConsolidationStorageFixture.WithMetadata(write.Entry,
                write.Entry.Metadata with { EffectiveDate = ConsolidationStorageFixture.AsOf.AddDays(1) })
        };
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        validate.Should().Throw<LedgerValidationException>().WithMessage("*effective date*");
    }

    [Fact]
    public void Correction_RequiresLinkToRetainedPostedElimination()
    {
        var write = ConsolidationStorageFixture.ReviewedWrite();
        var prior = Guid.NewGuid();
        write = ConsolidationStorageFixture.WithEvidence(write,
            ConsolidationStorageFixture.ReadEvidence(write) with { PriorPostedJournalIds = [prior] });
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        validate.Should().Throw<LedgerValidationException>().WithMessage("*linked prior posted elimination*");
        write = ConsolidationStorageFixture.AsReviewedCorrection(write, prior);
        AccountingPostingCommandValidator.NormalizeAndValidate(write).SourceJournalEntryId.Should().Be(prior);
    }

    [Theory]
    [InlineData("originating-intent")]
    [InlineData("originating-kind")]
    [InlineData("missing-approval")]
    [InlineData("rejected-approval")]
    [InlineData("different-approval")]
    public void Correction_RejectsMisclassifiedOrUnapprovedCorrection(string change)
    {
        var prior = Guid.NewGuid();
        var write = ConsolidationStorageFixture.ReviewedWrite();
        write = ConsolidationStorageFixture.WithEvidence(write,
            ConsolidationStorageFixture.ReadEvidence(write) with { PriorPostedJournalIds = [prior] });
        write = ConsolidationStorageFixture.AsReviewedCorrection(write, prior);
        write = change switch
        {
            "originating-intent" => write with { PostingCommand = write.PostingCommand! with { Intent = AccountingPostingIntentDto.Originating } },
            "originating-kind" => write with { PostingKind = LedgerPostingKindDto.Originating },
            "missing-approval" => write with { AdjustmentApproval = null },
            "rejected-approval" => write with { AdjustmentApproval = write.AdjustmentApproval! with { Status = LedgerAdjustmentApprovalStatusDto.Rejected } },
            "different-approval" => write with { AdjustmentApproval = write.AdjustmentApproval! with { ApprovalId = "unrelated-review" } },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        var validate = () => AccountingPostingCommandValidator.NormalizeAndValidate(write);
        validate.Should().Throw<LedgerValidationException>().WithMessage("*adjustment posting semantics*");
    }

    [Theory]
    [InlineData(AccountingPostingIntentDto.Rebook)]
    [InlineData(AccountingPostingIntentDto.Adjustment)]
    public void Correction_AcceptsReviewedRebookAndNormalizedAdjustment(AccountingPostingIntentDto intent)
    {
        var prior = Guid.NewGuid();
        var write = ConsolidationStorageFixture.ReviewedWrite();
        write = ConsolidationStorageFixture.WithEvidence(write,
            ConsolidationStorageFixture.ReadEvidence(write) with { PriorPostedJournalIds = [prior] });
        write = ConsolidationStorageFixture.AsReviewedCorrection(write, prior) with
        {
            PostingCommand = write.PostingCommand! with { Intent = intent, SourceJournalEntryId = prior },
            PostingKind = intent == AccountingPostingIntentDto.Adjustment ? LedgerPostingKindDto.Originating : LedgerPostingKindDto.Adjustment
        };
        var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(write);
        normalized.PostingKind.Should().Be(LedgerPostingKindDto.Adjustment);
        normalized.SourceJournalEntryId.Should().Be(prior);
        normalized.AdjustmentApproval!.ApprovalId.Should().Be(normalized.PostingCommand!.ApprovalId);
    }
}

internal static class ConsolidationStorageFixture
{
    internal static readonly DateOnly AsOf = new(2026, 5, 31);

    internal static LedgerJournalEntryWrite AsReviewedCorrection(LedgerJournalEntryWrite write, Guid prior) => write with
    {
        PostingKind = LedgerPostingKindDto.Adjustment,
        PostingCommand = write.PostingCommand! with { Intent = AccountingPostingIntentDto.Rebook, SourceJournalEntryId = prior },
        AdjustmentApproval = new(write.PostingCommand!.ApprovalId!, LedgerAdjustmentApprovalStatusDto.Approved,
            "reviewing-controller", DateTimeOffset.UtcNow, "consolidation-correction", GovernanceCaseId: $"journal-correction:{prior:D}")
    };

    internal static LedgerBookRecord Book(string name) => new(Guid.NewGuid(), "fund-consolidation", Guid.NewGuid(),
        FundStructureNodeKindDto.Fund, name, "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    internal static LedgerAccountingPeriod Period(LedgerBookRecord book, int month = 5) => new(Guid.NewGuid(),
        book.LedgerBookId, 2026, month, $"2026-{month:00}", new(2026, month, 1),
        new(2026, month, DateTime.DaysInMonth(2026, month)), "Open", DateTimeOffset.UtcNow, null, 0);

    internal static LedgerJournalEntryWrite Write(LedgerBookRecord book, LedgerAccountingPeriod period, DateOnly? date = null)
    {
        var id = Guid.NewGuid();
        var effective = date ?? AsOf;
        var at = new DateTimeOffset(effective.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        const string description = "Reviewed consolidation fixture";
        var entry = new JournalEntry(id, at, description,
        [
            new(Guid.NewGuid(), id, at, new("Liabilities:Intercompany Payable", LedgerAccountType.Liability), 100m, 0m, description),
            new(Guid.NewGuid(), id, at, new("Assets:Intercompany Receivable", LedgerAccountType.Asset), 0m, 100m, description)
        ]);
        var command = new AccountingPostingCommandDto(Guid.NewGuid(), book.LedgerBookId, period.PeriodId,
            effective, at, $"fixture:{id:N}", ExpectedVersion: period.Version,
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: $"review:{id:N}",
            OperatorRationale: description, LedgerBookId: book.LedgerBookId)
        {
            Actor = "posting-controller",
            BookContext = new(book.LedgerBookId, book.FundProfileId, book.FundStructureNodeId,
                book.FundStructureNodeKind, book.DisplayName, book.BaseCurrency, book.AccountingBasis,
                book.AccountingPolicyId, book.AccountingPolicyVersion, period.PeriodId)
        };
        return new(entry, book.LedgerBookId, period.PeriodId, PostingCommand: command, LedgerBookId: book.LedgerBookId);
    }

    internal static LedgerJournalEntryWrite ReviewedWrite()
    {
        var book = Book("Eliminations");
        var period = Period(book);
        return WithEvidence(Write(book, period), Evidence(book, period,
            [new(Guid.NewGuid(), 0, 0), new(Guid.NewGuid(), 0, 0), new(book.LedgerBookId, 0, 0)]));
    }

    internal static ConsolidationEvidenceDto Evidence(LedgerBookRecord book, LedgerAccountingPeriod period,
        IReadOnlyList<ConsolidationBookVersionDto> versions) => new(
            new(Guid.NewGuid(), Guid.NewGuid(), book.LedgerBookId, period.PeriodId, AsOf),
            "fixture-scope", "reviewed-source", versions, "reviewed-perimeter", "v1", [],
            [new("payable", AccountingTemplateLineSideDto.Debit, 100m, "USD", "Liabilities:Intercompany Payable"),
             new("receivable", AccountingTemplateLineSideDto.Credit, 100m, "USD", "Assets:Intercompany Receivable")]);

    internal static ConsolidationEvidenceDto ReadEvidence(LedgerJournalEntryWrite write) =>
        JsonSerializer.Deserialize<ConsolidationEvidenceDto>(write.Entry.Metadata.Tags!["consolidation.evidence"])!;

    internal static LedgerJournalEntryWrite WithEvidence(LedgerJournalEntryWrite write, ConsolidationEvidenceDto evidence)
    {
        var json = JsonSerializer.Serialize(evidence);
        var key = $"consolidation:{write.Entry.JournalEntryId:N}";
        var metadata = write.Entry.Metadata with
        {
            IdempotencyKey = key,
            Tags = new Dictionary<string, string>
            {
                ["consolidation.evidence"] = json,
                ["consolidation.digest"] = Sha256Digest.ComputeUtf8(json)
            }
        };
        return write with { Entry = WithMetadata(write.Entry, metadata), PostingCommand = write.PostingCommand! with { IdempotencyKey = key } };
    }

    internal static JournalEntry WithMetadata(JournalEntry entry, JournalEntryMetadata metadata) =>
        new(entry.JournalEntryId, entry.Timestamp, entry.Description, entry.Lines, metadata);
}
