using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.FinancialOperations.Reconciliation.Connectors;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Builds the multi-account margin operator view from retained connector evidence. Provider
/// figures remain authoritative; shadow figures are transparent diagnostics only.
/// </summary>
public sealed class MarginControlCenterReadService(
    StatementCanonicalEvidenceReader evidenceReader,
    MarginCertificationStore certificationStore)
{
    private static readonly TimeSpan MaximumCertificationAge = TimeSpan.FromHours(72);

    public async Task<MarginControlCenterDto> GetAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var artifacts = await evidenceReader.ListAsync(ct).ConfigureAwait(false);
        var certifications = await certificationStore.ListAsync(ct).ConfigureAwait(false);
        var latestAccounts = artifacts
            .SelectMany(envelope => (envelope.Artifact.AccountSnapshots ?? [])
                .Select(snapshot => new AccountEvidence(envelope, snapshot)))
            .GroupBy(static item => $"{item.Snapshot.ProviderId}\u001f{item.Snapshot.AccountId}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.OrderByDescending(item => item.Envelope.Artifact.RetainedAtUtc).First())
            .OrderBy(static item => item.Snapshot.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Snapshot.AccountId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var accounts = latestAccounts.Select(item => BuildAccount(item, now, certifications)).ToArray();
        var primes = accounts
            .GroupBy(static account => account.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => new MarginControlPrimeSummaryDto(
                ProviderId: group.Key,
                AccountCount: group.Count(),
                TotalEquity: group.Sum(static account => account.Equity),
                ProviderMaintenanceMargin: SumNullable(group.Select(static account => account.ProviderMaintenanceMargin)),
                ProviderExcessLiquidity: SumNullable(group.Select(static account => account.ProviderExcessLiquidity)),
                CriticalAccountCount: group.Count(static account => account.RiskLevel == "Critical")))
            .OrderBy(static prime => prime.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var alerts = accounts.SelectMany(BuildAlerts)
            .OrderBy(static alert => AlertRank(alert.Severity))
            .ThenBy(static alert => alert.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static alert => alert.AccountId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MarginControlCenterDto(
            GeneratedAtUtc: now,
            Accounts: accounts,
            PrimeSummaries: primes,
            Alerts: alerts,
            ProviderCount: primes.Length,
            AccountCount: accounts.Length,
            ProvisionalAccountCount: accounts.Count(static account => account.SnapshotPhase == "IntradayProvisional"),
            EndOfDayCertificationCandidateCount: accounts.Count(static account => account.CertificationState == "AwaitingOperatorCertification"),
            AuthorityNote: "Provider-reported margin and restrictions are authoritative. Meridian shadow requirements are diagnostic estimates and never trigger liquidation or order routing.",
            NextAction: accounts.Length == 0
                ? "Import or schedule an Alpaca or IB Flex statement containing account and position sections."
                : alerts.Any(static alert => alert.Severity == "Critical")
                    ? "Review critical provider margin restrictions and contact the broker before changing exposure."
                    : "Review provider-versus-shadow variance, then certify eligible end-of-day account snapshots.");
    }

    public async Task<MarginCertificationResultDto> CertifyAsync(
        MarginCertificationRequestDto request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EvidencePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (string.IsNullOrWhiteSpace(request.Note))
            throw new InvalidDataException("A certification note is required.");

        var center = await GetAsync(ct).ConfigureAwait(false);
        var account = center.Accounts.FirstOrDefault(item =>
            string.Equals(item.ProviderId, request.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.AccountId, request.AccountId, StringComparison.OrdinalIgnoreCase));
        if (account is null)
            throw new KeyNotFoundException("The margin account snapshot is not available.");
        if (account.AsOf != request.AsOf || !string.Equals(account.EvidencePath, request.EvidencePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The margin snapshot changed; refresh before certifying it.");
        if (account.SnapshotPhase != "EndOfDay")
            throw new InvalidOperationException("Intraday snapshots are provisional and cannot be certified as end of day.");
        if (account.CertificationState == "StaleEvidence")
            throw new InvalidOperationException("The margin snapshot is stale; fetch current provider evidence before certification.");
        if (account.ActivityComplete == false)
            throw new InvalidOperationException("The activity cursor is incomplete; fetch the account again before certification.");
        if (account.RiskLevel == "Critical")
            throw new InvalidOperationException("Critical provider margin conditions must be resolved before certification.");

        var result = new MarginCertificationResultDto(
            ProviderId: account.ProviderId,
            AccountId: account.AccountId,
            AsOf: account.AsOf,
            EvidencePath: account.EvidencePath,
            CertifiedBy: actor.Trim(),
            CertifiedAtUtc: DateTimeOffset.UtcNow,
            Note: request.Note.Trim(),
            Status: "Certified");
        return await certificationStore.UpsertAsync(result, ct).ConfigureAwait(false);
    }

    private static MarginControlAccountDto BuildAccount(
        AccountEvidence item,
        DateTimeOffset now,
        IReadOnlyList<MarginCertificationResultDto> certifications)
    {
        var snapshot = item.Snapshot;
        var artifact = item.Envelope.Artifact;
        var records = (artifact.Records ?? [])
            .Where(record => record.Kind == StatementRecordKind.Position &&
                             string.Equals(record.Account, snapshot.AccountId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var taxLots = (artifact.TaxLots ?? [])
            .Where(lot => string.IsNullOrWhiteSpace(lot.AccountId) ||
                          string.Equals(lot.AccountId, snapshot.AccountId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var borrowPositions = (artifact.BorrowPositions ?? [])
            .Where(position => string.IsNullOrWhiteSpace(position.AccountId) ||
                               string.Equals(position.AccountId, snapshot.AccountId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var activities = (artifact.ActivityEvents ?? [])
            .Where(activity => activity.Metadata?.TryGetValue("accountId", out var accountId) != true ||
                               string.Equals(accountId, snapshot.AccountId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var shadow = BuildShadow(snapshot, records);
        var activityComplete = artifact.ActivityCursors is { Count: > 0 }
            ? artifact.ActivityCursors.All(static cursor => cursor.IsComplete)
            : (bool?)null;
        var endOfDay = snapshot.SourceAttributes?.TryGetValue("snapshotPhase", out var sourcePhase) == true
            ? string.Equals(sourcePhase, "EndOfDay", StringComparison.OrdinalIgnoreCase)
            : snapshot.AsOf.UtcDateTime.Date < now.UtcDateTime.Date;
        var evidenceStale = now - snapshot.AsOf > MaximumCertificationAge || snapshot.AsOf > now.AddMinutes(5);
        var snapshotPhase = endOfDay ? "EndOfDay" : "IntradayProvisional";
        var certification = certifications.FirstOrDefault(certificationItem =>
            string.Equals(certificationItem.ProviderId, snapshot.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(certificationItem.AccountId, snapshot.AccountId, StringComparison.OrdinalIgnoreCase) &&
            certificationItem.AsOf == snapshot.AsOf &&
            string.Equals(certificationItem.EvidencePath, item.Envelope.RelativePath, StringComparison.OrdinalIgnoreCase));
        var certificationState = evidenceStale
            ? "StaleEvidence"
            : certification is not null
            ? "Certified"
            : activityComplete == false
            ? "IncompleteEvidence"
            : endOfDay
                ? "AwaitingOperatorCertification"
                : "ProvisionalIntraday";
        var riskLevel = ResolveRiskLevel(snapshot, activityComplete, evidenceStale);

        var positionContributions = records
            .GroupBy(static record => record.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildPositionContribution(
                group.Key,
                group.Sum(static record => record.Quantity),
                group.Sum(static record => record.CashAmount),
                snapshot.MarginRegime,
                borrowPositions.FirstOrDefault(position => string.Equals(position.Symbol, group.Key, StringComparison.OrdinalIgnoreCase)),
                taxLots.Count(lot => string.Equals(lot.Symbol, group.Key, StringComparison.OrdinalIgnoreCase)),
                activities.Count(activity => activity.Option is not null &&
                                             string.Equals(activity.Symbol, group.Key, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(static contribution => Math.Abs(contribution.ShadowMaintenanceMargin))
            .ThenBy(static contribution => contribution.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MarginControlAccountDto(
            ProviderId: snapshot.ProviderId,
            AccountId: snapshot.AccountId,
            AsOf: snapshot.AsOf,
            SnapshotPhase: snapshotPhase,
            CertificationState: certificationState,
            Currency: snapshot.Currency,
            MarginRegime: snapshot.MarginRegime.ToString(),
            Cash: snapshot.Cash,
            Equity: snapshot.Equity,
            BuyingPower: snapshot.BuyingPower,
            ProviderInitialMargin: snapshot.InitialMargin,
            ProviderMaintenanceMargin: snapshot.MaintenanceMargin,
            ProviderExcessLiquidity: snapshot.ExcessLiquidity,
            ProviderMarginLoan: snapshot.MarginLoan,
            ShadowModelName: shadow.ModelName,
            ShadowInitialMargin: shadow.InitialMargin,
            ShadowMaintenanceMargin: shadow.MaintenanceMargin,
            ShadowExcessLiquidity: shadow.MaintenanceMargin.HasValue ? snapshot.Equity - shadow.MaintenanceMargin.Value : null,
            MaintenanceVariance: snapshot.MaintenanceMargin.HasValue && shadow.MaintenanceMargin.HasValue
                ? snapshot.MaintenanceMargin.Value - shadow.MaintenanceMargin.Value
                : null,
            RiskLevel: riskLevel,
            ActivityComplete: activityComplete,
            Restrictions: snapshot.Restrictions ?? [],
            PositionContributions: positionContributions,
            OptionLifecycleEventCount: activities.Count(static activity => activity.Option is not null),
            BorrowPositionCount: borrowPositions.Length,
            TaxLotCount: taxLots.Length,
            EvidencePath: item.Envelope.RelativePath,
            CertifiedBy: certification?.CertifiedBy,
            CertifiedAtUtc: certification?.CertifiedAtUtc,
            CertificationNote: certification?.Note);
    }

    private static ShadowRequirement BuildShadow(
        BrokerageAccountSnapshotDto snapshot,
        IReadOnlyList<StatementCanonicalRecord> positions)
    {
        if (snapshot.MarginRegime == BrokerageMarginRegime.Cash)
            return new ShadowRequirement("Cash account (no margin estimate)", 0m, 0m);
        if (snapshot.MarginRegime == BrokerageMarginRegime.Unknown || positions.Count == 0)
            return new ShadowRequirement("Unavailable", null, null);

        decimal initial = 0m;
        decimal maintenance = 0m;
        foreach (var position in positions)
        {
            var marketValue = position.CashAmount != 0m
                ? position.CashAmount
                : position.Quantity * position.Price;
            var absoluteValue = Math.Abs(marketValue);
            if (snapshot.MarginRegime == BrokerageMarginRegime.PortfolioMargin)
            {
                initial += absoluteValue * 0.15m;
                maintenance += absoluteValue * 0.12m;
            }
            else if (position.Quantity < 0m || marketValue < 0m)
            {
                initial += absoluteValue * 1.50m;
                maintenance += absoluteValue * 1.30m;
            }
            else
            {
                initial += absoluteValue * 0.50m;
                maintenance += absoluteValue * 0.25m;
            }
        }

        return snapshot.MarginRegime == BrokerageMarginRegime.PortfolioMargin
            ? new ShadowRequirement("Meridian simplified 15% portfolio stress estimate", initial, maintenance)
            : new ShadowRequirement("Meridian standard Reg T estimate", initial, maintenance);
    }

    private static MarginPositionContributionDto BuildPositionContribution(
        string symbol,
        decimal quantity,
        decimal marketValue,
        BrokerageMarginRegime regime,
        BrokerageBorrowPositionSnapshotDto? borrow,
        int taxLotCount,
        int optionCount)
    {
        var absoluteValue = Math.Abs(marketValue);
        var (initialRate, maintenanceRate) = regime switch
        {
            BrokerageMarginRegime.Cash => (0m, 0m),
            BrokerageMarginRegime.PortfolioMargin => (0.15m, 0.12m),
            _ when quantity < 0m || marketValue < 0m => (1.50m, 1.30m),
            _ => (0.50m, 0.25m)
        };
        return new MarginPositionContributionDto(
            Symbol: symbol,
            Quantity: quantity,
            MarketValue: marketValue,
            ShadowInitialMargin: absoluteValue * initialRate,
            ShadowMaintenanceMargin: absoluteValue * maintenanceRate,
            BorrowStatus: borrow?.Status.ToString(),
            BorrowRate: borrow?.BorrowRate,
            TaxLotCount: taxLotCount,
            OptionLifecycleEventCount: optionCount,
            SecurityId: null,
            SecurityMasterSource: "ProviderStatementSymbolUnresolved");
    }

    private static string ResolveRiskLevel(
        BrokerageAccountSnapshotDto snapshot,
        bool? activityComplete,
        bool evidenceStale)
    {
        if (snapshot.AccountBlocked || snapshot.TradingBlocked || snapshot.ExcessLiquidity < 0m)
            return "Critical";
        if (evidenceStale || activityComplete == false || snapshot.TransfersBlocked ||
            snapshot.ExcessLiquidity is { } excess && snapshot.Equity > 0m && excess <= snapshot.Equity * 0.10m)
        {
            return "Warning";
        }
        return "Normal";
    }

    private static IEnumerable<MarginControlAlertDto> BuildAlerts(MarginControlAccountDto account)
    {
        if (account.ProviderExcessLiquidity < 0m)
        {
            yield return new MarginControlAlertDto(
                "Critical", account.ProviderId, account.AccountId, "PROVIDER_MARGIN_DEFICIT",
                $"Provider excess liquidity is {account.ProviderExcessLiquidity.Value:N2} {account.Currency}.",
                "Confirm the broker restriction and funding or exposure plan; Meridian will not liquidate automatically.");
        }
        if (account.Restrictions.Count > 0)
        {
            yield return new MarginControlAlertDto(
                account.RiskLevel == "Critical" ? "Critical" : "Warning",
                account.ProviderId, account.AccountId, "PROVIDER_RESTRICTION",
                string.Join("; ", account.Restrictions),
                "Review the provider account directly before routing orders or transfers.");
        }
        if (account.ActivityComplete == false)
        {
            yield return new MarginControlAlertDto(
                "Warning", account.ProviderId, account.AccountId, "ACTIVITY_INCOMPLETE",
                "Provider activity paging did not complete.",
                "Fetch the account again before end-of-day certification.");
        }
        if (account.CertificationState == "StaleEvidence")
        {
            yield return new MarginControlAlertDto(
                "Warning", account.ProviderId, account.AccountId, "EVIDENCE_STALE",
                "The latest account snapshot is older than the 72-hour certification window or has a future timestamp.",
                "Fetch a current provider statement before using this account in the end-of-day certification set.");
        }
        if (account.MaintenanceVariance is { } variance && account.ProviderMaintenanceMargin is { } provider &&
            Math.Abs(variance) > Math.Max(100m, Math.Abs(provider) * 0.10m))
        {
            yield return new MarginControlAlertDto(
                "Info", account.ProviderId, account.AccountId, "SHADOW_VARIANCE",
                $"Provider maintenance differs from the Meridian estimate by {variance:N2} {account.Currency}.",
                "Use the variance to investigate concentration, options, or broker-specific house charges; keep provider figures authoritative.");
        }
    }

    private static decimal? SumNullable(IEnumerable<decimal?> values)
    {
        var materialized = values.Where(static value => value.HasValue).Select(static value => value!.Value).ToArray();
        return materialized.Length == 0 ? null : materialized.Sum();
    }

    private static int AlertRank(string severity) => severity switch
    {
        "Critical" => 0,
        "Warning" => 1,
        _ => 2
    };

    private sealed record AccountEvidence(
        StatementCanonicalEvidenceEnvelope Envelope,
        BrokerageAccountSnapshotDto Snapshot);

    private sealed record ShadowRequirement(
        string ModelName,
        decimal? InitialMargin,
        decimal? MaintenanceMargin);
}
