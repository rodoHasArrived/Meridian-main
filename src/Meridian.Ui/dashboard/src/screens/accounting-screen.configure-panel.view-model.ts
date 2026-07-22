import type {
  AccountingConfigurationViewModel,
  AccountingProductionGapViewModel
} from "@/screens/accounting-screen.view-model";

/**
 * Presentation-only helpers for the Accounting Configuration panel usability layer:
 * section navigation, jump-to-setting search, activation checklist, typed key/value
 * editing, chart path building, and change-preview synthesis. These helpers derive
 * entirely from the existing {@link AccountingConfigurationViewModel} and never mutate
 * the underlying view-model contract, so the shared configuration API is untouched.
 */

export type ConfigureTone = "default" | "success" | "warning" | "danger";

export interface ConfigureSectionLink {
  id: string;
  label: string;
  anchorId: string;
  description: string;
}

/**
 * Stable top-level sections for the Configure workstream. Labels are intentionally
 * short and distinct from the section card headings so they can be rendered as an
 * in-page navigation without colliding with heading-based queries.
 */
export const CONFIGURE_SECTION_LINKS: readonly ConfigureSectionLink[] = [
  { id: "setup", label: "Setup", anchorId: "configure-section-setup", description: "Readiness metrics and setup candidate" },
  { id: "books", label: "Books", anchorId: "configure-section-books", description: "Ledger book administration" },
  { id: "chart", label: "Chart", anchorId: "configure-section-chart", description: "Chart account authoring" },
  { id: "mappings", label: "Mappings", anchorId: "configure-section-mappings", description: "Tenant admin, dimensions, and external GL" },
  { id: "rules", label: "Rules", anchorId: "configure-section-rules", description: "Posting rules studio" },
  { id: "activation", label: "Activation", anchorId: "configure-section-activation", description: "Validation and activation gate" }
];

const SECTION_ANCHOR_IDS = new Set(CONFIGURE_SECTION_LINKS.map((section) => section.anchorId));

/** True when the anchor id belongs to a known Configure section. */
export function isConfigureSectionAnchor(anchorId: string): boolean {
  return SECTION_ANCHOR_IDS.has(anchorId);
}

export interface ConfigureKeyValuePair {
  /** Stable-within-render identity for React keys. */
  id: string;
  key: string;
  value: string;
}

/**
 * Parse newline-delimited `key=value` text (the format the configuration setters
 * persist for dimension maps and external GL account mappings) into editable rows.
 * The split is on the first `=` only, so keys containing `:` (e.g. `Meridian:Account`)
 * round-trip cleanly. Blank lines are ignored.
 */
export function parseConfigureKeyValuePairs(text: string): ConfigureKeyValuePair[] {
  if (!text) {
    return [];
  }
  const pairs: ConfigureKeyValuePair[] = [];
  const lines = text.split(/\r?\n/);
  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index].trim();
    if (line.length === 0) {
      continue;
    }
    const separator = line.indexOf("=");
    if (separator === -1) {
      pairs.push({ id: `pair-${index}`, key: line.trim(), value: "" });
      continue;
    }
    const key = line.slice(0, separator).trim();
    const value = line.slice(separator + 1).trim();
    pairs.push({ id: `pair-${index}`, key, value });
  }
  return pairs;
}

/**
 * Serialize editable rows back into the canonical newline-delimited `key=value`
 * string. Rows whose key and value are both empty are dropped so that a trailing
 * blank editing row never leaks into persisted state; a keyed row with an empty
 * value is preserved as `key=`.
 */
export function serializeConfigureKeyValuePairs(pairs: readonly ConfigureKeyValuePair[]): string {
  return pairs
    .filter((pair) => pair.key.trim().length > 0 || pair.value.trim().length > 0)
    .map((pair) => `${pair.key.trim()}=${pair.value.trim()}`)
    .join("\n");
}

export interface ConfigureChecklistItem {
  id: string;
  label: string;
  detail: string;
  tone: ConfigureTone;
  anchorId: string;
}

export interface ConfigureActivationSummary {
  items: ConfigureChecklistItem[];
  blockerCount: number;
  readyCount: number;
  summaryLabel: string;
  tone: ConfigureTone;
}

/**
 * Map a readiness/gap label to the Configure section that most likely fixes it, so a
 * checklist row or production gap can deep-link the operator straight to the relevant
 * editor instead of leaving them to scroll.
 */
export function resolveConfigureAnchorForLabel(label: string): string {
  const normalized = label.toLowerCase();
  if (normalized.includes("chart")) {
    return "configure-section-chart";
  }
  if (normalized.includes("rule") || normalized.includes("posting")) {
    return "configure-section-rules";
  }
  if (
    normalized.includes("dimension") ||
    normalized.includes("external gl") ||
    normalized.includes("provider") ||
    normalized.includes("mapping") ||
    normalized.includes("tenant") ||
    normalized.includes("approval")
  ) {
    return "configure-section-mappings";
  }
  if (normalized.includes("ledger") || normalized.includes("book")) {
    return "configure-section-books";
  }
  if (normalized.includes("activat") || normalized.includes("certif") || normalized.includes("migration")) {
    return "configure-section-activation";
  }
  return "configure-section-setup";
}

/** Resolve the deep-link anchor for a production readiness gap card. */
export function resolveConfigureGapAnchor(gap: AccountingProductionGapViewModel): string {
  return resolveConfigureAnchorForLabel(`${gap.areaLabel} ${gap.label}`);
}

/**
 * Build the activation checklist rail model from setup readiness rows and production
 * readiness blockers. Everything here is a re-projection of data already present on the
 * view-model — no new fetch or state.
 */
export function buildConfigureActivationSummary(
  view: Pick<AccountingConfigurationViewModel, "setupReadinessRows" | "productionReadiness" | "canActivate">
): ConfigureActivationSummary {
  const items: ConfigureChecklistItem[] = [];

  for (const row of view.setupReadinessRows ?? []) {
    items.push({
      id: `setup-${row.id}`,
      label: row.label,
      detail: row.value,
      tone: row.tone,
      anchorId: resolveConfigureAnchorForLabel(row.label)
    });
  }

  for (const issue of view.productionReadiness?.blockerIssues ?? []) {
    items.push({
      id: `blocker-${issue.id}`,
      label: issue.label,
      detail: issue.suggestedAction,
      tone: issue.tone === "default" ? "warning" : issue.tone,
      anchorId: resolveConfigureAnchorForLabel(issue.label)
    });
  }

  const blockerCount = items.filter((item) => item.tone === "danger").length;
  const readyCount = items.filter((item) => item.tone === "success").length;
  const warningCount = items.filter((item) => item.tone === "warning").length;

  let tone: ConfigureTone = "success";
  if (blockerCount > 0) {
    tone = "danger";
  } else if (warningCount > 0) {
    tone = "warning";
  } else if (!view.canActivate) {
    tone = "warning";
  }

  const summaryLabel = view.canActivate
    ? "Ready to activate"
    : blockerCount > 0
      ? `${blockerCount} blocker${blockerCount === 1 ? "" : "s"} before activation`
      : `${Math.max(items.length - readyCount, 0)} item${items.length - readyCount === 1 ? "" : "s"} outstanding`;

  return { items, blockerCount, readyCount, summaryLabel, tone };
}

export interface ConfigureReadinessReviewItem {
  id: string;
  label: string;
  detail: string;
  tone: Exclude<ConfigureTone, "default" | "success">;
  anchorId: string;
}

export interface ConfigureOperationalReadinessSummary {
  statusLabel: "Assessing" | "Unavailable" | "Review required" | "Ready";
  tone: ConfigureTone;
  gateLabel: string;
  issueSummaryLabel: string;
  explanation: string;
  reviewItems: ConfigureReadinessReviewItem[];
}

const UNAVAILABLE_READINESS_PATTERN = /\b(unavailable|not loaded|no (?:readiness|tenant|dimensional|retained|component)|missing)\b/i;

function addConfigureReadinessReviewItem(
  items: ConfigureReadinessReviewItem[],
  item: ConfigureReadinessReviewItem
): void {
  if (!items.some((candidate) => candidate.id === item.id)) {
    items.push(item);
  }
}

/**
 * Build the Configure page's overall operational-readiness verdict. The shared
 * production-readiness score remains useful source evidence, but it is not sufficient
 * by itself: absent component checks, unavailable rollout/admin evidence, pending
 * editor changes, an unexecuted regression suite, and a closed activation gate all
 * keep the browser presentation fail-closed.
 */
export function buildConfigureOperationalReadinessSummary(
  view: Pick<
    AccountingConfigurationViewModel,
    | "productionReadiness"
    | "chartAccountEditor"
    | "productionCertificationProfile"
    | "tenantAdministrationProfile"
    | "externalGlMappingProfile"
    | "ruleTestSuite"
    | "canActivate"
    | "activateDisabledReason"
  >
): ConfigureOperationalReadinessSummary {
  const readiness = view.productionReadiness;
  const reviewItems: ConfigureReadinessReviewItem[] = [];
  const addReviewItem = (item: ConfigureReadinessReviewItem): void => {
    addConfigureReadinessReviewItem(reviewItems, item);
  };

  if (readiness.errorText) {
    addReviewItem({
      id: "assessment-unavailable",
      label: "Shared assessment unavailable",
      detail: "Refresh the shared accounting readiness assessment before relying on this configuration.",
      tone: "danger",
      anchorId: "configure-section-setup"
    });
  } else if (!readiness.loading && readiness.components.length === 0) {
    addReviewItem({
      id: "component-checks-unavailable",
      label: "Control-plane checks unavailable",
      detail: "The shared assessment returned no component checks, so its score cannot establish overall readiness.",
      tone: "danger",
      anchorId: "configure-section-setup"
    });
  }

  if (!readiness.loading && readiness.statusLabel.toLowerCase() !== "ready") {
    addReviewItem({
      id: "shared-assessment-review",
      label: "Shared assessment requires review",
      detail: `The shared accounting assessment is ${readiness.statusLabel.toLowerCase()}.`,
      tone: readiness.statusLabel.toLowerCase() === "unavailable" ? "danger" : "warning",
      anchorId: "configure-section-setup"
    });
  }

  if (UNAVAILABLE_READINESS_PATTERN.test(readiness.ledgerBookRolloutLabel)) {
    addReviewItem({
      id: "ledger-rollout-evidence",
      label: "Ledger-book rollout evidence unavailable",
      detail: "Load retained rollout and workflow-control evidence for the selected ledger book.",
      tone: "danger",
      anchorId: "configure-section-books"
    });
  }

  if (UNAVAILABLE_READINESS_PATTERN.test(readiness.tenantAdministrationLabel)) {
    addReviewItem({
      id: "tenant-administration-evidence",
      label: "Tenant administration evidence unavailable",
      detail: "Load retained tenant and company administration controls before treating the configuration as production-ready.",
      tone: "danger",
      anchorId: "configure-section-mappings"
    });
  }

  if (UNAVAILABLE_READINESS_PATTERN.test(readiness.dimensionalReportingLabel)) {
    addReviewItem({
      id: "dimensional-reporting-evidence",
      label: "Dimensional reporting evidence unavailable",
      detail: "Verify retained ledger, query, report, and export dimension controls.",
      tone: "warning",
      anchorId: "configure-section-mappings"
    });
  }

  if (/live posting disabled/i.test(readiness.externalGlLabel)) {
    addReviewItem({
      id: "external-gl-guarded-mode",
      label: "External GL is guarded-export only",
      detail: "Live posting is disabled; confirm the retained import, mapping, reconciliation, and guarded-export path.",
      tone: "warning",
      anchorId: "configure-section-mappings"
    });
  }

  const pendingEditorCount = [
    view.chartAccountEditor,
    view.productionCertificationProfile,
    view.tenantAdministrationProfile,
    view.externalGlMappingProfile
  ].filter((editor) => editor.canSave).length;
  if (pendingEditorCount > 0) {
    addReviewItem({
      id: "pending-editor-changes",
      label: `${pendingEditorCount} editor${pendingEditorCount === 1 ? " has" : "s have"} unsaved changes`,
      detail: "Save or discard pending editor changes, then refresh readiness before activation.",
      tone: "warning",
      anchorId: "configure-section-activation"
    });
  }

  if (!view.ruleTestSuite) {
    addReviewItem({
      id: "rule-suite-not-run",
      label: "Latest rule suite not run",
      detail: "Run the accounting rule regression suite against the current saved configuration.",
      tone: "warning",
      anchorId: "configure-section-rules"
    });
  } else if (view.ruleTestSuite.statusTone !== "success") {
    addReviewItem({
      id: "rule-suite-review",
      label: "Latest rule suite requires review",
      detail: view.ruleTestSuite.summaryLabel,
      tone: view.ruleTestSuite.statusTone === "danger" ? "danger" : "warning",
      anchorId: "configure-section-rules"
    });
  }

  const activationAlreadyActive = /already active/i.test(view.activateDisabledReason ?? "");
  if (!view.canActivate && !activationAlreadyActive) {
    addReviewItem({
      id: "activation-gate-closed",
      label: "Activation gate closed",
      detail: view.activateDisabledReason ?? "Resolve configuration and production-readiness gates before activation.",
      tone: "danger",
      anchorId: "configure-section-activation"
    });
  }

  for (const issue of readiness.blockerIssues) {
    addReviewItem({
      id: `shared-${issue.id}`,
      label: issue.label,
      detail: issue.suggestedAction,
      tone: issue.tone === "danger" ? "danger" : "warning",
      anchorId: resolveConfigureAnchorForLabel(issue.label)
    });
  }

  const dangerCount = reviewItems.filter((item) => item.tone === "danger").length;
  const statusLabel: ConfigureOperationalReadinessSummary["statusLabel"] = readiness.loading
    ? "Assessing"
    : readiness.errorText
      ? "Unavailable"
      : reviewItems.length > 0
        ? "Review required"
        : "Ready";
  const tone: ConfigureTone = readiness.loading
    ? "default"
    : dangerCount > 0
      ? "danger"
      : reviewItems.length > 0
        ? "warning"
        : "success";
  const issueSummaryLabel = readiness.loading
    ? "Readiness checks are still loading"
    : reviewItems.length > 0
      ? `${reviewItems.length} readiness item${reviewItems.length === 1 ? "" : "s"} require review`
      : "All readiness evidence and activation gates are clear";

  return {
    statusLabel,
    tone,
    gateLabel: readiness.loading
      ? "Checking gates"
      : reviewItems.length > 0
        ? `${reviewItems.length} review item${reviewItems.length === 1 ? "" : "s"}`
        : "All gates clear",
    issueSummaryLabel,
    explanation: "Overall readiness includes shared component checks, retained rollout and administration evidence, current rule tests, pending editor changes, and the activation gate.",
    reviewItems
  };
}

export interface ConfigureSearchEntry {
  id: string;
  label: string;
  sectionLabel: string;
  anchorId: string;
  keywords: string;
}

/**
 * Static jump-to-setting index. Each entry maps a human field/section name to the
 * section anchor that contains it. Kept in one place so search stays in sync with the
 * navigation rather than being hand-maintained across the render tree.
 */
export const CONFIGURE_SEARCH_INDEX: readonly ConfigureSearchEntry[] = [
  { id: "search-setup", label: "Setup readiness", sectionLabel: "Setup", anchorId: "configure-section-setup", keywords: "metrics candidate readiness" },
  { id: "search-books", label: "Ledger book administration", sectionLabel: "Books", anchorId: "configure-section-books", keywords: "book basis policy currency scope" },
  { id: "search-book-wizard", label: "Guided ledger book setup", sectionLabel: "Books", anchorId: "configure-section-books", keywords: "wizard create new book onboarding" },
  { id: "search-chart", label: "Chart account setup", sectionLabel: "Chart", anchorId: "configure-section-chart", keywords: "node path account name type parent" },
  { id: "search-chart-parent", label: "Parent path", sectionLabel: "Chart", anchorId: "configure-section-chart", keywords: "hierarchy tree parent child" },
  { id: "search-certification", label: "Production certification profile", sectionLabel: "Mappings", anchorId: "configure-section-mappings", keywords: "certify evidence readiness" },
  { id: "search-tenant", label: "Tenant administration setup", sectionLabel: "Mappings", anchorId: "configure-section-mappings", keywords: "tenant admin controls sandbox" },
  { id: "search-approval-queue", label: "Approval queue setup", sectionLabel: "Mappings", anchorId: "configure-section-mappings", keywords: "queue segregation role workflow approval" },
  { id: "search-dimension-map", label: "Dimension mapping setup", sectionLabel: "Mappings", anchorId: "configure-section-mappings", keywords: "dimensions provider meridian map" },
  { id: "search-external-gl", label: "External GL provider mapping", sectionLabel: "Mappings", anchorId: "configure-section-mappings", keywords: "quickbooks export account mappings provider" },
  { id: "search-rules", label: "Posting rules studio", sectionLabel: "Rules", anchorId: "configure-section-rules", keywords: "rule dry run predicate formula allocation promotion" },
  { id: "search-activation", label: "Validation and activation", sectionLabel: "Activation", anchorId: "configure-section-activation", keywords: "activate validation audit gate" }
];

/** Case-insensitive, whitespace-tolerant search over the jump-to-setting index. */
export function filterConfigureSearch(query: string, index: readonly ConfigureSearchEntry[] = CONFIGURE_SEARCH_INDEX): ConfigureSearchEntry[] {
  const trimmed = query.trim().toLowerCase();
  if (trimmed.length === 0) {
    return [];
  }
  const terms = trimmed.split(/\s+/);
  return index.filter((entry) => {
    const haystack = `${entry.label} ${entry.sectionLabel} ${entry.keywords}`.toLowerCase();
    return terms.every((term) => haystack.includes(term));
  });
}

export interface ConfigureChangePreviewRow {
  id: string;
  label: string;
  statusLabel: string;
  tone: ConfigureTone;
}

export interface ConfigureChangePreview {
  pendingRows: ConfigureChangePreviewRow[];
  pendingCount: number;
  currentVersionLabel: string;
  activationBadgeLabel: string;
  activationLabel: string;
  activationTone: ConfigureTone;
  headline: string;
}

/**
 * Synthesize a "what will change / what will activation do" preview from the editors'
 * own save-readiness flags. `canSave` on an editor means it holds an unsaved, valid draft;
 * we surface those as pending edits alongside the activation outlook so the operator can
 * review before committing.
 */
export function buildConfigureChangePreview(
  view: Pick<
    AccountingConfigurationViewModel,
    | "chartAccountEditor"
    | "productionCertificationProfile"
    | "tenantAdministrationProfile"
    | "externalGlMappingProfile"
    | "canActivate"
    | "activateDisabledReason"
    | "dryRunPreview"
  >
): ConfigureChangePreview {
  const candidates: Array<{ id: string; label: string; canSave: boolean; reason: string | null }> = [
    { id: "chart", label: "Chart account draft", canSave: view.chartAccountEditor.canSave, reason: view.chartAccountEditor.saveDisabledReason },
    { id: "certification", label: "Certification profile", canSave: view.productionCertificationProfile.canSave, reason: view.productionCertificationProfile.saveDisabledReason },
    { id: "tenant", label: "Tenant administration profile", canSave: view.tenantAdministrationProfile.canSave, reason: view.tenantAdministrationProfile.saveDisabledReason },
    { id: "external-gl", label: "External GL mapping profile", canSave: view.externalGlMappingProfile.canSave, reason: view.externalGlMappingProfile.saveDisabledReason }
  ];

  const pendingRows: ConfigureChangePreviewRow[] = candidates.map((candidate) => ({
    id: candidate.id,
    label: candidate.label,
    statusLabel: candidate.canSave ? "Unsaved draft changes ready to save" : candidate.reason ?? "No pending draft changes",
    tone: candidate.canSave ? "warning" : "default"
  }));

  const pendingCount = pendingRows.filter((row) => row.tone === "warning").length;
  const currentVersionIsActive = /already active/i.test(view.activateDisabledReason ?? "");
  const activationTone: ConfigureTone = view.canActivate
    ? "success"
    : currentVersionIsActive && pendingCount === 0
      ? "default"
      : "warning";
  const currentVersionLabel = currentVersionIsActive
    ? "Current saved version: Active"
    : "Current saved version: Not active";
  const activationBadgeLabel = view.canActivate
    ? "Draft ready to activate"
    : currentVersionIsActive
      ? pendingCount > 0
        ? "Draft promotion blocked"
        : "No draft to activate"
      : "Draft activation blocked";
  const activationLabel = currentVersionIsActive
    ? pendingCount > 0
      ? "The current saved version remains active. Save and re-validate the pending draft changes before promotion."
      : "The current saved version is active. No pending draft changes are available for activation."
    : view.canActivate
      ? view.dryRunPreview
        ? "The pending draft is activation-ready; a dry-run preview is available for the selected rule."
        : "The pending draft is activation-ready."
      : view.activateDisabledReason ?? "Pending draft activation is currently blocked.";

  const headline = pendingCount > 0
    ? `${pendingCount} editor${pendingCount === 1 ? "" : "s"} with unsaved draft changes`
    : "No unsaved draft changes";

  return {
    pendingRows,
    pendingCount,
    currentVersionLabel,
    activationBadgeLabel,
    activationLabel,
    activationTone,
    headline
  };
}

export interface ChartPathSegment {
  id: string;
  label: string;
  path: string;
}

/**
 * Detect the separator used in a chart account path. Chart nodes in Meridian use dotted
 * codes (e.g. `1200.Investments`) but nested paths may use `/`; we honor whichever the
 * operator is already using and default to `.`.
 */
export function detectChartPathSeparator(path: string | null | undefined): string {
  if (path && path.includes("/")) {
    return "/";
  }
  return ".";
}

/** Break a chart path into cumulative breadcrumb segments for the tree/path builder. */
export function buildChartPathSegments(path: string | null | undefined): ChartPathSegment[] {
  const trimmed = (path ?? "").trim();
  if (trimmed.length === 0) {
    return [];
  }
  const separator = detectChartPathSeparator(trimmed);
  const parts = trimmed.split(separator).map((part) => part.trim()).filter((part) => part.length > 0);
  const segments: ChartPathSegment[] = [];
  let accumulated = "";
  for (let index = 0; index < parts.length; index += 1) {
    accumulated = index === 0 ? parts[index] : `${accumulated}${separator}${parts[index]}`;
    segments.push({ id: `segment-${index}`, label: parts[index], path: accumulated });
  }
  return segments;
}

/** Append a new segment to a chart path using the path's existing separator. */
export function appendChartPathSegment(path: string | null | undefined, segment: string | null | undefined): string {
  const cleanSegment = (segment ?? "").trim();
  const trimmed = (path ?? "").trim();
  if (cleanSegment.length === 0) {
    return trimmed;
  }
  if (trimmed.length === 0) {
    return cleanSegment;
  }
  const separator = detectChartPathSeparator(trimmed);
  return `${trimmed}${separator}${cleanSegment}`;
}
