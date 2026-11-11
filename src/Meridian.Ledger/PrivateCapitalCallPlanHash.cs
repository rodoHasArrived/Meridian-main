using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;

namespace Meridian.Ledger;

/// <summary>Versioned digest of the reviewed allocation, excluding audit clock and subsequent posting observations.</summary>
public static class PrivateCapitalCallPlanHash
{
    public static string Compute(PrivateCapitalCallDto call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var canonical = new
        {
            Version = 1,
            Scope = PrivateCapitalAuthorityRules.NormalizeScope(call.Scope),
            CallId = PrivateCapitalAuthorityRules.Id(call.CallId, nameof(call.CallId)),
            call.PeriodId,
            call.NoticeDate,
            call.DueDate,
            call.AmountToCall,
            call.Purpose,
            Lines = call.Lines.OrderBy(x => x.CommitmentId, StringComparer.Ordinal).Select(x => new
            {
                x.InstallmentId,
                x.CommitmentId,
                x.CommitmentRevision,
                x.InvestorId,
                x.CapitalAccountId,
                x.Amount,
                x.UncalledBefore,
                x.UncalledAfter
            })
        };
        return Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(canonical));
    }
}
