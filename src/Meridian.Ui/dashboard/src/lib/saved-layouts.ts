// Saved operator layouts: a named snapshot of the workstation arrangement — the
// active route (scope query params and all), the operating scope, the display
// density, and which companion panes are popped out. Serialized through the
// versioned view-state envelope (lib/view-state-envelope.ts) so persistence, the
// version gate, and defensive decode are shared with the rest of the workstation.
//
// User layouts live in localStorage; team presets ship as read-only in-code seeds.
// Restore replays the snapshot: navigate the route, apply density/scope, and
// re-open the recorded companion panes (window positioning across monitors is
// browser-permission-limited, so a restored pop-out may land where the browser
// chooses and be dragged once).
import { COMPANION_PANES } from "@/lib/companion-pane/pane-window";
import { DEFAULT_DENSITY, isDensity, type Density } from "@/lib/density";
import { getLocalStorage } from "@/lib/theme";
import {
  VIEW_STATE_ENVELOPE_VERSION,
  decodeViewStateEnvelope,
  encodeViewStateEnvelope
} from "@/lib/view-state-envelope";
import type { AppShellOperatingScopeInput } from "@/app-shell.operating-scope";

export const SAVED_LAYOUTS_SCREEN = "operator-layout";
export const SAVED_LAYOUTS_STORAGE_KEY = "meridian.workstation.layouts.v1";
export const MAX_SAVED_LAYOUTS = 40;
export const MAX_LAYOUT_NAME_LENGTH = 80;
export const MAX_LAYOUT_DESCRIPTION_LENGTH = 200;
const MAX_LAYOUT_ROUTE_LENGTH = 512;

const OPERATING_SCOPE_KEYS = [
  "symbol",
  "fundAccountId",
  "runId",
  "provider",
  "from",
  "to",
  "date",
  "asOf"
] as const;

export interface SavedLayoutState {
  /** Route to restore, e.g. `/accounting/reconciliation?fundAccountId=fund-a`. */
  route: string;
  operatingScope: AppShellOperatingScopeInput;
  density: Density;
  /** Companion pane ids to re-open on restore (see COMPANION_PANES). */
  panes: string[];
}

export type SavedLayoutSource = "user" | "team";

export interface SavedLayout {
  id: string;
  name: string;
  description: string | null;
  source: SavedLayoutSource;
  state: SavedLayoutState;
}

/** Persisted shape of a user layout — the state is carried as an envelope token. */
interface StoredUserLayout {
  id: string;
  name: string;
  description: string | null;
  token: string;
}

// --- Serialization -------------------------------------------------------------

/** Encode a layout's state as a view-state envelope token, or null if oversized. */
export function encodeLayoutStateToken(state: SavedLayoutState): string | null {
  return encodeViewStateEnvelope({
    v: VIEW_STATE_ENVELOPE_VERSION,
    screen: SAVED_LAYOUTS_SCREEN,
    state: serializeLayoutState(state)
  });
}

/** Decode a token back into layout state; bad/foreign/oversized tokens yield null. */
export function decodeLayoutStateToken(token: string | null | undefined): SavedLayoutState | null {
  const envelope = decodeViewStateEnvelope(token);
  if (!envelope || envelope.screen !== SAVED_LAYOUTS_SCREEN) {
    return null;
  }
  return normalizeLayoutState(envelope.state);
}

function serializeLayoutState(state: SavedLayoutState): Record<string, unknown> {
  return {
    route: state.route,
    operatingScope: compactScope(state.operatingScope),
    density: state.density,
    panes: state.panes
  };
}

// --- Normalization -------------------------------------------------------------

/** Assemble a clean, normalized layout state from raw workstation inputs. */
export function captureLayoutState(input: {
  route: string;
  operatingScope: AppShellOperatingScopeInput;
  density: Density;
  panes: string[];
}): SavedLayoutState {
  return {
    route: normalizeRoute(input.route),
    operatingScope: compactScope(input.operatingScope),
    density: isDensity(input.density) ? input.density : DEFAULT_DENSITY,
    panes: normalizePanes(input.panes)
  };
}

export function normalizeLayoutState(value: unknown): SavedLayoutState | null {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return null;
  }
  const record = value as Record<string, unknown>;
  const route = normalizeRoute(record.route);
  if (!route) {
    return null;
  }
  return {
    route,
    operatingScope: normalizeScope(record.operatingScope),
    density: isDensity(record.density) ? record.density : DEFAULT_DENSITY,
    panes: normalizePanes(record.panes)
  };
}

/**
 * Only accept internal same-origin app routes — a leading single slash, no
 * protocol-relative `//`, no scheme. Guards restore against navigating off-site
 * from a tampered stored token.
 */
function normalizeRoute(value: unknown): string {
  if (typeof value !== "string") {
    return "";
  }
  const trimmed = value.trim();
  if (
    !trimmed.startsWith("/")
    || trimmed.startsWith("//")
    || trimmed.length > MAX_LAYOUT_ROUTE_LENGTH
    || /[\s]/.test(trimmed)
  ) {
    return "";
  }
  return trimmed;
}

function normalizeScope(value: unknown): AppShellOperatingScopeInput {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return {};
  }
  const record = value as Record<string, unknown>;
  const scope: AppShellOperatingScopeInput = {};
  for (const key of OPERATING_SCOPE_KEYS) {
    const raw = record[key];
    if (typeof raw === "string" && raw.trim().length > 0) {
      scope[key] = raw.trim().slice(0, 72);
    }
  }
  return scope;
}

function compactScope(scope: AppShellOperatingScopeInput): AppShellOperatingScopeInput {
  const compacted: AppShellOperatingScopeInput = {};
  for (const key of OPERATING_SCOPE_KEYS) {
    const raw = scope?.[key];
    if (typeof raw === "string" && raw.trim().length > 0) {
      compacted[key] = raw.trim().slice(0, 72);
    }
  }
  return compacted;
}

function normalizePanes(value: unknown): string[] {
  if (!Array.isArray(value)) {
    return [];
  }
  const seen = new Set<string>();
  const result: string[] = [];
  for (const id of value) {
    if (
      typeof id === "string"
      && Object.prototype.hasOwnProperty.call(COMPANION_PANES, id)
      && !seen.has(id)
    ) {
      seen.add(id);
      result.push(id);
    }
  }
  return result;
}

// --- Store (user layouts) ------------------------------------------------------

export function readUserLayouts(storage: Storage | null = getLocalStorage()): SavedLayout[] {
  try {
    const raw = storage?.getItem(SAVED_LAYOUTS_STORAGE_KEY);
    if (!raw) {
      return [];
    }
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) {
      return [];
    }
    const layouts: SavedLayout[] = [];
    for (const entry of parsed) {
      const layout = hydrateStoredLayout(entry);
      if (layout) {
        layouts.push(layout);
      }
    }
    return layouts;
  } catch {
    return [];
  }
}

export function writeUserLayouts(
  layouts: SavedLayout[],
  storage: Storage | null = getLocalStorage()
): void {
  try {
    const stored = layouts
      .slice(0, MAX_SAVED_LAYOUTS)
      .map(toStoredLayout)
      .filter((entry): entry is StoredUserLayout => entry !== null);
    if (stored.length === 0) {
      storage?.removeItem(SAVED_LAYOUTS_STORAGE_KEY);
    } else {
      storage?.setItem(SAVED_LAYOUTS_STORAGE_KEY, JSON.stringify(stored));
    }
  } catch {
    // Layout persistence is best-effort when browser storage is blocked.
  }
}

/**
 * Insert or replace a layout (matched by id, else by case-insensitive name so
 * re-saving "Month-End Close" overwrites rather than duplicates), newest first.
 */
export function upsertUserLayout(
  layout: SavedLayout,
  storage: Storage | null = getLocalStorage()
): SavedLayout[] {
  const existing = readUserLayouts(storage);
  const normalizedName = layout.name.trim().toLowerCase();
  const remaining = existing.filter(
    (entry) => entry.id !== layout.id && entry.name.trim().toLowerCase() !== normalizedName
  );
  const next = [layout, ...remaining].slice(0, MAX_SAVED_LAYOUTS);
  writeUserLayouts(next, storage);
  return next;
}

export function removeUserLayout(
  id: string,
  storage: Storage | null = getLocalStorage()
): SavedLayout[] {
  const next = readUserLayouts(storage).filter((entry) => entry.id !== id);
  writeUserLayouts(next, storage);
  return next;
}

/** Build a user layout from a name + captured state. Rejects an empty name. */
export function createUserLayout(input: {
  id: string;
  name: string;
  description?: string | null;
  state: SavedLayoutState;
}): SavedLayout | null {
  const name = input.name.trim().slice(0, MAX_LAYOUT_NAME_LENGTH);
  if (!name) {
    return null;
  }
  const description = input.description?.trim().slice(0, MAX_LAYOUT_DESCRIPTION_LENGTH) || null;
  return {
    id: input.id,
    name,
    description,
    source: "user",
    state: input.state
  };
}

/** Session-unique id for a new layout, degrading gracefully without crypto. */
export function generateLayoutId(): string {
  const cryptoRef = typeof globalThis !== "undefined" ? globalThis.crypto : undefined;
  if (cryptoRef?.randomUUID) {
    return `layout-${cryptoRef.randomUUID()}`;
  }
  // Fold in Math.random() so rapid successive calls in the same millisecond do not
  // collide (the crypto path above is preferred wherever available).
  return `layout-${Math.abs(hashString(`${Date.now()}-${Math.random()}`))}`;
}

function hashString(text: string): number {
  let hash = 0;
  for (let index = 0; index < text.length; index += 1) {
    hash = (hash << 5) - hash + text.charCodeAt(index);
    hash |= 0;
  }
  return hash;
}

function hydrateStoredLayout(entry: unknown): SavedLayout | null {
  if (typeof entry !== "object" || entry === null) {
    return null;
  }
  const record = entry as Record<string, unknown>;
  const id = typeof record.id === "string" ? record.id : null;
  const name = typeof record.name === "string" ? record.name.trim() : null;
  const token = typeof record.token === "string" ? record.token : null;
  if (!id || !name || !token) {
    return null;
  }
  const state = decodeLayoutStateToken(token);
  if (!state) {
    return null;
  }
  return {
    id,
    name: name.slice(0, MAX_LAYOUT_NAME_LENGTH),
    description: typeof record.description === "string"
      ? record.description.trim().slice(0, MAX_LAYOUT_DESCRIPTION_LENGTH) || null
      : null,
    source: "user",
    state
  };
}

function toStoredLayout(layout: SavedLayout): StoredUserLayout | null {
  const token = encodeLayoutStateToken(layout.state);
  if (!token) {
    return null;
  }
  return {
    id: layout.id,
    name: layout.name,
    description: layout.description,
    token
  };
}

// --- Team presets (read-only seeds) --------------------------------------------

/**
 * Shared team layouts. Read-only: restorable by anyone, never edited or deleted
 * from the switcher. Kept as normalized in-code seeds so they ship with the build.
 */
export const TEAM_LAYOUT_PRESETS: readonly SavedLayout[] = [
  {
    id: "team-month-end-close",
    name: "Month-End Close",
    description: "Reconciliation front and center, live quotes popped out, close calendar in reach.",
    source: "team",
    state: {
      route: "/accounting/reconciliation",
      operatingScope: {},
      density: "compact",
      panes: ["live-quotes"]
    }
  },
  {
    id: "team-trading-morning",
    name: "Trading Morning",
    description: "Trading readiness with the watchlist and live quotes torn off for the open.",
    source: "team",
    state: {
      route: "/trading/readiness",
      operatingScope: {},
      density: "default",
      panes: ["watchlist", "live-quotes"]
    }
  },
  {
    id: "team-reporting-review",
    name: "Reporting Review",
    description: "Report packs with evidence at hand for pack approval and sign-off.",
    source: "team",
    state: {
      route: "/reporting/report-packs",
      operatingScope: {},
      density: "default",
      panes: []
    }
  }
];

export function listTeamLayouts(): SavedLayout[] {
  return TEAM_LAYOUT_PRESETS.map((preset) => ({
    ...preset,
    state: { ...preset.state, panes: [...preset.state.panes] }
  }));
}

// --- Restore -------------------------------------------------------------------

export interface LayoutRestorePlan {
  route: string;
  density: Density;
  operatingScope: AppShellOperatingScopeInput;
  panes: string[];
}

/** Pure translation of a layout into the actions restore must perform. */
export function buildLayoutRestorePlan(layout: SavedLayout): LayoutRestorePlan {
  return {
    route: layout.state.route,
    density: layout.state.density,
    operatingScope: compactScope(layout.state.operatingScope),
    panes: normalizePanes(layout.state.panes)
  };
}
