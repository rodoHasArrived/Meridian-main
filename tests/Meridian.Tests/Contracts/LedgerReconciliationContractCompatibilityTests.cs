using System.Text.Json;
using Meridian.Contracts.Workstation;
using Xunit;

namespace Meridian.Tests.Contracts;

public sealed class LedgerReconciliationContractCompatibilityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void FundLedgerSummary_WebAndRetainedConsumers_SerializeSameMembers()
    {
        var dto = BuildFundLedgerSummary();

        LedgerReconciliationContractCompatibility.EnsureFundLedgerSummaryRequiredFields(dto);

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        Assert.Contains("\"fundProfileId\"", json);
        Assert.Contains("\"fundDisplayName\"", json);
        Assert.Contains("\"scopeKind\"", json);
        Assert.Contains("\"trialBalance\"", json);
        Assert.Contains("\"journal\"", json);

        Assert.DoesNotContain("\"FundProfileId\"", json);
    }

    [Fact]
    public void ReconciliationSummary_WebAndRetainedConsumers_SerializeSameMembers()
    {
        var dto = new ReconciliationRunSummary(
            ReconciliationRunId: "recon-1",
            RunId: "run-1",
            CreatedAt: DateTimeOffset.Parse("2026-01-05T12:00:00Z"),
            PortfolioAsOf: DateTimeOffset.Parse("2026-01-05T11:59:00Z"),
            LedgerAsOf: DateTimeOffset.Parse("2026-01-05T11:59:00Z"),
            MatchCount: 10,
            BreakCount: 2,
            OpenBreakCount: 1,
            HasTimingDrift: false,
            AmountTolerance: 0.01m,
            MaxAsOfDriftMinutes: 5);

        LedgerReconciliationContractCompatibility.EnsureReconciliationSummaryRequiredFields(dto);

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        Assert.Contains("\"reconciliationRunId\"", json);
        Assert.Contains("\"runId\"", json);
        Assert.Contains("\"breakCount\"", json);
        Assert.Contains("\"openBreakCount\"", json);
    }

    [Fact]
    public void ContinuityDto_HasCanonicalPayloadProfile_ForResearchTradingGovernance()
    {
        var dto = BuildContinuityDto();

        LedgerReconciliationContractCompatibility.EnsureContinuityRequiredFields(dto);

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        Assert.Contains("\"run\"", json);
        Assert.Contains("\"lineage\"", json);
        Assert.Contains("\"continuityStatus\"", json);
        Assert.Contains("\"reconciliation\"", json);
        Assert.Contains("\"cashFlow\"", json);
    }

    [Fact]
    public void AdditiveOnlyContractShape_GuardForLedgerReconciliationContinuityDtos()
    {
        AssertMembers(typeof(FundLedgerSummary),
            "FundProfileId", "FundDisplayName", "ScopeKind", "ScopeId", "AsOf",
            "JournalEntryCount", "LedgerEntryCount", "AssetBalance", "LiabilityBalance",
            "EquityBalance", "RevenueBalance", "ExpenseBalance", "TrialBalance", "Journal",
            "EntityCount", "SleeveCount", "VehicleCount", "ConsolidatedTotals", "LedgerSlices");

        AssertMembers(typeof(ReconciliationRunSummary),
            "ReconciliationRunId", "RunId", "CreatedAt", "PortfolioAsOf", "LedgerAsOf",
            "MatchCount", "BreakCount", "OpenBreakCount", "HasTimingDrift", "AmountTolerance",
            "MaxAsOfDriftMinutes", "SecurityIssueCount", "HasSecurityCoverageIssues",
            "BankTransactionCount", "BankBreakCount", "ExpectedAccountingEventCount",
            "ExpectedJournalPreviewCount", "SecurityMasterAccountingIssueCount", "HasSecurityMasterAccountingIssues");

        AssertMembers(typeof(StrategyRunContinuityDto),
            "Run", "Lineage", "CashFlow", "Reconciliation", "ContinuityStatus");

        AssertMembers(typeof(ReconciliationSchemaVersion),
            "ContractName", "Major", "Minor", "Patch", "ContentType");

        AssertMembers(typeof(ReconciliationCorrelationContext),
            "TraceId", "SpanId", "ParentSpanId", "WorkflowId", "JobId");

        AssertMembers(typeof(ReconciliationPayloadEnvelope<ReconciliationRunSummary>),
            "PayloadId", "Schema", "Correlation", "CreatedAt", "Producer", "Direction", "IdempotencyKey", "Payload");
    }


    [Fact]
    public void StatementReconciliationRun_WebAndRetainedConsumers_SerializeEvidenceAndNormalizedPayloads()
    {
        var dto = BuildStatementRunDto();

        var json = JsonSerializer.Serialize(dto, JsonOptions);

        Assert.Contains("\"runId\":\"statement-run-1\"", json);
        Assert.Contains("\"status\":\"ReviewRequired\"", json);
        Assert.Contains("\"sourceFileName\":\"custodian-statement.csv\"", json);
        Assert.Contains("\"sourceFileHash\":\"sha256:statement-source\"", json);
        Assert.Contains("\"mappingProfileId\":\"map-custodian-v1\"", json);
        Assert.Contains("\"mappingProfileVersion\":3", json);
        Assert.Contains("\"toleranceProfileId\":\"tol-equity-cash\"", json);
        Assert.Contains("\"toleranceProfileVersion\":2", json);
        Assert.Contains("\"importedBy\":\"ops@example.com\"", json);
        Assert.Contains("\"importedAtUtc\":\"2026-01-05T12:00:00+00:00\"", json);
        Assert.Contains("\"positions\"", json);
        Assert.Contains("\"cashBalances\"", json);
        Assert.Contains("\"transactions\"", json);
        Assert.Contains("\"breaks\"", json);
        Assert.Contains("\"cases\"", json);

        Assert.DoesNotContain("\"SourceFileName\"", json);
        Assert.DoesNotContain("\"MappingProfileId\"", json);
    }

    [Fact]
    public void AdditiveOnlyContractShape_GuardForStatementReconciliationDtos()
    {
        AssertMembers(typeof(StatementRunDto),
            "RunId", "Status", "Source", "StartedAtUtc", "CompletedAtUtc", "SourceFileName",
            "SourceFileHash", "MappingProfileId", "MappingProfileVersion", "ToleranceProfileId",
            "ToleranceProfileVersion", "ImportedBy", "ImportedAtUtc", "ValidationIssues",
            "Positions", "CashBalances", "Transactions", "MatchSummary", "Breaks", "Cases",
            "ImportId", "FundProfileId", "FundAccountId", "Notes");

        AssertMembers(typeof(StatementSourceDto),
            "SourceId", "SourceKind", "SourceSystem", "AccountId", "AccountName", "Currency",
            "PeriodStart", "PeriodEnd", "StatementAsOfUtc", "CustodianId", "ExternalAccountId");

        AssertMembers(typeof(StatementValidationIssueDto),
            "IssueId", "Severity", "Code", "Message", "SourceRowNumber", "SourceColumn",
            "RawValue", "RecommendedAction", "EvidenceLink");

        AssertMembers(typeof(StatementNormalizedPositionDto),
            "RowId", "AccountId", "Symbol", "Quantity", "MarketPrice", "MarketValue",
            "Currency", "AsOfUtc", "Fingerprint", "SecurityId", "Cusip", "Isin", "Sedol", "SourceReference");

        AssertMembers(typeof(StatementNormalizedCashDto),
            "RowId", "AccountId", "Currency", "Amount", "AsOfUtc", "Fingerprint", "CashType", "SourceReference");

        AssertMembers(typeof(StatementNormalizedTransactionDto),
            "RowId", "AccountId", "ActivityType", "TradeDate", "Amount", "Currency", "Fingerprint",
            "SettleDate", "ExternalTransactionId", "Symbol", "Quantity", "Price", "Description", "SourceReference");

        AssertMembers(typeof(StatementMatchSummaryDto),
            "StatementItemCount", "MatchedItemCount", "AutoMatchedItemCount", "ManualMatchedItemCount",
            "ReviewRequiredItemCount", "BreakCount", "PositionBreakCount", "CashBreakCount",
            "TransactionBreakCount", "HighestUnresolvedTier", "MatchedNotional", "UnmatchedNotional", "BaseCurrency");

        AssertMembers(typeof(StatementBreakDto),
            "BreakId", "BreakType", "Severity", "MatchTier", "StatementReference", "Description",
            "StatementAmount", "BookAmount", "Delta", "EscalationLabel", "EscalationReason", "Tolerance", "Currency", "CreatedAtUtc",
            "Status", "InternalReference", "Owner", "LastObservedAtUtc", "RecommendedAction", "EvidenceLink",
            "SlaDueAtUtc", "SlaWarningAtUtc", "SlaBreachedAtUtc", "SlaState", "Measures");

        AssertMembers(typeof(StatementReconciliationCaseDto),
            "CaseId", "RunId", "Status", "Priority", "Title", "Summary", "BreakIds", "CreatedAtUtc",
            "LastUpdatedAtUtc", "LastUpdatedBy", "Owner", "DueAtUtc", "ResolvedAtUtc", "ResolutionCode",
            "ResolutionSummary", "EvidenceLink", "Disposition", "AgingDays", "CommentThreads",
            "Attachments", "BreakExplanation", "AuditEvents");

        AssertMembers(typeof(StatementReconciliationCaseCommentThreadDto),
            "ThreadId", "Subject", "Comments");

        AssertMembers(typeof(StatementReconciliationCaseCommentDto),
            "CommentId", "Body", "Actor", "CreatedAtUtc", "ParentCommentId");

        AssertMembers(typeof(StatementReconciliationCaseAttachmentDto),
            "AttachmentId", "EvidenceKind", "SourceSystem", "SourceReference", "ContentHash", "Route", "AttachedAtUtc");

        AssertMembers(typeof(StatementReconciliationBreakExplanationDto),
            "Summary", "SourceSystems", "ProbableCause", "LedgerImpact", "SuggestedNextAction", "RequiredSignoffRole",
            "EvidenceLinks");

        AssertMembers(typeof(StatementReconciliationCaseAuditEventDto),
            "EventId", "EventType", "OccurredAtUtc", "Actor", "Detail");
    }

    [Fact]
    public void ReconciliationEnvelope_SerializesCanonicalVersionAndCorrelationShape()
    {
        var envelope = new ReconciliationPayloadEnvelope<ReconciliationRunSummary>(
            PayloadId: "payload-1",
            Schema: new ReconciliationSchemaVersion("ReconciliationRunSummary", 1, 0, 0),
            Correlation: new ReconciliationCorrelationContext("trace-1", "span-1", "parent-1", "workflow-1", "job-1"),
            CreatedAt: DateTimeOffset.Parse("2026-01-05T12:00:00Z"),
            Producer: "workstation-api",
            Direction: "outbound",
            IdempotencyKey: "recon:run-1:v1",
            Payload: new ReconciliationRunSummary(
                "recon-1", "run-1", DateTimeOffset.Parse("2026-01-05T12:00:00Z"),
                null, null, 12, 1, 1, false, 0.01m, 5));

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        Assert.Contains("\"schema\"", json);
        Assert.Contains("\"correlation\"", json);
        Assert.Contains("\"contractName\":\"ReconciliationRunSummary\"", json);
        Assert.Contains("\"major\":1", json);
        Assert.Contains("\"traceId\":\"trace-1\"", json);
        Assert.Contains("\"idempotencyKey\":\"recon:run-1:v1\"", json);
    }

    private static void AssertMembers(Type type, params string[] expectedMembers)
    {
        var actual = type.GetProperties().Select(p => p.Name).OrderBy(x => x).ToArray();
        var expected = expectedMembers.OrderBy(x => x).ToArray();
        Assert.Equal(expected, actual);
    }


    private static StatementRunDto BuildStatementRunDto() => new(
        RunId: "statement-run-1",
        Status: StatementRunStatus.ReviewRequired,
        Source: new StatementSourceDto(
            SourceId: "source-1",
            SourceKind: "custodian",
            SourceSystem: "PrimeBroker",
            AccountId: "acct-1",
            AccountName: "Alpha Prime",
            Currency: "USD",
            PeriodStart: DateOnly.Parse("2026-01-01"),
            PeriodEnd: DateOnly.Parse("2026-01-05"),
            StatementAsOfUtc: DateTimeOffset.Parse("2026-01-05T11:59:00Z")),
        StartedAtUtc: DateTimeOffset.Parse("2026-01-05T12:00:00Z"),
        CompletedAtUtc: null,
        SourceFileName: "custodian-statement.csv",
        SourceFileHash: "sha256:statement-source",
        MappingProfileId: "map-custodian-v1",
        MappingProfileVersion: 3,
        ToleranceProfileId: "tol-equity-cash",
        ToleranceProfileVersion: 2,
        ImportedBy: "ops@example.com",
        ImportedAtUtc: DateTimeOffset.Parse("2026-01-05T12:00:00Z"),
        ValidationIssues:
        [
            new StatementValidationIssueDto(
                IssueId: "issue-1",
                Severity: StatementValidationSeverity.Warning,
                Code: "UNKNOWN_COLUMN",
                Message: "Statement included an unmapped memo column.",
                SourceRowNumber: 4,
                SourceColumn: "memo",
                RecommendedAction: "Review mapping profile before promotion.")
        ],
        Positions:
        [
            new StatementNormalizedPositionDto(
                RowId: "row-position-1",
                AccountId: "acct-1",
                Symbol: "MSFT",
                Quantity: 10m,
                MarketPrice: 420m,
                MarketValue: 4200m,
                Currency: "USD",
                AsOfUtc: DateTimeOffset.Parse("2026-01-05T11:59:00Z"),
                Fingerprint: "fp-position-1",
                Cusip: "594918104")
        ],
        CashBalances:
        [
            new StatementNormalizedCashDto(
                RowId: "row-cash-1",
                AccountId: "acct-1",
                Currency: "USD",
                Amount: 1250m,
                AsOfUtc: DateTimeOffset.Parse("2026-01-05T11:59:00Z"),
                Fingerprint: "fp-cash-1",
                CashType: "settled")
        ],
        Transactions:
        [
            new StatementNormalizedTransactionDto(
                RowId: "row-transaction-1",
                AccountId: "acct-1",
                ActivityType: "buy",
                TradeDate: DateOnly.Parse("2026-01-03"),
                Amount: -4200m,
                Currency: "USD",
                Fingerprint: "fp-transaction-1",
                SettleDate: DateOnly.Parse("2026-01-05"),
                ExternalTransactionId: "txn-1",
                Symbol: "MSFT",
                Quantity: 10m,
                Price: 420m)
        ],
        MatchSummary: new StatementMatchSummaryDto(
            StatementItemCount: 3,
            MatchedItemCount: 2,
            AutoMatchedItemCount: 2,
            ManualMatchedItemCount: 0,
            ReviewRequiredItemCount: 1,
            BreakCount: 1,
            PositionBreakCount: 0,
            CashBreakCount: 1,
            TransactionBreakCount: 0,
            HighestUnresolvedTier: StatementMatchTier.Probable,
            MatchedNotional: 4200m,
            UnmatchedNotional: 25m,
            BaseCurrency: "USD"),
        Breaks:
        [
            new StatementBreakDto(
                BreakId: "break-1",
                BreakType: StatementBreakType.CashBalanceMismatch,
                Severity: StatementValidationSeverity.Warning,
                MatchTier: StatementMatchTier.Probable,
                StatementReference: "row-cash-1",
                Description: "Statement cash differs from book cash by tolerance breach.",
                StatementAmount: 1250m,
                BookAmount: 1225m,
                Delta: 25m,
                Tolerance: 1m,
                Currency: "USD",
                CreatedAtUtc: DateTimeOffset.Parse("2026-01-05T12:01:00Z"),
                Status: "Open")
        ],
        Cases:
        [
            new StatementReconciliationCaseDto(
                CaseId: "case-1",
                RunId: "statement-run-1",
                Status: "Open",
                Priority: "Normal",
                Title: "Cash variance review",
                Summary: "Review the custodian cash balance variance.",
                BreakIds: ["break-1"],
                CreatedAtUtc: DateTimeOffset.Parse("2026-01-05T12:02:00Z"),
                LastUpdatedAtUtc: DateTimeOffset.Parse("2026-01-05T12:02:00Z"),
                LastUpdatedBy: "ops@example.com")
        ],
        ImportId: "import-1",
        FundProfileId: "fund-1",
        FundAccountId: Guid.Parse("22222222-2222-2222-2222-222222222222"));

    private static FundLedgerSummary BuildFundLedgerSummary() => new(
        FundProfileId: "fund-1",
        FundDisplayName: "Alpha Fund",
        ScopeKind: FundLedgerScope.Consolidated,
        ScopeId: null,
        AsOf: DateTimeOffset.Parse("2026-01-05T12:00:00Z"),
        JournalEntryCount: 2,
        LedgerEntryCount: 2,
        AssetBalance: 100m,
        LiabilityBalance: 10m,
        EquityBalance: 90m,
        RevenueBalance: 5m,
        ExpenseBalance: 1m,
        TrialBalance: new[] { new FundTrialBalanceLine("Cash", "Asset", null, null, 100m, 1) },
        Journal: new[] { new FundJournalLine(Guid.Parse("11111111-1111-1111-1111-111111111111"), DateTimeOffset.Parse("2026-01-05T12:00:00Z"), "seed", 100m, 100m, 2) },
        EntityCount: 1,
        SleeveCount: 1,
        VehicleCount: 1);

    private static StrategyRunContinuityDto BuildContinuityDto()
    {
        var summary = new StrategyRunSummary(
            "run-1", "strat-1", "MeanRevert", StrategyRunMode.Paper, StrategyRunEngine.MeridianNative,
            StrategyRunStatus.Completed, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
            null, null, null, null, 1m, 0.1m, 101m, 1, DateTimeOffset.Parse("2026-01-02T00:00:00Z"));
        var detail = new StrategyRunDetail(summary, new Dictionary<string, string>(), null, null);
        var lineage = new StrategyRunContinuityLineage(null, null, Array.Empty<StrategyRunContinuityLink>());
        var status = new StrategyRunContinuityStatus(true, StrategyRunContinuitySeamHealthStatus.Healthy, true, StrategyRunContinuitySeamHealthStatus.Healthy, true, StrategyRunContinuitySeamHealthStatus.Healthy, true, StrategyRunContinuitySeamHealthStatus.Healthy, true, StrategyRunContinuitySeamHealthStatus.Healthy, true, StrategyRunContinuitySeamHealthStatus.Healthy, 0, 0, 0, false, Array.Empty<StrategyRunContinuityWarning>());
        var reconciliation = new ReconciliationRunSummary("recon-1", "run-1", DateTimeOffset.Parse("2026-01-02T00:00:00Z"), null, null, 1, 0, 0, false, 0.01m, 5);
        return new StrategyRunContinuityDto(detail, lineage, null, reconciliation, status);
    }
}
