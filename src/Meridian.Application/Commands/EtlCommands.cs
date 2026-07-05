using Meridian.Application.Composition;
using Meridian.Platform.Results;
using Meridian.Contracts.Etl;
using Meridian.DataIntegration.Etl;
using Meridian.Infrastructure.Etl.Sftp;
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
        => CliArguments.HasFlag(args, "--etl-import") || CliArguments.HasFlag(args, "--etl-export") || CliArguments.HasFlag(args, "--etl-roundtrip") || CliArguments.HasFlag(args, "--etl-resume") || CliArguments.HasFlag(args, "--etl-preview") || CliArguments.HasFlag(args, "--etl-list-files") || CliArguments.HasFlag(args, "--etl-test-connection");

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

        var inspectionMode = ResolveInspectionMode(args);
        if (inspectionMode == EtlInspectionMode.ListFiles)
        {
            var reader = ResolveSourceReader(startup.GetRequiredService<IEnumerable<IEtlSourceReader>>(), definition.Source.Kind);
            var files = await reader.ListFilesAsync(definition.Source, ct).ConfigureAwait(false);
            foreach (var file in files)
                Console.WriteLine(FormatListedFile(file));
            return CliResult.Ok();
        }

        if (inspectionMode == EtlInspectionMode.TestConnection)
        {
            if (definition.Source.Kind == EtlSourceKind.Sftp)
            {
                var capability = startup.GetRequiredService<ISftpCapabilityService>().Evaluate(definition.Source);
                foreach (var issue in capability.Issues)
                    Console.Error.WriteLine(issue);
                Console.WriteLine(capability.Ready ? "SFTP source configuration is ready." : "SFTP source configuration is not ready.");
                return capability.Ready ? CliResult.Ok() : CliResult.Fail(ErrorCode.ConfigurationInvalid);
            }

            try
            {
                var reader = ResolveSourceReader(startup.GetRequiredService<IEnumerable<IEtlSourceReader>>(), definition.Source.Kind);
                var files = await reader.ListFilesAsync(definition.Source, ct).ConfigureAwait(false);
                Console.WriteLine($"Local source is readable. Files={files.Count}");
                return CliResult.Ok();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine(ex.Message);
                return CliResult.Fail(ErrorCode.ConnectionFailed);
            }
        }

        if (inspectionMode == EtlInspectionMode.Preview)
        {
            var preview = startup.GetRequiredService<EtlPreviewService>();
            var sampleRows = int.TryParse(CliArguments.GetValue(args, "--etl-preview-sample-rows"), out var parsedSampleRows) ? parsedSampleRows : 10;
            var result = await preview.PreviewAsync(definition, sampleRows, ct).ConfigureAwait(false);
            foreach (var file in result.Files)
            {
                Console.WriteLine($"{file.SourceFile.Name}: {file.Disposition}; Records={file.RecordCount}; Hash={file.FileHashSha256}");
                if (!CliArguments.HasFlag(args, "--etl-list-files"))
                {
                    foreach (var issue in file.Issues)
                        Console.WriteLine($"  {issue.Severity}: {issue.Field}: {issue.Message}");
                }
            }
            foreach (var error in result.Errors)
                Console.Error.WriteLine(error);
            return result.Success ? CliResult.Ok() : CliResult.Fail(ErrorCode.Unknown);
        }

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
                HostKeySha256Fingerprint = CliArguments.GetValue(args, "--etl-source-host-key-sha256"),
                DeleteAfterSuccess = CliArguments.HasFlag(args, "--etl-delete-source"),
                PostProcessingAction = ParsePostProcessingAction(CliArguments.GetValue(args, "--etl-source-post-processing")),
                ArchiveLocation = CliArguments.GetValue(args, "--etl-source-archive-path"),
                ErrorLocation = CliArguments.GetValue(args, "--etl-source-error-path")
            },
            Destination = new EtlDestinationDefinition
            {
                Kind = destinationKind,
                Location = CliArguments.GetValue(args, "--etl-destination-path"),
                Username = CliArguments.GetValue(args, "--etl-destination-username"),
                SecretRef = CliArguments.GetValue(args, "--etl-destination-secret-ref"),
                HostKeySha256Fingerprint = CliArguments.GetValue(args, "--etl-destination-host-key-sha256"),
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

    internal static EtlInspectionMode ResolveInspectionMode(string[] args)
    {
        if (CliArguments.HasFlag(args, "--etl-list-files"))
            return EtlInspectionMode.ListFiles;
        if (CliArguments.HasFlag(args, "--etl-test-connection"))
            return EtlInspectionMode.TestConnection;
        if (CliArguments.HasFlag(args, "--etl-preview"))
            return EtlInspectionMode.Preview;
        return EtlInspectionMode.None;
    }

    internal static string FormatListedFile(EtlRemoteFile file)
    {
        var lastModified = file.LastModifiedUtc?.ToString("O") ?? "unknown";
        return $"{file.Name}: Path={file.Path}; Size={file.SizeBytes}; LastModified={lastModified}";
    }

    private static IEtlSourceReader ResolveSourceReader(IEnumerable<IEtlSourceReader> sourceReaders, EtlSourceKind sourceKind)
        => sourceReaders.FirstOrDefault(reader => reader.Kind == sourceKind)
           ?? throw new InvalidOperationException($"No ETL source reader is registered for kind '{sourceKind}'.");

    private static EtlSourceKind ParseSourceKind(string value)
        => value.Equals("sftp", StringComparison.OrdinalIgnoreCase) ? EtlSourceKind.Sftp : EtlSourceKind.Local;

    private static EtlSourcePostProcessingAction ParsePostProcessingAction(string? value)
        => value?.ToLowerInvariant() switch
        {
            "delete" => EtlSourcePostProcessingAction.Delete,
            "archive" or "move-to-archive" => EtlSourcePostProcessingAction.MoveToArchive,
            "error" or "move-to-error" => EtlSourcePostProcessingAction.MoveToError,
            "done" or "write-done-marker" => EtlSourcePostProcessingAction.WriteDoneMarker,
            _ => EtlSourcePostProcessingAction.LeaveInPlace
        };

    private static EtlDestinationKind ParseDestinationKind(string value)
        => value.ToLowerInvariant() switch
        {
            "local" => EtlDestinationKind.Local,
            "sftp" => EtlDestinationKind.Sftp,
            _ => EtlDestinationKind.StorageCatalog
        };
}

internal enum EtlInspectionMode
{
    None,
    Preview,
    ListFiles,
    TestConnection
}
