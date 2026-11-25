using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>An explicitly retained election cutoff. DeadlineAt preserves the source offset.</summary>
public sealed record CorporateActionElectionCutoffDto(
    string Id,
    string Label,
    DateTimeOffset DeadlineAt,
    string Source,
    string EvidenceReference);

/// <summary>Read-only source facts, independent of case SLA, ex-date and accounting authority.</summary>
public sealed record CorporateActionElectionCalendarDto(
    string EvidenceState,
    IReadOnlyList<CorporateActionElectionCutoffDto> Cutoffs,
    IReadOnlyList<string> Messages);

/// <summary>
/// Reads only the versioned electionCalendar companion retained in the provider action payload.
/// No offer expiry, ex-date, record date, or operational due date is promoted to an election cutoff.
/// A malformed declared calendar is unavailable as a whole, preventing a partial set of deadlines
/// from appearing complete. Empty declared calendars explicitly mean no supplied cutoffs.
/// </summary>
public static partial class CorporateActionElectionCalendar
{
    public static CorporateActionElectionCalendarDto Read(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } envelope
            || !envelope.TryGetProperty("electionCalendar", out var calendar))
        {
            return new("MissingEvidence", [], ["Actual election cutoffs were not retained by the source."]);
        }

        if (calendar.ValueKind != JsonValueKind.Object
            || !calendar.TryGetProperty("schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var schemaVersion) || schemaVersion != 1
            || !calendar.TryGetProperty("cutoffs", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            return Invalid("The retained election calendar has an unsupported or invalid schema.");
        }

        var cutoffs = new List<CorporateActionElectionCutoffDto>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows.EnumerateArray())
        {
            var id = Text(row, "id");
            var label = Text(row, "label");
            var deadline = Text(row, "deadlineAt");
            var source = Text(row, "source");
            var evidence = Text(row, "evidenceReference");
            if (id is null || label is null || source is null || evidence is null
                || !identities.Add(id)
                || !CorporateActionEvidenceKinds.IsTrustedReference(evidence)
                || deadline is null || !OffsetTimestamp().IsMatch(deadline)
                || !DateTimeOffset.TryParse(deadline, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var at))
            {
                return Invalid("A retained election cutoff lacks a unique identity, explicit timestamp offset, source or trusted evidence reference.");
            }

            cutoffs.Add(new(id, label, at, source, evidence));
        }

        return new("Available", cutoffs.OrderBy(static cutoff => cutoff.DeadlineAt).ToArray(), []);
    }

    private static string? Text(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim() : null;

    private static CorporateActionElectionCalendarDto Invalid(string message) => new("InvalidEvidence", [], [message]);

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex OffsetTimestamp();
}
