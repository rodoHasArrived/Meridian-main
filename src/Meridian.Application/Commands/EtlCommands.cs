using Meridian.Application.Composition;
using Meridian.Platform.Results;
using Meridian.Contracts.Etl;
using Meridian.Contracts.Operations;
using Meridian.DataIntegration.Etl;
using Meridian.Infrastructure.Etl.Sftp;
using Serilog;

namespace Meridian.Application.Commands;

internal sealed class EtlCommands : ICliCommand
{
    private readonly string _configPath;
    private readonly ILogger _log;
    private readonly CliCommandRouteTable _routes;

    public EtlCommands(string configPath, ILogger log)
    {
        _configPath = configPath;
        _log = log;
        _routes = new CliCommandRouteTable(
            CliCommandRoute.Flag("--etl-resume", RunResumeAsync),
            CliCommandRoute.Flags(
                [
                    "--etl-import",
                    "--etl-export",
                    "--etl-roundtrip",
                    "--etl-preview",
                    "--etl-list-files",
                    "--etl-test-connection"
                ],
                RunJobAsync));
    }

    public IReadOnlyList<string> Triggers { get; } =
        ["--etl-import", "--etl-export", "--etl-roundtrip", "--etl-resume", "--etl-preview", "--etl-list-files", "--etl-test-connection"];

    public bool CanHandle(string[] args) => _routes.CanHandle(args);

    public Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default)
        => _routes.ExecuteAsync(args, ct);

    private async Task<CliResult> RunResumeAsync(string[] args, CancellationToken ct)
    {
        await using var startup = HostStartup.CreateDefault(_configPath);
        var svc = startup.GetRequiredService<IEtlJobService>();

        var jobId = CliArguments.RequireValue(args, "--etl-resume", "--etl-resume <job-id>");
        if (jobId is null)
            return CliResult.Fail(ErrorCode.RequiredFieldMissing);
        var result = await svc.RunAsync(jobId, ct).ConfigureAwait(false);
        return ReportRunResult(jobId, result);
    }

    private async Task<CliResult> RunJobAsync(string[] args, CancellationToken ct)
    {
        await using var startup = HostStartup.CreateDefault(_configPath);
        var svc = startup.GetRequiredService<IEtlJobService>();
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
        return ReportRunResult(job.JobId, run);
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
            ContinueOnRecordError = CliArguments.HasFlag(args, "--etl-continue-on-error"),
            FailRoundTripOnExportError = !CliArguments.HasFlag(args, "--etl-continue-on-export-error")
        };
        return true;
    }

    internal static CliResult ToCliResult(EtlRunResult result) => result.Outcome.State switch
    {
        OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings => CliResult.Ok(),
        OperationTerminalState.Blocked => CliResult.Fail(ErrorCode.InvalidOperation),
        _ => CliResult.Fail(ErrorCode.Unknown)
    };

    internal static string FormatRunSummary(string jobId, EtlRunResult result) =>
        $"ETL job {jobId} {result.Outcome.State}. Files={result.FilesProcessed}, Records={result.RecordsProcessed}, " +
        $"Accepted={result.RecordsAccepted}, Rejected={result.RecordsRejected}, Deduplicated={result.RecordsDeduplicated}. " +
        $"Operation={result.Outcome.OperationId}.";

    private static CliResult ReportRunResult(string jobId, EtlRunResult result)
    {
        var summary = FormatRunSummary(jobId, result);
        if (result.Outcome.State == OperationTerminalState.Succeeded)
            Console.WriteLine(summary);
        else
            Console.Error.WriteLine(summary);

        foreach (var issue in result.Outcome.Issues)
            Console.Error.WriteLine($"{issue.Severity}: {issue.Code}: {issue.Message}");
        foreach (var recovery in result.Outcome.Recovery)
            Console.Error.WriteLine($"Recovery: {recovery.Guidance}");

        return ToCliResult(result);
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

public enum EtlInspectionMode
{
    None,
    Preview,
    ListFiles,
    TestConnection
}
