using System.Text;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class FundStructureEndpoints
{
    private static bool IsPortalJsonRequest(string? format) =>
        string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);

    private static string BuildDeliveryPortalHtml(ReportPackDeliveryPackageDto package)
    {
        var theme = package.BrandingTheme;
        var primaryColor = NormalizePortalColor(theme?.PrimaryColor, "#1F4E79");
        var accentColor = NormalizePortalColor(theme?.AccentColor, "#2F9E8F");
        var textColor = NormalizePortalColor(theme?.TextColor, "#111827");
        var backgroundColor = NormalizePortalColor(theme?.BackgroundColor, "#FFFFFF");
        var firmName = string.IsNullOrWhiteSpace(theme?.FirmName)
            ? "Meridian"
            : theme.FirmName.Trim();
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.Append("<html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>")
            .Append(EscapePortalHtml(firmName))
            .Append(" Report Package</title><style>");
        builder.Append("body{margin:0;font-family:Arial,sans-serif;background:")
            .Append(backgroundColor)
            .Append(";color:")
            .Append(textColor)
            .Append("}.portal-header{border-top:8px solid ")
            .Append(primaryColor)
            .Append(";padding:28px 32px 18px}.portal-kicker{color:")
            .Append(accentColor)
            .Append(";font-size:12px;text-transform:uppercase;letter-spacing:.12em}.content{padding:0 32px 32px;max-width:980px}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:12px}.panel{border:1px solid #d0d5dd;border-radius:6px;padding:14px;background:rgba(255,255,255,.72)}a{color:")
            .Append(primaryColor)
            .Append("}.artifact{display:flex;gap:8px;align-items:flex-start;justify-content:space-between;border-top:1px solid #eaecf0;padding:10px 0}.badge{border:1px solid ")
            .Append(accentColor)
            .Append(";border-radius:999px;padding:2px 8px;font-size:11px;text-transform:uppercase}.footer{border-top:2px solid ")
            .Append(accentColor)
            .Append(";margin-top:24px;padding-top:14px;font-size:12px}</style></head><body>");
        builder.Append("<header class=\"portal-header\"><div class=\"portal-kicker\">")
            .Append(EscapePortalHtml(firmName))
            .Append("</div><h1>Secure Report Package</h1><p>")
            .Append(EscapePortalHtml(package.DeliveryAccessSummary ?? "Token-gated report package."))
            .Append("</p>");
        if (!string.IsNullOrWhiteSpace(theme?.LogoUri))
        {
            builder.Append("<p>Logo: ")
                .Append(EscapePortalHtml(theme.LogoUri.Trim()))
                .Append("</p>");
        }

        builder.AppendLine("</header><main class=\"content\">");
        builder.AppendLine("<section class=\"grid\" aria-label=\"Package details\">");
        AppendPortalSummaryPanel(builder, "Package", package.PackageId, package.DeliveryChannelSummary ?? package.DeliveryMode.ToString());
        AppendPortalSummaryPanel(builder, "Integrity", package.IntegritySummary ?? "Integrity summary not retained.", package.RetainedManifestPath);
        AppendPortalSummaryPanel(builder, "Access", package.AccessExpiresAtUtc?.ToString("O") ?? "No expiry retained.", package.PortalRoute);
        AppendPortalSummaryPanel(builder, "Dataset", FormatPortalDatasetSummary(package), package.ReportingRunId ?? package.PublicationManifestId ?? "Publication package");
        builder.AppendLine("</section>");
        builder.AppendLine("<section class=\"panel\" aria-label=\"Package artifacts\"><h2>Downloads</h2>");
        foreach (var artifact in package.Artifacts ?? [])
        {
            builder.Append("<div class=\"artifact\"><span><strong>")
                .Append(EscapePortalHtml(artifact.ArtifactName))
                .Append("</strong><br><span>")
                .Append(EscapePortalHtml(artifact.ContentType))
                .Append("</span><br><span>SHA-256 ")
                .Append(EscapePortalHtml(artifact.ChecksumSha256))
                .Append("</span></span><span><span class=\"badge\">")
                .Append(EscapePortalHtml(artifact.Format.ToString()))
                .Append("</span> ");
            if (!string.IsNullOrWhiteSpace(artifact.DownloadRoute))
            {
                builder.Append("<a href=\"")
                    .Append(EscapePortalAttribute(artifact.DownloadRoute))
                    .Append("\">Download</a>");
            }

            builder.AppendLine("</span></div>");
        }

        builder.AppendLine("</section>");
        if (package.Notifications is { Count: > 0 })
        {
            builder.AppendLine("<section class=\"panel\" aria-label=\"Package notifications\"><h2>Notifications</h2><ul>");
            foreach (var notification in package.Notifications)
            {
                builder.Append("<li><strong>")
                    .Append(EscapePortalHtml(notification.Subject))
                    .Append("</strong><br>")
                    .Append(EscapePortalHtml(notification.Body))
                    .Append("</li>");
            }

            builder.AppendLine("</ul></section>");
        }

        if (theme is not null)
        {
            builder.Append("<footer class=\"footer\"><strong>")
                .Append(EscapePortalHtml(theme.Name))
                .Append("</strong><br>")
                .Append(EscapePortalHtml(theme.FooterText ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(theme.Disclaimer))
            {
                builder.Append("<br>")
                    .Append(EscapePortalHtml(theme.Disclaimer.Trim()));
            }

            builder.AppendLine("</footer>");
        }

        builder.AppendLine("</main></body></html>");
        return builder.ToString();
    }

    private static void AppendPortalSummaryPanel(
        StringBuilder builder,
        string label,
        string value,
        string detail)
    {
        builder.Append("<article class=\"panel\"><h2>")
            .Append(EscapePortalHtml(label))
            .Append("</h2><p><strong>")
            .Append(EscapePortalHtml(value))
            .Append("</strong></p><p>")
            .Append(EscapePortalHtml(detail))
            .AppendLine("</p></article>");
    }

    private static string FormatPortalDatasetSummary(ReportPackDeliveryPackageDto package)
    {
        var source = package.ReportWriterDatasetSourceLabel
            ?? package.ReportWriterDatasetSourceId
            ?? "Report package";
        return package.ReportWriterDatasetRowCount.HasValue
            ? $"{source} ({package.ReportWriterDatasetRowCount.Value} rows)"
            : source;
    }

    private static string NormalizePortalColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim();
        return normalized.Length == 7
               && normalized[0] == '#'
               && normalized.Skip(1).All(Uri.IsHexDigit)
            ? normalized
            : fallback;
    }

    private static string EscapePortalHtml(string? value) =>
        (value ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string EscapePortalAttribute(string? value) =>
        EscapePortalHtml(value).Replace("'", "&#39;", StringComparison.Ordinal);
}
