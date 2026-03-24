using Meridian.Contracts.Etl;

namespace Meridian.Infrastructure.Etl;

public sealed class CsvPartnerFileParser : IPartnerFileParser
{
    private readonly IPartnerSchemaRegistry _schemas;

    public CsvPartnerFileParser(IPartnerSchemaRegistry schemas)
    {
        _schemas = schemas;
    }

    public string SchemaId => "partner.trades.csv.v1";

    public bool CanParse(EtlStagedFile file)
        => Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase);

    public async IAsyncEnumerable<PartnerRecordEnvelope> ParseAsync(EtlStagedFile file, EtlCheckpointToken? checkpoint, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var schemaId = SchemaId;
        var schema = _schemas.GetCsvSchema(schemaId);
        using var reader = new StreamReader(file.StagedPath);
        string[]? headers = null;
        long recordIndex = 0;

        if (schema.HasHeaderRow)
        {
            var headerLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (headerLine is null)
                yield break;
            headers = SplitCsvLine(headerLine, schema.Delimiter).ToArray();
        }

        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            recordIndex++;
            if (checkpoint?.CurrentFileChecksum == file.ChecksumSha256 && checkpoint.CurrentRecordIndex.HasValue && recordIndex <= checkpoint.CurrentRecordIndex.Value)
                continue;

            var values = SplitCsvLine(line, schema.Delimiter).ToArray();
            headers ??= schema.Columns.Keys.ToArray();
            var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length && i < values.Length; i++)
            {
                fields[headers[i]] = values[i];
            }

            yield return new PartnerRecordEnvelope
            {
                PartnerSchemaId = schemaId,
                SourceFileName = file.FileName,
                SourceFileChecksum = file.ChecksumSha256,
                RecordIndex = recordIndex,
                Fields = fields,
                RawLine = line
            };
        }
    }

    private static IEnumerable<string> SplitCsvLine(string line, char delimiter)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == delimiter && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        values.Add(current.ToString());
        return values;
    }
}
