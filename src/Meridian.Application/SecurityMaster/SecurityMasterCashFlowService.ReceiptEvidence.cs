using System.Text.Json;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed partial class SecurityMasterCashFlowService
{
    /// <summary>Portable exact receipt bytes for independent retained report evidence, preserving historical row contracts.</summary>
    public static string ExportReceiptEvidence(SecurityCashFlowCalculationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.ContentSha256 != ReceiptHash(receipt))
            throw new InvalidDataException("The calculation receipt content digest is invalid.");
        return JsonSerializer.Serialize(receipt, SecurityCashFlowReceiptJsonContext.Default.SecurityCashFlowCalculationReceipt);
    }
}
