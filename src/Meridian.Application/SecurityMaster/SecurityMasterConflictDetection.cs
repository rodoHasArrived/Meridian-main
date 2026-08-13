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

    /// <summary>
    /// Detects every identifier-ambiguity conflict across the universe: an identifier that multiple
    /// distinct securities claim from different providers. Every returned conflict has status
    /// <c>Open</c> and a deterministic id, so re-detecting the same pair yields the same id.
    /// </summary>
    public static IReadOnlyList<SecurityMasterConflict> DetectAll(
        IReadOnlyList<SecurityProjectionRecord> universe,
        DateTimeOffset detectedAt)
    {
        var byIdentifier = new Dictionary<string, List<(Guid SecurityId, string Provider)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var record in universe)
        {
            foreach (var id in record.Identifiers)
            {
                var key = $"{id.Kind}|{id.Value}";
                if (!byIdentifier.TryGetValue(key, out var entries))
                {
                    entries = new List<(Guid, string)>();
                    byIdentifier[key] = entries;
                }

                var provider = id.Provider ?? UnknownProvider;
                if (!entries.Any(e => e.SecurityId == record.SecurityId))
                {
                    entries.Add((record.SecurityId, provider));
                }
            }
        }

        var conflicts = new List<SecurityMasterConflict>();
        foreach (var (key, entries) in byIdentifier)
        {
            if (entries.Count < 2)
            {
                continue;
            }

            var distinctSecurities = entries.DistinctBy(e => e.SecurityId).ToList();
            if (distinctSecurities.Count < 2)
            {
                continue;
            }

            var parts = key.Split('|', 2);
            var kind = parts[0];
            var value = parts.Length > 1 ? parts[1] : string.Empty;

            var a = distinctSecurities[0];
            var b = distinctSecurities[1];

            conflicts.Add(new SecurityMasterConflict(
                ConflictId: DeterministicConflictId(kind, value, a.SecurityId, b.SecurityId),
                SecurityId: a.SecurityId,
                ConflictKind: IdentifierAmbiguityKind,
                FieldPath: $"Identifiers.{kind}",
                ProviderA: a.Provider,
                ValueA: a.SecurityId.ToString(),
                ProviderB: b.Provider,
                ValueB: b.SecurityId.ToString(),
                DetectedAt: detectedAt,
                Status: "Open"));
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
    {
        var byIdentifier = new Dictionary<string, (Guid SecurityId, string Provider)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var existing in universe)
        {
            if (existing.SecurityId == projection.SecurityId)
            {
                continue;
            }

            foreach (var id in existing.Identifiers)
            {
                var key = $"{id.Kind}|{id.Value}";
                // Track only the first record we encounter for each identifier (deterministic).
                byIdentifier.TryAdd(key, (existing.SecurityId, id.Provider ?? UnknownProvider));
            }
        }

        var conflicts = new List<SecurityMasterConflict>();
        foreach (var id in projection.Identifiers)
        {
            var key = $"{id.Kind}|{id.Value}";
            if (!byIdentifier.TryGetValue(key, out var conflicting))
            {
                continue;
            }

            conflicts.Add(new SecurityMasterConflict(
                ConflictId: DeterministicConflictId(id.Kind.ToString(), id.Value, projection.SecurityId, conflicting.SecurityId),
                SecurityId: projection.SecurityId,
                ConflictKind: IdentifierAmbiguityKind,
                FieldPath: $"Identifiers.{id.Kind}",
                ProviderA: id.Provider ?? UnknownProvider,
                ValueA: projection.SecurityId.ToString(),
                ProviderB: conflicting.Provider,
                ValueB: conflicting.SecurityId.ToString(),
                DetectedAt: detectedAt,
                Status: "Open"));
        }

        return conflicts;
    }

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
    internal static string? ReadComparableFieldValue(SecurityDetailDto detail, string fieldPath)
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
            "EconomicTerms.principalSchedule" => terms.HasPrincipalSchedule
                ? NormalizePrincipalSchedule(terms.PrincipalSchedule!)
                : null,
            "CommonTerms.currency" => detail.Currency,
            "CommonTerms.countryOfRisk" => SecurityTermReader.ReadString(detail.CommonTerms, "countryOfRisk"),
            _ => null,
        };
    }

    /// <inheritdoc cref="ReadComparableFieldValue(SecurityDetailDto, string)"/>
    internal static string? ReadComparableFieldValue(SecurityProjectionRecord projection, string fieldPath)
        => ReadComparableFieldValue(SecurityMasterMapping.ToDetail(projection), fieldPath);

    /// <summary>
    /// Whether a persisted field value and a recorded candidate value assert the same economics:
    /// day counts compare through the canonical convention parser, numerics compare numerically
    /// ("6.00" and "6.0" are the same coupon), and everything else falls back to trimmed
    /// case-insensitive text. A blank persisted value matches nothing — absence is incompleteness,
    /// not agreement.
    /// </summary>
    internal static bool FieldValuesMatch(string fieldPath, string? persisted, string candidate)
    {
        if (string.IsNullOrWhiteSpace(persisted))
        {
            return false;
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

        if (decimal.TryParse(persisted.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var persistedNumber)
            && decimal.TryParse(candidate.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var candidateNumber))
        {
            return persistedNumber == candidateNumber;
        }

        return string.Equals(persisted.Trim(), candidate.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether an OPEN field conflict has been made obsolete by <paramref name="persistedValue"/>:
    /// a later canonical write that matches NEITHER recorded candidate replaced both sources'
    /// asserted values, so the conflict can never resolve to either candidate and only blocks the
    /// queue. A null/blank persisted value is NOT obsolescence — absence of a readable value must
    /// not silently retire a real disagreement.
    /// </summary>
    internal static bool FieldConflictIsObsolete(SecurityMasterConflict conflict, string? persistedValue)
        => !string.IsNullOrWhiteSpace(persistedValue)
           && !FieldValuesMatch(conflict.FieldPath, persistedValue, conflict.ValueA)
           && !FieldValuesMatch(conflict.FieldPath, persistedValue, conflict.ValueB);

    /// <summary>
    /// The governed field paths whose values differ between <paramref name="current"/> and
    /// <paramref name="incoming"/>, regardless of source. Cross-source disagreement is what OPENS a
    /// conflict, but per-field attribution goes stale whenever a governed field changes hands —
    /// including when the previous winner amends its own value — so retirement must not depend on
    /// the same-source short-circuit that conflict creation uses.
    /// </summary>
    internal static IReadOnlyList<string> ChangedGovernedFieldPaths(
        SecurityProjectionRecord current,
        SecurityProjectionRecord incoming)
        => DetectFieldConflictsCore(
                current, incoming, DateTimeOffset.UtcNow,
                requireDistinctSources: false,
                // A CLEARED value changed hands too: attribution for a value that no longer exists
                // is exactly as stale as attribution for a replaced one, so absence transitions
                // count as changes here even though conflict creation needs both values present.
                includeAbsenceTransitions: true)
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
        IReadOnlyDictionary<string, string>? incumbentFieldSources = null)
        => DetectFieldConflictsCore(
            current, incoming, detectedAt,
            requireDistinctSources: true,
            includeAbsenceTransitions: false,
            incumbentFieldSources);

    private static IReadOnlyList<SecurityMasterConflict> DetectFieldConflictsCore(
        SecurityProjectionRecord current,
        SecurityProjectionRecord incoming,
        DateTimeOffset detectedAt,
        bool requireDistinctSources,
        bool includeAbsenceTransitions,
        IReadOnlyDictionary<string, string>? incumbentFieldSources = null)
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

        return conflicts;

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
            // Both sources must assert a schedule for a disagreement to exist, mirroring the
            // both-present rule the scalar comparators use for sparse providers. A schedule that
            // was REMOVED is still a change for absence-transition consumers.
            if (a is not { Count: > 0 } || b is not { Count: > 0 })
            {
                if (includeAbsenceTransitions && (a is { Count: > 0 }) != (b is { Count: > 0 }))
                {
                    Add("EconomicTerms.principalSchedule", SecurityMasterConflictKinds.EconomicTermMismatch,
                        a is { Count: > 0 } ? NormalizePrincipalSchedule(a) : string.Empty,
                        b is { Count: > 0 } ? NormalizePrincipalSchedule(b) : string.Empty);
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

        void CompareText(string fieldPath, string kind, string? a, string? b)
        {
            if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
                && !string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase))
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
