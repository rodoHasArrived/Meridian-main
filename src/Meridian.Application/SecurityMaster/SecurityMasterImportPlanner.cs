using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>How a dry-run classifies one import row (review C4).</summary>
public enum SecurityMasterImportRowDisposition
{
    /// <summary>No security claims any of the row's identifiers; importing creates one.</summary>
    Create,

    /// <summary>One security claims the row and its terms already match.</summary>
    Unchanged,

    /// <summary>One security claims the row and its terms differ; the diff needs a governed amendment.</summary>
    AmendProposal,

    /// <summary>The row's identifiers are claimed by more than one security; nothing is picked.</summary>
    Ambiguous,

    /// <summary>The row carries no usable identifier.</summary>
    Invalid
}

/// <summary>One classified import row.</summary>
public sealed record SecurityMasterImportPlanRow(
    int RowIndex,
    string? PrimaryIdentifier,
    SecurityMasterImportRowDisposition Disposition,
    Guid? MatchedSecurityId,
    IReadOnlyList<Guid> CandidateSecurityIds,
    IReadOnlyList<string> ChangedFields,
    string? Message);

/// <summary>A dry-run of an import file: nothing is written.</summary>
public sealed record SecurityMasterImportPlan(
    IReadOnlyList<SecurityMasterImportPlanRow> Rows,
    IReadOnlyList<string> ParseErrors)
{
    public int Count(SecurityMasterImportRowDisposition disposition)
        => Rows.Count(row => row.Disposition == disposition);
}

/// <summary>
/// Review C4: match-first import planning. Each row resolves through the canonical identity claims
/// (provider is identity only for provider-scoped kinds) and is classified without writing anything.
/// A corrected file therefore shows its rows as amend proposals with the changed fields, instead of
/// the create-only import silently counting them as "skipped, already mastered".
/// </summary>
public sealed class SecurityMasterImportPlanner
{
    private readonly SecurityMasterCsvParser _csvParser;
    private readonly ISecurityMasterStore _store;

    public SecurityMasterImportPlanner(SecurityMasterCsvParser csvParser, ISecurityMasterStore store)
    {
        _csvParser = csvParser ?? throw new ArgumentNullException(nameof(csvParser));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<SecurityMasterImportPlan> PlanAsync(string fileContent, string fileExtension, string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var now = DateTimeOffset.UtcNow;
        var extension = (fileExtension ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        List<CreateSecurityRequest> requests;
        var parseErrors = new List<string>();
        try
        {
            if (extension == "csv")
            {
                requests = _csvParser.Parse(fileContent, out var csvErrors, actor, now)
                    .Select(request => SecurityMasterImportService.StampImportAuthority(request, actor, now))
                    .ToList();
                parseErrors.AddRange(csvErrors);
            }
            else if (extension == "json")
            {
                requests = (JsonSerializer.Deserialize(fileContent, SecurityMasterJsonContext.Default.ListCreateSecurityRequest) ?? [])
                    .Select(request => SecurityMasterImportService.StampImportAuthority(request, actor, now))
                    .ToList();
            }
            else
            {
                throw new FormatException($"Unsupported file extension: {fileExtension}");
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return new SecurityMasterImportPlan([], [ex.Message]);
        }

        var rows = new List<SecurityMasterImportPlanRow>(requests.Count);
        foreach (var (request, index) in requests.Select((request, index) => (request, index)))
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(await ClassifyAsync(index, request, now, ct).ConfigureAwait(false));
        }

        return new SecurityMasterImportPlan(rows, parseErrors);
    }

    private async Task<SecurityMasterImportPlanRow> ClassifyAsync(int index, CreateSecurityRequest request, DateTimeOffset asOf, CancellationToken ct)
    {
        var primary = request.Identifiers.FirstOrDefault(static identifier => identifier.IsPrimary) ?? request.Identifiers.FirstOrDefault();
        if (primary is null || string.IsNullOrWhiteSpace(primary.Value))
        {
            return new(index, null, SecurityMasterImportRowDisposition.Invalid, null, [], [], "The row carries no identifier.");
        }

        var claims = new List<SecurityIdentityClaim>();
        foreach (var identifier in request.Identifiers.Where(static identifier => !string.IsNullOrWhiteSpace(identifier.Value)))
        {
            if (SecurityIdentifierNormalizer.IsExcludedFromAmbiguityPairing(identifier.Kind))
                continue;
            claims.AddRange(await _store
                .FindIdentityClaimsAsync(identifier.Kind, identifier.Value, identifier.Provider, asOf, ignoreValidityWindow: true, ct)
                .ConfigureAwait(false));
        }

        var candidates = claims.Select(static claim => claim.SecurityId).Distinct().OrderBy(static id => id).ToArray();
        if (candidates.Length == 0)
        {
            return new(index, primary.Value, SecurityMasterImportRowDisposition.Create, null, [], [], null);
        }

        if (candidates.Length > 1)
        {
            return new(index, primary.Value, SecurityMasterImportRowDisposition.Ambiguous, null, candidates, [],
                $"The row's identifiers are claimed by {candidates.Length} securities; resolve the duplicate before importing.");
        }

        var existing = await _store.GetProjectionAsync(candidates[0], ct).ConfigureAwait(false);
        if (existing is null)
        {
            return new(index, primary.Value, SecurityMasterImportRowDisposition.Create, null, [], [], null);
        }

        var changed = new List<string>();
        if (!string.Equals(existing.AssetClass, request.AssetClass, StringComparison.OrdinalIgnoreCase))
            changed.Add("assetClass");
        changed.AddRange(DiffTopLevel("commonTerms", existing.CommonTerms, request.CommonTerms));
        changed.AddRange(DiffTopLevel("assetSpecificTerms", existing.AssetSpecificTerms, request.AssetSpecificTerms));

        return changed.Count == 0
            ? new(index, primary.Value, SecurityMasterImportRowDisposition.Unchanged, existing.SecurityId, candidates, [], null)
            : new(index, primary.Value, SecurityMasterImportRowDisposition.AmendProposal, existing.SecurityId, candidates, changed,
                $"Differs from the mastered record on {changed.Count} field(s); stage a governed amendment to apply them.");
    }

    /// <summary>
    /// Fields the incoming document asserts with a value different from the stored one. A field the
    /// file omits is not a change (absence is incompleteness, not disagreement), and the stored-only
    /// reserved keys are ignored.
    /// </summary>
    internal static IEnumerable<string> DiffTopLevel(string prefix, JsonElement stored, JsonElement incoming)
    {
        if (incoming.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var property in incoming.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Null
                || string.Equals(property.Name, "termsFormat", StringComparison.Ordinal)
                || string.Equals(property.Name, "schemaVersion", StringComparison.Ordinal))
            {
                continue;
            }

            var storedValue = stored.ValueKind == JsonValueKind.Object
                ? stored.EnumerateObject().FirstOrDefault(candidate => string.Equals(candidate.Name, property.Name, StringComparison.OrdinalIgnoreCase)).Value
                : default;
            if (storedValue.ValueKind == JsonValueKind.Undefined || !JsonElement.DeepEquals(storedValue, property.Value))
            {
                if (storedValue.ValueKind == JsonValueKind.Number && property.Value.ValueKind == JsonValueKind.Number
                    && storedValue.TryGetDecimal(out var left) && property.Value.TryGetDecimal(out var right) && left == right)
                {
                    continue;
                }

                yield return $"{prefix}.{property.Name}";
            }
        }
    }
}
