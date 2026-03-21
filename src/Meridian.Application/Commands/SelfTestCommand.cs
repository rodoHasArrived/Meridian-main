using Meridian.Application.ResultTypes;
using Meridian.Application.Testing;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles the --selftest CLI command.
/// Runs built-in self-tests (depth buffer, etc.) and reports results.
/// </summary>
internal sealed class SelfTestCommand : ICliCommand
{
    private readonly ILogger _log;

    public SelfTestCommand(ILogger log)
    {
        _log = log;
    }

    public bool CanHandle(string[] args)
    {
        return CliArguments.HasFlag(args, "--selftest");
    }

    public Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        _log.Information("Running self-tests...");

        try
        {
            DepthBufferSelfTests.Run();
            _log.Information("Self-tests passed");
            Console.WriteLine("Self-tests passed.");
            return Task.FromResult(CliResult.Ok());
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Self-tests failed");
            Console.Error.WriteLine($"Self-tests failed: {ex.Message}");
            return Task.FromResult(CliResult.Fail(ErrorCode.InternalError));
        }
    }
}
