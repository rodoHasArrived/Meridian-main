using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.FundAccounts;
using Microsoft.Extensions.Logging;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Application.Tenancy;

/// <summary>
/// Resolves affected accounting scopes from custodied holdings: an account that held the security on
/// the effective date is a scope the fact reaches.
///
/// <para>Custodian position lines are the authoritative holdings record here because they are
/// externally sourced and account-keyed — they say who actually held the security, rather than who
/// was configured to. The account row supplies the narrow scope (fund, portfolio, custody
/// sub-account, ledger reference, functional currency), and
/// <see cref="IFundProfileTenancyRegistry"/> supplies the owning tenant/company.</para>
///
/// <para>Attribution is all-or-nothing. A holding this provider can see but cannot key to an owning
/// tenant — an account with no fund, or a fund the registry has never bound — makes the whole slice
/// non-authoritative. Dropping such a holding would under-report the affected set, and guessing an
/// owner for it is exactly the defaulting that fail-closed tenancy forbids.</para>
/// </summary>
public sealed class FundAccountHoldingScopeAssignmentProvider : IScopeAssignmentProvider
{
    /// <summary>Stable authority id stamped onto every assignment this provider asserts.</summary>
    public const string Authority = "fund-account-custodied-holdings";

    private readonly IFundAccountStore? _accounts;
    private readonly IFundProfileTenancyRegistry? _tenancy;
    private readonly ILogger<FundAccountHoldingScopeAssignmentProvider> _logger;

    public FundAccountHoldingScopeAssignmentProvider(
        IFundAccountStore? accounts,
        IFundProfileTenancyRegistry? tenancy,
        ILogger<FundAccountHoldingScopeAssignmentProvider> logger)
    {
        _accounts = accounts;
        _tenancy = tenancy;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string AuthorityId => Authority;

    /// <inheritdoc />
    public async Task<ScopeAssignmentProviderResult> ResolveAsync(
        ScopeFanOutRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (_accounts is null)
        {
            return ScopeAssignmentProviderResult.NotAuthoritative(
                "No fund-account store is configured, so custodied holdings cannot be enumerated.");
        }

        if (_tenancy is null)
        {
            return ScopeAssignmentProviderResult.NotAuthoritative(
                "No fund-profile tenancy registry is configured, so holdings cannot be attributed to an owning tenant.");
        }

        var wanted = BuildIdentifierIndex(request.Identifiers);
        if (wanted.Count == 0)
        {
            return ScopeAssignmentProviderResult.NotAuthoritative(
                "The security's identifiers normalize to nothing, so no holding can be matched to it.");
        }

        var asOf = request.EffectiveDate;
        var resolvedAsOfUtc = new DateTimeOffset(asOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Enumerated across every tenant on purpose. A caller-scoped account list would make a
        // fact that also reaches other tenants look like it reaches only this one, which is the
        // false-confident answer this authority exists to prevent.
        IReadOnlyList<AccountSummaryDto> accounts;
        try
        {
            accounts = await _accounts
                .QueryAccountsAcrossTenantsAsync(new AccountStructureQuery(ActiveOnly: false), ct)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            return ScopeAssignmentProviderResult.NotAuthoritative(
                "The configured fund-account store cannot enumerate accounts across tenants, so the affected set would be limited to the caller's own tenant.");
        }

        var assignments = new List<AuthoritativeScopeAssignment>();
        var blockers = new List<string>();

        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();
            if (!CanHoldSecurities(account.AccountType) || !WasEffective(account, resolvedAsOfUtc))
            {
                continue;
            }

            // A missing statement is not an empty holding. Custodian positions are keyed to the
            // exact statement date, so an account with no statement on the effective date has
            // unobserved holdings — reporting it as a non-holder would silently shrink the
            // affected set, which is the one error this authority may not make.
            var batches = await _accounts
                .GetCustodianStatementBatchesAsync(account.AccountId, asOf, ct)
                .ConfigureAwait(false);
            if (batches.Count == 0)
            {
                blockers.Add(
                    $"Account '{account.AccountCode}' has no custodian statement for {asOf:yyyy-MM-dd}, so whether it holds the security is unobserved.");
                continue;
            }

            var lines = await _accounts
                .GetCustodianPositionsAsync(account.AccountId, asOf, ct)
                .ConfigureAwait(false);
            if (!HoldsSecurity(lines, wanted))
            {
                continue;
            }

            var fundProfileId = account.FundId?.ToString("D");
            if (string.IsNullOrWhiteSpace(fundProfileId))
            {
                blockers.Add(
                    $"Account '{account.AccountCode}' holds the security but is not assigned to a fund, so its owning tenant cannot be resolved.");
                continue;
            }

            var ownership = await _tenancy.ResolveAsync(fundProfileId, ct).ConfigureAwait(false);
            if (ownership is null || string.IsNullOrWhiteSpace(ownership.TenantId))
            {
                blockers.Add(
                    $"Fund '{fundProfileId}' holds the security but has no bound tenant owner, so the affected scope cannot be attributed.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(ownership.CompanyId))
            {
                blockers.Add(
                    $"Fund '{fundProfileId}' is bound to tenant '{ownership.TenantId}' with no company, so the affected scope is incomplete.");
                continue;
            }

            assignments.Add(new AuthoritativeScopeAssignment(
                ownership.TenantId.Trim(),
                ownership.CompanyId.Trim(),
                Authority,
                resolvedAsOfUtc,
                StructureNodeId: NarrowestStructureNode(account.VehicleId, account.SleeveId, account.EntityId),
                FundProfileId: fundProfileId,
                FinancialAccountId: account.AccountId.ToString("D"),
                PortfolioId: NormalizeOptional(account.PortfolioId),
                CustodyAccountId: NormalizeOptional(account.CustodianDetails?.SubAccountNumber),
                LedgerBookId: NormalizeOptional(account.LedgerReference),
                FunctionalCurrency: NormalizeOptional(account.BaseCurrency)));
        }

        if (blockers.Count > 0)
        {
            _logger.LogWarning(
                "Custodied-holdings scope attribution incomplete for security {SecurityId}: {BlockerCount} holding(s) could not be attributed",
                request.SecurityId,
                blockers.Count);
            return new ScopeAssignmentProviderResult(false, assignments, blockers);
        }

        return ScopeAssignmentProviderResult.Authoritative(assignments);
    }

    // Only account kinds that can custody securities are candidates. A bank or ledger-control
    // account never carries custodian position lines, so demanding a statement for one would
    // block every fan-out on an account that could not have held the security anyway.
    private static bool CanHoldSecurities(AccountTypeDto accountType)
        => accountType is AccountTypeDto.Brokerage
            or AccountTypeDto.Custody
            or AccountTypeDto.Margin
            or AccountTypeDto.PrimeBroker;

    // The store's account query ignores its as-of argument, so effectivity is applied here rather
    // than assumed: an account opened after the effective date, or closed before it, did not hold
    // the security on that date.
    private static bool WasEffective(AccountSummaryDto account, DateTimeOffset asOfUtc)
        => account.EffectiveFrom <= asOfUtc
            && (account.EffectiveTo is not { } closed || closed >= asOfUtc);

    private static bool HoldsSecurity(
        IReadOnlyList<CustodianPositionLineDto> lines,
        IReadOnlyDictionary<string, HashSet<string>> wanted)
    {
        foreach (var line in lines)
        {
            if (line.Quantity == 0m)
            {
                continue;
            }

            var normalizedValue = NormalizeIdentifierValue(line.IdentifierType, line.Identifier);
            if (normalizedValue.Length == 0)
            {
                continue;
            }

            var type = NormalizeIdentifierType(line.IdentifierType);
            if (wanted.TryGetValue(type, out var values) && values.Contains(normalizedValue))
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, HashSet<string>> BuildIdentifierIndex(
        IReadOnlyList<ScopeFanOutIdentifier> identifiers)
    {
        var index = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var identifier in identifiers)
        {
            var normalizedValue = NormalizeIdentifierValue(identifier.IdentifierType, identifier.Value);
            if (normalizedValue.Length == 0)
            {
                continue;
            }

            var type = NormalizeIdentifierType(identifier.IdentifierType);
            if (!index.TryGetValue(type, out var values))
            {
                values = new HashSet<string>(StringComparer.Ordinal);
                index[type] = values;
            }

            values.Add(normalizedValue);
        }

        return index;
    }

    // Identifier values are compared on the security master's own normalization so a custodian's
    // punctuation or casing cannot make a real holding look like a different instrument. An
    // identifier type the security master does not recognize still matches on its trimmed value,
    // because the type is compared separately and a custodian-specific kind is not a mismatch.
    private static string NormalizeIdentifierValue(string? identifierType, string? value)
        => SecurityIdentifierNormalizer.NormalizeAliasValue(identifierType, value);

    private static string NormalizeIdentifierType(string? identifierType)
        => identifierType?.Trim().ToUpperInvariant() ?? string.Empty;

    // Narrowest structure node first: a sleeve or vehicle is a more exact assignment than the
    // legal entity that contains it, and the fan-out set should name the most specific scope the
    // account actually sits in.
    private static string? NarrowestStructureNode(params Guid?[] candidates)
        => candidates
            .Where(static candidate => candidate is { } value && value != Guid.Empty)
            .Select(static candidate => candidate!.Value.ToString("D"))
            .FirstOrDefault();
}
