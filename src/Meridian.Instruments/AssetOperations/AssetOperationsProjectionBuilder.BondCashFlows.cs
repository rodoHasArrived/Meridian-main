using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Instruments.AssetOperations;

public static partial class AssetOperationsProjectionBuilder
{
    internal static Guid BondIdentity(Guid securityId, string key)
        => new(Sha256Digest.ComputeBytesUtf8($"meridian/bond-assets/v2/{securityId:N}/{key}").AsSpan(0, 16));

    internal static string BondProjectionInputKey(StructuredCashFlowProjectionDto? projection)
        => projection is null ? "legacy" : Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            projection.SourceKind,
            projection.SourceLastUpdatedUtc,
            projection.ScheduleEvidence,
            projection.InputManifest
        }));

    /// <summary>
    /// Adapts canonical projection amounts without reinterpreting rates, dates or principal. Dates
    /// not retained by the source (such as accrual boundaries) remain absent rather than invented.
    /// </summary>
    internal static IReadOnlyList<AssetProjectedCashFlowDto> MapCanonicalBondCashFlows(
        SecurityDetailDto security, StructuredCashFlowProjectionDto projection, Guid runId)
    {
        if (projection.BlockedReason is not null)
        {
            return [];
        }

        var result = new List<AssetProjectedCashFlowDto>();
        var ordinal = 0;
        foreach (var entry in projection.Schedule)
        {
            var date = DateOnly.FromDateTime(entry.PeriodDate.UtcDateTime.Date);
            ordinal++;
            if (entry.InterestAmount != 0m)
            {
                Add("Coupon", entry.InterestAmount);
            }
            if (entry.PrincipalAmount != 0m)
            {
                Add(date == projection.TermsUsed?.MaturityDate ? "Maturity" : "PrincipalRepayment", entry.PrincipalAmount);
            }

            void Add(string kind, decimal amount)
                => result.Add(new AssetProjectedCashFlowDto(
                    SchedulePeriodId.Derive(security.SecurityId,
                        entry.UnadjustedStart.HasValue ? $"canonical:{kind}" : $"canonical:{kind}:{ordinal}",
                        entry.UnadjustedStart ?? date, entry.UnadjustedEnd ?? date),
                    runId, security.SecurityId, result.Count + 1, kind, date, amount, security.Currency,
                    "Projected", entry.AccrualStart, entry.AccrualEnd,
                    SourceDomain: "SecurityMaster", SourceEntityId: security.SecurityId.ToString("D"),
                    ExtensionPayload: JsonSerializer.SerializeToElement(new
                    {
                        engineVersion = "asset-obligation-declared-v2",
                        termsVersion = security.Version,
                        projection.SourceKind,
                        projection.Staleness,
                        projection.ScheduleEvidence,
                        entry.Factor
                    })));
        }
        return result;
    }
}
