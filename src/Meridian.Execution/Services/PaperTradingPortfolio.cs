using System.Collections.Concurrent;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Margin;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Ledger;

namespace Meridian.Execution.Services;

/// <summary>
/// Tracks cash, positions, and realised P&amp;L for a paper trading session.
/// Supports both single-account (backward-compatible) and multi-account modes.
/// Optionally posts double-entry journal entries to a <see cref="Ledger"/> after each fill.
/// </summary>
public sealed class PaperTradingPortfolio : IMultiAccountPortfolioState
{
    /// <summary>Key used for the default / backwards-compatible account.</summary>
    public const string DefaultAccountId = "default";

    private readonly Meridian.Ledger.Ledger? _ledger;
    private readonly ILivePositionCorporateActionAdjuster? _corporateActionAdjuster;
    private readonly PositionLotSelectionMethod _lotSelectionMethod;
    private readonly Lock _lock = new();

    // Multi-account state — the default account always exists at key DefaultAccountId.
    private readonly Dictionary<string, AccountState> _accounts =
        new(StringComparer.OrdinalIgnoreCase);

    // ─── Constructors ────────────────────────────────────────────────────────

    /// <summary>
    /// Single-account initialiser for backwards compatibility.
    /// Creates one brokerage account with id <see cref="DefaultAccountId"/>.
    /// </summary>
#pragma warning disable CS0618
    public PaperTradingPortfolio(
        decimal initialCash,
        Meridian.Ledger.Ledger? ledger = null,
        Meridian.Application.SecurityMaster.CorporateActions.ILivePositionCorporateActionAdjuster? corporateActionAdjuster = null,
        PositionLotSelectionMethod lotSelectionMethod = PositionLotSelectionMethod.Fifo)
        : this(initialCash, ledger, (ILivePositionCorporateActionAdjuster?)corporateActionAdjuster, lotSelectionMethod)
    {
    }
#pragma warning restore CS0618

    /// <summary>Composes the portfolio through the Contracts-owned adjuster.</summary>
    public PaperTradingPortfolio(
        decimal initialCash,
        Meridian.Ledger.Ledger? ledger,
        ILivePositionCorporateActionAdjuster? corporateActionAdjuster,
        PositionLotSelectionMethod lotSelectionMethod = PositionLotSelectionMethod.Fifo)
    {
        if (initialCash < 0)
            throw new ArgumentOutOfRangeException(nameof(initialCash), "Initial cash must be non-negative.");

        _ledger = ledger;
        _corporateActionAdjuster = corporateActionAdjuster;
        _lotSelectionMethod = lotSelectionMethod;

        var defaultAccount = new AccountState(
            DefaultAccountId, "Default Paper Account", AccountKind.Brokerage, initialCash, syncRoot: _lock);
        _accounts[DefaultAccountId] = defaultAccount;

        if (ledger is not null && initialCash > 0)
        {
            ledger.PostLines(
                DateTimeOffset.UtcNow,
                "Initial capital — paper trading session",
                [
                    (LedgerAccounts.Cash, initialCash, 0m),
                    (LedgerAccounts.CapitalAccount, 0m, initialCash),
                ]);
        }
    }

    /// <summary>
    /// Multi-account initialiser.  Each entry in <paramref name="accounts"/> is registered
    /// with its own cash balance, kind, and display name.
    /// The first entry becomes the <em>primary</em> account (equivalent of <see cref="DefaultAccountId"/>
    /// for single-account callers).
    /// </summary>
    /// <param name="accounts">At least one account must be supplied.</param>
    /// <param name="ledger">Optional shared ledger.</param>
    /// <param name="corporateActionAdjuster">Optional corporate-action adjuster.</param>
#pragma warning disable CS0618
    public PaperTradingPortfolio(
        IReadOnlyList<AccountDefinition> accounts,
        Meridian.Ledger.Ledger? ledger = null,
        Meridian.Application.SecurityMaster.CorporateActions.ILivePositionCorporateActionAdjuster? corporateActionAdjuster = null,
        PositionLotSelectionMethod lotSelectionMethod = PositionLotSelectionMethod.Fifo)
        : this(accounts, ledger, (ILivePositionCorporateActionAdjuster?)corporateActionAdjuster, lotSelectionMethod)
    {
    }
#pragma warning restore CS0618

    /// <summary>Composes the portfolio through the Contracts-owned adjuster.</summary>
    public PaperTradingPortfolio(
        IReadOnlyList<AccountDefinition> accounts,
        Meridian.Ledger.Ledger? ledger,
        ILivePositionCorporateActionAdjuster? corporateActionAdjuster,
        PositionLotSelectionMethod lotSelectionMethod = PositionLotSelectionMethod.Fifo)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        if (accounts.Count == 0)
            throw new ArgumentException("At least one account must be supplied.", nameof(accounts));

        _ledger = ledger;
        _corporateActionAdjuster = corporateActionAdjuster;
        _lotSelectionMethod = lotSelectionMethod;

        foreach (var def in accounts)
        {
            if (def.InitialCash < 0)
                throw new ArgumentOutOfRangeException(nameof(accounts), $"Account '{def.AccountId}' initial cash must be non-negative.");

            _accounts[def.AccountId] = new AccountState(
                def.AccountId, def.DisplayName, def.Kind, def.InitialCash,
                def.MarginType, def.MarginModel, syncRoot: _lock);

            if (ledger is not null && def.InitialCash > 0)
            {
                ledger.PostLines(
                    DateTimeOffset.UtcNow,
                    $"Initial capital — {def.DisplayName}",
                    [
                        (LedgerAccounts.Cash, def.InitialCash, 0m),
                        (LedgerAccounts.CapitalAccount, 0m, def.InitialCash),
                    ]);
            }
        }
    }

    // ─── IPortfolioState (aggregate across all accounts) ────────────────────

    /// <inheritdoc />
    public decimal Cash
    {
        get { lock (_lock) { return _accounts.Values.Sum(static a => a.Cash); } }
    }

    /// <summary>Total margin borrowed (long positions) across all accounts (0 for all-cash portfolios).</summary>
    public decimal MarginBalance
    {
        get { lock (_lock) { return _accounts.Values.Sum(static a => a.MarginBalance); } }
    }

    /// <summary>Total short margin collateral held across all accounts.</summary>
    public decimal ShortMarginCollateral
    {
        get { lock (_lock) { return _accounts.Values.Sum(static a => a.ShortMarginCollateral); } }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses signed market values so short positions reduce portfolio value as the
    /// liability they are, and adds back short margin collateral held at the broker
    /// (deducted from cash at short entry but still owned by the trader).
    /// </remarks>
    public decimal PortfolioValue
    {
        get
        {
            lock (_lock)
            {
                return _accounts.Values.Sum(static a =>
                    a.Cash
                    + a.Positions.Values.Sum(static p => p.SignedMarketValue)
                    - a.MarginBalance
                    + a.ShortMarginCollateral);
            }
        }
    }

    /// <inheritdoc />
    public decimal UnrealisedPnl
    {
        get { lock (_lock) { return _accounts.Values.Sum(static a => a.Positions.Values.Sum(static p => p.UnrealisedPnl)); } }
    }

    /// <inheritdoc />
    public decimal RealisedPnl
    {
        get { lock (_lock) { return _accounts.Values.Sum(static a => a.RealisedPnl); } }
    }

    /// <inheritdoc />
    /// <remarks>Aggregates positions across all accounts; same-symbol positions are netted.</remarks>
    public IReadOnlyDictionary<string, IPosition> Positions
    {
        get
        {
            lock (_lock)
            {
                // Carry the unrounded quantity beside the whole-share one: position limits and
                // ownership attribution are decimal, and truncating here let a 0.9-share
                // holding read as 0 against a share cap.
                var netted = new Dictionary<string, (long qty, decimal exactQty, decimal costBasis, decimal unrealised, decimal realised, decimal multiplier)>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var account in _accounts.Values)
                {
                    foreach (var pos in account.Positions.Values)
                    {
                        if (netted.TryGetValue(pos.Symbol, out var existing))
                        {
                            netted[pos.Symbol] = (
                                existing.qty + (long)pos.Quantity,
                                existing.exactQty + pos.Quantity,
                                existing.costBasis, // keep first account's cost basis for simplicity
                                existing.unrealised + pos.UnrealisedPnl,
                                existing.realised + account.RealisedPnl,
                                existing.multiplier);
                        }
                        else
                        {
                            netted[pos.Symbol] = (
                                (long)pos.Quantity,
                                pos.Quantity,
                                pos.CostBasis,
                                pos.UnrealisedPnl,
                                account.RealisedPnl,
                                pos.EconomicMultiplier);
                        }
                    }
                }

                return netted.ToDictionary(
                    static kv => kv.Key,
                    static kv => (IPosition)new ExecutionPosition(kv.Key, kv.Value.qty, kv.Value.costBasis, kv.Value.unrealised, kv.Value.realised)
                    {
                        ExactQuantity = kv.Value.exactQty,
                        // The multiplier this view's UnrealizedPnl was computed with, so readers
                        // that recover a mark or value from quote × quantity × multiplier agree
                        // with the book (1 for shares and, for now, futures).
                        ContractMultiplier = kv.Value.multiplier
                    },
                    StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    // ─── IMultiAccountPortfolioState ────────────────────────────────────────

    /// <inheritdoc />
    public IReadOnlyList<IAccountPortfolio> Accounts
    {
        get { lock (_lock) { return _accounts.Values.Cast<IAccountPortfolio>().ToArray(); } }
    }

    /// <inheritdoc />
    public IAccountPortfolio? GetAccount(string accountId)
    {
        lock (_lock)
        {
            return _accounts.TryGetValue(accountId, out var acc) ? acc : null;
        }
    }

    /// <summary>
    /// Returns aggregate open positions across all accounts for legacy single-account callers.
    /// </summary>
    public IReadOnlyList<IPosition> GetPositions() => Positions.Values.ToArray();

    /// <inheritdoc />
    public MultiAccountPortfolioSnapshot GetAggregateSnapshot()
    {
        lock (_lock)
        {
            var snapshots = _accounts.Values
                .Select(static a => a.TakeSnapshot())
                .ToArray();

            return MultiAccountPortfolioSnapshot.FromAccounts(snapshots);
        }
    }

    /// <summary>
    /// Returns the current open lots for a symbol in the default account.
    /// </summary>
    public IReadOnlyList<PositionLotEntry> GetPositionLots(string symbol)
    {
        lock (_lock)
        {
            if (!_accounts.TryGetValue(DefaultAccountId, out var account) ||
                !account.Positions.TryGetValue(symbol, out var position))
            {
                return [];
            }

            return position.GetLots();
        }
    }

    // ─── Per-account snapshot convenience ───────────────────────────────────

    /// <summary>
    /// Returns a detailed snapshot for a single account.
    /// Returns <see langword="null"/> when the account does not exist.
    /// </summary>
    public ExecutionAccountDetailSnapshot? GetAccountSnapshot(string accountId)
    {
        lock (_lock)
        {
            return _accounts.TryGetValue(accountId, out var acc) ? acc.TakeSnapshot() : null;
        }
    }

    /// <summary>
    /// Returns detailed snapshots for all tracked accounts.
    /// </summary>
    public IReadOnlyList<ExecutionAccountDetailSnapshot> GetAllAccountSnapshots()
    {
        lock (_lock)
        {
            return _accounts.Values.Select(static a => a.TakeSnapshot()).ToArray();
        }
    }

    // ─── Margin maintenance / status ─────────────────────────────────────────

    /// <summary>
    /// Evaluates the current maintenance-margin health of one account given a set of
    /// current market prices, and returns a <see cref="MarginCallStatus"/> indicating
    /// whether a margin call is in effect.
    /// </summary>
    /// <param name="accountId">Account to evaluate.</param>
    /// <param name="prices">
    ///     Current market prices keyed by symbol (case-insensitive).
    ///     Every open position must have a price; margin is not evaluated on a partial feed.
    /// </param>
    /// <returns>
    ///     A <see cref="MarginCallStatus"/> for the account.
    ///     Returns <see cref="MarginCallStatus.NoMarginRequired"/> for cash accounts or
    ///     accounts with no open positions.
    /// </returns>
    /// <exception cref="MarginPriceUnavailableException">
    ///     An open position has no entry in <paramref name="prices"/>. Skipping it would
    ///     understate the requirement, so the check fails closed instead.
    /// </exception>
    public MarginCallStatus CheckMarginStatus(
        string accountId,
        IReadOnlyDictionary<string, decimal> prices)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(prices);

        lock (_lock)
        {
            if (!_accounts.TryGetValue(accountId, out var account))
                return MarginCallStatus.NoMarginRequired(accountId);

            if (account.MarginModel is null || account.Positions.Count == 0)
                return MarginCallStatus.NoMarginRequired(accountId);

            var marginBorrowed = account.Positions.Values.Sum(static p => p.MarginBorrowed);
            var effectiveCash = account.Cash - marginBorrowed;
            var executionPositions = account.Positions.ToDictionary(
                kv => kv.Key,
                // The margin models size a position as quantity × ContractMultiplier. Cash, loan
                // and collateral here move at the position's money scale (the contract multiplier
                // for premium contracts, 1 for futures and legacy 1x positions), so the margin view
                // carries that scale; otherwise a 100x option loan would be measured against a 1x
                // holding, or a 1x futures holding against a 50x requirement.
                kv => kv.Value.ToExecutionPosition() with { ContractMultiplier = kv.Value.EconomicMultiplier },
                StringComparer.OrdinalIgnoreCase);

            // Computed first so a missing price fails closed before any per-position result.
            var portfolioReq = account.MarginModel.CalculatePortfolioRequirement(
                executionPositions,
                prices,
                effectiveCash);

            // Net equity at the SUPPLIED prices (long MV − short MV + cash), the same snapshot the
            // portfolio requirement used. The account's stored marks may be stale, and a
            // per-position view priced differently could miss a call the portfolio view reports.
            var equity = effectiveCash + portfolioReq.NotionalValue;

            var posRequirements = new List<MarginRequirement>();
            foreach (var (symbol, pos) in executionPositions)
            {
                // Open positions are guaranteed a price by the portfolio check above; only a
                // net-flat shared-book entry can be absent here, and it carries no requirement.
                if (!prices.TryGetValue(symbol, out var px))
                    continue;

                posRequirements.Add(account.MarginModel.CalculateForPosition(pos, px, equity));
            }

            var isCall = portfolioReq.IsMarginCall;
            var deficiency = isCall ? Math.Abs(portfolioReq.ExcessLiquidity) : 0m;

            return new MarginCallStatus(accountId, portfolioReq, posRequirements, isCall, deficiency);
        }
    }


    /// <summary>
    /// Returns the portfolio-level <see cref="MarginRequirement"/> for the specified account.
    /// Returns a zeroed requirement for cash accounts or accounts with no margin model.
    /// </summary>
    public MarginRequirement GetMarginRequirement(
        string accountId,
        IReadOnlyDictionary<string, decimal> prices)
        => CheckMarginStatus(accountId, prices).PortfolioRequirement;

    /// <summary>
    /// Accrues interest on any outstanding margin loans in all margin accounts.
    /// </summary>
    /// <remarks>
    /// Call this once per interest period (e.g. daily).  The charge is paid from the
    /// account's <c>Cash</c> balance and posted as margin interest expense; the loan
    /// principal (<c>MarginBorrowed</c>) is unchanged. Equity therefore falls by exactly
    /// the interest, once — growing the loan as well would charge it a second time, both
    /// in <see cref="PortfolioValue"/> and again when the sale repays the inflated loan.
    /// This matches the backtest engine, which also settles margin interest from cash.
    /// </remarks>
    /// <param name="annualRate">Annual interest rate expressed as a decimal (e.g. 0.065 = 6.5 %).</param>
    /// <param name="days">Number of days to accrue interest for. Defaults to 1 (daily).</param>
    /// <param name="asOf">Timestamp for the ledger posting. Defaults to the current UTC time.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="annualRate"/> is negative or <paramref name="days"/> is less than 1.
    /// </exception>
    public void AccrueMarginInterest(decimal annualRate, int days = 1, DateTimeOffset? asOf = null)
    {
        if (annualRate < 0m)
            throw new ArgumentOutOfRangeException(nameof(annualRate), "Annual rate must be non-negative.");
        if (days < 1)
            throw new ArgumentOutOfRangeException(nameof(days), "Days must be at least 1.");

        if (annualRate == 0m)
            return;

        var dailyRate = annualRate / 365m;
        var periodRate = dailyRate * days;

        var ts = asOf ?? DateTimeOffset.UtcNow;

        lock (_lock)
        {
            foreach (var account in _accounts.Values)
            {
                if (account.MarginModel is null)
                    continue; // cash account

                var accountInterest = 0m;
                foreach (var pos in account.Positions.Values)
                {
                    if (pos.MarginBorrowed <= 0m)
                        continue;

                    accountInterest += pos.MarginBorrowed * periodRate;
                }

                if (accountInterest == 0m)
                    continue;

                // Paid from cash once; the loan principal stays where it is.
                account.Cash -= accountInterest;

                _ledger?.PostLines(ts, $"Margin interest — {account.AccountId}",
                [
                    (LedgerAccounts.MarginInterestExpense, accountInterest, 0m),
                    (LedgerAccounts.Cash, 0m, accountInterest),
                ]);
            }
        }
    }

    // ─── Fill application ────────────────────────────────────────────────────

    /// <summary>
    /// Applies a fill to the <see cref="DefaultAccountId"/> account (single-account path).
    /// </summary>
    public void ApplyFill(ExecutionReport report) => ApplyFill(DefaultAccountId, report);

    /// <summary>
    /// Applies a fill to the default account while recording the fund account that owns it
    /// and the contract multiplier it carries. The attribution lets a shared execution book be
    /// read per fund and keeps a derivative position's exposure from being measured as if each
    /// contract were one share. For options the multiplier also scales the money: cash, ledger,
    /// lot basis, market value and P&amp;L move at quote × quantity × multiplier, while the stored
    /// cost basis stays a per-unit quote. Futures stay at 1× pending a variation-margin model
    /// (see <c>ResolveEconomicMultiplier</c>).
    /// </summary>
    public void ApplyFill(
        ExecutionReport report,
        string? ownerAccountId,
        decimal contractMultiplier = 1m,
        bool usesFaceValuePercentageOfPar = false)
        => ApplyFill(DefaultAccountId, report, ownerAccountId, contractMultiplier, usesFaceValuePercentageOfPar);

    /// <summary>
    /// Applies a fill to a specific <paramref name="accountId"/>.
    /// When the account does not exist it is ignored (no-op).
    /// </summary>
    public void ApplyFill(string accountId, ExecutionReport report)
        => ApplyFill(accountId, report, ownerAccountId: null);

    /// <inheritdoc cref="ApplyFill(string, ExecutionReport)"/>
    /// <remarks>
    /// When <paramref name="usesFaceValuePercentageOfPar"/> is set, the report's quantity is
    /// face value and its clean price is quoted as a percentage of par. The price is converted
    /// to dollars per unit of face before any cash, cost-basis, or ledger math runs — 100,000
    /// face at 101.25 moves $101,250, not $10,125,000 — so the position's stored cost basis and
    /// market price are held in per-unit dollar terms for these instruments.
    /// </remarks>
    public void ApplyFill(
        string accountId,
        ExecutionReport report,
        string? ownerAccountId,
        decimal contractMultiplier = 1m,
        bool usesFaceValuePercentageOfPar = false)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.ReportType is not (ExecutionReportType.Fill or ExecutionReportType.PartialFill))
            return;

        if (report.FillPrice is null || report.FilledQuantity <= 0)
            return;

        var signedQty = report.Side == OrderSide.Buy
            ? report.FilledQuantity
            : -report.FilledQuantity;

        // Explicit transaction costs (commission, regulatory fees, and modeled slippage)
        // all charge cash and book to commission expense so paper economics reflect them.
        var explicitCosts = (report.Commission ?? 0m) + (report.Fees ?? 0m) + (report.SlippageCost ?? 0m);

        // The report's own stamp is authoritative alongside the caller's parameter: the OMS
        // stamps live increments, and durable session records replay those same reports after
        // a restart, when no caller is left to supply the classification.
        var faceValueSizing = usesFaceValuePercentageOfPar || report.UsesFaceValuePercentageOfPar;

        var multiplier = report.LotMultiplier ?? contractMultiplier;
        if (multiplier <= 0m)
            throw new ArgumentOutOfRangeException(nameof(contractMultiplier), "Lot multiplier must be positive.");
        var assetClassResolved = SecurityAssetClassCatalog.TryResolveLabel(report.AssetClass, out var descriptor);
        var lotUnit = report.LotUnitKind
            ?? (faceValueSizing ? SecurityLotUnitKind.FaceValue
                : assetClassResolved ? descriptor!.LotUnit
                : report.OptionContract is not null || multiplier != 1m ? SecurityLotUnitKind.Contracts
                : SecurityLotUnitKind.Shares); // Legacy symbol-only reports explicitly assume shares.
        if (!Enum.IsDefined(lotUnit))
            throw new ArgumentOutOfRangeException(nameof(report), "Lot denomination must be a defined unit.");

        // A stamped fill replays at exactly the scale it was booked at; an unstamped live fill
        // resolves it now (and the OMS stamps the increment before it is persisted).
        var economicMultiplier = report.BookedMoneyMultiplier
            ?? ResolveEconomicMultiplier(report, multiplier, faceValueSizing, assetClassResolved ? descriptor : null);
        if (economicMultiplier <= 0m)
            throw new ArgumentOutOfRangeException(nameof(report), "Booked money multiplier must be positive.");

        // A percentage of par is a fraction of par: convert once here so every downstream
        // lot, cost-basis, cash, and ledger computation shares one per-unit dollar price.
        var fillPrice = faceValueSizing
            ? report.FillPrice.Value / 100m
            : report.FillPrice.Value;

        lock (_lock)
        {
            if (!_accounts.TryGetValue(accountId, out var account))
                return;

            ApplyFillToAccount(account, report.Symbol, signedQty, fillPrice,
                explicitCosts, report.Timestamp, report.OrderId,
                ownerAccountId, multiplier, faceValueSizing, lotUnit, economicMultiplier);
        }
    }

    /// <summary>
    /// Applies a fill read back from a durable paper-session record. A record stamped with
    /// <see cref="ExecutionReport.BookedMoneyMultiplier"/> replays at exactly that scale. A record
    /// without the stamp was written before this book scaled contract premiums and booked its cash
    /// at 1×, so it replays at 1× rather than being silently reinterpreted.
    /// </summary>
    internal void ApplyRecordedFill(ExecutionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ApplyFill(report.BookedMoneyMultiplier is null ? report with { BookedMoneyMultiplier = 1m } : report);
    }

    /// <summary>
    /// The money multiplier an open position in the default account was booked at, or
    /// <see langword="null"/> when nothing is held in <paramref name="symbol"/>. A fill that
    /// adds to or closes that position must use the same scale: a session restored from records
    /// written before premiums were scaled holds 1x positions, and continuing them at 1x until
    /// they are flat keeps every lot, cash movement and replay consistent. A flat entry kept only
    /// for its fund attribution holds nothing, so it has no scale to continue: the next fill
    /// opens at its own scale (see <see cref="PaperPosition.ResetMoneyScale"/>).
    /// </summary>
    internal decimal? GetOpenMoneyMultiplier(string symbol)
    {
        lock (_lock)
        {
            return _accounts.TryGetValue(DefaultAccountId, out var account)
                && account.Positions.TryGetValue(symbol, out var pos)
                && pos.Quantity != 0m
                    ? pos.EconomicMultiplier
                    : null;
        }
    }

    /// <summary>
    /// The money multiplier <see cref="ApplyFill(string, ExecutionReport, string?, decimal, bool)"/>
    /// books a fill at, for callers that must stamp or mirror it (the OMS stamps it on the fill
    /// increment before persisting it and carries it into the accounting event).
    /// </summary>
    internal static decimal ResolveMoneyMultiplier(
        ExecutionReport report,
        decimal contractMultiplier,
        bool usesFaceValuePercentageOfPar)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.BookedMoneyMultiplier is { } booked)
            return booked;

        var faceValueSizing = usesFaceValuePercentageOfPar || report.UsesFaceValuePercentageOfPar;
        var multiplier = report.LotMultiplier ?? contractMultiplier;
        if (multiplier <= 0m)
            throw new ArgumentOutOfRangeException(nameof(contractMultiplier), "Lot multiplier must be positive.");
        var descriptor = SecurityAssetClassCatalog.TryResolveLabel(report.AssetClass, out var resolved) ? resolved : null;
        return ResolveEconomicMultiplier(report, multiplier, faceValueSizing, descriptor);
    }

    /// <summary>
    /// The multiplier that turns a fill's quantity × quote into money in this book. Listed
    /// premium contracts — options and warrants, the classes whose catalog descriptor declares
    /// <see cref="SecurityAssetClassDescriptor.UsesOccOptionSymbols"/> — pay their premium in full,
    /// so one $2.50 option on a 100 multiplier moves $250, the same figure
    /// <c>OrderNotionalResolver</c> sizes the order at.
    /// </summary>
    /// <remarks>
    /// Face-value fills are already converted to dollars per unit of face, so they stay at 1.
    /// Futures also stay at 1 on purpose: no notional changes hands, and debiting the full
    /// contract value would be wrong. They need a variation-margin cash model (see
    /// <c>FuturePosition.SettleDaily</c>), which this book does not have yet. Other resolved
    /// asset classes keep the 1× behaviour they had. An unresolved report that carries an option
    /// identity or a non-unit multiplier is the order-management option path: the OMS only
    /// records a multiplier above 1 from an option contract.
    /// </remarks>
    private static decimal ResolveEconomicMultiplier(
        ExecutionReport report,
        decimal multiplier,
        bool faceValueSizing,
        SecurityAssetClassDescriptor? descriptor)
    {
        if (faceValueSizing)
            return 1m;

        if (descriptor is not null)
        {
            // Read the catalog fact rather than the class name (the asset-class comparison
            // ratchet forbids spelling classes outside the catalog).
            return descriptor.UsesOccOptionSymbols ? multiplier : 1m;
        }

        return report.OptionContract is not null || multiplier != 1m ? multiplier : 1m;
    }

    /// <summary>
    /// Updates the last-known market price for <paramref name="symbol"/> across all accounts.
    /// A position opened by face-value fills stores its cost basis and market price in dollars
    /// per unit of face, so a quote still arriving as a percentage of par is normalized the
    /// same way the fill price was — otherwise a 100,000-face position marked at 102.25 would
    /// report ~$10.2M of market value instead of ~$102K.
    /// </summary>
    public void UpdateMarketPrice(string symbol, decimal price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        lock (_lock)
        {
            foreach (var account in _accounts.Values)
            {
                if (account.Positions.TryGetValue(symbol, out var pos))
                    pos.MarketPrice = pos.UsesFaceValuePricing ? price / 100m : price;
            }
        }
    }

    /// <summary>Updates the last-known market price for <paramref name="symbol"/> in a specific account.</summary>
    public void UpdateMarketPrice(string accountId, string symbol, decimal price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        lock (_lock)
        {
            if (_accounts.TryGetValue(accountId, out var account)
                && account.Positions.TryGetValue(symbol, out var pos))
            {
                pos.MarketPrice = pos.UsesFaceValuePricing ? price / 100m : price;
            }
        }
    }

    /// <summary>
    /// Read-only view of the double-entry ledger for this session.
    /// Returns <see langword="null"/> when no ledger was supplied at construction time.
    /// </summary>
    public IReadOnlyLedger? Ledger => _ledger;

    // ─── Corporate-action adjustment ────────────────────────────────────────

    /// <summary>
    /// Applies any corporate-action adjustments (splits, dividends) to an existing open position
    /// in the default account.  No-op when no adjuster was supplied.
    /// </summary>
    public Task ApplyCorporateActionsAsync(
        string symbol,
        DateTimeOffset positionOpenedAt,
        CancellationToken ct = default)
        => ApplyCorporateActionsAsync(DefaultAccountId, symbol, positionOpenedAt, ct);

    /// <summary>
    /// Applies corporate-action adjustments to a position in a specific <paramref name="accountId"/>.
    /// </summary>
    public async Task ApplyCorporateActionsAsync(
        string accountId,
        string symbol,
        DateTimeOffset positionOpenedAt,
        CancellationToken ct = default)
    {
        if (_corporateActionAdjuster is null || string.IsNullOrWhiteSpace(symbol))
            return;

        PaperPosition? pos;
        PaperPosition originalPosition;
        decimal originalQuantity;
        decimal originalCostBasis;
        decimal originalTotalCostBasis;
        long originalLotVersion;

        lock (_lock)
        {
            if (!_accounts.TryGetValue(accountId, out var acc)
                || !acc.Positions.TryGetValue(symbol, out pos))
            {
                return;
            }

            originalQuantity = pos.Quantity;
            originalCostBasis = pos.CostBasis;
            originalTotalCostBasis = pos.OpenCostBasis;
            originalLotVersion = pos.LotVersion;
            originalPosition = pos;
        }

        var adjustment = await _corporateActionAdjuster
            .AdjustPositionAsync(symbol, originalQuantity, originalCostBasis, originalTotalCostBasis, positionOpenedAt, ct)
            .ConfigureAwait(false);

        if (adjustment.ActionCount == 0)
            return;

        lock (_lock)
        {
            if (!_accounts.TryGetValue(accountId, out var acc)
                || !acc.Positions.TryGetValue(symbol, out pos))
            {
                return;
            }

            if (ReferenceEquals(pos, originalPosition) && pos.LotVersion == originalLotVersion
                && pos.Quantity == originalQuantity && pos.CostBasis == originalCostBasis)
            {
                // Rescale the lot book by the same factors so lots stay consistent with the
                // aggregate; otherwise the next lot-consuming fill falls into the shortfall
                // fallback and mis-states realized P&L (e.g. selling through a post-split
                // position priced at pre-split lot costs).
                var quantityFactor = originalQuantity == 0m
                    ? 1m
                    : adjustment.AdjustedQuantity / originalQuantity;
                var priceFactor = originalCostBasis == 0m
                    ? 1m
                    : adjustment.AdjustedCostBasis / originalCostBasis;
                pos.RescaleLots(quantityFactor, priceFactor, adjustment.AdjustedQuantity, adjustment.AdjustedTotalCostBasis);
                pos.Quantity = adjustment.AdjustedQuantity;
                pos.CostBasis = adjustment.AdjustedTotalCostBasis.HasValue && pos.Quantity != 0m
                    ? pos.OpenCostBasis / (Math.Abs(pos.Quantity) * pos.EconomicMultiplier)
                    : adjustment.AdjustedCostBasis;
                // Ownership rides on quantity, so a split that doubles the position must
                // double each fund's share too. Leaving the tallies behind would surface
                // the difference as an unattributable residual a scoped operator cannot see.
                pos.RescaleOwnerQuantities(quantityFactor);
            }
        }
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private void ApplyFillToAccount(
        AccountState account,
        string symbol,
        decimal signedQty,
        decimal price,
        decimal commission,
        DateTimeOffset ts,
        string orderId,
        string? ownerAccountId = null,
        decimal contractMultiplier = 1m,
        bool usesFaceValuePricing = false,
        SecurityLotUnitKind lotUnit = SecurityLotUnitKind.Shares,
        decimal economicMultiplier = 1m)
    {
        if (!account.Positions.TryGetValue(symbol, out var pos))
        {
            pos = new PaperPosition(symbol, price) { LotUnit = lotUnit, EconomicMultiplier = economicMultiplier };
            // Seed the exact attribution multiplier: AttributeFill only ever raises it, so a valid
            // multiplier below 1 would otherwise stay at the default and fail the equality check
            // on the very next fill.
            pos.InitializeContractMultiplier(contractMultiplier);
            account.Positions[symbol] = pos;
        }
        else if (pos.LotUnit != lotUnit)
        {
            throw new InvalidOperationException($"Fill lot denomination {lotUnit} does not match the open {pos.LotUnit} position for '{symbol}'.");
        }
        else if (pos.EconomicMultiplier != economicMultiplier)
        {
            if (pos.IsFlatWithoutLots)
            {
                // Nothing is held, only fund attribution remains (for example an orphaned tally
                // after a restored legacy 1x position was closed). The new holding opens at its
                // own scale; the owner tallies are kept so real offsetting exposure survives.
                pos.ResetMoneyScale(economicMultiplier);
            }
            else
            {
                // One book holds one money scale: blending 100x and 10x fills would misprice every lot.
                throw new InvalidOperationException($"Fill multiplier {economicMultiplier} does not match the open position's multiplier {pos.EconomicMultiplier} for '{symbol}'.");
            }
        }
        else if (pos.ContractMultiplier != contractMultiplier)
        {
            // Exposure and fund attribution read the position's single contract multiplier, so a
            // fill carrying a different one cannot be folded into the same lot book.
            throw new InvalidOperationException(
                $"Fill contract multiplier {contractMultiplier} does not match the open position multiplier {pos.ContractMultiplier} for '{symbol}'.");
        }

        // Once a face-value fill touches the position, its cost basis and market price are
        // held in dollars per unit of face — so later market quotes, still arriving as a
        // percentage of par, must be normalized the same way (see UpdateMarketPrice).
        if (usesFaceValuePricing)
        {
            pos.UsesFaceValuePricing = true;
        }

        // Attribution runs before the position math so it records the fill regardless of
        // which branch below applies it, and never depends on the resulting quantity.
        pos.AttributeFill(ownerAccountId, signedQty, contractMultiplier);

        // A fill that crosses through zero closes the existing side first, then opens
        // the residual on the opposite side — the residual must not be dropped.
        // Commission is charged once, on the closing leg.
        if (signedQty > 0 && pos.Quantity < 0)
        {
            var coverQty = Math.Min(signedQty, -pos.Quantity);
            ApplyCoverShort(account, pos, symbol, coverQty, price, commission, ts);

            var residualQty = signedQty - coverQty;
            if (residualQty > 0m)
                ApplyBuy(account, pos, symbol, residualQty, price, 0m, ts);
        }
        else if (signedQty > 0)
        {
            ApplyBuy(account, pos, symbol, signedQty, price, commission, ts);
        }
        else if (signedQty < 0 && pos.Quantity > 0)
        {
            var closeQty = Math.Min(-signedQty, pos.Quantity);
            ApplySellLong(account, pos, symbol, closeQty, price, commission, ts);

            var residualQty = -signedQty - closeQty;
            if (residualQty > 0m)
                ApplyShortSell(account, pos, symbol, residualQty, price, 0m, ts);
        }
        else if (signedQty < 0)
        {
            ApplyShortSell(account, pos, symbol, -signedQty, price, commission, ts);
        }

        if (commission > 0)
            PostCommissionEntry(symbol, commission, ts);

        pos.MarketPrice = price;

        // A net-flat SHARED book is not a flat book. Fund A long 100 against Fund B short
        // 100 nets to zero here, but both funds still hold real, opposing exposure that
        // gross limits must see. Only drop the position when nobody is left holding it.
        if (pos.Quantity == 0m && !pos.HasOwnerExposure)
            account.Positions.Remove(symbol);
    }

    private void ApplyBuy(
        AccountState account,
        PaperPosition pos,
        string symbol,
        decimal qty,
        decimal price,
        decimal commission,
        DateTimeOffset ts)
    {
        var notional = pos.Money(qty, price);
        pos.AddLot(qty, price, ts);
        var newQty = pos.Quantity + qty;
        pos.CostBasis = newQty == 0m ? 0m : pos.OpenCostBasis / (newQty * pos.EconomicMultiplier);
        pos.Quantity = newQty;

        if (account.MarginModel is RegTMarginModel regt)
        {
            // Reg T: the trader funds only the initial margin (50 %); the broker loans the rest.
            var cashRequired = notional * regt.LongInitialRate;
            var borrowed = notional - cashRequired;
            account.Cash -= cashRequired + commission;
            pos.MarginBorrowed += borrowed;
        }
        else if (account.MarginModel is PortfolioMarginModel)
        {
            // Portfolio margin: full notional deducted from cash (stress-test margining
            // is evaluated separately via CheckMarginStatus).
            account.Cash -= notional + commission;
        }
        else
        {
            // Cash account: full notional is deducted from cash.
            account.Cash -= notional + commission;
        }

        if (_ledger is not null)
        {
            if (account.MarginModel is RegTMarginModel regtForLedger)
            {
                var cashRequired = notional * regtForLedger.LongInitialRate;
                var borrowed = notional - cashRequired;
                _ledger.PostLines(ts, $"Buy {qty} {symbol} @ {price:F4}",
                [
                    (LedgerAccounts.Securities(symbol), notional, 0m),
                    (LedgerAccounts.Cash, 0m, cashRequired),
                    (LedgerAccounts.MarginLoanPayable, 0m, borrowed),
                ]);
            }
            else
            {
                _ledger.PostLines(ts, $"Buy {qty} {symbol} @ {price:F4}",
                [
                    (LedgerAccounts.Securities(symbol), notional, 0m),
                    (LedgerAccounts.Cash, 0m, notional),
                ]);
            }
        }
    }

    private void ApplyCoverShort(
        AccountState account,
        PaperPosition pos,
        string symbol,
        decimal qty,
        decimal price,
        decimal commission,
        DateTimeOffset ts)
    {
        var coverQty = Math.Min(qty, Math.Abs(pos.Quantity));
        var proceedsRemoved = pos.ConsumeLots(coverQty, _lotSelectionMethod, isCoveringShort: true);
        var coverCost = pos.Money(coverQty, price);
        var realised = proceedsRemoved - coverCost;

        // Release proportional short margin collateral back to cash.
        var marginReleased = 0m;
        if (pos.MarginBorrowed < 0m && pos.Quantity < 0m)
        {
            var closingRatio = coverQty / Math.Abs(pos.Quantity);
            marginReleased = Math.Abs(pos.MarginBorrowed) * closingRatio;
            pos.MarginBorrowed += marginReleased; // reduce the held collateral (stored as negative)
        }

        pos.Quantity += coverQty;
        if (pos.Quantity == 0m)
            pos.CostBasis = 0m;

        // Cash: buy-to-cover cost minus the extra margin collateral released.
        account.Cash -= coverCost + commission;
        account.Cash += marginReleased;
        account.RealisedPnl += realised;

        if (_ledger is not null)
            PostCoverShortEntry(symbol, coverQty, price, coverCost, proceedsRemoved, realised, ts);
    }

    private void ApplySellLong(
        AccountState account,
        PaperPosition pos,
        string symbol,
        decimal qty,
        decimal price,
        decimal commission,
        DateTimeOffset ts)
    {
        var closeQty = Math.Min(qty, pos.Quantity);
        var costBasisRemoved = pos.ConsumeLots(closeQty, _lotSelectionMethod, isCoveringShort: false);
        var proceeds = pos.Money(closeQty, price);
        var realised = proceeds - costBasisRemoved;

        // For margin positions, proportionally repay the broker loan before crediting cash.
        var loanRepaid = 0m;
        if (pos.MarginBorrowed > 0m && pos.Quantity > 0m)
        {
            var closingRatio = closeQty / pos.Quantity;
            loanRepaid = pos.MarginBorrowed * closingRatio;
            pos.MarginBorrowed -= loanRepaid;
        }

        pos.Quantity -= closeQty;
        if (pos.Quantity == 0m)
            pos.CostBasis = 0m;

        // The trader receives proceeds minus the loan repayment.
        account.Cash += proceeds - loanRepaid - commission;
        account.RealisedPnl += realised;

        if (_ledger is not null)
            PostSellLongEntry(symbol, closeQty, price, proceeds, costBasisRemoved, realised, loanRepaid, ts);
    }

    private void ApplyShortSell(
        AccountState account,
        PaperPosition pos,
        string symbol,
        decimal qty,
        decimal price,
        decimal commission,
        DateTimeOffset ts)
    {
        var proceeds = pos.Money(qty, price);
        pos.AddLot(-qty, price, ts);
        var newQty = pos.Quantity - qty;
        pos.CostBasis = newQty == 0m
            ? 0m
            : pos.OpenCostBasis / (Math.Abs(newQty) * pos.EconomicMultiplier);
        pos.Quantity = newQty;

        if (account.MarginModel is RegTMarginModel regt)
        {
            // Reg T: broker retains the short proceeds; trader must post the additional
            // margin (ShortInitialRate - 1) × notional on top.
            // e.g. 150 % total collateral: broker keeps 100 % proceeds + trader posts 50 %.
            var additionalMargin = proceeds * (regt.ShortInitialRate - 1m);
            account.Cash -= additionalMargin + commission;

            // Track total collateral held as a negative MarginBorrowed (broker holds it).
            pos.MarginBorrowed -= proceeds * regt.ShortInitialRate;
        }
        else
        {
            // Cash account: proceeds credited immediately; short position is funded by proceeds.
            account.Cash += proceeds - commission;
        }

        _ledger?.PostLines(ts, $"Short sell {qty} {symbol} @ {price:F4}",
        [
            (LedgerAccounts.Cash, proceeds, 0m),
            (LedgerAccounts.ShortSecuritiesPayable(symbol), 0m, proceeds),
        ]);
    }

    private void PostSellLongEntry(string symbol, decimal closeQty, decimal price, decimal proceeds,
        decimal costBasisRemoved, decimal realised, decimal loanRepaid, DateTimeOffset ts)
    {
        var cashProceeds = proceeds - loanRepaid;
        var description = $"Sell {closeQty} {symbol} @ {price:F4}";
        var lines = new List<(LedgerAccount Account, decimal Debit, decimal Credit)>();
        AddNonZeroLine(lines, LedgerAccounts.Cash, cashProceeds, 0m);
        AddNonZeroLine(lines, LedgerAccounts.MarginLoanPayable, loanRepaid, 0m);

        if (realised > 0)
        {
            AddNonZeroLine(lines, LedgerAccounts.Securities(symbol), 0m, costBasisRemoved);
            AddNonZeroLine(lines, LedgerAccounts.RealizedGain, 0m, realised);
        }
        else if (realised < 0)
        {
            AddNonZeroLine(lines, LedgerAccounts.RealizedLoss, Math.Abs(realised), 0m);
            AddNonZeroLine(lines, LedgerAccounts.Securities(symbol), 0m, costBasisRemoved);
        }
        else
        {
            AddNonZeroLine(lines, LedgerAccounts.Securities(symbol), 0m, costBasisRemoved);
        }

        _ledger!.PostLines(ts, description, lines);
    }

    private static void AddNonZeroLine(
        List<(LedgerAccount Account, decimal Debit, decimal Credit)> lines,
        LedgerAccount account,
        decimal debit,
        decimal credit)
    {
        if (debit != 0m || credit != 0m)
            lines.Add((account, debit, credit));
    }

    private void PostCoverShortEntry(string symbol, decimal coverQty, decimal price, decimal coverCost,
        decimal proceedsRemoved, decimal realised, DateTimeOffset ts)
    {
        var description = $"Cover {coverQty} {symbol} @ {price:F4}";
        if (realised > 0m)
            _ledger!.PostLines(ts, description,
            [
                (LedgerAccounts.ShortSecuritiesPayable(symbol), proceedsRemoved, 0m),
                (LedgerAccounts.Cash, 0m, coverCost),
                (LedgerAccounts.RealizedGain, 0m, realised),
            ]);
        else if (realised < 0m)
            _ledger!.PostLines(ts, description,
            [
                (LedgerAccounts.ShortSecuritiesPayable(symbol), proceedsRemoved, 0m),
                (LedgerAccounts.RealizedLoss, Math.Abs(realised), 0m),
                (LedgerAccounts.Cash, 0m, coverCost),
            ]);
        else
            _ledger!.PostLines(ts, description,
            [
                (LedgerAccounts.ShortSecuritiesPayable(symbol), proceedsRemoved, 0m),
                (LedgerAccounts.Cash, 0m, coverCost),
            ]);
    }

    private void PostCommissionEntry(string symbol, decimal commission, DateTimeOffset ts)
    {
        _ledger?.PostLines(ts, $"Commission — {symbol}",
        [
            (LedgerAccounts.CommissionExpense, commission, 0m),
            (LedgerAccounts.Cash, 0m, commission),
        ]);
    }
}

// ─── Supporting types ───────────────────────────────────────────────────────

/// <summary>
/// Defines one account within a multi-account paper trading session.
/// </summary>
/// <param name="AccountId">Unique account identifier.</param>
/// <param name="DisplayName">Human-readable name.</param>
/// <param name="Kind">Brokerage or Bank.</param>
/// <param name="InitialCash">Opening cash balance.</param>
/// <param name="MarginType">
///   Margin regime for this account.
///   Defaults to <see cref="MarginAccountType.Cash"/> (no borrowing).
/// </param>
/// <param name="MarginModel">
///   Optional explicit margin model. When <see langword="null"/> a default model is
///   created automatically for <see cref="MarginAccountType.RegT"/> and
///   <see cref="MarginAccountType.PortfolioMargin"/> accounts.
/// </param>
public sealed record AccountDefinition(
    string AccountId,
    string DisplayName,
    AccountKind Kind,
    decimal InitialCash,
    MarginAccountType MarginType = MarginAccountType.Cash,
    IMarginModel? MarginModel = null);

/// <summary>
/// Mutable per-account state maintained by <see cref="PaperTradingPortfolio"/>.
/// Also implements <see cref="IAccountPortfolio"/> for the read-only public surface.
/// </summary>
/// <summary>
/// Mutable per-account state maintained by <see cref="PaperTradingPortfolio"/>.
/// Also implements <see cref="IAccountPortfolio"/> for the read-only public surface.
/// </summary>
internal sealed class AccountState : IAccountPortfolio
{
    public string AccountId { get; }
    public string DisplayName { get; }
    public AccountKind Kind { get; }
    public decimal Cash { get; set; }
    public decimal RealisedPnl { get; set; }
    public MarginAccountType MarginType { get; }

    /// <summary>Active margin model; <see langword="null"/> for cash accounts.</summary>
    public IMarginModel? MarginModel { get; }

    public Dictionary<string, PaperPosition> Positions { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The owning portfolio's lock. Read-only projections off this account enumerate the
    /// live position dictionary, and a fill mutating it mid-enumeration throws. Since the
    /// pre-trade risk gate reads these projections on every submission, that exception
    /// surfaces as a server error refusing live order flow.
    /// </summary>
    private readonly Lock _syncRoot;

    public AccountState(
        string accountId,
        string displayName,
        AccountKind kind,
        decimal cash,
        MarginAccountType marginType = MarginAccountType.Cash,
        IMarginModel? marginModel = null,
        Lock? syncRoot = null)
    {
        _syncRoot = syncRoot ?? new Lock();
        AccountId = accountId;
        DisplayName = displayName;
        Kind = kind;
        Cash = cash;
        MarginType = marginType;
        MarginModel = marginModel ?? marginType switch
        {
            MarginAccountType.RegT => new RegTMarginModel(),
            MarginAccountType.PortfolioMargin => new PortfolioMarginModel(),
            _ => null,
        };
    }

    // IAccountPortfolio explicit implementation (read-only projection)
    IReadOnlyDictionary<string, IPosition> IAccountPortfolio.Positions
    {
        get
        {
            lock (_syncRoot)
            {
                return Positions.ToDictionary(
                    static kv => kv.Key,
                    static kv => (IPosition)kv.Value.ToExecutionPosition(),
                    StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// Total amount borrowed from the broker to fund long positions.
    /// Zero for cash accounts and for accounts with no margin longs.
    /// </summary>
    public decimal MarginBalance => Positions.Values
        .Where(static p => p.MarginBorrowed > 0m)
        .Sum(static p => p.MarginBorrowed);

    /// <summary>
    /// Total collateral held by the broker against short positions.
    /// Zero for cash accounts and accounts with no open shorts.
    /// </summary>
    public decimal ShortMarginCollateral => Positions.Values
        .Where(static p => p.MarginBorrowed < 0m)
        .Sum(static p => Math.Abs(p.MarginBorrowed));

    public decimal UnrealisedPnl => Positions.Values.Sum(static p => p.UnrealisedPnl);

    public decimal LongMarketValue => Positions.Values
        .Where(static p => p.Quantity > 0)
        .Sum(static p => p.MarketValue);

    public decimal ShortMarketValue => Positions.Values
        .Where(static p => p.Quantity < 0)
        .Sum(static p => p.MarketValue);

    /// <summary>
    /// Available buying power based on the margin regime.
    /// <list type="bullet">
    ///   <item>Cash account: equals <see cref="Cash"/>.</item>
    ///   <item>Reg T: up to 2× cash equity (50 % initial margin rate).</item>
    ///   <item>Portfolio margin: model-specific; defaults to <see cref="Cash"/>.</item>
    /// </list>
    /// </summary>
    public decimal BuyingPower =>
        MarginModel is RegTMarginModel regt && regt.LongInitialRate > 0m
            ? Cash / regt.LongInitialRate
            : Cash;

    public ExecutionAccountDetailSnapshot TakeSnapshot()
    {
        var positionList = Positions.Values
            .Select(static p => p.ToExecutionPosition())
            .ToArray();

        var longMv = LongMarketValue;
        var shortMv = ShortMarketValue;

        return new ExecutionAccountDetailSnapshot(
            AccountId: AccountId,
            DisplayName: DisplayName,
            Kind: Kind,
            Cash: Cash,
            MarginBalance: MarginBalance,
            LongMarketValue: longMv,
            ShortMarketValue: shortMv,
            GrossExposure: longMv + shortMv,
            NetExposure: longMv - shortMv,
            UnrealisedPnl: UnrealisedPnl,
            RealisedPnl: RealisedPnl,
            Positions: positionList,
            AsOf: DateTimeOffset.UtcNow,
            MarginType: MarginType,
            BuyingPower: BuyingPower,
            ShortMarginCollateral: ShortMarginCollateral);
    }
}

/// <summary>
/// Mutable position state used internally by <see cref="PaperTradingPortfolio"/>.
/// </summary>
internal sealed class PaperPosition(string symbol, decimal marketPrice = 0m)
{
    private readonly List<PositionLot> _lots = [];
    private readonly Dictionary<string, decimal> _ownerQuantities = new(StringComparer.OrdinalIgnoreCase);
    public string Symbol { get; } = symbol;
    public decimal Quantity { get; set; }
    public decimal CostBasis { get; set; }
    public decimal MarketPrice { get; set; } = marketPrice;
    public decimal OpenCostBasis { get; private set; }
    public long LotVersion { get; private set; }

    /// <summary>
    /// Amount borrowed from the broker to fund this position.
    /// Non-zero only for margin accounts; zero for cash accounts and short positions.
    /// </summary>
    public decimal MarginBorrowed { get; set; }

    /// <summary>
    /// The position was opened by face-value fills whose clean prices are quoted as a
    /// percentage of par, so <see cref="CostBasis"/> and <see cref="MarketPrice"/> are held
    /// in dollars per unit of face and incoming market quotes must be normalized by 100
    /// before being stored (see <c>PaperTradingPortfolio.UpdateMarketPrice</c>).
    /// </summary>
    public bool UsesFaceValuePricing { get; set; }

    /// <summary>
    /// Multiplier that turns quantity × quote into money in this book: the contract multiplier
    /// for listed premium contracts such as options and warrants (premium is paid in full), and 1
    /// for everything else — including futures,
    /// which settle variation margin rather than exchanging notional and are not yet modelled.
    /// <see cref="CostBasis"/>, <see cref="MarketPrice"/> and lot entry prices stay per-unit
    /// quotes, because every downstream reader applies the multiplier to them itself; only the
    /// money amounts (<see cref="OpenCostBasis"/>, market value, P&amp;L, cash) are scaled.
    /// Distinct from <see cref="ContractMultiplier"/>, the exposure multiplier risk reads.
    /// </summary>
    public decimal EconomicMultiplier { get => _economicMultiplier; init => _economicMultiplier = value; }

    private decimal _economicMultiplier = 1m;

    /// <summary>True when the entry holds nothing: zero quantity and no open lots.</summary>
    public bool IsFlatWithoutLots => Quantity == 0m && _lots.Count == 0;

    /// <summary>
    /// Re-bases the money scale of a flat entry that is kept only for its fund attribution, so
    /// the next holding opens at its own scale. Refused while anything is held, because lots
    /// and cost basis are priced on the existing scale.
    /// </summary>
    public void ResetMoneyScale(decimal economicMultiplier)
    {
        if (economicMultiplier <= 0m)
            throw new ArgumentOutOfRangeException(nameof(economicMultiplier), "Money multiplier must be positive.");
        if (!IsFlatWithoutLots)
            throw new InvalidOperationException($"Cannot change the money scale of '{Symbol}' while it holds a position.");
        _economicMultiplier = economicMultiplier;
    }

    /// <summary>Money value of <paramref name="quantity"/> units at a per-unit <paramref name="price"/>.</summary>
    public decimal Money(decimal quantity, decimal price) => quantity * price * EconomicMultiplier;

    /// <summary>
    /// What one unit of this position's lots measures, retained from resolved fill metadata.
    /// Symbol-only legacy reports retain the explicit shares assumption.
    /// </summary>
    public SecurityLotUnitKind LotUnit { get; init; } = SecurityLotUnitKind.Shares;

    /// <summary>The open lot book in the shared lot shape.</summary>
    public IReadOnlyList<IUnitizedLot> UnitizedLots => _lots;

    /// <summary>Unsigned exposure magnitude (|qty| × price); use <see cref="SignedMarketValue"/> for equity math.</summary>
    public decimal MarketValue => Money(Math.Abs(Quantity), MarketPrice);

    /// <summary>Signed market value: positive for longs, negative for shorts (a liability).</summary>
    public decimal SignedMarketValue => Money(Quantity, MarketPrice);

    /// <summary>
    /// Unrealised P&amp;L uses exact retained basis; the display's weighted unit quote
    /// cannot reconstruct the remaining basis after fractional relief. The retained basis is
    /// held per contract, so it is scaled by the multiplier like market value.
    /// </summary>
    public decimal UnrealisedPnl => Math.Sign(Quantity) * (MarketValue - OpenCostBasis);

    public IReadOnlyList<PositionLotEntry> GetLots() => _lots
        .Select(static lot => new PositionLotEntry(
            lot.LotId,
            Math.Abs(lot.OpenQuantity),
            lot.EntryPrice,
            lot.OpenedAt))
        .ToArray();

    public void AddLot(decimal signedQuantity, decimal entryPrice, DateTimeOffset openedAt)
    {
        if (signedQuantity == 0m)
        {
            return;
        }

        _lots.Add(new PositionLot(this, $"lot-{Guid.NewGuid():N}", signedQuantity, entryPrice, openedAt));
        OpenCostBasis = _lots.Sum(static lot => lot.OpenCostBasis);
        LotVersion++;
    }

    /// <summary>
    /// Rescales every open lot by the corporate-action factors applied to the aggregate
    /// position, preserving lot identity and open time. Keeping the lot book in step with
    /// <see cref="Quantity"/>/<see cref="CostBasis"/> is required for lot consumption: a
    /// desynced book would push post-split sells into the shortfall fallback and fabricate
    /// cost basis at the aggregate average.
    /// </summary>
    /// <summary>
    /// Rescales every owner tally by the corporate-action quantity factor, keeping fund
    /// attribution in step with the position it describes.
    /// </summary>
    public void RescaleOwnerQuantities(decimal quantityFactor)
    {
        if (quantityFactor == 1m || _ownerQuantities.Count == 0)
        {
            return;
        }

        foreach (var owner in _ownerQuantities.Keys.ToArray())
        {
            var rescaled = _ownerQuantities[owner] * quantityFactor;
            if (rescaled == 0m)
            {
                _ownerQuantities.Remove(owner);
                continue;
            }

            _ownerQuantities[owner] = rescaled;
        }
    }

    public void RescaleLots(decimal quantityFactor, decimal priceFactor, decimal adjustedQuantity, decimal? adjustedTotalCostBasis)
    {
        if (quantityFactor == 1m && priceFactor == 1m
            && (!adjustedTotalCostBasis.HasValue || adjustedTotalCostBasis.Value == OpenCostBasis))
        {
            return;
        }

        var bases = RescaledLotBases(adjustedTotalCostBasis);
        var rescaled = _lots
            .Select(lot => new PositionLot(
                this,
                lot.LotId,
                lot.OpenQuantity * quantityFactor,
                lot.EntryPrice * priceFactor,
                lot.OpenedAt,
                bases?.GetValueOrDefault(lot.LotId)))
            .ToList();

        if (adjustedTotalCostBasis.HasValue && rescaled.Count > 0)
        {
            // Keep the aggregate and its lot book equal even when each scaled quantity
            // rounds. No basis-bearing lot may silently vanish below decimal precision.
            rescaled[^1].OpenQuantity += adjustedQuantity - rescaled.Sum(static lot => lot.OpenQuantity);
            if (rescaled.Any(lot => lot.OpenQuantity == 0m
                ? lot.OpenCostBasis != 0m
                : Math.Sign(lot.OpenQuantity) != Math.Sign(adjustedQuantity)))
                throw new InvalidOperationException("The corporate action cannot retain the lot quantities and carrying basis at decimal precision.");
        }

        _lots.Clear();
        _lots.AddRange(rescaled.Where(static lot => lot.OpenQuantity != 0m));
        OpenCostBasis = _lots.Sum(static lot => lot.OpenCostBasis);
        LotVersion++;
    }

    private Dictionary<string, decimal>? RescaledLotBases(decimal? adjustedTotalCostBasis)
    {
        if (adjustedTotalCostBasis is not { } target)
            return null; // Legacy adjusters supply only their existing quantity/quote factors.

        var current = OpenCostBasis;
        if (target == current)
            return _lots.ToDictionary(static lot => lot.LotId, static lot => lot.OpenCostBasis);
        if (current <= 0m || target < 0m)
            throw new InvalidOperationException("An exact corporate-action basis change requires a positive retained basis pool and a non-negative result.");

        // Existing lot basis is the allocation weight. The shared pool allocator keeps
        // every result non-negative and assigns the exact total's rounding remainder.
        var weights = _lots.Where(static lot => lot.OpenCostBasis > 0m)
            .Select(static lot => new LotSlice<PositionLot>(lot, lot.OpenCostBasis, true)).ToArray();
        return LotBasisRelief.Allocate(weights, static lot => lot.OpenCostBasis,
                static lot => lot.OpenCostBasis, new LotBasisPool(current, target))
            .ToDictionary(static slice => slice.Lot.LotId, static slice => slice.CostBasis);
    }

    public decimal ConsumeLots(decimal quantity, PositionLotSelectionMethod method, bool isCoveringShort)
    {
        if (quantity <= 0m)
        {
            return 0m;
        }

        var consumption = LotConsumption.Consume(
            OrderLots(method, isCoveringShort),
            quantity,
            static lot => Math.Abs(lot.OpenQuantity));

        var removedCostBasis = 0m;
        var relief = LotBasisRelief.Allocate(consumption.Slices,
            static lot => Math.Abs(lot.OpenQuantity), static lot => lot.OpenCostBasis);
        foreach (var slice in relief)
        {
            removedCostBasis += slice.CostBasis;

            if (slice.ClosesLot)
            {
                _lots.Remove(slice.Lot);
            }
            else
            {
                slice.Lot.OpenCostBasis -= slice.CostBasis;
                slice.Lot.OpenQuantity = slice.Lot.OpenQuantity > 0m
                    ? slice.Lot.OpenQuantity - slice.Quantity
                    : slice.Lot.OpenQuantity + slice.Quantity;
            }
        }

        if (consumption.Shortfall > 0m)
        {
            // No matching lot records remain; carry the unmatched remainder at the
            // position-level average cost (legacy positions without lot history).
            removedCostBasis += Money(consumption.Shortfall, CostBasis);
        }

        OpenCostBasis = _lots.Sum(static lot => lot.OpenCostBasis);
        CostBasis = CalculateRemainingLotCostBasis(isCoveringShort);
        LotVersion++;

        return removedCostBasis;
    }

    private decimal CalculateRemainingLotCostBasis(bool isCoveringShort)
    {
        var remainingLots = isCoveringShort
            ? _lots.Where(static lot => lot.OpenQuantity < 0m)
            : _lots.Where(static lot => lot.OpenQuantity > 0m);

        var remainingQuantity = remainingLots.Sum(static lot => Math.Abs(lot.OpenQuantity));
        if (remainingQuantity == 0m)
        {
            return 0m;
        }

        var remainingNotional = remainingLots.Sum(static lot => lot.OpenCostBasis);
        return remainingNotional / (remainingQuantity * EconomicMultiplier);
    }

    private IEnumerable<PositionLot> OrderLots(PositionLotSelectionMethod method, bool isCoveringShort)
    {
        var candidates = isCoveringShort
            ? _lots.Where(static lot => lot.OpenQuantity < 0m)
            : _lots.Where(static lot => lot.OpenQuantity > 0m);

        return method switch
        {
            PositionLotSelectionMethod.Fifo => candidates.OrderBy(static lot => lot.OpenedAt),
            PositionLotSelectionMethod.Lifo => candidates.OrderByDescending(static lot => lot.OpenedAt),
            PositionLotSelectionMethod.Hifo => candidates.OrderByDescending(static lot => lot.EntryPrice).ThenBy(static lot => lot.OpenedAt),
            _ => candidates.OrderBy(static lot => lot.OpenedAt),
        };
    }

    /// <summary>
    /// Contract multiplier for this position, taken from the fills that opened it. Cash,
    /// P&amp;L and market value are scaled by it, so a fill with a different multiplier is
    /// refused while the position is open (see <c>ApplyFillToAccount</c>).
    /// </summary>
    public decimal ContractMultiplier { get; private set; } = 1m;

    /// <summary>Signed quantity attributed to each owning fund account.</summary>
    public IReadOnlyDictionary<string, decimal> OwnerQuantities => _ownerQuantities;

    /// <summary>
    /// True while some fund still holds a non-zero position here, even if they cancel out
    /// in aggregate. Offsetting fund books are exposure, not an absence of it.
    /// </summary>
    public bool HasOwnerExposure => _ownerQuantities.Count > 0;

    /// <summary>
    /// Records which fund a fill belonged to and the exposure multiplier risk reads. Attribution
    /// only — the lot book, cost basis, realised P&amp;L and cash are untouched here (the money
    /// scale lives on <see cref="EconomicMultiplier"/>), while the read path gains the ownership
    /// the shared execution account cannot express.
    /// The tallies always sum to <see cref="Quantity"/>: each is that fund's net signed
    /// contribution, so a fund that bought and sold the same size nets to zero.
    /// </summary>
    /// <summary>Sets the multiplier of a newly opened position before its first fill is applied.</summary>
    public void InitializeContractMultiplier(decimal contractMultiplier)
    {
        if (contractMultiplier <= 0m)
            throw new ArgumentOutOfRangeException(nameof(contractMultiplier), "Contract multiplier must be positive.");
        if (Quantity != 0m || _lots.Count > 0 || _ownerQuantities.Count > 0)
            throw new InvalidOperationException("The contract multiplier can only be initialized on an empty position.");
        ContractMultiplier = contractMultiplier;
    }

    public void AttributeFill(string? ownerAccountId, decimal signedQuantity, decimal contractMultiplier)
    {
        if (contractMultiplier > ContractMultiplier)
        {
            ContractMultiplier = contractMultiplier;
        }

        if (string.IsNullOrWhiteSpace(ownerAccountId) || signedQuantity == 0m)
        {
            return;
        }

        var owner = ownerAccountId.Trim();
        var updated = _ownerQuantities.GetValueOrDefault(owner) + signedQuantity;
        if (updated == 0m)
        {
            _ownerQuantities.Remove(owner);
            return;
        }

        _ownerQuantities[owner] = updated;
    }

    public ExecutionPosition ToExecutionPosition() => new(
        Symbol,
        (long)Quantity,
        CostBasis,
        UnrealisedPnl,
        0m)  // realised P&L is carried at the account level
    {
        OwnerQuantities = new Dictionary<string, decimal>(_ownerQuantities, StringComparer.OrdinalIgnoreCase),
        ContractMultiplier = ContractMultiplier,
        // The record's Quantity is whole shares; owner attribution is not. Carry the
        // unrounded size so the unattributed remainder is a real remainder.
        ExactQuantity = Quantity
    };
}

/// <summary>
/// One open paper lot. Implements the shared lot shape (<see cref="IUnitizedLot"/>) so the paper
/// book is read the same way as execution, backtesting and ledger lots: the unit and multiplier
/// are read live from the owning <see cref="PaperPosition"/>, which is what fills opened it in, so a
/// contract multiplier recorded after the lot opened is never stale on the lot.
/// </summary>
internal sealed class PositionLot(PaperPosition position, string lotId, decimal openQuantity, decimal entryPrice,
    DateTimeOffset openedAt, decimal? openCostBasis = null) : IUnitizedLot
{
    public string LotId { get; } = lotId;
    public decimal OpenQuantity { get; set; } = openQuantity;
    public decimal EntryPrice { get; } = entryPrice;
    public decimal OpenCostBasis { get; set; } = openCostBasis ?? position.Money(Math.Abs(openQuantity), entryPrice);
    public DateTimeOffset OpenedAt { get; } = openedAt;

    /// <summary>Unsigned open size; <see cref="OpenQuantity"/> keeps the sign (negative for shorts).</summary>
    public decimal Quantity => Math.Abs(OpenQuantity);

    public SecurityLotUnitKind UnitKind => position.LotUnit;

    public decimal Multiplier => position.ContractMultiplier;
}
