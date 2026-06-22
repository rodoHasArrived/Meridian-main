import { existsSync, readdirSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

function readDashboardStyles() {
  return readFileSync(resolve(process.cwd(), "src/styles/index.css"), "utf8");
}

function readTailwindConfig() {
  return readFileSync(resolve(process.cwd(), "tailwind.config.ts"), "utf8");
}

function readAccountingScreen() {
  return readFileSync(resolve(process.cwd(), "src/screens/accounting-screen.tsx"), "utf8");
}

function readAccountingViewModel() {
  return readFileSync(resolve(process.cwd(), "src/screens/accounting-screen.view-model.ts"), "utf8");
}

function readReferenceWorkbenchPreview() {
  return readFileSync(resolve(process.cwd(), "../../../Meridian Design System/preview/reference-workbench.html"), "utf8");
}

function readRepositoryFile(path: string) {
  return readFileSync(resolve(process.cwd(), "../../..", path), "utf8");
}

function resolveDashboardPrimitive(path: string) {
  return resolve(process.cwd(), "src/components/ui", path);
}

function readDashboardPrimitive(path: string) {
  return readFileSync(resolveDashboardPrimitive(path), "utf8");
}

describe("dashboard design-system contract", () => {
  it("keeps the workstation color tokens aligned with the light Institutional Ops design-system source", () => {
    const styles = readDashboardStyles();

    expect(styles).toContain("--background: 214 23% 94%");
    expect(styles).toContain("--foreground: 215 15% 16%");
    expect(styles).toContain("--primary: 200 51% 37%");
    expect(styles).toContain("--ws-page-bg: #ECEFF3");
    expect(styles).toContain("--ws-surface: #ffffff");
    expect(styles).toContain("--ws-masthead-bg: #171A1F");
    expect(styles).toContain("--ws-border: #D7DCE2");
    expect(styles).toContain("--ws-accent: #2F6F8F");
    expect(styles).toContain("--bg: var(--ws-page-bg)");
    expect(styles).toContain("--surface-topbar: var(--ws-masthead-bg)");
    expect(styles).toContain("--border-color: var(--ws-border)");
    expect(styles).toContain("--card-bg: var(--ws-surface)");
    expect(styles).toContain("--shadow-workstation: var(--ws-shadow)");
    expect(styles).toContain("--cyan-primary: var(--ws-accent)");
    expect(styles.match(/--ws-page-bg:/g)).toHaveLength(1);
    expect(styles.match(/--ws-masthead-bg:/g)).toHaveLength(1);
  });

  it("keeps workstation radii tight and shadows shallow", () => {
    const styles = readDashboardStyles();

    expect(styles).toContain("--radius: 0.5rem");
    expect(styles).toContain("--radius-xl: 0.625rem");
    expect(styles).toContain("--radius-lg: 0.5rem");
    expect(styles).toContain("--radius-md: 0.375rem");
    expect(styles).toContain("--radius-chip: 4px");
    expect(styles).toContain("--radius-button: 6px");
    expect(styles).toContain("--radius-card: 8px");
    expect(styles).toContain("--shadow-workstation:");
    expect(styles).toContain("0 1px 1px rgba(0, 0, 0, 0.08)");
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

  // ─── Light Institutional Ops alignment contracts ─────────────────────────

  it("exposes sidebar tokens aligned with the light institutional rail", () => {
    const styles = readDashboardStyles();
    const tailwindConfig = readTailwindConfig();

    expect(styles).toContain("--sidebar: 210 22% 96%");
    expect(styles).toContain("--sidebar-foreground: 212 14% 35%");
    expect(styles).toContain("--sidebar-primary: 200 51% 37%");
    expect(styles).toContain("--sidebar-border: 213 16% 86%");
    expect(styles).toContain("--sidebar-ring: 200 51% 37%");
    expect(styles).toContain("[data-appearance=\"light\"]");

    // Tailwind color registrations present
    expect(tailwindConfig).toContain("sidebar: {");
    expect(tailwindConfig).toContain("\"hsl(var(--sidebar) / <alpha-value>)\"");
    expect(tailwindConfig).toContain("\"hsl(var(--sidebar-border) / <alpha-value>)\"");
  });

  it("exposes chart-1…5 tokens aligned with the Meridian semantic palette", () => {
    const styles = readDashboardStyles();
    const tailwindConfig = readTailwindConfig();

    expect(styles).toContain("--chart-1: 200 51% 37%");
    expect(styles).toContain("--chart-2: 159 72% 31%");
    expect(styles).toContain("--chart-3: 347 49% 49%");
    expect(styles).toContain("--chart-4: 35 71% 42%");
    expect(styles).toContain("--chart-5: 203 27% 59%");

    // Tailwind color registrations present
    expect(tailwindConfig).toContain("chart: {");
    expect(tailwindConfig).toContain("\"hsl(var(--chart-1) / <alpha-value>)\"");
    expect(tailwindConfig).toContain("\"hsl(var(--chart-5) / <alpha-value>)\"");
  });

  it("exposes shadow tokens aligned with the design-system hairline shadow system", () => {
    const styles = readDashboardStyles();
    const tailwindConfig = readTailwindConfig();

    expect(styles).toContain("--shadow-offset-x: 0px");
    expect(styles).toContain("--shadow-offset-y: 1px");
    expect(styles).toContain("--shadow-blur: 1px");
    expect(styles).toContain("--shadow-spread: 0px");
    expect(tailwindConfig).toContain("flat:");
  });

  it("uses Segoe UI as the primary sans font and Cascadia Mono with JetBrains fallback for mono", () => {
    const tailwindConfig = readTailwindConfig();

    expect(tailwindConfig).toContain("\"Segoe UI Variable Text\"");
    expect(tailwindConfig).toContain("\"Segoe UI Variable Display\"");
    expect(tailwindConfig).toContain("\"Cascadia Mono\"");
    expect(tailwindConfig).toContain("\"JetBrains Mono\"");
  });

  it("loads JetBrains Mono and declares local Segoe/Cascadia faces", () => {
    const styles = readDashboardStyles();

    expect(styles).toContain("family=JetBrains+Mono");
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
    const viewModel = readAccountingViewModel();

    expect(screen).toContain("function SecuritySchedulesPanel");
    expect(screen).toContain("<DenseDataTable");
    expect(screen).toContain("<ToolbarStrip");
    expect(screen).toContain("<EntitySummary");
    expect(viewModel).toContain("buildSecuritySchedulesViewState");
    expect(viewModel).toContain("SecuritySchedulesViewState");
    expect(viewModel).toContain("Cash-flow and factor schedules");
    expect(viewModel).toContain("statusAnnouncement");
    expect(viewModel).toContain("emptyText");
  });

  it("keeps evidence semantic states aligned across browser, WPF, docs, and screenshot gates", () => {
    const badge = readRepositoryFile("src/Meridian.Ui/dashboard/src/components/ui/badge.tsx");
    const evidenceScreen = readRepositoryFile("src/Meridian.Ui/dashboard/src/screens/evidence-workbench-screen.tsx");
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
    expect(evidenceScreen).toContain('Record<EvidenceStatusTone, "success" | "warning" | "danger" | "outline">');
    expect(evidenceScreen).toContain('muted: "outline"');
    expect(evidenceScreen).toContain("badgeVariant[panel.statusTone]");
    expect(evidenceScreen).toContain("badgeVariant[row.tone]");

    for (const state of ["Success", "Warning", "Danger"]) {
      expect(wpfThemeTokens).toContain(`${state}ColorBrush`);
      expect(wpfThemeSurfaces).toContain(`Binding=\"{Binding Tone}\" Value=\"${state}\"`);
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

  it("keeps UI primitives on tokenized radii and shallow light-system shadows", () => {
    const primitiveSource = readdirSync(resolve(process.cwd(), "src/components/ui"))
      .filter((entry) => entry.endsWith(".tsx") && !entry.endsWith(".test.tsx"))
      .map((entry) => readDashboardPrimitive(entry))
      .join("\n");

    expect(primitiveSource).toContain("--radius-button");
    expect(primitiveSource).toContain("--radius-card");
    expect(primitiveSource).toContain("--shadow-panel");
    expect(primitiveSource).not.toContain("1px_1px_0");
    expect(primitiveSource).not.toContain("2px_2px_0");
    expect(primitiveSource).not.toContain("border-slate");
    expect(primitiveSource).not.toContain("bg-slate");
    expect(primitiveSource).not.toContain("text-slate");
  });
});
