namespace Meridian.FinancialOperations.Reconciliation.Connectors;

/// <summary>
/// Built-in mapping-profile documents shipped with the platform. The first two mirror the
/// legacy code-defined defaults; the rest back the OFX, IB Flex, and Alpaca connectors.
/// Built-ins cannot be edited or deleted through the profile store, but operators can clone
/// them into custom documents.
/// </summary>
public static class StatementBuiltInProfiles
{
    public const string OfxBankV1ProfileId = "ofx-bank-v1";
    public const string IbFlexV1ProfileId = "ib-flex-v1";
    public const string AlpacaActivityV1ProfileId = "alpaca-activity-v1";

    public static IReadOnlyList<StatementMappingProfileDocument> All { get; } =
    [
        CanonicalCsvV1(),
        SampleBrokerCsvV1(),
        OfxBankV1(),
        IbFlexV1(),
        AlpacaActivityV1()
    ];

    private static StatementMappingProfileDocument CanonicalCsvV1() => new(
        SchemaVersion: StatementMappingProfileDocument.CurrentSchemaVersion,
        ProfileId: StatementMappingProfileRegistry.CanonicalCsvV1ProfileId,
        DisplayName: "Canonical CSV v1",
        Format: StatementMappingProfileDocument.CsvFormat,
        Csv: new StatementProfileCsvOptions(),
        Culture: null,
        DateFormats: ["yyyy-MM-dd"],
        Fields:
        [
            new("Account", "account", ["account number", "acct", "account id"], Required: true),
            new("SecurityIdentifier", "symbol", ["ticker", "security", "instrument", "cusip"], Required: true),
            new("Quantity", "quantity", ["qty", "units", "shares"], Required: true),
            new("Price", "price", ["unit price", "trade price", "execution price"], Required: true),
            new("CashAmount", "cashAmount", ["cash amount", "net amount", "net cash"], Required: true),
            new("ActivityType", "activityType", ["activity type", "transaction type", "txn type", "activity"], Required: true),
            new("TradeDate", "tradeDate", ["trade date", "transaction date", "date"], Required: true),
            new("SettlementDate", "settlementDate", ["settle date", "settlement date"]),
            new("Currency", "currency", ["ccy", "iso currency"]),
            new("FeesCommission", "feesCommission", ["fees", "commission", "fee amount"]),
            new("ExternalTransactionId", "externalTransactionId", ["transaction id", "reference", "external id"]),
            new("AccountId", "accountId"),
            new("ExternalAccountId", "externalAccountId"),
            new("SecurityId", "securityId"),
            new("UnresolvedIdentifier", "unresolvedIdentifier"),
            new("MarketValue", "marketValue", ["market value", "value"]),
            new("Amount", "amount"),
            new("ExternalReference", "externalReference")
        ],
        ActivityCodes:
        [
            new("position", "position"),
            new("pos", "position"),
            new("cash", "cash"),
            new("cashbalance", "cashbalance"),
            new("trade", "trade"),
            new("buy", "trade"),
            new("sell", "trade"),
            new("fee", "fee"),
            new("dividend", "dividend"),
            new("div", "dividend")
        ],
        IsBuiltIn: true,
        Notes: "Canonical Meridian statement layout. Commit artifacts always render in this shape.");

    private static StatementMappingProfileDocument SampleBrokerCsvV1() => new(
        SchemaVersion: StatementMappingProfileDocument.CurrentSchemaVersion,
        ProfileId: StatementMappingProfileRegistry.SampleBrokerCsvV1ProfileId,
        DisplayName: "Sample broker CSV v1",
        Format: StatementMappingProfileDocument.CsvFormat,
        Csv: new StatementProfileCsvOptions(),
        Culture: null,
        DateFormats: ["yyyy-MM-dd", "MM/dd/yyyy"],
        Fields:
        [
            new("Account", "BrokerAccount", ["Account"], Required: true),
            new("SecurityIdentifier", "Ticker", ["Symbol"], Required: true),
            new("Quantity", "Units", ["Quantity"], Required: true),
            new("Price", "ExecutionPrice", ["Price"], Required: true),
            new("CashAmount", "NetCash", ["Net Amount"], Required: true),
            new("ActivityType", "TxnCode", ["Activity"], Required: true),
            new("TradeDate", "TradeDate", ["Trade Date"], Required: true),
            new("SettlementDate", "SettleDate"),
            new("Currency", "CCY"),
            new("FeesCommission", "Commission"),
            new("ExternalTransactionId", "BrokerTransactionId")
        ],
        ActivityCodes:
        [
            new("POS", "position"),
            new("CASH", "cash"),
            new("BUY", "trade"),
            new("SELL", "trade"),
            new("FEE", "fee"),
            new("DIV", "dividend")
        ],
        IsBuiltIn: true,
        Notes: "Mirrors the legacy sample-broker layout used by existing fixtures and demos.");

    private static StatementMappingProfileDocument OfxBankV1() => new(
        SchemaVersion: StatementMappingProfileDocument.CurrentSchemaVersion,
        ProfileId: OfxBankV1ProfileId,
        DisplayName: "OFX bank & investment v1",
        Format: StatementMappingProfileDocument.OfxFormat,
        Csv: null,
        Culture: null,
        DateFormats: ["yyyyMMdd", "yyyy-MM-dd"],
        Fields:
        [
            new("Account", "ACCTID", ["ACCTKEY"], Required: true),
            new("SecurityIdentifier", "TICKER", ["UNIQUEID", "SECNAME"]),
            new("Quantity", "UNITS"),
            new("Price", "UNITPRICE"),
            new("CashAmount", "TRNAMT", ["TOTAL", "BALAMT", "MKTVAL"], Required: true),
            new("ActivityType", "TRNTYPE", ["AGGREGATE"], Required: true),
            new("TradeDate", "DTPOSTED", ["DTTRADE", "DTASOF"], Required: true),
            new("SettlementDate", "DTSETTLE", ["DTAVAIL"]),
            new("Currency", "CURSYM", ["CURDEF"]),
            new("FeesCommission", "COMMISSION", ["FEES"]),
            new("ExternalTransactionId", "FITID")
        ],
        ActivityCodes:
        [
            // STMTTRN types are ledger movements, not the account's ending cash balance. The canonical
            // "cash"/"cashbalance" activities are reserved for balance rows (LEDGERBAL), so movements map
            // to "transaction" and reconcile against ledger transactions with their FITID identity intact.
            new("CREDIT", "transaction"),
            new("DEBIT", "transaction"),
            new("INT", "transaction"),
            new("XFER", "transaction"),
            new("PAYMENT", "transaction"),
            new("ATM", "transaction"),
            new("POS", "transaction"),
            new("CHECK", "transaction"),
            new("DEP", "transaction"),
            new("DIRECTDEP", "transaction"),
            new("DIRECTDEBIT", "transaction"),
            new("REPEATPMT", "transaction"),
            new("CASH", "transaction"),
            new("OTHER", "transaction"),
            new("FEE", "fee"),
            new("SRVCHG", "fee"),
            new("DIV", "dividend"),
            new("LEDGERBAL", "cashbalance"),
            new("INVBUY", "trade"),
            new("INVSELL", "trade"),
            new("BUYSTOCK", "trade"),
            new("SELLSTOCK", "trade"),
            new("BUYMF", "trade"),
            new("SELLMF", "trade"),
            new("POSSTOCK", "position"),
            new("POSMF", "position"),
            new("POSOTHER", "position"),
            new("INCOME", "dividend")
        ],
        IsBuiltIn: true,
        Notes: "Maps OFX 1.x/2.x aggregates (STMTTRN, LEDGERBAL, INVBUY/INVSELL, INVPOSLIST) flattened to tag pseudo-columns.");

    private static StatementMappingProfileDocument IbFlexV1() => new(
        SchemaVersion: StatementMappingProfileDocument.CurrentSchemaVersion,
        ProfileId: IbFlexV1ProfileId,
        DisplayName: "Interactive Brokers Flex Report v1",
        Format: StatementMappingProfileDocument.CsvFormat,
        Csv: null,
        Culture: null,
        DateFormats: ["yyyyMMdd", "yyyy-MM-dd"],
        Fields:
        [
            new("Account", "accountId", Aliases: null, Required: true),
            new("SecurityIdentifier", "symbol"),
            new("Quantity", "quantity", ["position"]),
            new("Price", "tradePrice", ["markPrice"]),
            new("CashAmount", "netCash", ["amount", "positionValue"], Required: true),
            new("ActivityType", "type", Aliases: null, Required: true),
            new("TradeDate", "tradeDate", ["dateTime", "reportDate"], Required: true),
            new("SettlementDate", "settleDateTarget"),
            new("Currency", "currency"),
            new("FeesCommission", "ibCommission"),
            new("ExternalTransactionId", "tradeID", ["transactionID"])
        ],
        ActivityCodes:
        [
            new("Dividends", "dividend"),
            new("Payment In Lieu Of Dividends", "dividend"),
            new("Withholding Tax", "fee"),
            new("Broker Interest Paid", "fee"),
            // Flex CashTransactions are ledger movements, not the account's ending cash balance, so
            // they reconcile against ledger transactions: canonical "cash"/"cashbalance" are reserved
            // for balance rows (the CashReport section).
            new("Broker Interest Received", "transaction"),
            new("Other Fees", "fee"),
            new("Commission Adjustments", "fee"),
            new("Deposits/Withdrawals", "transaction"),
            new("Deposits & Withdrawals", "transaction")
        ],
        IsBuiltIn: true,
        Notes: "IB Flex Query XML sections: Trades, CashTransactions (types mapped here), OpenPositions. Sections present depend on the operator's Flex query configuration.");

    private static StatementMappingProfileDocument AlpacaActivityV1() => new(
        SchemaVersion: StatementMappingProfileDocument.CurrentSchemaVersion,
        ProfileId: AlpacaActivityV1ProfileId,
        DisplayName: "Alpaca account activity v1",
        Format: StatementMappingProfileDocument.CsvFormat,
        Csv: null,
        Culture: null,
        DateFormats: ["yyyy-MM-dd"],
        Fields:
        [
            new("Account", "accountId", Aliases: null, Required: true),
            new("SecurityIdentifier", "symbol"),
            new("Quantity", "quantity"),
            new("Price", "price"),
            new("CashAmount", "amount", Aliases: null, Required: true),
            new("ActivityType", "activityType", Aliases: null, Required: true),
            new("TradeDate", "date", Aliases: null, Required: true),
            new("Currency", "currency"),
            new("FeesCommission", "commission"),
            new("ExternalTransactionId", "externalId")
        ],
        ActivityCodes:
        [
            new("FILL", "trade"),
            new("DIV", "dividend"),
            new("DIVCGL", "dividend"),
            new("DIVCGS", "dividend"),
            new("DIVNRA", "dividend"),
            new("DIVROC", "dividend"),
            // Alpaca cash activities (interest, deposits, withdrawals, journals) are ledger movements;
            // the portfolio snapshot's balance record is the only genuine cash balance for this profile.
            new("INT", "transaction"),
            new("CSD", "transaction"),
            new("CSW", "transaction"),
            new("JNLC", "transaction"),
            new("FEE", "fee"),
            new("REG", "fee"),
            new("TAF", "fee"),
            new("PTC", "fee")
        ],
        IsBuiltIn: true,
        Notes: "Maps Alpaca brokerage activity + portfolio snapshots fetched through the registered Alpaca gateway.");
}
