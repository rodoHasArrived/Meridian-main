using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public interface ISecurityAssetProfilePromotionReadService
{
    Task<SecurityAssetProfilePromotionAssessmentsDto> GetAssessmentsAsync(CancellationToken ct = default);
    Task<SecurityAssetProfileCandidateExportDto> ExportCandidateAsync(string profileId, int version,
        string expectedProfileFingerprint, string actor, CancellationToken ct = default);
}

/// <summary>Read-only demand evidence and explicit developer drafts; no registration or governance writes.</summary>
public sealed class SecurityAssetProfilePromotionReadService(
    ISecurityAssetProfileGovernanceService profiles, ISecurityAssetProfileUsageStore usage,
    TimeProvider? timeProvider = null) : ISecurityAssetProfilePromotionReadService
{
    public const string CatalogScope = "Current Security Master reference catalog in this database; counts are records, not holdings or tenant exposure.";
    private static readonly JsonSerializerOptions HashOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<SecurityAssetProfilePromotionAssessmentsDto> GetAssessmentsAsync(CancellationToken ct = default)
    {
        var approved = profiles.GetAllProfiles().Where(static profile => profile.Status == SecurityAssetProfileStatusDto.Approved).ToArray();
        var counts = await usage.GetProfileUsageSnapshotAsync(approved.Select(Requirements).ToArray(), ct).ConfigureAwait(false);
        var assessments = profiles.GetPromotionCandidates();
        var result = approved.Select(profile => new SecurityAssetProfilePromotionAssessmentDto(
                profile.ProfileId, profile.Version, profile.Name, Fingerprint(profile), Aggregate(profile, counts),
                assessments.SingleOrDefault(candidate => candidate.Version == profile.Version
                    && string.Equals(candidate.ProfileId, profile.ProfileId, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(static assessment => assessment.Usage.ActiveRecordCount)
            .ThenBy(static assessment => assessment.Name, StringComparer.OrdinalIgnoreCase).ThenBy(static assessment => assessment.Version).ToArray();
        var unresolved = counts.Where(count => !approved.Any(profile => Matches(profile, count)))
            .Select(count => ToDto(count, count.EnvelopeIssue ?? "Profile version is unknown or is not currently approved"))
            .ToArray();
        return new(CatalogScope, _timeProvider.GetUtcNow(), result, unresolved);
    }

    public async Task<SecurityAssetProfileCandidateExportDto> ExportCandidateAsync(string profileId, int version,
        string expectedProfileFingerprint, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("An authenticated actor is required.", nameof(actor));
        if (!profiles.TryGetProfile(profileId, version, out var profile))
            throw new KeyNotFoundException("The exact profile version does not exist.");
        if (profile.Status != SecurityAssetProfileStatusDto.Approved)
            throw new InvalidOperationException("Only an exact currently approved profile version can be exported.");
        var fingerprint = Fingerprint(profile);
        if (!string.Equals(expectedProfileFingerprint, fingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The profile changed. Refresh the assessment before exporting.");
        var counts = await usage.GetProfileUsageSnapshotAsync([Requirements(profile)], ct).ConfigureAwait(false);
        // Recheck governance after the asynchronous read. The captured source itself is retained in
        // the artifact; a changed definition must never be presented as the reviewed fingerprint.
        if (!profiles.TryGetProfile(profileId, version, out var current) || Fingerprint(current) != fingerprint)
            throw new InvalidOperationException("The profile changed. Refresh the assessment before exporting.");
        var token = string.Concat(profile.ProfileId.Select(character => char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')).Trim('-');
        if (token.Length == 0)
            token = "profile";
        var proposedClass = string.Concat(token.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(static word => char.ToUpperInvariant(word[0]) + word[1..])) + "Asset";
        var candidate = SecurityAssetPackRegistry.CreateCandidateDescriptor(
            $"candidate-{token}-v{version}", $"{profile.Name} developer draft", [], [], [], [],
            AssetPackAutomationDepth.WideCapture, plannedAssetClasses: [proposedClass]);
        return new("Meridian.AssetPackDeveloperDraft.v1", CatalogScope, _timeProvider.GetUtcNow(), actor.Trim(),
            fingerprint, profile, Aggregate(profile, counts), candidate,
            SecurityAssetPackRegistry.ValidateDescriptor(candidate), SecurityAssetPackRegistry.ValidateCandidateSet([candidate]),
            ["Review the proposed class identity and its economic meaning; this export registers nothing.",
             "Implement typed fields and validation from SourceProfile; observed record values are excluded and supply no defaults.",
             "Declare lifecycle and valuation support after review; profile lifecycle states and accounting hints are not executable mappings.",
             "Implement and review rate units, cash-flow economics, codecs, upgrades, storage and governed journal bindings.",
             "Resolve every validation issue and complete developer review before any source-code registration."]);
    }

    public static string Fingerprint(SecurityAssetProfileDefinitionDto profile)
        => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(profile, HashOptions));

    private static bool Matches(SecurityAssetProfileDefinitionDto profile, SecurityAssetProfileUsageCount count)
        => count.ProfileVersion == profile.Version && string.Equals(count.ProfileId, profile.ProfileId, StringComparison.OrdinalIgnoreCase);

    private static SecurityAssetProfileUsageDto Aggregate(SecurityAssetProfileDefinitionDto profile, IReadOnlyList<SecurityAssetProfileUsageCount> counts)
    {
        var matching = counts.Where(count => Matches(profile, count)).ToArray();
        var qualityAvailable = matching.All(static count => count.CompleteActiveRecordCount.HasValue && count.IncompleteActiveRecordCount.HasValue);
        var missing = profile.Fields.Where(static field => field.IsRequired).ToDictionary(static field => field.Key, static _ => 0L, StringComparer.Ordinal);
        foreach (var row in matching)
            foreach (var field in row.MissingRequiredFieldCounts ?? new Dictionary<string, long>())
                if (missing.ContainsKey(field.Key))
                    missing[field.Key] += field.Value;
        return new(profile.ProfileId, profile.Version, matching.Sum(static count => count.ActiveRecordCount),
            matching.Sum(static count => count.InactiveRecordCount), matching.Sum(static count => count.UnknownStatusRecordCount),
            matching.Any(static count => count.EnvelopeIssue is not null) ? "Malformed fields envelopes have unknown required-field presence." : null)
        {
            CompleteActiveRecordCount = qualityAvailable ? matching.Sum(static count => count.CompleteActiveRecordCount!.Value) : null,
            IncompleteActiveRecordCount = qualityAvailable ? matching.Sum(static count => count.IncompleteActiveRecordCount!.Value) : null,
            UnknownActiveRecordCount = matching.Sum(static count => count.UnknownActiveRecordCount ?? count.ActiveRecordCount),
            MissingRequiredFieldCounts = qualityAvailable ? missing : null
        };
    }

    private static SecurityAssetProfileUsageDto ToDto(SecurityAssetProfileUsageCount count, string reason)
        => new(count.ProfileId, count.ProfileVersion, count.ActiveRecordCount, count.InactiveRecordCount, count.UnknownStatusRecordCount, reason)
        { UnknownActiveRecordCount = count.ActiveRecordCount };

    private static SecurityAssetProfileUsageRequirements Requirements(SecurityAssetProfileDefinitionDto profile)
        => new(profile.ProfileId, profile.Version, profile.Fields.Where(static field => field.IsRequired).Select(static field => field.Key).ToArray());
}
