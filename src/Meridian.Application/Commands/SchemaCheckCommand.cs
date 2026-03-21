using Meridian.Application.Config;
using Meridian.Application.Monitoring;
using Meridian.Application.ResultTypes;
using Meridian.Application.Services;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles the --check-schemas CLI command.
/// Verifies stored data schema compatibility.
/// </summary>
internal sealed class SchemaCheckCommand : ICliCommand
{
    private readonly AppConfig _cfg;
    private readonly ILogger _log;

    public SchemaCheckCommand(AppConfig cfg, ILogger log)
    {
        _cfg = cfg;
        _log = log;
    }

    public bool CanHandle(string[] args)
    {
        return CliArguments.HasFlag(args, "--check-schemas");
    }

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        _log.Information("Checking stored data schema compatibility...");

        var schemaOptions = new SchemaValidationOptions
        {
            EnableVersionTracking = true,
            MaxFilesToCheck = CliArguments.GetInt(args, "--max-files", 100),
            FailOnFirstIncompatibility = CliArguments.HasFlag(args, "--fail-fast")
        };

        await using var schemaService = new SchemaValidationService(schemaOptions, _cfg.DataRoot);
        var result = await schemaService.PerformStartupCheckAsync(ct);

        Console.WriteLine();
        if (result.Success)
        {
            Console.WriteLine("Schema Compatibility Check: PASSED");
            Console.WriteLine(new string('=', 50));
            Console.WriteLine($"  {result.Message}");
            Console.WriteLine($"  Current schema version: {SchemaValidationService.CurrentSchemaVersion}");
        }
        else
        {
            Console.WriteLine("Schema Compatibility Check: ISSUES FOUND");
            Console.WriteLine(new string('=', 50));
            Console.WriteLine($"  {result.Message}");
            Console.WriteLine();
            Console.WriteLine("  Incompatible files:");
            foreach (var incompat in result.Incompatibilities.Take(10))
            {
                var migratable = incompat.CanMigrate ? " (can migrate)" : "";
                Console.WriteLine($"    - {incompat.FilePath}");
                Console.WriteLine($"      Version: {incompat.DetectedVersion} (expected {incompat.ExpectedVersion}){migratable}");
            }
            if (result.Incompatibilities.Length > 10)
            {
                Console.WriteLine($"    ... and {result.Incompatibilities.Length - 10} more");
            }
        }
        Console.WriteLine();

        return CliResult.FromBool(result.Success, ErrorCode.SchemaMismatch);
    }

}
