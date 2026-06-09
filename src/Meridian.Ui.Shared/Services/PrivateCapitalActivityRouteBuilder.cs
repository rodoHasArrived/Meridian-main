using Meridian.Contracts.Api;

namespace Meridian.Ui.Shared.Services;

internal static class PrivateCapitalActivityRouteBuilder
{
    private const string EvidenceSubjectKind = "private-capital-fund-event";

    public static string Build(
        string fundProfileId,
        string fundEventId,
        string? capitalAccountId = null,
        string? investorId = null)
    {
        var query = new List<string>
        {
            $"fundProfileId={Uri.EscapeDataString(fundProfileId.Trim())}",
            $"fundEventId={Uri.EscapeDataString(fundEventId.Trim())}"
        };

        if (!string.IsNullOrWhiteSpace(capitalAccountId))
        {
            query.Add($"capitalAccountId={Uri.EscapeDataString(capitalAccountId.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(investorId))
        {
            query.Add($"investorId={Uri.EscapeDataString(investorId.Trim())}");
        }

        return UiApiRoutes.WithQuery(UiApiRoutes.LedgerPrivateCapitalActivity, string.Join("&", query));
    }

    public static string BuildEvidenceRoute(string fundEventId)
        => UiApiRoutes.WithParam(
            UiApiRoutes.WithParam(
                UiApiRoutes.WorkstationEvidenceSubjectPacket,
                "subjectKind",
                EvidenceSubjectKind),
            "subjectId",
            fundEventId);

    public static string? BuildApprovalRoute(
        string fundProfileId,
        Guid journalEntryId,
        string? approvalId)
    {
        if (string.IsNullOrWhiteSpace(approvalId))
        {
            return null;
        }

        var query = new[]
        {
            $"fundProfileId={Uri.EscapeDataString(fundProfileId.Trim())}",
            $"journalEntryId={Uri.EscapeDataString(journalEntryId.ToString("D"))}",
            $"approvalId={Uri.EscapeDataString(approvalId.Trim())}"
        };

        return UiApiRoutes.WithQuery(UiApiRoutes.LedgerManualJournalEntryWorkbench, string.Join("&", query));
    }
}
