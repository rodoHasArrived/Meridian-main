using System;
using System.Collections.Generic;

namespace Meridian.Application.Reconciliation;

public enum ReconciliationContractVersion
{
    V1 = 1,
}

public static class ReconciliationContractCatalog
{
    public const string BreakWorkflowContract = "meridian.reconciliation.break-workflow";

    private static readonly IReadOnlyDictionary<string, ReconciliationContractVersion> SupportedContracts =
        new Dictionary<string, ReconciliationContractVersion>(StringComparer.Ordinal)
        {
            [BreakWorkflowContract] = ReconciliationContractVersion.V1,
        };

    public static bool IsSupported(string contractName, ReconciliationContractVersion requestedVersion)
    {
        if (string.IsNullOrWhiteSpace(contractName))
        {
            return false;
        }

        return SupportedContracts.TryGetValue(contractName, out var supportedVersion)
               && supportedVersion == requestedVersion;
    }
}
