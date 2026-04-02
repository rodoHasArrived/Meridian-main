using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Hosted service that warms the in-memory projection cache on startup when
/// <see cref="SecurityMasterOptions.PreloadProjectionCache"/> is enabled.
/// This eliminates cold-start latency for the first queries after deployment.
/// </summary>
public sealed class SecurityMasterProjectionWarmupService : IHostedService
{
    private readonly SecurityMasterProjectionService _projectionService;
    private readonly SecurityMasterOptions _options;
    private readonly ILogger<SecurityMasterProjectionWarmupService> _logger;
    private readonly SecurityMasterCanonicalSymbolSeedService? _seedService;

    public SecurityMasterProjectionWarmupService(
        SecurityMasterProjectionService projectionService,
        SecurityMasterOptions options,
        ILogger<SecurityMasterProjectionWarmupService> logger,
        SecurityMasterCanonicalSymbolSeedService? seedService = null)
    {
        _projectionService = projectionService;
        _options = options;
        _logger = logger;
        _seedService = seedService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.PreloadProjectionCache)
        {
            _logger.LogDebug("Security Master projection cache pre-warm is disabled (PreloadProjectionCache=false).");
            return;
        }

        _logger.LogInformation("Warming Security Master projection cache on startup...");

        try
        {
            await _projectionService.WarmAsync(cancellationToken).ConfigureAwait(false);

            // Seed the canonical symbol registry from the freshly-populated projection cache.
            if (_seedService is not null)
            {
                await _seedService.SeedAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Security Master projection cache warm-up was cancelled during startup.");
        }
        catch (Exception ex)
        {
            // Log and continue — a warm cache is a performance optimisation, not a hard requirement.
            _logger.LogError(ex, "Security Master projection cache warm-up failed; queries will hit the database directly.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
