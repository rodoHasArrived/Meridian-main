using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;
using Microsoft.FSharp.Core;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Builds the report-first StructuredCredit reclassification inventory (ADR-022): every active
/// Bond in a securitised subclass and every CustomAsset whose category names a securitised product,
/// with the StructuredCredit terms the domain can carry over and the blockers it cannot resolve
/// without operator input. The proposal logic is
/// <c>SecurityMasterLegacyUpgrade.proposeStructuredCreditReclassification</c>; this service only
/// loads records, maps them through the read-tolerant mapper and shapes the DTO. Nothing is written.
/// </summary>
public sealed class SecurityMasterStructuredCreditReclassificationService : ISecurityMasterStructuredCreditReclassificationService
{
    private readonly ISecurityMasterStore _securityStore;
    private readonly ILogger<SecurityMasterStructuredCreditReclassificationService> _logger;

    public SecurityMasterStructuredCreditReclassificationService(
        ISecurityMasterStore securityStore,
        ILogger<SecurityMasterStructuredCreditReclassificationService> logger)
    {
        _securityStore = securityStore;
        _logger = logger;
    }

    public async Task<StructuredCreditReclassificationReportDto> BuildReportAsync(CancellationToken ct = default)
    {
        var securities = await _securityStore.LoadActiveAsync(ct).ConfigureAwait(false);
        var candidates = new List<StructuredCreditReclassificationCandidateDto>();
        var scanned = 0;
        var unreadable = 0;

        foreach (var projection in securities)
        {
            ct.ThrowIfCancellationRequested();
            if (projection.Status != SecurityStatusDto.Active)
                continue;

            scanned++;
            SecurityMasterRecord record;
            try
            {
                record = SecurityMasterMapping.ToRecord(projection);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException or FormatException or ArgumentException)
            {
                unreadable++;
                _logger.LogWarning(
                    ex,
                    "Structured-credit reclassification report skipped security {SecurityId} ({AssetClass}): stored terms could not be read.",
                    projection.SecurityId,
                    projection.AssetClass);
                continue;
            }

            var proposal = SecurityMasterLegacyUpgrade.proposeStructuredCreditReclassification(record);
            if (proposal is null)
                continue;

            candidates.Add(ToCandidate(projection, record, proposal.Value));
        }

        var ordered = candidates
            .OrderBy(static candidate => candidate.Disposition)
            .ThenBy(static candidate => candidate.SourceAssetClass, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.SecurityId)
            .ToArray();
        var ready = ordered.Count(static candidate => candidate.Disposition == StructuredCreditReclassificationDisposition.Ready);

        _logger.LogInformation(
            "Structured-credit reclassification report built: {Scanned} securities scanned, {Candidates} candidates ({Ready} ready, {Blocked} blocked), {Unreadable} unreadable.",
            scanned,
            ordered.Length,
            ready,
            ordered.Length - ready,
            unreadable);

        return new StructuredCreditReclassificationReportDto(
            DateTimeOffset.UtcNow,
            scanned,
            unreadable,
            ordered.Length,
            ready,
            ordered.Length - ready,
            ordered);
    }

    internal static StructuredCreditReclassificationCandidateDto ToCandidate(
        SecurityProjectionRecord projection,
        SecurityMasterRecord record,
        SecurityMasterLegacyUpgrade.StructuredCreditReclassificationProposal proposal)
    {
        var blockers = proposal.Blockers
            .Select(static blocker => new StructuredCreditReclassificationBlockerDto(blocker.Code, blocker.Message))
            .ToArray();
        var proposedTerms = RefOrNull(proposal.ProposedTerms);
        var derivedTerms = proposedTerms is null
            ? DraftTerms(proposal.Draft, projection.AssetClass, record)
            : StampedTerms(record, proposedTerms);

        return new StructuredCreditReclassificationCandidateDto(
            projection.SecurityId,
            projection.DisplayName,
            projection.PrimaryIdentifierKind,
            projection.PrimaryIdentifierValue,
            projection.AssetClass,
            proposal.SourceLabel,
            proposedTerms is null
                ? StructuredCreditReclassificationDisposition.Blocked
                : StructuredCreditReclassificationDisposition.Ready,
            KindLabel(proposal.Draft.Kind),
            derivedTerms,
            blockers);
    }

    /// <summary>
    /// A ready proposal is serialised through the same snapshot wrapper the write path uses, so the
    /// report shows the exact stamped document a governed amendment would persist.
    /// </summary>
    private static JsonElement StampedTerms(SecurityMasterRecord record, StructuredCreditTerms terms)
    {
        var reclassified = new SecurityMasterRecord(
            record.SecurityId,
            record.Status,
            record.Common,
            record.Identifiers,
            SecurityKind.NewStructuredCredit(terms),
            record.Version,
            record.EffectiveFrom,
            record.EffectiveTo,
            record.Provenance);
        using var document = JsonDocument.Parse(new SecurityMasterSnapshotWrapper(reclassified).AssetSpecificTermsJson);
        return document.RootElement.Clone();
    }

    /// <summary>The terms carried over from the source record, under the StructuredCredit wire keys.</summary>
    private static JsonElement DraftTerms(SecurityMasterLegacyUpgrade.StructuredCreditReclassificationDraft draft, string sourceAssetClass, SecurityMasterRecord source)
    {
        var node = new JsonObject();
        AddString(node, "tranche", RefOrNull(draft.Tranche));
        AddString(node, "poolId", RefOrNull(draft.PoolId));
        AddString(node, "collateralType", RefOrNull(draft.CollateralType));
        AddDecimal(node, "originalFace", StructOrNull(draft.OriginalFace));
        AddDecimal(node, "currentFactor", StructOrNull(draft.CurrentFactor));
        AddString(node, "couponOrIndex", RefOrNull(draft.CouponOrIndex));
        var maturity = StructOrNull(draft.Maturity);
        if (maturity is { } date)
            node["maturity"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        AddString(node, "structuredCreditKind", KindLabel(draft.Kind));
        AddString(node, "seniority", RefOrNull(draft.Seniority));
        // A blocked bond draft still needs the exact window/notice members for operator repair;
        // rebuilding these from the lossy economic Call lists would widen the exercise contract.
        if (SecurityAssetClassCatalog.GetOrDefault(sourceAssetClass).AssetClass == "Bond")
        {
            using var sourceTerms = JsonDocument.Parse(new SecurityMasterSnapshotWrapper(source).AssetSpecificTermsJson);
            foreach (var key in new[] { SecurityVersionedTerms.CallScheduleKey, SecurityVersionedTerms.PutScheduleKey })
            {
                if (SecurityAssetTermsSchema.Field("StructuredCredit", key) is { } field
                    && field.SinceTermsVersion <= SecurityAssetClassCatalog.GetTermsVersion("StructuredCredit")
                    && sourceTerms.RootElement.TryGetProperty(key, out var schedules)
                    && schedules.ValueKind == JsonValueKind.Array && schedules.GetArrayLength() > 0)
                    node[key] = JsonNode.Parse(schedules.GetRawText());
            }
        }
        return JsonSerializer.SerializeToElement(node);
    }

    private static void AddString(JsonObject node, string key, string? value)
    {
        if (value is not null)
            node[key] = value;
    }

    private static void AddDecimal(JsonObject node, string key, decimal? value)
    {
        if (value is { } number)
            node[key] = number;
    }

    private static string? KindLabel(FSharpOption<StructuredCreditKind>? kind)
        => RefOrNull(kind) is { } value ? StructuredCreditKindModule.asString(value) : null;

    private static T? RefOrNull<T>(FSharpOption<T>? option) where T : class
        => option is not null && OptionModule.IsSome(option) ? option.Value : null;

    private static T? StructOrNull<T>(FSharpOption<T>? option) where T : struct
        => option is not null && OptionModule.IsSome(option) ? option.Value : null;
}
