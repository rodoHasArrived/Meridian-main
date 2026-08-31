namespace Meridian.Contracts.Tenancy;

/// <summary>
/// One authoritative scope assignment: the exact tenant, company, and narrow accounting scope that a
/// registered authority asserts is affected by a security-level fact.
///
/// <para>An assignment is a statement of record, not a caller-supplied hint. Every field beyond
/// <see cref="TenantId"/>/<see cref="CompanyId"/> is populated only when the asserting authority can
/// name it from durable assignment data; a field it cannot attribute stays null rather than being
/// defaulted, so a consumer can tell "not applicable" from "not resolved".</para>
/// </summary>
/// <param name="TenantId">Owning tenant. Never blank on a resolved assignment.</param>
/// <param name="CompanyId">Owning company. Never blank on a resolved assignment.</param>
/// <param name="AuthorityId">Stable id of the provider that asserted this assignment, for audit.</param>
/// <param name="ResolvedAsOfUtc">The observation time the assertion was resolved against.</param>
public sealed record AuthoritativeScopeAssignment(
    string TenantId,
    string CompanyId,
    string AuthorityId,
    DateTimeOffset ResolvedAsOfUtc,
    string? StructureNodeId = null,
    string? FundProfileId = null,
    string? FinancialAccountId = null,
    string? PortfolioId = null,
    string? CustodyAccountId = null,
    string? LedgerBookId = null,
    string? PeriodId = null,
    string? AccountingBasis = null,
    string? FunctionalCurrency = null,
    string? Jurisdiction = null)
{
    /// <summary>
    /// Identity of the scope this assignment names, ignoring which authority asserted it and when.
    /// Two authorities that resolve the same accounting scope must produce the same key so the
    /// fan-out set is a set of scopes rather than a set of assertions.
    /// </summary>
    public string ScopeKey => string.Join(
        '|',
        Normalize(TenantId),
        Normalize(CompanyId),
        Normalize(StructureNodeId),
        Normalize(FundProfileId),
        Normalize(FinancialAccountId),
        Normalize(PortfolioId),
        Normalize(CustodyAccountId),
        Normalize(LedgerBookId),
        Normalize(PeriodId),
        Normalize(AccountingBasis),
        Normalize(FunctionalCurrency),
        Normalize(Jurisdiction));

    /// <summary>Whether this assignment is owned by the supplied tenant/company pair.</summary>
    public bool IsOwnedBy(string? tenantId, string? companyId)
        => !string.IsNullOrWhiteSpace(tenantId)
            && !string.IsNullOrWhiteSpace(companyId)
            && string.Equals(Normalize(TenantId), Normalize(tenantId), StringComparison.Ordinal)
            && string.Equals(Normalize(CompanyId), Normalize(companyId), StringComparison.Ordinal);

    private static string Normalize(string? value)
        => value?.Trim().ToUpperInvariant() ?? string.Empty;
}

/// <summary>One identifier a holdings record may carry for the security under decision.</summary>
/// <param name="IdentifierType">Identifier kind as written by the holdings source (ticker, cusip, isin, …).</param>
/// <param name="Value">Identifier value; matching is normalization-based, not literal.</param>
public sealed record ScopeFanOutIdentifier(string IdentifierType, string Value);

/// <summary>
/// A request to enumerate every accounting scope affected by a security-level fact.
/// </summary>
/// <param name="SecurityId">Canonical security identity the fact applies to.</param>
/// <param name="Identifiers">
/// Every identifier the security is known by. Holdings are matched on these; an empty list is not a
/// "match nothing" instruction but a missing input, and authorities must fail closed on it rather
/// than report an empty affected set.
/// </param>
/// <param name="EffectiveDate">The date holdings are evaluated as of (an ex-date, record date, or equivalent).</param>
public sealed record ScopeFanOutRequest(
    Guid SecurityId,
    IReadOnlyList<ScopeFanOutIdentifier> Identifiers,
    DateOnly EffectiveDate);

/// <summary>
/// The result of a fan-out resolution.
///
/// <para><see cref="IsAuthoritative"/> is the only field a caller may gate a mutation on.
/// <see cref="Scopes"/> is meaningful only when it is true: a non-authoritative result may still
/// carry partially resolved scopes for diagnostics, and acting on those would apply a decision to
/// some affected scopes while silently missing others.</para>
/// </summary>
/// <param name="IsAuthoritative">
/// True only when every registered authority answered completely. False when no authority is
/// composed, when one could not attribute a holding, or when a backing store was unavailable.
/// </param>
/// <param name="Scopes">Distinct affected scopes, deterministically ordered by <see cref="AuthoritativeScopeAssignment.ScopeKey"/>.</param>
/// <param name="Blockers">Stated reasons the result is not authoritative. Empty when it is.</param>
public sealed record ScopeFanOutResult(
    bool IsAuthoritative,
    IReadOnlyList<AuthoritativeScopeAssignment> Scopes,
    IReadOnlyList<string> Blockers)
{
    /// <summary>A fail-closed result carrying a single stated reason.</summary>
    public static ScopeFanOutResult NotAuthoritative(string blocker)
        => new(false, [], [blocker]);

    /// <summary>An authoritative result over the supplied affected scopes (possibly none).</summary>
    public static ScopeFanOutResult Authoritative(IReadOnlyList<AuthoritativeScopeAssignment> scopes)
        => new(true, scopes, []);
}

/// <summary>
/// The authoritative multi-tenant scope fan-out authority: given a security-level fact, it names
/// every accounting scope that fact reaches, or refuses to answer.
///
/// <para>This is the tenancy authority that security-master decisions depend on. A corporate-action
/// source decision is a statement about a globally observed fact, but applying it creates per-scope
/// casework; without an authority that can enumerate the affected scopes, a decision taken in one
/// tenant either silently misses other affected tenants or leaks their existence. Consumers must
/// treat a non-authoritative result as a denial, never as an empty affected set.</para>
/// </summary>
public interface IAuthoritativeScopeFanOutService
{
    /// <summary>
    /// Enumerates every accounting scope affected by the fact, across all tenants.
    /// Never throws for an unavailable backing store — that is reported as a blocker.
    /// </summary>
    Task<ScopeFanOutResult> ResolveAffectedScopesAsync(
        ScopeFanOutRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Enumerates the affected scopes owned by exactly one tenant/company, for a caller that may
    /// only see its own tenancy. The result is authoritative only when the full cross-tenant
    /// resolution was authoritative, so a caller cannot obtain a confident single-tenant answer
    /// while the wider fan-out is unknown.
    /// </summary>
    Task<ScopeFanOutResult> ResolveOwnedScopesAsync(
        string tenantId,
        string companyId,
        ScopeFanOutRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// One authority over a slice of the scope-assignment graph (custodied holdings, ledger books,
/// mandates, …). Providers are composed by <see cref="IAuthoritativeScopeFanOutService"/>; each is
/// individually responsible for failing closed rather than under-reporting.
/// </summary>
public interface IScopeAssignmentProvider
{
    /// <summary>Stable id stamped onto every assignment this provider asserts.</summary>
    string AuthorityId { get; }

    /// <summary>
    /// Resolves the scopes this authority can attribute the fact to. Implementations must report
    /// <c>IsAuthoritative = false</c> with a stated blocker whenever they cannot see the whole of
    /// their own slice, and must not throw for expected unavailability.
    /// </summary>
    Task<ScopeAssignmentProviderResult> ResolveAsync(
        ScopeFanOutRequest request,
        CancellationToken ct = default);
}

/// <summary>One authority's contribution to a fan-out resolution.</summary>
/// <param name="IsAuthoritative">Whether this authority saw the whole of its own slice.</param>
/// <param name="Scopes">Scopes this authority attributes the fact to.</param>
/// <param name="Blockers">Stated reasons this authority could not answer completely.</param>
public sealed record ScopeAssignmentProviderResult(
    bool IsAuthoritative,
    IReadOnlyList<AuthoritativeScopeAssignment> Scopes,
    IReadOnlyList<string> Blockers)
{
    /// <summary>A fail-closed contribution carrying a single stated reason.</summary>
    public static ScopeAssignmentProviderResult NotAuthoritative(string blocker)
        => new(false, [], [blocker]);

    /// <summary>A complete contribution over the supplied scopes (possibly none).</summary>
    public static ScopeAssignmentProviderResult Authoritative(IReadOnlyList<AuthoritativeScopeAssignment> scopes)
        => new(true, scopes, []);
}
