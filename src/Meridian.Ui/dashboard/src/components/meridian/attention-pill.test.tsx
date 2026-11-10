import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { axe } from "jest-axe";
import type { AppShellAttentionState } from "@/app-shell.view-model";
import { AttentionPill } from "@/components/meridian/attention-pill";

function attention(overrides: Partial<AppShellAttentionState> = {}): AppShellAttentionState {
  return {
    tone: "ready",
    clear: true,
    statusLabel: "All clear",
    title: "No open issues",
    moreCount: 0,
    counts: { blocked: 0, review: 0, unconfirmed: 0 },
    href: "/",
    ariaLabel: "Workstation attention: All clear. No open issues. Open Today.",
    signals: [],
    issues: [],
    sourceSignals: [],
    ...overrides
  };
}

const renderPill = (state: AppShellAttentionState) =>
  render(<MemoryRouter><AttentionPill attention={state} /></MemoryRouter>);

describe("AttentionPill", () => {
  it("names the worst issue, counts the rest, and opens Today", async () => {
    const { container } = renderPill(attention({
      tone: "blocked",
      clear: false,
      statusLabel: "Blocked",
      title: "Cash variance needs review",
      moreCount: 2,
      ariaLabel: "Workstation attention: Blocked. Cash variance needs review. 2 more issues. Open Today."
    }));

    const pill = screen.getByRole("link", {
      name: "Workstation attention: Blocked. Cash variance needs review. 2 more issues. Open Today."
    });
    expect(pill).toHaveAttribute("href", "/");
    expect(pill).toHaveClass("masthead-status-pill-blocked");
    expect(pill).toHaveTextContent("Blocked");
    expect(pill).toHaveTextContent("Cash variance needs review");
    expect(pill).toHaveTextContent("+2");
    expect((await axe(container)).violations).toEqual([]);
  });

  it("reads all clear, with no issue title, when nothing is open", () => {
    renderPill(attention());

    const pill = screen.getByRole("link", { name: "Workstation attention: All clear. No open issues. Open Today." });
    expect(pill).toHaveClass("masthead-status-pill-ready");
    expect(pill).toHaveTextContent(/^All clear$/);
    expect(pill).toHaveAttribute("href", "/");
  });
});
