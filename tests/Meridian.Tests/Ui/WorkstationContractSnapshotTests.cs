using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Workstation;
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
        var approvedHash = "C7D6B15F8A4B2A9D7D4D9B6A6D9A5D2A4A0CF6C2B3ED0867B3E8A7C15149D5E3";
        Assert.Equal(approvedHash, actualHash);
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
