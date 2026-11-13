using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Read-only fixed-income input coverage for exact tenant-owned connections. A successful login
/// never establishes a verified bond workflow, market-data entitlement, or execution authority.
/// </summary>
public sealed class BondWorkflowConnectionReadinessService(
    ProviderConnectionService connections,
    ProviderConnectionLifecycleService? lifecycle = null,
    IDataVendorEntitlementService? entitlements = null,
    TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<IReadOnlyList<BondConnectionCapabilityDto>> GetAsync(string tenantId, string? accountId = null,
        Guid? securityId = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var retained = await connections.GetConnectionsForTenantAsync(tenantId, ct).ConfigureAwait(false);
        var licenses = entitlements is null ? [] : await entitlements.GetAllAsync(ct).ConfigureAwait(false);
        var rows = new List<BondConnectionCapabilityDto>();
        foreach (var connection in retained)
        {
            ProviderConnectionRowDto? credential = null;
            if (lifecycle is not null)
            {
                try
                { credential = await lifecycle.GetConnectionStatusForTenantAsync(connection.ConnectionId, tenantId, ct).ConfigureAwait(false); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
                { /* An unconfigured/unrecognized connection remains visible with unavailable credentials. */ }
            }
            rows.AddRange(BuildRows(connection, credential, licenses, _clock.GetUtcNow(), accountId, securityId));
        }
        rows.AddRange(ImportRows());
        return rows;
    }

    /// <summary>Pure projection used by endpoint and contract tests; credentials contain status only.</summary>
    public static IReadOnlyList<BondConnectionCapabilityDto> BuildRows(ProviderConnectionDto connection,
        ProviderConnectionRowDto? credential, IReadOnlyList<DataVendorEntitlementDto> entitlements,
        DateTimeOffset now, string? accountId = null, Guid? securityId = null)
    {
        var provider = connection.ProviderFamilyId.Trim().ToLowerInvariant();
        var inputs = new (string Capability, DataVendorDataType Type)[]
        {
            ("IdentifierDiscovery", DataVendorDataType.Identifiers), ("ReferenceTerms", DataVendorDataType.TermsAndConditions),
            ("Holdings", DataVendorDataType.Holdings), ("Quotes", DataVendorDataType.Pricing),
            ("ObservedFixings", DataVendorDataType.RateFixings), ("InflationIndexes", DataVendorDataType.InflationIndexes),
            ("PrincipalFactors", DataVendorDataType.CashFlows), ("CreditRatings", DataVendorDataType.CreditRatings),
            ("Curves", DataVendorDataType.Curves), ("Execution", DataVendorDataType.Classification)
        };
        return inputs.Select(input =>
        {
            // This describes the bond workflow's typed input adapter, not a vendor-wide Bond flag.
            var implemented = (provider, input.Capability) switch
            {
                ("openfigi", "IdentifierDiscovery") => true,
                ("fred", "ObservedFixings" or "InflationIndexes") => true,
                ("ibkr" or "ib" or "interactivebrokers" or "alpaca", "Holdings") => true,
                _ => false
            };
            var configured = connection.Enabled && credential?.CredentialState is
                ProviderCredentialStateDto.Configured or ProviderCredentialStateDto.Verified or ProviderCredentialStateDto.NotRequired;
            var matching = entitlements.Where(row => string.Equals(row.VendorName.Trim(), provider, StringComparison.OrdinalIgnoreCase)
                    && row.DataType == input.Type
                    && (row.AccountId is null || accountId is not null && string.Equals(row.AccountId, accountId, StringComparison.Ordinal))
                    && (row.SecurityId is null || securityId.HasValue && row.SecurityId == securityId)
                    && row.ClientId is null && row.FundProfileId is null).ToArray();
            bool? entitled = matching.Length == 0 ? null : matching.Any(row => row.EffectiveFrom <= now
                && (row.EffectiveTo is null || row.EffectiveTo >= now)
                && row.Status is DataVendorEntitlementStatus.Active or DataVendorEntitlementStatus.ExpiringSoon);
            var blockers = new List<string>();
            if (!implemented)
                blockers.Add("No typed bond-workflow adapter supplies this input through this connection.");
            if (!connection.Enabled)
                blockers.Add("The connection is disabled.");
            else if (!configured)
                blockers.Add("Credentials for this exact connection are unavailable or incomplete.");
            if (entitled is null)
                blockers.Add("Applicable input-specific entitlement has not been established.");
            else if (entitled == false)
                blockers.Add("Applicable input-specific entitlement is inactive or outside its effective dates.");
            if (input.Capability == "Holdings")
                blockers.Add("Bond quantities, denomination and valuation basis require retained normalization review before use.");
            return new BondConnectionCapabilityDto(connection.ConnectionId, provider, input.Capability, implemented, configured,
                entitled, implemented ? BondProviderEvidenceStatus.LiveUntested : BondProviderEvidenceStatus.Unsupported,
                null, null, blockers);
        }).ToArray();
    }

    private static IEnumerable<BondConnectionCapabilityDto> ImportRows()
    {
        foreach (var capability in new[] { "ReferenceTerms", "Holdings", "Quotes", "ObservedFixings", "InflationIndexes", "PrincipalFactors", "CreditRatings", "Curves" })
            yield return new BondConnectionCapabilityDto("manual-import", "manual-import", capability, true, true, true,
                BondProviderEvidenceStatus.SyntheticVerified, null,
                "Deterministic import, normalization and retained-input tests; imported values still require their own source evidence.", []);
    }
}
