using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Routes UFL projection rebuild requests through the shared Security Master rebuild pipeline.
/// </summary>
public sealed class UflProjectionRebuilder : IUflProjectionRebuilder
{
    private static readonly HashSet<string> SupportedAssetClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Equity",
        "Option",
        "Future",
        "Bond",
        "FxSpot",
        "Deposit",
        "MoneyMarketFund",
        "CertificateOfDeposit",
        "CommercialPaper",
        "TreasuryBill",
        "Repo",
        "CashSweep",
        "OtherSecurity",
        "Swap",
        "DirectLoan",
        "Commodity",
        "CryptoCurrency",
        "Cfd",
        "Warrant"
    };

    private readonly SecurityMasterRebuildOrchestrator _orchestrator;
    private readonly ILogger<UflProjectionRebuilder> _logger;

    public UflProjectionRebuilder(
        SecurityMasterRebuildOrchestrator orchestrator,
        ILogger<UflProjectionRebuilder> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task RebuildAsync(string assetClass, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(assetClass))
        {
            throw new ArgumentException("Asset class is required.", nameof(assetClass));
        }

        if (!SupportedAssetClasses.Contains(assetClass))
        {
            throw new ArgumentOutOfRangeException(
                nameof(assetClass),
                assetClass,
                "Asset class is not supported by the UFL projection rebuild pipeline.");
        }

        _logger.LogInformation(
            "Rebuilding UFL projections for asset class {AssetClass} via security master projection replay.",
            assetClass);

        await _orchestrator.RebuildAsync(assetClass, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// No-op projection rebuilder used when Security Master backing storage is not configured.
/// </summary>
public sealed class NullUflProjectionRebuilder : IUflProjectionRebuilder
{
    private readonly ILogger<NullUflProjectionRebuilder> _logger;

    public NullUflProjectionRebuilder(ILogger<NullUflProjectionRebuilder> logger)
    {
        _logger = logger;
    }

    public Task RebuildAsync(string assetClass, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "Ignoring UFL projection rebuild request for asset class {AssetClass} because Security Master storage is not configured.",
            assetClass);
        return Task.CompletedTask;
    }
}
