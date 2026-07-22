import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ValidationIssueList } from "./validation-issue-list";

const issues = [
  { code: "RECON-204", severity: "Critical", message: "Custody cash break exceeds tolerance.", gate: "Reconciliation" },
  { code: "MAP-118", severity: "Warning", message: "Provider field 'cusip' unmapped for 12 rows.", gate: "SecurityMaster" },
  { code: "EVID-007", severity: "Info", message: "Approval evidence pack not yet generated." },
];

describe("ValidationIssueList", () => {
  it("renders one row per issue with code, message, and gate", () => {
    const { container } = render(<ValidationIssueList issues={issues} />);
    expect(container.querySelectorAll(".mds-issue")).toHaveLength(3);
    expect(screen.getByText("RECON-204")).toBeInTheDocument();
    expect(screen.getByText("Custody cash break exceeds tolerance.")).toBeInTheDocument();
    expect(screen.getByText("Reconciliation")).toBeInTheDocument();
  });

  it("maps validation severities onto issue tone classes", () => {
    const { container } = render(<ValidationIssueList issues={issues} />);
    expect(container.querySelector(".mds-issue--blocked")).not.toBeNull();
    expect(container.querySelector(".mds-issue--action")).not.toBeNull();
    expect(container.querySelector(".mds-issue--info")).not.toBeNull();
  });

  it("renders the empty label when there are no issues", () => {
    render(<ValidationIssueList issues={[]} emptyLabel="All clear" />);
    expect(screen.getByText("All clear")).toBeInTheDocument();
  });
});
