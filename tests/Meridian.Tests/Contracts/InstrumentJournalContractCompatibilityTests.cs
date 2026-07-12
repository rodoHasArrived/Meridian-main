using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.Contracts;

/// <summary>
/// Protects the additive instrument-to-journal contract used by an MBS factor-paydown workflow.
/// </summary>
public sealed class InstrumentJournalContractCompatibilityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid LedgerBookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid FundStructureNodeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PeriodId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid InstrumentRoleId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid BookPositionId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid EconomicEventId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid ProjectionRunId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    [Fact]
    public void JsonRoundTrip_MbsFactorPaydownContracts_PreservesSemanticValues()
    {
        var bookContext = BuildBookContext();
        var rulePack = BuildRulePackReference();
        var economicEvent = BuildEconomicEvent();
        var lineage = BuildProjectionLineage();
        var state = new PositionEconomicStateDto(
            EconomicStateId: Guid.Parse("99999999-9999-9999-9999-999999999999"),
            PositionId: BookPositionId,
            AsOfDate: new DateOnly(2026, 6, 30),
            Currency: "USD",
            Version: 7,
            Quantity: 100_000m,
            ParAmount: 96_250m,
            NotionalAmount: 96_250m,
            OriginalFaceAmount: 100_000m,
            CurrentFaceAmount: 96_250m,
            CarryingAmount: 96_250m,
            PriorFactor: 0.9800m,
            CurrentFactor: 0.9625m,
            SourceEvent: economicEvent,
            EvidenceLinks: ["evidence://factor/2026-06"]);
        var role = new InstrumentRoleDto(
            RoleId: InstrumentRoleId,
            SecurityId: SecurityId,
            OwnerScopeId: "fund-alpha",
            OwnerScopeKind: "Fund",
            RoleKind: InstrumentRoleKinds.Holder,
            AccountingSide: InstrumentAccountingSides.Debit,
            EconomicSide: InstrumentEconomicSides.Asset,
            EffectiveFrom: new DateOnly(2026, 6, 1),
            DefaultAccountId: "asset-mbs",
            Version: 7,
            OriginEvent: economicEvent,
            EvidenceLinks: ["evidence://factor/2026-06"]);
        var position = new BookPositionDto(
            PositionId: BookPositionId,
            SecurityId: SecurityId,
            RoleId: InstrumentRoleId,
            BookContext: bookContext,
            PositionSide: BookPositionSides.Long,
            Status: "Active",
            EffectiveFrom: new DateOnly(2026, 6, 1),
            Version: 7,
            PrimaryAccountId: "asset-mbs",
            CurrentEconomicState: state,
            OriginEvent: economicEvent,
            ProjectionLineage: lineage,
            EvidenceLinks: ["evidence://position/mbs-1"]);

        RoundTrip(bookContext).Should().BeEquivalentTo(bookContext);
        RoundTrip(rulePack).Should().BeEquivalentTo(rulePack);
        RoundTrip(role).Should().BeEquivalentTo(role);
        RoundTrip(position).Should().BeEquivalentTo(position);
        RoundTrip(state).Should().BeEquivalentTo(state);
        RoundTrip(economicEvent).Should().BeEquivalentTo(economicEvent);
        RoundTrip(lineage).Should().BeEquivalentTo(lineage);
    }

    [Fact]
    public void JsonRoundTrip_PostingIntentWithTypedReferences_PreservesBookEventAndRulePackValues()
    {
        var bookContext = BuildBookContext();
        var rulePack = BuildRulePackReference();
        var economicEvent = BuildEconomicEvent();
        var lineage = BuildProjectionLineage();
        var candidate = new PostingRuleJournalCandidateRequestDto(
            FundProfileId: "fund-alpha",
            SourceEventType: "MbsFactorPaydown",
            EventAmount: 1_750m,
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "preparer@meridian.local",
            AggregateId: LedgerBookId,
            PeriodId: PeriodId,
            AccountingTimestamp: DateTimeOffset.Parse("2026-07-01T12:00:00Z"),
            Description: "June MBS factor principal paydown",
            LedgerBookId: LedgerBookId,
            Dimensions: bookContext.Dimensions,
            SourceEventId: EconomicEventId,
            PolicyId: "gaap-mbs",
            EvidenceLinks: ["evidence://factor/2026-06"])
        {
            BookContext = bookContext,
            BookPositionId = BookPositionId,
            EconomicEvent = economicEvent,
            ProjectionLineage = lineage,
            RulePackReference = rulePack
        };
        var command = new AccountingPostingCommandDto(
            CommandId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            AggregateId: LedgerBookId,
            PeriodId: PeriodId,
            EffectiveDate: new DateOnly(2026, 6, 30),
            PostingDate: DateTimeOffset.Parse("2026-07-01T12:00:00Z"),
            IdempotencyKey: "fund-alpha:mbs-factor:2026-06",
            SourceEventId: EconomicEventId,
            LedgerBookId: LedgerBookId)
        {
            BookContext = bookContext,
            BookPositionId = BookPositionId,
            EconomicEvent = economicEvent,
            ProjectionLineage = lineage,
            RulePackReference = rulePack
        };

        var candidateCopy = RoundTrip(candidate);
        var commandCopy = RoundTrip(command);

        candidateCopy.BookContext.Should().BeEquivalentTo(bookContext);
        candidateCopy.BookPositionId.Should().Be(BookPositionId);
        candidateCopy.EconomicEvent.Should().BeEquivalentTo(economicEvent);
        candidateCopy.ProjectionLineage.Should().BeEquivalentTo(lineage);
        candidateCopy.RulePackReference.Should().BeEquivalentTo(rulePack);
        commandCopy.BookContext.Should().BeEquivalentTo(bookContext);
        commandCopy.BookPositionId.Should().Be(BookPositionId);
        commandCopy.EconomicEvent.Should().BeEquivalentTo(economicEvent);
        commandCopy.ProjectionLineage.Should().BeEquivalentTo(lineage);
        commandCopy.RulePackReference.Should().BeEquivalentTo(rulePack);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonDeserialize_LegacyAssetOperationsPayload_DefaultsTypedProjectionCollections(bool projectionPayload)
    {
        const string legacyJson = """
        {
          "subject": {
            "securityId": "11111111-1111-1111-1111-111111111111",
            "assetClass": "MBS",
            "displayName": "Agency MBS 2026-1",
            "primaryIdentifier": "CUSIP-EXAMPLE",
            "operationalProfile": ["FactorProcessing"]
          },
          "termsHistory": [],
          "lifecycleEvents": [],
          "cashFlowProjectionRuns": [],
          "projectedCashFlows": [],
          "actualActivity": [],
          "reconciliationRuns": [],
          "reconciliationResults": [],
          "ledgerProjections": [],
          "readiness": {
            "securityId": "11111111-1111-1111-1111-111111111111",
            "status": "Ready",
            "capabilities": [],
            "readyCapabilities": [],
            "missingCapabilities": [],
            "warnings": [],
            "evaluatedAt": "2026-07-01T12:00:00Z",
            "sourceDomain": "AssetOperations",
            "sourceEntityId": "mbs-1"
          },
          "workflowAudit": []
        }
        """;

        if (projectionPayload)
        {
            var projection = JsonSerializer.Deserialize<AssetOperationsProjectionDto>(legacyJson, JsonOptions);

            projection.Should().NotBeNull();
            AssertEmptyTypedCollections(projection!);
            return;
        }

        var detail = JsonSerializer.Deserialize<AssetOperationsDetailDto>(legacyJson, JsonOptions);

        detail.Should().NotBeNull();
        AssertEmptyTypedCollections(detail!);
    }

    [Fact]
    public void JsonDeserialize_LegacyPostingPayloads_DefaultTypedContextAndCollections()
    {
        const string legacyCandidateJson = """
        {
          "fundProfileId": "fund-alpha",
          "sourceEventType": "MbsFactorPaydown",
          "eventAmount": 1750,
          "currency": "USD",
          "effectiveDate": "2026-06-30",
          "actor": "preparer@meridian.local",
          "aggregateId": "22222222-2222-2222-2222-222222222222",
          "periodId": "44444444-4444-4444-4444-444444444444",
          "accountingTimestamp": "2026-07-01T12:00:00Z",
          "description": "June MBS factor principal paydown"
        }
        """;
        const string legacyCommandJson = """
        {
          "commandId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
          "aggregateId": "22222222-2222-2222-2222-222222222222",
          "periodId": "44444444-4444-4444-4444-444444444444",
          "effectiveDate": "2026-06-30",
          "postingDate": "2026-07-01T12:00:00Z",
          "idempotencyKey": "fund-alpha:mbs-factor:2026-06"
        }
        """;
        const string legacyCandidateResultJson = """
        {
          "dryRunResult": {
            "fundProfileId": "fund-alpha",
            "ledgerBookId": null,
            "sourceEventType": "MbsFactorPaydown",
            "effectiveDate": "2026-06-30",
            "eventAmount": 1750,
            "currency": "USD",
            "isPostingBalanced": true,
            "selectedRuleId": null,
            "ruleMatches": [],
            "generatedLines": [],
            "validationIssues": []
          },
          "selectedRuleId": null,
          "selectedRuleVersion": null,
          "generatedPostingLines": [],
          "postingCommand": null,
          "journalEntryId": null,
          "totalDebits": 0,
          "totalCredits": 0,
          "imbalance": 0,
          "isBalanced": true,
          "hasBlockingIssues": false,
          "canSubmitForApproval": true,
          "canPostWithoutAdditionalApproval": false,
          "evidenceLinks": [],
          "issues": []
        }
        """;

        var candidate = JsonSerializer.Deserialize<PostingRuleJournalCandidateRequestDto>(legacyCandidateJson, JsonOptions);
        var command = JsonSerializer.Deserialize<AccountingPostingCommandDto>(legacyCommandJson, JsonOptions);
        var result = JsonSerializer.Deserialize<PostingRuleJournalCandidateResultDto>(legacyCandidateResultJson, JsonOptions);

        candidate.Should().NotBeNull();
        AssertTypedContextIsAbsent(candidate!);
        candidate.EvidenceLinks.Should().BeEmpty();
        command.Should().NotBeNull();
        AssertTypedContextIsAbsent(command!);
        command.Evidence.Should().BeEmpty();
        result.Should().NotBeNull();
        AssertTypedContextIsAbsent(result!);
        result.GeneratedPostingLines.Should().BeEmpty();
        result.EvidenceLinks.Should().BeEmpty();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void PositionalConstructors_LegacyCallers_ReceiveAdditiveDefaults()
    {
        var subject = BuildSubject();
        var readiness = BuildReadiness();
        var detail = new AssetOperationsDetailDto(
            subject, [], [], [], [], [], [], [], [], readiness, []);
        var projection = new AssetOperationsProjectionDto(
            subject, [], [], [], [], [], [], [], [], readiness, []);
        var candidate = new PostingRuleJournalCandidateRequestDto(
            "fund-alpha",
            "MbsFactorPaydown",
            1_750m,
            "USD",
            new DateOnly(2026, 6, 30),
            "preparer@meridian.local",
            LedgerBookId,
            PeriodId,
            DateTimeOffset.Parse("2026-07-01T12:00:00Z"),
            "June MBS factor principal paydown");
        var command = new AccountingPostingCommandDto(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            LedgerBookId,
            PeriodId,
            new DateOnly(2026, 6, 30),
            DateTimeOffset.Parse("2026-07-01T12:00:00Z"),
            "fund-alpha:mbs-factor:2026-06");
        var dimensions = new LedgerDimensionSetDto("fund-alpha", "entity-alpha");

        AssertEmptyTypedCollections(detail);
        AssertEmptyTypedCollections(projection);
        AssertTypedContextIsAbsent(candidate);
        candidate.EvidenceLinks.Should().BeEmpty();
        AssertTypedContextIsAbsent(command);
        command.Evidence.Should().BeEmpty();
        dimensions.PositionId.Should().BeNull();
    }

    private static AccountingBookContextDto BuildBookContext() => new(
        LedgerBookId,
        FundProfileId: "fund-alpha",
        FundStructureNodeId,
        FundStructureNodeKindDto.Fund,
        DisplayName: "Alpha Fund GAAP Book",
        BaseCurrency: "USD",
        AccountingBasisKindDto.Gaap,
        AccountingPolicyId: "gaap-mbs",
        AccountingPolicyVersion: "2026.1",
        PeriodId,
        new LedgerDimensionSetDto(
            FundId: "fund-alpha",
            EntityId: "entity-alpha",
            InstrumentId: SecurityId,
            BookId: LedgerBookId.ToString())
        {
            PositionId = BookPositionId
        });

    private static AccountingRulePackReferenceDto BuildRulePackReference() => new(
        RulePackId: "mbs-factor-paydown",
        RulePackVersion: "2026.1",
        SelectedRuleId: "factor-principal-paydown",
        SelectedRuleVersion: "v3");

    private static EconomicEventReferenceDto BuildEconomicEvent() => new(
        EventId: EconomicEventId,
        EventType: "MbsFactorPaydown",
        EventVersion: 1,
        EffectiveDate: new DateOnly(2026, 6, 30),
        OccurredAtUtc: DateTimeOffset.Parse("2026-07-01T11:55:00Z"),
        SourceDomain: "SecurityMaster",
        SourceEntityId: "factor-source-2026-06",
        SourceContentHash: "sha256:factor-2026-06",
        EvidenceLinks: ["evidence://factor/2026-06"])
    {
        SecurityId = SecurityId,
        BookPositionId = BookPositionId
    };

    private static ProjectionLineageDto BuildProjectionLineage() => new(
        ProjectionRunId: ProjectionRunId,
        ProjectionEventId: EconomicEventId,
        ModelKey: "mbs-factor-paydown",
        ModelVersion: "1.0.0",
        EngineVersion: "meridian-instruments-1.0",
        Scenario: "base",
        ProjectionAsOfDate: new DateOnly(2026, 6, 30),
        GeneratedAtUtc: DateTimeOffset.Parse("2026-07-01T12:00:00Z"),
        SourceDomain: "AssetOperations",
        SourceEntityId: "position-mbs-1",
        TriggerEvent: BuildEconomicEvent(),
        TermsVersion: "2026.1",
        TermsHash: "sha256:terms-mbs-2026-1",
        EvidenceLinks: ["evidence://projection/mbs-2026-06"])
    {
        BookPositionId = BookPositionId
    };

    private static AssetOperationSubjectDto BuildSubject() => new(
        SecurityId,
        AssetClass: "MBS",
        DisplayName: "Agency MBS 2026-1",
        PrimaryIdentifier: "CUSIP-EXAMPLE",
        OperationalProfile: ["FactorProcessing"]);

    private static AssetOperationsReadinessDto BuildReadiness() => new(
        SecurityId,
        Status: "Ready",
        Capabilities: [],
        ReadyCapabilities: [],
        MissingCapabilities: [],
        Warnings: [],
        EvaluatedAt: DateTimeOffset.Parse("2026-07-01T12:00:00Z"),
        SourceDomain: "AssetOperations",
        SourceEntityId: "mbs-1");

    private static T RoundTrip<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialize {typeof(T).Name}.");
    }

    private static void AssertEmptyTypedCollections(AssetOperationsDetailDto detail)
    {
        detail.InstrumentRoles.Should().BeEmpty();
        detail.BookPositions.Should().BeEmpty();
        detail.PositionEconomicStates.Should().BeEmpty();
        detail.ProjectionLineages.Should().BeEmpty();
    }

    private static void AssertEmptyTypedCollections(AssetOperationsProjectionDto projection)
    {
        projection.InstrumentRoles.Should().BeEmpty();
        projection.BookPositions.Should().BeEmpty();
        projection.PositionEconomicStates.Should().BeEmpty();
        projection.ProjectionLineages.Should().BeEmpty();
    }

    private static void AssertTypedContextIsAbsent(PostingRuleJournalCandidateRequestDto candidate)
    {
        candidate.BookContext.Should().BeNull();
        candidate.BookPositionId.Should().BeNull();
        candidate.EconomicEvent.Should().BeNull();
        candidate.ProjectionLineage.Should().BeNull();
        candidate.RulePackReference.Should().BeNull();
    }

    private static void AssertTypedContextIsAbsent(AccountingPostingCommandDto command)
    {
        command.BookContext.Should().BeNull();
        command.BookPositionId.Should().BeNull();
        command.EconomicEvent.Should().BeNull();
        command.ProjectionLineage.Should().BeNull();
        command.RulePackReference.Should().BeNull();
    }

    private static void AssertTypedContextIsAbsent(PostingRuleJournalCandidateResultDto result)
    {
        result.BookContext.Should().BeNull();
        result.BookPositionId.Should().BeNull();
        result.EconomicEvent.Should().BeNull();
        result.ProjectionLineage.Should().BeNull();
        result.RulePackReference.Should().BeNull();
    }
}
