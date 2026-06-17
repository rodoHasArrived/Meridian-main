using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.ProviderSdk;

namespace Meridian.DataIntegration.Credentials;

public interface IProviderSetupHandler
{
    ProviderSetupDescriptor Descriptor { get; }
    bool CanHandle(string providerIdOrAlias);
    ProviderSetupValidationResult Validate(ProviderSetupContext context);
    ProviderSetupExecutionResult BuildExecution(ProviderSetupContext context, ProviderCredentialStoreStatus credentialStatus);
}

public sealed record ProviderSetupContext(
    string ProviderIdOrAlias,
    string DisplayName,
    IReadOnlyList<string> Capabilities,
    string? Environment,
    string? ApiKey = null,
    string? ApiSecret = null,
    string? Endpoint = null,
    bool LiveAcknowledged = false);

public sealed record ProviderSetupDescriptor(
    string ProviderId,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<ProviderCredentialFieldMetadataDto> AcceptedCredentialFields,
    IReadOnlyList<ProviderEnvironmentOptionDto> EnvironmentOptions,
    bool SupportsVerification,
    ProviderConnectionMode DefaultRoutingMode,
    bool EnableBindingsImmediately,
    bool CredentialOnly = false,
    DataSourceKind? DataSourceKind = null,
    IReadOnlyList<string>? SetupWarnings = null);

public sealed record ProviderSetupValidationResult(
    bool Success,
    string? Error = null,
    IReadOnlyDictionary<string, string?>? Credentials = null,
    string? NormalizedEnvironment = null,
    IReadOnlyList<string>? Warnings = null)
{
    public static ProviderSetupValidationResult Failure(string error) => new(false, error);
}

public sealed record ProviderSetupExecutionResult(
    DataSourceKind? DataSourceKind,
    ProviderConnectionMode ConnectionMode,
    bool EnableBindings,
    bool CredentialOnly,
    IReadOnlyList<string> Warnings);

public interface IProviderSetupRegistry
{
    IReadOnlyList<IProviderSetupHandler> Handlers { get; }
    IProviderSetupHandler? Find(string providerIdOrAlias);
}

public sealed class ProviderSetupRegistry : IProviderSetupRegistry
{
    private readonly IReadOnlyList<IProviderSetupHandler> _handlers;

    public ProviderSetupRegistry(IEnumerable<IProviderSetupHandler> handlers)
    {
        _handlers = handlers.ToArray();
    }

    public IReadOnlyList<IProviderSetupHandler> Handlers => _handlers;

    public IProviderSetupHandler? Find(string providerIdOrAlias)
        => _handlers.FirstOrDefault(handler => handler.CanHandle(providerIdOrAlias));
}

public abstract class ProviderSetupHandlerBase : IProviderSetupHandler
{
    private readonly ProviderCredentialCatalogEntry _catalogEntry;

    protected ProviderSetupHandlerBase(
        string providerId,
        IReadOnlyList<string> aliases,
        bool supportsVerification,
        ProviderConnectionMode defaultRoutingMode,
        bool enableBindingsImmediately,
        bool credentialOnly = false,
        DataSourceKind? dataSourceKind = null,
        IReadOnlyList<string>? setupWarnings = null)
    {
        _catalogEntry = ProviderCredentialCatalog.Find(providerId)
            ?? throw new InvalidOperationException($"Provider '{providerId}' is not in the credential catalog.");
        Descriptor = new ProviderSetupDescriptor(
            _catalogEntry.ProviderId,
            aliases,
            ProviderCredentialCatalog.BuildCredentialFields(_catalogEntry),
            ProviderCredentialCatalog.BuildEnvironmentOptions(_catalogEntry),
            supportsVerification,
            defaultRoutingMode,
            enableBindingsImmediately,
            credentialOnly,
            dataSourceKind,
            setupWarnings ?? []);
    }

    public ProviderSetupDescriptor Descriptor { get; }

    public bool CanHandle(string providerIdOrAlias)
    {
        var normalized = NormalizeAlias(providerIdOrAlias);
        return string.Equals(normalized, NormalizeAlias(Descriptor.ProviderId), StringComparison.OrdinalIgnoreCase) ||
               Descriptor.Aliases.Any(alias => string.Equals(normalized, NormalizeAlias(alias), StringComparison.OrdinalIgnoreCase)) ||
               string.Equals(ProviderCredentialCatalog.NormalizeProviderId(providerIdOrAlias), Descriptor.ProviderId, StringComparison.OrdinalIgnoreCase);
    }

    public virtual ProviderSetupValidationResult Validate(ProviderSetupContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var credentials = BuildCredentialMap(context);
        var environment = _catalogEntry.NormalizeEnvironment(context.Environment);
        return new ProviderSetupValidationResult(true, Credentials: credentials, NormalizedEnvironment: environment, Warnings: []);
    }

    public virtual ProviderSetupExecutionResult BuildExecution(ProviderSetupContext context, ProviderCredentialStoreStatus credentialStatus)
    {
        var mode = ResolveConnectionMode(credentialStatus.Environment, context.Capabilities, Descriptor.DefaultRoutingMode);
        return new ProviderSetupExecutionResult(Descriptor.DataSourceKind, mode, Descriptor.EnableBindingsImmediately, Descriptor.CredentialOnly, Descriptor.SetupWarnings);
    }

    protected virtual IReadOnlyDictionary<string, string?> BuildCredentialMap(ProviderSetupContext context)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        AddIfPresent(values, 0, context.ApiKey);
        AddIfPresent(values, 1, context.ApiSecret);
        return values;
    }

    protected ProviderCredentialCatalogEntry CatalogEntry => _catalogEntry;

    protected static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    protected void AddIfPresent(IDictionary<string, string?> values, int fieldIndex, string? value)
    {
        var trimmed = NullIfBlank(value);
        if (trimmed is not null && CatalogEntry.RequiredFields.Count > fieldIndex)
        {
            values[CatalogEntry.RequiredFields[fieldIndex].Name] = trimmed;
        }
    }

    private static ProviderConnectionMode ResolveConnectionMode(string? environment, IReadOnlyList<string> capabilities, ProviderConnectionMode fallback)
    {
        if (environment is not null)
        {
            if (environment.Equals("live", StringComparison.OrdinalIgnoreCase) || environment.Equals("production", StringComparison.OrdinalIgnoreCase))
            {
                return ProviderConnectionMode.Live;
            }

            if (environment.Equals("paper", StringComparison.OrdinalIgnoreCase) || environment.Equals("sandbox", StringComparison.OrdinalIgnoreCase))
            {
                return ProviderConnectionMode.Paper;
            }
        }

        return capabilities.Count > 0 ? ProviderConnectionMode.ReadOnly : fallback;
    }

    private static string NormalizeAlias(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
}

public sealed class AlpacaProviderSetupHandler : ProviderSetupHandlerBase
{
    public AlpacaProviderSetupHandler()
        : base("alpaca", ["alpaca-api"], true, ProviderConnectionMode.Paper, false, dataSourceKind: DataSourceKind.Alpaca)
    {
    }

    protected override IReadOnlyDictionary<string, string?> BuildCredentialMap(ProviderSetupContext context)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var apiKey = NullIfBlank(context.ApiKey);
        var apiSecret = NullIfBlank(context.ApiSecret);
        if (apiKey is not null) values["KeyId"] = apiKey;
        if (apiSecret is not null) values["SecretKey"] = apiSecret;
        return values;
    }

    public override ProviderSetupExecutionResult BuildExecution(ProviderSetupContext context, ProviderCredentialStoreStatus credentialStatus)
    {
        var warnings = Descriptor.SetupWarnings.ToList();
        if (credentialStatus.Environment?.Equals("live", StringComparison.OrdinalIgnoreCase) == true)
        {
            warnings.Add("Alpaca is configured for a live environment; keep live trading and production routing gated until verification and certification pass.");
        }

        var baseResult = base.BuildExecution(context, credentialStatus);
        return baseResult with { EnableBindings = credentialStatus.VerificationState == ProviderVerificationStateDto.Verified, Warnings = warnings };
    }
}

public sealed class PolygonProviderSetupHandler : ProviderSetupHandlerBase
{
    public PolygonProviderSetupHandler()
        : base("polygon", ["polygonio", "polygon-io"], true, ProviderConnectionMode.ReadOnly, false, dataSourceKind: DataSourceKind.Polygon)
    {
    }
}

public sealed class PlaidProviderSetupHandler : ProviderSetupHandlerBase
{
    public PlaidProviderSetupHandler()
        : base("plaid", ["plaidapi", "plaid-api"], true, ProviderConnectionMode.ReadOnly, false, credentialOnly: true)
    {
    }
}

public sealed class QuickBooksProviderSetupHandler : ProviderSetupHandlerBase
{
    public QuickBooksProviderSetupHandler()
        : base("quickbooks", ["qbo", "quickbooks-online"], true, ProviderConnectionMode.ReadOnly, false, credentialOnly: true)
    {
    }
}

public sealed class GenericReadOnlyDataProviderSetupHandler : ProviderSetupHandlerBase
{
    public GenericReadOnlyDataProviderSetupHandler(string providerId, DataSourceKind? dataSourceKind = null, IReadOnlyList<string>? aliases = null)
        : base(providerId, aliases ?? [], false, ProviderConnectionMode.ReadOnly, true, dataSourceKind: dataSourceKind)
    {
    }
}

public static class DefaultProviderSetupHandlers
{
    public static IReadOnlyList<IProviderSetupHandler> Create()
        =>
        [
            new AlpacaProviderSetupHandler(),
            new PolygonProviderSetupHandler(),
            new PlaidProviderSetupHandler(),
            new QuickBooksProviderSetupHandler(),
            new GenericReadOnlyDataProviderSetupHandler("yahoo", DataSourceKind.Yahoo, ["yahoo-finance", "yahoofinance"]),
            new GenericReadOnlyDataProviderSetupHandler("synthetic", DataSourceKind.Synthetic, ["custom"])
        ];
}
