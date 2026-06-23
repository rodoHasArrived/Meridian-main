using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace Meridian.Ledger;

/// <summary>
/// Builds export-delivery artifacts for a scheduled ledger report occurrence.
/// </summary>
public static class LedgerScheduledReportExportPackageBuilder
{
    public static IReadOnlyList<LedgerReportPackArtifact> Build(
        LedgerFinancialReportPack reportPack,
        LedgerReportScheduledExport scheduledExport)
    {
        ArgumentNullException.ThrowIfNull(reportPack);
        ArgumentNullException.ThrowIfNull(scheduledExport);
        ValidateScheduleMatchesReport(reportPack, scheduledExport);

        var artifacts = new List<LedgerReportPackArtifact>
        {
            CreateDeliveryManifest(reportPack, scheduledExport),
        };

        if (scheduledExport.Formats.Contains(LedgerReportExportFormat.RegulatoryXml))
            artifacts.Add(CreateRegulatorySummaryXml(reportPack, scheduledExport));

        return artifacts
            .OrderBy(static artifact => artifact.Name, StringComparer.Ordinal)
            .ToArray();
    }

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

    private static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

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
    {
        dimensions = LedgerLineDimensionSetNormalizer.Canonicalize(dimensions);
        if (dimensions is null)
            yield break;

        if (HasValue(dimensions.FundId))
            yield return ("FundId", dimensions.FundId!.Trim());
        if (HasValue(dimensions.EntityId))
            yield return ("EntityId", dimensions.EntityId!.Trim());
        if (HasValue(dimensions.SleeveId))
            yield return ("SleeveId", dimensions.SleeveId!.Trim());
        if (HasValue(dimensions.StrategyId))
            yield return ("StrategyId", dimensions.StrategyId!.Trim());
        if (HasValue(dimensions.InvestorId))
            yield return ("InvestorId", dimensions.InvestorId!.Trim());
        if (HasValue(dimensions.CapitalAccountId))
            yield return ("CapitalAccountId", dimensions.CapitalAccountId!.Trim());
        if (dimensions.InstrumentId is not null)
            yield return ("InstrumentId", dimensions.InstrumentId.Value.ToString("D"));
        if (HasValue(dimensions.TaxLotId))
            yield return ("TaxLotId", dimensions.TaxLotId!.Trim());
        if (HasValue(dimensions.CostCenterId))
            yield return ("CostCenterId", dimensions.CostCenterId!.Trim());
        if (HasValue(dimensions.CounterpartyId))
            yield return ("CounterpartyId", dimensions.CounterpartyId!.Trim());
        if (HasValue(dimensions.OrganizationId))
            yield return ("OrganizationId", dimensions.OrganizationId!.Trim());
        if (HasValue(dimensions.PortfolioId))
            yield return ("PortfolioId", dimensions.PortfolioId!.Trim());
        if (HasValue(dimensions.BookId))
            yield return ("BookId", dimensions.BookId!.Trim());
        if (HasValue(dimensions.AccountId))
            yield return ("AccountId", dimensions.AccountId!.Trim());
        if (HasValue(dimensions.CustomerId))
            yield return ("CustomerId", dimensions.CustomerId!.Trim());
        if (HasValue(dimensions.VendorId))
            yield return ("VendorId", dimensions.VendorId!.Trim());
        if (HasValue(dimensions.ProjectId))
            yield return ("ProjectId", dimensions.ProjectId!.Trim());

        foreach (var (key, value) in dimensions.ExternalGlDimensions.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!HasValue(key) || !HasValue(value))
                continue;

            yield return ($"ExternalGl_{NormalizeXmlName(key)}", value.Trim());
        }
    }

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

    private static bool HasValue(string? value)
        => !string.IsNullOrWhiteSpace(value);

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
