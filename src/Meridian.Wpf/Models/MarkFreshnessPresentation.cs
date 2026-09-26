using System.Globalization;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Models;

/// <summary>Formats the shared assessment without deriving a desktop freshness policy.</summary>
public sealed class MarkFreshnessPresentation(MarkFreshnessAssessmentDto? assessment)
{
    public bool ReviewRequired => assessment?.Status != "Current";
    public string Label => ReviewRequired ? "Review required" : "Current";
    public string Tone => ReviewRequired ? WorkspaceTone.Warning : WorkspaceTone.Success;
    public string ObservedOn => assessment?.ObservedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Unknown";
    public string Age => assessment?.AgeDays is { } days ? $"{days} day(s)" : "Unknown";
    public string ValuationDate => assessment?.ValuationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Unknown";
    public string PolicyVersion => assessment?.PolicyVersion ?? "Unavailable";
    public string Reason => assessment?.BlockReason ?? (ReviewRequired
        ? "Shared mark assessment unavailable. Refresh valuation evidence before approval."
        : "Mark observation is eligible under the shared valuation policy.");
    public string RecordedValue(decimal value) => ReviewRequired ? $"{value:C2} (review required)" : value.ToString("C2");
}
