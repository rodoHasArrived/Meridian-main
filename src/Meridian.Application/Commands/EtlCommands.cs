using Meridian.Application.Composition;
using Meridian.Platform.Results;
using Meridian.Contracts.Etl;
using Meridian.DataIntegration.Etl;
using Serilog;

namespace Meridian.Application.Commands;

internal sealed class EtlCommands : ICliCommand
{
    private readonly string _configPath;
    private readonly ILogger _log;

    public EtlCommands(string configPath, ILogger log)
    {
        _configPath = configPath;
        _log = log;
    }

    public bool CanHandle(string[] args)
        => CliArguments.HasFlag(args, "--etl-import") || CliArguments.HasFlag(args, "--etl-export") || CliArguments.HasFlag(args, "--etl-roundtrip") || CliArguments.HasFlag(args, "--etl-resume");

    public async Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
    {
        await using var startup = HostStartup.CreateDefault(_configPath);
        var svc = startup.GetRequiredService<IEtlJobService>();

        if (CliArguments.HasFlag(args, "--etl-resume"))
        {
            var jobId = CliArguments.RequireValue(args, "--etl-resume", "--etl-resume <job-id>");
            if (jobId is null)
                return CliResult.Fail(ErrorCode.RequiredFieldMissing);
            var result = await svc.RunAsync(jobId, ct).ConfigureAwait(false);
            return result.Success ? CliResult.Ok() : CliResult.Fail(ErrorCode.Unknown);
        }

        if (!TryBuildDefinition(args, out var definition))
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);

        var job = await svc.CreateJobAsync(definition, ct).ConfigureAwait(false);
        var run = await svc.RunAsync(job.JobId, ct).ConfigureAwait(false);
        if (!run.Success)
        {
            Console.Error.WriteLine($"ETL failed: {string.Join("; ", run.Errors)}");
            return CliResult.Fail(ErrorCode.Unknown);
        }

        Console.WriteLine($"ETL job {job.JobId} completed. Files={run.FilesProcessed}, Records={run.RecordsProcessed}, Accepted={run.RecordsAccepted}, Rejected={run.RecordsRejected}, Deduplicated={run.RecordsDeduplicated}");
        return CliResult.Ok();
    }

    internal static bool TryBuildDefinition(string[] args, out EtlJobDefinition definition)
    {
        definition = null!;
        var sourceKindArg = CliArguments.RequireValue(args, "--etl-source-kind", "--etl-source-kind local|sftp");
        var sourcePath = CliArguments.RequireValue(args, "--etl-source-path", "--etl-source-path <path>");
        if (sourceKindArg is null || sourcePath is null)
            return false;

        var sourceKind = ParseSourceKind(sourceKindArg);
        var flowDirection = CliArguments.HasFlag(args, "--etl-roundtrip") ? EtlFlowDirection.RoundTrip : CliArguments.HasFlag(args, "--etl-export") ? EtlFlowDirection.Export : EtlFlowDirection.Import;
        var destinationKind = ParseDestinationKind(CliArguments.GetValue(args, "--etl-destination-kind") ?? "storage");
        definition = new EtlJobDefinition
        {
            JobId = Guid.NewGuid().ToString(),
            FlowDirection = flowDirection,
            PartnerSchemaId = CliArguments.GetValue(args, "--etl-schema") ?? "partner.trades.csv.v1",
            LogicalSourceName = CliArguments.GetValue(args, "--etl-logical-source") ?? "etl",
            Source = new EtlSourceDefinition
            {
                Kind = sourceKind,
                Location = sourcePath,
                FilePattern = CliArguments.GetValue(args, "--etl-file-pattern"),
                Username = CliArguments.GetValue(args, "--etl-source-username"),
                SecretRef = CliArguments.GetValue(args, "--etl-source-secret-ref"),
                DeleteAfterSuccess = CliArguments.HasFlag(args, "--etl-delete-source")
            },
            Destination = new EtlDestinationDefinition
            {
                Kind = destinationKind,
                Location = CliArguments.GetValue(args, "--etl-destination-path"),
                Username = CliArguments.GetValue(args, "--etl-destination-username"),
                SecretRef = CliArguments.GetValue(args, "--etl-destination-secret-ref"),
                TransferMode = EtlTransferMode.BatchExchange,
                OverwriteIfExists = CliArguments.HasFlag(args, "--etl-overwrite")
            },
            Symbols = CliArguments.GetValue(args, "--etl-symbols")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [],
            EventTypes = CliArguments.GetValue(args, "--etl-events")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [],
            FromDateUtc = DateTime.TryParse(CliArguments.GetValue(args, "--etl-from"), out var from) ? from : null,
            ToDateUtc = DateTime.TryParse(CliArguments.GetValue(args, "--etl-to"), out var to) ? to : null,
            PublishPortablePackage = CliArguments.HasFlag(args, "--etl-publish-package"),
            PublishNormalizedExtract = CliArguments.HasFlag(args, "--etl-publish-normalized"),
            ContinueOnRecordError = CliArguments.HasFlag(args, "--etl-continue-on-error")
        };
        return true;
    }

    private static EtlSourceKind ParseSourceKind(string value)
        => value.Equals("sftp", StringComparison.OrdinalIgnoreCase) ? EtlSourceKind.Sftp : EtlSourceKind.Local;

    private static EtlDestinationKind ParseDestinationKind(string value)
        => value.ToLowerInvariant() switch
        {
            "local" => EtlDestinationKind.Local,
            "sftp" => EtlDestinationKind.Sftp,
            _ => EtlDestinationKind.StorageCatalog
        };
}
