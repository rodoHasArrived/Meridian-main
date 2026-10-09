import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { MemoryRouter, Router, useLocation, useNavigate } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { ReportingHub } from "@/components/meridian/reporting-hub";
import { buildReportingHubModel } from "@/lib/reporting-hub";
import type { ReportingHubDailyWorkInput, ReportingHubModel } from "@/lib/reporting-hub";

const dailyItems: ReportingHubDailyWorkInput[] = [
  {
    workItemId: "delivery-failure:board",
    kind: "delivery-failure",
    title: "Board portal package failed",
    statusLabel: "Blocked",
    detail: "Secure portal rejected the latest board package.",
    tone: "danger",
    owner: "fund-controller",
    dueAtUtc: "2026-06-30T15:00:00Z",
    primaryActionLabel: "Review delivery",
    primaryActionHref: "/reporting/report-packs?recipient=board",
    secondaryActionLabel: "Evidence",
    secondaryActionHref: "/reporting/evidence?subjectId=board",
    evidenceGaps: ["Board portal proof missing."],
    context: ["Board reporting committee"]
  },
  {
    workItemId: "evidence-gap:capital",
    kind: "evidence-gap",
    title: "Capital statement needs support",
    statusLabel: "Review",
    detail: "Capital statement is waiting for retained source records.",
    tone: "warning",
    owner: "capital-reviewer",
    dueAtUtc: null,
    primaryActionLabel: "Review capital support",
    primaryActionHref: "/reporting/evidence?subjectId=capital",
    secondaryActionLabel: "Open capital statement",
    secondaryActionHref: "/reporting/runs/capital",
    evidenceGaps: ["Capital source missing.", "Approval missing.", "Allocation missing.", "Fourth support gap."],
    context: ["Capital statement", "Fund A", "Book A", "Period A", "Fifth retained context"]
  }
];

function dailyModel(items: ReportingHubDailyWorkInput[] = dailyItems) {
  return buildReportingHubModel([], [], items);
}

function LocationProbe() {
  const location = useLocation();
  const navigate = useNavigate();
  return (
    <>
      <output aria-label="Current review URL">{location.pathname}{location.search}{location.hash}</output>
      <button onClick={() => navigate("/reporting?companyId=company-b&reportingWork=delivery-failure%3Aboard&filter=due#queue")}>Change company</button>
      <button onClick={() => navigate("/reporting?companyId=company-a&reportingWork=delivery-failure%3Aboard&filter=all#queue")}>Change filter</button>
      {["asOf", "date", "from", "to"].map((key) => (
        <button key={key} onClick={() => {
          const params = new URLSearchParams(location.search);
          params.set(key, "2026-07-31");
          navigate({ pathname: location.pathname, search: `?${params.toString()}`, hash: location.hash });
        }}>Change {key}</button>
      ))}
    </>
  );
}

function HubHarness({ model, initialUrl = "/reporting" }: { model: ReportingHubModel; initialUrl?: string }) {
  return <MemoryRouter initialEntries={[initialUrl]}><ReportingHub model={model} /><LocationProbe /></MemoryRouter>;
}

function selectedPanel() {
  return screen.getByRole("region", { name: "Selected reporting work detail" });
}

function rowFor(title: string) {
  return screen.getByRole("row", { name: `Select reporting work: ${title}` });
}

describe("ReportingHub", () => {
  it("renders daily reporting work as control-tower decision facts", () => {
    const model = buildReportingHubModel(
      [],
      [],
      [
        {
          workItemId: "delivery-failure:board",
          kind: "delivery-failure",
          title: "Board portal package failed",
          statusLabel: "Blocked",
          detail: "Secure portal rejected the latest board package.",
          tone: "danger",
          owner: "fund-controller",
          dueAtUtc: "2026-06-30T15:00:00Z",
          primaryActionLabel: "Review delivery",
          primaryActionHref: "/reporting/report-packs?recipient=board",
          secondaryActionLabel: "Evidence",
          secondaryActionHref: "/reporting/evidence?subjectKind=report-pack-delivery&subjectId=board",
          evidenceGaps: ["Delivery rejection lacks retained portal proof."],
          context: ["Board reporting committee", "SecurePortal"]
        }
      ]
    );

    render(<ReportingHub model={model} />);

    const cockpit = screen.getByRole("region", { name: "Daily reporting cockpit" });
    const facts = within(cockpit).getByLabelText("Board portal package failed decision facts");
    expect(within(facts).getAllByText("Blocked")).toHaveLength(2);
    expect(within(facts).getByText("fund-controller")).toBeInTheDocument();
    expect(within(facts).getByText("Board reporting committee")).toBeInTheDocument();
    expect(within(facts).getByText("Review delivery")).toBeInTheDocument();
    expect(within(facts).getByText("1 evidence gap")).toBeInTheDocument();
    expect(within(cockpit).getByLabelText("Selected reporting work detail")).toHaveTextContent("Secure portal rejected the latest board package.");
  });

  it("renders report families as a health table instead of launch cards", () => {
    const model = buildReportingHubModel(
      [
        {
          templateId: "monthly",
          family: "Investor statements",
          status: "Released",
          asOfDateLabel: "2026-06-30",
          runIdLabel: "run-1",
          drilldownLinks: [{ href: "/reporting/runs/run-1", label: "Run", isBrowserNavigable: true }]
        }
      ],
      [
        {
          templateName: "investor-monthly",
          name: "Investor Monthly Statement",
          family: "Investor statements"
        }
      ]
    );

    render(<ReportingHub model={model} />);

    const familyHealth = screen.getByRole("region", { name: "Report family organizer" });
    expect(within(familyHealth).getByRole("columnheader", { name: "Family" })).toBeInTheDocument();
    expect(within(familyHealth).getByText("Investor Statements")).toBeInTheDocument();
    expect(within(familyHealth).getByRole("link", { name: "Open latest output for Investor Statements" })).toHaveAttribute(
      "href",
      "/reporting/runs/run-1"
    );
  });

  it("does not present family setup work as a successful empty queue", () => {
    const model = buildReportingHubModel([], [{
      templateName: "draft-pack",
      name: "Draft Pack",
      family: "CustomReport",
      canRunOnDemand: false
    }]);

    render(<ReportingHub model={model} />);

    const summaryBadges = screen.getAllByText("No urgent work queued · 1 report family needs setup or review");
    expect(summaryBadges.some((badge) => badge.classList.contains("border-warning/35"))).toBe(true);
    expect(screen.getByText("No dedicated daily work items are loaded. 1 report family still needs review in the organizer below.")).toBeInTheDocument();
    expect(screen.queryByText(/No due packages, approvals/i)).not.toBeInTheDocument();
  });

  it("selects any work item and exposes all of its retained support and actions", async () => {
    const user = userEvent.setup();
    render(<ReportingHub model={dailyModel()} />);

    await user.click(rowFor("Capital statement needs support"));

    const panel = selectedPanel();
    expect(rowFor("Capital statement needs support")).toHaveAttribute("aria-selected", "true");
    expect(rowFor("Board portal package failed")).not.toHaveAttribute("aria-selected");
    expect(panel).toHaveTextContent("capital-reviewer");
    expect(panel).toHaveTextContent("Fourth support gap.");
    expect(panel).toHaveTextContent("Fifth retained context");
    expect(within(panel).getByRole("link", { name: "Review capital support: Capital statement needs support" })).toHaveAttribute("href", "/reporting/evidence?subjectId=capital");
    expect(within(panel).getByRole("link", { name: "Open capital statement: Capital statement needs support" })).toHaveAttribute("href", "/reporting/runs/capital");
    expect(within(panel).queryByRole("link", { name: /Review delivery/ })).not.toBeInTheDocument();
  });

  it("restores URL selection and preserves unrelated query parameters and hash", async () => {
    const user = userEvent.setup();
    render(<HubHarness model={dailyModel()} initialUrl="/reporting?companyId=company-a&filter=due&reportingWork=evidence-gap%3Acapital#queue" />);

    expect(rowFor("Capital statement needs support")).toHaveAttribute("aria-selected", "true");
    expect(selectedPanel()).toHaveTextContent("capital-reviewer");
    await user.click(rowFor("Board portal package failed"));

    const currentUrl = new URL(screen.getByLabelText("Current review URL").textContent!, "https://meridian.test");
    expect(currentUrl.searchParams.get("reportingWork")).toBe("delivery-failure:board");
    expect(currentUrl.searchParams.get("companyId")).toBe("company-a");
    expect(currentUrl.searchParams.get("filter")).toBe("due");
    expect(currentUrl.hash).toBe("#queue");
  });

  it("shares the row keyboard contract and returns focus to the chosen row", async () => {
    const user = userEvent.setup();
    render(<HubHarness model={dailyModel()} />);
    const first = rowFor("Board portal package failed");
    const second = rowFor("Capital statement needs support");
    const panel = selectedPanel();
    first.focus();

    await user.keyboard("{ArrowDown}");
    expect(second).toHaveFocus();
    expect(second).toHaveAttribute("aria-selected", "true");
    expect(second).toHaveAttribute("aria-controls", panel.id);
    expect(second).toHaveAttribute("aria-expanded", "true");
    expect(first).toHaveAttribute("aria-expanded", "false");
    expect(within(screen.getByRole("treegrid", { name: "Daily reporting work" }).parentElement!).getByRole("status")).toHaveTextContent("Capital statement needs support selected. Detail panel updated.");
    await user.keyboard("{Enter}");
    await waitFor(() => expect(panel).toHaveFocus());
    await user.keyboard("{Escape}");
    expect(second).toHaveFocus();
    await user.keyboard("{Home}");
    expect(first).toHaveFocus();
    expect(first).toHaveAttribute("aria-selected", "true");
    await user.keyboard("{End}");
    expect(second).toHaveFocus();
    await user.keyboard(" ");
    await waitFor(() => expect(panel).toHaveFocus());
    await user.keyboard("{Escape}");
    expect(second).toHaveFocus();
  });

  it("returns Escape to the newly activated row before deferred URL navigation commits", async () => {
    const user = userEvent.setup();
    const navigationDriver = { createHref: () => "/reporting", go: vi.fn(), push: vi.fn(), replace: vi.fn() };
    const initialUrl = "/reporting?companyId=company-a&reportingWork=delivery-failure%3Aboard";
    const controlledHub = (location: string) => (
      <Router location={location} navigator={navigationDriver}>
        <ReportingHub model={dailyModel()} />
        <LocationProbe />
      </Router>
    );
    const { rerender } = render(controlledHub(initialUrl));
    const second = rowFor("Capital statement needs support");
    second.focus();

    await user.keyboard("{Enter}");
    await waitFor(() => expect(selectedPanel()).toHaveFocus());
    expect(selectedPanel()).toHaveTextContent("capital-reviewer");
    expect(second).toHaveAttribute("aria-selected", "true");
    expect(screen.getByLabelText("Current review URL")).toHaveTextContent(initialUrl);
    expect(navigationDriver.replace).toHaveBeenCalled();
    expect(new URLSearchParams(navigationDriver.replace.mock.calls[0][0].search).get("reportingWork")).toBe("evidence-gap:capital");
    await user.keyboard("{Escape}");
    expect(second).toHaveFocus();

    rerender(controlledHub("/reporting?companyId=company-a&reportingWork=unknown"));
    expect(selectedPanel()).toHaveTextContent("This reporting work item is no longer in the current queue.");
    expect(within(selectedPanel()).queryByRole("link")).not.toBeInTheDocument();
    expect(screen.queryByRole("row", { selected: true })).not.toBeInTheDocument();

    rerender(controlledHub("/reporting?companyId=company-b&reportingWork=evidence-gap%3Acapital"));
    expect(selectedPanel()).toHaveTextContent("Reporting scope changed.");
    expect(within(selectedPanel()).queryByRole("link")).not.toBeInTheDocument();
    expect(screen.queryByRole("row", { selected: true })).not.toBeInTheDocument();
  });

  it("retains selection by identity through refresh and removes unavailable evidence and actions", () => {
    const { rerender } = render(<HubHarness model={dailyModel()} initialUrl="/reporting?reportingWork=evidence-gap%3Acapital" />);
    const refreshedCapital = { ...dailyItems[1], owner: "new-reviewer", evidenceGaps: ["Updated source gap."], primaryActionHref: "/reporting/evidence?subjectId=capital-v2" };
    rerender(<HubHarness model={dailyModel([refreshedCapital, dailyItems[0]])} />);
    expect(selectedPanel()).toHaveTextContent("new-reviewer");
    expect(selectedPanel()).toHaveTextContent("Updated source gap.");
    expect(selectedPanel()).not.toHaveTextContent("Fourth support gap.");
    expect(within(selectedPanel()).getByRole("link", { name: /Review capital support/ })).toHaveAttribute("href", "/reporting/evidence?subjectId=capital-v2");

    rerender(<HubHarness model={dailyModel([dailyItems[0]])} />);
    expect(selectedPanel()).toHaveTextContent("This reporting work item is no longer in the current queue.");
    expect(within(selectedPanel()).queryByRole("link")).not.toBeInTheDocument();
    expect(rowFor("Board portal package failed")).not.toHaveAttribute("aria-selected");

    rerender(<HubHarness model={dailyModel([])} />);
    expect(selectedPanel()).toHaveTextContent("This reporting work item is no longer in the current queue.");
    expect(selectedPanel()).not.toHaveTextContent("new-reviewer");
  });

  it("handles an unavailable deep-linked item without selecting an unrelated row", async () => {
    const user = userEvent.setup();
    render(<HubHarness model={dailyModel()} initialUrl="/reporting?reportingWork=unknown" />);
    expect(selectedPanel()).toHaveTextContent("This reporting work item is no longer in the current queue.");
    expect(screen.queryByRole("row", { selected: true })).not.toBeInTheDocument();
    await user.click(rowFor("Capital statement needs support"));
    expect(selectedPanel()).toHaveTextContent("capital-reviewer");
  });

  it("clears selected proof on a scope change even when the new scope reuses its work ID", async () => {
    const user = userEvent.setup();
    render(<HubHarness model={dailyModel()} initialUrl="/reporting?companyId=company-a&reportingWork=delivery-failure%3Aboard&filter=due#queue" />);
    expect(selectedPanel()).toHaveTextContent("fund-controller");

    await user.click(screen.getByRole("button", { name: "Change company" }));
    expect(selectedPanel()).toHaveTextContent("Reporting scope changed.");
    expect(within(selectedPanel()).queryByRole("link")).not.toBeInTheDocument();
    expect(screen.queryByRole("row", { selected: true })).not.toBeInTheDocument();
    const url = new URL(screen.getByLabelText("Current review URL").textContent!, "https://meridian.test");
    expect(url.searchParams.has("reportingWork")).toBe(false);
    expect(url.searchParams.get("companyId")).toBe("company-b");
    expect(url.searchParams.get("filter")).toBe("due");
    expect(url.hash).toBe("#queue");

    await user.click(rowFor("Board portal package failed"));
    expect(selectedPanel()).toHaveTextContent("fund-controller");
  });

  it("keeps selection when an unrelated query filter changes", async () => {
    const user = userEvent.setup();
    render(<HubHarness model={dailyModel()} initialUrl="/reporting?companyId=company-a&reportingWork=delivery-failure%3Aboard&filter=due#queue" />);
    await user.click(screen.getByRole("button", { name: "Change filter" }));
    expect(selectedPanel()).toHaveTextContent("fund-controller");
    expect(rowFor("Board portal package failed")).toHaveAttribute("aria-selected", "true");
  });

  it.each(["asOf", "date", "from", "to"])("clears selected proof when the canonical %s date scope changes", async (key) => {
    const user = userEvent.setup();
    render(<HubHarness model={dailyModel()} initialUrl={`/reporting?${key}=2026-06-30&reportingWork=delivery-failure%3Aboard&filter=due#queue`} />);
    expect(selectedPanel()).toHaveTextContent("fund-controller");

    await user.click(screen.getByRole("button", { name: `Change ${key}` }));
    expect(selectedPanel()).toHaveTextContent("Reporting scope changed.");
    expect(within(selectedPanel()).queryByRole("link")).not.toBeInTheDocument();
    expect(screen.queryByRole("row", { selected: true })).not.toBeInTheDocument();
    const url = new URL(screen.getByLabelText("Current review URL").textContent!, "https://meridian.test");
    expect(url.searchParams.get(key)).toBe("2026-07-31");
    expect(url.searchParams.has("reportingWork")).toBe(false);
    expect(url.searchParams.get("filter")).toBe("due");
    expect(url.hash).toBe("#queue");
  });

  it("has no basic accessibility violations after selecting another item", async () => {
    const user = userEvent.setup();
    const { container } = render(<HubHarness model={dailyModel()} />);
    await user.click(rowFor("Capital statement needs support"));
    expect((await axe(container)).violations).toEqual([]);
  });
});
