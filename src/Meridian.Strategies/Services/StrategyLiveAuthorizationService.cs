using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Storage.Archival;
using Meridian.Strategies.Artifacts;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Storage;

namespace Meridian.Strategies.Services;

/// <summary>Resolves immutable promotion scope and serializes a run's revocation with dispatch.</summary>
public sealed class StrategyLiveAuthorizationService(
    string dataRoot,
    IStrategyRepository runs,
    IPromotionRecordStore promotions,
    StrategyArtifactRegistry artifacts,
    IExecutionGateway? gateway,
    TimeProvider clock,
    Func<Guid, CancellationToken, Task<bool>> accountIsCurrent) : ILiveStrategyOrderAuthorization
{
    private readonly string _directory = Path.GetFullPath(Path.Combine(dataRoot, "strategies", "authorization-revocations"));

    public async Task<(StrategyLiveAuthorization? Grant, string? Failure)> PrepareGrantAsync(
        StrategyRunEntry source, StrategyLiveAuthorizationRequest? request, CancellationToken ct)
    {
        if (source.StrategyArtifactId is not { } artifactId || request is null ||
            !string.Equals(artifactId, request.ExpectedStrategyArtifactId, StringComparison.Ordinal))
            return (null, "Live approval requires the expected artifact ID of the reviewed Paper run.");
        if (source.FundAccountId is not { } account || account == Guid.Empty ||
            gateway is not IBrokerageConnectionState { ScopeIdentity: { } brokerScope } ||
            !await accountIsCurrent(account, ct).ConfigureAwait(false))
            return (null, "Live approval requires a retained fund account and a verified broker account connection.");
        if (!string.Equals(request.Currency, "USD", StringComparison.OrdinalIgnoreCase) ||
            request.MaxOrderNotional <= 0m || request.ExpiresAt <= clock.GetUtcNow())
            return (null, "Enter a positive USD per-order ceiling and a future expiry.");
        if (await artifacts.GetAsync(artifactId, ct).ConfigureAwait(false) is not { } artifact ||
            !string.Equals(artifact.StrategyId, source.StrategyId, StringComparison.Ordinal) ||
            artifact.Route == StrategyAuthoringRoute.QuantScript)
            return (null, "The reviewed strategy artifact is missing or its runtime is deferred.");

        var symbols = (request.AllowedSymbols ?? [])
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        if (symbols.Length == 0 || symbols.Any(symbol => !artifact.Universe.Contains(symbol, StringComparer.OrdinalIgnoreCase)))
            return (null, "Authorize at least one symbol from the retained strategy universe.");

        return (new(account, gateway.GatewayId, Sha256Digest.ComputeUtf8(brokerScope), symbols, request.MaxOrderNotional,
            "USD", request.ExpiresAt.ToUniversalTime()), null);
    }

    public async Task<StrategyLiveAuthorizationStatus> CheckRunAsync(StrategyRunEntry run, CancellationToken ct)
    {
        var promotion = await FindPromotionAsync(run.RunId, ct).ConfigureAwait(false);
        return await EvaluateAsync(run, promotion, ct).ConfigureAwait(false);
    }

    public async Task<LiveStrategyOrderAuthorizationLease> AcquireAsync(
        string runId, string? strategyArtifactId, Guid? fundAccountId, string symbol, Func<decimal?> measureUsdNotional,
        CancellationToken ct)
    {
        var authorityLock = await LockAsync(runId, ct).ConfigureAwait(false);
        try
        {
            var run = await runs.GetRunByIdAsync(runId, ct).ConfigureAwait(false);
            var promotion = await FindPromotionAsync(runId, ct).ConfigureAwait(false);
            var status = await EvaluateAsync(run, promotion, ct).ConfigureAwait(false);
            var grant = promotion?.LiveAuthorization;
            if (grant is null || status.State != "Active")
                return new(status.Reason, authorityLock);
            if (run!.TerminalStatus == Meridian.Contracts.Workstation.StrategyRunStatus.Paused ||
                run.FundAccountId != fundAccountId ||
                !string.Equals(run.StrategyArtifactId, strategyArtifactId, StringComparison.Ordinal))
                return new("The order differs from the active run's retained account or strategy artifact.", authorityLock);
            if (!grant.AllowedSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
                return new("The symbol is outside the live strategy authorization.", authorityLock);
            var usdNotional = measureUsdNotional();
            if (usdNotional is not > 0m || usdNotional > grant.MaxOrderNotional)
                return new("The order exceeds the live strategy authorization's per-order notional ceiling.", authorityLock);
            if (clock.GetUtcNow() >= grant.ExpiresAt)
                return new("The live strategy authorization has expired.", authorityLock);

            return new(null, authorityLock);
        }
        catch
        {
            await authorityLock.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async Task<StrategyLiveAuthorizationStatus> DescribeAsync(StrategyPromotionRecord promotion, CancellationToken ct)
    {
        var run = promotion.TargetRunId is { } id ? await runs.GetRunByIdAsync(id, ct).ConfigureAwait(false) : null;
        return await EvaluateAsync(run, promotion, ct).ConfigureAwait(false);
    }

    internal async Task<StrategyLiveAuthorizationRevocation> RevokeAsync(
        StrategyPromotionRecord promotion, string actor, string reason, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var runId = promotion.TargetRunId!;
        await using var authorityLock = await LockAsync(runId, ct).ConfigureAwait(false);
        if (await ReadRevocationAsync(promotion, ct).ConfigureAwait(false) is { } previous)
            return previous;

        var receipt = new StrategyLiveAuthorizationRevocation(promotion.PromotionId, actor, reason.Trim(), clock.GetUtcNow());
        var payload = JsonSerializer.Serialize(receipt, StrategyLiveAuthorizationJsonContext.Default.StrategyLiveAuthorizationRevocation);
        await AtomicFileWriter.WriteAsync(RevocationPath(runId), payload, ct).ConfigureAwait(false);
        return receipt;
    }

    internal async Task<StrategyLiveAuthorizationStatus?> GetWithdrawalAsync(StrategyPromotionRecord promotion, CancellationToken ct)
    {
        if (promotion.LiveAuthorization is not { } grant)
            return null;
        if (await ReadRevocationAsync(promotion, ct).ConfigureAwait(false) is not null)
            return new("Revoked", "The live strategy authorization has been revoked.");
        return clock.GetUtcNow() >= grant.ExpiresAt
            ? new("Expired", "The live strategy authorization has expired.")
            : null;
    }

    private async Task<StrategyLiveAuthorizationStatus> EvaluateAsync(
        StrategyRunEntry? run, StrategyPromotionRecord? promotion, CancellationToken ct)
    {
        if (promotion?.LiveAuthorization is not { } grant)
            return new("Blocked", "No live authorization is recorded for the promoted run.");
        if (await GetWithdrawalAsync(promotion, ct).ConfigureAwait(false) is { } withdrawal)
            return withdrawal;
        if (run is not { RunType: RunType.Live, EndedAt: null, StrategyArtifactId: { } artifactId } ||
            run.FundAccountId != grant.FundAccountId ||
            !string.Equals(run.AuditReference, promotion.AuditReference, StringComparison.Ordinal) ||
            !string.Equals(run.ParentRunId, promotion.SourceRunId, StringComparison.Ordinal) ||
            !string.Equals(artifactId, promotion.StrategyArtifactId, StringComparison.Ordinal) ||
            await artifacts.GetAsync(artifactId, ct).ConfigureAwait(false) is not { } artifact ||
            !string.Equals(artifact.StrategyId, run.StrategyId, StringComparison.Ordinal))
            return new("Blocked", "The live authorization no longer matches the retained run and strategy artifact.");
        if (!await accountIsCurrent(grant.FundAccountId, ct).ConfigureAwait(false) ||
            !string.Equals(gateway?.GatewayId, grant.GatewayId, StringComparison.OrdinalIgnoreCase) ||
            gateway is not IBrokerageConnectionState { ScopeIdentity: { } brokerScope } ||
            !string.Equals(Sha256Digest.ComputeUtf8(brokerScope), grant.BrokerScope, StringComparison.Ordinal))
            return new("Blocked", "The verified broker account differs from the live authorization.");

        return clock.GetUtcNow() >= grant.ExpiresAt
            ? new("Expired", "The live strategy authorization has expired.")
            : new("Active", null);
    }

    private async Task<StrategyPromotionRecord?> FindPromotionAsync(string runId, CancellationToken ct) =>
        (await promotions.LoadAllAsync(ct).ConfigureAwait(false)).LastOrDefault(record =>
            record.TargetRunType == RunType.Live && record.Decision == PromotionDecisionKinds.Approved &&
            string.Equals(record.TargetRunId, runId, StringComparison.Ordinal));

    private async Task<StrategyLiveAuthorizationRevocation?> ReadRevocationAsync(StrategyPromotionRecord promotion, CancellationToken ct)
    {
        // An inaccessible or malformed record must never be interpreted as lack of revocation.
        string payload;
        try
        {
            payload = await File.ReadAllTextAsync(RevocationPath(promotion.TargetRunId!), ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }

        var receipt = JsonSerializer.Deserialize(payload, StrategyLiveAuthorizationJsonContext.Default.StrategyLiveAuthorizationRevocation);
        if (receipt is null || receipt.AuthorizationId != promotion.PromotionId ||
            string.IsNullOrWhiteSpace(receipt.Actor) || string.IsNullOrWhiteSpace(receipt.Reason))
            throw new InvalidDataException("The live authorization revocation could not be verified.");
        return receipt;
    }

    private Task<FileStream> LockAsync(string runId, CancellationToken ct) =>
        JsonlPromotionRecordStore.AcquireAuthorityLockAsync(RevocationPath(runId) + ".lock", ct);

    private string RevocationPath(string runId) => Path.Combine(_directory, Sha256Digest.ComputeUtf8(runId) + ".json");
}
