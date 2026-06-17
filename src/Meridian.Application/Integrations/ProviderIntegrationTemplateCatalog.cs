using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

public sealed class ProviderIntegrationTemplateCatalog
{
    private const string SeedAuthor = "meridian-system";
    private static readonly DateTimeOffset SeededAt = DateTimeOffset.Parse("2026-06-16T00:00:00Z");
    private static readonly IReadOnlyList<ProviderIntegrationManifestDto> Templates =
    [
        CreateManualCsvUploadTemplate(),
        CreateCustodianPositionsTemplate(),
        CreateBrokerageTransactionsTemplate(),
        CreateFixedIncomeSecurityMasterTemplate()
    ];

    public IReadOnlyList<ProviderIntegrationManifestDto> ListManifests() => Templates;

    public IReadOnlyList<ProviderIntegrationTemplateCatalogEntryDto> ListEntries()
        => Templates
            .Select(template => new ProviderIntegrationTemplateCatalogEntryDto(
                template.ManifestId,
                template.ProviderId,
                template.DisplayName,
                template.IntegrationType,
                template.Capabilities
                    .Where(capability => capability.Enabled)
                    .Select(capability => capability.Capability)
                    .ToArray(),
                template.ChangeReason ?? template.DisplayName,
                template.Auth.Type != ProviderIntegrationAuthTypeDto.None))
            .ToArray();

    public ProviderIntegrationManifestDto? GetManifest(string manifestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestId);
        return Templates.FirstOrDefault(template =>
            StringComparer.OrdinalIgnoreCase.Equals(template.ManifestId, manifestId));
    }

    private static ProviderIntegrationManifestDto CreateManualCsvUploadTemplate()
        => new(
            "template-manual-csv-upload-v1",
            1,
            "manual-csv-upload",
            "Manual CSV Upload",
            IntegrationTypeDto.ManualUpload,
            "template",
            NoAuth(),
            [
                Capability(
                    ProviderCapabilityKindDto.Positions,
                    "providerAccountId",
                    "security.cusip",
                    "quantity",
                    "asOf"),
                Capability(
                    ProviderCapabilityKindDto.Transactions,
                    "providerTransactionId",
                    "providerAccountId",
                    "transactionType",
                    "postingDate",
                    "amount.amount",
                    "amount.currency"),
                Capability(
                    ProviderCapabilityKindDto.SecurityReferenceData,
                    "security.cusip",
                    "security.name")
            ],
            [],
            [
                Mapping(ProviderCapabilityKindDto.Positions, "$.account_id", "providerAccountId", "trim", required: true),
                Mapping(ProviderCapabilityKindDto.Positions, "$.cusip", "security.cusip", "uppercase", required: true),
                Mapping(ProviderCapabilityKindDto.Positions, "$.quantity", "quantity", "decimal", required: true),
                Mapping(ProviderCapabilityKindDto.Positions, "$.as_of", "asOf", "date", required: true),
                Mapping(ProviderCapabilityKindDto.Positions, "$.currency", "marketValue.currency", "uppercase", defaultValue: "USD"),
                Mapping(ProviderCapabilityKindDto.Positions, "$.source_id", "sourceRecordId"),
                Mapping(ProviderCapabilityKindDto.Transactions, "$.transaction_id", "providerTransactionId", "trim", required: true),
                Mapping(ProviderCapabilityKindDto.Transactions, "$.account_id", "providerAccountId", "trim", required: true),
                Mapping(
                    ProviderCapabilityKindDto.Transactions,
                    "$.transaction_type",
                    "transactionType",
                    "enum",
                    required: true,
                    parameters: TransactionTypeMap()),
                Mapping(ProviderCapabilityKindDto.Transactions, "$.posting_date", "postingDate", "date", required: true),
                Mapping(
                    ProviderCapabilityKindDto.Transactions,
                    "$.amount",
                    "amount.amount",
                    "signedAmount",
                    required: true,
                    parameters: new Dictionary<string, string>
                    {
                        ["conditionSourcePath"] = "$.transaction_type",
                        ["negativeValues"] = "SELL,FEE,WD,WITHDRAWAL"
                    }),
                Mapping(ProviderCapabilityKindDto.Transactions, "$.currency", "amount.currency", "uppercase", required: true, defaultValue: "USD"),
                Mapping(ProviderCapabilityKindDto.Transactions, "$.cusip", "security.cusip", "uppercase"),
                Mapping(ProviderCapabilityKindDto.SecurityReferenceData, "$.cusip", "security.cusip", "uppercase", required: true),
                Mapping(ProviderCapabilityKindDto.SecurityReferenceData, "$.security_name", "security.name", "trim", required: true),
                Mapping(ProviderCapabilityKindDto.SecurityReferenceData, "$.isin", "security.isin", "uppercase"),
                Mapping(ProviderCapabilityKindDto.SecurityReferenceData, "$.currency", "security.currency", "uppercase", defaultValue: "USD")
            ],
            ManualSync(),
            [
                RequiredRule(ProviderCapabilityKindDto.Positions, "providerAccountId", "security.cusip", "quantity", "asOf"),
                RequiredRule(ProviderCapabilityKindDto.Transactions, "providerTransactionId", "providerAccountId", "transactionType", "postingDate", "amount.amount", "amount.currency"),
                RequiredRule(ProviderCapabilityKindDto.SecurityReferenceData, "security.cusip", "security.name")
            ],
            ManualActivationPolicy(),
            ProviderIntegrationActivationStateDto.Draft,
            SeedAuthor,
            SeededAt,
            null,
            null,
            "Starter template for operator-uploaded CSV files with positions, transactions, and security reference data.");

    private static ProviderIntegrationManifestDto CreateCustodianPositionsTemplate()
        => new(
            "template-custodian-positions-v1",
            1,
            "custodian-positions",
            "Custodian Positions",
            IntegrationTypeDto.Rest,
            "template",
            OAuthAuth("accounts.read", "positions.read"),
            [
                Capability(ProviderCapabilityKindDto.Accounts, "providerAccountId", "accountName", "baseCurrency"),
                Capability(ProviderCapabilityKindDto.Positions, "providerAccountId", "security.cusip", "quantity", "asOf")
            ],
            [
                Endpoint(
                    "accounts",
                    ProviderCapabilityKindDto.Accounts,
                    "/v1/accounts",
                    new EndpointResponseShapeDto("$.accounts", null, ["$.accounts[*].id"])),
                Endpoint(
                    "positions",
                    ProviderCapabilityKindDto.Positions,
                    "/v1/accounts/{accountId}/positions",
                    new EndpointResponseShapeDto("$.positions", null, ["$.positions[*].account_id", "$.positions[*].quantity"]),
                    new EndpointDependencyDto("accounts", "$.id", "accountId"))
            ],
            [
                Mapping(ProviderCapabilityKindDto.Accounts, "$.id", "providerAccountId", "trim", required: true),
                Mapping(ProviderCapabilityKindDto.Accounts, "$.name", "accountName", "trim", required: true),
                Mapping(ProviderCapabilityKindDto.Accounts, "$.currency", "baseCurrency", "uppercase", required: true, defaultValue: "USD"),
                Mapping(ProviderCapabilityKindDto.Accounts, "$.type", "accountType", "lowercase"),
                Mapping(ProviderCapabilityKindDto.Positions, "$.account_id", "providerAccountId", "trim", required: true),
                Mapping(ProviderCapabilityKindDto.Positions, "$.cusip", "security.cusip", "uppercase", required: true),
                Mapping(ProviderCapabilityKindDto.Positions, "$.isin", "security.isin", "uppercase"),
                Mapping(ProviderCapabilityKindDto.Positions, "$.symbol", "security.symbol", "uppercase"),
                Mapping(ProviderCapabilityKindDto.Positions, "$.quantity", "quantity", "decimal", required: true),
                Mapping(ProviderCapabilityKindDto.Positions, "$.price", "price.amount", "decimal"),
                Mapping(ProviderCapabilityKindDto.Positions, "$.market_value", "marketValue.amount", "decimal"),
                Mapping(ProviderCapabilityKindDto.Positions, "$.currency", "marketValue.currency", "uppercase", defaultValue: "USD"),
                Mapping(ProviderCapabilityKindDto.Positions, "$.as_of_date", "asOf", "date", required: true),
                Mapping(ProviderCapabilityKindDto.Positions, "$.position_id", "sourceRecordId")
            ],
            IncrementalSync("updated_at"),
            [
                RequiredRule(ProviderCapabilityKindDto.Accounts, "providerAccountId", "accountName", "baseCurrency"),
                RequiredRule(ProviderCapabilityKindDto.Positions, "providerAccountId", "security.cusip", "quantity", "asOf")
            ],
            ApiActivationPolicy(),
            ProviderIntegrationActivationStateDto.Draft,
            SeedAuthor,
            SeededAt,
            null,
            null,
            "REST template for account-first custodian position feeds with cursor-friendly dependency mapping.");

    private static ProviderIntegrationManifestDto CreateBrokerageTransactionsTemplate()
        => new(
            "template-brokerage-transactions-v1",
            1,
            "brokerage-transactions",
            "Brokerage Transactions",
            IntegrationTypeDto.Rest,
            "template",
            OAuthAuth("accounts.read", "transactions.read"),
            [
                Capability(ProviderCapabilityKindDto.Accounts, "providerAccountId", "accountName", "baseCurrency"),
                Capability(ProviderCapabilityKindDto.Transactions, "providerTransactionId", "providerAccountId", "transactionType", "postingDate", "amount.amount", "amount.currency")
            ],
            [
                Endpoint(
                    "accounts",
                    ProviderCapabilityKindDto.Accounts,
                    "/v1/accounts",
                    new EndpointResponseShapeDto("$.accounts", null, ["$.accounts[*].id"])),
                Endpoint(
                    "transactions",
                    ProviderCapabilityKindDto.Transactions,
                    "/v1/accounts/{accountId}/transactions",
                    new EndpointResponseShapeDto("$.transactions", null, ["$.transactions[*].transaction_id", "$.transactions[*].amount"]),
                    new EndpointDependencyDto("accounts", "$.id", "accountId"))
            ],
            [
                Mapping(ProviderCapabilityKindDto.Accounts, "$.id", "providerAccountId", "trim", required: true),
                Mapping(ProviderCapabilityKindDto.Accounts, "$.name", "accountName", "trim", required: true),
                Mapping(ProviderCapabilityKindDto.Accounts, "$.currency", "baseCurrency", "uppercase", required: true, defaultValue: "USD"),
                Mapping(ProviderCapabilityKindDto.Transactions, "$.transaction_id", "providerTransactionId", "trim", required: true),
                Mapping(ProviderCapabilityKindDto.Transactions, "$.account_id", "providerAccountId", "trim", required: true),
                Mapping(
                    ProviderCapabilityKindDto.Transactions,
                    "$.type",
                    "transactionType",
                    "enum",
                    required: true,
                    parameters: TransactionTypeMap()),
                Mapping(ProviderCapabilityKindDto.Transactions, "$.trade_date", "tradeDate", "date"),
                Mapping(ProviderCapabilityKindDto.Transactions, "$.settlement_date", "settlementDate", "date"),
                Mapping(ProviderCapabilityKindDto.Transactions, "$.posting_date", "postingDate", "date", required: true),
                Mapping(ProviderCapabilityKindDto.Transactions, "$.quantity", "quantity", "decimal"),
                Mapping(ProviderCapabilityKindDto.Transactions, "$.price", "price.amount", "decimal"),
                Mapping(
                    ProviderCapabilityKindDto.Transactions,
                    "$.amount",
                    "amount.amount",
                    "signedAmount",
                    required: true,
                    parameters: new Dictionary<string, string>
                    {
                        ["conditionSourcePath"] = "$.type",
                        ["negativeValues"] = "SELL,FEE,WD,WITHDRAWAL"
                    }),
                Mapping(ProviderCapabilityKindDto.Transactions, "$.currency", "amount.currency", "uppercase", required: true, defaultValue: "USD"),
                Mapping(ProviderCapabilityKindDto.Transactions, "$.cusip", "security.cusip", "uppercase"),
                Mapping(ProviderCapabilityKindDto.Transactions, "$.description", "description", "trim")
            ],
            IncrementalSync("updated_at"),
            [
                RequiredRule(ProviderCapabilityKindDto.Accounts, "providerAccountId", "accountName", "baseCurrency"),
                RequiredRule(ProviderCapabilityKindDto.Transactions, "providerTransactionId", "providerAccountId", "transactionType", "postingDate", "amount.amount", "amount.currency")
            ],
            ApiActivationPolicy(),
            ProviderIntegrationActivationStateDto.Draft,
            SeedAuthor,
            SeededAt,
            null,
            null,
            "REST template for brokerage transaction feeds with enum mapping and amount sign handling.");

    private static ProviderIntegrationManifestDto CreateFixedIncomeSecurityMasterTemplate()
        => new(
            "template-fixed-income-security-master-v1",
            1,
            "fixed-income-security-master",
            "Fixed Income Security Master",
            IntegrationTypeDto.SftpFile,
            "template",
            NoAuth(),
            [
                Capability(
                    ProviderCapabilityKindDto.SecurityReferenceData,
                    "security.cusip",
                    "security.name",
                    "security.maturityDate",
                    "security.currency")
            ],
            [],
            [
                Mapping(ProviderCapabilityKindDto.SecurityReferenceData, "$.cusip", "security.cusip", "uppercase", required: true),
                Mapping(ProviderCapabilityKindDto.SecurityReferenceData, "$.isin", "security.isin", "uppercase"),
                Mapping(ProviderCapabilityKindDto.SecurityReferenceData, "$.ticker", "security.ticker", "uppercase"),
                Mapping(ProviderCapabilityKindDto.SecurityReferenceData, "$.issuer", "security.issuer", "trim"),
                Mapping(ProviderCapabilityKindDto.SecurityReferenceData, "$.description", "security.name", "trim", required: true),
                Mapping(ProviderCapabilityKindDto.SecurityReferenceData, "$.coupon", "security.coupon", "decimal"),
                Mapping(ProviderCapabilityKindDto.SecurityReferenceData, "$.maturity_date", "security.maturityDate", "date", required: true),
                Mapping(ProviderCapabilityKindDto.SecurityReferenceData, "$.currency", "security.currency", "uppercase", required: true, defaultValue: "USD"),
                Mapping(ProviderCapabilityKindDto.SecurityReferenceData, "$.rating", "security.rating", "uppercase"),
                Mapping(ProviderCapabilityKindDto.SecurityReferenceData, "$.sector", "security.sector", "trim"),
                Mapping(ProviderCapabilityKindDto.SecurityReferenceData, "$.naic_designation", "security.naicDesignation", "trim")
            ],
            ManualSync(),
            [
                RequiredRule(ProviderCapabilityKindDto.SecurityReferenceData, "security.cusip", "security.name", "security.maturityDate", "security.currency")
            ],
            ManualActivationPolicy(),
            ProviderIntegrationActivationStateDto.Draft,
            SeedAuthor,
            SeededAt,
            null,
            null,
            "File template for fixed income security master records with identifier, economics, rating, sector, and NAIC fields.");

    private static ProviderCapabilityDto Capability(
        ProviderCapabilityKindDto capability,
        params string[] requiredCanonicalFields)
        => new(
            capability,
            Enabled: true,
            RequiresCertifiedAdapter: false,
            RequiredCanonicalFields: requiredCanonicalFields);

    private static EndpointDefinitionDto Endpoint(
        string endpointKey,
        ProviderCapabilityKindDto capability,
        string path,
        EndpointResponseShapeDto response,
        EndpointDependencyDto? dependsOn = null)
        => new(
            endpointKey,
            capability,
            ProviderIntegrationHttpMethodDto.Get,
            path,
            Headers: new Dictionary<string, string>(),
            Query: new Dictionary<string, string>(),
            RequestBodyTemplate: null,
            dependsOn,
            new EndpointPaginationDto(
                ProviderIntegrationPaginationTypeDto.Cursor,
                CursorPath: "$.nextCursor",
                CursorParam: "cursor",
                NextUrlPath: null,
                PageSize: 500),
            response);

    private static FieldMappingDto Mapping(
        ProviderCapabilityKindDto capability,
        string sourcePath,
        string targetField,
        string? transform = null,
        bool required = false,
        string? defaultValue = null,
        string? constantValue = null,
        IReadOnlyDictionary<string, string>? parameters = null)
        => new(
            capability,
            sourcePath,
            targetField,
            string.IsNullOrWhiteSpace(transform)
                ? null
                : new TransformRuleDto(transform, parameters ?? new Dictionary<string, string>()),
            required,
            ProviderMappingConfidenceDto.Approved,
            defaultValue,
            constantValue);

    private static ValidationRuleDto RequiredRule(
        ProviderCapabilityKindDto capability,
        params string[] targetFields)
        => new(
            capability,
            "required-fields",
            ProviderIntegrationIssueSeverityDto.Critical,
            "Required canonical fields must be mapped before activation.",
            targetFields);

    private static ProviderIntegrationAuthConfigDto NoAuth()
        => new(
            ProviderIntegrationAuthTypeDto.None,
            TokenUrl: null,
            Scopes: [],
            Metadata: new Dictionary<string, string>());

    private static ProviderIntegrationAuthConfigDto OAuthAuth(params string[] scopes)
        => new(
            ProviderIntegrationAuthTypeDto.OAuth2,
            TokenUrl: "https://provider.example.com/oauth/token",
            Scopes: scopes,
            Metadata: new Dictionary<string, string>
            {
                ["credentialFields"] = "clientId,clientSecret"
            });

    private static SyncScheduleDto ManualSync()
        => new(
            "manual",
            "manual",
            Time: null,
            "America/New_York",
            ProviderIntegrationCursorTypeDto.None,
            CursorField: null,
            FullRefreshFrequency: null);

    private static SyncScheduleDto IncrementalSync(string cursorField)
        => new(
            "incremental",
            "daily",
            "06:00",
            "America/New_York",
            ProviderIntegrationCursorTypeDto.Timestamp,
            cursorField,
            "monthly");

    private static ProviderIntegrationActivationPolicyDto ManualActivationPolicy()
        => new(
            RequiresAuthenticationTest: false,
            RequiresEndpointTest: false,
            RequiresDryRun: true,
            RequiresApproval: true,
            ProductionWriteCapabilitiesAllowed: false,
            RequiredIssueCodes: ["provider-manifest.dry-run-required", "provider-manifest.approval-evidence-required"]);

    private static ProviderIntegrationActivationPolicyDto ApiActivationPolicy()
        => new(
            RequiresAuthenticationTest: true,
            RequiresEndpointTest: true,
            RequiresDryRun: true,
            RequiresApproval: true,
            ProductionWriteCapabilitiesAllowed: false,
            RequiredIssueCodes:
            [
                "provider-manifest.endpoint-test-required",
                "provider-manifest.dry-run-required",
                "provider-manifest.approval-evidence-required"
            ]);

    private static IReadOnlyDictionary<string, string> TransactionTypeMap()
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BUY"] = "buy",
            ["SELL"] = "sell",
            ["DIV"] = "dividend",
            ["DIVIDEND"] = "dividend",
            ["INT"] = "interest",
            ["INTEREST"] = "interest",
            ["FEE"] = "fee",
            ["DEP"] = "deposit",
            ["DEPOSIT"] = "deposit",
            ["WD"] = "withdrawal",
            ["WITHDRAWAL"] = "withdrawal",
            ["MATURITY"] = "maturity",
            ["PRIN_PAYDOWN"] = "principalPaydown"
        };
}
