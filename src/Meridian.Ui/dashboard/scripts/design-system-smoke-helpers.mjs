import path from "node:path";
import { tmpdir } from "node:os";

export const DESIGN_SYSTEM_APPEARANCES = [
  { name: "light", os: "light", stored: "light", dark: false },
  { name: "manual-dark", os: "light", stored: "dark", dark: true },
  { name: "system-dark", os: "dark", stored: "system", dark: true },
  { name: "forced-light", os: "dark", stored: "light", dark: false }
];
export const DESIGN_SYSTEM_VIEWPORTS = [
  { width: 1920, height: 1080 }, { width: 1440, height: 900 }, { width: 1366, height: 768 }
];
export const DESIGN_SYSTEM_ROUTES = [
  { path: "accounting/reconciliation", label: "Reconciliation", ready: "Reconciliation break queue" },
  { path: "portfolio", label: "Portfolio", ready: "Execution-linked holdings" },
  { path: "trading", label: "Trading", ready: "Trading" }
];

export function designSystemMatrix() {
  return DESIGN_SYSTEM_VIEWPORTS.flatMap(viewport => DESIGN_SYSTEM_APPEARANCES.flatMap(appearance =>
    DESIGN_SYSTEM_ROUTES.map(route => ({
      ...viewport, appearance, route,
      name: route.path.replaceAll("/", "-") + "-" + viewport.width + "-" + appearance.name
    }))
  ));
}

export function workstationBase(value) {
  const url = new URL(value);
  if (!["http:", "https:"].includes(url.protocol) || url.username || url.password || url.search || url.hash) {
    throw new Error("Workstation URL must be an HTTP(S) URL without credentials, query or fragment");
  }
  if (url.pathname.replace(/\/+$/, "") !== "/workstation") {
    throw new Error("Workstation URL must end in /workstation");
  }
  return url.href.replace(/\/+$/, "");
}

export function parseDesignSystemOptions(args) {
  const options = new Map();
  let expanded = false;
  let help = false;
  for (let index = 0; index < args.length; index++) {
    const argument = args[index];
    if (argument === "--help" || argument === "-h") { help = true; continue; }
    if (argument === "--expanded") { expanded = true; continue; }
    const separator = argument.indexOf("=");
    const key = separator < 0 ? argument : argument.slice(0, separator);
    if (!["--url", "--output", "--backend-url", "--production-url", "--timeout-ms"].includes(key)) {
      throw new Error("Invalid option: " + argument);
    }
    const value = separator < 0 ? args[++index] : argument.slice(separator + 1);
    if (!value || value.startsWith("--")) throw new Error("Missing value for " + key);
    options.set(key, value);
  }
  const base = workstationBase(options.get("--url") || "http://127.0.0.1:5178/workstation");
  const timeoutMs = Number(options.get("--timeout-ms") || 20000);
  if (!Number.isInteger(timeoutMs) || timeoutMs < 1000 || timeoutMs > 120000) {
    throw new Error("--timeout-ms must be an integer between 1000 and 120000");
  }
  const backendBase = options.has("--backend-url") ? workstationBase(options.get("--backend-url")) : null;
  const productionBase = options.has("--production-url") ? workstationBase(options.get("--production-url")) : null;
  if (expanded && (!backendBase || !productionBase)) {
    throw new Error("--expanded requires --backend-url and --production-url to prove workbench gating");
  }
  return {
    base, output: path.resolve(options.get("--output") || path.join(tmpdir(), "meridian-design-system-smoke")),
    timeoutMs, expanded, help, backendBase, productionBase
  };
}

/**
 * Smoke-only GET support for six exact fixture identities. Personal views are empty;
 * history is unavailable/empty, never reconstructed from current task/provider state.
 */
export function readOnlySmokeResponse(url, method = "GET") {
  if (method !== "GET") return null;
  const pathname = new URL(url, "http://meridian.test").pathname;
  const views = new Set([
    "/api/workstation/tables/close-checklist-progress-data/views",
    "/api/workstation/tables/portfolio-positions/views",
    "/api/workstation/tables/provider-evidence-age-data/views",
    "/api/workstation/tables/accounting-reconciliation-breaks/views"
  ]);
  let body, limitation;
  if (views.has(pathname)) {
    body = [];
    limitation = "Synthetic empty personal-view list; persistence is not exercised.";
  } else if (pathname === "/api/workstation/operations/continuity/79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6/checklist/history") {
    body = {
      workflowId: "79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6", asOf: "2026-11-11T00:00:00Z",
      status: "Unavailable", reason: "Synthetic smoke fixture has no retained checklist snapshots.",
      coverageStart: null, coverageEnd: null, isPartial: true, missingAuditIds: [], versionGaps: [], observations: []
    };
    limitation = "Retained checklist history is unavailable; no observations are fabricated.";
  } else if (pathname === "/api/workstation/provider-verification-history/provider-alpaca-paper") {
    body = { connectionId: "provider-alpaca-paper", providerId: "alpaca", asOfUtc: "2026-11-11T00:00:00Z", observations: [], isTruncated: false };
    limitation = "Synthetic empty verification history; no provider uptime or live verification claim.";
  } else return null;
  return {
    status: 200, body,
    headers: { "x-meridian-dev-fixture": "true", "x-meridian-smoke-support": "empty-read-only" },
    smokeFixture: { method, path: pathname, limitation }
  };
}

/** Explicit fixture additions cover observed read-only support and one harmless visit record. */
export function supportingSmokeResponse(url, method = "GET") {
  const readOnly = readOnlySmokeResponse(url, method);
  if (readOnly) return readOnly;
  const pathname = new URL(url, "http://meridian.test").pathname;
  if (method === "GET" && pathname === "/api/risk/escalations") return { status: 200, body: [] };
  if (["GET", "POST"].includes(method) && pathname === "/api/workstation/operator/visits/reconciliation-breaks") {
    return {
      status: 200,
      body: { surface: "reconciliation-breaks", previousVisitAt: null, visitedAt: "2026-06-30T18:00:00Z" }
    };
  }
  return null;
}

export function parseCssColor(value) {
  if (value === "transparent") return [0, 0, 0, 0];
  const hex = /^#([0-9a-f]{6})$/i.exec(value);
  if (hex) return [0, 2, 4].map(index => parseInt(hex[1].slice(index, index + 2), 16)).concat(1);
  const rgb = /^rgba?\(([^)]+)\)$/.exec(value);
  if (rgb) {
    const values = rgb[1].split(/[,/\s]+/).filter(Boolean).map(Number);
    if (values.length >= 3 && values.every(Number.isFinite)) return [...values.slice(0, 3), values[3] ?? 1];
  }
  const srgb = /^color\(srgb\s+([^)]+)\)$/.exec(value);
  if (srgb) {
    const values = srgb[1].split(/[/\s]+/).filter(Boolean).map(Number);
    if (values.length >= 3 && values.every(Number.isFinite)) return [...values.slice(0, 3).map(v => v * 255), values[3] ?? 1];
  }
  // Chromium preserves the interpolation space for Tailwind's translucent
  // color-mix surfaces. Convert that actual Oklab value before compositing.
  const oklab = /^oklab\(([^)]+)\)$/.exec(value);
  if (oklab) {
    const values = oklab[1].split(/[/\s]+/).filter(Boolean).map(Number);
    if (values.length >= 3 && values.every(Number.isFinite)) {
      const [L, a, b, alpha = 1] = values;
      const l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3;
      const m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3;
      const s = (L - 0.0894841775 * a - 1.291485548 * b) ** 3;
      return [4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
        -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
        -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s]
        .map(channel => Math.max(0, Math.min(1, channel <= 0.0031308 ? 12.92 * channel : 1.055 * channel ** (1 / 2.4) - 0.055)) * 255)
        .concat(alpha);
    }
  }
  throw new Error("Unsupported computed color: " + value);
}

export function compositeColor(foreground, background) {
  const alpha = foreground[3] + background[3] * (1 - foreground[3]);
  if (!alpha) return [0, 0, 0, 0];
  return foreground.slice(0, 3).map((value, index) =>
    (value * foreground[3] + background[index] * background[3] * (1 - foreground[3])) / alpha
  ).concat(alpha);
}

export function contrastRatio(foreground, background) {
  const luminance = color => color.slice(0, 3).map(channel => {
    const c = channel / 255;
    return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
  }).reduce((sum, value, index) => sum + value * [0.2126, 0.7152, 0.0722][index], 0);
  const a = luminance(foreground), b = luminance(background);
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

export function assertHealthyScene(scene) {
  for (const key of ["exceptions", "errors", "assetFailures", "unexpectedRequests"]) {
    if (scene[key]?.length) throw new Error(key + ": " + scene[key].join(" | "));
  }
}

export function chromiumLaunchOptions(env = process.env) {
  const executablePath = env.MERIDIAN_CHROMIUM_PATH || env.CHROMIUM_EXECUTABLE_PATH;
  return { ...(executablePath ? { executablePath } : {}), args: ["--no-sandbox"] };
}
