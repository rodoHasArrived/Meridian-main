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
    /// </summary>
    public static IReadOnlyList<SecurityMasterConflict> DetectFieldConflicts(
        SecurityProjectionRecord current,
        SecurityProjectionRecord incoming,
        DateTimeOffset detectedAt)
    {
        if (current.SecurityId != incoming.SecurityId)
        {
            return [];
        }

        var sourceA = SecurityMasterProvenanceReader.Read(current.Provenance).SourceSystem;
        var sourceB = SecurityMasterProvenanceReader.Read(incoming.Provenance).SourceSystem;
        if (string.Equals(sourceA, sourceB, StringComparison.OrdinalIgnoreCase))
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
        CompareText("CommonTerms.currency", SecurityMasterConflictKinds.CommonTermMismatch,
            current.Currency, incoming.Currency);
        CompareText("CommonTerms.countryOfRisk", SecurityMasterConflictKinds.CommonTermMismatch,
            SecurityTermReader.ReadString(current.CommonTerms, "countryOfRisk"),
            SecurityTermReader.ReadString(incoming.CommonTerms, "countryOfRisk"));

        return conflicts;

        void Add(string fieldPath, string kind, string valueA, string valueB)
            => conflicts.Add(new SecurityMasterConflict(
                ConflictId: DeterministicFieldConflictId(current.SecurityId, fieldPath, sourceA, valueA, sourceB, valueB),
                SecurityId: current.SecurityId,
                ConflictKind: kind,
                FieldPath: fieldPath,
                ProviderA: sourceA,
                ValueA: valueA,
                ProviderB: sourceB,
                ValueB: valueB,
                DetectedAt: detectedAt,
                Status: "Open"));

        void CompareDate(string fieldPath, DateOnly? a, DateOnly? b)
        {
            if (a is DateOnly left && b is DateOnly right && left != right)
            {
                Add(fieldPath, SecurityMasterConflictKinds.EconomicTermMismatch,
                    left.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    right.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
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
        }

        void CompareText(string fieldPath, string kind, string? a, string? b)
        {
            if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
                && !string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                Add(fieldPath, kind, a.Trim(), b.Trim());
            }
        }

        void CompareDayCount(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
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
