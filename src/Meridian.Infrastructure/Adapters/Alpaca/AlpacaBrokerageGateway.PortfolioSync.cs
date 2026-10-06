using System.Globalization;
using Meridian.Execution.Sdk;

namespace Meridian.Infrastructure.Adapters.Alpaca;

public sealed partial class AlpacaBrokerageGateway
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<BrokerageExternalAccountDto>> GetAccountsAsync(CancellationToken ct = default)
    {
        var account = await GetAccountInfoAsync(ct).ConfigureAwait(false);
        return
        [
            new BrokerageExternalAccountDto(
                ProviderId: GatewayId,
                AccountId: account.AccountId,
                DisplayName: string.IsNullOrWhiteSpace(account.AccountId) ? BrokerDisplayName : $"{BrokerDisplayName} {account.AccountId}",
                Status: account.Status,
                Currency: account.Currency,
                RetrievedAt: account.RetrievedAt,
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["environment"] = CurrentCredentials.Environment,
                    ["broker"] = BrokerDisplayName
                })
        ];
    }

    /// <inheritdoc />
    public async Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(
        string externalAccountId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalAccountId);

        var completenessIssues = new List<string>();
        var account = await RequireRequestedAccountAsync(externalAccountId, ct, completenessIssues).ConfigureAwait(false);
        var positions = await GetPositionsCoreAsync(completenessIssues, ct).ConfigureAwait(false);
        var accountDto = new BrokerageExternalAccountDto(
            ProviderId: GatewayId,
            AccountId: account.AccountId,
            DisplayName: string.IsNullOrWhiteSpace(account.AccountId) ? BrokerDisplayName : $"{BrokerDisplayName} {account.AccountId}",
            Status: account.Status,
            Currency: account.Currency,
            RetrievedAt: account.RetrievedAt,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requestedAccountId"] = externalAccountId,
                ["environment"] = CurrentCredentials.Environment
            });

        var retrievedAt = DateTimeOffset.UtcNow;
        var restrictions = BuildAccountRestrictions(account);
        var marginRegime = account.MarginMultiplier switch
        {
            <= 1m => BrokerageMarginRegime.Cash,
            > 1m => BrokerageMarginRegime.RegulationT,
            _ => BrokerageMarginRegime.Unknown
        };

        return new BrokeragePortfolioSnapshotDto(
            Account: accountDto,
            Balance: new BrokerageBalanceSnapshotDto(
                Cash: account.Cash,
                Equity: account.Equity,
                BuyingPower: account.BuyingPower,
                Currency: account.Currency,
                MarginBalance: Math.Max(0m, -account.Cash)),
            Positions: positions
                .Select(position => new BrokeragePositionSnapshotDto(
                    Symbol: position.Symbol,
                    Quantity: position.Quantity,
                    AverageEntryPrice: position.AverageEntryPrice,
                    MarketPrice: position.MarketPrice,
                    MarketValue: position.MarketValue,
                    UnrealizedPnl: position.UnrealizedPnl,
                    AssetClass: position.AssetClass,
                    Description: position.Description,
                    PositionId: position.PositionId,
                    // Trading API stock/option dollar values are bound at this adapter;
                    // account base currency is not a general position denomination fallback.
                    Currency: string.Equals(account.Currency, "USD", StringComparison.Ordinal)
                        && position.AssetClass is "us_equity" or "us_option" ? "USD" : null,
                    Metadata: position.Metadata))
                .ToArray(),
            RetrievedAt: retrievedAt,
            AccountSnapshot: new BrokerageAccountSnapshotDto(
                ProviderId: GatewayId,
                AccountId: account.AccountId,
                AsOf: retrievedAt,
                Currency: account.Currency,
                Status: account.Status,
                MarginRegime: marginRegime,
                Cash: account.Cash,
                Equity: account.Equity,
                BuyingPower: account.BuyingPower,
                LongMarketValue: account.LongMarketValue,
                ShortMarketValue: account.ShortMarketValue,
                RegTBuyingPower: account.RegTBuyingPower,
                InitialMargin: account.InitialMargin,
                MaintenanceMargin: account.MaintenanceMargin,
                LastMaintenanceMargin: account.LastMaintenanceMargin,
                ExcessLiquidity: account.MaintenanceMargin.HasValue
                    ? account.Equity - account.MaintenanceMargin.Value
                    : null,
                SpecialMemorandumAccount: account.SpecialMemorandumAccount,
                MarginLoan: account.Cash < 0m ? -account.Cash : null,
                Multiplier: account.MarginMultiplier,
                TradingBlocked: account.TradingBlocked,
                TransfersBlocked: account.TransfersBlocked,
                AccountBlocked: account.AccountBlocked,
                ShortingEnabled: account.ShortingEnabled,
                OptionsApprovedLevel: account.OptionsApprovedLevel,
                OptionsTradingLevel: account.OptionsTradingLevel,
                Restrictions: restrictions,
                SourceAttributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["environment"] = CurrentCredentials.Environment,
                    ["sourceAuthority"] = "ProviderReported"
                }),
            BorrowPositions: positions
                .Where(static position => position.Quantity < 0m)
                .Select(position => new BrokerageBorrowPositionSnapshotDto(
                    Symbol: position.Symbol,
                    Quantity: position.Quantity,
                    Status: BrokerageBorrowStatus.Unknown,
                    Currency: account.Currency,
                    AccountId: account.AccountId))
                .ToArray(),
            IsComplete: completenessIssues.Count == 0,
            CompletenessIssues: completenessIssues.ToArray());
    }

    private static void RequirePortfolioDecimal(string? value, string field, List<string> issues)
    {
        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            issues.Add($"Broker {field} was missing or invalid.");
    }
}
