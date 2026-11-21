import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { DesignSystemBadge } from "@/design-system/badge";
import { DesignSystemStatus, StatusBadge, toneToSeverity, usesTrackedCaps } from "@/design-system/status";
import { SeverityBadge } from "@/components/operations/severity-badge";

describe("StatusBadge", () => {
  it("is the one status component: SeverityBadge and DesignSystemStatus are aliases of it", () => {
    expect(SeverityBadge).toBe(StatusBadge);
    expect(DesignSystemStatus).toBe(StatusBadge);
  });

  it("renders the five tones, escalating an unrecognized server state to action", () => {
    const { container } = render(
      <>
        <StatusBadge status="Ready" />
        <StatusBadge status="ReviewRequired" />
        <StatusBadge status="Stale" />
        <StatusBadge status="Blocked" />
        <StatusBadge status="Unknown" />
        <StatusBadge status="unmapped-server-criticality" label="Unmapped" />
      </>
    );
    expect([...container.querySelectorAll("[data-severity]")].map((node) => node.getAttribute("data-severity")))
      .toEqual(["ready", "review", "action", "blocked", "info", "action"]);
  });

  it("keeps tracked caps for one- or two-word labels and uses normal case for counts and sentences", () => {
    render(
      <>
        <StatusBadge status="review" label="Needs review" />
        <StatusBadge status="action" label="0 of 2 families current · 2 need attention" />
      </>
    );
    expect(screen.getByText("Needs review")).not.toHaveClass("mds-sev--sentence");
    expect(screen.getByText("0 of 2 families current · 2 need attention")).toHaveClass("mds-sev--sentence");
  });

  it("accepts the label as children, as DesignSystemStatus callers pass it", () => {
    render(<StatusBadge status="blocked">Halted</StatusBadge>);
    expect(screen.getByText("Halted")).toHaveAttribute("data-severity", "blocked");
  });
});

describe("toneToSeverity", () => {
  it.each([
    ["success", "ready"],
    ["primary", "review"],
    ["warning", "action"],
    ["danger", "blocked"],
    ["muted", "info"],
    [undefined, "info"],
    ["something-new", "action"]
  ] as const)("maps %s to %s", (tone, severity) => {
    expect(toneToSeverity(tone)).toBe(severity);
  });
});

describe("DesignSystemBadge label case", () => {
  it("counts words through numbers and elements", () => {
    expect(usesTrackedCaps("Ready")).toBe(true);
    expect(usesTrackedCaps(<>{3} breaks</>)).toBe(true);
    expect(usesTrackedCaps(<>{3} breaks need review</>)).toBe(false);
  });

  it("drops tracked mono caps for labels over two words", () => {
    render(
      <>
        <DesignSystemBadge variant="success">Live</DesignSystemBadge>
        <DesignSystemBadge variant="warning">2 of 4 providers degraded</DesignSystemBadge>
      </>
    );
    expect(screen.getByText("Live")).toHaveClass("uppercase", "font-mono");
    const sentence = screen.getByText("2 of 4 providers degraded");
    expect(sentence).toHaveClass("normal-case");
    expect(sentence).not.toHaveClass("uppercase");
  });
});
