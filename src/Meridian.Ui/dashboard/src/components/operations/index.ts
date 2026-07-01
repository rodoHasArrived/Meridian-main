// Meridian "Concrete" operations component library — the operator-readiness layer.
//
// NEW shared components for the browser workstation, natively re-implemented from the
// Meridian Design System handoff (React 18 + TypeScript + Tailwind 3). Wave 2 screens
// adopt these; nothing imports them yet. Design tokens (`--severity-*`, `--state-*`,
// `--border`, `--accent`, `--bg-*`, `--text-*`) are owned by sibling U1; every consumer
// here reads them with a hex fallback so this library is independently mergeable.

export {
  normalizeSeverity,
  humanizeStatus,
  severityKeys,
  severityLabels,
  type SeverityKey,
} from "./status";
export { SeverityBadge, type SeverityBadgeProps } from "./severity-badge";
export { ReadinessPanel, type ReadinessPanelProps } from "./readiness-panel";
export { GateRail, type GateRailProps, type GateRailGate } from "./gate-rail";
export {
  ValidationIssueList,
  type ValidationIssueListProps,
  type ValidationIssue,
} from "./validation-issue-list";
export { EvidenceLink, type EvidenceLinkProps } from "./evidence-link";
export { TrustStrip, type TrustStripProps, type TrustStripItem } from "./trust-strip";
export { WorkspaceSection, type WorkspaceSectionProps } from "./workspace-section";
