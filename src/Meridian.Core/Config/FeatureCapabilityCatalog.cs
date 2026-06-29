namespace Meridian.Core.Config;

public static class FeatureCapabilityCatalog
{
    public static IReadOnlyList<FeatureCapabilityDescriptor> Trading { get; } =
    [
        new("desktop.trading.workspace", "Trading workspace", "Trading shell, market feed, order book, position blotter, and run-risk screens.", true, true),
        new("desktop.trading.hours", "Trading hours", "Market-session briefing and schedule coverage review in the Trading workspace.", true, false)
    ];

    public static IReadOnlyList<FeatureCapabilityDescriptor> Data { get; } =
    [
        new("desktop.data.workspace", "Data workspace", "Provider setup, backfill, storage, quality, and data catalog workstation surfaces.", true, true),
        new("desktop.data.security-master", "Security master", "Reference-data review, security lifecycle readiness, and vendor-symbol mapping workbench.", true, false),
        new("desktop.data.retention", "Retention assurance", "Archive health, storage optimization, and retention-policy assurance surfaces.", true, false)
    ];

    public static IReadOnlyList<FeatureCapabilityDescriptor> Settings { get; } =
    [
        new("desktop.settings.workspace", "Settings workspace", "Preferences, credentials, diagnostics, service management, notifications, and support surfaces.", true, true),
        new("desktop.settings.capabilities", "Capability toggles", "Generated capability-control tab for module-declared desktop feature switches.", true, true),
        new("desktop.settings.workflow-library", "Workflow library", "Reusable workflow catalog and action launch surface in Settings.", true, false)
    ];

    public static IReadOnlyList<FeatureCapabilityDescriptor> Reporting { get; } =
    [
        new("desktop.reporting.workspace", "Reporting workspace", "Governed report-pack review, delivery posture, publication evidence, and line-provenance surfaces.", true, true),
        new("desktop.reporting.review-workbench", "Reporting review workbench", "Approval, rejection, publication, restatement, and retained evidence review surfaces.", true, false),
        new("desktop.reporting.delivery-readiness", "Reporting delivery readiness", "Schedule due work, delivery blockers, failure evidence, and readiness warnings.", true, false)
    ];

    public static IReadOnlyList<FeatureCapabilityDescriptor> All { get; } =
        Trading
            .Concat(Data)
            .Concat(Reporting)
            .Concat(Settings)
            .GroupBy(static capability => capability.CapabilityKey, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static capability => capability.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
