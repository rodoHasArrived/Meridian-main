using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Serialization;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class WorkstationContractSnapshotTests
{
    private static readonly Type[] DashboardCriticalContractTypes =
    [
        typeof(TradingOperatorReadinessDto),
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
        var approvedHash = "DEE1D9396E74FB9202798AB08849C13498E0C99B8C7541783ACBD0D1E4895858";
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

    private static string BuildDescriptor()
    {
        var sb = new StringBuilder();
        foreach (var type in DashboardCriticalContractTypes.OrderBy(static t => t.FullName, StringComparer.Ordinal))
        {
            sb.AppendLine(type.FullName);
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .OrderBy(static p => p.Name, StringComparer.Ordinal))
            {
                sb.Append(property.Name).Append(':').AppendLine(property.PropertyType.FullName ?? property.PropertyType.Name);
            }

            if (type.IsEnum)
            {
                foreach (var name in Enum.GetNames(type).OrderBy(static x => x, StringComparer.Ordinal))
                {
                    sb.Append("enum:").AppendLine(name);
                }
            }
        }

        return sb.ToString();
    }
}
