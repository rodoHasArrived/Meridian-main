Status — the operator status vocabulary as a consumer-callable carrier (`window.MeridianDesignSystem_205d17.Status`). Exposes the exact `normalizeSeverity` / `humanizeStatus` / `severityKeys` / `severityLabels` that SeverityBadge, GateRail, ReadinessPanel, SlaChip, ValidationIssueList, EvidenceLink, and ProvenanceChip use internally — so consumer-side severity logic (queue sorting, worst-of roll-ups, filter counts) always agrees with what the chips render. The domain-word → severity mapping is the wire contract documented in `guidelines/ENTITY_SCHEMAS.md › Status vocabulary`; pass raw domain words, never pre-mapped severities.

```jsx
const { Status, SeverityBadge } = window.MeridianDesignSystem_205d17;
const { normalizeSeverity, severityKeys, severityLabels, humanizeStatus } = Status;

// Sort a case queue worst-first, exactly as the badges will color it
const rank = (s) => severityKeys.indexOf(normalizeSeverity(s));
const queue = [...cases].sort((a, b) => rank(b.status) - rank(a.status));

// Filter-chip counts that match the rendered badges
const counts = {};
for (const c of cases) counts[normalizeSeverity(c.status)] = (counts[normalizeSeverity(c.status)] || 0) + 1;

// Worst-of roll-up for a panel header
const worst = queue.length ? normalizeSeverity(queue[0].status) : "info";
<SeverityBadge status={worst} label={`${severityLabels[worst]} · ${counts[worst] || 0}`} />
```

Non-obvious: `normalizeSeverity` is case- and punctuation-insensitive (`"ReviewRequired"` ≡ `"review required"` ≡ `"REVIEW_REQUIRED"`), and anything unrecognized returns `"info"` (muted) — never a reassuring green. `severityKeys` order is best → informational (`ready · review · action · blocked · info`); for worst-first sorts note that `info` ranks BELOW `blocked` because it means "no claim", not "fine". The backing module also covers the platform contract enums (`OperationsGateStatus`, `ReconciliationState`, `OperationsWorkflowStatus`, SLA states) — see the ENTITY_SCHEMAS table before inventing a mapping.
