using System.Globalization;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

internal static class OperatorInboxPriorityScoringService
{
    public static OperatorWorkItemDto ApplyScore(OperatorWorkItemDto item, DateTimeOffset asOf)
    {
        var severityScore = item.Tone switch
        {
            OperatorWorkItemToneDto.Critical => 600,
            OperatorWorkItemToneDto.Warning => 350,
            OperatorWorkItemToneDto.Info => 120,
            OperatorWorkItemToneDto.Success => 0,
            _ => 0
        };

        var ageHours = Math.Max(0d, (asOf - item.CreatedAt).TotalHours);
        var ageScore = Math.Min(220, (int)Math.Round(ageHours * 4d, MidpointRounding.AwayFromZero));

        var accountScopeScore = item.FundAccountId.HasValue ? 80 : 20;
        var replayStalenessScore = item.Kind == OperatorWorkItemKindDto.PaperReplay ? 140 : 0;
        var reconciliationScore = item.Kind == OperatorWorkItemKindDto.ReconciliationBreak
            ? Math.Min(180, (int)Math.Round(ageHours * 6d, MidpointRounding.AwayFromZero))
            : 0;

        var total = severityScore + ageScore + accountScopeScore + replayStalenessScore + reconciliationScore;
        var explanation =
            $"severity={severityScore}; ageHours={ageHours.ToString("0.0", CultureInfo.InvariantCulture)} => {ageScore}; " +
            $"accountScope={(item.FundAccountId.HasValue ? "scoped" : "global")} => {accountScopeScore}; " +
            $"replay={(replayStalenessScore > 0 ? "stale-risk" : "n/a")} => {replayStalenessScore}; " +
            $"reconciliationAge={reconciliationScore}.";

        return item with
        {
            PriorityScore = total,
            PriorityExplanation = explanation
        };
    }
}
