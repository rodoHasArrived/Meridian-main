using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Meridian.Contracts.Integrity;

namespace Meridian.Ledger;

/// <summary>
/// Builds export-delivery artifacts for a scheduled ledger report occurrence.
/// </summary>
public static class LedgerScheduledReportExportPackageBuilder
{
    public static IReadOnlyList<LedgerReportPackArtifact> Build(
        LedgerFinancialReportPack reportPack,
        LedgerReportScheduledExport scheduledExport,
        ILedgerReportBinaryRenderer? binaryRenderer = null)
    {
        ArgumentNullException.ThrowIfNull(reportPack);
        ArgumentNullException.ThrowIfNull(scheduledExport);
        ValidateScheduleMatchesReport(reportPack, scheduledExport);

        var renderer = binaryRenderer ?? BuiltInLedgerReportBinaryRenderer.Instance;
        var artifacts = new List<LedgerReportPackArtifact>
        {
            CreateDeliveryManifest(reportPack, scheduledExport),
        };

        // Honor every declared format: each requested LedgerReportExportFormat now yields a matching
        // delivery artifact instead of being silently echoed only in the manifest header.
        foreach (var format in scheduledExport.Formats.Distinct())
            artifacts.Add(CreateFormatArtifact(reportPack, scheduledExport, format, renderer));

        return artifacts
            .OrderBy(static artifact => artifact.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static LedgerReportPackArtifact CreateFormatArtifact(
        LedgerFinancialReportPack reportPack,
        LedgerReportScheduledExport scheduledExport,
        LedgerReportExportFormat format,
        ILedgerReportBinaryRenderer renderer)
        => format switch
        {
            LedgerReportExportFormat.Csv => CreateStatementsCsv(reportPack),
            LedgerReportExportFormat.Json => CreateStatementsJson(reportPack),
            LedgerReportExportFormat.Xlsx => CreateBinaryArtifact(
                "scheduled-export-financials.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                renderer.RenderWorkbook(reportPack)),
            LedgerReportExportFormat.Pdf => CreateBinaryArtifact(
                "scheduled-export-financials.pdf",
                "application/pdf",
                renderer.RenderPdf(reportPack)),
            LedgerReportExportFormat.RegulatoryXml => CreateRegulatorySummaryXml(reportPack, scheduledExport),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported scheduled-export format."),
        };

    private static LedgerReportPackArtifact CreateBinaryArtifact(string name, string contentType, byte[] bytes)
    {
        if (bytes.Length == 0)
            throw new InvalidOperationException($"Scheduled-export artifact '{name}' rendered no bytes.");

        var descriptor = $"{name}; {bytes.Length.ToString(CultureInfo.InvariantCulture)} bytes";
        return new LedgerReportPackArtifact(name, contentType, descriptor, ComputeSha256(bytes), bytes);
    }

    private static LedgerReportPackArtifact CreateStatementsCsv(LedgerFinancialReportPack reportPack)
    {
        var builder = new StringBuilder();
        foreach (var table in LedgerReportPresentation.BuildTables(reportPack))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"# {table.Title}");
            builder.AppendLine(string.Join(',', table.Headers.Select(EscapeCsv)));
            foreach (var row in table.Rows)
                builder.AppendLine(string.Join(',', row.Select(EscapeCsv)));
            builder.AppendLine();
        }

        var content = builder.ToString();
        return new LedgerReportPackArtifact("scheduled-export-financials.csv", "text/csv", content, ComputeSha256(content));
    }

    private static LedgerReportPackArtifact CreateStatementsJson(LedgerFinancialReportPack reportPack)
    {
        var statements = reportPack.Statements;
        var builder = new StringBuilder();
        builder.AppendLine("{");
        builder.AppendLine("  \"schema\": \"ledger-scheduled-export-statements-v1\",");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"reportId\": {JsonString(reportPack.Request.ReportId)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"fundId\": {JsonString(reportPack.Request.FundId)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"periodId\": {JsonString(reportPack.Request.PeriodId)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"baseCurrency\": {JsonString(reportPack.Request.BaseCurrency)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"totalAssets\": {FormatDecimal(statements.TotalAssets)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"totalLiabilities\": {FormatDecimal(statements.TotalLiabilities)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"endingEquity\": {FormatDecimal(statements.EndingEquity)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"netIncome\": {FormatDecimal(statements.NetIncome)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"netCashFlow\": {FormatDecimal(statements.CashFlow?.NetCashFlow ?? 0m)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"endingCash\": {FormatDecimal(statements.CashFlow?.EndingCash ?? 0m)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"endingPartnersCapital\": {FormatDecimal(statements.PartnersCapital?.EndingCapital ?? 0m)},");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  \"reportPackSignature\": {JsonString(reportPack.Signature.PayloadChecksumSha256)}");
        builder.AppendLine("}");
        var content = builder.ToString();
        return new LedgerReportPackArtifact("scheduled-export-financials.json", "application/json", content, ComputeSha256(content));
    }

    private static string JsonString(string value)
        => System.Text.Json.JsonSerializer.Serialize(value);

    private static void ValidateScheduleMatchesReport(
        LedgerFinancialReportPack reportPack,
        LedgerReportScheduledExport scheduledExport)
    {
        if (!string.Equals(reportPack.Request.ReportId, scheduledExport.ReportId, StringComparison.Ordinal))
            throw new ArgumentException("Scheduled export report identifier must match the report pack.", nameof(scheduledExport));
        if (!string.Equals(reportPack.Request.FundId, scheduledExport.Schedule.FundId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Scheduled export fund identifier must match the report pack.", nameof(scheduledExport));
        if (!string.Equals(reportPack.Request.PeriodId, scheduledExport.PeriodId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Scheduled export period identifier must match the report pack.", nameof(scheduledExport));
        if (reportPack.Request.AsOf != scheduledExport.AsOf)
            throw new ArgumentException("Scheduled export as-of timestamp must match the report pack.", nameof(scheduledExport));
    }

    private static LedgerReportPackArtifact CreateDeliveryManifest(
        LedgerFinancialReportPack reportPack,
        LedgerReportScheduledExport scheduledExport)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ledger-scheduled-export-manifest-v1");
        builder.AppendLine(CultureInfo.InvariantCulture, $"schedule-id,{EscapeCsv(scheduledExport.Schedule.ScheduleId)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"report-id,{EscapeCsv(scheduledExport.ReportId)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"fund-id,{EscapeCsv(scheduledExport.Schedule.FundId)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"report-name,{EscapeCsv(scheduledExport.Schedule.ReportName)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"period-id,{EscapeCsv(scheduledExport.PeriodId)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"period-start,{scheduledExport.PeriodStart:O}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"period-end,{scheduledExport.PeriodEnd:O}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"as-of,{scheduledExport.AsOf:O}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"due-at-utc,{scheduledExport.DueAtUtc:O}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"base-currency,{EscapeCsv(reportPack.Request.BaseCurrency)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"dimension-scope,{EscapeCsv(FormatDimensionScope(reportPack.Request.LineDimensions))}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"formats,{EscapeCsv(string.Join('|', scheduledExport.Formats.OrderBy(static format => format.ToString(), StringComparer.Ordinal)))}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"recipients,{EscapeCsv(string.Join('|', scheduledExport.Recipients.Order(StringComparer.OrdinalIgnoreCase)))}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"source-artifact-count,{reportPack.Artifacts.Count}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"report-pack-signature,{reportPack.Signature.PayloadChecksumSha256}");
        builder.AppendLine("artifact,content-type,sha256");

        foreach (var artifact in reportPack.Artifacts.OrderBy(static artifact => artifact.Name, StringComparer.Ordinal))
            builder.AppendLine(CultureInfo.InvariantCulture, $"{EscapeCsv(artifact.Name)},{EscapeCsv(artifact.ContentType)},{artifact.ChecksumSha256}");

        var content = builder.ToString();
        return new LedgerReportPackArtifact("scheduled-export-manifest.csv", "text/csv", content, ComputeSha256(content));
    }

    private static LedgerReportPackArtifact CreateRegulatorySummaryXml(
        LedgerFinancialReportPack reportPack,
        LedgerReportScheduledExport scheduledExport)
    {
        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(builder, new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            Indent = true,
            OmitXmlDeclaration = false,
        }))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("LedgerRegulatorySummary");
            writer.WriteAttributeString("schema", "ledger-regulatory-summary-v1");
            writer.WriteElementString("ScheduleId", scheduledExport.Schedule.ScheduleId);
            writer.WriteElementString("ReportId", reportPack.Request.ReportId);
            writer.WriteElementString("FundId", reportPack.Request.FundId);
            writer.WriteElementString("PeriodId", reportPack.Request.PeriodId);
            writer.WriteElementString("BaseCurrency", reportPack.Request.BaseCurrency);
            WriteDimensionScope(writer, reportPack.Request.LineDimensions);
            writer.WriteElementString("AsOf", reportPack.Request.AsOf.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteElementString("DueAtUtc", scheduledExport.DueAtUtc.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteElementString("TotalAssets", FormatDecimal(reportPack.Statements.TotalAssets));
            writer.WriteElementString("TotalLiabilities", FormatDecimal(reportPack.Statements.TotalLiabilities));
            writer.WriteElementString("TotalEquity", FormatDecimal(reportPack.Statements.TotalEquity));
            writer.WriteElementString("TotalRevenue", FormatDecimal(reportPack.Statements.TotalRevenue));
            writer.WriteElementString("TotalExpenses", FormatDecimal(reportPack.Statements.TotalExpenses));
            writer.WriteElementString("NetIncome", FormatDecimal(reportPack.Statements.NetIncome));
            writer.WriteElementString("EndingEquity", FormatDecimal(reportPack.Statements.EndingEquity));
            writer.WriteElementString("AccountingEquationVariance", FormatDecimal(reportPack.Statements.AccountingEquationVariance));
            writer.WriteElementString("ReportPackSignature", reportPack.Signature.PayloadChecksumSha256);
            writer.WriteStartElement("Recipients");
            foreach (var recipient in scheduledExport.Recipients.Order(StringComparer.OrdinalIgnoreCase))
                writer.WriteElementString("Recipient", recipient);
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        var content = builder.ToString();
        return new LedgerReportPackArtifact("regulatory-summary.xml", "application/xml", content, ComputeSha256(content));
    }

    // Both overloads delegate rather than being replaced at the call sites: this type declares
    // two of them, so a name-only rewrite cannot tell which one a given call meant. The duplicated
    // hashing is gone either way, which is the part that could drift.
    private static string ComputeSha256(string value)
        => Sha256Digest.ComputeUtf8(value);

    private static string ComputeSha256(byte[] bytes)
        => Sha256Digest.Compute(bytes);

    private static string FormatDecimal(decimal value)
        => value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static void WriteDimensionScope(XmlWriter writer, LedgerLineDimensionSet? dimensions)
    {
        writer.WriteStartElement("DimensionScope");

        foreach (var (name, value) in BuildDimensionFields(dimensions))
            writer.WriteElementString(name, value);

        writer.WriteEndElement();
    }

    private static string FormatDimensionScope(LedgerLineDimensionSet? dimensions)
        => string.Join(
            ';',
            BuildDimensionFields(dimensions)
                .OrderBy(static field => field.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static field => $"{field.Name}={field.Value}"));

    private static IEnumerable<(string Name, string Value)> BuildDimensionFields(LedgerLineDimensionSet? dimensions)
        => LedgerLineDimensionSetFields.Enumerate(dimensions)
            .Select(static field => field.ExternalGlKey is null
                ? (ToXmlElementName(field.Name), field.Value)
                : ($"ExternalGl_{NormalizeXmlName(field.ExternalGlKey)}", field.Value));

    private static string ToXmlElementName(string camelCaseName)
        => string.Create(camelCaseName.Length, camelCaseName, static (span, name) =>
        {
            name.CopyTo(span);
            span[0] = char.ToUpperInvariant(span[0]);
        });

    private static string NormalizeXmlName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' || character == '-'
                ? character
                : '_');
        }

        return builder.Length == 0 ? "Dimension" : builder.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
