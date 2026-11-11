import { screen } from "@testing-library/react";
import { axe } from "jest-axe";
import { renderWithRouter } from "@/test/render";
import { CloseChecklistHistoryView } from "./close-checklist-history-chart";
import type { CloseChecklistHistory } from "@/lib/api/close-checklist-history.api";

const history: CloseChecklistHistory = {
  workflowId: "workflow-1", asOf: "2026-11-11T12:00:00Z", status: "Ready", reason: null,
  coverageStart: "2026-11-10T08:00:00Z", coverageEnd: "2026-11-11T10:00:00Z", isPartial: true,
  missingAuditIds: ["legacy-2"], versionGaps: [], observations: [
    { auditId: "audit-1", recordedAtUtc: "2026-11-10T08:00:00Z", workflowVersion: 1, totalTasks: 5, remainingTasks: 5, blockedTasks: 0, segment: 0 },
    { auditId: "audit-2", recordedAtUtc: "2026-11-10T10:00:00Z", workflowVersion: 2, totalTasks: 5, remainingTasks: 4, blockedTasks: 1, segment: 0 },
    { auditId: "audit-4", recordedAtUtc: "2026-11-11T08:00:00Z", workflowVersion: 4, totalTasks: 5, remainingTasks: 2, blockedTasks: 0, segment: 1 },
    { auditId: "audit-5", recordedAtUtc: "2026-11-11T10:00:00Z", workflowVersion: 5, totalTasks: 5, remainingTasks: 0, blockedTasks: 0, segment: 1 }
  ]
};

it("keeps real zero remaining and breaks the line across missing historical states", async () => {
  const { container } = renderWithRouter(<CloseChecklistHistoryView history={history} />);
  expect(screen.getByText(/0 of 5 tasks remaining/)).toBeInTheDocument();
  expect(container.querySelectorAll("path[data-coverage-segment]")).toHaveLength(2);
  expect(screen.getByText(/Partial coverage: 1 event/)).toBeInTheDocument();
  expect(screen.getByRole("table", { name: "Retained close checklist observations", hidden: true })).toBeInTheDocument();
  expect((await axe(container)).violations).toEqual([]);
});

it("shows one recorded snapshot as evidence without inventing a trend", () => {
  renderWithRouter(<CloseChecklistHistoryView history={{ ...history, observations: history.observations.slice(0, 1) }} />);
  expect(screen.getByText(/One retained snapshot/)).toBeInTheDocument();
  expect(screen.queryByRole("img")).not.toBeInTheDocument();
});

it("names unavailable legacy coverage instead of showing an empty successful burndown", () => {
  renderWithRouter(<CloseChecklistHistoryView history={{ ...history, status: "Unavailable", observations: [], reason: "Legacy audit has no retained checklist snapshots." }} />);
  expect(screen.getByRole("status")).toHaveTextContent("Legacy audit has no retained checklist snapshots.");
  expect(screen.queryByRole("img")).not.toBeInTheDocument();
});
