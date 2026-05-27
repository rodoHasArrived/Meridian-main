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
