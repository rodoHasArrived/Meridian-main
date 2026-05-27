import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

function readDashboardStyles() {
  return readFileSync(resolve(process.cwd(), "src/styles/index.css"), "utf8");
}

function readTailwindConfig() {
  return readFileSync(resolve(process.cwd(), "tailwind.config.ts"), "utf8");
}

function readGovernanceScreen() {
  return readFileSync(resolve(process.cwd(), "src/screens/governance-screen.tsx"), "utf8");
}

function readGovernanceViewModel() {
  return readFileSync(resolve(process.cwd(), "src/screens/governance-screen.view-model.ts"), "utf8");
}

function readReferenceWorkbenchPreview() {
  return readFileSync(resolve(process.cwd(), "../../../Meridian Design System/preview/reference-workbench.html"), "utf8");
}

describe("dashboard design-system contract", () => {
  it("keeps the cockpit color tokens aligned with the design-system source", () => {
    const styles = readDashboardStyles();

    expect(styles).toContain("--background: 220 10% 6%");
    expect(styles).toContain("--foreground: 210 18% 94%");
    expect(styles).toContain("--primary: 198 72% 50%");
    expect(styles).toContain("--border-hi: #477089");
    expect(styles).toContain("--cyan-primary: #2ab2d4");
  });

  it("keeps workstation radii tight and shadows hard-offset", () => {
    const styles = readDashboardStyles();

    expect(styles).toContain("--radius: 0.5rem");
    expect(styles).toContain("--radius-xl: 0.625rem");
    expect(styles).toContain("--radius-lg: 0.5rem");
    expect(styles).toContain("--radius-md: 0.375rem");
    expect(styles).toContain("--shadow-workstation:");
    expect(styles).toContain("3px 3px 0 rgba(0, 0, 0, 0.72)");
  });

  it("keeps positive state utilities wired to the success trust token", () => {
    const tailwindConfig = readTailwindConfig();

    expect(tailwindConfig).toContain("success: \"hsl(var(--success) / <alpha-value>)\"");
    expect(tailwindConfig).toContain("positive: \"hsl(var(--success) / <alpha-value>)\"");
  });

  it("uses the high-contrast focus token for translucent primary focus rings", () => {
    const styles = readDashboardStyles();

    expect(styles).toContain("--cyan-focus: #20d3f7");
    expect(styles).toContain(".focus-visible\\:ring-primary\\/40:focus-visible");
    expect(styles).toContain("--tw-ring-color: var(--cyan-focus)");
  });

  it("uses the ledger-grid cockpit background from the design-system documentation", () => {
    const styles = readDashboardStyles();

    expect(styles).toContain("linear-gradient(rgba(49, 83, 109, 0.18) 1px, transparent 1px)");
    expect(styles).toContain("linear-gradient(90deg, rgba(49, 83, 109, 0.14) 1px, transparent 1px)");
    expect(styles).toContain("background-size: 48px 48px, 48px 48px, auto");
    expect(styles).not.toContain("radial-gradient(ellipse at 6% 0%");
    expect(styles).not.toContain("rgba(214, 158, 56, 0.12)");
    expect(styles).not.toContain("rgba(52, 211, 153");
  });

  // ─── cash-flow-factor-sch alignment contracts ───────────────────────────

  it("exposes sidebar tokens aligned with cash-flow-factor-sch token contract", () => {
    const styles = readDashboardStyles();
    const tailwindConfig = readTailwindConfig();

    // CSS variables present in dark-mode :root
    expect(styles).toContain("--sidebar: 220 12% 5%");
    expect(styles).toContain("--sidebar-foreground: 210 18% 94%");
    expect(styles).toContain("--sidebar-primary: 198 72% 50%");
    expect(styles).toContain("--sidebar-border: 205 34% 29%");
    expect(styles).toContain("--sidebar-ring: 198 72% 50%");

    // Light-mode sidebar tokens exist
    expect(styles).toContain("[data-appearance=\"light\"]");
    expect(styles).toContain("--sidebar: 215 22% 94%");

    // Tailwind color registrations present
    expect(tailwindConfig).toContain("sidebar: {");
    expect(tailwindConfig).toContain("\"hsl(var(--sidebar) / <alpha-value>)\"");
    expect(tailwindConfig).toContain("\"hsl(var(--sidebar-border) / <alpha-value>)\"");
  });

  it("exposes chart-1…5 tokens aligned with cash-flow-factor-sch naming convention", () => {
    const styles = readDashboardStyles();
    const tailwindConfig = readTailwindConfig();

    // Semantic chart palette: cyan / green / red / amber / blue
    expect(styles).toContain("--chart-1: 198 72% 50%");
    expect(styles).toContain("--chart-2: 158 66% 44%");
    expect(styles).toContain("--chart-3: 354 68% 62%");
    expect(styles).toContain("--chart-4: 36 80% 58%");
    expect(styles).toContain("--chart-5: 214 82% 68%");

    // Tailwind color registrations present
    expect(tailwindConfig).toContain("chart: {");
    expect(tailwindConfig).toContain("\"hsl(var(--chart-1) / <alpha-value>)\"");
    expect(tailwindConfig).toContain("\"hsl(var(--chart-5) / <alpha-value>)\"");
  });

  it("exposes flat shadow tokens aligned with cash-flow-factor-sch shadow system", () => {
    const styles = readDashboardStyles();
    const tailwindConfig = readTailwindConfig();

    expect(styles).toContain("--shadow-offset-x: 3px");
    expect(styles).toContain("--shadow-offset-y: 3px");
    expect(styles).toContain("--shadow-blur: 0px");
    expect(styles).toContain("--shadow-spread: 0px");
    expect(tailwindConfig).toContain("flat:");
  });

  it("uses Inter as the primary sans font and JetBrains Mono as the primary mono font", () => {
    const tailwindConfig = readTailwindConfig();

    expect(tailwindConfig).toContain("\"Inter\"");
    expect(tailwindConfig).toContain("\"JetBrains Mono\"");
    // Legacy fonts retained as fallbacks
    expect(tailwindConfig).toContain("\"IBM Plex Sans\"");
    expect(tailwindConfig).toContain("\"IBM Plex Mono\"");
  });

  it("loads Inter and JetBrains Mono from the Google Fonts import", () => {
    const styles = readDashboardStyles();

    expect(styles).toContain("family=Inter");
    expect(styles).toContain("family=JetBrains+Mono");
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
    const screen = readGovernanceScreen();
    const viewModel = readGovernanceViewModel();

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
});
