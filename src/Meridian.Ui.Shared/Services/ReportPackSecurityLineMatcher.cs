using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Shared "does this report line reference a security" matcher for the report-pack restatement path.
/// Extracted so the candidate resolver (<see cref="ReportPackRestatementCandidateResolver"/>) and the
/// durable security→report-line index (<see cref="IReportPackSecurityLineIndex"/>) agree on exactly one
/// membership rule: a report-line provenance entry references a security when its
/// <c>SecurityMasterId</c> or <c>SecurityDefinitionId</c> parses to the security id, or when it carries a
/// security-kind source whose <c>SourceId</c> parses to the security id.
/// </summary>
public static class ReportPackSecurityLineMatcher
{
    /// <summary>True when <paramref name="line"/> ties a report line to <paramref name="securityId"/>.</summary>
    public static bool LineReferencesSecurity(ReportPackLineProvenanceDto line, Guid securityId)
    {
        ArgumentNullException.ThrowIfNull(line);
        return MatchesSecurity(line.SecurityMasterId, securityId)
            || MatchesSecurity(line.SecurityDefinitionId, securityId)
            || (ContainsToken(line.SourceKind, "security") && MatchesSecurity(line.SourceId, securityId));
    }

    /// <summary>True when <paramref name="record"/> has any retained provenance line referencing the security.</summary>
    public static bool RecordReferencesSecurity(ReportPackWorkflowRecordDto record, Guid securityId)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.LineProvenance is { Count: > 0 } provenance
            && provenance.Any(line => LineReferencesSecurity(line, securityId));
    }

    /// <summary>
    /// The distinct security ids a single provenance line contributes — the reverse of
    /// <see cref="LineReferencesSecurity"/>: for any returned id <c>s</c>, <c>LineReferencesSecurity(line, s)</c>
    /// is true, and vice versa. Used to build the security→report-line index.
    /// </summary>
    public static IEnumerable<Guid> ReferencedSecurityIds(ReportPackLineProvenanceDto line)
    {
        ArgumentNullException.ThrowIfNull(line);

        // Dedupe on successfully-parsed ids only. Comparing against the out-variables would wrongly
        // suppress a legitimate all-zeros (Guid.Empty) definition id whenever an earlier source failed
        // to parse (its out-variable also defaults to Guid.Empty) — which would make this the inexact
        // reverse of LineReferencesSecurity and silently drop that record from the index.
        var seen = new HashSet<Guid>();

        if (TryParseSecurity(line.SecurityMasterId, out var fromMaster) && seen.Add(fromMaster))
        {
            yield return fromMaster;
        }

        if (TryParseSecurity(line.SecurityDefinitionId, out var fromDefinition) && seen.Add(fromDefinition))
        {
            yield return fromDefinition;
        }

        if (ContainsToken(line.SourceKind, "security")
            && TryParseSecurity(line.SourceId, out var fromSource)
            && seen.Add(fromSource))
        {
            yield return fromSource;
        }
    }

    private static bool MatchesSecurity(string? value, Guid securityId)
        => TryParseSecurity(value, out var parsed) && parsed == securityId;

    private static bool TryParseSecurity(string? value, out Guid securityId)
    {
        // Behaviour-preserving with the original resolver matcher: non-blank and parseable. Guid.Empty is
        // not special-cased, so the extracted predicate matches the merged resolver exactly (the parity
        // test guards this), and the index's ReferencedSecurityIds stays the exact reverse of it.
        if (!string.IsNullOrWhiteSpace(value) && Guid.TryParse(value, out securityId))
        {
            return true;
        }

        securityId = Guid.Empty;
        return false;
    }

    private static bool ContainsToken(string? value, string token)
        => !string.IsNullOrWhiteSpace(value)
           && value.Contains(token, StringComparison.OrdinalIgnoreCase);
}
