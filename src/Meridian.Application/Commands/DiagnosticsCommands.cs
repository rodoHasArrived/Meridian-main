using Meridian.Core.Config;
using Meridian.Core.Logging;
using Meridian.Platform.Results;
using Meridian.Platform.Runtime;
using Meridian.Application.Services;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles diagnostics-related CLI commands:
/// --diagnostics, --quick-check, --test-connectivity, --error-codes, --show-config, --validate-credentials
/// </summary>
internal sealed class DiagnosticsCommands : ICliCommand
{
    private readonly AppConfig _cfg;
    private readonly string _cfgPath;
    private readonly ConfigurationService _configService;
    private readonly ILogger _log;
    private readonly CliCommandRouteTable _routes;

    public DiagnosticsCommands(AppConfig cfg, string cfgPath, ConfigurationService configService, ILogger log)
    {
        _cfg = cfg;
        _cfgPath = cfgPath;
        _configService = configService;
        _log = log;
        _routes = new CliCommandRouteTable(
            CliCommandRoute.Flag("--diagnostics", RunDiagnostics),
            CliCommandRoute.Flag("--quick-check", RunQuickCheck),
            CliCommandRoute.Flag("--test-connectivity", RunTestConnectivityAsync),
            CliCommandRoute.Flag("--error-codes", _ => RunErrorCodes()),
            CliCommandRoute.Flag("--show-config", RunShowConfig),
            CliCommandRoute.Flag("--validate-credentials", RunValidateCredentialsAsync));
    }

    public IReadOnlyList<string> Triggers { get; } = ["--diagnostics", "--quick-check", "--test-connectivity", "--error-codes", "--show-config", "--validate-credentials"];

    public bool CanHandle(string[] args) => _routes.CanHandle(args);

    public Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
        => _routes.ExecuteAsync(args, ct);

    private CliResult RunDiagnostics(string[] args)
    {
        _log.Information("Running configuration diagnostics...");
        _configService.DisplayConfigSummary(_cfg, _cfgPath, args);
        var result = _configService.PerformQuickCheck(_cfg);
        var summary = new StartupSummary();
        summary.DisplayQuickCheck(result);
        return CliResult.FromBool(result.Success, ErrorCode.ConfigurationInvalid);
    }

    private CliResult RunQuickCheck(string[] args)
    {
        _log.Information("Running quick configuration check...");
        var result = _configService.PerformQuickCheck(_cfg);
        var summary = new StartupSummary();
        summary.DisplayQuickCheck(result);
        return CliResult.FromBool(result.Success, ErrorCode.ConfigurationInvalid);
    }

    private async Task<CliResult> RunTestConnectivityAsync(string[] args, CancellationToken ct)
    {
        _log.Information("Testing provider connectivity...");
        var result = await _configService.TestConnectivityAsync(_cfg, ct);
        await using var tester = new ConnectivityTestService();
        tester.DisplaySummary(result);
        return CliResult.FromBool(result.AllReachable, ErrorCode.ConnectionFailed);
    }

    private static CliResult RunErrorCodes()
    {
        FriendlyErrorFormatter.DisplayErrorCodeReference();
        return CliResult.Ok();
    }

    private CliResult RunShowConfig(string[] args)
    {
        _configService.DisplayConfigSummary(_cfg, _cfgPath, args);
        return CliResult.Ok();
    }

    private async Task<CliResult> RunValidateCredentialsAsync(string[] args, CancellationToken ct)
    {
        _log.Information("Validating API credentials...");
        var validationResult = await _configService.ValidateCredentialsAsync(_cfg, ct);
        await using var validationService = new CredentialValidationService();
        validationService.PrintSummary(validationResult);
        return CliResult.FromBool(validationResult.AllValid, ErrorCode.CredentialsInvalid);
    }
}
