using System.Threading;
using System.Threading.Tasks;
using Meridian.Backtesting;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Wpf.Services;

/// <summary>
/// WPF singleton wrapper around <see cref="BacktestEngine"/> that manages the active backtest
/// run state and exposes the last result to the UI.
/// </summary>
public sealed class BacktestService
{
    private static readonly Lazy<BacktestService> _instance = new(() => new BacktestService());
    public static BacktestService Instance => _instance.Value;

    private CancellationTokenSource? _cts;

    /// <summary>
    /// Optional Security Master query service injected by DI to enable corporate action price adjustments.
    /// Set this before the first backtest run.
    /// </summary>
    public ISecurityMasterQueryService? SecurityMasterQueryService { get; set; }

    /// <summary>
    /// Optional corporate action adjustment service injected by DI to enable split/dividend adjustments.
    /// Set this before the first backtest run.
    /// </summary>
    public ICorporateActionAdjustmentService? CorporateActionAdjustmentService { get; set; }

    public BacktestResult? LastResult { get; private set; }
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Optional corporate-action adjustment service injected by the DI container when a
    /// Security Master connection string is configured. When set, the <see cref="BacktestEngine"/>
    /// will apply split and dividend adjustments to historical bars before replaying them.
    /// </summary>
    public ICorporateActionAdjustmentService? CorporateActionAdjustmentService { get; set; }

    public event EventHandler<BacktestResult>? BacktestCompleted;
    public event EventHandler? BacktestCancelled;

    private BacktestService() { }

    /// <summary>Starts a backtest run asynchronously. Only one run may be active at a time.</summary>
    public async Task<BacktestResult?> RunAsync(
        BacktestRequest request,
        IBacktestStrategy strategy,
        IProgress<BacktestProgressEvent>? progress = null, CancellationToken ct = default)
    {
        if (IsRunning) return null;

        _cts = new CancellationTokenSource();
        IsRunning = true;

        try
        {
            var storageOptions = new StorageOptions { RootPath = request.DataRoot };
            var catalogService = new StorageCatalogService(request.DataRoot, storageOptions);
            var engineLogger = NullLogger<BacktestEngine>.Instance;
<<<<<<< copilot/add-corporate-action-adjustment-service-implementa
            var engine = new BacktestEngine(engineLogger, catalogService,
                corporateActionAdjustment: CorporateActionAdjustmentService);
=======
            var engine = new BacktestEngine(
                engineLogger,
                catalogService,
                SecurityMasterQueryService,
                CorporateActionAdjustmentService);
>>>>>>> main

            var result = await engine.RunAsync(request, strategy, progress, _cts.Token);
            LastResult = result;
            BacktestCompleted?.Invoke(this, result);
            return result;
        }
        catch (OperationCanceledException)
        {
            BacktestCancelled?.Invoke(this, EventArgs.Empty);
            return null;
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>Cancels the active backtest run if one is in progress.</summary>
    public void Cancel() => _cts?.Cancel();
}
