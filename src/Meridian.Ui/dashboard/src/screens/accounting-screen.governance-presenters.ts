import type { ApiErrorDisplay } from "@/lib/api-errors";
import type {
  AccountingMigrationRunArtifactViewModel,
  AccountingProductionReadinessComponentViewModel,
} from "./accounting-screen.view-model";
import type { AccountingMigrationRunArtifact, AccountingProductionReadiness, PostingRule, RuleDryRunResult } from "@/types";

export function resolveAccountingProductionReadinessActivationDisabledReason(
  readiness: AccountingProductionReadiness | null,
  loading: boolean,
  error: ApiErrorDisplay | null
): string | null {
  if (loading) {
    return "Wait for the production-readiness assessment before activation.";
  }

  if (error || !readiness) {
    return "Refresh production readiness successfully before activation.";
  }

  if (readiness.status !== "Ready") {
    return `Resolve the ${formatProductionReadinessStatus(readiness.status).toLowerCase()} production-readiness assessment before activation.`;
  }

  return null;
}

export function formatMigrationRunKind(kind: AccountingMigrationRunArtifact["kind"]): string {
  switch (kind) {
    case "LedgerBookScope":
      return "Ledger-book scope";
    case "HistoricalJournalBackfill":
      return "Historical journal backfill";
    case "DimensionalBackfill":
      return "Dimensional backfill";
    case "AccountingConfigurationPromotion":
      return "Configuration promotion";
    case "CloseReportingEvidence":
      return "Close/reporting evidence";
    default:
      return String(kind);
  }
}

export function formatMigrationRunStatus(status: AccountingMigrationRunArtifact["status"]): string {
  return status === "Completed" ? "Completed" : status === "Certified" ? "Certified" : status === "Failed" ? "Failed" : status === "Running" ? "Running" : "Planned";
}

export function migrationRunStatusTone(status: AccountingMigrationRunArtifact["status"]): AccountingMigrationRunArtifactViewModel["tone"] {
  switch (status) {
    case "Certified":
    case "Completed":
      return "success";
    case "Failed":
      return "danger";
    case "Running":
      return "warning";
    case "Planned":
    default:
      return "default";
  }
}

export function formatProductionReadinessStatus(status: AccountingProductionReadiness["status"]): string {
  switch (status) {
    case "Ready":
      return "Ready";
    case "ReviewRequired":
      return "Review required";
    case "Blocked":
      return "Blocked";
    case "Unavailable":
      return "Unavailable";
    default:
      return String(status);
  }
}

export function formatProductionReadinessArea(area: string): string {
  return area
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/\bGl\b/g, "GL");
}

export function productionReadinessStatusTone(status: AccountingProductionReadiness["status"]): AccountingProductionReadinessComponentViewModel["tone"] {
  switch (status) {
    case "Ready":
      return "success";
    case "Blocked":
    case "Unavailable":
      return "danger";
    case "ReviewRequired":
    default:
      return "warning";
  }
}

export function formatConfigurationActorLabel(actor: string | null | undefined): string {
  const normalized = actor?.trim() ?? "";
  if (!normalized) {
    return "Unknown operator";
  }

  if (/^fixture[-_:]/i.test(normalized)) {
    return /review/i.test(normalized) ? "Configuration reviewer" : "Configuration controller";
  }

  if (normalized === "browser-accounting-operator") {
    return "Accounting operator";
  }

  return normalized;
}

export function formatConfigurationActionLabel(action: string): string {
  const words = action.trim().replace(/[._-]+/g, " ");
  return words.length > 0
    ? `${words.charAt(0).toUpperCase()}${words.slice(1)}`
    : "Configuration change";
}

export function resolveDryRunRuleMismatchReason(
  dryRunPreview: RuleDryRunResult,
  selectedRule: PostingRule,
  action: string
): string | null {
  return dryRunPreview.selectedRuleId && dryRunPreview.selectedRuleId !== selectedRule.ruleId
    ? `Dry-run preview must match the selected posting rule before ${action}.`
    : null;
}

export function formatRuleEffectiveRange(rule: PostingRule): string {
  const start = rule.effectiveFrom ?? "open start";
  const end = rule.effectiveTo ?? "open end";
  return `${start} -> ${end}`;
}
