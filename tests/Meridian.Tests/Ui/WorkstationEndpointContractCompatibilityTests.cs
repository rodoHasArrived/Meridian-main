using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Services;
using FsCheck.Xunit;
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

    [Property(MaxTest = 200)]
    public void Scenario_WorkstationJsonContracts_GeneratedOperatorWorkItemsRoundTripWithOptionalNulls(
        int kindSeed,
        int toneSeed,
        int optionalShapeSeed)
    {
        var kind = Pick(Enum.GetValues<OperatorWorkItemKindDto>(), kindSeed);
        var tone = Pick(Enum.GetValues<OperatorWorkItemToneDto>(), toneSeed);
        var workItemId = $"item-{Math.Abs((long)kindSeed) % 10_000}";
        var includeOptionalNulls = Math.Abs((long)optionalShapeSeed) % 2 == 0;
        var optionalJson = includeOptionalNulls
            ? """,
              "runId": null,
              "workspace": "Trading",
              "targetRoute": null,
              "targetPageTag": "ProviderHealth",
              "scope": null,
              "priorityExplanation": null
              """
            : string.Empty;
        var json = $$"""
            {
              "workItemId": "{{workItemId}}",
              "kind": "{{kind}}",
              "label": "Generated {{kind}}",
              "detail": "Generated contract item",
              "tone": "{{tone}}",
              "createdAt": "2026-02-11T13:30:00+00:00",
              "priorityScore": 42{{optionalJson}}
            }
            """;

        var item = JsonSerializer.Deserialize<OperatorWorkItemDto>(json, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<OperatorWorkItemDto>(
            JsonSerializer.Serialize(item, JsonOptions),
            JsonOptions);

        roundTripped.Should().NotBeNull();
        roundTripped!.WorkItemId.Should().Be(workItemId);
        roundTripped.Kind.Should().Be(kind);
        roundTripped.Tone.Should().Be(tone);
        roundTripped.Title.Should().Be($"Generated {kind}");
        roundTripped.Description.Should().Be("Generated contract item");
        roundTripped.PriorityScore.Should().Be(42);
        if (includeOptionalNulls)
        {
            roundTripped.Workspace.Should().Be("Trading");
            roundTripped.TargetPageTag.Should().Be("ProviderHealth");
            roundTripped.RunId.Should().BeNull();
            roundTripped.TargetRoute.Should().BeNull();
        }
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

    private static T Pick<T>(IReadOnlyList<T> values, int seed)
        => values[(int)(Math.Abs((long)seed) % values.Count)];
}
