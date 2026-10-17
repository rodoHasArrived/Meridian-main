import { useState } from "react";
import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as api from "@/lib/reporting-income-comparison-api";
import { ReportingIncomeComparisonPanel } from "@/screens/reporting-income-comparison";
import { renderWithRouter } from "@/test/render";
import { buildIncomeComparison, buildIncomeContributionSupport, currentIncomeRun, incomeComparisonCandidates, publishedIncomeRun, restatedIncomeRun } from "@/test/reporting-income-comparison-fixtures";
import type { IncomeComparison, IncomeComparisonRun } from "@/types/reporting-income-comparison";

vi.mock("@/lib/reporting-income-comparison-api", () => ({
  createIncomeComparison: vi.fn(),
  getIncomeComparison: vi.fn(),
  getIncomeComparisonCandidates: vi.fn(),
  getIncomeContributionSupport: vi.fn()
}));

const retained = buildIncomeComparison();
const selectionQuery = `incomeBaseline=${publishedIncomeRun.runId}&incomeCurrent=${currentIncomeRun.runId}&incomeGrid=investment-income&incomeMetric=Income`;
const savedQuery = `${selectionQuery}&incomeComparison=${retained.comparisonId}`;

function Harness() {
  const [hostRun, setHostRun] = useState(currentIncomeRun.runId);
  const location = useLocation();
  const navigate = useNavigate();
  const [, setQuery] = useSearchParams();
  return <>
    <ReportingIncomeComparisonPanel currentRunId={hostRun} />
    <output data-testid="route-context">{location.search}</output>
    <button onClick={() => setHostRun("income-another-scope")}>Change host run</button>
    <button onClick={() => navigate(-1)}>Back in comparison history</button>
    <button onClick={() => setQuery((query) => { const next = new URLSearchParams(query); next.set("incomeComparison", "comparison-2"); return next; })}>Open another saved comparison</button>
  </>;
}

function renderComparison(query = "") {
  return renderWithRouter(<Harness />, { initialEntries: [`/reporting/runs/detail?runId=${currentIncomeRun.runId}&tab=retained&${query}`] });
}

function queryContext() {
  return new URLSearchParams(screen.getByTestId("route-context").textContent ?? "");
}

async function selectMeasure() {
  await waitFor(() => expect(screen.getByRole("combobox", { name: "Baseline run" })).toBeEnabled());
  fireEvent.change(screen.getByRole("combobox", { name: "Report grid" }), { target: { value: "investment-income" } });
  fireEvent.change(screen.getByRole("combobox", { name: "Income measure" }), { target: { value: "Income" } });
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((accept) => { resolve = accept; });
  return { promise, resolve };
}

describe("ReportingIncomeComparisonPanel", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(api.getIncomeComparisonCandidates).mockResolvedValue(incomeComparisonCandidates);
    vi.mocked(api.createIncomeComparison).mockResolvedValue(retained);
    vi.mocked(api.getIncomeComparison).mockResolvedValue(retained);
    vi.mocked(api.getIncomeContributionSupport).mockResolvedValue(buildIncomeContributionSupport());
  });

  it("requires an explicit baseline and distinguishes originally published from restated runs", async () => {
    renderComparison();
    await selectMeasure();

    const baseline = screen.getByRole("combobox", { name: "Baseline run" });
    expect(baseline).toHaveValue("");
    expect(within(baseline).getByRole("option", { name: /Originally published · revision 1/ })).toHaveValue(publishedIncomeRun.runId);
    expect(within(baseline).getByRole("option", { name: /Restated · revision 2/ })).toHaveValue(restatedIncomeRun.runId);
    expect(screen.getByRole("button", { name: "Compare and retain explanation" })).toBeDisabled();
    expect(api.createIncomeComparison).not.toHaveBeenCalled();

    fireEvent.change(baseline, { target: { value: restatedIncomeRun.runId } });
    expect(screen.getByRole("button", { name: "Compare and retain explanation" })).toBeEnabled();
    expect(queryContext().get("incomeBaseline")).toBe(restatedIncomeRun.runId);
  });

  it("preserves rapid baseline and grid selections made before the router commits a render", async () => {
    renderComparison();
    const baseline = screen.getByRole("combobox", { name: "Baseline run" });
    const grid = screen.getByRole("combobox", { name: "Report grid" });
    await waitFor(() => expect(baseline).toBeEnabled());

    act(() => {
      fireEvent.change(baseline, { target: { value: publishedIncomeRun.runId } });
      fireEvent.change(grid, { target: { value: "investment-income" } });
    });
    fireEvent.change(screen.getByRole("combobox", { name: "Income measure" }), { target: { value: "Income" } });

    expect(baseline).toHaveValue(publishedIncomeRun.runId);
    expect(queryContext().get("incomeBaseline")).toBe(publishedIncomeRun.runId);
    expect(queryContext().get("incomeGrid")).toBe("investment-income");
    expect(queryContext().get("incomeMetric")).toBe("Income");
    expect(queryContext().get("runId")).toBe(currentIncomeRun.runId);
    expect(queryContext().get("tab")).toBe("retained");
    const compare = screen.getByRole("button", { name: "Compare and retain explanation" });
    expect(compare).toBeEnabled();
    fireEvent.click(compare);
    await waitFor(() => expect(api.createIncomeComparison).toHaveBeenCalledWith(
      { baselineRunId: publishedIncomeRun.runId, currentRunId: currentIncomeRun.runId, gridId: "investment-income", metricColumn: "Income" },
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    ));
  });

  it("adopts browser history before applying further rapid selection changes", async () => {
    renderComparison();
    const baseline = screen.getByRole("combobox", { name: "Baseline run" });
    const grid = screen.getByRole("combobox", { name: "Report grid" });
    await waitFor(() => expect(baseline).toBeEnabled());
    act(() => {
      fireEvent.change(baseline, { target: { value: publishedIncomeRun.runId } });
      fireEvent.change(grid, { target: { value: "investment-income" } });
    });
    fireEvent.click(screen.getByRole("button", { name: "Back in comparison history" }));
    expect(baseline).toHaveValue(publishedIncomeRun.runId);
    expect(grid).toHaveValue("");

    act(() => {
      fireEvent.change(baseline, { target: { value: restatedIncomeRun.runId } });
      fireEvent.change(grid, { target: { value: "investment-income" } });
    });
    fireEvent.change(screen.getByRole("combobox", { name: "Income measure" }), { target: { value: "Income" } });
    expect(queryContext().get("incomeBaseline")).toBe(restatedIncomeRun.runId);
    expect(queryContext().get("incomeGrid")).toBe("investment-income");
    expect(queryContext().get("incomeMetric")).toBe("Income");
    expect(queryContext().get("runId")).toBe(currentIncomeRun.runId);
    expect(screen.getByRole("button", { name: "Compare and retain explanation" })).toBeEnabled();
  });

  it("adopts a history entry after rapid selections return to the same query", async () => {
    renderComparison(`incomeBaseline=${publishedIncomeRun.runId}`);
    const baseline = screen.getByRole("combobox", { name: "Baseline run" });
    await waitFor(() => expect(baseline).toBeEnabled());
    act(() => {
      fireEvent.change(baseline, { target: { value: restatedIncomeRun.runId } });
      fireEvent.change(baseline, { target: { value: publishedIncomeRun.runId } });
    });
    fireEvent.click(screen.getByRole("button", { name: "Back in comparison history" }));
    expect(baseline).toHaveValue(restatedIncomeRun.runId);
    fireEvent.change(screen.getByRole("combobox", { name: "Report grid" }), { target: { value: "investment-income" } });

    expect(baseline).toHaveValue(restatedIncomeRun.runId);
    expect(queryContext().get("incomeBaseline")).toBe(restatedIncomeRun.runId);
    expect(queryContext().get("incomeGrid")).toBe("investment-income");
  });

  it("does not let a pending retention replace browser history for the same selected pair", async () => {
    const stale = deferred<IncomeComparison>();
    renderComparison(selectionQuery);
    const compare = screen.getByRole("button", { name: "Compare and retain explanation" });
    await waitFor(() => expect(compare).toBeEnabled());
    fireEvent.click(compare);
    await screen.findByText("Movement reconciled to retained evidence");
    expect(queryContext().get("incomeComparison")).toBe(retained.comparisonId);

    vi.mocked(api.createIncomeComparison).mockReturnValue(stale.promise);
    fireEvent.click(screen.getByRole("button", { name: "Compare and retain explanation" }));
    const signal = vi.mocked(api.createIncomeComparison).mock.calls[1][1]?.signal;
    fireEvent.click(screen.getByRole("button", { name: "Back in comparison history" }));
    expect(queryContext().get("incomeComparison")).toBeNull();
    expect(signal?.aborted).toBe(true);

    await act(async () => stale.resolve(retained));
    expect(queryContext().get("incomeComparison")).toBeNull();
    expect(queryContext().get("incomeBaseline")).toBe(publishedIncomeRun.runId);
    expect(screen.getByRole("button", { name: "Compare and retain explanation" })).toBeEnabled();
  });

  it("retains the selected pair then reads the saved identity before displaying its explanation", async () => {
    const savedRead = deferred<IncomeComparison>();
    vi.mocked(api.getIncomeComparison).mockReturnValue(savedRead.promise);
    renderComparison(selectionQuery);
    const compare = screen.getByRole("button", { name: "Compare and retain explanation" });
    await waitFor(() => expect(compare).toBeEnabled());
    fireEvent.click(compare);

    await waitFor(() => expect(api.getIncomeComparison).toHaveBeenCalledWith(retained.comparisonId, expect.objectContaining({ signal: expect.any(AbortSignal) })));
    expect(api.createIncomeComparison).toHaveBeenCalledWith({ baselineRunId: publishedIncomeRun.runId, currentRunId: currentIncomeRun.runId, gridId: "investment-income", metricColumn: "Income" }, expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(screen.queryByRole("region", { name: "Retained income explanation" })).not.toBeInTheDocument();
    expect(queryContext().get("incomeComparison")).toBe(retained.comparisonId);
    expect(queryContext().get("tab")).toBe("retained");

    await act(async () => savedRead.resolve(retained));
    expect(await screen.findByText("Movement reconciled to retained evidence")).toBeInTheDocument();
    const dimensions = screen.getByRole("table", { name: "Comparison dimensions" });
    for (const name of ["Period", "Population", "Accounting basis", "Currency"]) {
      expect(within(dimensions).getByRole("rowheader", { name })).toBeInTheDocument();
    }
    expect(screen.getByRole("heading", { name: "Retained journal contributions" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Population changes" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Documented methodology changes" })).toBeInTheDocument();
  });

  it("opens the exact late accrual journal and returns with the retained pair and measure intact", async () => {
    const user = userEvent.setup();
    renderComparison(savedQuery);
    const trigger = await screen.findByRole("button", { name: "Open retained support for Late June interest accrual" });
    await user.click(trigger);

    const dialog = await screen.findByRole("dialog", { name: "Retained supporting record" });
    expect(await within(dialog).findByText("Record: journal-late-accrual-17")).toBeInTheDocument();
    expect(within(dialog).getByText("income-line-2")).toBeInTheDocument();
    expect(within(dialog).getByText("125")).toBeInTheDocument();
    expect(dialog).toHaveAccessibleDescription(`${publishedIncomeRun.runId} → ${currentIncomeRun.runId}. Closing returns to this comparison.`);
    expect(api.getIncomeContributionSupport).toHaveBeenCalledWith(retained.comparisonId, "journal:late-accrual-17", expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(queryContext().get("incomeContribution")).toBe("journal:late-accrual-17");

    await user.click(within(dialog).getByRole("button", { name: "Return to comparison" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(queryContext().get("incomeContribution")).toBeNull();
    expect(queryContext().get("incomeComparison")).toBe(retained.comparisonId);
    expect(queryContext().get("incomeBaseline")).toBe(publishedIncomeRun.runId);
    expect(queryContext().get("incomeCurrent")).toBe(currentIncomeRun.runId);
    expect(queryContext().get("incomeGrid")).toBe("investment-income");
    expect(queryContext().get("incomeMetric")).toBe("Income");
    await waitFor(() => expect(trigger).toHaveFocus());
    expect(api.createIncomeComparison).not.toHaveBeenCalled();
  });

  it.each([1, 2])("opens retained support for contribution %i without leaving the comparison", async (index) => {
    const contribution = retained.contributions[index];
    vi.mocked(api.getIncomeContributionSupport).mockResolvedValue(buildIncomeContributionSupport(retained, index));
    renderComparison(savedQuery);
    fireEvent.click(await screen.findByRole("button", { name: `Open retained support for ${contribution.label}` }));

    const dialog = await screen.findByRole("dialog");
    expect(await within(dialog).findByText(`Record: ${contribution.recordId}`)).toBeInTheDocument();
    expect(api.getIncomeContributionSupport).toHaveBeenCalledWith(retained.comparisonId, contribution.contributionId, expect.anything());
    expect(queryContext().get("incomeComparison")).toBe(retained.comparisonId);
  });

  it("reopens a saved explanation and journal from its URL when current candidate discovery fails", async () => {
    vi.mocked(api.getIncomeComparisonCandidates).mockRejectedValue(new Error("Candidate source changed"));
    renderComparison(`incomeComparison=${retained.comparisonId}&incomeContribution=journal%3Alate-accrual-17`);

    expect(await screen.findByText("Retained run selection unavailable")).toBeInTheDocument();
    expect(await screen.findByText("Movement reconciled to retained evidence")).toBeInTheDocument();
    expect(await screen.findByText("Record: journal-late-accrual-17")).toBeInTheDocument();
    expect(api.getIncomeComparison).toHaveBeenCalledWith(retained.comparisonId, expect.anything());
    expect(api.createIncomeComparison).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Compare and retain explanation" })).toBeDisabled();
  });

  it("keeps an unsupported residual visible beside supported journal contributions", async () => {
    vi.mocked(api.getIncomeComparison).mockResolvedValue(buildIncomeComparison({ currentAmount: "1192", movement: "192", residualAmount: "42", status: "Unexplained", warnings: ["42 USD has no retained supporting record."] }));
    renderComparison(savedQuery);

    expect(await screen.findByText("Unexplained differences remain")).toBeInTheDocument();
    expect(screen.getByText("Unexplained residual").parentElement).toHaveTextContent("42.00 USD");
    expect(screen.getByRole("list", { name: "Comparison warnings" })).toHaveTextContent("42 USD has no retained supporting record.");
    expect(screen.getByRole("button", { name: "Open retained support for Late June interest accrual" })).toBeInTheDocument();
    expect(screen.queryByText("Movement reconciled to retained evidence")).not.toBeInTheDocument();
  });

  it.each([
    { residual: "0.0000000001", visible: "0.0000000001 USD" },
    { residual: "-0.000000000000000000000001", visible: "-0.000000000000000000000001 USD" },
    { residual: "0.0000000000000000000000000001", visible: "0.0000000000000000000000000001 USD" }
  ])("keeps a nonzero unsupported residual of $residual visible instead of rounding it to zero", async ({ residual, visible }) => {
    vi.mocked(api.getIncomeComparison).mockResolvedValue(buildIncomeComparison({
      baselineAmount: "0",
      currentAmount: residual,
      movement: residual,
      explainedAmount: "0",
      residualAmount: residual,
      contributions: [],
      status: "Reconciled"
    }));
    renderComparison(savedQuery);

    expect(await screen.findByText("Unexplained differences remain")).toBeInTheDocument();
    expect(screen.getByText("Unexplained residual").parentElement?.querySelector("dd")).toHaveTextContent(visible);
    expect(screen.getByText("Total movement").parentElement?.querySelector("dd")).toHaveTextContent(visible);
    expect(screen.queryByText("Movement reconciled to retained evidence")).not.toBeInTheDocument();
  });

  it.each([
    { retainedAmount: "9007199254740993", visible: "9,007,199,254,740,993.00 USD" },
    { retainedAmount: "9007199254740993.123456789012", visible: "9,007,199,254,740,993.123456789012 USD" },
    { retainedAmount: "-79228162514264337593543950335", visible: "-79,228,162,514,264,337,593,543,950,335.00 USD" },
    { retainedAmount: "7.9228162514264337593543950335", visible: "7.9228162514264337593543950335 USD" }
  ])("preserves every retained digit of $retainedAmount in totals, contributions, and exact journal support", async ({ retainedAmount, visible }) => {
    const exact = buildIncomeComparison({
      baselineAmount: "0",
      currentAmount: retainedAmount,
      movement: retainedAmount,
      explainedAmount: retainedAmount,
      contributions: [{ ...retained.contributions[0], amount: retainedAmount }]
    });
    vi.mocked(api.getIncomeComparison).mockResolvedValue(exact);
    vi.mocked(api.getIncomeContributionSupport).mockResolvedValue(buildIncomeContributionSupport(exact));
    renderComparison(savedQuery);

    expect(await screen.findByText("Movement reconciled to retained evidence")).toBeInTheDocument();
    for (const label of ["Current income", "Total movement", "Supported contributions"]) {
      expect(screen.getByText(label).parentElement?.querySelector("dd")?.textContent).toBe(visible);
    }
    expect(within(screen.getByText("Late June interest accrual").closest("li")!).getByText(visible)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Open retained support for Late June interest accrual" }));
    const dialog = await screen.findByRole("dialog");
    expect(await within(dialog).findByText(retainedAmount, { exact: true })).toBeInTheDocument();
  });

  it("normalizes signed decimal zero consistently and reconciles only the server-confirmed exact zero", async () => {
    vi.mocked(api.getIncomeComparison).mockResolvedValue(buildIncomeComparison({
      baselineAmount: "-0.000",
      currentAmount: "0.0000",
      movement: "-0",
      explainedAmount: "-0.00",
      residualAmount: "-0.0000",
      contributions: [{ ...retained.contributions[0], amount: "-0.00" }]
    }));
    renderComparison(savedQuery);

    expect(await screen.findByText("Movement reconciled to retained evidence")).toBeInTheDocument();
    for (const label of ["Baseline income", "Current income", "Total movement", "Supported contributions", "Unexplained residual"]) {
      expect(screen.getByText(label).parentElement?.querySelector("dd")?.textContent).toBe("0.00 USD");
    }
    expect(within(screen.getByText("Late June interest accrual").closest("li")!).getByText("0.00 USD", { exact: true })).toBeInTheDocument();
    expect(screen.queryByText("-0.00 USD", { exact: true })).not.toBeInTheDocument();
  });

  it("does not treat a missing retained residual as exact zero", async () => {
    vi.mocked(api.getIncomeComparison).mockResolvedValue(buildIncomeComparison({ residualAmount: null, status: "Reconciled" }));
    renderComparison(savedQuery);

    expect(await screen.findByText("Unexplained differences remain")).toBeInTheDocument();
    expect(screen.getByText("Unexplained residual").parentElement).toHaveTextContent("Not comparable");
    expect(screen.queryByText("Movement reconciled to retained evidence")).not.toBeInTheDocument();
  });

  it("does not call offsetting unsupported movements reconciled when the net residual is zero", async () => {
    const unsupported = buildIncomeComparison({
      status: "Unexplained",
      residualAmount: "0",
      warnings: ["Unsupported positive and negative movements offset."],
      contributions: [
        ...retained.contributions,
        { contributionId: "unexplained:credit", kind: "Unexplained", label: "Unsupported credit movement", amount: "25", recordId: "unexplained-income-credit", sourceRunId: currentIncomeRun.runId, detail: "Retained row has no exact supporting journal." },
        { contributionId: "unexplained:debit", kind: "Unexplained", label: "Unsupported debit movement", amount: "-25", recordId: "unexplained-income-debit", sourceRunId: currentIncomeRun.runId, detail: "Retained row has no documented methodology change." }
      ]
    });
    vi.mocked(api.getIncomeComparison).mockResolvedValue(unsupported);
    vi.mocked(api.getIncomeContributionSupport).mockResolvedValue(buildIncomeContributionSupport(unsupported, 3));
    renderComparison(savedQuery);

    expect(await screen.findByText("Unexplained differences remain")).toBeInTheDocument();
    expect(screen.getByText("Unexplained residual").parentElement).toHaveTextContent("0.00 USD");
    expect(screen.getByText("Unsupported positive and negative movements offset.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Unexplained retained movements" })).toBeInTheDocument();
    expect(screen.getByText("Unsupported credit movement").closest("li")).toHaveTextContent("25.00 USD");
    expect(screen.getByText("Unsupported debit movement").closest("li")).toHaveTextContent("-25.00 USD");
    expect(screen.queryByText("Movement reconciled to retained evidence")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Open retained support for Unsupported credit movement" }));
    expect(await screen.findByText("Record: unexplained-income-credit")).toBeInTheDocument();
    expect(queryContext().get("incomeComparison")).toBe(retained.comparisonId);
  });

  it("cannot present incompatible zero movement as reconciled even when the server status says Reconciled", async () => {
    vi.mocked(api.getIncomeComparison).mockResolvedValue(buildIncomeComparison({ compatible: false, status: "Reconciled", movement: "0", residualAmount: "0", current: { ...currentIncomeRun, currency: "EUR" }, differences: [{ dimension: "Currency", baseline: "USD", current: "EUR", compatible: false, detail: "No retained conversion basis." }] }));
    renderComparison(savedQuery);

    expect(await screen.findByText("Incompatible comparison — not reconciled")).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Comparison dimensions" })).toHaveTextContent("USD");
    expect(screen.getByRole("table", { name: "Comparison dimensions" })).toHaveTextContent("EUR");
    expect(screen.getByText("Incompatible. No retained conversion basis.")).toBeInTheDocument();
    expect(screen.queryByText("Movement reconciled to retained evidence")).not.toBeInTheDocument();
  });

  it("shows candidate failures and never supplies a sample pair or explanation", async () => {
    vi.mocked(api.getIncomeComparisonCandidates).mockRejectedValue(new Error("Access denied"));
    renderComparison();

    expect(await screen.findByText("Retained run selection unavailable")).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Baseline run" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Compare and retain explanation" })).toBeDisabled();
    expect(screen.queryByRole("region", { name: "Retained income explanation" })).not.toBeInTheDocument();
    expect(api.createIncomeComparison).not.toHaveBeenCalled();
  });

  it("keeps a failed saved read unavailable without recreating it", async () => {
    vi.mocked(api.getIncomeComparison).mockRejectedValue(new Error("Retained manifest missing"));
    renderComparison(savedQuery);

    expect(await screen.findByRole("alert")).toHaveTextContent("Retry without recalculating it.");
    expect(screen.queryByRole("region", { name: "Retained income explanation" })).not.toBeInTheDocument();
    expect(api.createIncomeComparison).not.toHaveBeenCalled();
    expect(queryContext().get("incomeComparison")).toBe(retained.comparisonId);
  });

  it("does not display or link an explanation when retaining it fails", async () => {
    vi.mocked(api.createIncomeComparison).mockRejectedValue(new Error("Retention failed"));
    renderComparison(selectionQuery);
    const compare = screen.getByRole("button", { name: "Compare and retain explanation" });
    await waitFor(() => expect(compare).toBeEnabled());
    fireEvent.click(compare);

    expect(await screen.findByRole("alert")).toHaveTextContent("No retained explanation was confirmed.");
    expect(queryContext().get("incomeComparison")).toBeNull();
    expect(api.getIncomeComparison).not.toHaveBeenCalled();
    expect(screen.queryByRole("region", { name: "Retained income explanation" })).not.toBeInTheDocument();
  });

  it("rejects a saved comparison response for a different identity", async () => {
    vi.mocked(api.getIncomeComparison).mockResolvedValue(buildIncomeComparison({ comparisonId: "wrong-comparison" }));
    renderComparison(savedQuery);
    expect(await screen.findByRole("alert")).toHaveTextContent("Comparison unavailable");
    expect(screen.queryByRole("region", { name: "Retained income explanation" })).not.toBeInTheDocument();
  });

  it("rejects a retention response for a pair other than the explicit selection", async () => {
    vi.mocked(api.createIncomeComparison).mockResolvedValue(buildIncomeComparison({ baseline: restatedIncomeRun }));
    renderComparison(selectionQuery);
    const compare = screen.getByRole("button", { name: "Compare and retain explanation" });
    await waitFor(() => expect(compare).toBeEnabled());
    fireEvent.click(compare);

    expect(await screen.findByRole("alert")).toHaveTextContent("No retained explanation was confirmed.");
    expect(queryContext().get("incomeComparison")).toBeNull();
    expect(api.getIncomeComparison).not.toHaveBeenCalled();
  });

  it("rejects support from a different retained pair and preserves the return path", async () => {
    vi.mocked(api.getIncomeContributionSupport).mockResolvedValue({ ...buildIncomeContributionSupport(), baselineRunId: "wrong-baseline" });
    renderComparison(`${savedQuery}&incomeContribution=journal%3Alate-accrual-17`);
    const dialog = await screen.findByRole("dialog");

    expect(await within(dialog).findByRole("alert")).toHaveTextContent("Supporting record unavailable");
    expect(within(dialog).queryByText("Record: journal-late-accrual-17")).not.toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Return to comparison" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(queryContext().get("incomeComparison")).toBe(retained.comparisonId);
  });

  it("ignores a late retained comparison response after another saved identity is selected", async () => {
    const stale = deferred<IncomeComparison>();
    vi.mocked(api.getIncomeComparison).mockReturnValueOnce(stale.promise).mockResolvedValue(buildIncomeComparison({ comparisonId: "comparison-2", status: "Unexplained", residualAmount: "9" }));
    renderComparison(savedQuery);
    await waitFor(() => expect(api.getIncomeComparison).toHaveBeenCalledTimes(1));
    const oldSignal = vi.mocked(api.getIncomeComparison).mock.calls[0][1]?.signal;
    fireEvent.click(screen.getByRole("button", { name: "Open another saved comparison" }));
    expect(await screen.findByText("Unexplained differences remain")).toBeInTheDocument();
    expect(oldSignal?.aborted).toBe(true);

    await act(async () => stale.resolve(retained));
    expect(screen.getByText("Unexplained residual").parentElement).toHaveTextContent("9.00 USD");
    expect(screen.queryByText("Movement reconciled to retained evidence")).not.toBeInTheDocument();
    expect(queryContext().get("incomeComparison")).toBe("comparison-2");
  });

  it("abandons a pending retention when the baseline changes", async () => {
    const stale = deferred<IncomeComparison>();
    vi.mocked(api.createIncomeComparison).mockReturnValue(stale.promise);
    renderComparison(selectionQuery);
    const compare = screen.getByRole("button", { name: "Compare and retain explanation" });
    await waitFor(() => expect(compare).toBeEnabled());
    fireEvent.click(compare);
    const signal = vi.mocked(api.createIncomeComparison).mock.calls[0][1]?.signal;
    fireEvent.change(screen.getByRole("combobox", { name: "Baseline run" }), { target: { value: restatedIncomeRun.runId } });
    expect(signal?.aborted).toBe(true);

    await act(async () => stale.resolve(retained));
    expect(queryContext().get("incomeBaseline")).toBe(restatedIncomeRun.runId);
    expect(queryContext().get("incomeComparison")).toBeNull();
    expect(api.getIncomeComparison).not.toHaveBeenCalled();
  });

  it("aborts an outstanding support read when returning to the comparison", async () => {
    const stale = deferred<ReturnType<typeof buildIncomeContributionSupport>>();
    vi.mocked(api.getIncomeContributionSupport).mockReturnValue(stale.promise);
    renderComparison(`${savedQuery}&incomeContribution=journal%3Alate-accrual-17`);
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByRole("status")).toHaveTextContent("Loading retained support");
    const signal = vi.mocked(api.getIncomeContributionSupport).mock.calls[0][2]?.signal;
    fireEvent.click(within(dialog).getByRole("button", { name: "Return to comparison" }));
    expect(signal?.aborted).toBe(true);

    await act(async () => stale.resolve(buildIncomeContributionSupport()));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(queryContext().get("incomeComparison")).toBe(retained.comparisonId);
    expect(queryContext().get("incomeContribution")).toBeNull();
  });

  it("ignores late candidate discovery after the host run scope changes", async () => {
    const stale = deferred<IncomeComparisonRun[]>();
    vi.mocked(api.getIncomeComparisonCandidates).mockReturnValueOnce(stale.promise).mockResolvedValue([]);
    renderComparison();
    const oldSignal = vi.mocked(api.getIncomeComparisonCandidates).mock.calls[0][0]?.signal;
    fireEvent.click(screen.getByRole("button", { name: "Change host run" }));
    expect(await screen.findByText("Two retained runs required")).toBeInTheDocument();
    expect(oldSignal?.aborted).toBe(true);

    await act(async () => stale.resolve(incomeComparisonCandidates));
    expect(within(screen.getByRole("combobox", { name: "Baseline run" })).queryByRole("option", { name: /Originally published/ })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Compare and retain explanation" })).toBeDisabled();
  });

  it("aborts pending retention when the host run scope changes", async () => {
    const stale = deferred<IncomeComparison>();
    vi.mocked(api.createIncomeComparison).mockReturnValue(stale.promise);
    renderComparison(selectionQuery);
    const compare = screen.getByRole("button", { name: "Compare and retain explanation" });
    await waitFor(() => expect(compare).toBeEnabled());
    fireEvent.click(compare);
    const signal = vi.mocked(api.createIncomeComparison).mock.calls[0][1]?.signal;
    fireEvent.click(screen.getByRole("button", { name: "Change host run" }));
    expect(signal?.aborted).toBe(true);

    await act(async () => stale.resolve(retained));
    expect(queryContext().get("incomeComparison")).toBeNull();
    expect(api.getIncomeComparison).not.toHaveBeenCalled();
  });

  it("has no basic accessibility violations in the retained explanation and supporting-record sheet", async () => {
    const { container } = renderComparison(savedQuery);
    const support = await screen.findByRole("button", { name: "Open retained support for Late June interest accrual" });
    expect((await axe(container)).violations).toEqual([]);

    fireEvent.click(support);
    await screen.findByText("Record: journal-late-accrual-17");
    expect((await axe(container)).violations).toEqual([]);
  });
});
