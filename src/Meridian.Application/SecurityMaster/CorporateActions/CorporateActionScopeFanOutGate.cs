using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using ContractSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Application.SecurityMaster.CorporateActions;

/// <summary>
/// Why a source decision was refused. Each member maps to a distinct caller-visible problem: an
/// unestablished affected set is an availability fact the caller may retry, a fan-out that spans
/// scopes is a missing capability, and a fan-out that misses the caller entirely is a scope fact.
/// </summary>
public enum CorporateActionScopeFanOutRefusal
{
    /// <summary>Not a refusal — the decision was permitted.</summary>
    None = 0,

    /// <summary>
    /// The affected set could not be established: no authority saw the whole of its slice, the
    /// security has no resolvable record, or it carries no identifiers to match holdings on.
    /// </summary>
    NotAuthoritative,

    /// <summary>No scope holds the security, so there is no case to open.</summary>
    NoAffectedScope,

    /// <summary>The affected set reaches scopes the deciding caller does not own.</summary>
    ForeignScope,

    /// <summary>The affected set spans several scopes and cannot be applied in one atomic command.</summary>
    MultiScope,
}

/// <summary>
/// The outcome of asking whether a source decision may be applied, and to which exact scope.
/// </summary>
/// <param name="IsPermitted">
/// True only when the affected set was authoritatively enumerated AND is a single scope owned by the
/// deciding caller, so the existing single-command durable path applies it atomically.
/// </param>
/// <param name="ResolvedScope">
/// The server-resolved scope to stamp on the command. Non-null exactly when <paramref name="IsPermitted"/>
/// is true. Its narrow fields come from the assignment authority, never from the caller.
/// </param>
/// <param name="Blockers">Stated reasons the decision is refused. Empty when permitted.</param>
public sealed record CorporateActionScopeFanOutDecision(
    bool IsPermitted,
    CorporateActionCaseScopeDto? ResolvedScope,
    CorporateActionScopeFanOutRefusal Refusal,
    IReadOnlyList<string> Blockers)
{
    /// <summary>A refusal carrying its reason and the stated blockers.</summary>
    public static CorporateActionScopeFanOutDecision Refused(
        CorporateActionScopeFanOutRefusal refusal,
        IReadOnlyList<string> blockers)
        => new(false, null, refusal, blockers);

    /// <summary>A refusal carrying its reason and one stated blocker.</summary>
    public static CorporateActionScopeFanOutDecision Refused(
        CorporateActionScopeFanOutRefusal refusal,
        string blocker)
        => new(false, null, refusal, [blocker]);
}

/// <summary>
/// Decides whether a corporate-action source decision may be applied, by asking the authoritative
/// scope fan-out service which accounting scopes the fact reaches.
/// </summary>
public interface ICorporateActionScopeFanOutGate
{
    /// <summary>
    /// Resolves the exact scope a decision on <paramref name="securityId"/> may be applied to for
    /// the deciding tenant/company, or refuses with stated reasons.
    /// </summary>
    Task<CorporateActionScopeFanOutDecision> ResolveDecisionScopeAsync(
        Guid securityId,
        DateOnly effectiveDate,
        string tenantId,
        string companyId,
        CancellationToken ct = default);
}

/// <summary>
/// Default gate over <see cref="IAuthoritativeScopeFanOutService"/>.
///
/// <para>Two conditions must both hold before a decision is applied, and they are separate
/// questions. First, the affected set must be <em>known</em>: a non-authoritative fan-out is a
/// refusal, never an empty affected set. Second, the known set must be <em>applicable in one
/// command</em>: the durable acceptance path opens exactly one case in one serializable
/// transaction, so a fact reaching several scopes cannot be applied without leaving the remainder
/// un-cased. Refusing the multi-scope case is what keeps "apply the decision atomically" true
/// rather than aspirational; lifting it is store work, not endpoint work.</para>
///
/// <para>A fact reaching a scope the caller does not own is refused for the same reason and one
/// more: the caller may not decide on another tenant's behalf, and telling them the scope exists
/// would itself be a cross-tenant disclosure. The refusal states that the fan-out is not confined
/// to the caller's own scope without naming the others.</para>
/// </summary>
public sealed class CorporateActionScopeFanOutGate : ICorporateActionScopeFanOutGate
{
    internal const string UnknownSecurityBlocker =
        "The security under decision has no resolvable security-master record, so its holders cannot be enumerated.";

    internal const string NoIdentifiersBlocker =
        "The security under decision carries no identifiers, so holdings cannot be attributed to it.";

    internal const string NoAffectedScopeBlocker =
        "No accounting scope in this deployment holds the security on the effective date, so there is no case to open.";

    internal const string ForeignScopeBlocker =
        "The affected scope set is not confined to the deciding tenant and company, and a decision may not be applied on another tenant's behalf.";

    internal const string MultiScopeBlocker =
        "The security is held in more than one accounting scope; applying the decision would leave part of the affected set un-cased, so it is refused until atomic multi-scope application exists.";

    private readonly IAuthoritativeScopeFanOutService _fanOut;
    private readonly ContractSecurityMasterQueryService _securities;

    public CorporateActionScopeFanOutGate(
        IAuthoritativeScopeFanOutService fanOut,
        ContractSecurityMasterQueryService securities)
    {
        _fanOut = fanOut ?? throw new ArgumentNullException(nameof(fanOut));
        _securities = securities ?? throw new ArgumentNullException(nameof(securities));
    }

    /// <inheritdoc />
    public async Task<CorporateActionScopeFanOutDecision> ResolveDecisionScopeAsync(
        Guid securityId,
        DateOnly effectiveDate,
        string tenantId,
        string companyId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
        {
            return CorporateActionScopeFanOutDecision.Refused(
                CorporateActionScopeFanOutRefusal.ForeignScope,
                "A resolved tenant and company are required before a source decision can be scoped.");
        }

        var security = await _securities.GetByIdAsync(securityId, ct).ConfigureAwait(false);
        if (security is null)
        {
            return CorporateActionScopeFanOutDecision.Refused(
                CorporateActionScopeFanOutRefusal.NotAuthoritative,
                UnknownSecurityBlocker);
        }

        var identifiers = BuildIdentifiers(security);
        if (identifiers.Count == 0)
        {
            return CorporateActionScopeFanOutDecision.Refused(
                CorporateActionScopeFanOutRefusal.NotAuthoritative,
                NoIdentifiersBlocker);
        }

        var fanOut = await _fanOut
            .ResolveAffectedScopesAsync(new ScopeFanOutRequest(securityId, identifiers, effectiveDate), ct)
            .ConfigureAwait(false);
        if (!fanOut.IsAuthoritative)
        {
            return CorporateActionScopeFanOutDecision.Refused(
                CorporateActionScopeFanOutRefusal.NotAuthoritative,
                fanOut.Blockers);
        }

        if (fanOut.Scopes.Count == 0)
        {
            return CorporateActionScopeFanOutDecision.Refused(
                CorporateActionScopeFanOutRefusal.NoAffectedScope,
                NoAffectedScopeBlocker);
        }

        if (fanOut.Scopes.Any(scope => !scope.IsOwnedBy(tenantId, companyId)))
        {
            return CorporateActionScopeFanOutDecision.Refused(
                CorporateActionScopeFanOutRefusal.ForeignScope,
                ForeignScopeBlocker);
        }

        if (fanOut.Scopes.Count > 1)
        {
            return CorporateActionScopeFanOutDecision.Refused(
                CorporateActionScopeFanOutRefusal.MultiScope,
                MultiScopeBlocker);
        }

        var resolved = fanOut.Scopes[0];
        return new CorporateActionScopeFanOutDecision(
            true,
            new CorporateActionCaseScopeDto(
                tenantId.Trim(),
                companyId.Trim(),
                resolved.StructureNodeId,
                resolved.FundProfileId,
                resolved.FinancialAccountId,
                resolved.PortfolioId,
                resolved.CustodyAccountId,
                resolved.LedgerBookId,
                resolved.PeriodId,
                resolved.AccountingBasis,
                resolved.FunctionalCurrency,
                resolved.Jurisdiction),
            CorporateActionScopeFanOutRefusal.None,
            []);
    }

    // Aliases are included alongside identifiers because custodian holdings are commonly keyed by a
    // vendor or internal code the security master retains as an alias rather than a primary
    // identifier; omitting them would under-report the affected set.
    private static IReadOnlyList<ScopeFanOutIdentifier> BuildIdentifiers(SecurityDetailDto security)
    {
        var identifiers = new List<ScopeFanOutIdentifier>(
            security.Identifiers.Count + security.Aliases.Count);
        foreach (var identifier in security.Identifiers)
        {
            if (!string.IsNullOrWhiteSpace(identifier.Value))
            {
                identifiers.Add(new ScopeFanOutIdentifier(identifier.Kind.ToString(), identifier.Value));
            }
        }

        foreach (var alias in security.Aliases)
        {
            if (alias.IsEnabled && !string.IsNullOrWhiteSpace(alias.AliasValue))
            {
                identifiers.Add(new ScopeFanOutIdentifier(alias.AliasKind, alias.AliasValue));
            }
        }

        return identifiers;
    }
}
