import { ArrowRight } from "lucide-react";
import { Link } from "react-router-dom";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

const reportingTaskModeLinks = [
  {
    id: "report-builder",
    label: "Report Builder",
    description: "Design governed templates, report-writer grids, schedules, branding, and delivery history.",
    href: WORKSTATION_ROUTE_CATALOG.reportingReportBuilder
  },
  {
    id: "run-status",
    label: "Run Status",
    description: "Review report-run queue posture, retries, generated grids, and approval blockers.",
    href: WORKSTATION_ROUTE_CATALOG.reportingRunStatus
  },
  {
    id: "delivery-evidence",
    label: "Delivery Evidence",
    description: "Inspect report-pack recipients, proof bundles, publication support, and report-line provenance.",
    href: WORKSTATION_ROUTE_CATALOG.reportingReportPacks
  },
  {
    id: "exports",
    label: "Exports",
    description: "Run governed exports, inspect generated artifacts, and retain manifest evidence.",
    href: WORKSTATION_ROUTE_CATALOG.reportingExports
  },
  {
    id: "governance",
    label: "Governance",
    description: "Audit access scope, approval posture, lifecycle decisions, and retained controls.",
    href: WORKSTATION_ROUTE_CATALOG.reportingGovernance
  }
] as const;

export function ReportingTaskModeLauncher() {
  return (
    <section
      className="workspace-section-band"
      role="navigation"
      aria-label="Reporting task modes"
    >
      <div className="workspace-section-subheader">
        <div>
          <p className="eyebrow-label">Task modes</p>
          <h3 className="workspace-section-title">Open focused reporting work</h3>
          <p className="workspace-section-summary">
            The daily cockpit stays on the control queue; focused routes hold the heavier builder, run, evidence, export, and governance work.
          </p>
        </div>
      </div>
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-5">
        {reportingTaskModeLinks.map((mode) => (
          <Link
            key={mode.id}
            to={mode.href}
            className="group rounded-md border border-border/70 bg-background/75 px-4 py-3 text-sm transition hover:border-primary/50 hover:bg-primary/5 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            aria-label={`Open ${mode.label} reporting task mode`}
          >
            <span className="flex items-start justify-between gap-3">
              <span className="font-semibold text-foreground">{mode.label}</span>
              <ArrowRight className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground transition group-hover:text-primary" aria-hidden="true" />
            </span>
            <span className="mt-2 block text-xs leading-5 text-muted-foreground">{mode.description}</span>
          </Link>
        ))}
      </div>
    </section>
  );
}
