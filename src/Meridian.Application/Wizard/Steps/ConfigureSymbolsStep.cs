using Meridian.Application.Config;
using Meridian.Application.Services;
using Meridian.Application.Wizard.Core;

namespace Meridian.Application.Wizard.Steps;

/// <summary>
/// Step 6: Lets the user pick a symbol preset or enter custom symbols.
/// Writes <see cref="WizardContext.Symbols"/>.
/// </summary>
public sealed class ConfigureSymbolsStep : IWizardStep
{
    private readonly TextWriter _output;
    private readonly TextReader _input;

    public WizardStepId StepId => WizardStepId.ConfigureSymbols;

    public ConfigureSymbolsStep(TextWriter output, TextReader input)
    {
        _output = output;
        _input = input;
    }

    public async Task<WizardStepResult> ExecuteAsync(WizardContext context, CancellationToken ct)
    {
        _output.WriteLine();
        _output.WriteLine("Step 6: Configure Symbols");
        _output.WriteLine("----------------------------------------");
        _output.WriteLine("\nSelect symbol preset or enter custom symbols:\n");
        _output.WriteLine("  1. US Major Indices (SPY, QQQ, DIA, IWM)");
        _output.WriteLine("  2. Tech Giants (AAPL, MSFT, GOOGL, AMZN, META, NVDA)");
        _output.WriteLine("  3. S&P 500 Top 20");
        _output.WriteLine("  4. Crypto (BTC/USD, ETH/USD, SOL/USD)");
        _output.WriteLine("  5. Custom symbols");

        var choice = await PromptChoiceAsync("Select preset", 1, 5, 1, ct);

        SymbolConfig[] symbols;
        if (choice == 5)
        {
            symbols = await ConfigureCustomSymbolsAsync(ct);
        }
        else
        {
            var preset = choice switch
            {
                1 => SymbolPreset.USMajorIndices,
                2 => SymbolPreset.TechGiants,
                3 => SymbolPreset.SP500Top20,
                4 => SymbolPreset.Crypto,
                _ => SymbolPreset.USMajorIndices
            };
            symbols = GetPresetSymbols(preset);

            _output.WriteLine($"\nSelected {symbols.Length} symbols:");
            _output.WriteLine($"  {string.Join(", ", symbols.Take(10).Select(s => s.Symbol))}");
            if (symbols.Length > 10)
                _output.WriteLine($"  ... and {symbols.Length - 10} more");
        }

        var useCase = context.SelectedUseCase ?? UseCase.Development;
        if (useCase != UseCase.BackfillOnly)
        {
            var subscribeDepth = await PromptYesNoAsync(
                "\nSubscribe to market depth for these symbols", defaultValue: false, ct: ct);

            if (subscribeDepth)
            {
                var depthStr = await PromptStringAsync("Depth levels (1-50)", defaultValue: "10", ct: ct);
                var depthLevels = int.TryParse(depthStr, out var d) ? Math.Clamp(d, 1, 50) : 10;
                symbols = symbols.Select(s => s with { SubscribeDepth = true, DepthLevels = depthLevels }).ToArray();
            }
        }

        context.Symbols = symbols;
        return WizardStepResult.Succeeded();
    }

    // ── Preset symbol lists ──────────────────────────────────────────────────

    private static SymbolConfig[] GetPresetSymbols(SymbolPreset preset) => preset switch
    {
        SymbolPreset.USMajorIndices => new[]
        {
            new SymbolConfig("SPY", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("QQQ", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("DIA", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("IWM", SubscribeTrades: true, SubscribeDepth: false)
        },
        SymbolPreset.TechGiants => new[]
        {
            new SymbolConfig("AAPL", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("MSFT", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("GOOGL", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("AMZN", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("META", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("NVDA", SubscribeTrades: true, SubscribeDepth: false)
        },
        SymbolPreset.SP500Top20 => new[]
        {
            new SymbolConfig("AAPL", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("MSFT", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("GOOGL", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("AMZN", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("NVDA", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("META", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("BRK.B", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("TSLA", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("UNH", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("JNJ", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("JPM", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("V", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("PG", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("MA", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("HD", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("CVX", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("MRK", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("ABBV", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("LLY", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("PEP", SubscribeTrades: true, SubscribeDepth: false)
        },
        SymbolPreset.Crypto => new[]
        {
            new SymbolConfig("BTC/USD", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("ETH/USD", SubscribeTrades: true, SubscribeDepth: false),
            new SymbolConfig("SOL/USD", SubscribeTrades: true, SubscribeDepth: false)
        },
        _ => new[] { new SymbolConfig("SPY", SubscribeTrades: true, SubscribeDepth: false) }
    };

    // ── Custom symbols path ──────────────────────────────────────────────────

    private async Task<SymbolConfig[]> ConfigureCustomSymbolsAsync(CancellationToken ct)
    {
        _output.WriteLine("\n  Enter symbols separated by commas (e.g., SPY,AAPL,MSFT):");
        var input = await PromptStringAsync("Symbols", required: true, ct: ct);

        var symbolNames = input?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? new[] { "SPY" };

        var subscribeTrades = await PromptYesNoAsync("Subscribe to trades", defaultValue: true, ct: ct);
        var subscribeDepth = await PromptYesNoAsync("Subscribe to market depth", defaultValue: false, ct: ct);

        var depthLevels = 10;
        if (subscribeDepth)
        {
            var depthStr = await PromptStringAsync("Depth levels (1-50)", defaultValue: "10", ct: ct);
            depthLevels = int.TryParse(depthStr, out var d) ? Math.Clamp(d, 1, 50) : 10;
        }

        return symbolNames.Select(s => new SymbolConfig(
            Symbol: s.ToUpperInvariant(),
            SubscribeTrades: subscribeTrades,
            SubscribeDepth: subscribeDepth,
            DepthLevels: depthLevels
        )).ToArray();
    }

    // ── Prompt helpers ───────────────────────────────────────────────────────

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
