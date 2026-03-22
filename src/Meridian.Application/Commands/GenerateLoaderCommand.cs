using Meridian.Application.ResultTypes;
using Meridian.Storage.Export;
using Serilog;

namespace Meridian.Application.Commands;

/// <summary>
/// Handles --generate-loader CLI command for generating standalone analysis loader scripts
/// without requiring a full export. Useful for sharing data directories with researchers.
/// </summary>
internal sealed class GenerateLoaderCommand : ICliCommand
{
    private readonly string _dataRoot;
    private readonly ILogger _log;

    public GenerateLoaderCommand(string dataRoot, ILogger log)
    {
        _dataRoot = dataRoot;
        _log = log;
    }

    public bool CanHandle(string[] args)
    {
        return args.Any(a => a.Equals("--generate-loader", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        var targetTool = CliArguments.GetValue(args, "--generate-loader") ?? "python";
        var outputDir = CliArguments.GetValue(args, "--output") ?? Path.Combine(_dataRoot, "_loaders");
        var symbolsCsv = CliArguments.GetValue(args, "--symbols");
        var symbols = symbolsCsv?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Console.WriteLine();
        Console.WriteLine($"  Generating {targetTool} loader script...");
        Console.WriteLine($"  Data root:  {_dataRoot}");
        Console.WriteLine($"  Output dir: {outputDir}");
        if (symbols is { Length: > 0 })
            Console.WriteLine($"  Symbols:    {string.Join(", ", symbols)}");

        try
        {
            var exportService = new AnalysisExportService(_dataRoot);
            var scriptPath = await exportService.GenerateStandaloneLoaderAsync(
                outputDir, targetTool, symbols, ct);

            if (string.IsNullOrEmpty(scriptPath))
            {
                Console.Error.WriteLine($"  No loader generator available for tool: {targetTool}");
                Console.Error.WriteLine("  Supported tools: python, r, pyarrow, postgresql, runmat");
                return CliResult.Fail(ErrorCode.ValidationFailed);
            }

            Console.WriteLine();
            Console.WriteLine("  Generated files:");
            foreach (var file in Directory.GetFiles(outputDir))
            {
                var fileInfo = new FileInfo(file);
                Console.WriteLine($"    {Path.GetFileName(file),-30} ({fileInfo.Length:N0} bytes)");
            }
            Console.WriteLine();
            Console.WriteLine($"  Loader script ready at: {scriptPath}");
            Console.WriteLine();

            _log.Information("Generated {Tool} loader script at {Path}", targetTool, scriptPath);
            return CliResult.Ok();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to generate loader script");
            Console.Error.WriteLine($"  Error: {ex.Message}");
            return CliResult.Fail(ErrorCode.StorageError);
        }
    }
}
