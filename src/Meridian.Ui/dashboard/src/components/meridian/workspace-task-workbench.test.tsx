import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, useLocation, useNavigate } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { WorkspaceTaskWorkbench, type WorkspaceTaskItem } from "./workspace-task-workbench";

const items: WorkspaceTaskItem[] = [
  { id: "one", title: "First run", description: "Paper evidence", status: "Needs review", href: "/strategy/promotions?runId=one", actionLabel: "Review run", facts: [{ label: "Dataset", value: "FX" }] },
  { id: "two", title: "Second run", description: "Retained backtest", status: "Completed", href: "/strategy/promotions?runId=two", actionLabel: "Review run" }
];
function LocationProbe() {
  const location = useLocation();
  const navigate = useNavigate();
  return <><output aria-label="Location">{location.pathname}{location.search}{location.hash}</output><button onClick={() => navigate(-1)}>Go back</button><button onClick={() => navigate(1)}>Go forward</button></>;
}
function setup(url = "/strategy?companyId=a&unrelated=keep#evidence", rows = items) {
  return render(<MemoryRouter initialEntries={[url]}><WorkspaceTaskWorkbench title="Retained runs" description="Review retained evidence" items={rows} stateKey="run" /><LocationProbe /></MemoryRouter>);
}
afterEach(() => vi.unstubAllGlobals());

describe("WorkspaceTaskWorkbench", () => {
  it("restores explicit selection and preserves scope, hash and destination parameters", () => {
    setup("/strategy?companyId=a&run=two&unrelated=keep#evidence");
    const panel = screen.getByRole("region", { name: "Second run detail" });
    const link = within(panel).getByRole("link", { name: "Review run: Second run" });
    const url = new URL(link.getAttribute("href")!, "https://local.test");
    expect(url.pathname).toBe("/strategy/promotions");
    expect(url.searchParams.get("companyId")).toBe("a");
    expect(url.searchParams.get("unrelated")).toBe("keep");
    expect(url.searchParams.get("runId")).toBe("two");
    expect(url.hash).toBe("#evidence");
  });
  it("keeps an excluded selection inspectable and distinguishes no matches from no data", async () => {
    const user = userEvent.setup();
    setup();
    await user.type(screen.getByRole("searchbox"), "absent");
    expect(screen.getByText("No work items match this search.")).toBeInTheDocument();
    const panel = screen.getByRole("region", { name: "First run detail" });
    expect(within(panel).getByText("Selected item is outside the current results.")).toBeInTheDocument();
    await user.click(within(panel).getByRole("button", { name: "Clear search" }));
    expect(screen.getByRole("row", { name: "First run" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByLabelText("Location")).toHaveTextContent("companyId=a&unrelated=keep#evidence");
  });
  it("retains an unknown explicit selection instead of selecting a different record", () => {
    setup("/strategy?run=removed");
    expect(screen.getByRole("region", { name: "Selection unavailable" })).toHaveTextContent("no longer in the loaded results");
    expect(screen.getByRole("row", { name: "First run" })).not.toHaveAttribute("aria-selected", "true");
  });
  it("restores discrete selections with browser history", async () => {
    const user = userEvent.setup();
    setup();
    await user.click(screen.getByRole("row", { name: "Second run" }));
    expect(screen.getByRole("region", { name: "Second run detail" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Go back" }));
    expect(screen.getByRole("region", { name: "First run detail" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Go forward" }));
    expect(screen.getByRole("region", { name: "Second run detail" })).toBeInTheDocument();
  });
  it("switches a constrained workbench on Enter and restores queue focus on Escape", async () => {
    vi.stubGlobal("matchMedia", vi.fn(() => ({ matches: true })));
    const user = userEvent.setup();
    setup();
    const row = screen.getByRole("row", { name: "Second run" });
    row.focus();
    await user.keyboard("{Enter}");
    const detail = screen.getByRole("region", { name: "Second run detail" });
    await waitFor(() => expect(detail).toHaveFocus());
    expect(screen.getByText("Back to work list").closest("button")).toHaveAttribute("aria-expanded", "true");
    await user.keyboard("{Escape}");
    await waitFor(() => expect(row).toHaveFocus());
    expect(screen.getByText("Inspect selection").closest("button")).toHaveAttribute("aria-expanded", "false");
  });
  it("renders an empty queue and has no basic accessibility violations", async () => {
    const { container } = setup("/strategy", []);
    expect(screen.getAllByText("No work items are available.").length).toBeGreaterThan(0);
    expect((await axe(container)).violations).toEqual([]);
  });
});
