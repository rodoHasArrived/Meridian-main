using System.Net.Http.Json;
using Meridian.Execution.Sdk;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Infrastructure.Adapters.Alpaca;

// How the gateway names the connected account. Alpaca reports two identifiers for one account;
// the account number is the identity shown to operators and the UUID is accepted as an alias.
public sealed partial class AlpacaBrokerageGateway
{
    /// <inheritdoc />
    public bool IdentifiesConnectedAccount(string externalAccountId)
    {
        if (ScopeIdentity is null || string.IsNullOrWhiteSpace(externalAccountId))
            return false;
        var requested = externalAccountId.Trim();
        return string.Equals(_connectedAccountId, requested, StringComparison.OrdinalIgnoreCase)
            || _connectedProviderAccountId is { } providerAccountId
                && string.Equals(providerAccountId, requested, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the account with both of Alpaca's names for it: the account number an operator sees
    /// in the dashboard and on statements, which is the account's identity here, and the internal
    /// UUID, which is only ever accepted as an alias.
    /// </summary>
    private async Task<(AccountInfo Account, string? ProviderAccountId)> ReadAccountAsync(
        List<string>? completenessIssues, CancellationToken ct)
    {
        using var client = CreateHttpClient();
        var response = await client.GetAsync($"{BaseUrl}/v2/account", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var account = await response.Content.ReadFromJsonAsync(
            AlpacaBrokerageSerializerContext.Default.AlpacaAccountResponse, ct).ConfigureAwait(false);

        if (completenessIssues is not null)
        {
            RequirePortfolioDecimal(account?.Cash, "account cash", completenessIssues);
            RequirePortfolioDecimal(account?.Equity, "account equity", completenessIssues);
            RequirePortfolioDecimal(account?.BuyingPower, "account buying power", completenessIssues);
            RequireOptionalPortfolioDecimal(account?.Multiplier, "account multiplier", completenessIssues);
            RequireOptionalPortfolioDecimal(account?.RegTBuyingPower, "account Reg T buying power", completenessIssues);
            RequireOptionalPortfolioDecimal(account?.InitialMargin, "account initial margin", completenessIssues);
            RequireOptionalPortfolioDecimal(account?.MaintenanceMargin, "account maintenance margin", completenessIssues);
            RequireOptionalPortfolioDecimal(account?.LastMaintenanceMargin, "account last maintenance margin", completenessIssues);
            RequireOptionalPortfolioDecimal(account?.SpecialMemorandumAccount, "account SMA", completenessIssues);
            RequireOptionalPortfolioDecimal(account?.LongMarketValue, "account long market value", completenessIssues);
            RequireOptionalPortfolioDecimal(account?.ShortMarketValue, "account short market value", completenessIssues);
            RequireOptionalPortfolioDecimal(account?.NonMarginableBuyingPower, "account non-marginable buying power", completenessIssues);
            if (string.IsNullOrWhiteSpace(account?.Currency))
                completenessIssues.Add("Account currency was not reported.");
            if (string.IsNullOrWhiteSpace(account?.Status))
                completenessIssues.Add("Account status was not reported.");
            if (account?.TradingBlocked is null || account.AccountBlocked is null)
                completenessIssues.Add("Account trading restrictions were not reported.");
        }

        var accountNumber = NormalizeOptional(account?.AccountNumber);
        var providerAccountId = NormalizeOptional(account?.Id);
        return (new AccountInfo
        {
            AccountId = accountNumber ?? providerAccountId ?? "unknown",
            Equity = ParseDecimal(account?.Equity),
            Cash = ParseDecimal(account?.Cash),
            BuyingPower = ParseDecimal(account?.BuyingPower),
            Currency = account?.Currency ?? string.Empty,
            Status = account?.Status ?? "unknown",
            MarginMultiplier = ParseNullableDecimal(account?.Multiplier),
            RegTBuyingPower = ParseNullableDecimal(account?.RegTBuyingPower),
            InitialMargin = ParseNullableDecimal(account?.InitialMargin),
            MaintenanceMargin = ParseNullableDecimal(account?.MaintenanceMargin),
            LastMaintenanceMargin = ParseNullableDecimal(account?.LastMaintenanceMargin),
            SpecialMemorandumAccount = ParseNullableDecimal(account?.SpecialMemorandumAccount),
            LongMarketValue = ParseNullableDecimal(account?.LongMarketValue),
            ShortMarketValue = ParseNullableDecimal(account?.ShortMarketValue),
            NonMarginableBuyingPower = ParseNullableDecimal(account?.NonMarginableBuyingPower),
            TradingBlocked = account?.TradingBlocked ?? false,
            TransfersBlocked = account?.TransfersBlocked ?? false,
            AccountBlocked = account?.AccountBlocked ?? false,
            ShortingEnabled = account?.ShortingEnabled ?? false,
            OptionsApprovedLevel = account?.OptionsApprovedLevel,
            OptionsTradingLevel = account?.OptionsTradingLevel,
        }, accountNumber is null ? null : providerAccountId);
    }

    private async Task<AccountInfo> RequireRequestedAccountAsync(
        string externalAccountId,
        CancellationToken ct,
        List<string>? completenessIssues = null)
    {
        var (account, providerAccountId) = await ReadAccountAsync(completenessIssues, ct).ConfigureAwait(false);
        var requested = externalAccountId.Trim();
        if (string.Equals(account.AccountId, requested, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(account.AccountId, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return account;
        }

        if (providerAccountId is not null && string.Equals(providerAccountId, requested, StringComparison.OrdinalIgnoreCase))
        {
            // A link retained under the UUID keeps resolving: the evidence is returned under the
            // name the caller holds, so its own identity checks still compare like with like.
            return account with { AccountId = providerAccountId };
        }

        throw new InvalidOperationException(
            "The requested Alpaca account does not match the account resolved by the configured credentials.");
    }
}
