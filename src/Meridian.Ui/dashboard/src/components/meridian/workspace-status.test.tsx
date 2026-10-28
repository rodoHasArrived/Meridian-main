import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { buildShellFailureItems, buildShellStatusPanel } from "@/app-shell.status-panel";
import { WorkspaceStatus } from "@/components/meridian/workspace-status";

const panel = buildShellStatusPanel({
  loading: false,
  error: null,
  bootstrapFailed: false,
  failedItems: buildShellFailureItems({ accounting: "Ledger source timed out." }, null)
})!;

describe("WorkspaceStatus", () => {
  it("lets operators inspect affected areas and recover without a second warning banner", async () => {
    const user = userEvent.setup();
    const retry = vi.fn();
    render(<MemoryRouter><WorkspaceStatus panel={panel} onRetry={retry} /></MemoryRouter>);
    expect(screen.getByRole("status", { name: "Some workspace data is unavailable" })).toBeInTheDocument();
    const disclosure = screen.getByText("Workspace data issues (1)");
    expect(disclosure.closest("details")).not.toHaveAttribute("open");
    await user.click(disclosure);
    expect(screen.getByRole("list", { name: "Workspace data issues" })).toBeVisible();
    expect(screen.getByText("Ledger source timed out.")).toBeVisible();
    await user.keyboard("{Escape}");
    expect(disclosure.closest("details")).not.toHaveAttribute("open");
    expect(disclosure).toHaveFocus();
    await user.click(screen.getByRole("button", { name: "Retry failed workspace areas" }));
    expect(retry).toHaveBeenCalledOnce();
    expect(screen.getByRole("link", { name: "Review Settings diagnostics for failed workspace areas" })).toHaveAttribute("href", "/settings#backend-capability-coverage");
  });

  it("prevents repeated recovery requests and keeps issue details available while retrying", async () => {
    const user = userEvent.setup();
    const retry = vi.fn();
    render(<MemoryRouter><WorkspaceStatus panel={panel} onRetry={retry} refreshing /></MemoryRouter>);
    expect(screen.getByRole("status")).toHaveAttribute("aria-busy", "true");
    const retryButton = screen.getByRole("button", { name: "Retrying workspace data" });
    expect(retryButton).toBeDisabled();
    expect(retryButton).toHaveAttribute("title", "Workspace data recovery is in progress.");
    await user.click(retryButton);
    expect(retry).not.toHaveBeenCalled();
    await user.click(screen.getByText("Workspace data issues (1)"));
    expect(screen.getByText("Ledger source timed out.")).toBeVisible();
  });
});
