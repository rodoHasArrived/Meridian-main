using System.Text.RegularExpressions;
using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

/// <summary>
/// Shared text/redaction and evidence-normalization helpers for the Operations Continuity
/// workflow service and its extracted sub-services (ledger posting, projection). Split out so
/// both the facade and the sub-services reference one copy without a back-dependency.
/// </summary>
internal static class OperationsContinuityWorkflowText
{
    internal static readonly Regex SensitiveAssignmentPattern = new(
        @"\b(?<key>api[_-]?key|secret|token|password|passphrase|client[_-]?secret|private[_-]?key|credential)\s*(?<separator>[:=])\s*(?<value>[^&;\s,]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static readonly Regex BearerTokenPattern = new(
        @"\bbearer\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static readonly Regex BasicAuthUriPattern = new(
        @"(?<scheme>https?://)(?<user>[^/@\s:]+):(?<password>[^/@\s]+)@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static IReadOnlyList<OperationsEvidenceLinkDto> NormalizeEvidence(
        IReadOnlyList<OperationsEvidenceLinkDto>? evidenceLinks) =>
        evidenceLinks?
            .Where(static link => !string.IsNullOrWhiteSpace(link.EvidenceId))
            .Select(static link => link with
            {
                // Evidence ids are the durable lineage key for report packs, incidents, and audit joins.
                EvidenceId = link.EvidenceId.Trim(),
                Label = RedactSensitiveText(link.Label) ?? string.Empty,
                Route = RedactSensitiveText(link.Route),
                Source = RedactSensitiveText(link.Source)
            })
            .ToArray() ?? [];

    internal static string? RedactSensitiveText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var redacted = SensitiveAssignmentPattern.Replace(
            value,
            match => $"{match.Groups["key"].Value}{match.Groups["separator"].Value}[redacted]");
        redacted = BearerTokenPattern.Replace(redacted, "Bearer [redacted]");
        return BasicAuthUriPattern.Replace(redacted, "${scheme}[redacted]@");
    }
}
