import { existsSync, readdirSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

function readDashboardStyles() {
  return readFileSync(resolve(process.cwd(), "src/styles/index.css"), "utf8");
}

function readManualDarkHexToken(styles: string, token: string) {
  const darkScope = styles.match(/:root\[data-theme="dark"\]\s*\{([\s\S]*?)\n {2}\}/)?.[1];
  if (!darkScope) {
    throw new Error("Manual dark-theme scope is missing");
  }

  const escapedToken = token.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const value = darkScope.match(new RegExp(`${escapedToken}:\\s*(#[0-9A-F]{6})`, "i"))?.[1];
  if (!value) {
    throw new Error(`Dark-theme token ${token} is missing or is not a six-digit hex color`);
  }

  return value.toUpperCase();
}

function relativeLuminance(hexColor: string) {
  const channels = hexColor
    .slice(1)
    .match(/.{2}/g)
    ?.map((channel) => Number.parseInt(channel, 16) / 255);
  if (!channels || channels.length !== 3 || channels.some(Number.isNaN)) {
    throw new Error(`Invalid six-digit hex color: ${hexColor}`);
  }

  const [red, green, blue] = channels.map((channel) =>
    channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4
  );
  return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
}

function contrastRatio(foreground: string, background: string) {
  const foregroundLuminance = relativeLuminance(foreground);
  const backgroundLuminance = relativeLuminance(background);
  const lighter = Math.max(foregroundLuminance, backgroundLuminance);
  const darker = Math.min(foregroundLuminance, backgroundLuminance);

  return (lighter + 0.05) / (darker + 0.05);
}

function readWorkspaceSurfaceStyles() {
  return readFileSync(resolve(process.cwd(), "src/styles/workspace-surface.css"), "utf8");
}

function readWorkspaceNavStyles() {
  return readFileSync(resolve(process.cwd(), "src/styles/workspace-nav.css"), "utf8");
}

function readDashboardEntry() {
  return readFileSync(resolve(process.cwd(), "src/main.tsx"), "utf8");
}

function readTailwindConfig() {
  return readFileSync(resolve(process.cwd(), "tailwind.config.ts"), "utf8");
}

function readAccountingScreen() {
  return readFileSync(resolve(process.cwd(), "src/screens/accounting-screen.tsx"), "utf8");
}

function readAccountingSecurityMasterPanels() {
  return readFileSync(resolve(process.cwd(), "src/screens/accounting-screen.security-master-panels.tsx"), "utf8");
}

function readAccountingViewModel() {
  return readFileSync(resolve(process.cwd(), "src/screens/accounting-screen.view-model.ts"), "utf8");
}

function readDesignSystemPrimitives() {
  return readFileSync(resolve(process.cwd(), "src/design-system/primitives.tsx"), "utf8");
}

function readReferenceWorkbenchPreview() {
  return readFileSync(resolve(process.cwd(), "../../../Meridian Design System/preview/reference-workbench.html"), "utf8");
}

function readRepositoryFile(path: string) {
  return readFileSync(resolve(process.cwd(), "../../..", path), "utf8");
}

function readDesignSystemPackageFile(path: string) {
  return readFileSync(resolve(process.cwd(), "../../../Meridian Design System", path), "utf8");
}

function resolveDashboardAsset(path: string) {
  return resolve(process.cwd(), "src/assets", path);
}

function resolveDashboardPrimitive(path: string) {
  return resolve(process.cwd(), "src/components/ui", path);
}

function readDashboardPrimitive(path: string) {
  return readFileSync(resolveDashboardPrimitive(path), "utf8");
}

describe("dashboard design-system contract", () => {
  it("vendors the Meridian design-system source package beside the dashboard bridge", () => {
    const manifest = JSON.parse(readDesignSystemPackageFile("_ds_manifest.json")) as {
      components: Array<{ name: string; sourcePath: string }>;
      tokens: Array<{ name: string; value: string; definedIn: string; scope?: string }>;
    };
    const componentSourceByName = new Map(
      manifest.components.map((component) => [component.name, component.sourcePath])
    );
    const defaultThemeTokenByName = new Map(
      manifest.tokens
        .filter((token) => token.definedIn === "tokens/theme.css" && token.scope === undefined)
        .map((token) => [token.name, token])
    );

    expect(componentSourceByName.get("Button")).toBe("components/core/Button.jsx");
    expect(componentSourceByName.get("DenseDataTable")).toBe("components/data/DenseDataTable.jsx");
    expect(componentSourceByName.get("WorkstationTopbar")).toBe("components/shell/WorkstationTopbar.jsx");
    expect(componentSourceByName.get("StatusBar")).toBe("components/shell/StatusBar.jsx");
    expect(componentSourceByName.get("SessionControls")).toBe("components/shell/SessionControls.jsx");
    expect(componentSourceByName.get("TrialBalance")).toBe("components/accounting/TrialBalance.jsx");
    expect(componentSourceByName.get("AgingTable")).toBe("components/accounting/AgingTable.jsx");
    expect(componentSourceByName.get("ReconciliationPanel")).toBe("components/accounting/ReconciliationPanel.jsx");
    expect(defaultThemeTokenByName.get("--theme-bg-canvas")).toMatchObject({
      value: "#DEE3EA",
      definedIn: "tokens/theme.css"
    });
    expect(defaultThemeTokenByName.get("--theme-accent")).toMatchObject({
      value: "#2F6F8F",
      definedIn: "tokens/theme.css"
    });

    expect(existsSync(resolve(process.cwd(), "../../../Meridian Design System/CHANGELOG.md"))).toBe(true);
    expect(existsSync(resolve(process.cwd(), "../../../Meridian Design System/PATTERNS.md"))).toBe(true);
    expect(existsSync(resolve(process.cwd(), "../../../Meridian Design System/guidelines/TOKEN_REFERENCE.md"))).toBe(true);
    expect(existsSync(resolve(process.cwd(), "../../../Meridian Design System/components/operations/ReadinessPanel.jsx"))).toBe(true);
    expect(existsSync(resolve(process.cwd(), "../../../Meridian Design System/templates/report-library/screen.jsx"))).toBe(true);
    expect(existsSync(resolve(process.cwd(), "../../../Meridian Design System/screenshots/core-top.png"))).toBe(true);
  });

  it("declares dashboard-native live adapters backed by manifest component names", () => {
    const manifest = JSON.parse(readDesignSystemPackageFile("_ds_manifest.json")) as {
      components: Array<{ name: string; sourcePath: string }>;
    };
    const componentNames = new Set(manifest.components.map((component) => component.name));
    const bridge = readRepositoryFile("src/Meridian.Ui/dashboard/src/design-system/assets.ts");
    const primitives = readDesignSystemPrimitives();
    const button = readRepositoryFile("src/Meridian.Ui/dashboard/src/design-system/button.tsx");
    const badge = readRepositoryFile("src/Meridian.Ui/dashboard/src/design-system/badge.tsx");
    const status = readRepositoryFile("src/Meridian.Ui/dashboard/src/design-system/status.tsx");
    const masthead = readRepositoryFile("src/Meridian.Ui/dashboard/src/design-system/masthead.tsx");
    const navRail = readRepositoryFile("src/Meridian.Ui/dashboard/src/design-system/nav-rail.tsx");
    const topbar = readRepositoryFile("src/Meridian.Ui/dashboard/src/components/meridian/workstation-topbar.tsx");
    const nav = readRepositoryFile("src/Meridian.Ui/dashboard/src/components/meridian/workspace-nav.tsx");
    const statusBar = readRepositoryFile("src/Meridian.Ui/dashboard/src/components/meridian/workstation-status-bar.tsx");
    const trialBalance = readRepositoryFile("src/Meridian.Ui/dashboard/src/components/accounting/TrialBalanceTable.tsx");
    const aging = readRepositoryFile("src/Meridian.Ui/dashboard/src/components/accounting/AgingTable.tsx");
    const reconciliation = readRepositoryFile("src/Meridian.Ui/dashboard/src/components/accounting/ReconciliationComparisonPanel.tsx");

    const liveComponentNames = [
      "Button",
      "Badge",
      "NavRail",
      "WorkstationTopbar",
      "TrustStrip",
      "StatusBar",
      "SessionControls",
      "TrialBalance",
      "AgingTable",
      "ReconciliationPanel"
    ];
    for (const componentName of liveComponentNames) {
      expect(componentNames.has(componentName)).toBe(true);
      expect(bridge).toContain(`"${componentName}"`);
    }

    expect(bridge).toContain("DESIGN_SYSTEM_MANIFEST_FILE");
    expect(bridge).toContain("DESIGN_SYSTEM_SHELL_ASSET_MAPPINGS");
    expect(bridge).toContain("DESIGN_SYSTEM_CORE_COMPONENTS");
    expect(primitives).toContain('export { DesignSystemButton, type DesignSystemButtonProps } from "@/design-system/button"');
    expect(primitives).toContain('export { DesignSystemBadge, type DesignSystemBadgeProps } from "@/design-system/badge"');
    expect(primitives).toContain('export { DesignSystemTrustStrip } from "@/design-system/trust-strip"');
    expect(button).toContain("export const DesignSystemButton");
    expect(badge).toContain("export function DesignSystemBadge");
    expect(badge).toContain("mds-badge__dot");
    expect(status).toContain("export function DesignSystemStatus");
    expect(masthead).toContain("export function DesignSystemMasthead");
    expect(masthead).toContain("workstation-masthead mds-masthead ws-masthead");
    expect(navRail).toContain("export function DesignSystemNavRail");
    expect(navRail).toContain("mds-nav-rail op-rail");
    expect(navRail).toContain("operator-rail-nav op-rail__nav");
    expect(navRail).toContain("operator-nav-item op-nav-item");
    expect(topbar).toContain("DesignSystemMasthead");
    expect(nav).toContain("DesignSystemNavRail");
    expect(nav).toContain("meridianWorkspaceIconAssets[item.key]");
    expect(primitives).not.toContain("../../../Meridian Design System/components");
    expect(statusBar).toContain("export function WorkstationStatusBar");
    expect(trialBalance).toContain("export function TrialBalanceTable");
    expect(aging).toContain("export function AgingTable");
    expect(reconciliation).toContain("export function ReconciliationComparisonPanel");
  });

  it("routes dashboard brand and workspace icons through the design-system asset bridge", () => {
    const app = readRepositoryFile("src/Meridian.Ui/dashboard/src/app.tsx");
    const bridge = readRepositoryFile("src/Meridian.Ui/dashboard/src/design-system/assets.ts");
    const nav = readRepositoryFile("src/Meridian.Ui/dashboard/src/components/meridian/workspace-nav.tsx");

    expect(bridge).toContain('export const DESIGN_SYSTEM_PACKAGE_ROOT = "../../../Meridian Design System"');
    expect(bridge).toContain('import meridianMarkLightUrl from "@/assets/brand/meridian-mark-light.svg"');
    expect(bridge).toContain('import meridianTilePngUrl from "@/assets/brand/meridian-tile-256.png"');
    expect(bridge).toContain('import strategyIconUrl from "@/assets/icons/strategy-builder.svg"');
    expect(bridge).toContain("satisfies Record<WorkspaceKey, string>");
    expect(app).toContain('import { meridianBrandAssets } from "@/design-system/assets"');
    expect(app).toContain('import { DesignSystemMasthead } from "@/design-system/primitives"');
    expect(app).toContain("<DesignSystemMasthead");
    expect(app).toContain("meridianBrandAssets.markLight");
    expect(nav).toContain('import { meridianWorkspaceIconAssets } from "@/design-system/assets"');
    expect(nav).toContain('import { DesignSystemNavRail, designSystemNavRailClasses } from "@/design-system/primitives"');
    expect(nav).toContain("meridianWorkspaceIconAssets[item.key]");

    expect(existsSync(resolveDashboardAsset("app.ico"))).toBe(true);
    expect(existsSync(resolveDashboardAsset("brand/README.md"))).toBe(true);
    expect(existsSync(resolveDashboardAsset("brand/meridian-tile-256.png"))).toBe(true);
    expect(existsSync(resolveDashboardAsset("icons/README.md"))).toBe(true);
  });

  it("keeps the workstation color tokens aligned with the Concrete light design-system source", () => {
    const styles = readDashboardStyles();
    const designSystemTheme = readDesignSystemPackageFile("tokens/theme.css");

    // Concrete canvas #DEE3EA → 215 22% 89%; steel accent #2F6F8F → 200 51% 37%.
    expect(designSystemTheme).toContain("--theme-bg-canvas: #DEE3EA");
    expect(designSystemTheme).toContain("--theme-accent: #2F6F8F");
    expect(designSystemTheme).toContain("--theme-border: #CBD3DC");
    expect(styles).toContain("--background: 215 22% 89%");
    expect(styles).toContain("--foreground: 215 15% 16%");
    expect(styles).toContain("--primary: 200 51% 37%");
    expect(styles).toContain("--ws-page-bg: #DEE3EA");
    expect(styles).toContain("--ws-surface: #ffffff");
    expect(styles).toContain("--ws-surface-subtle: #EBEFF4");
    expect(styles).toContain("--ws-surface-raised: #F3F6F9");
    expect(styles).toContain("--ws-masthead-bg: #171A1F");
    expect(styles).toContain("--ws-border: #CBD3DC");
    expect(styles).toContain("--ws-border-strong: #99A5B2");
    expect(styles).toContain("--ws-accent: #2F6F8F");
    expect(styles).toContain("--bg: var(--ws-page-bg)");
    expect(styles).toContain("--surface-topbar: var(--ws-masthead-bg)");
    expect(styles).toContain("--border-color: var(--ws-border)");
    expect(styles).toContain("--card-bg: var(--ws-surface)");
    expect(styles).toContain("--shadow-workstation: var(--ws-shadow)");
    expect(styles).toContain("--cyan-primary: var(--ws-accent)");
    // --ws-page-bg / --ws-masthead-bg now appear once in :root plus once each in
    // the dark-mode and forced-light scopes (auto-dark, manual dark, light opt-out).
    expect(styles.match(/--ws-page-bg:/g)).toHaveLength(4);
    expect(styles.match(/--ws-masthead-bg:/g)).toHaveLength(4);
  });

  it("keeps dark readiness and inactive navigation text above WCAG AA contrast", () => {
    const styles = readDashboardStyles();
    const readinessPanel = readRepositoryFile(
      "src/Meridian.Ui/dashboard/src/components/operations/readiness-panel.tsx"
    );

    expect(styles).toContain("--theme-primary-text: var(--ws-text)");
    expect(styles).toContain("--theme-secondary-text: var(--ws-text-secondary)");
    expect(styles).toContain("--theme-muted-text: var(--ws-text-muted)");
    expect(styles).toContain("--text-primary: var(--theme-primary-text)");
    expect(styles).toContain("--text-secondary: var(--theme-secondary-text)");
    expect(styles).toContain("--text-muted: var(--theme-muted-text)");
    expect(styles).toContain("--nav-item: var(--text-secondary)");
    expect(styles.match(/--ws-text-muted: #8F9AA7/g)).toHaveLength(2);
    expect(styles).not.toContain("--ws-text-muted: #8893A0");
    expect(readinessPanel).toContain(
      "var(--text-primary,var(--ws-text,#22272E))"
    );
    expect(readinessPanel).toContain(
      "var(--text-muted,var(--ws-text-muted,#59636F))"
    );

    const panelBackground = readManualDarkHexToken(styles, "--ws-surface");
    const railBackground = readManualDarkHexToken(styles, "--ws-rail-bg");
    const contrastPairs = [
      {
        label: "readiness title",
        foreground: readManualDarkHexToken(styles, "--ws-text"),
        background: panelBackground
      },
      {
        label: "readiness detail",
        foreground: readManualDarkHexToken(styles, "--ws-text-muted"),
        background: panelBackground
      },
      {
        label: "inactive navigation",
        foreground: readManualDarkHexToken(styles, "--ws-text-secondary"),
        background: railBackground
      }
    ];

    for (const { label, foreground, background } of contrastPairs) {
      expect(
        contrastRatio(foreground, background),
        `${label} (${foreground} on ${background})`
      ).toBeGreaterThanOrEqual(4.5);
    }
  });

  it("unifies workstation radii to Concrete 2px and keeps surfaces flat", () => {
    const styles = readDashboardStyles();

    // Concrete: one tight 2px corner across chips/controls/cards; the named
    // scale tops out at 6px (0.375rem) for large sheets only.
    expect(styles).toContain("--radius: 0.125rem");
    expect(styles).toContain("--radius-xl: 0.375rem");
    expect(styles).toContain("--radius-lg: 0.25rem");
    expect(styles).toContain("--radius-md: 0.1875rem");
    expect(styles).toContain("--radius-sm: 0.125rem");
    expect(styles).toContain("--radius-xs: 0.125rem");
    expect(styles).toContain("--radius-chip: 2px");
    expect(styles).toContain("--radius-button: 2px");
    expect(styles).toContain("--radius-card: 2px");
    expect(styles).toContain("--radius-checkbox: 2px");

    // Flat by mandate: the card/panel shadow tokens resolve to none; borders
    // carry elevation. The only shadow is the detached-overlay menu shadow.
    expect(styles).toContain("--ws-shadow: none");
    expect(styles).toContain("--shadow-workstation: var(--ws-shadow)");
    expect(styles).toContain("--shadow-panel: var(--ws-shadow)");
    expect(styles).toContain("--shadow-menu: 0 2px 6px rgba(0, 0, 0, 0.18)");
    expect(styles).toContain("--shadow-float: var(--shadow-menu)");
    expect(styles).not.toContain("3px 3px 0 rgba(0, 0, 0, 0.72)");
  });

  it("keeps positive state utilities wired to the success trust token", () => {
    const tailwindConfig = readTailwindConfig();

    expect(tailwindConfig).toContain("success: \"hsl(var(--success) / <alpha-value>)\"");
    expect(tailwindConfig).toContain("positive: \"hsl(var(--success) / <alpha-value>)\"");
  });

  it("uses the high-contrast focus token for translucent primary focus rings", () => {
    const styles = readDashboardStyles();

    expect(styles).toContain("--cyan-focus: var(--ws-accent)");
    expect(styles).toContain(".focus-visible\\:ring-primary\\/40:focus-visible");
    expect(styles).toContain("--tw-ring-color: var(--cyan-focus)");
  });

  it("uses the paper canvas background from the design-system documentation", () => {
    const styles = readDashboardStyles();

    expect(styles).toContain("background: var(--bg)");
    expect(styles).toContain("background-image: none");
    expect(styles).not.toContain("linear-gradient(rgba(49, 83, 109, 0.18) 1px, transparent 1px)");
    expect(styles).not.toContain("linear-gradient(90deg, rgba(49, 83, 109, 0.14) 1px, transparent 1px)");
    expect(styles).not.toContain("radial-gradient(ellipse at 6% 0%");
    expect(styles).not.toContain("rgba(214, 158, 56, 0.12)");
    expect(styles).not.toContain("rgba(52, 211, 153");
  });

  it("keeps final workspace surface rules outside the global token stylesheet", () => {
    const entry = readDashboardEntry();
    const styles = readDashboardStyles();
    const surface = readWorkspaceSurfaceStyles();

    expect(styles).not.toContain("Final cascade for the light-first Institutional Ops system");
    expect(surface).toContain("Final cascade for the light-first Institutional Ops system");
    expect(surface).toContain("--mds-workspace-bg: var(--theme-bg-canvas, var(--ws-page-bg))");
    expect(surface).toContain("background: var(--mds-workspace-bg, var(--ws-page-bg))");
    expect(surface).toContain("--mds-workspace-accent: var(--theme-accent, var(--ws-accent))");
    expect(surface).toContain(".workspace-table-inspector-layout");
    expect(entry.indexOf('import "@/styles/index.css";')).toBeLessThan(
      entry.indexOf('import "@/styles/workspace-surface.css";')
    );
    expect(entry.indexOf('import "@/styles/workspace-surface.css";')).toBeLessThan(
      entry.indexOf('import "@/styles/command-palette.css";')
    );
  });

  // ─── Light Institutional Ops alignment contracts ─────────────────────────

  it("exposes sidebar tokens aligned with the Concrete institutional rail", () => {
    const styles = readDashboardStyles();
    const navStyles = readWorkspaceNavStyles();
    const tailwindConfig = readTailwindConfig();

    // Concrete rail: band #EBEFF4 → 213 29% 94%; border #CBD3DC → 212 20% 83%.
    expect(styles).toContain("--sidebar: 213 29% 94%");
    expect(styles).toContain("--sidebar-foreground: 212 14% 35%");
    expect(styles).toContain("--sidebar-primary: 200 51% 37%");
    expect(styles).toContain("--sidebar-border: 212 20% 83%");
    expect(styles).toContain("--sidebar-ring: 200 51% 37%");
    expect(styles).toContain("[data-appearance=\"light\"]");
    expect(navStyles).toContain("--mds-nav-rail-bg: var(--theme-bg-hover, var(--ws-rail-bg))");
    expect(navStyles).toContain("--mds-nav-rail-accent: var(--theme-accent, var(--ws-accent))");
    expect(navStyles).toContain("background: var(--mds-nav-rail-active, var(--ws-rail-active, #E1EAF2))");

    // Tailwind color registrations present
    expect(tailwindConfig).toContain("sidebar: {");
    expect(tailwindConfig).toContain("\"hsl(var(--sidebar) / <alpha-value>)\"");
    expect(tailwindConfig).toContain("\"hsl(var(--sidebar-border) / <alpha-value>)\"");
  });

  it("exposes chart-1…5 tokens aligned with the Concrete semantic palette", () => {
    const styles = readDashboardStyles();
    const tailwindConfig = readTailwindConfig();

    // steel #2F6F8F · spruce #16885F · brick #BA3F55 · ochre #8A520E · slate #6E8597
    expect(styles).toContain("--chart-1: 200 51% 37%");
    expect(styles).toContain("--chart-2: 158 72% 31%");
    expect(styles).toContain("--chart-3: 349 49% 49%");
    expect(styles).toContain("--chart-4: 33 82% 30%");
    expect(styles).toContain("--chart-5: 206 16% 51%");

    // Tailwind color registrations present
    expect(tailwindConfig).toContain("chart: {");
    expect(tailwindConfig).toContain("\"hsl(var(--chart-1) / <alpha-value>)\"");
    expect(tailwindConfig).toContain("\"hsl(var(--chart-5) / <alpha-value>)\"");
  });

  it("exposes shadow channel tokens that back the Tailwind flat hairline", () => {
    const styles = readDashboardStyles();
    const tailwindConfig = readTailwindConfig();

    expect(styles).toContain("--shadow-offset-x: 0px");
    expect(styles).toContain("--shadow-offset-y: 1px");
    expect(styles).toContain("--shadow-blur: 1px");
    expect(styles).toContain("--shadow-spread: 0px");
    expect(tailwindConfig).toContain("flat:");
  });

  it("adds the Concrete semantic palette and environment-mode tokens", () => {
    const styles = readDashboardStyles();

    // Tier 2 semantic hues authored once in :root (light).
    expect(styles).toContain("--green: #16885F");
    expect(styles).toContain("--red: #BA3F55");
    expect(styles).toContain("--orange: #8A520E");
    expect(styles).toContain("--purple: #6F5BA7");
    expect(styles).toContain("--amber: var(--orange)");

    // Environment modes derive from the semantic palette (Live·Paper·Fixture).
    expect(styles).toContain("--mode-live: var(--red)");
    expect(styles).toContain("--mode-paper: var(--ws-accent)");
    expect(styles).toContain("--mode-fixture: var(--orange)");
  });

  it("derives the severity and state trios from the semantic palette via color-mix", () => {
    const styles = readDashboardStyles();

    // Severity chips (Ready·Review·Action·Blocked·Info) derive from Tier 2.
    expect(styles).toContain("--severity-ready-fg: var(--green)");
    expect(styles).toContain("--severity-review-fg: var(--ws-accent)");
    expect(styles).toContain("--severity-action-fg: var(--orange)");
    expect(styles).toContain("--severity-blocked-fg: var(--red)");
    expect(styles).toContain(
      "--severity-blocked-bg: color-mix(in srgb, var(--red) 10%, transparent)"
    );

    // State layer (healthy/warn/danger/paper/strategy/live/pending) derives too.
    expect(styles).toContain("--state-live-fg: var(--red)");
    expect(styles).toContain("--state-paper-fg: var(--ws-accent)");
    expect(styles).toContain("--state-strategy-fg: var(--purple)");
    expect(styles).toContain("--state-pending-fg: var(--purple)");
    expect(styles).toContain(
      "--state-strategy-bg: color-mix(in srgb, var(--purple) 10%, transparent)"
    );
  });

  it("preserves the dark-mode scope and the forced-light opt-out", () => {
    const styles = readDashboardStyles();

    // Both the auto (media) and manual (attribute) dark scopes exist.
    expect(styles).toContain("@media (prefers-color-scheme: dark)");
    expect(styles).toContain(":root[data-theme=\"dark\"]");
    expect(styles).toContain(":root[data-theme=\"light\"]");

    // Concrete graphite dark base: canvas #0E1113 · panel #1A2026 · steel #5790BE.
    expect(styles).toContain("--ws-page-bg: #0E1113");
    expect(styles).toContain("--ws-surface: #1A2026");
    expect(styles).toContain("--ws-accent: #5790BE");

    // The forced-light opt-out re-asserts the Concrete light canvas.
    expect(styles.match(/--ws-page-bg: #DEE3EA/g)).toHaveLength(2);
  });

  it("uses Segoe UI as the primary sans font and Cascadia Mono with JetBrains fallback for mono", () => {
    const tailwindConfig = readTailwindConfig();

    expect(tailwindConfig).toContain("\"Segoe UI Variable Text\"");
    expect(tailwindConfig).toContain("\"Segoe UI Variable Display\"");
    expect(tailwindConfig).toContain("\"Cascadia Mono\"");
    expect(tailwindConfig).toContain("\"JetBrains Mono\"");
  });

  it("declares local Segoe/Cascadia faces and fetches no external fonts", () => {
    const styles = readDashboardStyles();

    // Operator workstation must render offline / under strict CSP: no external
    // font fetch. Monospace resolves via the local Cascadia @font-face plus the
    // system ui-monospace stack (JetBrains Mono only if locally installed).
    expect(styles).not.toContain("googleapis.com");
    expect(styles).not.toMatch(/@import\s+url\(\s*["']?https?:/i);
    expect(styles).toContain("font-family: \"Segoe UI Variable Display\"");
    expect(styles).toContain("font-family: \"Segoe UI Variable Text\"");
    expect(styles).toContain("font-family: \"Cascadia Mono\"");
  });

  it("keeps the reference workbench preview aligned with the schedule evidence pattern", () => {
    const preview = readReferenceWorkbenchPreview();

    expect(preview).toContain("Cash-flow and factor schedule");
    expect(preview).toContain("Selected event detail");
    expect(preview).toContain("Expected");
    expect(preview).toContain("Actual");
    expect(preview).toContain("Variance");
    expect(preview).toContain("Controls and validation");
  });

  it("keeps Security Master schedules on shared workbench primitives with view-model copy", () => {
    const screen = readAccountingScreen();
    const securityMasterPanels = readAccountingSecurityMasterPanels();
    const viewModel = readAccountingViewModel();

    expect(screen).toContain('from "@/screens/accounting-screen.security-master-panels"');
    expect(securityMasterPanels).toContain("export function SecuritySchedulesPanel");
    expect(securityMasterPanels).toContain("<DenseDataTable");
    expect(securityMasterPanels).toContain("<ToolbarStrip");
    expect(securityMasterPanels).toContain("<EntitySummary");
    expect(viewModel).toContain("buildSecuritySchedulesViewState");
    expect(viewModel).toContain("SecuritySchedulesViewState");
    expect(viewModel).toContain("Cash-flow and factor schedules");
    expect(viewModel).toContain("statusAnnouncement");
    expect(viewModel).toContain("emptyText");
  });

  it("keeps evidence semantic states aligned across browser, WPF, docs, and screenshot gates", () => {
    // The badge variant vocabulary now lives in the design-system primitive adapter that
    // `@/components/ui/badge` re-exports, so assert the semantic tones at their source.
    const badge = readRepositoryFile("src/Meridian.Ui/dashboard/src/design-system/badge.tsx");
    const sharedToneMappings = readRepositoryFile("src/Meridian.Ui/dashboard/src/lib/shared-tone-mappings.ts");
    const evidenceScreen = readRepositoryFile("src/Meridian.Ui/dashboard/src/screens/evidence-workbench-screen.tsx");
    const evidenceAssuranceLists = readRepositoryFile("src/Meridian.Ui/dashboard/src/screens/evidence-workbench-assurance-lists.tsx");
    const evidenceViewModel = readRepositoryFile("src/Meridian.Ui/dashboard/src/screens/evidence-workbench-screen.view-model.ts");
    const wpfThemeTokens = readRepositoryFile("src/Meridian.Wpf/Styles/ThemeTokens.xaml");
    const wpfThemeSurfaces = readRepositoryFile("src/Meridian.Wpf/Styles/ThemeSurfaces.xaml");
    const designDocs = readRepositoryFile("archive/docs/plans/desktop-ui-workflow-acceptance-matrix.md");
    const screenshotDocs = readRepositoryFile("docs/screenshots/README.md");
    const screenshotValidator = readRepositoryFile("scripts/dev/validate-screenshot-captures.py");

    for (const variant of ["success", "warning", "danger", "outline"]) {
      expect(badge).toContain(`${variant}:`);
    }

    expect(evidenceViewModel).toContain('export type EvidenceStatusTone = "success" | "warning" | "danger" | "muted";');
    expect(sharedToneMappings).toContain("export function readinessToneToBadgeVariant");
    expect(sharedToneMappings).toContain("case \"success\":");
    expect(sharedToneMappings).toContain("return \"outline\"");
    expect(evidenceScreen).toContain('import { evidenceStatusToneToTextClass, readinessToneToBadgeVariant } from "@/lib/shared-tone-mappings"');
    expect(evidenceScreen).toContain("readinessToneToBadgeVariant(panel.statusTone)");
    expect(evidenceAssuranceLists).toContain('import { readinessToneToBadgeVariant } from "@/lib/shared-tone-mappings"');
    expect(evidenceAssuranceLists).toContain("readinessToneToBadgeVariant(row.tone)");
    expect(evidenceScreen).toContain("evidenceStatusToneToTextClass(tone)");
    expect(evidenceScreen).toContain("actionBadgeVariant[action.tone]");

    for (const state of ["Success", "Warning", "Danger"]) {
      expect(wpfThemeTokens).toContain(`${state}ColorBrush`);
      expect(wpfThemeSurfaces).toContain(`Binding="{Binding Tone}" Value="${state}"`);
      expect(wpfThemeSurfaces).toContain(`Badge${state}BackgroundBrush`);
      expect(wpfThemeSurfaces).toContain(`Badge${state}ForegroundBrush`);
    }

    expect(wpfThemeSurfaces).toContain('Binding="{Binding Tone}" Value="Info"');
    expect(designDocs).toContain("Ready/review/blocked/current/muted mapping");
    expect(designDocs).toContain("screenshot quality gates");
    expect(screenshotDocs).toContain("semantic-state evidence");
    expect(screenshotValidator).toContain("capture is likely blank");
    expect(screenshotValidator).toContain("capture is likely low-entropy");
    expect(screenshotValidator).toContain("Manifest is missing expected");
    expect(screenshotValidator).toContain("Unexpected screenshot files");
  });

  it("exposes the complete core UI primitive vocabulary from the design-system manifest", () => {
    const primitiveFiles = [
      "badge.tsx",
      "breadcrumb.tsx",
      "button.tsx",
      "checkbox.tsx",
      "context-menu.tsx",
      "eyebrow.tsx",
      "form.tsx",
      "input.tsx",
      "modal.tsx",
      "multi-select.tsx",
      "panel-surface.tsx",
      "select.tsx",
      "status-banner.tsx",
      "tabs.tsx",
      "toast.tsx",
      "tooltip.tsx"
    ];

    for (const primitiveFile of primitiveFiles) {
      expect(existsSync(resolveDashboardPrimitive(primitiveFile))).toBe(true);
    }

    expect(readDashboardPrimitive("checkbox.tsx")).toContain("export const Toggle");
    expect(readDashboardPrimitive("form.tsx")).toContain("export function FormRow");
    expect(readDashboardPrimitive("form.tsx")).toContain("export function FormGrid");
    expect(readDashboardPrimitive("form.tsx")).toContain("export function FormDivider");
    expect(readDashboardPrimitive("tabs.tsx")).toContain("export function TabPanel");
    expect(readDashboardPrimitive("toast.tsx")).toContain("export function ToastProvider");
  });

  it("keeps UI primitives on Concrete 2px radii and shallow light-system shadows", () => {
    const primitiveSource = readdirSync(resolve(process.cwd(), "src/components/ui"))
      .filter((entry) => entry.endsWith(".tsx") && !entry.endsWith(".test.tsx"))
      .map((entry) => readDashboardPrimitive(entry))
      .join("\n");

    // Concrete unifies primitives on a single tight 2px corner, and the only
    // elevation is the detached-overlay menu shadow — surfaces are otherwise flat.
    // (The --radius-*/--shadow-* tokens themselves are asserted separately above.)
    expect(primitiveSource).toContain("rounded-[2px]");
    expect(primitiveSource).toContain("shadow-[0_2px_6px_rgba(0,0,0,0.18)]");
    expect(primitiveSource).not.toContain("1px_1px_0");
    expect(primitiveSource).not.toContain("2px_2px_0");
    expect(primitiveSource).not.toContain("border-slate");
    expect(primitiveSource).not.toContain("bg-slate");
    expect(primitiveSource).not.toContain("text-slate");
  });

  it("exposes dashboard-native design-system primitive adapters for the shell and core controls", () => {
    expect(existsSync(resolve(process.cwd(), "src/design-system/primitives.tsx"))).toBe(true);

    const primitives = readRepositoryFile("src/Meridian.Ui/dashboard/src/design-system/primitives.tsx");
    const button = readRepositoryFile("src/Meridian.Ui/dashboard/src/design-system/button.tsx");
    const badge = readRepositoryFile("src/Meridian.Ui/dashboard/src/design-system/badge.tsx");
    const status = readRepositoryFile("src/Meridian.Ui/dashboard/src/design-system/status.tsx");
    const masthead = readRepositoryFile("src/Meridian.Ui/dashboard/src/design-system/masthead.tsx");
    const trustStrip = readRepositoryFile("src/Meridian.Ui/dashboard/src/design-system/trust-strip.tsx");
    const navRail = readRepositoryFile("src/Meridian.Ui/dashboard/src/design-system/nav-rail.tsx");
    const tokens = readRepositoryFile("src/Meridian.Ui/dashboard/src/design-system/tokens.ts");
    const bridge = readRepositoryFile("src/Meridian.Ui/dashboard/src/design-system/assets.ts");
    const implementation = [button, badge, status, masthead, trustStrip, navRail, tokens].join("\n");

    // `primitives.tsx` remains the compatibility barrel while the adapters live in
    // per-component modules.
    for (const reexport of [
      'export { DesignSystemButton, type DesignSystemButtonProps } from "@/design-system/button"',
      'export { DesignSystemBadge, type DesignSystemBadgeProps } from "@/design-system/badge"',
      'DesignSystemStatus',
      'DesignSystemMasthead',
      'export { DesignSystemTrustStrip } from "@/design-system/trust-strip"',
      'DesignSystemNavRail',
      'export { DESIGN_SYSTEM_SHELL_TOKEN_CONTRACT } from "@/design-system/tokens"'
    ]) {
      expect(primitives).toContain(reexport);
    }

    // The primitive adapters are all present and typed.
    for (const symbol of [
      "export const DesignSystemButton",
      "export interface DesignSystemButtonProps",
      "export function DesignSystemBadge",
      "export interface DesignSystemBadgeProps",
      "export function DesignSystemStatus",
      "export function DesignSystemMasthead",
      "export function DesignSystemTrustStrip",
      "export interface DesignSystemMastheadProps",
      "export function DesignSystemNavRail",
      "export const designSystemNavRailClasses"
    ]) {
      expect(implementation).toContain(symbol);
    }

    // The five canonical operator severities are encoded once, in the primitive layer.
    expect(status).toContain('export const DESIGN_SYSTEM_SEVERITIES = ["ready", "review", "action", "blocked", "info"]');
    expect(status).toContain("export function normalizeDesignSystemSeverity");

    // Shell primitives are wired against the workstation token contract from the bridge.
    expect(tokens).toContain('import { DESIGN_SYSTEM_WORKSTATION_TOKENS } from "@/design-system/assets"');
    expect(tokens).toContain("export const DESIGN_SYSTEM_SHELL_TOKEN_CONTRACT = DESIGN_SYSTEM_WORKSTATION_TOKENS");
    expect(bridge).toContain("export const DESIGN_SYSTEM_WORKSTATION_TOKENS");
    expect(bridge).toContain('"--ws-masthead-bg"');
    expect(bridge).toContain('"--ws-accent"');
  });

  it("delegates public shell components to the design-system primitive adapters", () => {
    const button = readRepositoryFile("src/Meridian.Ui/dashboard/src/components/ui/button.tsx");
    const badge = readRepositoryFile("src/Meridian.Ui/dashboard/src/components/ui/badge.tsx");
    const nav = readRepositoryFile("src/Meridian.Ui/dashboard/src/components/meridian/workspace-nav.tsx");
    const app = readRepositoryFile("src/Meridian.Ui/dashboard/src/app.tsx");
    const topbar = readRepositoryFile("src/Meridian.Ui/dashboard/src/components/meridian/workstation-topbar.tsx");

    // @/components/ui/button and @/components/ui/badge stay stable public imports that
    // re-export the primitive adapters rather than forking behavior.
    expect(button).toContain('import { DesignSystemButton, type DesignSystemButtonProps } from "@/design-system/primitives"');
    expect(button).toContain("export const Button = DesignSystemButton");
    expect(button).toContain("export type ButtonProps = DesignSystemButtonProps");
    expect(badge).toContain('import { DesignSystemBadge, type DesignSystemBadgeProps } from "@/design-system/primitives"');
    expect(badge).toContain("export const Badge = DesignSystemBadge");
    expect(badge).toContain("export type BadgeProps = DesignSystemBadgeProps");

    // The rail composes the design-system rail class contract; the shell masthead renders
    // the design-system masthead adapter directly, and the WorkstationTopbar live-adapter
    // name keeps resolving through a delegating wrapper.
    expect(nav).toContain('import { DesignSystemNavRail, designSystemNavRailClasses } from "@/design-system/primitives"');
    expect(nav).toContain("<DesignSystemNavRail");
    expect(nav).toContain("designSystemNavRailClasses.item");
    expect(app).toContain('import { DesignSystemMasthead } from "@/design-system/primitives"');
    expect(app).toContain("<DesignSystemMasthead");
    expect(topbar).toContain("export function WorkstationTopbar");
    expect(topbar).toContain("return <DesignSystemMasthead {...props} />");
  });
});
