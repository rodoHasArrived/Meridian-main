// Meridian `Status` carrier — exposes the status.js operator-status vocabulary on the
// namespace (its exports are lowercase, so unreachable directly). Same pattern as Money:
//   const { normalizeSeverity } = window.MeridianDesignSystem_205d17.Status;
// These are the exact functions SeverityBadge, GateRail, ReadinessPanel, SlaChip,
// ValidationIssueList, EvidenceLink, and ProvenanceChip use internally, so a consumer's
// own status handling (sorting a queue by severity, worst-of roll-ups, filter chips)
// always agrees with what the operations components render. The wire contract — which
// domain words map to which of the five canonical severities — is documented in
// guidelines/ENTITY_SCHEMAS.md › Status vocabulary.
import { severityKeys, severityLabels, normalizeSeverity, humanizeStatus } from "./status.js";

export const Status = {
  severityKeys,
  severityLabels,
  normalizeSeverity,
  humanizeStatus,
};
