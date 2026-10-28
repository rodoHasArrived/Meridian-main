import { screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { getAccountingWorkspace, getReconciliationBreakQueue } from "@/lib/api";
import { RECONCILIATION_API_ENDPOINTS, WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import { getScenario, resolveScenarioResponse, scenarioPreviewUrl } from "@/scenarios";
import { accountingPayload } from "@/scenarios/accounting-payload";
import { ApprovalInboxScreen } from "@/screens/finance-standard-pages-screen";
import { ReconciliationQueueSummaryCard } from "@/screens/accounting-screen.operations-panels";
import { buildReconciliationQueuePanelViewState } from "@/screens/accounting-screen.reconciliation.view-model";
import { requireFirst } from "@/test/fixtures";
import { renderWithRouter } from "@/test/render";
import { installScenario } from "@/test/scenarios";

const accountingEndpoint = WORKSTATION_API_ENDPOINTS.accounting;

afterEach(() => {
  vi.useRealTimers();
});

describe("shared accounting scenarios", () => {
  it.each([
    ["accounting.normal", 200],
    ["accounting.empty", 200],
    ["accounting.delayed", 200],
    ["accounting.forbidden", 403],
    ["accounting.failed", 500]
  ] as const)("selects %s with explicit HTTP %s", (id, status) => {
    const scenario = getScenario(id);
    expect(resolveScenarioResponse(scenario, accountingEndpoint)?.status).toBe(status);
    expect(resolveScenarioResponse(scenario, RECONCILIATION_API_ENDPOINTS.breakQueue)?.status).toBe(status);
    const preview = new URL(scenarioPreviewUrl(scenario), "http://localhost");
    expect(preview.pathname).toBe("/workstation/accounting/reconciliation");
    expect(preview.searchParams.get("scenario")).toBe(id);
  });

  it("passes the one accounting payload through the real API client and production queue card", async () => {
    installScenario("accounting.normal");
    const workspace = await getAccountingWorkspace();
    expect(workspace).toEqual(accountingPayload);
    await expect(getReconciliationBreakQueue()).resolves.toEqual(accountingPayload.breakQueue);

    renderWithRouter(
      <ReconciliationQueueSummaryCard
        view={buildReconciliationQueuePanelViewState(workspace.reconciliationQueue, null)}
      />
    );

    const record = requireFirst(accountingPayload.reconciliationQueue, "accounting reconciliation queue");
    const row = screen.getByRole("row", {
      name: `${record.strategyName}. ${record.reconciliationStatus}. ${record.openBreakCount} open breaks. Updated ${record.lastUpdated}.`
    });
    expect(within(row).getByText(record.strategyName)).toBeVisible();
    expect(within(row).getByText(`${record.openBreakCount} open`)).toBeVisible();
    expect(screen.getByRole("link", { name: "Open Accounting reconciliation workstream" }))
      .toHaveAttribute("href", "/accounting/reconciliation");
  });

  it("keeps an empty accounting response empty in the production queue card", async () => {
    installScenario("accounting.empty");
    const workspace = await getAccountingWorkspace();
    expect(workspace.reconciliationQueue).toEqual([]);
    expect(workspace.breakQueue).toEqual([]);
    await expect(getReconciliationBreakQueue()).resolves.toEqual([]);

    renderWithRouter(
      <ReconciliationQueueSummaryCard
        view={buildReconciliationQueuePanelViewState(workspace.reconciliationQueue, null)}
      />
    );
    expect(screen.getByText("No reconciliation runs are available for this accounting scope.")).toBeVisible();
    const record = requireFirst(accountingPayload.reconciliationQueue, "accounting reconciliation queue");
    expect(screen.queryByText(record.strategyName)).not.toBeInTheDocument();
  });

  it("retains the shared approval inbox sample and its decision link through the real API client", async () => {
    installScenario("accounting.normal");
    const workspace = await getAccountingWorkspace();
    renderWithRouter(<ApprovalInboxScreen data={workspace} />);
    const closePlan = requireFirst(accountingPayload.closePlans, "accounting close-plan summaries");
    const approval = requireFirst(closePlan.approvals, "accounting pending approvals");
    expect(screen.getByRole("heading", { name: "Approval queue" })).toBeVisible();
    expect(screen.getByText(approval.label)).toBeVisible();
    expect(screen.getByRole("link", { name: `Review and decide ${approval.label}` })).toHaveAttribute(
      "href", `/accounting/approvals?approvalId=${approval.approvalId}`
    );
  });

  it("keeps the empty approval inbox free of populated scenario rows", async () => {
    installScenario("accounting.empty");
    const workspace = await getAccountingWorkspace();
    renderWithRouter(<ApprovalInboxScreen data={workspace} />);
    expect(screen.getByText("No approvals in the supplied accounting scope")).toBeVisible();
    expect(screen.queryByRole("heading", { name: "Approval queue" })).not.toBeInTheDocument();
  });

  it("holds the delayed response pending until its declared delay has elapsed", async () => {
    vi.useFakeTimers();
    const scenario = getScenario("accounting.delayed");
    const delayMs = resolveScenarioResponse(scenario, accountingEndpoint)?.delayMs;
    expect(delayMs).toBeGreaterThan(0);
    installScenario(scenario);

    let completed = false;
    const request = getAccountingWorkspace().then((workspace) => {
      completed = true;
      return workspace;
    });
    await vi.advanceTimersByTimeAsync((delayMs ?? 0) - 1);
    expect(completed).toBe(false);
    await vi.advanceTimersByTimeAsync(1);
    await expect(request).resolves.toEqual(accountingPayload);
    expect(completed).toBe(true);
  });

  it("honors cancellation while an accounting response is delayed", async () => {
    vi.useFakeTimers();
    installScenario("accounting.delayed");
    const controller = new AbortController();
    const request = getAccountingWorkspace({ signal: controller.signal });
    const rejected = expect(request).rejects.toMatchObject({ name: "AbortError" });
    controller.abort();
    await rejected;
  });

  it.each([
    ["accounting.forbidden", 403],
    ["accounting.failed", 500]
  ] as const)("preserves the %s API error without a development fixture fallback", async (id, status) => {
    installScenario(id);
    expect(import.meta.env.VITE_MERIDIAN_DEV_MODE).toBe("fixture-only");
    await expect(getAccountingWorkspace()).rejects.toMatchObject({ status });
    await expect(getReconciliationBreakQueue()).rejects.toMatchObject({ status });
  });

  it("keeps scenario failures visible even with the legacy development fallback enabled", async () => {
    installScenario("accounting.failed");
    expect(import.meta.env.DEV).toBe(true);
    vi.stubEnv("VITE_MERIDIAN_DEV_MODE", "");
    await expect(getAccountingWorkspace()).rejects.toMatchObject({ status: 500 });
  });

  it("serves the selected scenario when a caller disables automatic development fallback", async () => {
    installScenario("accounting.normal");
    await expect(getAccountingWorkspace({ allowDevelopmentFallback: false })).resolves.toEqual(accountingPayload);
  });

  it("keeps consumer edits isolated from the shared accounting payload", async () => {
    installScenario("accounting.normal");
    const workspace = await getAccountingWorkspace();
    const record = requireFirst(workspace.reconciliationQueue, "accounting reconciliation queue");
    record.strategyName = "Edited in a consumer";
    await expect(getAccountingWorkspace()).resolves.toEqual(accountingPayload);
  });
});

describe("strict Vitest scenario installation", () => {
  it.each([
    ["GET", "/api/workstation/misspelled-accounting"],
    ["GET", `${accountingEndpoint}/unexpected`],
    ["POST", accountingEndpoint]
  ])("reports caught unexpected %s %s requests at cleanup", async (method, url) => {
    const scenario = installScenario("accounting.normal");
    // Application error handlers may catch this rejection. Cleanup still fails.
    const error = await fetch(url, { method }).catch((caught: unknown) => caught);
    expect(error).toBeInstanceOf(Error);
    expect(String(error)).toContain("accounting.normal");
    expect(String(error)).toContain(method);
    expect(String(error)).toContain(url);
    expect(scenario.unexpectedRequests).toHaveLength(1);
    expect(() => scenario.restore()).toThrow(url);
  });

  it("restores the preceding fetch and environment without removing unrelated stubs", () => {
    const previousFetch = globalThis.fetch;
    const previousMode = import.meta.env.VITE_MERIDIAN_DEV_MODE;
    const previousProcessMode = process.env.VITE_MERIDIAN_DEV_MODE;
    const precedingFetch = vi.fn<typeof fetch>();
    const markerKey = "__meridianScenarioUnrelatedStub";
    const marker = { retained: true };
    const globals = globalThis as typeof globalThis & Record<string, unknown>;
    const markerDescriptor = Object.getOwnPropertyDescriptor(globals, markerKey);

    try {
      globalThis.fetch = precedingFetch;
      Reflect.set(import.meta.env, "VITE_MERIDIAN_DEV_MODE", "backend-connected");
      process.env.VITE_MERIDIAN_DEV_MODE = "backend-connected";
      vi.stubGlobal(markerKey, marker);
      const scenario = installScenario("accounting.normal");
      expect(globalThis.fetch).not.toBe(precedingFetch);
      scenario.restore();
      expect(globalThis.fetch).toBe(precedingFetch);
      expect(import.meta.env.VITE_MERIDIAN_DEV_MODE).toBe("backend-connected");
      expect(process.env.VITE_MERIDIAN_DEV_MODE).toBe("backend-connected");
      expect(globals[markerKey]).toBe(marker);
    } finally {
      globalThis.fetch = previousFetch;
      Reflect.set(import.meta.env, "VITE_MERIDIAN_DEV_MODE", previousMode);
      if (previousProcessMode === undefined) delete process.env.VITE_MERIDIAN_DEV_MODE;
      else process.env.VITE_MERIDIAN_DEV_MODE = previousProcessMode;
      if (markerDescriptor) Object.defineProperty(globals, markerKey, markerDescriptor);
      else Reflect.deleteProperty(globals, markerKey);
    }
  });
});
