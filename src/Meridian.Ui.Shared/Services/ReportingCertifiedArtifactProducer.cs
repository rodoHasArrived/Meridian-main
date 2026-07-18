using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Hash and byte-length declaration embedded in the retained manifest. The manifest entry itself
/// intentionally has no hash/length to avoid a circular self-hash; its exact hash and length are
/// authoritatively carried by the immutable package catalog.
/// </summary>
public sealed record ReportingRetainedManifestArtifactDescriptor(
    string ArtifactId,
    string FileName,
    string ContentType,
    long? ByteLength,
    string? ContentHashSha256);

/// <summary>
/// Canonical manifest bytes retained alongside every governed reporting package. This document is
/// deliberately sufficient to validate release without consulting the mutable orchestration store.
/// </summary>
public sealed record ReportingRetainedManifestDocument(
    string SchemaVersion,
    string RunId,
    string SeriesId,
    string TemplateId,
    VersionedReportTemplateIdDto ResolvedTemplate,
    DateOnly AsOfDate,
    string ParametersCanonicalJson,
    string ParametersHash,
    ReportingOperationalScope Scope,
    ReportingAccessScope Access,
    ReportingCertifiedSnapshotScope Snapshot,
    ReportingAuthoritativeSourceCheckpoint AuthoritativeSource,
    ReportingRunReadinessDto Readiness,
    ImmutableArray<ReportingSectionManifest> Sections,
    int CertifiedDatasetRowCount,
    string CertifiedDatasetHashSha256,
    ImmutableArray<ReportingRetainedManifestArtifactDescriptor> Artifacts);

/// <summary>
/// Production deterministic artifact implementation. Every byte is derived only from the completed
/// certified manifest, uses stable ordering, and is emitted exactly once according to
/// <see cref="ReportingRunParametersDto.OutputFormat"/> and the optional evidence/schedule flags.
/// </summary>
public sealed class DeterministicReportingCertifiedArtifactProducer : IReportingCertifiedArtifactProducer
{
    public const string RetainedManifestSchemaVersion = "meridian.reporting.retained-manifest.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Default,
        WriteIndented = false
    };

    public ValueTask<ReportingGovernedArtifactProduction> ProduceAsync(
        ReportingOutputManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateManifest(manifest);

        var declarations = ReportingArtifactDeclaration.Build(manifest);
        var declaredIds = declarations.Select(static artifact => artifact.ArtifactId).ToArray();
        if (!declaredIds.SequenceEqual(manifest.Artifacts, StringComparer.Ordinal))
        {
            throw new ReportingGovernanceException(
                $"Certified manifest '{manifest.RunId}' artifact declaration drifted before byte production.");
        }

        var rendered = ImmutableArray.CreateBuilder<ReportingRenderedArtifact>(declarations.Length);
        var descriptors = ImmutableArray.CreateBuilder<ReportingRetainedManifestArtifactDescriptor>(declarations.Length);
        var manifestDeclaration = declarations.Single(static artifact =>
            artifact.Kind == ReportingDeclaredArtifactKind.Manifest);

        foreach (var declaration in declarations.Where(static artifact =>
                     artifact.Kind != ReportingDeclaredArtifactKind.Manifest))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = RenderArtifact(manifest, declaration);
            if (content.Length == 0)
            {
                throw new ReportingGovernanceException(
                    $"Certified artifact '{declaration.ArtifactId}' rendered no bytes.");
            }

            rendered.Add(new ReportingRenderedArtifact(
                declaration.ArtifactId,
                declaration.FileName,
                declaration.ContentType,
                content));
            descriptors.Add(new ReportingRetainedManifestArtifactDescriptor(
                declaration.ArtifactId,
                declaration.FileName,
                declaration.ContentType,
                content.LongLength,
                ComputeSha256(content)));
        }

        descriptors.Add(new ReportingRetainedManifestArtifactDescriptor(
            manifestDeclaration.ArtifactId,
            manifestDeclaration.FileName,
            manifestDeclaration.ContentType,
            ByteLength: null,
            ContentHashSha256: null));

        var retainedDocument = new ReportingRetainedManifestDocument(
            RetainedManifestSchemaVersion,
            manifest.RunId,
            NormalizeOptional(manifest.RunSeriesId) ?? manifest.RunId,
            manifest.TemplateId,
            manifest.ResolvedTemplate!,
            manifest.AsOfDate,
            manifest.CertifiedSnapshot!.ParametersCanonicalJson!,
            manifest.CertifiedSnapshot.ParametersHash!,
            manifest.OperationalScope!,
            manifest.ImmutableAccessScope!,
            manifest.CertifiedSnapshot,
            manifest.AuthoritativeSource!,
            manifest.Readiness!,
            manifest.Sections,
            manifest.CertifiedDatasetRows.Length,
            ComputeCertifiedRowsHash(manifest.CertifiedDatasetRows),
            descriptors
                .OrderBy(static descriptor => descriptor.ArtifactId, StringComparer.Ordinal)
                .ToImmutableArray());
        var retainedManifestBytes = SerializeCanonical(retainedDocument);
        rendered.Add(new ReportingRenderedArtifact(
            manifestDeclaration.ArtifactId,
            manifestDeclaration.FileName,
            manifestDeclaration.ContentType,
            retainedManifestBytes));

        var orderedArtifacts = rendered
            .OrderBy(artifact => Array.IndexOf(declaredIds, artifact.ArtifactId))
            .ToImmutableArray();
        var source = manifest.AuthoritativeSource;
        var certification = new ReportingAuthoritativeAsOfCertification(
            manifest.AsOfDate,
            manifest.OperationalScope!,
            manifest.ImmutableAccessScope!,
            manifest.CertifiedSnapshot!,
            source!.CheckpointId,
            source.CheckpointHash,
            IsAuthoritative: true,
            source.EvidenceIds
                .Append($"reconciliation-checkpoint:{manifest.CertifiedSnapshot.ReconciliationCheckpointId}:{manifest.CertifiedSnapshot.ReconciliationCheckpointHash}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static evidence => evidence, StringComparer.Ordinal)
                .ToImmutableArray());

        return ValueTask.FromResult(new ReportingGovernedArtifactProduction(
            manifest.RunId,
            manifestDeclaration.ArtifactId,
            certification,
            orderedArtifacts));
    }

    public static ReportingRetainedManifestDocument ParseRetainedManifest(
        ReadOnlySpan<byte> retainedBytes)
    {
        if (retainedBytes.IsEmpty)
        {
            throw new ReportingArtifactCatalogIntegrityException("Retained reporting manifest bytes are empty.");
        }

        ReportingRetainedManifestDocument document;
        try
        {
            document = JsonSerializer.Deserialize<ReportingRetainedManifestDocument>(retainedBytes, JsonOptions)
                ?? throw new ReportingArtifactCatalogIntegrityException(
                    "Retained reporting manifest deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained reporting manifest is not valid canonical JSON: {exception.Message}");
        }

        if (!string.Equals(document.SchemaVersion, RetainedManifestSchemaVersion, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(document.RunId)
            || string.IsNullOrWhiteSpace(document.SeriesId)
            || string.IsNullOrWhiteSpace(document.TemplateId)
            || document.ResolvedTemplate is null
            || document.Scope is null
            || document.Access is null
            || document.Snapshot is null
            || document.AuthoritativeSource is null
            || document.Readiness is null
            || document.Artifacts.IsDefaultOrEmpty
            || document.Artifacts.Any(static artifact =>
                string.IsNullOrWhiteSpace(artifact.ArtifactId)
                || string.IsNullOrWhiteSpace(artifact.FileName)
                || string.IsNullOrWhiteSpace(artifact.ContentType)
                || artifact.ByteLength is <= 0
                || artifact.ContentHashSha256 is not null && !IsSha256(artifact.ContentHashSha256))
            || document.Artifacts.Select(static artifact => artifact.ArtifactId)
                .Distinct(StringComparer.Ordinal).Count() != document.Artifacts.Length
            || document.CertifiedDatasetRowCount < 0
            || !IsSha256(document.CertifiedDatasetHashSha256)
            || !IsSha256(document.ParametersHash)
            || !string.Equals(
                ComputeSha256(Encoding.UTF8.GetBytes(document.ParametersCanonicalJson)),
                document.ParametersHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                "Retained reporting manifest is incomplete or failed canonical identity validation.");
        }

        return document;
    }

    private static byte[] RenderArtifact(
        ReportingOutputManifest manifest,
        ReportingDeclaredArtifact declaration) => declaration.Kind switch
        {
            ReportingDeclaredArtifactKind.PrimaryOutput => manifest.ResolvedParameters!.OutputFormat switch
            {
                ReportingOutputFormatDto.Xlsx => RenderXlsx(manifest),
                ReportingOutputFormatDto.Csv => RenderPrimaryCsv(manifest),
                ReportingOutputFormatDto.EvidenceVault => RenderEvidenceVault(manifest),
                _ => RenderPdf(manifest)
            },
            ReportingDeclaredArtifactKind.CertifiedSourceSchedule => RenderCertifiedRowsCsv(manifest),
            ReportingDeclaredArtifactKind.SupportingSchedules => RenderSupportingSchedules(manifest),
            ReportingDeclaredArtifactKind.EvidenceAppendix => RenderEvidenceAppendix(manifest),
            ReportingDeclaredArtifactKind.ReportWriterGrid => RenderGrid(manifest, declaration.GridId!),
            _ => throw new ReportingGovernanceException(
                $"Artifact kind '{declaration.Kind}' cannot be rendered outside the retained manifest pass.")
        };

    private static byte[] RenderPrimaryCsv(ReportingOutputManifest manifest)
        => RenderCertifiedRowsCsv(manifest);

    private static byte[] RenderCertifiedRowsCsv(ReportingOutputManifest manifest)
    {
        var builder = new StringBuilder();
        var columns = ResolveCertifiedColumns(manifest.CertifiedDatasetRows);
        if (columns.Length == 0)
        {
            AppendCsvRow(builder, "sourceCheckpointId", "ledgerLineCount", "datasetHashSha256");
            AppendCsvRow(
                builder,
                manifest.AuthoritativeSource!.CheckpointId,
                manifest.AuthoritativeSource.LedgerLineCount.ToString(CultureInfo.InvariantCulture),
                ComputeCertifiedRowsHash(manifest.CertifiedDatasetRows));
            return Utf8(builder.ToString());
        }

        AppendCsvRow(builder, columns);
        foreach (var row in manifest.CertifiedDatasetRows)
        {
            AppendCsvRow(
                builder,
                columns.Select(column => row.TryGetValue(column, out var value) ? value : string.Empty).ToArray());
        }

        return Utf8(builder.ToString());
    }

    private static byte[] RenderSupportingSchedules(ReportingOutputManifest manifest)
    {
        var builder = new StringBuilder("gridId,rowKey,column,value\r\n");
        foreach (var grid in manifest.RenderedReportWriterGrids.OrderBy(static grid => grid.GridId, StringComparer.Ordinal))
        {
            foreach (var row in grid.Rows.OrderBy(static row => row.RowKey, StringComparer.Ordinal))
            {
                foreach (var value in row.Values.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    AppendCsvRow(builder, grid.GridId, row.RowKey, value.Key, value.Value);
                }
            }
        }

        if (manifest.RenderedReportWriterGrids.IsDefaultOrEmpty)
        {
            return RenderCertifiedRowsCsv(manifest);
        }

        return Utf8(builder.ToString());
    }

    private static byte[] RenderEvidenceVault(ReportingOutputManifest manifest) =>
        SerializeCanonical(new
        {
            schemaVersion = "meridian.reporting.evidence-vault.v1",
            manifest.RunId,
            manifest.AsOfDate,
            source = manifest.AuthoritativeSource,
            snapshot = manifest.CertifiedSnapshot,
            readiness = manifest.Readiness,
            sections = manifest.Sections.OrderBy(static section => section.SectionId, StringComparer.Ordinal),
            certifiedDatasetHashSha256 = ComputeCertifiedRowsHash(manifest.CertifiedDatasetRows),
            certifiedDatasetRows = manifest.CertifiedDatasetRows
        });

    private static byte[] RenderEvidenceAppendix(ReportingOutputManifest manifest) =>
        SerializeCanonical(new
        {
            schemaVersion = "meridian.reporting.evidence-appendix.v1",
            manifest.RunId,
            evidence = manifest.AuthoritativeSource!.EvidenceIds
                .Concat(manifest.Readiness!.Checks.SelectMany(static check => check.EvidenceReferences))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static evidence => evidence, StringComparer.Ordinal),
            lineage = manifest.Sections
                .OrderBy(static section => section.SectionId, StringComparer.Ordinal)
                .Select(static section => section.Lineage)
        });

    private static byte[] RenderGrid(ReportingOutputManifest manifest, string gridId)
    {
        var grid = manifest.RenderedReportWriterGrids.SingleOrDefault(item =>
            string.Equals(item.GridId, gridId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ReportingGovernanceException(
                $"Declared report-writer grid '{gridId}' has no rendered grid payload.");
        return SerializeCanonical(new
        {
            schemaVersion = "meridian.reporting.grid.v1",
            grid.GridId,
            grid.Title,
            kind = grid.Kind.ToString(),
            columns = grid.Columns.Select(static column => new { column.Key, column.Label, column.Role }),
            rows = grid.Rows
                .OrderBy(static row => row.RowKey, StringComparer.Ordinal)
                .Select(static row => new
                {
                    row.RowKey,
                    values = row.Values.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                        .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)
                }),
            warnings = grid.Warnings.OrderBy(static warning => warning, StringComparer.Ordinal),
            grid.Lineage,
            grid.DataDictionary,
            grid.ValidationChecks,
            grid.Chart
        });
    }

    private static byte[] RenderPdf(ReportingOutputManifest manifest)
    {
        var textLines = new List<string>
        {
            "Meridian governed report",
            $"Run: {manifest.RunId}",
            $"Template: {manifest.ResolvedTemplate!.Name} v{manifest.ResolvedTemplate.Version.ToString(CultureInfo.InvariantCulture)}",
            $"As of: {manifest.AsOfDate:yyyy-MM-dd}",
            $"Source checkpoint: {manifest.AuthoritativeSource!.CheckpointId}",
            $"Snapshot: {manifest.CertifiedSnapshot!.SnapshotId}",
            $"Certified ledger rows: {manifest.CertifiedDatasetRows.Length.ToString(CultureInfo.InvariantCulture)}",
            $"Certified row hash: {ComputeCertifiedRowsHash(manifest.CertifiedDatasetRows)}"
        };
        var accountSummaries = manifest.CertifiedDatasetRows
            .Where(static row => row.ContainsKey("account"))
            .GroupBy(static row => row["account"], StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(group => new
            {
                Account = group.Key,
                Debit = group.Sum(static row => ReadDecimal(row, "debit")),
                Credit = group.Sum(static row => ReadDecimal(row, "credit")),
                Net = group.Sum(static row => ReadDecimal(row, "netAmount"))
            })
            .ToArray();
        if (accountSummaries.Length > 0)
        {
            textLines.Add("Account totals (debit / credit / net)");
            textLines.AddRange(accountSummaries.Select(summary =>
                $"{summary.Account}: {summary.Debit.ToString("G29", CultureInfo.InvariantCulture)} / {summary.Credit.ToString("G29", CultureInfo.InvariantCulture)} / {summary.Net.ToString("G29", CultureInfo.InvariantCulture)}"));
        }
        else
        {
            foreach (var row in manifest.CertifiedDatasetRows.Take(40))
            {
                textLines.Add(string.Join(
                    "; ",
                    row.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                        .Select(static pair => $"{pair.Key}={pair.Value}")));
            }
        }
        var content = new StringBuilder("BT /F1 10 Tf 48 744 Td 14 TL\n");
        foreach (var line in textLines)
        {
            content.Append('(').Append(EscapePdf(line)).Append(") Tj T*\n");
        }
        content.Append("ET\n");
        var stream = content.ToString();
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(stream).ToString(CultureInfo.InvariantCulture)} >>\nstream\n{stream}endstream"
        };
        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.4\n%Meridian\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(output.Position);
            WriteAscii(output, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = output.Position;
        WriteAscii(output, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            WriteAscii(output, $"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        }
        WriteAscii(output,
            $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
        return output.ToArray();
    }

    private static byte[] RenderXlsx(ReportingOutputManifest manifest)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            AddZipEntry(archive, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            AddZipEntry(archive, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            AddZipEntry(archive, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Report\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            AddZipEntry(archive, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            var sheet = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            var columns = ResolveCertifiedColumns(manifest.CertifiedDatasetRows);
            if (columns.Length == 0)
            {
                var metadata = new[]
                {
                    ("Run", manifest.RunId),
                    ("Template", $"{manifest.ResolvedTemplate!.Name} v{manifest.ResolvedTemplate.Version.ToString(CultureInfo.InvariantCulture)}"),
                    ("As of", manifest.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    ("Source checkpoint", manifest.AuthoritativeSource!.CheckpointId),
                    ("Snapshot", manifest.CertifiedSnapshot!.SnapshotId)
                };
                for (var index = 0; index < metadata.Length; index++)
                {
                    AppendXlsxRow(sheet, index + 1, [metadata[index].Item1, metadata[index].Item2]);
                }
            }
            else
            {
                AppendXlsxRow(sheet, 1, columns);
                var rowNumber = 2;
                foreach (var row in manifest.CertifiedDatasetRows)
                {
                    AppendXlsxRow(
                        sheet,
                        rowNumber++,
                        columns.Select(column => row.TryGetValue(column, out var value) ? value : string.Empty).ToArray());
                }
            }
            sheet.Append("</sheetData></worksheet>");
            AddZipEntry(archive, "xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return output.ToArray();
    }

    private static void AddZipEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open();
        var bytes = Utf8(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void AppendXlsxRow(
        StringBuilder sheet,
        int rowNumber,
        IReadOnlyList<string> values)
    {
        sheet.Append("<row r=\"").Append(rowNumber).Append("\">");
        for (var index = 0; index < values.Count; index++)
        {
            var cell = $"{ToExcelColumnName(index + 1)}{rowNumber.ToString(CultureInfo.InvariantCulture)}";
            sheet.Append("<c r=\"").Append(cell)
                .Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                .Append(EscapeXml(values[index]))
                .Append("</t></is></c>");
        }
        sheet.Append("</row>");
    }

    private static string ToExcelColumnName(int ordinal)
    {
        var builder = new StringBuilder();
        var value = ordinal;
        while (value > 0)
        {
            value--;
            builder.Insert(0, (char)('A' + value % 26));
            value /= 26;
        }
        return builder.ToString();
    }

    private static string[] ResolveCertifiedColumns(
        ImmutableArray<IReadOnlyDictionary<string, string>> rows) =>
        rows.IsDefaultOrEmpty
            ? []
            : rows.SelectMany(static row => row.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static column => column, StringComparer.Ordinal)
                .ToArray();

    private static decimal ReadDecimal(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value)
        && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;

    private static string ComputeCertifiedRowsHash(
        ImmutableArray<IReadOnlyDictionary<string, string>> rows)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var row in rows.IsDefault
                         ? ImmutableArray<IReadOnlyDictionary<string, string>>.Empty
                         : rows)
            {
                writer.WriteStartObject();
                foreach (var pair in row.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WriteString(pair.Key, pair.Value);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return ComputeSha256(stream.ToArray());
    }

    private static void AppendCsvRow(StringBuilder builder, params string?[] values)
    {
        builder.AppendJoin(',', values.Select(EscapeCsv)).Append("\r\n");
    }

    private static string EscapeCsv(string? value)
    {
        var normalized = NeutralizeSpreadsheetFormula(value ?? string.Empty);
        return normalized.IndexOfAny([',', '\"', '\r', '\n']) >= 0
            ? $"\"{normalized.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : normalized;
    }

    private static string NeutralizeSpreadsheetFormula(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var firstNonSpace = 0;
        while (firstNonSpace < value.Length && value[firstNonSpace] == ' ')
        {
            firstNonSpace++;
        }
        if (firstNonSpace >= value.Length)
        {
            return value;
        }

        var leading = value[firstNonSpace];
        var isSafeNegativeNumber = leading == '-'
            && decimal.TryParse(
                value.AsSpan(firstNonSpace),
                NumberStyles.Number | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out _);
        return leading is '=' or '+' or '@' or '\t' or '\r' or '\n'
            || leading == '-' && !isSafeNegativeNumber
                ? $"'{value}"
                : value;
    }

    private static void ValidateManifest(ReportingOutputManifest manifest)
    {
        if (manifest.Status != ReportingRunStatus.Draft
            || manifest.ResolvedTemplate is null
            || manifest.ResolvedParameters is null
            || manifest.Readiness is null
            || manifest.OperationalScope is null
            || manifest.ImmutableAccessScope is null
            || manifest.CertifiedSnapshot is null
            || manifest.AuthoritativeSource is null
            || manifest.Artifacts.IsDefaultOrEmpty
            || manifest.CertifiedDatasetRows.IsDefault
            || manifest.CertifiedDatasetRows.Length != manifest.AuthoritativeSource.LedgerLineCount
            || manifest.CertifiedDatasetRows.Any(static row =>
                row is null
                || row.Keys.Any(string.IsNullOrWhiteSpace)
                || row.Values.Any(static value => value is null))
            || string.IsNullOrWhiteSpace(manifest.CertifiedSnapshot.ParametersCanonicalJson)
            || !IsSha256(manifest.CertifiedSnapshot.ParametersHash)
            || !string.Equals(
                manifest.CertifiedSnapshot.SourceCheckpointId,
                manifest.AuthoritativeSource.CheckpointId,
                StringComparison.Ordinal)
            || !string.Equals(
                manifest.CertifiedSnapshot.SourceCheckpointHash,
                manifest.AuthoritativeSource.CheckpointHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingGovernanceException(
                $"Reporting manifest '{manifest.RunId}' is not a complete authoritative certified Draft.");
        }
    }

    private static byte[] SerializeCanonical<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static string EscapePdf(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static string EscapeXml(string value) =>
        System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
