import type {
  PortfolioMultiAssetCoverageGroup,
  PortfolioMultiAssetCoverageRow
} from "./portfolio-screen.view-model";

/**
 * Pure presenters for the Portfolio desk's multi-asset coverage table: status tone and label,
 * readiness grouping, and the per-group summary rows.
 *
 * Extracted from the view model so that file can keep changing without growing past its
 * no-new-god-file cap (build/config/file-size-baseline.json). The row and group types stay owned
 * by the view model and are imported type-only, so this pair carries no runtime import cycle.
 */
export function multiAssetStatusTone(status: string): PortfolioMultiAssetCoverageRow["statusTone"] {
  if (status === "Ready") return "success";
  if (status === "Blocked") return "danger";
  if (status === "ReviewRequired" || status === "Degraded") return "warning";
  return "default";
}

export function multiAssetStatusLabel(status: string): string {
  if (status === "ReviewRequired") return "Review required";
  return status;
}

export function multiAssetReadinessGroup(status: string): Pick<PortfolioMultiAssetCoverageGroup, "id" | "label" | "statusTone"> {
  if (status === "Ready") return { id: "ready", label: "Ready", statusTone: "success" };
  if (status === "Blocked") return { id: "blocked", label: "Blocked", statusTone: "danger" };
  if (status === "ReviewRequired" || status === "Degraded") return { id: "review", label: "Review required", statusTone: "warning" };
  return { id: "other", label: "Other state", statusTone: "default" };
}

export function multiAssetReadinessDetail(
  status: string,
  statusLabel: string,
  blockerCount: number,
  evidenceReady: number,
  evidenceTotal: number
): string {
  if (status === "Ready") {
    return `${statusLabel}: ${evidenceReady}/${evidenceTotal} evidence targets ready.`;
  }

  const blockerLabel = blockerCount === 0
    ? "no blockers"
    : `${blockerCount} blocker${blockerCount === 1 ? "" : "s"}`;
  return `${statusLabel}: ${evidenceReady}/${evidenceTotal} evidence targets ready with ${blockerLabel}.`;
}

export function buildMultiAssetCoverageGroups(rows: PortfolioMultiAssetCoverageRow[]): PortfolioMultiAssetCoverageGroup[] {
  const order = ["blocked", "review", "ready", "other"];
  const groups = order
    .map((id) => {
      const groupRows = rows.filter((row) => row.readinessGroupId === id);
      if (groupRows.length === 0) {
        return null;
      }

      const label = groupRows[0].readinessGroupLabel;
      return {
        id,
        label,
        statusTone: groupRows[0].readinessGroupId === "blocked"
          ? "danger"
          : groupRows[0].readinessGroupId === "ready"
            ? "success"
            : groupRows[0].readinessGroupId === "review"
              ? "warning"
              : "default",
        summary: `${groupRows.length} asset class${groupRows.length === 1 ? "" : "es"}`,
        rows: groupRows
      };
    })
    .filter((group): group is PortfolioMultiAssetCoverageGroup => group !== null);

  return groups;
}
