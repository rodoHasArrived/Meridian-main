using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// The governed historical-identifier-window proposal carried by a workbench revision. A revision
/// whose <c>FieldPath</c> is <see cref="FieldPath"/> stages NO overlay value: its
/// <c>FieldValue</c> is the serialized <see cref="SecurityMasterHistoricalIdentifierWindowPayload"/>,
/// which the published-revision handler
/// (<see cref="ApprovedHistoricalIdentifierWindowHandler"/>) applies to the canonical record as a
/// governed amendment keeping the proposed <c>ValidFrom</c>. The path is reserved: the generic
/// field-edit route refuses it, so a window can only be staged through the proposal command, which
/// requires the evidence reference and justification.
/// </summary>
public static class SecurityMasterHistoricalIdentifierWindow
{
    /// <summary>Reserved revision field path for a historical identifier window proposal.</summary>
    public const string FieldPath = "identifiers.historicalWindow";

    /// <summary>True when <paramref name="fieldPath"/> names the reserved window path.</summary>
    public static bool IsWindowPath(string? fieldPath)
        => fieldPath is not null && string.Equals(fieldPath.Trim(), FieldPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="revision"/> is a historical-identifier-window proposal.</summary>
    public static bool IsWindowRevision(SecurityMasterRevisionRecord? revision)
        => revision is not null && IsWindowPath(revision.FieldPath);

    internal static string Serialize(SecurityMasterHistoricalIdentifierWindowPayload payload)
        => JsonSerializer.Serialize(payload, SecurityMasterHistoricalIdentifierWindowJsonContext.Default.SecurityMasterHistoricalIdentifierWindowPayload);

    /// <summary>
    /// Parses and re-validates a recorded payload. A payload that is missing, unparseable, or no
    /// longer satisfies the proposal invariants is refused: publish applies only what the proposal
    /// command admitted.
    /// </summary>
    internal static SecurityMasterHistoricalIdentifierWindowPayload Parse(Guid revisionId, string? recorded)
    {
        SecurityMasterHistoricalIdentifierWindowPayload? payload = null;
        if (!string.IsNullOrWhiteSpace(recorded))
        {
            try
            {
                payload = JsonSerializer.Deserialize(
                    recorded,
                    SecurityMasterHistoricalIdentifierWindowJsonContext.Default.SecurityMasterHistoricalIdentifierWindowPayload);
            }
            catch (JsonException)
            {
                payload = null;
            }
        }

        if (payload is null)
        {
            throw new InvalidOperationException(
                $"Revision '{revisionId:D}' does not carry a readable historical identifier window proposal.");
        }

        var problem = Validate(payload.Kind, payload.Value, payload.Provider, payload.ValidFrom, payload.ValidTo,
            payload.EvidenceReference, payload.Justification);
        if (problem is not null)
        {
            throw new InvalidOperationException(
                $"Revision '{revisionId:D}' carries an invalid historical identifier window proposal: {problem}");
        }

        return payload;
    }

    /// <summary>
    /// The proposal invariants, or null when they hold. The window must start in the past (it is
    /// historical), end after it starts when it ends at all, and cite evidence and a justification.
    /// </summary>
    internal static string? Validate(
        SecurityIdentifierKind kind,
        string? value,
        string? provider,
        DateTimeOffset validFrom,
        DateTimeOffset? validTo,
        string? evidenceReference,
        string? justification,
        DateTimeOffset? now = null)
    {
        if (!Enum.IsDefined(kind))
        {
            return $"identifier kind '{kind}' is not recognized.";
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return "an identifier value is required.";
        }

        if (kind == SecurityIdentifierKind.ProviderSymbol && string.IsNullOrWhiteSpace(provider))
        {
            return "a ProviderSymbol window must name its provider.";
        }

        if (validTo is { } end && end <= validFrom)
        {
            return "ValidTo must be later than ValidFrom.";
        }

        if (now is { } current && validFrom >= current)
        {
            return "a historical window must start before the time it is proposed.";
        }

        if (string.IsNullOrWhiteSpace(evidenceReference))
        {
            return "an Evidence Vault reference is required.";
        }

        if (string.IsNullOrWhiteSpace(justification))
        {
            return "a justification is required.";
        }

        return null;
    }
}

/// <summary>
/// The historical identifier window a proposal revision governs, recorded verbatim in the revision's
/// field value so the approved window, its evidence, and its proposer are durable with the revision.
/// </summary>
public sealed record SecurityMasterHistoricalIdentifierWindowPayload(
    SecurityIdentifierKind Kind,
    string Value,
    string? Provider,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    string EvidenceReference,
    string Justification,
    string ProposedBy);

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(SecurityMasterHistoricalIdentifierWindowPayload))]
internal sealed partial class SecurityMasterHistoricalIdentifierWindowJsonContext : JsonSerializerContext;
