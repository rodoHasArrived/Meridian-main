import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { WorkspaceNav } from "@/components/meridian/workspace-nav";
import { renderWithRouter } from "@/test/render";

describe("WorkspaceNav", () => {
  it("announces the current workspace route", () => {
    renderWithRouter(<WorkspaceNav />, { initialEntries: ["/accounting/reconciliation"] });

    expect(screen.getByRole("navigation", { name: "Workspaces" })).toBeInTheDocument();
    expect(screen.queryByLabelText("Current workspace: Accounting, Review posture")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Accounting workspace, current route, Review")).toHaveAttribute("aria-current", "page");
    expect(screen.getByText("Review · Current")).toBeInTheDocument();
    expect(screen.queryByText("Review posture")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Canonical route /accounting")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Open command palette with Control K")).not.toBeInTheDocument();
  });
});
