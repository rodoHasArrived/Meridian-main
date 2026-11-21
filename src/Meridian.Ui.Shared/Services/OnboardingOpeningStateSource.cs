using System.Text.Json;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Onboarding;
using Meridian.FinancialOperations.Onboarding.OpeningBalances;
using Meridian.PortfolioRecords.Accounts;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Reads Meridian's own retained account records for the workspace start date: the latest balance snapshot
/// (cash and NAV components) and the retained position lines. It never imports, posts, or substitutes zero
/// for an absent component.
/// </summary>
public sealed class OnboardingOpeningStateSource(
    IAccountQueryService? accounts = null,
    ICloseReadinessSubjectSource? subjects = null,
    IFundProfileTenancyRegistry? tenancy = null) : IOnboardingOpeningStateProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<OnboardingSourceCaptureDto> CaptureOpeningAsync(OnboardingWorkspaceDto workspace, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (accounts is null || subjects is null || tenancy is null)
            throw new OnboardingValidationException("Meridian account, book membership, and tenant ownership authorities are required to capture opening records.");
        var before = await ValidateScopeAsync(workspace.Scope, ct).ConfigureAwait(false);
        var asOf = workspace.Scope.StartDate;
        var snapshots = new List<OnboardingSourceSnapshotDto>();
        var observations = new List<OnboardingObservationDto>();
        var missing = new List<OnboardingMissingSourceDto>();
        foreach (var selected in workspace.Scope.AccountIds)
        {
            ct.ThrowIfCancellationRequested();
            var accountId = Guid.Parse(selected);
            var balance = (await accounts.GetBalanceTimelineAsync(accountId, asOf, asOf, ct).ConfigureAwait(false))
                .Where(item => item.AccountId == accountId && item.AsOfDate == asOf)
                .OrderBy(item => item.RecordedAt).ThenBy(item => item.SnapshotId).LastOrDefault();
            var positions = (await accounts.GetCustodianPositionsAsync(accountId, asOf, ct).ConfigureAwait(false))
                .Where(item => item.AccountId == accountId && item.AsOfDate == asOf)
                .OrderBy(item => item.Identifier, StringComparer.Ordinal).ToArray();
            var payload = JsonSerializer.Serialize(new
            {
                Scope = workspace.Scope,
                Ownership = before.Ownership,
                SubjectEvidenceVersion = before.Versions[selected],
                AccountId = selected,
                AsOfDate = asOf,
                Balance = balance,
                Positions = positions
            }, Json);
            var snapshotId = $"meridian-opening:{selected}";
            snapshots.Add(new(snapshotId, "MeridianOpeningState", selected, balance?.SnapshotId.ToString("D") ?? "none",
                OnboardingWorkspaceService.HashSourcePayload(payload), DateTimeOffset.UtcNow, asOf, "", payload,
                balance?.ExternalReference is { Length: > 0 } reference ? [reference] : []));
            if (balance is null)
            {
                missing.Add(new("onboarding.opening.meridian_missing", "MeridianOpeningState", selected,
                    $"Meridian holds no balance snapshot for this account dated {asOf:yyyy-MM-dd}."));
            }
            else
            {
                observations.Add(new("Balance", selected, balance.Currency.ToUpperInvariant(), null, balance.CashBalance, null,
                    [snapshotId], []));
                var navComplete = balance.SecuritiesMarketValue.HasValue && balance.AccruedInterest.HasValue && balance.PendingSettlement.HasValue;
                observations.Add(new("Nav", selected, balance.Currency.ToUpperInvariant(), null,
                    navComplete ? balance.CashBalance + balance.SecuritiesMarketValue!.Value + balance.AccruedInterest!.Value + balance.PendingSettlement!.Value : null,
                    null, [snapshotId], [],
                    navComplete ? null : "Meridian NAV requires explicit securities, accrual, and settlement components; absent components are not zero."));
            }
            foreach (var position in positions)
            {
                var currency = position.Currency.ToUpperInvariant();
                observations.Add(new("Position", selected, currency, $"position-quantity:{position.Identifier}",
                    position.IsShort ? -Math.Abs(position.Quantity) : position.Quantity, null, [snapshotId], []));
                observations.Add(new("Position", selected, currency, $"position-market-value:{position.Identifier}", position.MarketValue, null, [snapshotId], []));
            }
        }
        var after = await ValidateScopeAsync(workspace.Scope, ct).ConfigureAwait(false);
        if (before.Ownership != after.Ownership || before.Versions.Count != after.Versions.Count
            || before.Versions.Any(item => !after.Versions.TryGetValue(item.Key, out var version) || item.Value != version))
            throw new OnboardingValidationException("The opening account or ledger-book membership changed during capture. Refresh the scope and retry.");
        return new(snapshots, observations, missing, null);
    }

    private async Task<(FundProfileOwnership Ownership, Dictionary<string, string> Versions)> ValidateScopeAsync(
        OnboardingScopeDto scope, CancellationToken ct)
    {
        var ownership = await tenancy!.ResolveAsync(scope.FundProfileId, ct).ConfigureAwait(false);
        if (ownership is null || !ownership.IsHeldBy(scope.TenantId)
            || !string.Equals(ownership.CompanyId, scope.CompanyId, StringComparison.OrdinalIgnoreCase))
            throw new OnboardingValidationException("The selected opening fund is not owned by this tenant and company.");
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var selected in scope.AccountIds)
        {
            if (!Guid.TryParse(selected, out var accountId) || accountId == Guid.Empty)
                throw new OnboardingValidationException("Opening records require canonical financial account identities.");
            var requested = new CloseReadinessScopeDto(scope.FundProfileId, scope.LedgerBookId, accountId, scope.EntityId, null);
            var subject = await subjects!.GetSubjectAsync(requested, ct).ConfigureAwait(false);
            if (subject is null || subject.Scope != requested || subject.Status != "Ready"
                || string.IsNullOrWhiteSpace(subject.EvidenceVersion))
                throw new OnboardingValidationException("Every opening account must still belong to the selected active entity, fund, and ledger book.");
            if (!versions.TryAdd(selected, subject.EvidenceVersion))
                throw new OnboardingValidationException("The selected opening account population contains duplicates.");
        }
        if (versions.Count == 0)
            throw new OnboardingValidationException("Select an explicit opening account population.");
        return (ownership, versions);
    }
}
