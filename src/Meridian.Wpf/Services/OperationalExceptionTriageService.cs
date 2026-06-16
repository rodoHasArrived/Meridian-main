using System;
using System.Collections.Generic;
using System.Linq;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

public interface IOperationalExceptionTriageService
{
    IReadOnlyList<OperationalExceptionRecord> GetExceptions();
}

public sealed class OperationalExceptionTriageService : IOperationalExceptionTriageService
{
    private static readonly DateTimeOffset s_anchor = new(2026, 6, 15, 13, 30, 0, TimeSpan.Zero);

    public IReadOnlyList<OperationalExceptionRecord> GetExceptions() => SeedExceptions;

    private static readonly IReadOnlyList<OperationalExceptionRecord> SeedExceptions =
    [
        new(
            "RECON-001",
            OperationalExceptionSeverity.Critical,
            "Accounting",
            "Reconciliation",
            "CASH-OPERATING-USD",
            "Meridian Core Fund",
            "USD cash",
            "NAV support pack",
            OperationalExceptionStatus.ActionRequired,
            "Match bank statement item BAI2-88421 to ledger cash movement, then route the retained evidence packet for close sign-off.",
            "EvidenceWorkbench:reconciliation/RECON-001",
            Audit("Reconciliation engine", "Opened", "Cash ledger differs from retained bank statement by 12,450.32 USD.", 70, "Operations lead", "Escalated", "Close checklist blocked until bank evidence is attached.", 42)),
        new(
            "RECON-002",
            OperationalExceptionSeverity.High,
            "Accounting",
            "Reconciliation",
            "BROKER-IBKR-MARGIN",
            "Multi-Asset Income Fund",
            "AAPL US equity",
            "Position roll-forward",
            OperationalExceptionStatus.InReview,
            "Review broker activity against posted trade ledger and confirm whether a late allocation amendment is expected.",
            "EvidenceWorkbench:reconciliation/RECON-002",
            Audit("Reconciliation engine", "Opened", "Broker position quantity is 500 shares above ledger projection.", 55, "Fund accountant", "Review started", "Trade blotter and broker activity retained for comparison.", 18)),
        new(
            "DATA-101",
            OperationalExceptionSeverity.High,
            "Data",
            "Provider/Data quality",
            "GLOBAL-DATA-FEED",
            "Meridian Core Fund",
            "EURUSD spot",
            "Daily exposure report",
            OperationalExceptionStatus.WaitingForEvidence,
            "Wait for provider replay evidence or switch the report package to the approved fallback vendor snapshot.",
            "DataQuality:provider-gap/DATA-101",
            Audit("Provider health", "Opened", "Primary provider missed four EURUSD bars during the valuation window.", 63, "Data steward", "Evidence requested", "Provider replay request queued; fallback vendor snapshot is available.", 21)),
        new(
            "DATA-102",
            OperationalExceptionSeverity.Medium,
            "Data",
            "Provider/Data quality",
            "REFDATA-SECMASTER",
            "Private Credit Sleeve",
            "CUSIP 12345ABC9",
            "Security master coverage",
            OperationalExceptionStatus.New,
            "Confirm the coupon reset date from retained source evidence before publishing the asset operations package.",
            "SecurityInstrumentExplorer:instrument/DATA-102",
            Audit("Data quality", "Opened", "Security Master reset-date confidence fell below approval threshold.", 48, "Data steward", "Queued", "Awaiting source document comparison.", 8)),
        new(
            "PORT-201",
            OperationalExceptionSeverity.Medium,
            "Portfolio",
            "Operational exception",
            "FUND-OPS-001",
            "Meridian Core Fund",
            "US Treasury ladder",
            "Portfolio exposure summary",
            OperationalExceptionStatus.InReview,
            "Open the filtered queue from Portfolio and confirm the exposure summary uses the reconciled position set.",
            "PortfolioExplorer:exception/PORT-201",
            Audit("Portfolio cockpit", "Opened", "Exposure summary references a position set with unresolved reconciliation pressure.", 38, "Portfolio analyst", "Review started", "Deep-linked from Portfolio cockpit filtered queue.", 15)),
        new(
            "RPT-301",
            OperationalExceptionSeverity.Low,
            "Reporting",
            "Operational exception",
            "REPORTING-PACK",
            "Meridian Core Fund",
            "All instruments",
            "Board package v2026-06",
            OperationalExceptionStatus.Resolved,
            "No action required; retained export evidence was attached and the report package can proceed.",
            "ReportLineProvenanceExplorer:report/RPT-301",
            Audit("Reporting", "Opened", "Scheduled PDF delivery lacked retained export manifest.", 95, "Reporting lead", "Resolved", "Export manifest and approval history attached.", 9))
    ];

    private static IReadOnlyList<OperationalExceptionAuditEntry> Audit(
        string actor1,
        string action1,
        string detail1,
        int minutesAgo1,
        string actor2,
        string action2,
        string detail2,
        int minutesAgo2)
        =>
        [
            new(s_anchor.AddMinutes(-minutesAgo1), actor1, action1, detail1),
            new(s_anchor.AddMinutes(-minutesAgo2), actor2, action2, detail2)
        ];
}
