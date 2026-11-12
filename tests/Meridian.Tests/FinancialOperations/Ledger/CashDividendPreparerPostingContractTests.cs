using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Storage.AssetOperations;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed class CashDividendPreparerPostingContractTests
{
    private static readonly Guid BookId = Guid.Parse("72000000-0000-0000-0000-000000000001");
    private static readonly Guid PeriodId = Guid.Parse("72000000-0000-0000-0000-000000000002");
    private static readonly Guid EventId = Guid.Parse("72000000-0000-0000-0000-000000000003");
    private static readonly Guid SecurityId = Guid.Parse("72000000-0000-0000-0000-000000000004");
    private static readonly Guid PositionId = Guid.Parse("72000000-0000-0000-0000-000000000005");
    private static readonly DateTimeOffset At = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Date = new(2026, 11, 1);

    [Fact]
    public void NativeVerifiedCommandRetainsDistinctTypedDecisionAndNeverCreatesApproval()
    {
        var write = BuildWrite();
        var result = AccountingPostingCommandValidator.NormalizeAndValidate(write, true, true, allowPreparerAttestation: true);

        result.PostingCommand!.ApprovalState.Should().Be(AccountingPostingApprovalStateDto.PreparerAttested);
        result.PostingCommand.ApprovalId.Should().BeNull();
        result.Entry.Metadata.PreparerAttestation.Should().BeEquivalentTo(write.PostingCommand!.CashDividendAttestation);
        result.Entry.Metadata.Tags!["decisionKind"].Should().Be("PreparerAttestation");
        result.Entry.Metadata.Tags["attestationReceiptRef"].Should().Be(
            PreparerAttestationApprovalBoundary.ReceiptReference(write.PostingCommand.CashDividendAttestation!.Receipt.AttestationId));
        result.Entry.Metadata.Tags.Should().NotContainKey("approvalId");
        result.PostingCommand.Evidence.Should().OnlyContain(item => item.Kind == AccountingPostingEvidenceKindDto.Source,
            "the native preparer receipt is not fabricated as Accepted independent-approval evidence");
    }

    [Fact]
    public void OrdinaryAppendCannotOptItselfIntoPreparerAuthority()
    {
        var append = () => AccountingPostingCommandValidator.NormalizeAndValidate(BuildWrite(), true, true);
        append.Should().Throw<LedgerValidationException>().WithMessage("*native lease*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NativeAttestedAppendRejectsMissingOrEmptyLedgerBook(bool missing)
    {
        var write = BuildWrite();
        Guid? bookId = missing ? null : Guid.Empty;
        write = write with
        {
            LedgerBookId = bookId,
            PostingCommand = write.PostingCommand! with { LedgerBookId = bookId }
        };

        var append = () => AccountingPostingCommandValidator.NormalizeAndValidate(write,
            true, true, allowPreparerAttestation: true);

        append.Should().Throw<LedgerValidationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryNativeCommandCannotUpcastReservedPreparerReference(bool evidenceReference)
    {
        var write = BuildWrite();
        var reserved = PreparerAttestationApprovalBoundary.ReceiptReference(PreparerAttestationApprovalBoundary.NewReceiptId());
        var command = write.PostingCommand! with
        {
            CashDividendAttestation = null,
            ApprovalState = AccountingPostingApprovalStateDto.Approved,
            ApprovalId = evidenceReference ? "ordinary-approval" : reserved,
            Evidence = evidenceReference ? [write.PostingCommand!.Evidence[0] with
                { Kind = AccountingPostingEvidenceKindDto.Approval, Uri = reserved }] : write.PostingCommand!.Evidence
        };
        var append = () => AccountingPostingCommandValidator.NormalizeAndValidate(write with { PostingCommand = command }, true, true);
        append.Should().Throw<LedgerValidationException>().WithMessage("*cannot supply ordinary*");
    }

    [Fact]
    public async Task OrdinaryPostingServiceRefusesReservedReceiptBeforeBuildingCandidate()
    {
        var builder = Substitute.For<IAccountingPostingCandidateWriteBuilder>();
        var service = new AccountingPostingCandidatePostService(builder, Substitute.For<ILedgerJournalStore>());
        var candidate = new PostingRuleJournalCandidateRequestDto("fund-a", "Dividend", 100m, "USD", Date,
            "solo", BookId, PeriodId, At, "Synthetic ordinary posting", LedgerBookId: BookId);
        var request = new PostPostingRuleJournalCandidateRequestDto(candidate, "independent-human",
            PreparerAttestationApprovalBoundary.ReceiptReference(PreparerAttestationApprovalBoundary.NewReceiptId()));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PostCandidateAsync(request));
        await builder.DidNotReceiveWithAnyArgs().BuildCandidateWriteAsync(default!, default);
    }

    [Theory]
    [InlineData("ordinary-approval-id")]
    [InlineData("other-actor")]
    [InlineData("other-book")]
    [InlineData("other-period")]
    [InlineData("wrong-policy")]
    [InlineData("event-cap")]
    [InlineData("part-greater-than-whole")]
    [InlineData("foreign-currency")]
    [InlineData("voided-receipt")]
    [InlineData("missing-entitlement")]
    [InlineData("correction")]
    [InlineData("mixed-approval-state")]
    public void NativeFlagCannotCureInvalidScopePolicyEconomicsOrClassification(string mutation)
    {
        var write = BuildWrite();
        var command = write.PostingCommand!;
        var binding = command.CashDividendAttestation!;
        var receipt = binding.Receipt;
        command = mutation switch
        {
            "ordinary-approval-id" => command with { ApprovalId = "fake-independent-review" },
            "other-actor" => command with { Actor = "other-human" },
            "other-book" => command with { CashDividendAttestation = binding with { LedgerBookId = Guid.NewGuid() } },
            "other-period" => command with { CashDividendAttestation = binding with { PeriodId = Guid.NewGuid() } },
            "wrong-policy" => command with { CashDividendAttestation = binding with { Receipt = receipt with { PolicyVersion = "unadopted" } } },
            "event-cap" => command with { CashDividendAttestation = binding with { Receipt = receipt with { WholeEventGrossUsd = 10_000.01m } } },
            "part-greater-than-whole" => command with { CashDividendAttestation = binding with { Receipt = receipt with { WholeEventGrossUsd = 99m } } },
            "foreign-currency" => command with { BookContext = command.BookContext! with { BaseCurrency = "EUR" } },
            "voided-receipt" => command with { CashDividendAttestation = binding with { Receipt = receipt with { VoidedAtUtc = At.AddHours(1) } } },
            "missing-entitlement" => command with { CashDividendAttestation = binding with { Receipt = receipt with { EntitlementSnapshotJson = "" } } },
            "correction" => command with { Intent = AccountingPostingIntentDto.Reversal, SourceJournalEntryId = Guid.NewGuid() },
            "mixed-approval-state" => command with { ApprovalState = AccountingPostingApprovalStateDto.Approved },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        var append = () => AccountingPostingCommandValidator.NormalizeAndValidate(write with { PostingCommand = command },
            true, true, allowPreparerAttestation: true);
        append.Should().Throw<LedgerValidationException>();
    }

    [Fact]
    public void OrdinaryApprovedCommandKeepsItsExistingPathWithoutTypedPreparerMetadata()
    {
        var write = BuildWrite();
        write = write with
        {
            PostingCommand = write.PostingCommand! with
            {
                Actor = "independent-reviewer",
                ApprovalState = AccountingPostingApprovalStateDto.Approved,
                ApprovalId = "ordinary-review",
                CashDividendAttestation = null
            }
        };
        var result = AccountingPostingCommandValidator.NormalizeAndValidate(write, true, true);
        result.Entry.Metadata.PreparerAttestation.Should().BeNull();
        result.Entry.Metadata.Tags!["approvalId"].Should().Be("ordinary-review");
        result.Entry.Metadata.Tags.Should().NotContainKey("attestationReceiptRef");
    }

    [Fact]
    public void JournalOnlyMetadataCannotSmugglePreparerClassificationIntoOrdinaryPosting()
    {
        var write = BuildWrite();
        var entry = new JournalEntry(write.Entry.JournalEntryId, write.Entry.Timestamp, write.Entry.Description,
            write.Entry.Lines, write.Entry.Metadata with { PreparerAttestation = write.PostingCommand!.CashDividendAttestation });
        var append = () => AccountingPostingCommandValidator.NormalizeAndValidate(write with { Entry = entry, PostingCommand = null });
        append.Should().Throw<LedgerValidationException>();
        JsonSerializer.SerializeToElement(new JournalEntryMetadata(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .TryGetProperty("preparerAttestation", out _).Should().BeFalse("old journal hashes must omit the absent new field");
    }

    [Fact]
    public void ReplayCannotChangeOnlyTheTypedNativeReceipt()
    {
        var write = AccountingPostingCommandValidator.NormalizeAndValidate(BuildWrite(), true, true, true);
        var retained = new LedgerJournalEntryRecord(write.Entry, write.AggregateId, write.PeriodId,
            write.CommandId, write.CorrelationId, 1, At.AddMinutes(10), write.AccountingBasis,
            write.AccountingPolicyId, write.AccountingPolicyVersion, write.RuleId, write.RuleVersion, write.SourceEventId);
        var original = write.Entry.Metadata.PreparerAttestation!;
        var changedEntry = new JournalEntry(write.Entry.JournalEntryId, write.Entry.Timestamp, write.Entry.Description,
            write.Entry.Lines, write.Entry.Metadata with
            {
                PreparerAttestation = original with
                { Receipt = original.Receipt with { AttestationId = Guid.NewGuid() } }
            });
        RetainedPostingEquivalence.Matches(retained, write with { Entry = changedEntry }, out var difference).Should().BeFalse();
        difference.Should().Be("native preparer attestation");
    }

    [Fact]
    public async Task MissingNativeLeaseFailsBeforeCandidateBuilderOrLedgerAccess()
    {
        var builder = Substitute.For<IAccountingPostingCandidateWriteBuilder>();
        var service = new AccountingPostingCandidatePostService(builder);
        var request = new PostAttestedCashDividendPostingCandidateRequestDto(new("fund-a",
            AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction), 100m, "USD", Date,
            "solo", BookId, PeriodId, At, "Synthetic regular dividend", LedgerBookId: BookId),
            "solo", Guid.NewGuid(), "tenant-a", "company-a", "Reviewed exact synthetic entitlement");
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.PostAttestedCashDividendCandidateAsync(request, null!));
        await builder.DidNotReceiveWithAnyArgs().BuildCandidateWriteAsync(default!, default);
    }

    [Fact]
    public void NativePreparerSpineUsesDistinctDecisionStageWithoutIndependentReviewEvidence()
    {
        var spine = BuildPreparerSpine();
        AssetAccountingEventSpineValidator.Validate(spine).Should().BeEmpty();
        spine.Stages.Should().NotContain(stage => stage.Stage == AssetAccountingLifecycleStageDto.Approved);
        spine.Stages[^1].Evidence.Should().OnlyContain(item => item.SubjectType == AssetAccountingEvidenceSubjects.Event);
        var draft = spine with
        {
            SpineVersion = spine.SpineVersion - 1,
            Stages = spine.Stages.Take(3).ToArray(),
            PreparerAttestation = null
        };
        var append = () => AssetAccountingEventProjectionRules.ValidateStreamContinuity(draft, spine);
        append.Should().NotThrow();
    }

    [Theory]
    [InlineData("ordinary-approval-stage")]
    [InlineData("missing-native-binding")]
    [InlineData("foreign-scope")]
    [InlineData("wrong-reference")]
    [InlineData("missing-receipt")]
    public void PreparerSpineCannotClaimIndependentReviewOrLoseItsExactNativeBinding(string mutation)
    {
        var spine = BuildPreparerSpine();
        spine = mutation switch
        {
            "ordinary-approval-stage" => spine with
            {
                Stages = [.. spine.Stages.Take(3), spine.Stages[^1] with { Stage = AssetAccountingLifecycleStageDto.Approved }]
            },
            "missing-native-binding" => spine with { PreparerAttestation = null },
            "foreign-scope" => spine with { PreparerAttestation = spine.PreparerAttestation! with { TenantId = "other-tenant" } },
            "wrong-reference" => spine with
            {
                Stages = [.. spine.Stages.Take(3), spine.Stages[^1] with { ReferenceId = "ordinary-approval" }]
            },
            "missing-receipt" => spine with { PreparerAttestation = spine.PreparerAttestation! with { Receipt = null! } },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        AssetAccountingEventSpineValidator.Validate(spine).Should().NotBeEmpty();
    }

    private static AssetAccountingEventSpineDto BuildPreparerSpine()
    {
        var command = BuildWrite().PostingCommand!;
        var evidence = command.Evidence.Select(item => new RetainedEvidenceIdentityDto(item.EvidenceId, item.Uri,
            item.ContentHash!, item.SourceSystem, item.SourceReference!, item.ReviewStatus!, item.Reviewer!,
            item.ReviewedAtUtc!.Value, item.EffectiveDate!.Value, item.EvidenceVersion!.Value,
            item.RetainedAtUtc, item.RetainedBy, item.SubjectType!, item.SubjectId!)).ToArray();
        var candidate = new PostingRuleJournalCandidateRequestDto("fund-a", command.SourceEventType!, 100m, "USD", Date,
            "solo", BookId, PeriodId, At, "Synthetic regular dividend", AccountingBasisKindDto.Gaap,
            LedgerBookId: BookId, SourceEventId: EventId, TenantId: "tenant-a", CompanyId: "company-a")
        {
            RetainedEvidence = evidence,
            BookContext = command.BookContext,
            BookPositionId = PositionId,
            EconomicEvent = command.EconomicEvent,
            ProjectionLineage = command.ProjectionLineage,
            RulePackReference = command.RulePackReference
        };
        var lines = new GeneratedPostingLineDto[]
        {
            new("receivable", "Assets:DividendReceivable", AccountingTemplateLineSideDto.Debit, "gross", 100m),
            new("income", "Income:Dividend", AccountingTemplateLineSideDto.Credit, "gross", 100m)
        };
        var dryRun = new RuleDryRunResultDto("fund-a", BookId, command.SourceEventType!, Date, 100m, "USD", true,
            "cash-dividend", [], [], [], lines);
        var candidateResult = new PostingRuleJournalCandidateResultDto(dryRun, "cash-dividend", "v1", lines,
            command with { CashDividendAttestation = null, ApprovalState = AccountingPostingApprovalStateDto.Pending },
            Guid.NewGuid(), 100m, 100m, 0m, true, false, true, false, [], []);
        var candidateFingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidate);
        var binding = command.CashDividendAttestation! with { DraftedCandidateFingerprint = candidateFingerprint };
        var lineage = command.ProjectionLineage!;
        return new(EventId, AssetAccountingEventKindDto.CorporateAction, 1, 3, Date, 100m, "USD",
            new(SecurityId, 1, PositionId, 1, BookId, PeriodId, AccountingBasisKindDto.Gaap, "fund-a", "tenant-a", "company-a"),
            command.EconomicEvent!, lineage, evidence,
            [
                new(AssetAccountingLifecycleStageDto.Expected, At, "solo", evidence, EventId.ToString("D")),
                new(AssetAccountingLifecycleStageDto.Projected, At.AddMinutes(1), "solo", evidence, lineage.ProjectionRunId.ToString("D")),
                new(AssetAccountingLifecycleStageDto.Drafted, At.AddMinutes(3), "solo", evidence, "synthetic-draft"),
                new(AssetAccountingLifecycleStageDto.PreparerAttested, binding.Receipt.AttestedAtUtc, "solo", evidence,
                    PreparerAttestationApprovalBoundary.ReceiptReference(binding.Receipt.AttestationId))
            ],
            new(lineage.ProjectionRunId, lineage.ModelKey, lineage.ModelVersion, Date, 100m, 100m, "USD",
                [new("Assets:DividendReceivable", 100m, 0m, "USD"), new("Income:Dividend", 0m, 100m, "USD")]),
            DraftedCandidate: candidate, DraftedCandidateResult: candidateResult,
            DraftedCandidateFingerprint: candidateFingerprint,
            DraftedCandidateResultFingerprint: AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidateResult))
        { PreparerAttestation = binding };
    }

    internal static LedgerJournalEntryWrite BuildWrite()
    {
        var sourceHash = Sha256Digest.ComputeUtf8("synthetic provider dividend source");
        var entitlementJson = "{\"schemaVersion\":1,\"synthetic\":true,\"complete\":true,\"grossUsd\":100}";
        var entitlementHash = Sha256Digest.ComputeUtf8(entitlementJson);
        var receipt = new CorporateActionCashDividendAttestationDto(PreparerAttestationApprovalBoundary.NewReceiptId(), Guid.NewGuid(), Guid.NewGuid(), 3,
            Guid.NewGuid(), 1, CorporateActionCashDividendAttestationPolicy.Version, CorporateActionCashDividendAttestationPolicy.PolicyHash,
            Guid.NewGuid(), "synthetic-related-dividend", Date, "synthetic-provider", "synthetic-source-event", "1",
            sourceHash, entitlementHash, entitlementJson, 100m, 100m, Date, "solo", At.AddMinutes(5),
            "Reviewed complete synthetic entitlement", "cash-dividend-eligibility:" + entitlementHash, entitlementHash,
            CorporateActionCashDividendAttestationPolicy.Confirmation, CorporateActionCashDividendAttestationPolicy.Disclosure,
            AccountRoleBinding: new(BookId, "Assets:DividendReceivable", "Income:Dividend", "dividend-pack", "v1", "cash-dividend", "v1",
                DividendReceivableAccountName: "Dividend Receivable", DividendIncomeAccountName: "Dividend Income"));
        var binding = new AssetAccountingPreparerAttestationDto(receipt, BookId, PeriodId, "tenant-a", "company-a", EventId, 1,
            Sha256Digest.ComputeUtf8("synthetic candidate"), Sha256Digest.ComputeUtf8("synthetic posting intent"));
        var economic = new EconomicEventReferenceDto(EventId, AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction),
            1, Date, At, "SyntheticProvider", "synthetic-source-event", SourceContentHash: sourceHash)
        { SecurityId = SecurityId, BookPositionId = PositionId };
        var lineage = new ProjectionLineageDto(Guid.NewGuid(), null, "regular-cash-dividend", "v1", "v1", "synthetic",
            Date, At, "SyntheticProvider", "synthetic-source-event", economic)
        { BookPositionId = PositionId };
        var dimensions = new LedgerLineDimensionSet(FundId: "fund-a", BookId: BookId.ToString("D"), InstrumentId: SecurityId)
        { PositionId = PositionId };
        var journalId = Guid.NewGuid();
        var entry = new JournalEntry(journalId, At, "Synthetic regular dividend",
        [
            new LedgerEntry(Guid.NewGuid(), journalId, At, new("Dividend Receivable", LedgerAccountType.Asset), 100m, 0m,
                "Synthetic regular dividend", dimensions, new("USD", "USD", 100m, 0m, 1m)),
            new LedgerEntry(Guid.NewGuid(), journalId, At, new("Dividend Income", LedgerAccountType.Revenue), 0m, 100m,
                "Synthetic regular dividend", dimensions, new("USD", "USD", 0m, 100m, 1m))
        ], new JournalEntryMetadata());
        var evidence = new AccountingPostingEvidenceReferenceDto("synthetic-source", "evidence://synthetic/source",
            AccountingPostingEvidenceKindDto.Source, "SyntheticProvider", At, "synthetic-importer", EventId.ToString("D"),
            sourceHash, SourceReference: "synthetic-source-event", Reviewer: "source-reviewer", ReviewedAtUtc: At,
            EffectiveDate: Date, EvidenceVersion: 1, ReviewStatus: RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            SubjectType: AssetAccountingEvidenceSubjects.Event);
        var command = new AccountingPostingCommandDto(Guid.NewGuid(), BookId, PeriodId, Date, At.AddMinutes(10),
            "synthetic-dividend-1", SourceEventId: EventId, ExpectedVersion: 1, SourceEventType: economic.EventType,
            ApprovalState: AccountingPostingApprovalStateDto.PreparerAttested, OperatorRationale: "Synthetic preparer decision",
            Evidence: [evidence], LedgerBookId: BookId)
        {
            Actor = "solo",
            CashDividendAttestation = binding,
            BookPositionId = PositionId,
            EconomicEvent = economic,
            ProjectionLineage = lineage,
            RulePackReference = new("dividend-pack", "v1", "cash-dividend", "v1"),
            BookContext = new(BookId, "fund-a", Guid.NewGuid(), FundStructureNodeKindDto.Fund, "USD synthetic book", "USD",
                AccountingBasisKindDto.Gaap, "policy-v1", "v1", PeriodId),
            Provenance = DataProvenance.Simulated
        };
        return new(entry, BookId, PeriodId, CommandId: command.CommandId, AccountingBasis: AccountingBasisKindDto.Gaap,
            AccountingPolicyId: "policy-v1", AccountingPolicyVersion: "v1", RuleId: "cash-dividend", RuleVersion: "v1",
            SourceEventId: EventId, PostingCommand: command, LedgerBookId: BookId);
    }
}
