using Meridian.Core.Config;
using Meridian.Application.Services;
using Meridian.Application.ProviderRouting;
using Meridian.Application.Wizard.Core;
using Meridian.Application.Wizard.Metadata;

namespace Meridian.Application.Wizard.Steps;

/// <summary>
/// Step 5: Validates credentials for the selected data source.
/// Does not modify context state; purely informational.
/// </summary>
public sealed class ValidateCredentialsStep : IWizardStep
{
    private readonly TextWriter _output;
    private readonly TextReader _input;
    private readonly ProviderCredentialOwnershipPolicy _ownershipPolicy;
    private readonly Func<DataSourceSelection, CancellationToken, Task<CredentialValidationService.ValidationResult?>> _validate;

    public WizardStepId StepId => WizardStepId.ValidateCredentials;

    public ValidateCredentialsStep(TextWriter output, TextReader input,
        ProviderCredentialOwnershipPolicy? ownershipPolicy = null)
        : this(output, input, ownershipPolicy, ValidateAsync)
    {
    }

    internal ValidateCredentialsStep(TextWriter output, TextReader input,
        ProviderCredentialOwnershipPolicy? ownershipPolicy,
        Func<DataSourceSelection, CancellationToken, Task<CredentialValidationService.ValidationResult?>> validate)
    {
        _output = output;
        _input = input;
        _ownershipPolicy = ownershipPolicy ?? ProviderCredentialOwnershipPolicy.ForCurrentEnvironment();
        _validate = validate;
    }

    public async Task<WizardStepResult> ExecuteAsync(WizardContext context, CancellationToken ct)
    {
        _output.WriteLine();
        _output.WriteLine("Step 5: Validate Credentials");
        _output.WriteLine("----------------------------------------");

        var dataSource = context.DataSource;
        if (dataSource == null)
            return WizardStepResult.Skipped("No data source configured; skipping validation.");

        ProviderConfigurationCredentialGuard.Prepare(new AppConfig(Alpaca: dataSource.Alpaca, Polygon: dataSource.Polygon), _ownershipPolicy);
        var hasCredentials = dataSource.DataSource switch
        {
            DataSourceKind.Alpaca => !string.IsNullOrWhiteSpace(dataSource.Alpaca?.KeyId) &&
                                      !string.IsNullOrWhiteSpace(dataSource.Alpaca?.SecretKey),
            DataSourceKind.Polygon => !string.IsNullOrWhiteSpace(dataSource.Polygon?.ApiKey),
            DataSourceKind.IB => true,
            _ => true
        };

        if (!hasCredentials)
        {
            _output.WriteLine("  No credentials configured for the selected provider.");
            _output.WriteLine("  You can still save the configuration and add credentials later.");
            _output.WriteLine("  The collector will not be able to connect until credentials are set.\n");
            ShowCredentialHelp(dataSource.DataSource.ToString());
            return WizardStepResult.Succeeded();
        }

        // Skip live validation for IB (needs a local connection)
        if (dataSource.DataSource is DataSourceKind.IB)
        {
            _output.WriteLine($"  {dataSource.DataSource} uses a local connection - skipping API validation.");
            return WizardStepResult.Succeeded();
        }

        // This legacy wizard has no retained tenant/account selection. Even an environment
        // reference must go through scoped setup instead of this provider-wide live validator.
        if (_ownershipPolicy.RequireExplicitOwnership && dataSource.DataSource is DataSourceKind.Alpaca or DataSourceKind.Polygon)
            throw new ProviderConfigurationCredentialException();

        _output.WriteLine("  Validating credentials with provider API...\n");

        try
        {
            var result = await _validate(dataSource, ct);

            if (result == null)
            {
                _output.WriteLine("  Skipped - no validation available for this provider.");
                return WizardStepResult.Succeeded();
            }

            if (result.IsValid)
            {
                _output.WriteLine($"  [OK] {result.Provider}: {result.Message} ({result.ResponseTime.TotalMilliseconds:F0}ms)");
                if (!string.IsNullOrEmpty(result.AccountInfo))
                    _output.WriteLine($"       {result.AccountInfo}");
            }
            else
            {
                _output.WriteLine($"  [FAIL] {result.Provider}: {result.Message}");
                _output.WriteLine("\n  Your credentials appear to be invalid.");

                var retry = await PromptYesNoAsync("  Would you like to re-enter credentials", defaultValue: false, ct: ct);
                if (retry)
                {
                    // Re-run this step after allowing the data-source step to re-capture credentials.
                    // For simplicity, jump back to ConfigureDataSource.
                    return WizardStepResult.JumpTo(WizardStepId.ConfigureDataSource,
                        "Retrying credential entry.");
                }

                _output.WriteLine("\n  Continuing with current credentials. You can fix them later in config/appsettings.json.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _output.WriteLine($"  Could not validate credentials: {ex.Message}");
            _output.WriteLine("  This may be a network issue. Continuing with setup...");
        }

        return WizardStepResult.Succeeded();
    }

    private static async Task<CredentialValidationService.ValidationResult?> ValidateAsync(DataSourceSelection dataSource, CancellationToken ct)
    {
        await using var validator = new CredentialValidationService();
        return dataSource.DataSource switch
        {
            DataSourceKind.Alpaca when dataSource.Alpaca != null => await validator.ValidateAlpacaAsync(dataSource.Alpaca, ct),
            DataSourceKind.Polygon when dataSource.Polygon != null => await validator.ValidatePolygonAsync(dataSource.Polygon, ct),
            _ => null
        };
    }

    private void ShowCredentialHelp(string providerName)
    {
        var descriptor = ProviderRegistry.Get(providerName);
        if (descriptor != null)
        {
            _output.WriteLine($"  To get {descriptor.DisplayName} credentials:");
            _output.WriteLine($"    1. Sign up at: {descriptor.SignupUrl}");
            _output.WriteLine($"    2. Set environment variables before running the collector");
            _output.WriteLine($"    3. Docs: {descriptor.DocsUrl}");
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
}
