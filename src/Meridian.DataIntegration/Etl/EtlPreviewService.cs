using Meridian.Contracts.Etl;
using Meridian.Contracts.Integrity;

namespace Meridian.DataIntegration.Etl;

public sealed class EtlPreviewService
{
    private readonly IEnumerable<IEtlSourceReader> _sourceReaders;
    private readonly IPartnerFileParser _parser;
    private readonly IPartnerSchemaRegistry _schemas;

    public EtlPreviewService(
        IEnumerable<IEtlSourceReader> sourceReaders,
        IPartnerFileParser parser,
        IPartnerSchemaRegistry schemas)
    {
        _sourceReaders = sourceReaders;
        _parser = parser;
        _schemas = schemas;
    }

    public async Task<EtlPreviewResult> PreviewAsync(
        EtlJobDefinition definition,
        int sampleRowLimit = 10,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        sampleRowLimit = Math.Clamp(sampleRowLimit, 1, 100);

        if (!_schemas.IsSupported(definition.PartnerSchemaId))
        {
            return new EtlPreviewResult
            {
                Success = false,
                SourceKind = definition.Source.Kind,
                SchemaId = definition.PartnerSchemaId,
                Errors = [$"Unsupported ETL schema '{definition.PartnerSchemaId}'."]
            };
        }

        var reader = _sourceReaders.FirstOrDefault(candidate => candidate.Kind == definition.Source.Kind);
        if (reader is null)
        {
            return new EtlPreviewResult
            {
                Success = false,
                SourceKind = definition.Source.Kind,
                SchemaId = definition.PartnerSchemaId,
                Errors = [$"No ETL source reader is registered for kind '{definition.Source.Kind}'."]
            };
        }

        var files = await reader.ListFilesAsync(definition.Source, ct).ConfigureAwait(false);
        var previews = new List<EtlFilePreview>();
        var errors = new List<string>();
        var previewJobId = $"preview-{definition.JobId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        foreach (var file in files)
        {
            try
            {
                var staged = await reader.StageFileAsync(previewJobId, definition.Source, file, ct).ConfigureAwait(false);
                previews.Add(await PreviewFileAsync(staged, file, definition.PartnerSchemaId, sampleRowLimit, ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"{file.Name}: {ex.Message}");
                previews.Add(new EtlFilePreview
                {
                    SourceFile = file,
                    SchemaId = definition.PartnerSchemaId,
                    Disposition = EtlPreviewDisposition.Error,
                    Issues = [new EtlFilePreviewIssue { Severity = "Error", Field = "file", Message = ex.Message }]
                });
            }
        }

        return new EtlPreviewResult
        {
            Success = errors.Count == 0 && previews.All(preview => preview.Disposition != EtlPreviewDisposition.Error),
            SourceKind = definition.Source.Kind,
            SchemaId = definition.PartnerSchemaId,
            Files = previews,
            Errors = errors
        };
    }

    private async Task<EtlFilePreview> PreviewFileAsync(
        EtlStagedFile staged,
        EtlRemoteFile sourceFile,
        string schemaId,
        int sampleRowLimit,
        CancellationToken ct)
    {
        var schema = _schemas.GetCsvSchema(schemaId);
        var requiredHeaders = schema.Columns.Keys.ToArray();
        var sampleRows = new List<IReadOnlyDictionary<string, string?>>();
        var issues = new List<EtlFilePreviewIssue>();
        var recordCount = 0;

        await foreach (var row in _parser.ParseAsync(staged, null, schemaId, ct).ConfigureAwait(false))
        {
            recordCount++;
            if (sampleRows.Count < sampleRowLimit)
                sampleRows.Add(row.Fields);
        }

        if (recordCount == 0)
        {
            issues.Add(new EtlFilePreviewIssue { Severity = "Error", Field = "file", Message = "The file did not contain any data rows." });
        }

        if (sampleRows.Count > 0)
        {
            var headers = sampleRows[0].Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var required in requiredHeaders)
            {
                if (!headers.Contains(required))
                {
                    issues.Add(new EtlFilePreviewIssue
                    {
                        Severity = "Error",
                        Field = required,
                        Message = $"Required field '{required}' is missing from the file header.",
                        RowNumber = 1
                    });
                }
            }
        }

        var hash = await ComputeSha256Async(staged.StagedPath, ct).ConfigureAwait(false);
        return new EtlFilePreview
        {
            SourceFile = sourceFile,
            SchemaId = schemaId,
            Disposition = issues.Any(issue => string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase))
                ? EtlPreviewDisposition.Error
                : EtlPreviewDisposition.Ready,
            RecordCount = recordCount,
            SampleRows = sampleRows,
            Issues = issues,
            FileHashSha256 = hash,
            IdempotencyKey = $"etl-preview|{schemaId}|{sourceFile.Name}|{hash}"
        };
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return await Sha256Digest.ComputeAsync(stream, ct).ConfigureAwait(false);
    }
}
