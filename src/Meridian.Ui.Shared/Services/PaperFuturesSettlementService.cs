using Meridian.Application.Accounting;
using Meridian.Contracts.Services;
using Meridian.Execution.Services;
using Meridian.Platform.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Ui.Shared.Services;

/// <summary>Options for the paper futures end-of-day variation-margin settlement job.</summary>
public sealed class PaperFuturesSettlementOptions
{
    /// <summary>Configuration section bound to these options.</summary>
    public const string SectionKey = "PaperTrading:FuturesSettlement";

    /// <summary>Whether the job runs. Off by default, so hosts opt in.</summary>
    public bool Enabled { get; set; }

    /// <summary>How often the job looks for closed, unsettled trading days.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The most recent closed trading days one pass settles per position, oldest first. A longer
    /// outage settles from its last days only; the total cash is the same, because each
    /// settlement moves the change since the previous one.
    /// </summary>
    public int MaxCatchUpTradingDays { get; set; } = 5;
}

/// <summary>
/// Settles the daily variation margin of every open paper futures position once per trading day,
/// after the regular close. Each pass walks the active paper sessions; for every variation-margin
/// position it settles each closed trading day after its last settlement (and after the position
/// opened), oldest first, at that day's mark from <see cref="IMarkPriceSource"/>. The close stands
/// in for the exchange settlement price, which is not modelled yet. A day with no price is
/// skipped with a warning and never settled on a stale price. Settlements go through
/// <see cref="PaperSessionPersistenceService.RecordSettlementAsync"/>, which persists before
/// applying and ignores a day already settled, so overlapping or repeated passes are safe.
/// </summary>
public sealed class PaperFuturesSettlementService(
    IServiceProvider serviceProvider,
    IOptions<PaperFuturesSettlementOptions> options,
    ILogger<PaperFuturesSettlementService> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private const string Market = "US";
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogDebug("Paper futures settlement is disabled ({Section}:Enabled is false).", PaperFuturesSettlementOptions.SectionKey);
            return;
        }

        if (serviceProvider.GetService<PaperSessionPersistenceService>() is null
            || serviceProvider.GetService<IMarkPriceSource>() is null)
        {
            logger.LogWarning(
                "Paper futures settlement is enabled but idle: paper sessions or a mark price source are not registered in this host.");
            return;
        }

        var interval = settings.PollInterval > TimeSpan.Zero ? settings.PollInterval : TimeSpan.FromMinutes(15);
        using var timer = new PeriodicTimer(interval, _timeProvider);
        do
        {
            try
            {
                var settled = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                if (settled > 0)
                    logger.LogInformation("Paper futures settlement applied {Count} daily settlement(s).", settled);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Paper futures settlement pass failed; retrying on the next interval.");
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Runs one settlement pass at the current time and returns how many settlements it applied.
    /// Does nothing when the job is disabled or paper sessions or a mark price source are not
    /// registered.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        if (!options.Value.Enabled)
            return 0;

        var persistence = serviceProvider.GetService<PaperSessionPersistenceService>();
        var markSource = serviceProvider.GetService<IMarkPriceSource>();
        if (persistence is null || markSource is null)
            return 0;

        IOperationalTradingCalendar calendar = serviceProvider.GetService<IOperationalTradingCalendar>() ?? new TradingCalendar();
        var now = _timeProvider.GetUtcNow();
        var maxDays = Math.Max(1, options.Value.MaxCatchUpTradingDays);
        var applied = 0;

        foreach (var session in persistence.GetSessions())
        {
            ct.ThrowIfCancellationRequested();
            if (!session.IsActive)
                continue;

            var portfolio = persistence.GetActivePortfolio(session.SessionId);
            if (portfolio is null)
                continue;

            foreach (var position in portfolio.GetOpenVariationMarginPositions())
            {
                var lastSettled = persistence.GetLastSettlementDate(session.SessionId, position.AccountId, position.Symbol);
                foreach (var (tradingDate, closeTime) in DaysToSettle(calendar, position, lastSettled, now, maxDays))
                {
                    var quote = await markSource.GetMarkPriceAsync(position.Symbol, tradingDate, ct).ConfigureAwait(false);
                    if (quote is null || quote.Price <= 0m)
                    {
                        logger.LogWarning(
                            "No settlement price for {Symbol} on {TradingDate} in paper session {SessionId}; leaving the day unsettled.",
                            position.Symbol,
                            tradingDate,
                            session.SessionId);
                        continue;
                    }

                    var record = PaperSessionSettlementRecord.Create(
                        position.AccountId, position.Symbol, tradingDate, quote.Price, closeTime);
                    if (await persistence.RecordSettlementAsync(session.SessionId, record, ct).ConfigureAwait(false))
                        applied++;
                }
            }
        }

        return applied;
    }

    /// <summary>
    /// The closed trading days a position still needs settled, oldest first: each day whose
    /// regular close is after the position opened, after its last settled date, and not later
    /// than now, limited to the most recent <paramref name="maxDays"/>.
    /// </summary>
    private static IReadOnlyList<(DateOnly TradingDate, DateTimeOffset CloseTime)> DaysToSettle(
        IOperationalTradingCalendar calendar,
        VariationMarginPositionSnapshot position,
        DateOnly? lastSettled,
        DateTimeOffset now,
        int maxDays)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var openedAt = position.OpenedAt ?? now;
        var start = DateOnly.FromDateTime(openedAt.UtcDateTime).AddDays(-1);
        if (lastSettled is { } settled && settled >= start)
            start = settled.AddDays(1);

        var days = new List<(DateOnly, DateTimeOffset)>();
        for (var date = start; date <= today; date = date.AddDays(1))
        {
            if (!calendar.IsTradingDay(date, Market))
                continue;

            var regular = calendar.GetTradingSessions(date, Market)
                .FirstOrDefault(static session => string.Equals(session.SessionType, "regular", StringComparison.OrdinalIgnoreCase));
            if (regular is null || regular.CloseTime > now || regular.CloseTime <= openedAt)
                continue;

            days.Add((date, regular.CloseTime));
        }

        return days.Count <= maxDays ? days : days[^maxDays..];
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

/// <summary>Registers the paper futures end-of-day settlement job.</summary>
public static class PaperFuturesSettlementServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="PaperFuturesSettlementOptions"/> from
    /// <see cref="PaperFuturesSettlementOptions.SectionKey"/> and registers the job as a singleton
    /// and hosted service. It stays idle unless enabled and a mark price source is registered.
    /// </summary>
    public static IServiceCollection AddPaperFuturesSettlement(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<PaperFuturesSettlementOptions>(configuration.GetSection(PaperFuturesSettlementOptions.SectionKey));
        services.TryAddSingleton<PaperFuturesSettlementService>();
        services.AddHostedService(sp => sp.GetRequiredService<PaperFuturesSettlementService>());
        return services;
    }
}
