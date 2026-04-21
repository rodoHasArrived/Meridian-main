using Meridian.Application.Config;
using Meridian.Application.Services;
using Meridian.Application.Wizard.Core;

namespace Meridian.Application.Wizard.Steps;

/// <summary>
/// Step 4: Selects the primary real-time data source and configures provider options.
/// Writes <see cref="WizardContext.DataSource"/>.
/// </summary>
public sealed class ConfigureDataSourceStep : IWizardStep
{
    private readonly TextWriter _output;
    private readonly TextReader _input;

    public WizardStepId StepId => WizardStepId.ConfigureDataSource;

    public ConfigureDataSourceStep(TextWriter output, TextReader input)
    {
        _output = output;
        _input = input;
    }

    public async Task<WizardStepResult> ExecuteAsync(WizardContext context, CancellationToken ct)
    {
        _output.WriteLine();
        _output.WriteLine("Step 4: Configure Data Source");
        _output.WriteLine("----------------------------------------");

        var useCase = context.SelectedUseCase ?? UseCase.Development;
        var selection = new DataSourceSelection();

        // For backfill-only, skip real-time provider selection
        if (useCase == UseCase.BackfillOnly)
        {
            _output.WriteLine("\nBackfill mode selected - skipping real-time data source configuration.");
            selection.DataSource = DataSourceKind.IB; // Default; won't be used
            context.DataSource = selection;
            return WizardStepResult.Succeeded();
        }

        var realTimeProviders = context.DetectedProviders
            .Where(p => p.Capabilities.Contains("RealTime"))
            .ToList();

        var configuredRealTime = realTimeProviders.Where(p => p.HasCredentials).ToList();

        _output.WriteLine("\nSelect your primary real-time data source:\n");

        var options = new List<(DataSourceKind Kind, string Name, bool Available)>();
        foreach (var provider in realTimeProviders)
        {
            if (Enum.TryParse<DataSourceKind>(provider.Name, out var kind))
                options.Add((kind, provider.DisplayName, provider.HasCredentials));
        }

        for (var i = 0; i < options.Count; i++)
        {
            var (_, name, available) = options[i];
            var status = available ? "[OK]" : "[--]";
            _output.WriteLine($"  {i + 1}. {status} {name}");
        }

        var defaultChoice = configuredRealTime.Any()
            ? options.FindIndex(o => o.Name == configuredRealTime.First().DisplayName) + 1
            : 1;
        if (defaultChoice < 1)
            defaultChoice = 1;

        var choice = await PromptChoiceAsync("Select data source", 1, options.Count, defaultChoice, ct);
        selection.DataSource = options[choice - 1].Kind;

        switch (selection.DataSource)
        {
            case DataSourceKind.Alpaca:
                selection.Alpaca = await ConfigureAlpacaAsync(ct);
                break;
            case DataSourceKind.Polygon:
                selection.Polygon = await ConfigurePolygonAsync(ct);
                break;
            case DataSourceKind.IB:
                var ibSelection = await ConfigureIBAsync(ct);
                selection.IB = ibSelection.IB;
                selection.IBClientPortal = ibSelection.ClientPortal;
                break;
            case DataSourceKind.Synthetic:
                _output.WriteLine("\n  Synthetic offline dataset selected. No credentials are required.");
                break;
        }

        context.DataSource = selection;
        return WizardStepResult.Succeeded();
    }

    // ── Provider-specific sub-configuration ─────────────────────────────────

    private async Task<AlpacaOptions?> ConfigureAlpacaAsync(CancellationToken ct)
    {
        _output.WriteLine("\n  Alpaca Configuration:");

        var keyId = Environment.GetEnvironmentVariable("ALPACA_KEY_ID") ??
                    Environment.GetEnvironmentVariable("MDC_ALPACA_KEY_ID");
        var secretKey = Environment.GetEnvironmentVariable("ALPACA_SECRET_KEY") ??
                        Environment.GetEnvironmentVariable("MDC_ALPACA_SECRET_KEY");

        if (string.IsNullOrEmpty(keyId))
        {
            _output.WriteLine("\n  No Alpaca credentials found in environment.");
            _output.WriteLine("  You can set them now or configure later in appsettings.json\n");

            keyId = await PromptStringAsync("  Alpaca Key ID (or press Enter to skip)", required: false, ct: ct);
            if (string.IsNullOrEmpty(keyId))
            {
                _output.WriteLine("  Skipping Alpaca configuration. Set ALPACA_KEY_ID environment variable later.");
                return null;
            }
            secretKey = await PromptStringAsync("  Alpaca Secret Key", required: true, ct: ct);
        }
        else
        {
            _output.WriteLine($"  Using credentials from environment (Key ID: {keyId[..Math.Min(8, keyId.Length)]}...)");
        }

        _output.WriteLine("\n  Select data feed:");
        _output.WriteLine("    1. IEX (free, ~10% of trades)");
        _output.WriteLine("    2. SIP (paid, full market data)");
        _output.WriteLine("    3. Delayed SIP (free, 15-minute delay)");

        var feedChoice = await PromptChoiceAsync("  Feed", 1, 3, 1, ct);
        var feed = feedChoice switch { 1 => "iex", 2 => "sip", 3 => "delayed_sip", _ => "iex" };
        var useSandbox = await PromptYesNoAsync("  Use sandbox/paper trading", defaultValue: false, ct: ct);

        return new AlpacaOptions(KeyId: keyId ?? "", SecretKey: secretKey ?? "", Feed: feed, UseSandbox: useSandbox);
    }

    private async Task<PolygonOptions?> ConfigurePolygonAsync(CancellationToken ct)
    {
        _output.WriteLine("\n  Polygon Configuration:");

        var apiKey = Environment.GetEnvironmentVariable("POLYGON_API_KEY") ??
                     Environment.GetEnvironmentVariable("MDC_POLYGON_API_KEY");

        if (string.IsNullOrEmpty(apiKey))
            apiKey = await PromptStringAsync("  Polygon API Key", required: true, ct: ct);
        else
            _output.WriteLine($"  Using API key from environment ({apiKey[..Math.Min(8, apiKey.Length)]}...)");

        var subscribeTrades = await PromptYesNoAsync("  Subscribe to trades", defaultValue: true, ct: ct);
        var subscribeQuotes = await PromptYesNoAsync("  Subscribe to quotes", defaultValue: false, ct: ct);

        return new PolygonOptions(ApiKey: apiKey!, SubscribeTrades: subscribeTrades, SubscribeQuotes: subscribeQuotes);
    }

    private async Task<(IBOptions? IB, IBClientPortalOptions? ClientPortal)> ConfigureIBAsync(CancellationToken ct)
    {
        _output.WriteLine("\n  Interactive Brokers Configuration:");
        _output.WriteLine("  Note: IB requires TWS or IB Gateway to be running.\n");

        var host = await PromptStringAsync("  TWS/Gateway Host", defaultValue: "127.0.0.1", ct: ct);
        var portStr = await PromptStringAsync("  Port (7497=paper, 7496=live)", defaultValue: "7497", ct: ct);
        var port = int.TryParse(portStr, out var p) ? p : 7497;
        var clientIdStr = await PromptStringAsync("  Client ID", defaultValue: "1", ct: ct);
        var clientId = int.TryParse(clientIdStr, out var c) ? c : 1;
        var subscribeDepth = await PromptYesNoAsync("  Subscribe to market depth (Level 2)", defaultValue: true, ct: ct);
        var tickByTick = await PromptYesNoAsync("  Prefer tick-by-tick trade data", defaultValue: true, ct: ct);
        var enableClientPortal = await PromptYesNoAsync("  Configure Client Portal for portfolio import", defaultValue: false, ct: ct);

        IBClientPortalOptions? clientPortal = null;
        if (enableClientPortal)
        {
            var baseUrl = await PromptStringAsync("  Client Portal Base URL", defaultValue: "https://localhost:5000", ct: ct);
            var allowSelfSigned = await PromptYesNoAsync("  Allow self-signed Client Portal certificate", defaultValue: true, ct: ct);
            clientPortal = new IBClientPortalOptions(
                Enabled: true,
                BaseUrl: baseUrl ?? "https://localhost:5000",
                AllowSelfSignedCertificates: allowSelfSigned);
        }

        return (
            new IBOptions(
                Host: host ?? "127.0.0.1",
                Port: port,
                ClientId: clientId,
                UsePaperTrading: port == 7497,
                SubscribeDepth: subscribeDepth,
                TickByTick: tickByTick),
            clientPortal);
    }

    // ── Shared prompt helpers ────────────────────────────────────────────────

    private async Task<int> PromptChoiceAsync(string prompt, int min, int max, int defaultValue, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            _output.Write($"\n{prompt} [{min}-{max}] (default: {defaultValue}): ");
            var input = await Task.Run(() => _input.ReadLine(), ct);
            if (string.IsNullOrWhiteSpace(input))
                return defaultValue;
            if (int.TryParse(input, out var value) && value >= min && value <= max)
                return value;
            _output.WriteLine($"  Please enter a number between {min} and {max}");
        }
    }

    private async Task<bool> PromptYesNoAsync(string prompt, bool defaultValue, CancellationToken ct)
    {
        var defaultText = defaultValue ? "Y/n" : "y/N";
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            _output.Write($"{prompt} [{defaultText}]: ");
            var input = await Task.Run(() => _input.ReadLine(), ct);
            if (string.IsNullOrWhiteSpace(input))
                return defaultValue;
            if (input.Equals("y", StringComparison.OrdinalIgnoreCase) || input.Equals("yes", StringComparison.OrdinalIgnoreCase))
                return true;
            if (input.Equals("n", StringComparison.OrdinalIgnoreCase) || input.Equals("no", StringComparison.OrdinalIgnoreCase))
                return false;
            _output.WriteLine("  Please enter 'y' or 'n'");
        }
    }

    private async Task<string?> PromptStringAsync(string prompt, bool required = false, string? defaultValue = null, CancellationToken ct = default)
    {
        var defaultText = defaultValue != null ? $" (default: {defaultValue})" : "";
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            _output.Write($"{prompt}{defaultText}: ");
            var input = await Task.Run(() => _input.ReadLine(), ct);
            if (string.IsNullOrWhiteSpace(input))
            {
                if (defaultValue != null)
                    return defaultValue;
                if (!required)
                    return null;
                _output.WriteLine("  This field is required");
                continue;
            }
            return input;
        }
    }
}
