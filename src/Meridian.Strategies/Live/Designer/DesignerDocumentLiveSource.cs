using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Strategies.Live.Designer;

/// <summary>
/// Resolves promoted runs whose strategy is a Strategy Designer document, closing the
/// designer-strategy activation gap tracked by <c>PRD-020</c>.
/// </summary>
/// <remarks>
/// <para>
/// Before this source existed, <see cref="LiveStrategyCatalog"/> resolved only the two built-in
/// reference strategies and plugin assemblies, so a promoted designer run recorded governance and
/// then deferred forever. Registering this source as an <see cref="IBacktestStrategyLiveSource"/>
/// gives designer documents the same live path plugin strategies already had.
/// </para>
/// <para>
/// Resolution is fail-closed at every step: the document must exist, pass designer validation, and
/// compile to a <see cref="DesignerStrategyPlan"/> whose every construct has real live semantics.
/// Each refusal returns a reason the catalog surfaces through
/// <c>LiveTradingEngine.DeferAsync</c>, which records it as an operator-visible
/// <c>ActivationDeferred</c> lifecycle event on the run itself.
/// </para>
/// <para>
/// No arbitrary code executes here. Designer documents compile to a closed expression grammar
/// (<see cref="DesignerExpression"/>), so this path needs none of the process isolation
/// <c>PRD-012</c> requires for QuantLab's Roslyn scripting; documents that do carry free-form code
/// are refused and pointed at the plugin or QuantLab route instead.
/// </para>
/// </remarks>
public sealed class DesignerDocumentLiveSource : IBacktestStrategyLiveSource
{
    /// <summary>Run parameter naming the designer document to activate.</summary>
    public const string DesignerDocumentParameterKey = "designerDocumentId";

    private static readonly TimeSpan DefaultLoadTimeout = TimeSpan.FromSeconds(30);

    private readonly IStrategyDesignRepository? _repository;
    private readonly StrategyDesignService _designService;
    private readonly ILogger<DesignerDocumentLiveSource>? _logger;
    private readonly TimeSpan _loadTimeout;

    /// <param name="repository">
    /// Design document store. Null on a host that composes the trading engine without the
    /// workstation design surfaces; designer-backed runs then defer with that as the stated
    /// reason rather than the source silently not being consulted.
    /// </param>
    /// <param name="designService">Normalization and validation shared with the designer endpoints.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="loadTimeout">Bounds the bridged repository read; defaults to 30 seconds.</param>
    public DesignerDocumentLiveSource(
        IStrategyDesignRepository? repository,
        StrategyDesignService designService,
        ILogger<DesignerDocumentLiveSource>? logger = null,
        TimeSpan? loadTimeout = null)
    {
        _repository = repository;
        _designService = designService ?? throw new ArgumentNullException(nameof(designService));
        _logger = logger;
        _loadTimeout = loadTimeout is { } timeout && timeout > TimeSpan.Zero ? timeout : DefaultLoadTimeout;
    }

    /// <inheritdoc/>
    public bool TryCreate(
        LiveStrategyCreationContext context,
        out IBacktestStrategy? strategy,
        out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(context);
        strategy = null;
        failureReason = null;

        if (!TryResolveDocumentId(context, out var documentId))
        {
            // Not a designer-backed run. Stay silent so other fallbacks get their turn.
            return false;
        }

        if (_repository is null)
        {
            failureReason =
                $"Run references designer document '{documentId}', but no strategy design repository is " +
                "registered on this host, so the design cannot be loaded.";
            return false;
        }

        var document = LoadDocument(documentId, out failureReason);
        if (document is null)
        {
            return false;
        }

        // Normalization, validation, and compilation are wrapped together because a malformed
        // stored document can make the shared design service throw rather than report -- duplicate
        // cell ids reach its ToDictionary, for one. An escape here would propagate out of
        // TryLaunchAsync and abort the whole startup resume sweep, so one unreadable design would
        // stop every other retained paper and live run from resuming. It becomes this run's
        // deferral reason instead.
        DesignerStrategyPlan? plan;
        try
        {
            var normalized = _designService.Normalize(document);

            if (!TryVerifyRevision(context, documentId, normalized, out failureReason))
            {
                _logger?.LogWarning(
                    "Designer document {DocumentId} failed revision verification for run {StrategyId}: {Reason}",
                    documentId,
                    context.StrategyId,
                    failureReason);
                return false;
            }

            var validation = _designService.Validate(normalized);

            if (!DesignerStrategyPlan.TryCompile(normalized, validation, out plan, out failureReason))
            {
                _logger?.LogWarning(
                    "Designer document {DocumentId} cannot be activated for run {StrategyId}: {Reason}",
                    documentId,
                    context.StrategyId,
                    failureReason);
                return false;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failureReason =
                $"Designer document '{documentId}' could not be compiled: {ex.Message}. The stored design is " +
                "malformed; re-save it from the Strategy Builder before promoting a run against it.";
            _logger?.LogWarning(
                ex,
                "Designer document {DocumentId} threw while compiling for run {StrategyId}",
                documentId,
                context.StrategyId);
            return false;
        }

        strategy = new DesignerDocumentStrategy(plan!, _logger);
        failureReason = null;
        return true;
    }

    /// <summary>
    /// A run is designer-backed when it names a document explicitly. The run's own strategy id is
    /// accepted as the document id only under the <c>strategy-design-</c> prefix the designer
    /// assigns, so an unrelated run id is never treated as a missing designer document and does
    /// not mask the catalog's own "no implementation registered" message.
    /// </summary>
    private static bool TryResolveDocumentId(LiveStrategyCreationContext context, out string documentId)
    {
        if (context.Parameters.TryGetValue(DesignerDocumentParameterKey, out var explicitId)
            && !string.IsNullOrWhiteSpace(explicitId))
        {
            documentId = explicitId.Trim();
            return true;
        }

        if (context.StrategyId.StartsWith("strategy-design-", StringComparison.OrdinalIgnoreCase))
        {
            documentId = context.StrategyId.Trim();
            return true;
        }

        documentId = string.Empty;
        return false;
    }

    /// <summary>
    /// Confirms the loaded document is the revision the run was promoted against.
    /// </summary>
    /// <remarks>
    /// The repository returns the latest saved revision for a document id, so without this an edit
    /// made after backtest or approval would silently become what the promoted run trades. The
    /// hash is required rather than optional: no designer run could activate before this change,
    /// so there is no earlier run to grandfather, and treating a missing hash as "trust it" would
    /// leave the governance hole open for exactly the runs that skip promotion.
    /// </remarks>
    private static bool TryVerifyRevision(
        LiveStrategyCreationContext context,
        string documentId,
        StrategyDesignDocument normalized,
        out string? failureReason)
    {
        if (!context.Parameters.TryGetValue(DesignerDocumentRevision.ParameterKey, out var approvedHash)
            || string.IsNullOrWhiteSpace(approvedHash))
        {
            failureReason =
                $"Run references designer document '{documentId}' but carries no " +
                $"'{DesignerDocumentRevision.ParameterKey}', so the engine cannot tell whether the stored design " +
                "is still the one that was backtested and approved. Re-run the design and promote it again.";
            return false;
        }

        var actualHash = DesignerDocumentRevision.ComputeHash(normalized);
        if (!string.Equals(actualHash, approvedHash.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            failureReason =
                $"Designer document '{documentId}' has changed since this run was promoted (approved revision " +
                $"{Shorten(approvedHash)}, stored revision {Shorten(actualHash)}). Re-run the backtest and promote " +
                "the current design rather than activating an unapproved revision.";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static string Shorten(string hash) =>
        hash.Length <= 12 ? hash : string.Concat(hash.AsSpan(0, 12), "...");

    private StrategyDesignDocument? LoadDocument(string documentId, out string? failureReason)
    {
        try
        {
            // The catalog contract is synchronous and sits on the promotion path, so the async
            // repository read is bridged here under an explicit timeout: a stalled store must
            // defer the run with a reason, never hold the launch loop open indefinitely.
            using var timeout = new CancellationTokenSource(_loadTimeout);

            // WaitAsync enforces the bound even when a repository implementation ignores the token:
            // cancelling a token cannot stop a delegate that has already started, so relying on
            // cooperative cancellation alone would let a stalled store block the promotion path
            // and the startup resume sweep indefinitely.
            var document = Task.Run(
                    () => _repository!.GetAsync(documentId, timeout.Token),
                    CancellationToken.None)
                .WaitAsync(_loadTimeout)
                .GetAwaiter()
                .GetResult();

            if (document is null)
            {
                failureReason =
                    $"Designer document '{documentId}' was not found in the strategy design repository. " +
                    "Save the design before promoting a run that references it.";
                return null;
            }

            failureReason = null;
            return document;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            failureReason =
                $"Loading designer document '{documentId}' exceeded {_loadTimeout.TotalSeconds:F0}s; " +
                "the run stays deferred rather than activating without its design.";
            _logger?.LogWarning(
                "Timed out loading designer document {DocumentId} after {TimeoutSeconds}s",
                documentId,
                _loadTimeout.TotalSeconds);
            return null;
        }
        catch (Exception ex)
        {
            failureReason = $"Designer document '{documentId}' could not be loaded: {ex.Message}";
            _logger?.LogWarning(ex, "Failed to load designer document {DocumentId}", documentId);
            return null;
        }
    }
}
