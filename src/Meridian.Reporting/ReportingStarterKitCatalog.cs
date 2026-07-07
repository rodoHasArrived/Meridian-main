using Meridian.Contracts.Workstation;

namespace Meridian.Reporting;

public interface IReportingStarterKitCatalog
{
    ReportingStarterKitDefinition Get(string kitId);

    IReadOnlyList<ReportingStarterKitDefinition> ListKits();
}

public sealed record ReportingStarterKitDefinition(
    string KitId,
    string Archetype,
    string DisplayName,
    string Description,
    IReadOnlyList<string> TemplateIds,
    string DefaultLayoutId,
    string DefaultPeriod,
    IReadOnlyList<ReportingStarterSeedScheduleDefinition> SeedSchedules);

public sealed record ReportingStarterSeedScheduleDefinition(
    string ScheduleId,
    string TemplateId,
    string CronExpression,
    string Cadence,
    string Description,
    int DueOffsetDays,
    int DueHourUtc = 14,
    ReportingScheduleStateDto State = ReportingScheduleStateDto.Draft,
    string? DefaultPeriod = null,
    IReadOnlyList<ReportingScheduleDeliveryTargetDto>? DeliveryTargets = null,
    string? DatasetSourceId = null,
    string? BrandingThemeId = null,
    int MaxRetries = 1);

public sealed class DefaultReportingStarterKitCatalog : IReportingStarterKitCatalog
{
    private readonly IReadOnlyDictionary<string, ReportingStarterKitDefinition> kits;

    public DefaultReportingStarterKitCatalog()
    {
        kits = new Dictionary<string, ReportingStarterKitDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["emerging-manager"] = new ReportingStarterKitDefinition(
                "emerging-manager",
                "Emerging Manager",
                "Emerging Manager",
                "Investor-ready monthly statements, capital accounts, and a draft shadow NAV operating pack.",
                [
                    "investor-monthly-statement",
                    "capital-account-statement",
                    "shadow-nav-daily-pack"
                ],
                "reporting-hub.emerging-manager.v1",
                "CurrentMonth",
                [
                    new ReportingStarterSeedScheduleDefinition(
                        "starter-emerging-manager-investor-monthly",
                        "investor-monthly-statement",
                        "0 9 5 * *",
                        "Monthly",
                        "Draft monthly investor statement schedule; confirm recipients and delivery windows before activation.",
                        DueOffsetDays: 5,
                        DeliveryTargets:
                        [
                            new ReportingScheduleDeliveryTargetDto(
                                "investor-relations",
                                [GovernanceReportArtifactFormatDto.Pdf, GovernanceReportArtifactFormatDto.Xlsx],
                                ReportPackDeliveryModeDto.SecurePortal,
                                "Starter target for monthly limited-partner statements.")
                        ]),
                    new ReportingStarterSeedScheduleDefinition(
                        "starter-emerging-manager-shadow-nav-daily",
                        "shadow-nav-daily-pack",
                        "0 16 * * 1-5",
                        "Business daily",
                        "Draft daily shadow NAV review pack for operations sign-off.",
                        DueOffsetDays: 0,
                        DueHourUtc: 20,
                        DefaultPeriod: "CurrentBusinessDay",
                        DeliveryTargets:
                        [
                            new ReportingScheduleDeliveryTargetDto(
                                "fund-operations",
                                [GovernanceReportArtifactFormatDto.Pdf],
                                ReportPackDeliveryModeDto.InternalRoute,
                                "Starter target for the internal operations desk.")
                        ])
                ]),
            ["fund-administrator"] = new ReportingStarterKitDefinition(
                "fund-administrator",
                "Fund Administrator",
                "Fund Administrator",
                "Evidence, board governance, and holdings packs for outsourced fund administration desks.",
                [
                    "audit-evidence-package",
                    "board-governance-packet",
                    "holdings-board-report"
                ],
                "reporting-hub.fund-administrator.v1",
                "CurrentMonth",
                [
                    new ReportingStarterSeedScheduleDefinition(
                        "starter-fund-admin-audit-evidence",
                        "audit-evidence-package",
                        "0 10 3 * *",
                        "Monthly",
                        "Draft audit evidence package for controller review before auditor delivery.",
                        DueOffsetDays: 3,
                        DeliveryTargets:
                        [
                            new ReportingScheduleDeliveryTargetDto(
                                "compliance-archive",
                                [GovernanceReportArtifactFormatDto.Pdf, GovernanceReportArtifactFormatDto.Csv],
                                ReportPackDeliveryModeDto.EvidenceVault,
                                "Starter target for retained audit evidence.")
                        ]),
                    new ReportingStarterSeedScheduleDefinition(
                        "starter-fund-admin-board-governance",
                        "board-governance-packet",
                        "0 11 7 * *",
                        "Monthly",
                        "Draft board governance packet for monthly administrator checkpoint.",
                        DueOffsetDays: 7,
                        DeliveryTargets:
                        [
                            new ReportingScheduleDeliveryTargetDto(
                                "board-reporting-committee",
                                [GovernanceReportArtifactFormatDto.Pdf],
                                ReportPackDeliveryModeDto.SecurePortal,
                                "Starter target for board reporting committee review.")
                        ])
                ]),
            ["ria-wealth"] = new ReportingStarterKitDefinition(
                "ria-wealth",
                "RIA / Wealth",
                "RIA / Wealth",
                "Quarterly performance reporting with holdings detail for wealth and advisory review cycles.",
                [
                    "performance-quarterly-report",
                    "holdings-board-report"
                ],
                "reporting-hub.ria-wealth.v1",
                "CurrentQuarter",
                [
                    new ReportingStarterSeedScheduleDefinition(
                        "starter-ria-performance-quarterly",
                        "performance-quarterly-report",
                        "0 13 10 1,4,7,10 *",
                        "Quarterly",
                        "Draft quarterly performance report for advisor review.",
                        DueOffsetDays: 10,
                        DeliveryTargets:
                        [
                            new ReportingScheduleDeliveryTargetDto(
                                "investor-relations",
                                [GovernanceReportArtifactFormatDto.Pdf, GovernanceReportArtifactFormatDto.Xlsx],
                                ReportPackDeliveryModeDto.SecurePortal,
                                "Starter target for advisor review.")
                        ]),
                    new ReportingStarterSeedScheduleDefinition(
                        "starter-ria-holdings-monthly",
                        "holdings-board-report",
                        "0 13 5 * *",
                        "Monthly",
                        "Draft holdings review packet for advisor oversight.",
                        DueOffsetDays: 5,
                        DefaultPeriod: "CurrentMonth",
                        DeliveryTargets:
                        [
                            new ReportingScheduleDeliveryTargetDto(
                                "investor-relations",
                                [GovernanceReportArtifactFormatDto.Xlsx],
                                ReportPackDeliveryModeDto.SecurePortal,
                                "Starter target for holdings review.")
                        ])
                ]),
            ["regulated-fund"] = new ReportingStarterKitDefinition(
                "regulated-fund",
                "Regulated Fund",
                "Regulated Fund",
                "Regulatory filing and certified dataset exports for compliance-led reporting desks.",
                [
                    "sec-13f-packet",
                    "certified-dataset-export"
                ],
                "reporting-hub.regulated-fund.v1",
                "CurrentQuarter",
                [
                    new ReportingStarterSeedScheduleDefinition(
                        "starter-regulated-fund-13f",
                        "sec-13f-packet",
                        "0 15 15 2,5,8,11 *",
                        "Quarterly",
                        "Draft SEC 13F packet for compliance review before filing.",
                        DueOffsetDays: 15,
                        DeliveryTargets:
                        [
                            new ReportingScheduleDeliveryTargetDto(
                                "compliance-archive",
                                [GovernanceReportArtifactFormatDto.Pdf, GovernanceReportArtifactFormatDto.Xlsx],
                                ReportPackDeliveryModeDto.EvidenceVault,
                                "Starter target for compliance retention.")
                        ]),
                    new ReportingStarterSeedScheduleDefinition(
                        "starter-regulated-fund-certified-dataset",
                        "certified-dataset-export",
                        "0 16 5 * *",
                        "Monthly",
                        "Draft certified dataset export for downstream compliance and data-governance review.",
                        DueOffsetDays: 5,
                        DefaultPeriod: "CurrentMonth",
                        DeliveryTargets:
                        [
                            new ReportingScheduleDeliveryTargetDto(
                                "compliance-archive",
                                [GovernanceReportArtifactFormatDto.Csv, GovernanceReportArtifactFormatDto.Xlsx],
                                ReportPackDeliveryModeDto.EvidenceVault,
                                "Starter target for retained certified datasets.")
                        ])
                ])
        };
    }

    public ReportingStarterKitDefinition Get(string kitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kitId);
        return kits.TryGetValue(kitId.Trim(), out var kit)
            ? kit
            : throw new KeyNotFoundException($"Unknown reporting starter kit '{kitId}'.");
    }

    public IReadOnlyList<ReportingStarterKitDefinition> ListKits() =>
        kits.Values.OrderBy(static kit => kit.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
}
