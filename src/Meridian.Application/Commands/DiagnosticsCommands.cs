using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.ResultTypes;
using Meridian.Application.Services;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles diagnostics-related CLI commands:
/// --quick-check, --test-connectivity, --error-codes, --show-config, --validate-credentials
/// </summary>
internal sealed class DiagnosticsCommands : ICliCommand
{
    private readonly AppConfig _cfg;
    private readonly string _cfgPath;
    private readonly ConfigurationService _configService;
    private readonly ILogger _log;

    public DiagnosticsCommands(AppConfig cfg, string cfgPath, ConfigurationService configService, ILogger log)
    {
        _cfg = cfg;
        _cfgPath = cfgPath;
        _configService = configService;
        _log = log;
    }

    public bool CanHandle(string[] args)
    {
        return CliArguments.HasFlag(args, "--quick-check") ||
            CliArguments.HasFlag(args, "--test-connectivity") ||
            CliArguments.HasFlag(args, "--error-codes") ||
            CliArguments.HasFlag(args, "--show-config") ||
            CliArguments.HasFlag(args, "--validate-credentials");
    }

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        if (CliArguments.HasFlag(args, "--quick-check"))
        {
            _log.Information("Running quick configuration check...");
            var result = _configService.PerformQuickCheck(_cfg);
            var summary = new StartupSummary();
            summary.DisplayQuickCheck(result);
            return CliResult.FromBool(result.Success, ErrorCode.ConfigurationInvalid);
        }

        if (CliArguments.HasFlag(args, "--test-connectivity"))
        {
            _log.Information("Testing provider connectivity...");
            var result = await _configService.TestConnectivityAsync(_cfg, ct);
            await using var tester = new ConnectivityTestService();
            tester.DisplaySummary(result);
            return CliResult.FromBool(result.AllReachable, ErrorCode.ConnectionFailed);
        }

        if (CliArguments.HasFlag(args, "--error-codes"))
        {
            FriendlyErrorFormatter.DisplayErrorCodeReference();
            return CliResult.Ok();
        }

        if (CliArguments.HasFlag(args, "--show-config"))
        {
            _configService.DisplayConfigSummary(_cfg, _cfgPath, args);
            return CliResult.Ok();
        }

        if (CliArguments.HasFlag(args, "--validate-credentials"))
        {
            _log.Information("Validating API credentials...");
            var validationResult = await _configService.ValidateCredentialsAsync(_cfg, ct);
            await using var validationService = new CredentialValidationService();
            validationService.PrintSummary(validationResult);
            return CliResult.FromBool(validationResult.AllValid, ErrorCode.CredentialsInvalid);
        }

        return CliResult.Fail(ErrorCode.Unknown);
    }
}
