using Meridian.Application.Config;
using Meridian.Application.ResultTypes;
using Meridian.Application.Services;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles the --dry-run CLI command.
/// Performs comprehensive validation without starting the collector (QW-93).
/// </summary>
internal sealed class DryRunCommand : ICliCommand
{
    private readonly AppConfig _cfg;
    private readonly ConfigurationService _configService;
    private readonly ILogger _log;

    public DryRunCommand(AppConfig cfg, ConfigurationService configService, ILogger log)
    {
        _cfg = cfg;
        _configService = configService;
        _log = log;
    }

    public bool CanHandle(string[] args)
    {
        return CliArguments.HasFlag(args, "--dry-run");
    }

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        _log.Information("Running in dry-run mode...");
        DryRunService.EnableDryRunMode();

        var options = new DryRunOptions(
            ValidateConfiguration: true,
            ValidateFileSystem: true,
            ValidateConnectivity: !CliArguments.HasFlag(args, "--offline"),
            ValidateProviders: true,
            ValidateSymbols: true,
            ValidateResources: true
        );

        var result = await _configService.DryRunValidationAsync(_cfg, options, ct);
        var dryRunService = new DryRunService();
        var report = dryRunService.GenerateReport(result);
        Console.WriteLine(report);

        return CliResult.FromBool(result.OverallSuccess, ErrorCode.ValidationFailed);
    }
}
