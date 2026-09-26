using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Serialization;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class WorkstationContractSnapshotTests
{
    private static readonly Type[] DashboardCriticalContractTypes =
    [
        typeof(TradingOperatorReadinessDto),
        typeof(TradingLiveOperationRequirementDto),
        typeof(TradingAcceptanceGateDto),
        typeof(OperatorWorkItemDto),
        typeof(OperatorInboxDto),
        typeof(WorkflowActionDto)
    ];

    [Fact]
    public void DashboardCriticalContracts_Fingerprint_ShouldMatchApprovedSnapshot()
    {
        var descriptor = BuildDescriptor();
        var actualHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(descriptor)));
        var approvedHash = "E6E6285EB5CF18520C1C033BBFDD9824B314870D057E5318FDF0309AE18590B7";
        Assert.Equal(approvedHash, actualHash);
    }

    [Fact]
    public void WorkstationOperationsJsonContext_ShouldCoverActiveOperationsContinuityContracts()
    {
        var context = WorkstationOperationsJsonContext.Default;

        Assert.Same(typeof(OperationsContinuityWorkflowDto), context.OperationsContinuityWorkflowDto.Type);
        Assert.Same(typeof(OperationsAccountingRecordSummaryDto), context.OperationsAccountingRecordSummaryDto.Type);
        Assert.Same(typeof(OperationsAccountingRecordEvidenceCategoryDto), context.OperationsAccountingRecordEvidenceCategoryDto.Type);
        Assert.Same(typeof(OperationsTransitionResultDto), context.OperationsTransitionResultDto.Type);
        Assert.Same(typeof(OperationsLedgerPostRequestDto), context.OperationsLedgerPostRequestDto.Type);
        Assert.Same(typeof(IReadOnlyList<OperationsTimelineEntryDto>), context.IReadOnlyListOperationsTimelineEntryDto.Type);

        var statusJson = JsonSerializer.Serialize(
            OperationsWorkflowStatusDto.CollectingBrokerData,
            context.OperationsWorkflowStatusDto);
        Assert.Equal("\"CollectingBrokerData\"", statusJson);

        var resultJson = JsonSerializer.Serialize(
            new OperationsTransitionResultDto(
                Success: true,
                ErrorCode: null,
                ErrorMessage: null,
                Workflow: null,
                Blockers: [],
                NextActions: []),
            context.OperationsTransitionResultDto);
        Assert.Contains("\"success\":true", resultJson, StringComparison.Ordinal);
        Assert.Contains("\"blockers\":[]", resultJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ReconciliationBreakQueueStatus.Open, "Open", 0)]
    [InlineData(ReconciliationBreakQueueStatus.InReview, "InReview", 1)]
    [InlineData(ReconciliationBreakQueueStatus.Resolved, "Resolved", 2)]
    [InlineData(ReconciliationBreakQueueStatus.Dismissed, "Dismissed", 3)]
    [InlineData(ReconciliationBreakQueueStatus.SignedOff, "SignedOff", 4)]
    public void ReconciliationBreakQueueItem_SeededCashVariance_UsesTextStatusAndReadsLegacyNumbers(
        ReconciliationBreakQueueStatus status,
        string expectedName,
        int legacyValue)
    {
        var detectedAt = new DateTimeOffset(2026, 9, 4, 16, 0, 0, TimeSpan.Zero);
        var item = new ReconciliationBreakQueueItem(
            BreakId: "SAMPLE-BRK-001",
            RunId: "SAMPLE-RUN-COVERED-CALL",
            StrategyName: "Northstar Sample Portfolio",
            Category: ReconciliationBreakCategory.CashMismatch,
            Status: status,
            Variance: 1_250m,
            Reason: "Custodian cash balance is $1,250.00 above the internal ledger for the sample account.",
            AssignedTo: null,
            DetectedAt: detectedAt,
            LastUpdatedAt: detectedAt,
            Severity: ReconciliationBreakSeverity.High,
            SourceType: "seeded",
            SourceSystem: "Meridian Seeded Demo");
        var typeInfo = WorkstationOperationsJsonContext.Default.ReconciliationBreakQueueItem;
        var endpointOptions = UiEndpoints.CreateEndpointJsonOptions();
        var generatedJson = JsonSerializer.Serialize(item, typeInfo);
        var endpointJson = JsonSerializer.Serialize(item, endpointOptions);

        foreach (var json in new[] { generatedJson, endpointJson })
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Assert.Equal(JsonValueKind.String, root.GetProperty("status").ValueKind);
            Assert.Equal(expectedName, root.GetProperty("status").GetString());
            Assert.Equal("SAMPLE-BRK-001", root.GetProperty("breakId").GetString());
            Assert.Equal(1_250m, root.GetProperty("variance").GetDecimal());
        }

        var legacyPayload = JsonNode.Parse(generatedJson)!;
        legacyPayload["status"] = legacyValue;
        var legacyJson = legacyPayload.ToJsonString();

        foreach (var restored in new[]
        {
            JsonSerializer.Deserialize(legacyJson, typeInfo),
            JsonSerializer.Deserialize<ReconciliationBreakQueueItem>(legacyJson, endpointOptions)
        })
        {
            Assert.NotNull(restored);
            Assert.Equal(status, restored.Status);
            Assert.Equal("SAMPLE-BRK-001", restored.BreakId);
            Assert.Equal(1_250m, restored.Variance);
        }
    }

    private static string BuildDescriptor()
    {
        var sb = new StringBuilder();
        foreach (var type in DashboardCriticalContractTypes.OrderBy(static t => t.FullName, StringComparer.Ordinal))
        {
            AppendSnapshotLine(sb, type.FullName ?? type.Name);
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .OrderBy(static p => p.Name, StringComparer.Ordinal))
            {
                AppendSnapshotLine(sb, $"{property.Name}:{property.PropertyType.FullName ?? property.PropertyType.Name}");
            }

            if (type.IsEnum)
            {
                foreach (var name in Enum.GetNames(type).OrderBy(static x => x, StringComparer.Ordinal))
                {
                    AppendSnapshotLine(sb, $"enum:{name}");
                }
            }
        }

        return sb.ToString();
    }

    private static void AppendSnapshotLine(StringBuilder sb, string value)
    {
        sb.Append(value).Append('\n');
    }
}
