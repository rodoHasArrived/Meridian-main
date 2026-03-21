using Meridian.Application.Services;
using Meridian.Application.Wizard.Core;

namespace Meridian.Application.Wizard.Steps;

/// <summary>
/// Step 3: Asks the user to select a use case and writes it to
/// <see cref="WizardContext.SelectedUseCase"/>.
/// </summary>
public sealed class SelectUseCaseStep : IWizardStep
{
    private readonly TextWriter _output;
    private readonly TextReader _input;

    public WizardStepId StepId => WizardStepId.SelectUseCase;

    public SelectUseCaseStep(TextWriter output, TextReader input)
    {
        _output = output;
        _input = input;
    }

    public async Task<WizardStepResult> ExecuteAsync(WizardContext context, CancellationToken ct)
    {
        _output.WriteLine();
        _output.WriteLine("Step 3: Select Your Use Case");
        _output.WriteLine("----------------------------------------");
        _output.WriteLine("\nHow will you use Meridian?\n");
        _output.WriteLine("  1. Development/Testing - Local development with sample data");
        _output.WriteLine("  2. Research - Historical data analysis and backtesting");
        _output.WriteLine("  3. Real-Time Trading - Live market data streaming");
        _output.WriteLine("  4. Backfill Only - Historical data collection only");
        _output.WriteLine("  5. Production - Full production deployment");

        var choice = await PromptChoiceAsync("Select option", 1, 5, defaultValue: 1, ct: ct);

        context.SelectedUseCase = choice switch
        {
            1 => UseCase.Development,
            2 => UseCase.Research,
            3 => UseCase.RealTimeTrading,
            4 => UseCase.BackfillOnly,
            5 => UseCase.Production,
            _ => UseCase.Development
        };

        return WizardStepResult.Succeeded();
    }

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
}
