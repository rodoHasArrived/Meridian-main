import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { ReportingLineageSummary } from "@/components/meridian/reporting-lineage-summary";
import type {
  FinancialRecordExplorerGraphNodeDto,
  FinancialRecordExplorerRecordGraphDto
} from "@/types";

function row(nodeId: string, label: string): FinancialRecordExplorerGraphNodeDto {
  return { nodeId, label, nodeType: "trade", tone: "Info", href: `/accounting/${nodeId}` };
}

function relationship(recordId: string, relationshipId: string): FinancialRecordExplorerGraphNodeDto {
  return {
    nodeId: `rel:${recordId}:${relationshipId}`,
    label: `${relationshipId} detail`,
    nodeType: relationshipId,
    tone: "Info",
    href: `/accounting/${relationshipId}`
  };
}

function placeholder(recordId: string, relationshipId: string): FinancialRecordExplorerGraphNodeDto {
  return {
    nodeId: `rel:${recordId}:${relationshipId}`,
    label: relationshipId,
    nodeType: relationshipId,
    tone: "Warning",
    href: ""
  };
}

function graph(nodes: FinancialRecordExplorerGraphNodeDto[]): FinancialRecordExplorerRecordGraphDto {
  return { nodes, edges: [] };
}

describe("ReportingLineageSummary", () => {
  it("renders every governed stage, marking the ones with no retained step", () => {
    render(
      <ReportingLineageSummary
        graph={graph([
          row("rec-1", "Trade 4471"),
          relationship("rec-1", "report-line"),
          placeholder("rec-1", "reconciliation")
        ])}
        requiredStages={["Reconciliation"]}
      />
    );

    expect(screen.getByText("Trade 4471")).toBeInTheDocument();
    expect(screen.getByText("Reconciliation (missing)")).toBeInTheDocument();
    expect(screen.getByText("Incomplete")).toBeInTheDocument();
  });

  it("reports a complete lineage as complete", () => {
    render(
      <ReportingLineageSummary
        graph={graph([
          row("rec-1", "Trade 4471"),
          relationship("rec-1", "terms-obligations"),
          relationship("rec-1", "journal"),
          relationship("rec-1", "reconciliation"),
          relationship("rec-1", "report-line"),
          relationship("rec-1", "evidence")
        ])}
      />
    );

    expect(screen.getByText("Complete")).toBeInTheDocument();
    expect(screen.queryByText(/\(missing\)/)).not.toBeInTheDocument();
  });

  it("renders one entry per record, capped", () => {
    render(
      <ReportingLineageSummary
        graph={graph([row("rec-1", "Trade 1"), row("rec-2", "Trade 2"), row("rec-3", "Trade 3")])}
        maxRecords={2}
      />
    );

    expect(screen.getByText("Trade 1")).toBeInTheDocument();
    expect(screen.getByText("Trade 2")).toBeInTheDocument();
    expect(screen.queryByText("Trade 3")).not.toBeInTheDocument();
  });

  it("renders nothing when the graph has no records", () => {
    const { container } = render(<ReportingLineageSummary graph={graph([])} />);

    expect(container).toBeEmptyDOMElement();
  });

  it("renders nothing when there is no graph at all", () => {
    const { container } = render(<ReportingLineageSummary graph={null} />);

    expect(container).toBeEmptyDOMElement();
  });

  it("keeps the stages in source-to-publication order", () => {
    render(<ReportingLineageSummary graph={graph([row("rec-1", "Trade 4471")])} />);

    const list = screen.getByText("Trade 4471").closest("li");
    const stages = within(list as HTMLElement)
      .getAllByRole("listitem")
      .map((item) => item.textContent?.replace("→", "").replace(" (missing)", "").trim());

    expect(stages).toEqual([
      "Source",
      "Normalization",
      "Calculation",
      "Reconciliation",
      "Report block",
      "Publication"
    ]);
  });
});
