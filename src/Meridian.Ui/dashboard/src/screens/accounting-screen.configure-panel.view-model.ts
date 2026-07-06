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
    statusLabel: candidate.canSave ? "Unsaved changes ready to save" : candidate.reason ?? "No pending changes",
    tone: candidate.canSave ? "warning" : "default"
  }));

  const pendingCount = pendingRows.filter((row) => row.tone === "warning").length;

  const activationTone: ConfigureTone = view.canActivate ? "success" : "warning";
  const activationLabel = view.canActivate
    ? view.dryRunPreview
      ? "Configuration is activation-ready; a dry-run preview is available for the selected rule."
      : "Configuration is activation-ready."
    : view.activateDisabledReason ?? "Activation is currently blocked.";

  const headline = pendingCount > 0
    ? `${pendingCount} editor${pendingCount === 1 ? "" : "s"} with unsaved changes`
    : "No unsaved editor changes";

  return { pendingRows, pendingCount, activationLabel, activationTone, headline };
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
