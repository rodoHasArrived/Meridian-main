using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed class AssetAccountingPostingEvidenceValidatorTests
{
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 7, 21, 8, 0, 0, TimeSpan.Zero);

    private const string SourceHash =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void NormalizeAndValidate_CompleteAssetEvidence_PreservesAuditMetadata()
    {
        var write = BuildWrite();

        var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(write);

        var evidence = normalized.Entry.Metadata.EvidenceReferences.Should().ContainSingle().Subject;
        evidence.ContentHash.Should().Be(SourceHash);
        evidence.SourceReference.Should().Be("custodian-statement-2026-07-21");
        evidence.ReviewStatus.Should().Be(RetainedEvidenceIdentityValidator.AcceptedReviewStatus);
        evidence.ReviewedBy.Should().Be("investment-accountant");
        evidence.EffectiveDate.Should().Be(new DateOnly(2026, 7, 21));
        evidence.EvidenceVersion.Should().Be(3);
        evidence.SubjectType.Should().Be("AssetAccountingEvent");
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("hash")]
    [InlineData("source")]
    [InlineData("reviewer")]
    [InlineData("effective-date")]
    [InlineData("version")]
    public void NormalizeAndValidate_IncompleteAssetEvidence_FailsClosed(string missingField)
    {
        var write = BuildWrite();
        var command = write.PostingCommand!;
        var evidence = command.Evidence.Single();
        var incomplete = missingField switch
        {
            "identity" => evidence with { EvidenceId = string.Empty },
            "hash" => evidence with { ContentHash = null },
            "source" => evidence with { SourceReference = null },
            "reviewer" => evidence with { Reviewer = null },
            "effective-date" => evidence with { EffectiveDate = null },
            "version" => evidence with { EvidenceVersion = null },
            _ => throw new ArgumentOutOfRangeException(nameof(missingField))
        };

        var act = () => AccountingPostingCommandValidator.NormalizeAndValidate(write with
        {
            PostingCommand = command with { Evidence = [incomplete] }
        });

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*evidence is incomplete*");
    }

    [Fact]
    public void NormalizeAndValidate_AssetPostingWithRationaleOnly_FailsClosed()
    {
        var write = BuildWrite();

        var act = () => AccountingPostingCommandValidator.NormalizeAndValidate(write with
        {
            PostingCommand = write.PostingCommand! with
            {
                Evidence = [],
                OperatorRationale = "Controller reviewed the event."
            }
        });

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*operator rationale cannot substitute*");
    }

    [Fact]
    public void NormalizeAndValidate_AssetPostingWithMismatchedSourceHash_FailsClosed()
    {
        var write = BuildWrite();
        var command = write.PostingCommand!;

        var act = () => AccountingPostingCommandValidator.NormalizeAndValidate(write with
        {
            PostingCommand = command with
            {
                EconomicEvent = command.EconomicEvent! with
                {
                    SourceContentHash = new string('f', 64)
                }
            }
        });

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*source content hash*");
    }

    [Theory]
    [InlineData("source-reference")]
    [InlineData("uri")]
    [InlineData("evidence-id")]
    public void NormalizeAndValidate_RealPostingWithSyntheticEvidenceSegment_FailsClosed(string field)
    {
        // A mark priced off a fabricated close retains structured evidence such as
        // "daily-close:AAPL:2026-07-21:synthetic". Whichever field carries it, a posting
        // still marked Real must be refused at the append boundary.
        var write = BuildWrite();
        var command = write.PostingCommand!;
        var evidence = command.Evidence.Single();
        var synthetic = field switch
        {
            "source-reference" => evidence with { SourceReference = "daily-close:AAPL:2026-07-21:synthetic" },
            "uri" => evidence with { Uri = "workstation://valuation/daily-close:AAPL:2026-07-21:synthetic" },
            "evidence-id" => evidence with { EvidenceId = "daily-close:AAPL:2026-07-21:synthetic" },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        var act = () => AccountingPostingCommandValidator.NormalizeAndValidate(write with
        {
            PostingCommand = command with { Evidence = [synthetic] }
        });

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*simulated, seeded, or sample origin*");
    }

    [Fact]
    public void NormalizeAndValidate_MarkedSimulatedPostingWithSyntheticEvidence_Posts()
    {
        var write = BuildWrite();
        var command = write.PostingCommand!;
        var evidence = command.Evidence.Single() with
        {
            SourceReference = "daily-close:AAPL:2026-07-21:synthetic"
        };

        var act = () => AccountingPostingCommandValidator.NormalizeAndValidate(write with
        {
            PostingCommand = command with
            {
                Evidence = [evidence],
                Provenance = DataProvenance.Simulated
            }
        });

        act.Should().NotThrow("a posting that carries its simulated mark retains the origin honestly");
    }

    [Fact]
    public void NormalizeAndValidate_FreeTextSourceNames_AreNotMistakenForSimulatedOrigin()
    {
        var write = BuildWrite();
        var command = write.PostingCommand!;
        var evidence = command.Evidence.Single() with { SourceSystem = "Sample Custodian" };

        var act = () => AccountingPostingCommandValidator.NormalizeAndValidate(write with
        {
            PostingCommand = command with { Evidence = [evidence] }
        });

        act.Should().NotThrow("whole free-text names are not structured origin segments");
    }

    private static LedgerJournalEntryWrite BuildWrite()
    {
        var journalId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var aggregateId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var periodId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var commandId = Guid.Parse("10000000-0000-0000-0000-000000000004");
        var eventId = Guid.Parse("10000000-0000-0000-0000-000000000005");
        var securityId = Guid.Parse("10000000-0000-0000-0000-000000000006");
        var positionId = Guid.Parse("10000000-0000-0000-0000-000000000007");
        var projectionRunId = Guid.Parse("10000000-0000-0000-0000-000000000008");
        var effectiveDate = new DateOnly(2026, 7, 21);
        var eventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.Acquisition);
        var sourceEvent = new EconomicEventReferenceDto(
            eventId,
            eventType,
            EventVersion: 1,
            effectiveDate,
            OccurredAt,
            "AssetOperations",
            "asset-event-1",
            CorrelationId: eventId,
            CausationId: eventId,
            SourceContentHash: SourceHash,
            EvidenceLinks: ["evidence://asset-accounting/event-1"])
        {
            SecurityId = securityId,
            BookPositionId = positionId
        };
        var lineage = new ProjectionLineageDto(
            projectionRunId,
            eventId,
            "asset-accounting-acquisition",
            "1.0.0",
            "1.0.0",
            "Authoritative",
            effectiveDate,
            OccurredAt,
            "AssetOperations",
            "asset-event-1",
            sourceEvent,
            EvidenceLinks: ["evidence://asset-accounting/event-1"])
        {
            BookPositionId = positionId
        };
        var evidence = new AccountingPostingEvidenceReferenceDto(
            "evidence-1",
            "evidence://asset-accounting/event-1",
            AccountingPostingEvidenceKindDto.Source,
            "custodian",
            OccurredAt.AddMinutes(-10),
            "evidence-vault",
            SubjectId: eventId.ToString("D"),
            ContentHash: SourceHash,
            SourceReference: "custodian-statement-2026-07-21",
            Reviewer: "investment-accountant",
            ReviewedAtUtc: OccurredAt.AddMinutes(-15),
            EffectiveDate: effectiveDate,
            EvidenceVersion: 3,
            ReviewStatus: RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            SubjectType: "AssetAccountingEvent");
        var account = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var offset = new LedgerAccount("Investments", LedgerAccountType.Asset);
        var entry = new JournalEntry(
            journalId,
            OccurredAt,
            "Acquire asset",
            [
                new LedgerEntry(Guid.NewGuid(), journalId, OccurredAt, offset, 100m, 0m, "Acquire asset"),
                new LedgerEntry(Guid.NewGuid(), journalId, OccurredAt, account, 0m, 100m, "Acquire asset")
            ],
            new JournalEntryMetadata(
                ActivityType: eventType,
                SecurityId: securityId,
                LedgerBook: aggregateId.ToString("D"),
                EffectiveDate: effectiveDate,
                IdempotencyKey: "asset-acquisition-1"));
        var command = new AccountingPostingCommandDto(
            commandId,
            aggregateId,
            periodId,
            effectiveDate,
            OccurredAt,
            "asset-acquisition-1",
            SourceEventId: eventId,
            CorrelationId: eventId,
            CausationId: eventId,
            ExpectedVersion: 4,
            SourceEventType: eventType,
            ApprovalState: AccountingPostingApprovalStateDto.Approved,
            ApprovalId: "approval-1",
            Evidence: [evidence],
            ActionOrigin: OperationsActionOriginDto.HumanOperator,
            LedgerBookId: aggregateId)
        {
            BookContext = new AccountingBookContextDto(
                aggregateId,
                "fund-1",
                Guid.Parse("10000000-0000-0000-0000-000000000009"),
                FundStructureNodeKindDto.Fund,
                "Primary book",
                "USD",
                AccountingBasisKindDto.Gaap,
                "asset-policy",
                "1.0.0",
                periodId),
            BookPositionId = positionId,
            EconomicEvent = sourceEvent,
            ProjectionLineage = lineage,
            RulePackReference = new AccountingRulePackReferenceDto(
                "asset-rule-pack",
                "1.0.0",
                "acquisition-rule",
                "1.0.0")
        };

        return new LedgerJournalEntryWrite(
            entry,
            aggregateId,
            periodId,
            commandId,
            eventId,
            AccountingBasisKindDto.Gaap,
            "asset-policy",
            "1.0.0",
            "acquisition-rule",
            "1.0.0",
            eventId,
            PostingCommand: command,
            LedgerBookId: aggregateId);
    }
}
