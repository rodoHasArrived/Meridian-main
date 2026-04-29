import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

function readDashboardStyles() {
  return readFileSync(resolve(process.cwd(), "src/styles/index.css"), "utf8");
}

describe("dashboard design-system contract", () => {
  it("keeps the cockpit color tokens aligned with the design-system source", () => {
    const styles = readDashboardStyles();

    expect(styles).toContain("--background: 215 42% 6%");
    expect(styles).toContain("--foreground: 210 30% 92%");
    expect(styles).toContain("--primary: 198 72% 50%");
    expect(styles).toContain("--border-hi: #2a4566");
    expect(styles).toContain("--cyan-primary: #2ab2d4");
  });

  it("keeps workstation radii tight and shadows shallow", () => {
    const styles = readDashboardStyles();

    expect(styles).toContain("--radius-xl: 0.625rem");
    expect(styles).toContain("--radius-lg: 0.5rem");
    expect(styles).toContain("--radius-md: 0.375rem");
    expect(styles).toContain("--shadow-workstation:");
    expect(styles).toContain("0 1px 2px rgba(0, 0, 0, 0.30)");
  });

  it("uses the restrained ambient cockpit background from the design-system documentation", () => {
    const styles = readDashboardStyles();

    expect(styles).toContain("radial-gradient(ellipse at 6% 0%, rgba(42, 178, 212, 0.08) 0%, transparent 40%)");
    expect(styles).toContain("radial-gradient(ellipse at 96% 96%, rgba(96, 165, 250, 0.05) 0%, transparent 38%)");
    expect(styles).not.toContain("rgba(214, 158, 56, 0.12)");
    expect(styles).not.toContain("rgba(52, 211, 153");
  });
});
