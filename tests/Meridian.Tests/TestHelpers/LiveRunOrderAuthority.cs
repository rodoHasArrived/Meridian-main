using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;

namespace Meridian.Tests.TestHelpers;

/// <summary>
/// Submits test orders the way a live strategy run does: under typed strategy-run authority for a
/// run that an approved live promotion targets. Live routing refuses untyped orders, so tests of
/// live-path behavior (readiness, brokerage limits, controls) submit through this helper and give
/// the OMS <see cref="Source"/> as its run-authority source.
/// </summary>
/// <remarks>
/// The run id is read from the order's <c>runId</c> metadata, exactly as the untyped path read
/// it, so a test's readiness fakes see the same run as before.
/// </remarks>
public sealed class LiveRunOrderAuthority
{
    public const string RuntimeActor = "live-test-runtime";
    public const string DefaultRunId = "live-test-run";

    /// <summary>The account every order in <see cref="Default"/> routes under.</summary>
    public static readonly Guid DefaultAccountId = Guid.Parse("7a1c0b5e-3c2d-4e8f-9a6b-1d2e3f405162");

    public static LiveRunOrderAuthority Default { get; } = new(DefaultAccountId);

    private readonly PromotedRunSource _source;

    public LiveRunOrderAuthority(Guid fundAccountId)
    {
        FundAccountId = fundAccountId;
        _source = new PromotedRunSource(fundAccountId);
    }

    /// <summary>The account used for orders that name none.</summary>
    public Guid FundAccountId { get; }

    /// <summary>
    /// Reports every run as active and targeted by an approved live promotion, for the account the
    /// run's last command was granted (or <see cref="FundAccountId"/> for a run with no command yet).
    /// </summary>
    public IStrategyRunAuthoritySource Source => _source;

    /// <summary>Typed strategy-run authority for <paramref name="order"/>.</summary>
    public OrderSubmissionAuthority AuthorityFor(OrderRequest order)
    {
        ArgumentNullException.ThrowIfNull(order);
        string? runId = null;
        order.Metadata?.TryGetValue("runId", out runId);
        var account = order.FundAccountId ?? FundAccountId;
        var resolvedRunId = string.IsNullOrWhiteSpace(runId) ? DefaultRunId : runId.Trim();
        _source.Bind(resolvedRunId, account);
        var grant = new ExecutionAccountGrant(account, RuntimeActor, ExecutionAccountGrantSources.StrategyRun, DateTimeOffset.UtcNow);
        return OrderSubmissionAuthority.ForStrategyRun(RuntimeActor, grant, resolvedRunId);
    }

    /// <summary>The submit command for <paramref name="order"/> under typed strategy-run authority.</summary>
    public OrderSubmitCommand Command(OrderRequest order) =>
        new(order with { FundAccountId = order.FundAccountId ?? FundAccountId }, AuthorityFor(order));

    private sealed class PromotedRunSource(Guid defaultAccountId) : IStrategyRunAuthoritySource
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Guid> _accounts = new(StringComparer.Ordinal);

        public void Bind(string runId, Guid fundAccountId) => _accounts[runId] = fundAccountId;

        public Task<StrategyRunAuthoritySnapshot?> GetAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult<StrategyRunAuthoritySnapshot?>(new StrategyRunAuthoritySnapshot(
                RunId: runId,
                FundAccountId: _accounts.TryGetValue(runId, out var account) ? account : defaultAccountId,
                IsActive: true,
                PromotionId: $"promotion-{runId}",
                PromotionTargetRunId: runId,
                PromotionAuditReference: $"audit://promotion/{runId}",
                RunStrategyArtifactId: null,
                PromotedStrategyArtifactId: null));
    }
}

/// <summary>Submit helpers for tests of live-path OMS behavior.</summary>
public static class LiveRunOrderSubmission
{
    /// <summary>Submits <paramref name="order"/> under <see cref="LiveRunOrderAuthority.Default"/>.</summary>
    public static Task<OrderResult> SubmitLiveRunAsync(this IOrderManager oms, OrderRequest order, CancellationToken ct = default) =>
        oms.SubmitAsync(LiveRunOrderAuthority.Default.Command(order), ct);

    /// <summary>Submits <paramref name="order"/> under typed authority for <paramref name="authority"/>'s account.</summary>
    public static Task<OrderResult> SubmitLiveRunAsync(
        this IOrderManager oms,
        OrderRequest order,
        LiveRunOrderAuthority authority,
        CancellationToken ct = default) =>
        oms.SubmitAsync(authority.Command(order), ct);
}
