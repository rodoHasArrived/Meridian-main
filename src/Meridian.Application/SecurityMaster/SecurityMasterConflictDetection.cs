using System.Text.Json;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Pure golden-record conflict detection over the Security Master projection universe. Kept storage
/// agnostic so both the in-memory and the durable Postgres conflict stores detect identical conflicts;
/// the store layer decides how detected candidates are persisted and whether existing resolution state
/// is preserved.
/// </summary>
internal static class SecurityMasterConflictDetection
{
    private const string IdentifierAmbiguityKind = SecurityMasterConflictKinds.IdentifierAmbiguity;
    private const string UnknownProvider = SecurityMasterProvenanceReader.UnknownSource;

    private readonly record struct IdentifierKey(
        SecurityIdentifierKind Kind,
        string NormalizedValue,
        string IdentityScope);

    private sealed record IdentifierClaim(
        Guid SecurityId,
        string Provider,
        DateTimeOffset ValidFrom,
        DateTimeOffset? ValidTo);

    /// <summary>
    /// Detects every identifier-ambiguity conflict across the universe: an identifier that multiple
    /// distinct securities claim from different providers. Every returned conflict has status
    /// <c>Open</c> and a deterministic id, so re-detecting the same pair yields the same id.
    /// </summary>
    public static IReadOnlyList<SecurityMasterConflict> DetectAll(
        IReadOnlyList<SecurityProjectionRecord> universe,
        DateTimeOffset detectedAt)
        => DetectAll(universe, detectedAt, subjectIds: null);

    /// <summary>
    /// Full-universe detection, optionally restricted to pairs involving at least one subject.
    /// The indexed candidate lookup deliberately returns every historical claimant of a shared
    /// identifier, so an unrestricted scan of a batch with many candidates enumerates
    /// candidate-to-candidate pairs the caller would discard anyway — quadratic in claimants of
    /// a recycled identifier. The restriction skips those pairs before the overlap check.
    /// </summary>
    private static IReadOnlyList<SecurityMasterConflict> DetectAll(
        IReadOnlyList<SecurityProjectionRecord> universe,
        DateTimeOffset detectedAt,
        IReadOnlySet<Guid>? subjectIds)
    {
        var byIdentifier = new Dictionary<IdentifierKey, List<IdentifierClaim>>();

        foreach (var record in universe)
        {
            foreach (var id in record.Identifiers)
            {
                var normalizedValue = SecurityIdentifierNormalizer.GetOrComputeNormalizedValue(id);
                if (normalizedValue.Length == 0)
                {
                    continue;
                }

                var key = new IdentifierKey(
                    id.Kind,
                    normalizedValue,
                    SecurityIdentifierNormalizer.GetIdentityScope(id));
                if (!byIdentifier.TryGetValue(key, out var entries))
                {
                    entries = new List<IdentifierClaim>();
                    byIdentifier[key] = entries;
                }

                entries.Add(new IdentifierClaim(
                    record.SecurityId,
                    id.Provider ?? UnknownProvider,
                    id.ValidFrom,
                    id.ValidTo));
            }
        }

        var conflicts = new List<SecurityMasterConflict>();
        foreach (var (key, entries) in byIdentifier)
        {
            var claimants = entries
                .GroupBy(static entry => entry.SecurityId)
                .OrderBy(static group => group.Key)
                .ToArray();
            for (var leftIndex = 0; leftIndex < claimants.Length; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < claimants.Length; rightIndex++)
                {
                    if (subjectIds is not null
                        && !subjectIds.Contains(claimants[leftIndex].Key)
                        && !subjectIds.Contains(claimants[rightIndex].Key))
                    {
                        continue;
                    }

                    var overlap = FindDeterministicOverlap(claimants[leftIndex], claimants[rightIndex]);
                    if (overlap is null)
                    {
                        continue;
                    }

                    var (left, right) = overlap.Value;
                    conflicts.Add(new SecurityMasterConflict(
                        ConflictId: DeterministicConflictId(
                            key.Kind.ToString(),
                            CanonicalConflictIdentity(key),
                            left.SecurityId,
                            right.SecurityId),
                        SecurityId: left.SecurityId,
                        ConflictKind: IdentifierAmbiguityKind,
                        FieldPath: $"Identifiers.{key.Kind}",
                        ProviderA: left.Provider,
                        ValueA: left.SecurityId.ToString(),
                        ProviderB: right.Provider,
                        ValueB: right.SecurityId.ToString(),
                        DetectedAt: detectedAt,
                        Status: "Open"));
                }
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Detects conflicts a freshly written projection introduces: identifiers on the new projection
    /// that another existing security already claims. Used at ingest time so a create/amend/import
    /// records new conflicts immediately without a full-universe rescan.
    /// </summary>
    public static IReadOnlyList<SecurityMasterConflict> DetectForProjection(
        SecurityProjectionRecord projection,
        IReadOnlyList<SecurityProjectionRecord> universe,
        DateTimeOffset detectedAt)
        => DetectForProjections([projection], universe, detectedAt);

    /// <summary>
    /// Detects every ambiguity involving at least one subject projection. Subjects are authoritative
    /// when the candidate set contains an older copy of the same security, and the identifier map is
    /// built once for the whole batch so rebuild cost is linear in claims plus actual conflicts.
    /// </summary>
    public static IReadOnlyList<SecurityMasterConflict> DetectForProjections(
        IReadOnlyList<SecurityProjectionRecord> projections,
        IReadOnlyList<SecurityProjectionRecord> candidates,
        DateTimeOffset detectedAt)
    {
        if (projections.Count == 0)
        {
            return Array.Empty<SecurityMasterConflict>();
        }

        var subjectIds = projections.Select(static projection => projection.SecurityId).ToHashSet();
        var universe = projections
            .Concat(candidates.Where(candidate => !subjectIds.Contains(candidate.SecurityId)))
            .ToArray();
        var results = new List<SecurityMasterConflict>();
        foreach (var conflict in DetectAll(universe, detectedAt, subjectIds))
        {
            if (subjectIds.Contains(conflict.SecurityId))
            {
                results.Add(conflict);
                continue;
            }

            if (Guid.TryParse(conflict.ValueB, out var otherSecurityId) && subjectIds.Contains(otherSecurityId))
            {
                results.Add(conflict with
                {
                    SecurityId = otherSecurityId,
                    ProviderA = conflict.ProviderB,
                    ValueA = conflict.ValueB,
                    ProviderB = conflict.ProviderA,
                    ValueB = conflict.ValueA,
                });
            }
        }

        return results;
    }

    private static (IdentifierClaim Left, IdentifierClaim Right)? FindDeterministicOverlap(
        IEnumerable<IdentifierClaim> leftClaims,
        IEnumerable<IdentifierClaim> rightClaims)
    {
        foreach (var left in leftClaims.OrderBy(static claim => claim.ValidFrom).ThenBy(static claim => claim.Provider, StringComparer.Ordinal))
        {
            foreach (var right in rightClaims.OrderBy(static claim => claim.ValidFrom).ThenBy(static claim => claim.Provider, StringComparer.Ordinal))
            {
                if (WindowsOverlap(left.ValidFrom, left.ValidTo, right.ValidFrom, right.ValidTo))
                {
                    return (left, right);
                }
            }
        }

        return null;
    }

    private static bool WindowsOverlap(
        DateTimeOffset leftFrom,
        DateTimeOffset? leftTo,
        DateTimeOffset rightFrom,
        DateTimeOffset? rightTo)
        => (!leftTo.HasValue || rightFrom < leftTo.Value)
           && (!rightTo.HasValue || leftFrom < rightTo.Value);

    private static string CanonicalConflictIdentity(IdentifierKey key)
        => key.IdentityScope.Length == 0
            ? key.NormalizedValue
            : $"{key.IdentityScope}|{key.NormalizedValue}";

    /// <summary>
    /// Canonical text form of a contractual principal schedule for conflict comparison and the
    /// resolution-time persisted-value guard: same-day instalments summed into one entry per payment
    /// date (a source splitting a date's amount across rows asserts the same economics as one that
    /// records it whole), then date-sorted <c>yyyy-MM-dd:amount</c> pairs with scale-normalized
    /// amounts (G29 drops trailing zeros), joined with <c>|</c>. Both sides of a comparison MUST use
    /// this same normalization or textually different but economically equal schedules would
    /// conflict forever.
    /// </summary>
    internal static string NormalizePrincipalSchedule(IReadOnlyList<StructuredPrincipalScheduleEntry> schedule)
        => string.Join("|", schedule
            .GroupBy(static entry => entry.PaymentDate)
            .OrderBy(static group => group.Key)
            .Select(static group =>
                $"{group.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}:" +
                group.Sum(static entry => entry.Amount).ToString("G29", System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>
    /// The comparable persisted value a record carries for a conflicted field path, in the SAME
    /// canonical normalization the conflict candidates were recorded with (typed term readers,
    /// normalized principal schedule) — the only representation against which a recorded candidate
    /// value can be meaningfully compared. Returns null for paths this detection does not compare.
    /// </summary>
    internal static string? ReadComparableFieldValue(
        SecurityDetailDto detail,
        string fieldPath,
        Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? assetProfileCatalog = null)
    {
        var terms = StructuredCashFlowTermsResolver.Resolve(detail);
        return fieldPath switch
        {
            "EconomicTerms.maturityDate" => terms.MaturityDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            "EconomicTerms.issueDate" => terms.IssueDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            "EconomicTerms.couponRate" => terms.CouponRate?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "EconomicTerms.principalFace" => terms.PrincipalFace?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "EconomicTerms.paymentFrequency" => terms.PaymentFrequency,
            "EconomicTerms.dayCountConvention" => terms.DayCountConvention,
            // PRESENCE keys on the schedule PROPERTY, not row count: an asserted-empty
            // principalSchedule normalizes to "" (a real bullet assertion the empty candidate must
            // be able to match), while a missing property stays null (absence).
            "EconomicTerms.principalSchedule" => terms.PrincipalSchedule is null
                ? null
                : NormalizePrincipalSchedule(terms.PrincipalSchedule),
            "CommonTerms.currency" => detail.Currency,
            "CommonTerms.countryOfRisk" => SecurityTermReader.ReadString(detail.CommonTerms, "countryOfRisk"),
            _ => fieldPath.StartsWith(ProfileFieldPathPrefix, StringComparison.Ordinal)
                ? ReadComparableProfileFieldValue(
                    GetProfileFields(detail.AssetSpecificTerms),
                    fieldPath[ProfileFieldPathPrefix.Length..],
                    ResolveDeclaredProfileFieldType(
                        assetProfileCatalog,
                        detail.AssetSpecificTerms,
                        fieldPath[ProfileFieldPathPrefix.Length..]))
                : null,
        };
    }

    /// <inheritdoc cref="ReadComparableFieldValue(SecurityDetailDto, string, Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog?)"/>
    internal static string? ReadComparableFieldValue(
        SecurityProjectionRecord projection,
        string fieldPath,
        Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? assetProfileCatalog = null)
        => ReadComparableFieldValue(SecurityMasterMapping.ToDetail(projection), fieldPath, assetProfileCatalog);

    /// <summary>
    /// The declared type of one pinned-profile field, or null when no catalog is wired, the pin
    /// does not resolve, or the profile does not declare the key — callers then compare the raw
    /// spelling, matching the undeclared-key posture everywhere else.
    /// </summary>
    private static Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldTypeDto? ResolveDeclaredProfileFieldType(
        Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? assetProfileCatalog,
        JsonElement assetSpecificTerms,
        string fieldKey)
        => ResolveDeclaredProfileFields(assetProfileCatalog, assetSpecificTerms)
            ?.FirstOrDefault(field => string.Equals(field.Key, fieldKey, StringComparison.OrdinalIgnoreCase))
            ?.FieldType;

    /// <summary>
    /// The governed field paths whose values ARE numbers, and therefore the only paths where
    /// numeric equality is meaningful. Everywhere else — Text, Enum, and code-valued fields —
    /// numeric-looking strings keep their textual identity: a text pool ID of "001" and "1" are
    /// DIFFERENT values, and comparing them numerically would let a resolution close a conflict
    /// (and record a winning provenance) for a value that was never applied. ProfileFields.* stays
    /// textual too: both sides of a profile-field comparison flow through
    /// <see cref="ReadComparableProfileFieldValue"/>, whose G29 normalization already makes equal
    /// numbers textually identical, while an undeclared or Text-typed field keeps its spelling.
    /// </summary>
    private static readonly HashSet<string> NumericComparableFieldPaths = new(StringComparer.Ordinal)
    {
        "EconomicTerms.couponRate",
        "EconomicTerms.principalFace",
    };

    /// <summary>
    /// Whether a persisted field value and a recorded candidate value assert the same economics:
    /// day counts compare through the canonical convention parser, KNOWN NUMERIC term paths
    /// compare numerically ("6.00" and "6.0" are the same coupon), and everything else compares as
    /// trimmed case-insensitive text — numeric-looking strings on Text/Enum/code fields keep their
    /// textual identity. A blank persisted value matches nothing — absence is incompleteness, not
    /// agreement.
    /// </summary>
    internal static bool FieldValuesMatch(
        string fieldPath,
        string? persisted,
        string candidate,
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldTypeDto? declaredProfileFieldType = null)
    {
        if (string.IsNullOrWhiteSpace(persisted))
        {
            // One exception to absence-is-incompleteness: the principal schedule's NORMALIZED
            // empty form is the empty string — an ASSERTED bullet structure, not a missing value
            // (readers return null for genuine absence). An operator selecting the bullet-side
            // provider persists that empty schedule, and its "" must match the candidate recorded
            // as "" or no resolution could ever accept the bullet side.
            return persisted is not null
                && string.Equals(fieldPath, "EconomicTerms.principalSchedule", StringComparison.Ordinal)
                && persisted.Length == 0
                && candidate.Length == 0;
        }

        if (string.Equals(fieldPath, "EconomicTerms.dayCountConvention", StringComparison.Ordinal))
        {
            var persistedConvention = DayCountConventions.Parse(persisted);
            var candidateConvention = DayCountConventions.Parse(candidate);
            if (persistedConvention != DayCountConvention.Unknown || candidateConvention != DayCountConvention.Unknown)
            {
                return persistedConvention == candidateConvention;
            }
        }

        if (NumericComparableFieldPaths.Contains(fieldPath)
            && decimal.TryParse(persisted.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var persistedNumber)
            && decimal.TryParse(candidate.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var candidateNumber))
        {
            return persistedNumber == candidateNumber;
        }

        // Governed profile fields compare under their DECLARED type's equality contract: a Text
        // field carries a case-sensitive code (Pool-A and pool-a are different governed values),
        // while numeric/date/boolean/enum values reach here in normalized spellings whose
        // contracts tolerate case. Non-profile paths keep the historical trimmed
        // case-insensitive comparison.
        var comparison = fieldPath.StartsWith(ProfileFieldPathPrefix, StringComparison.Ordinal)
            ? ProfileFieldValueComparison(declaredProfileFieldType)
            : StringComparison.OrdinalIgnoreCase;
        return string.Equals(persisted.Trim(), candidate.Trim(), comparison);
    }

    /// <summary>
    /// Equality contract for one declared profile field's comparable values: Text (and
    /// undeclared) fields are case-SENSITIVE codes — a poolId of "Pool-A" and "pool-a" are
    /// different governed values, matching <see cref="ReadComparableProfileFieldValue"/>'s
    /// raw-identity posture — while the remaining declared types compare through normalized
    /// spellings whose contracts are case-insensitive (currency codes, enum options, booleans).
    /// </summary>
    internal static StringComparison ProfileFieldValueComparison(
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldTypeDto? fieldType)
        => fieldType is null or Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldTypeDto.Text
            or Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldTypeDto.SecurityLink
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// The declared type governing <paramref name="fieldPath"/> when it addresses a pinned
    /// profile's field, or null for non-profile paths and unresolvable pins — the value guards
    /// then fall back to the raw-identity (case-sensitive) posture for profile paths.
    /// </summary>
    internal static Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldTypeDto? ResolveDeclaredFieldTypeForPath(
        SecurityDetailDto detail,
        string fieldPath,
        Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? assetProfileCatalog)
        => fieldPath.StartsWith(ProfileFieldPathPrefix, StringComparison.Ordinal)
            ? ResolveDeclaredProfileFieldType(assetProfileCatalog, detail.AssetSpecificTerms, fieldPath[ProfileFieldPathPrefix.Length..])
            : null;

    /// <inheritdoc cref="ResolveDeclaredFieldTypeForPath(SecurityDetailDto, string, Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog?)"/>
    internal static Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldTypeDto? ResolveDeclaredFieldTypeForPath(
        SecurityProjectionRecord projection,
        string fieldPath,
        Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? assetProfileCatalog)
        => fieldPath.StartsWith(ProfileFieldPathPrefix, StringComparison.Ordinal)
            ? ResolveDeclaredProfileFieldType(assetProfileCatalog, projection.AssetSpecificTerms, fieldPath[ProfileFieldPathPrefix.Length..])
            : null;

    /// <summary>
    /// Whether an OPEN field conflict has been made obsolete by <paramref name="persistedValue"/>:
    /// a later canonical write that matches NEITHER recorded candidate replaced both sources'
    /// asserted values, so the conflict can never resolve to either candidate and only blocks the
    /// queue. A null/blank persisted value is NOT obsolescence — absence of a readable value must
    /// not silently retire a real disagreement — with one exception mirroring
    /// <see cref="FieldValuesMatch"/>: the principal schedule's normalized EMPTY form is the empty
    /// string, an ASSERTED bullet structure rather than absence (readers return null for genuine
    /// absence), so an asserted-empty canonical write can supersede nonempty candidates.
    /// </summary>
    internal static bool FieldConflictIsObsolete(
        SecurityMasterConflict conflict,
        string? persistedValue,
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldTypeDto? declaredProfileFieldType = null)
    {
        var persistedAsserted = !string.IsNullOrWhiteSpace(persistedValue)
            || (persistedValue is { Length: 0 }
                && string.Equals(conflict.FieldPath, "EconomicTerms.principalSchedule", StringComparison.Ordinal));
        return persistedAsserted
            && !FieldValuesMatch(conflict.FieldPath, persistedValue, conflict.ValueA, declaredProfileFieldType)
            && !FieldValuesMatch(conflict.FieldPath, persistedValue, conflict.ValueB, declaredProfileFieldType);
    }

    /// <summary>
    /// Whether <paramref name="source"/> is one of the conflict's own candidate providers, and
    /// which one. A canonical write AUTHORED by a candidate is that candidate revising its own
    /// value — the disagreement with the other provider is still live, so obsolescence handling
    /// must refresh the candidate rather than retire the conflict as a third-source replacement.
    /// </summary>
    internal static bool TryMatchCandidateProvider(
        SecurityMasterConflict conflict, string? source, out bool matchesProviderA)
    {
        matchesProviderA = false;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        matchesProviderA = string.Equals(source.Trim(), conflict.ProviderA.Trim(), StringComparison.OrdinalIgnoreCase);
        return matchesProviderA
            || string.Equals(source.Trim(), conflict.ProviderB.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether two field conflicts dispute between the SAME pair of providers (order-insensitive):
    /// a refreshed older row and a newly detected row with this property describe one live
    /// disagreement, and keeping both open would surface two independently resolvable queue
    /// entries for it.
    /// </summary>
    internal static bool SameProviderPair(SecurityMasterConflict left, SecurityMasterConflict right)
    {
        static bool ProviderEquals(string a, string b)
            => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        return (ProviderEquals(left.ProviderA, right.ProviderA) && ProviderEquals(left.ProviderB, right.ProviderB))
            || (ProviderEquals(left.ProviderA, right.ProviderB) && ProviderEquals(left.ProviderB, right.ProviderA));
    }

    /// <summary>
    /// The governed field paths whose values differ between <paramref name="current"/> and
    /// <paramref name="incoming"/>, regardless of source. Cross-source disagreement is what OPENS a
    /// conflict, but per-field attribution goes stale whenever a governed field changes hands —
    /// including when the previous winner amends its own value — so retirement must not depend on
    /// the same-source short-circuit that conflict creation uses.
    /// </summary>
    internal static IReadOnlyList<string> ChangedGovernedFieldPaths(
        SecurityProjectionRecord current,
        SecurityProjectionRecord incoming,
        Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? assetProfileCatalog = null)
        => DetectFieldConflictsCore(
                current, incoming, DateTimeOffset.UtcNow,
                requireDistinctSources: false,
                // A CLEARED value changed hands too: attribution for a value that no longer exists
                // is exactly as stale as attribution for a replaced one, so absence transitions
                // count as changes here even though conflict creation needs both values present.
                includeAbsenceTransitions: true,
                assetProfileCatalog: assetProfileCatalog)
            .Select(static conflict => conflict.FieldPath)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Detects field-level cross-source conflicts between the stored golden copy and an incoming
    /// revision of the same security written by a different source system — the golden-copy case
    /// "Bloomberg set the coupon, Reuters disagrees on country-of-risk". Fields are compared through
    /// the canonical typed readers (<see cref="StructuredCashFlowTermsResolver"/>,
    /// <see cref="DayCountConventions"/>), so vendor alias spellings and equivalent day-count
    /// notations never produce false conflicts. A field missing on either side is never a conflict
    /// (absence is incompleteness, not disagreement), and a revision from the same source system
    /// produces nothing (that is versioning, not a cross-source conflict). Pool factor is
    /// deliberately not compared: sources snapshot it at different dates, so mismatches are
    /// expected, not disagreements.
    /// <para><paramref name="incumbentFieldSources"/> supplies per-field attribution (field path →
    /// source system) for the stored side; when a path has an entry, that source is named as the
    /// conflict's incumbent instead of the record-level provenance, which flips on every amendment
    /// and can otherwise name an unrelated source.</para>
    /// </summary>
    public static IReadOnlyList<SecurityMasterConflict> DetectFieldConflicts(
        SecurityProjectionRecord current,
        SecurityProjectionRecord incoming,
        DateTimeOffset detectedAt,
        IReadOnlyDictionary<string, string>? incumbentFieldSources = null,
        Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? assetProfileCatalog = null)
        => DetectFieldConflictsCore(
            current, incoming, detectedAt,
            requireDistinctSources: true,
            includeAbsenceTransitions: false,
            incumbentFieldSources,
            assetProfileCatalog);

    private static IReadOnlyList<SecurityMasterConflict> DetectFieldConflictsCore(
        SecurityProjectionRecord current,
        SecurityProjectionRecord incoming,
        DateTimeOffset detectedAt,
        bool requireDistinctSources,
        bool includeAbsenceTransitions,
        IReadOnlyDictionary<string, string>? incumbentFieldSources = null,
        Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? assetProfileCatalog = null)
    {
        if (current.SecurityId != incoming.SecurityId)
        {
            return [];
        }

        var sourceA = SecurityMasterProvenanceReader.Read(current.Provenance).SourceSystem;
        var sourceB = SecurityMasterProvenanceReader.Read(incoming.Provenance).SourceSystem;
        // The record-level same-source short-circuit is only sound when no per-field attribution is
        // available: record-level provenance names the record's LAST writer, so when source B
        // amended an unrelated field and now changes a field source A supplied, both sides read B
        // even though the true disagreement is A-versus-B. With an incumbent map the distinct-source
        // test moves into Add, per field, against that field's actual incumbent.
        var hasFieldAttribution = incumbentFieldSources is { Count: > 0 };
        if (requireDistinctSources && !hasFieldAttribution
            && string.Equals(sourceA, sourceB, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var termsA = StructuredCashFlowTermsResolver.Resolve(SecurityMasterMapping.ToDetail(current));
        var termsB = StructuredCashFlowTermsResolver.Resolve(SecurityMasterMapping.ToDetail(incoming));
        var conflicts = new List<SecurityMasterConflict>();

        CompareDate("EconomicTerms.maturityDate", termsA.MaturityDate, termsB.MaturityDate);
        CompareDate("EconomicTerms.issueDate", termsA.IssueDate, termsB.IssueDate);
        CompareDecimal("EconomicTerms.couponRate", termsA.CouponRate, termsB.CouponRate);
        CompareDecimal("EconomicTerms.principalFace", termsA.PrincipalFace, termsB.PrincipalFace);
        CompareText("EconomicTerms.paymentFrequency", SecurityMasterConflictKinds.EconomicTermMismatch,
            termsA.PaymentFrequency, termsB.PaymentFrequency);
        CompareDayCount(termsA.DayCountConvention, termsB.DayCountConvention);
        // The contractual principal schedule is an authoritative economic term (it drives
        // calculated cash flows and ledger support), so a source replacing another source's dated
        // instalments must open a conflict like any other economic-term disagreement.
        ComparePrincipalSchedule(termsA.PrincipalSchedule, termsB.PrincipalSchedule);
        CompareText("CommonTerms.currency", SecurityMasterConflictKinds.CommonTermMismatch,
            current.Currency, incoming.Currency);
        CompareText("CommonTerms.countryOfRisk", SecurityMasterConflictKinds.CommonTermMismatch,
            SecurityTermReader.ReadString(current.CommonTerms, "countryOfRisk"),
            SecurityTermReader.ReadString(incoming.CommonTerms, "countryOfRisk"));
        CompareProfileFields();

        return conflicts;

        // Governed profile fields ARE the economics of a profile-backed record (commitment,
        // fundedAmount, appraisalValue, latestValuation, ...), so a cross-source amendment
        // replacing them must open a conflict — and per-field attribution must track them — just
        // like the shared fixed-income terms. The comparison set is the PINNED PROFILE'S DECLARED
        // fields: undeclared pass-through keys (operator metadata like operatorNote) are not
        // governed economics and must not open conflicts or mint attribution. Declared keys the
        // typed comparisons above already cover through the resolver (and pool-factor evidence,
        // which sources legitimately snapshot at different dates) stay excluded so one
        // disagreement is never reported under two paths. Without a resolvable pinned profile the
        // comparison is skipped entirely — no declaration means no basis to call a key governed.
        void CompareProfileFields()
        {
            var fieldsA = GetProfileFields(current.AssetSpecificTerms);
            var fieldsB = GetProfileFields(incoming.AssetSpecificTerms);
            if (fieldsA is null && fieldsB is null)
            {
                return;
            }

            // The INCOMING envelope's pinned profile resolves first: an amendment may REPIN the
            // record, and the submitted profile is the one that will govern the persisted record —
            // comparing under the incumbent's declaration would treat old-profile fields retained
            // as pass-through metadata as governed economics and open false conflicts. The
            // incumbent is only the fallback when the incoming side does not resolve.
            var declaredFields = ResolveDeclaredProfileFields(
                assetProfileCatalog, incoming.AssetSpecificTerms, current.AssetSpecificTerms);
            if (declaredFields is null)
            {
                return;
            }

            foreach (var field in declaredFields)
            {
                if (ProfileFieldComparisonExclusions.Contains(field.Key))
                {
                    continue;
                }

                // Declared-type-aware equality: a Text field's value is a case-sensitive code
                // (poolId "Pool-A" vs "pool-a" is a real governed disagreement), so it must not
                // slip through the case-insensitive default used for enum/code spellings.
                CompareText(
                    ProfileFieldPathPrefix + field.Key,
                    SecurityMasterConflictKinds.EconomicTermMismatch,
                    ReadComparableProfileFieldValue(fieldsA, field.Key, field.FieldType),
                    ReadComparableProfileFieldValue(fieldsB, field.Key, field.FieldType),
                    ProfileFieldValueComparison(field.FieldType));
            }
        }

        void Add(string fieldPath, string kind, string valueA, string valueB)
        {
            // The incumbent for a FIELD is whoever last supplied that field's value — per-field
            // attribution when a resolution recorded one — not whichever source happened to touch
            // the record most recently. Record-level provenance flips on every amendment, so using
            // it unconditionally can name an unrelated source as the incumbent and let the
            // authority policy persist false field provenance.
            var incumbent = incumbentFieldSources is not null
                && incumbentFieldSources.TryGetValue(fieldPath, out var fieldSource)
                && !string.IsNullOrWhiteSpace(fieldSource)
                    ? fieldSource
                    : sourceA;
            // Per-field distinct-source test: a source revising ITS OWN field is versioning, not a
            // cross-source conflict — judged against the field's incumbent, not the record's last
            // writer, so B changing A's field still opens the A-versus-B disagreement.
            if (requireDistinctSources && string.Equals(incumbent, sourceB, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            conflicts.Add(new SecurityMasterConflict(
                ConflictId: DeterministicFieldConflictId(current.SecurityId, fieldPath, incumbent, valueA, sourceB, valueB),
                SecurityId: current.SecurityId,
                ConflictKind: kind,
                FieldPath: fieldPath,
                ProviderA: incumbent,
                ValueA: valueA,
                ProviderB: sourceB,
                ValueB: valueB,
                DetectedAt: detectedAt,
                Status: "Open"));
        }

        void CompareDate(string fieldPath, DateOnly? a, DateOnly? b)
        {
            if (a is DateOnly left && b is DateOnly right && left != right)
            {
                Add(fieldPath, SecurityMasterConflictKinds.EconomicTermMismatch,
                    left.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    right.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (includeAbsenceTransitions && a.HasValue != b.HasValue)
            {
                Add(fieldPath, SecurityMasterConflictKinds.EconomicTermMismatch,
                    a?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    b?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
            }
        }

        void ComparePrincipalSchedule(
            IReadOnlyList<StructuredPrincipalScheduleEntry>? a,
            IReadOnlyList<StructuredPrincipalScheduleEntry>? b)
        {
            // Both sources must ASSERT a schedule for a disagreement to exist, mirroring the
            // both-present rule the scalar comparators use for sparse providers — but presence is
            // the schedule PROPERTY, not its row count: the Bond codec emits principalSchedule: []
            // to assert "no contractual instalments" (the resolver keeps that as an empty array,
            // distinct from null for a source that said nothing). An asserted empty schedule
            // against another source's asserted instalments is therefore a genuine bullet-versus-
            // sinker disagreement that must open a conflict — equating empty with missing would
            // let the empty assertion silently replace the sinker's economics. A schedule that
            // was REMOVED entirely is still a change for absence-transition consumers.
            if (a is null || b is null)
            {
                if (includeAbsenceTransitions && (a is null) != (b is null))
                {
                    Add("EconomicTerms.principalSchedule", SecurityMasterConflictKinds.EconomicTermMismatch,
                        a is null ? string.Empty : NormalizePrincipalSchedule(a),
                        b is null ? string.Empty : NormalizePrincipalSchedule(b));
                }

                return;
            }

            var normalizedA = NormalizePrincipalSchedule(a);
            var normalizedB = NormalizePrincipalSchedule(b);
            if (!string.Equals(normalizedA, normalizedB, StringComparison.Ordinal))
            {
                Add("EconomicTerms.principalSchedule", SecurityMasterConflictKinds.EconomicTermMismatch,
                    normalizedA, normalizedB);
            }
        }

        void CompareDecimal(string fieldPath, decimal? a, decimal? b)
        {
            if (a is decimal left && b is decimal right && left != right)
            {
                Add(fieldPath, SecurityMasterConflictKinds.EconomicTermMismatch,
                    left.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    right.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (includeAbsenceTransitions && a.HasValue != b.HasValue)
            {
                Add(fieldPath, SecurityMasterConflictKinds.EconomicTermMismatch,
                    a?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    b?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
            }
        }

        void CompareText(string fieldPath, string kind, string? a, string? b,
            StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
                && !string.Equals(a.Trim(), b.Trim(), comparison))
            {
                Add(fieldPath, kind, a.Trim(), b.Trim());
            }
            else if (includeAbsenceTransitions && string.IsNullOrWhiteSpace(a) != string.IsNullOrWhiteSpace(b))
            {
                Add(fieldPath, kind, a?.Trim() ?? string.Empty, b?.Trim() ?? string.Empty);
            }
        }

        void CompareDayCount(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                // A cleared day count changed hands like any other governed field: attribution for
                // the removed value is stale, so absence transitions count for retirement consumers
                // even though conflict creation still requires both values present.
                if (includeAbsenceTransitions && string.IsNullOrWhiteSpace(a) != string.IsNullOrWhiteSpace(b))
                {
                    Add("EconomicTerms.dayCountConvention", SecurityMasterConflictKinds.EconomicTermMismatch,
                        a?.Trim() ?? string.Empty, b?.Trim() ?? string.Empty);
                }

                return;
            }

            var parsedA = DayCountConventions.Parse(a);
            var parsedB = DayCountConventions.Parse(b);

            // Different spellings of the same recognized convention agree ("30/360" vs "Thirty360").
            if (parsedA == parsedB && parsedA != DayCountConvention.Unknown)
            {
                return;
            }

            // Two unrecognized spellings can only be compared textually.
            if (parsedA == DayCountConvention.Unknown && parsedB == DayCountConvention.Unknown)
            {
                CompareText("EconomicTerms.dayCountConvention", SecurityMasterConflictKinds.EconomicTermMismatch, a, b);
                return;
            }

            Add("EconomicTerms.dayCountConvention", SecurityMasterConflictKinds.EconomicTermMismatch, a.Trim(), b.Trim());
        }
    }

    internal const string ProfileFieldPathPrefix = "ProfileFields.";

    /// <summary>
    /// Governed profile-field keys EXCLUDED from the dynamic comparison: terms the typed
    /// fixed-income comparisons already read through the shared resolver — which probes the
    /// nested profileFields object too, so comparing the raw nested copy again would
    /// double-report one disagreement under two paths — plus pool-factor evidence, which sources
    /// legitimately snapshot at different dates. Names covered only by RECORD-LEVEL comparators
    /// (currency reads projection.Currency, countryOfRisk reads CommonTerms.countryOfRisk — never
    /// the nested profileFields values) must NOT appear here: excluding them would leave a
    /// profile-declared currency/countryOfRisk field with no comparator at all, letting two
    /// providers disagree on a governed field without ever opening a conflict.
    /// </summary>
    private static readonly HashSet<string> ProfileFieldComparisonExclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        "maturity", "maturityDate", "issueDate", "couponRate", "par", "principalFace",
        "paymentFrequency", "dayCount", "dayCountConvention",
        // Only the resolver-consumed principalSchedule key stays excluded (its typed comparator
        // above owns it). The legacy sinkingFundSchedule/amortizationSchedule aliases are NOT
        // read by StructuredCashFlowTermsResolver, so a profile declaring one of them has no
        // other comparator — excluding them would let providers disagree on a governed schedule
        // with no conflict opened and no attribution update.
        "principalSchedule",
        "currentFactor", "factor", "factorDate", "factorSchedule", "factorScheduleEntries",
    };

    private static JsonElement? GetProfileFields(JsonElement assetSpecificTerms)
        => SecurityTermReader.TryGetProperty(assetSpecificTerms, "profileFields", out var profileFields)
           && profileFields.ValueKind == JsonValueKind.Object
            ? profileFields
            : null;

    /// <summary>
    /// The declared field definitions of the record's PINNED profile, resolved from the FIRST
    /// envelope whose pin is registered — callers pass the envelope that will govern the persisted
    /// record first (the incoming side of an amendment, since a repin's submitted profile is the
    /// one the record reads under after the write) and the fallback second. Null when no catalog
    /// is wired or no side's pinned version is registered: no declaration means no basis to treat
    /// a key as governed economics.
    /// </summary>
    private static IReadOnlyList<Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldDefinitionDto>? ResolveDeclaredProfileFields(
        Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? assetProfileCatalog,
        params JsonElement[] envelopes)
    {
        if (assetProfileCatalog is null)
        {
            return null;
        }

        foreach (var envelope in envelopes)
        {
            if (envelope.ValueKind != JsonValueKind.Object
                || !envelope.TryGetProperty("customProfileId", out var profileId)
                || profileId.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(profileId.GetString())
                || !envelope.TryGetProperty("profileVersion", out var versionElement)
                || !versionElement.TryGetInt32(out var profileVersion))
            {
                continue;
            }

            if (assetProfileCatalog.TryGetProfile(profileId.GetString()!, profileVersion, out var profile))
            {
                return profile.Fields;
            }
        }

        return null;
    }

    /// <summary>
    /// Canonical comparable text for one governed profile-field value: numbers scale-normalized
    /// (G29 drops trailing zeros), booleans lowercased, structured values as raw JSON. String
    /// values canonicalize through the pinned profile's DECLARED type when one is supplied —
    /// dates to <c>yyyy-MM-dd</c>, security links to the canonical GUID spelling, declared
    /// numerics through G29 — because write validation accepts multiple spellings of the same
    /// typed value, and comparing raw spellings would open conflicts (and block resolutions) for
    /// "2026-07-01" versus "07/01/2026". Text-like and undeclared fields keep their raw identity.
    /// Null when the field is absent or JSON null — absence is incompleteness, not a value.
    /// </summary>
    private static string? ReadComparableProfileFieldValue(
        JsonElement? profileFields,
        string key,
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldTypeDto? declaredType = null)
    {
        if (profileFields is not JsonElement fields
            || !SecurityTermReader.TryGetProperty(fields, key, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => CanonicalizeDeclaredProfileFieldText(declaredType, value.GetString()!),
            JsonValueKind.Number => value.TryGetDecimal(out var number)
                ? number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture)
                : value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText(),
        };
    }

    /// <summary>
    /// Canonical spelling of a string-carried profile-field value under its declared type; the
    /// raw text when the type is text-like, undeclared, or the value does not parse (a
    /// non-parsing value keeps its identity and surfaces as the disagreement it is).
    /// </summary>
    private static string CanonicalizeDeclaredProfileFieldText(
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldTypeDto? declaredType, string raw)
        => declaredType switch
        {
            Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldTypeDto.Date
                when DateOnly.TryParse(raw.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var date)
                => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldTypeDto.SecurityLink
                when Guid.TryParse(raw.Trim(), out var link)
                => link.ToString("D"),
            Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldTypeDto.Decimal
                or Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldTypeDto.Integer
                when decimal.TryParse(raw.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var number)
                => number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture),
            _ => raw,
        };

    /// <summary>
    /// Stable id for a field conflict: the (source, value) sides are ordered before hashing so the
    /// same disagreement yields the same id regardless of which side is the stored copy.
    /// </summary>
    public static Guid DeterministicFieldConflictId(
        Guid securityId, string fieldPath, string sourceA, string valueA, string sourceB, string valueB)
    {
        var sideA = $"{sourceA}|{valueA}";
        var sideB = $"{sourceB}|{valueB}";
        var ordered = string.CompareOrdinal(sideA, sideB) <= 0
            ? $"{securityId}|{fieldPath}|{sideA}|{sideB}"
            : $"{securityId}|{fieldPath}|{sideB}|{sideA}";

        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(ordered));
        return new Guid(bytes);
    }

    /// <summary>
    /// Generates a stable conflict id from the identifier tuple so that re-detection of the same
    /// conflict yields the same id regardless of which security was encountered first.
    /// </summary>
    public static Guid DeterministicConflictId(string kind, string value, Guid secA, Guid secB)
    {
        var ordered = secA.CompareTo(secB) <= 0
            ? $"{kind}|{value}|{secA}|{secB}"
            : $"{kind}|{value}|{secB}|{secA}";

        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(ordered));
        return new Guid(bytes);
    }
}
