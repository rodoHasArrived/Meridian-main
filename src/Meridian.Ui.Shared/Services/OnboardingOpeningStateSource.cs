using System.Text.Json;
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
public sealed class OnboardingOpeningStateSource(IAccountQueryService? accounts = null) : IOnboardingOpeningStateProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<OnboardingSourceCaptureDto> CaptureOpeningAsync(OnboardingWorkspaceDto workspace, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (accounts is null)
            throw new OnboardingValidationException("Meridian account records are unavailable in this host.");
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
            var payload = JsonSerializer.Serialize(new { AccountId = selected, AsOfDate = asOf, Balance = balance, Positions = positions }, Json);
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
                observations.Add(new("Position", selected, currency, $"position-quantity:{position.Identifier}", position.Quantity, null, [snapshotId], []));
                observations.Add(new("Position", selected, currency, $"position-market-value:{position.Identifier}", position.MarketValue, null, [snapshotId], []));
            }
        }
        return new(snapshots, observations, missing, null);
    }
}
