import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import {
  buildCellActions,
  useCellActions,
  CellActionConfirmDialog,
  CellActionTrigger,
  ContextMenu,
  type AnomalyCellContext,
  type CellActionApi,
  type CellActionId,
  type CoverageGapCellContext
} from "./data-screen.cell-actions";
import type { ToastApi } from "@/components/ui/toast";

const coverageContext: CoverageGapCellContext = {
  kind: "coverage-gap",
  symbol: "GME",
  sourcesLabel: "configured-streaming"
};

const anomalyContext: AnomalyCellContext = {
  kind: "anomaly",
  symbol: "AMC",
  anomalyId: "anomaly-1",
  anomalyType: "PriceSpike"
};

function actionIds(context: CoverageGapCellContext | AnomalyCellContext) {
  return buildCellActions(context, () => undefined)
    .filter((entry) => entry.type !== "divider")
    .map((entry) => entry.id);
}

describe("buildCellActions", () => {
  it("projects coverage-gap actions with a destructive exclude", () => {
    expect(actionIds(coverageContext)).toEqual([
      "backfill",
      "master",
      "open-capability-matrix",
      "exclude"
    ]);
    const exclude = buildCellActions(coverageContext, () => undefined).find((entry) => entry.id === "exclude");
    expect(exclude?.type !== "divider" && exclude?.danger).toBe(true);
  });

  it("projects anomaly actions without a destructive item", () => {
    expect(actionIds(anomalyContext)).toEqual([
      "acknowledge",
      "backfill",
      "compare-providers",
      "open-capability-matrix"
    ]);
    const dangerous = buildCellActions(anomalyContext, () => undefined).some(
      (entry) => entry.type !== "divider" && entry.danger
    );
    expect(dangerous).toBe(false);
  });

  it("routes every actionable item through the dispatcher", () => {
    const dispatch = vi.fn();
    for (const entry of buildCellActions(coverageContext, dispatch)) {
      if (entry.type !== "divider") {
        entry.onSelect?.();
      }
    }
    expect(dispatch).toHaveBeenCalledWith<[CellActionId]>("backfill");
    expect(dispatch).toHaveBeenCalledWith<[CellActionId]>("exclude");
  });

  it("disables mutating actions while busy but keeps navigation live", () => {
    const entries = buildCellActions(coverageContext, () => undefined, { busy: true });
    const disabledFor = (id: CellActionId) => {
      const entry = entries.find((candidate) => candidate.id === id);
      return entry && entry.type !== "divider" ? entry.disabled : undefined;
    };
    expect(disabledFor("backfill")).toBe(true);
    expect(disabledFor("open-capability-matrix")).toBeFalsy();
  });
});

function stubToast(): ToastApi {
  return {
    dismiss: vi.fn(),
    show: vi.fn(() => ""),
    success: vi.fn(() => ""),
    warning: vi.fn(() => ""),
    danger: vi.fn(() => ""),
    info: vi.fn(() => "")
  };
}

function Harness({
  context,
  toast,
  api,
  onOpenCapabilityMatrix
}: {
  context: CoverageGapCellContext | AnomalyCellContext;
  toast: ToastApi;
  api?: Partial<CellActionApi>;
  onOpenCapabilityMatrix?: (symbol: string) => void;
}) {
  const cellActions = useCellActions({ toast, api, onOpenCapabilityMatrix });
  return (
    <div>
      <CellActionTrigger label="Open actions" onOpen={(event) => cellActions.openFor(event, context)} />
      <ContextMenu
        open={cellActions.menu.open}
        position={cellActions.menu.position}
        items={cellActions.menu.items}
        onClose={cellActions.menu.close}
        label={cellActions.menu.label}
      />
      <CellActionConfirmDialog
        confirm={cellActions.confirm}
        running={cellActions.running}
        onResolve={cellActions.resolveConfirm}
      />
    </div>
  );
}

describe("useCellActions", () => {
  it("triggers a backfill through the endpoint layer and reports success", async () => {
    const user = userEvent.setup();
    const toast = stubToast();
    const triggerBackfill = vi.fn().mockResolvedValue({ success: true });

    render(<Harness context={coverageContext} toast={toast} api={{ triggerBackfill }} />);

    await user.click(screen.getByRole("button", { name: "Open actions" }));
    await user.click(await screen.findByRole("menuitem", { name: /Backfill this range/ }));

    await waitFor(() =>
      expect(triggerBackfill).toHaveBeenCalledWith({ provider: null, symbols: ["GME"], from: null, to: null })
    );
    expect(toast.success).toHaveBeenCalled();
  });

  it("gates exclude behind a confirmation dialog before archiving", async () => {
    const user = userEvent.setup();
    const toast = stubToast();
    const archiveSymbol = vi.fn().mockResolvedValue({ success: true, symbol: "GME" });

    render(<Harness context={coverageContext} toast={toast} api={{ archiveSymbol }} />);

    await user.click(screen.getByRole("button", { name: "Open actions" }));
    await user.click(await screen.findByRole("menuitem", { name: /Exclude symbol/ }));

    // The archive must not fire until the operator confirms.
    expect(archiveSymbol).not.toHaveBeenCalled();
    const dialog = await screen.findByRole("dialog");
    expect(dialog).toHaveTextContent("Exclude GME?");

    await user.click(screen.getByRole("button", { name: "Exclude symbol" }));
    await waitFor(() => expect(archiveSymbol).toHaveBeenCalledWith("GME"));
  });

  it("cancels exclude without archiving", async () => {
    const user = userEvent.setup();
    const archiveSymbol = vi.fn();

    render(<Harness context={coverageContext} toast={stubToast()} api={{ archiveSymbol }} />);

    await user.click(screen.getByRole("button", { name: "Open actions" }));
    await user.click(await screen.findByRole("menuitem", { name: /Exclude symbol/ }));
    await user.click(screen.getByRole("button", { name: "Cancel" }));

    expect(archiveSymbol).not.toHaveBeenCalled();
  });

  it("keeps the confirm dialog open and undismissable while the action is in flight", async () => {
    const user = userEvent.setup();
    let resolveArchive: (() => void) | undefined;
    const archiveSymbol = vi.fn(
      () =>
        new Promise<{ success: boolean; symbol: string }>((resolve) => {
          resolveArchive = () => resolve({ success: true, symbol: "GME" });
        })
    );

    render(<Harness context={coverageContext} toast={stubToast()} api={{ archiveSymbol }} />);

    await user.click(screen.getByRole("button", { name: "Open actions" }));
    await user.click(await screen.findByRole("menuitem", { name: /Exclude symbol/ }));
    await user.click(screen.getByRole("button", { name: "Exclude symbol" }));

    // In flight: the confirm control is disabled and Escape must not dismiss the dialog.
    await waitFor(() => expect(screen.getByRole("button", { name: "Working…" })).toBeDisabled());
    await user.keyboard("{Escape}");
    expect(screen.getByRole("dialog")).toBeInTheDocument();

    resolveArchive?.();
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("acknowledges an anomaly through the endpoint layer", async () => {
    const user = userEvent.setup();
    const acknowledgeAnomaly = vi.fn().mockResolvedValue(undefined);

    render(<Harness context={anomalyContext} toast={stubToast()} api={{ acknowledgeAnomaly }} />);

    await user.click(screen.getByRole("button", { name: "Open actions" }));
    await user.click(await screen.findByRole("menuitem", { name: /Flag as acknowledged/ }));

    await waitFor(() => expect(acknowledgeAnomaly).toHaveBeenCalledWith("anomaly-1"));
  });

  it("delegates compare-across-providers to the capability-matrix navigator", async () => {
    const user = userEvent.setup();
    const onOpenCapabilityMatrix = vi.fn();

    render(
      <Harness context={anomalyContext} toast={stubToast()} onOpenCapabilityMatrix={onOpenCapabilityMatrix} />
    );

    await user.click(screen.getByRole("button", { name: "Open actions" }));
    await user.click(await screen.findByRole("menuitem", { name: /Compare across providers/ }));

    expect(onOpenCapabilityMatrix).toHaveBeenCalledWith("AMC");
  });
});
