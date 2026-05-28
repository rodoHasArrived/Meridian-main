using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Services;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class WorkstationEndpointContractCompatibilityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    [Fact]
    public void WorkstationContracts_Fingerprint_ShouldMatchApprovedSnapshot()
    {
        var descriptor = BuildDescriptor(
        [
            typeof(TradingOperatorReadinessDto),
            typeof(OperatorInboxDto),
            typeof(TradingAcceptanceGateDto),
            typeof(OperatorWorkItemDto),
            typeof(TradingReplayReadinessDto),
            typeof(TradingReportPackReadinessDto),
            typeof(PaperSessionReplayVerificationDto)
        ]);

        var hash = ComputeSha256(descriptor);
        hash.Should().Be("E102CE3B4B4AA43D85069E6429DE4D24A60A3A8E8BE22CEE7158257221A90F51");
    }

    [Fact]
    public void WorkstationEnums_ShouldRemainBackwardCompatibleForExistingMembers()
    {
        AssertEnumHasStableMembers<TradingAcceptanceGateStatusDto>(new Dictionary<string, int>
        {
            ["Ready"] = 0,
            ["ReviewRequired"] = 1,
            ["Blocked"] = 2,
            ["Unknown"] = 99
        });

        AssertEnumHasStableMembers<OperatorWorkItemKindDto>(new Dictionary<string, int>
        {
            ["PaperReplay"] = 0,
            ["PromotionReview"] = 1,
            ["BrokerageSync"] = 2,
            ["SecurityMasterCoverage"] = 3,
            ["ReconciliationBreak"] = 4,
            ["ReportPackApproval"] = 5,
            ["ProviderTrustGate"] = 6,
            ["ExecutionControl"] = 7,
            ["LedgerPeriodClose"] = 8
        });

        AssertEnumHasStableMembers<OperatorWorkItemToneDto>(new Dictionary<string, int>
        {
            ["Info"] = 0,
            ["Success"] = 1,
            ["Warning"] = 2,
            ["Critical"] = 3
        });
    }

    [Fact]
    public void WorkstationPayloadShapes_ShouldAllowAdditiveOnlyChangesForCriticalPayloads()
    {
        var readinessShape = ExtractTopLevelProperties(CreateReadinessFixture());
        readinessShape.Should().Contain(new[]
        {
            "asOf", "activeSession", "sessions", "replay", "controls", "promotion", "trustGate", "brokerageSync",
            "workItems", "warnings", "overallStatus", "readyForPaperOperation", "acceptanceGates", "reportPack",
            "evidenceCompleteness", "snapshotMaterializedAt", "snapshotVersion", "providerPromotionChecklist"
        });

        var inboxShape = ExtractTopLevelProperties(CreateInboxFixture());
        inboxShape.Should().Contain(new[] { "asOf", "items", "criticalCount", "warningCount", "reviewCount", "summary", "workItems" });

        var replayShape = ExtractTopLevelProperties(CreateReplayFixture());
        replayShape.Should().Contain(new[]
        {
            "summary", "verifiedAt", "isConsistent", "mismatchReasons", "replaySource", "symbols",
            "verifiedFilledCount", "verifiedOrderCount", "verifiedLedgerEntriesCount", "comparedFillCount",
            "comparedOrderCount", "comparedLedgerEntryCount", "corruptLedgerEntryCount", "corruptLedgerEntryIds",
            "currentPortfolio", "replayPortfolio", "lastPersistedFillAt", "lastPersistedOrderUpdateAt",
            "verificationAuditId", "lastVerifiedAt"
        });
    }

    private static string[] ExtractTopLevelProperties<T>(T payload)
    {
        var node = JsonSerializer.SerializeToNode(payload, JsonOptions).AsObject();
        return node.Select(static kvp => kvp.Key).OrderBy(static x => x, StringComparer.Ordinal).ToArray();
    }

    private static TradingOperatorReadinessDto CreateReadinessFixture() => new(
        DateTimeOffset.UnixEpoch,
        ActiveSession: null,
        Sessions: [],
        Replay: null,
        Controls: new TradingControlReadinessDto(false, null, null, null, 0, 0, null),
        Promotion: null,
        TrustGate: new TradingTrustGateReadinessDto("dk1", "pending", false, true, "missing", null, null, null, 1, 0, 0, [], [], "pending"),
        BrokerageSync: null,
        WorkItems: [],
        Warnings: [])
    {
        OverallStatus = TradingAcceptanceGateStatusDto.ReviewRequired,
        ReadyForPaperOperation = false,
        AcceptanceGates = [],
        ReportPack = null,
        EvidenceCompleteness = null,
        SnapshotMaterializedAt = DateTimeOffset.UnixEpoch,
        SnapshotVersion = "v1"
    };

    private static OperatorInboxDto CreateInboxFixture() => new(DateTimeOffset.UnixEpoch, [], 0, 0, 0, "ok");

    private static PaperSessionReplayVerificationDto CreateReplayFixture() => new(
        Summary: new PaperSessionSummaryDto("session", "strategy", "name", 1_000m, DateTimeOffset.UnixEpoch, null, true),
        Symbols: [],
        ReplaySource: "DurableFillLog",
        IsConsistent: true,
        MismatchReasons: [],
        CurrentPortfolio: new ExecutionPortfolioSnapshotDto(1_000m, 1_000m, 0m, 0m, [], DateTimeOffset.UnixEpoch),
        ReplayPortfolio: new ExecutionPortfolioSnapshotDto(1_000m, 1_000m, 0m, 0m, [], DateTimeOffset.UnixEpoch),
        VerifiedAt: DateTimeOffset.UnixEpoch,
        ComparedFillCount: 0,
        ComparedOrderCount: 0,
        ComparedLedgerEntryCount: 0,
        CorruptLedgerEntryCount: 0,
        CorruptLedgerEntryIds: [],
        LastPersistedFillAt: null,
        LastPersistedOrderUpdateAt: null,
        VerificationAuditId: "audit-1");

    private static void AssertEnumHasStableMembers<TEnum>(IReadOnlyDictionary<string, int> stableMembers)
        where TEnum : struct, Enum
    {
        var actual = Enum.GetValues<TEnum>().ToDictionary(static v => v.ToString(), static v => Convert.ToInt32(v));
        foreach (var (name, numericValue) in stableMembers)
        {
            actual.Should().ContainKey(name);
            actual[name].Should().Be(numericValue);
        }
    }

    private static string BuildDescriptor(IEnumerable<Type> types)
    {
        var sb = new StringBuilder();
        foreach (var type in types.OrderBy(static t => t.FullName, StringComparer.Ordinal))
        {
            sb.AppendLine(type.FullName);
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .OrderBy(static p => p.Name, StringComparer.Ordinal))
            {
                sb.Append(property.Name).Append(':').AppendLine(property.PropertyType.FullName ?? property.PropertyType.Name);
            }
        }

        return sb.ToString();
    }

    private static string ComputeSha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
