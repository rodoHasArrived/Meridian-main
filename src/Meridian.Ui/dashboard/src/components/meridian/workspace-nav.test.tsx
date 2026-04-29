import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { WorkspaceNav } from "@/components/meridian/workspace-nav";

describe("WorkspaceNav", () => {
  it("announces the current workspace route", () => {
    render(
      <MemoryRouter initialEntries={["/accounting/reconciliation"]}>
        <WorkspaceNav />
      </MemoryRouter>
    );

    expect(screen.getByRole("navigation", { name: "Workspaces" })).toBeInTheDocument();
    expect(screen.getByLabelText("Accounting workspace, current route, Review")).toHaveAttribute("aria-current", "page");
    expect(screen.getByText("Review · Current")).toBeInTheDocument();
  });
});
