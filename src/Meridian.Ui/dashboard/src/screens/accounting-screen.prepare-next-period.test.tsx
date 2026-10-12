import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { axe } from "jest-axe";
import { ApiError } from "@/lib/api-errors";
import * as api from "@/lib/api/close-preparation.api";
import { PrepareNextPeriodPanel } from "./accounting-screen.prepare-next-period";
import { preparationPreview, preparedResult, retainedTemplate, sourcePlan, sourceWorkflow, targetBook, targetPeriod } from "./accounting-screen.prepare-next-period.test-fixtures";

vi.mock("@/lib/api/close-preparation.api", () => ({
  listClosePreparationSources: vi.fn(), getClosePreparationSource: vi.fn(), getClosePreparationBooks: vi.fn(), getClosePreparationPeriods: vi.fn(),
  listClosePlanTemplates: vi.fn(), captureClosePlanTemplate: vi.fn(), previewClosePreparation: vi.fn(), createPreparedClosePlan: vi.fn(),
}));
const service = vi.mocked(api);

beforeEach(() => {
  vi.resetAllMocks();
  sessionStorage.clear();
  service.listClosePreparationSources.mockResolvedValue([sourceWorkflow]);
  service.getClosePreparationSource.mockResolvedValue(sourcePlan);
  service.getClosePreparationBooks.mockResolvedValue([targetBook]);
  service.getClosePreparationPeriods.mockResolvedValue([targetPeriod]);
  service.listClosePlanTemplates.mockResolvedValue([retainedTemplate]);
  service.captureClosePlanTemplate.mockResolvedValue(retainedTemplate);
  service.previewClosePreparation.mockResolvedValue(preparationPreview);
  service.createPreparedClosePlan.mockResolvedValue(preparedResult);
});

const recoveryKey = `meridian.close-preparation.pending.v1:${sourceWorkflow.workflowId}`;
const storedRecovery = () => JSON.parse(sessionStorage.getItem(recoveryKey) ?? "null") as { previewId: string; idempotencyKey: string } | null;
const pendingCreation = () => ({ version: 1, sourceWorkflowId: sourceWorkflow.workflowId, sourceLedgerBookId: targetBook.ledgerBookId,
  fundProfileId: retainedTemplate.fundProfileId, templateId: retainedTemplate.templateId, templateVersion: retainedTemplate.version,
  targetLedgerBookId: targetBook.ledgerBookId, targetPeriodId: targetPeriod.periodId, previewId: preparationPreview.previewId, idempotencyKey: "retained-request-key" });

async function openTemplate() {
  const user = userEvent.setup();
  const rendered = render(<MemoryRouter><PrepareNextPeriodPanel initialWorkflowId={sourceWorkflow.workflowId} /></MemoryRouter>);
  expect(service.listClosePreparationSources).not.toHaveBeenCalled();
  await user.click(screen.getByRole("button", { name: "Prepare next period" }));
  await user.selectOptions(await screen.findByLabelText("Retained template"), `${retainedTemplate.templateId}:${retainedTemplate.version}`);
  return { user, ...rendered };
}

async function selectTarget() {
  const opened = await openTemplate();
  await opened.user.selectOptions(screen.getByLabelText("Target book"), targetBook.ledgerBookId);
  await waitFor(() => expect(screen.getByLabelText("Target period")).toBeEnabled());
  await opened.user.selectOptions(screen.getByLabelText("Target period"), targetPeriod.periodId);
  await opened.user.type(screen.getByLabelText("Target owner for Alex"), "Sam");
  await opened.user.click(screen.getByLabelText("I have reviewed policy changes for this target book and period"));
  return opened;
}

it("captures explicit calendar rules and shows retained configuration history", async () => {
  service.listClosePlanTemplates.mockResolvedValue([]);
  const user = userEvent.setup();
  render(<MemoryRouter><PrepareNextPeriodPanel initialWorkflowId={sourceWorkflow.workflowId} /></MemoryRouter>);
  await user.click(screen.getByRole("button", { name: "Prepare next period" }));
  await user.type(await screen.findByLabelText("Template name"), "Monthly close");
  await user.type(screen.getByLabelText("Calendar name"), "Finance calendar");
  await user.type(screen.getByLabelText("Calendar version"), "2026.1");
  await user.type(screen.getByLabelText("Holiday dates"), "2026-11-02");
  expect(screen.getByRole("button", { name: "Capture template" })).toBeDisabled();
  await user.type(screen.getByLabelText("Reconcile cash day offset"), "2");
  await user.type(screen.getByLabelText("Review close day offset"), "3");
  await user.click(screen.getByRole("button", { name: "Capture template" }));
  await screen.findByLabelText("Target book");
  expect(service.captureClosePlanTemplate).toHaveBeenCalledWith({ sourceWorkflowId: sourceWorkflow.workflowId, name: "Monthly close", templateId: undefined,
    calendar: retainedTemplate.calendar, deadlineRules: retainedTemplate.tasks.map(task => task.deadlineRule) }, expect.any(AbortSignal));
  expect(screen.getByText("Template history (1)")).toBeInTheDocument();
  expect(screen.getByText("Captured configuration from September close.")).toBeInTheDocument();
});

it("renders the authoritative September-to-October dates, dependencies, changed owners and fresh requirements before creation", async () => {
  const { user, container } = await selectTarget();
  await user.click(screen.getByRole("button", { name: "Preview next period" }));
  const preview = await screen.findByRole("region", { name: "Next-period preview" });
  expect(within(preview).getByText("2026-11-04")).toBeInTheDocument();
  expect(within(preview).getByText("2026-11-05")).toBeInTheDocument();
  expect(within(preview).getByText("After: Reconcile cash")).toBeInTheDocument();
  expect(within(preview).getAllByText("Owner changed")).toHaveLength(2);
  expect(within(preview).getByText("Policy changed for the target")).toBeInTheDocument();
  expect(within(preview).getAllByText("Controller: 1 new approval(s)")).toHaveLength(2);
  expect(service.previewClosePreparation).toHaveBeenCalledWith({ templateId: retainedTemplate.templateId, templateVersion: 3,
    targetLedgerBookId: targetBook.ledgerBookId, targetPeriodId: targetPeriod.periodId, ownerMappings: [{ sourceOwner: "Alex", targetOwner: "Sam" }], acknowledgePolicyChange: true }, expect.any(AbortSignal));
  expect((await axe(container)).violations).toEqual([]);
  await user.click(screen.getByRole("button", { name: "Create next-period plan" }));
  await screen.findByText("Next-period plan created");
  expect(screen.getByText(/All required sign-offs and evidence reviews must be completed/)).toBeInTheDocument();
  expect(screen.getByText("Creation history (1)")).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Open prepared close plan" })).toHaveAttribute("href", expect.stringContaining(`ledgerBookId=${targetBook.ledgerBookId}`));
});

it("keeps unresolved mappings visible and blocks creation", async () => {
  service.previewClosePreparation.mockResolvedValue({ ...preparationPreview, canCreate: false,
    issues: [{ code: "OwnerUnresolved", message: "Assign an owner for Alex.", isBlocking: true, taskId: "reconcile" }] });
  const { user } = await selectTarget();
  await user.clear(screen.getByLabelText("Target owner for Alex"));
  await user.click(screen.getByRole("button", { name: "Preview next period" }));
  expect(await screen.findByText(/Assign an owner for Alex/)).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Create next-period plan" })).toBeDisabled();
  expect(service.createPreparedClosePlan).not.toHaveBeenCalled();
});

it("rejects stale previews and requires a new preview before another creation", async () => {
  service.createPreparedClosePlan.mockRejectedValueOnce(new ApiError({ path: "/prepare/create", status: 409, detail: "Target period changed after preview.", responseBody: JSON.stringify({ code: "PREPARATION_PREVIEW_STALE" }) }));
  const { user } = await selectTarget();
  await user.click(screen.getByRole("button", { name: "Preview next period" }));
  await user.click(await screen.findByRole("button", { name: "Create next-period plan" }));
  await screen.findByText(/Refresh the preview before creating the plan/);
  expect(storedRecovery()).toBeNull();
  expect(screen.queryByRole("button", { name: "Create next-period plan" })).not.toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: "Refresh preview" }));
  await user.click(await screen.findByRole("button", { name: "Create next-period plan" }));
  await screen.findByText("Next-period plan created");
  expect(service.previewClosePreparation).toHaveBeenCalledTimes(2);
  expect(service.createPreparedClosePlan.mock.calls[1][0].idempotencyKey).not.toEqual(service.createPreparedClosePlan.mock.calls[0][0].idempotencyKey);
});

it.each([new Error("Connection interrupted"), new ApiError({ path: "/prepare/create", status: 409, detail: "Interrupted creation; retry the same key." })])("retries an interrupted creation with the same preview and idempotency key: %s", async failure => {
  service.createPreparedClosePlan.mockRejectedValueOnce(failure).mockResolvedValueOnce({ ...preparedResult, wasAlreadyCreated: true });
  const { user } = await selectTarget();
  await user.click(screen.getByRole("button", { name: "Preview next period" }));
  await user.click(await screen.findByRole("button", { name: "Create next-period plan" }));
  await user.click(await screen.findByRole("button", { name: "Retry creation" }));
  await screen.findByText("Existing preparation recovered");
  expect(service.createPreparedClosePlan.mock.calls[1][0]).toEqual(service.createPreparedClosePlan.mock.calls[0][0]);
  expect(service.previewClosePreparation).toHaveBeenCalledTimes(1);
  expect(storedRecovery()).toBeNull();
});

it("persists before posting and recovers a lost response after reload with the original identity", async () => {
  service.createPreparedClosePlan.mockImplementationOnce(async command => {
    expect(storedRecovery()).toEqual(expect.objectContaining(command));
    throw new Error("Response lost after creation");
  }).mockResolvedValueOnce({ ...preparedResult, wasAlreadyCreated: true });
  const { user, unmount } = await selectTarget();
  await user.click(screen.getByRole("button", { name: "Preview next period" }));
  await user.click(await screen.findByRole("button", { name: "Create next-period plan" }));
  await screen.findByRole("button", { name: "Retry creation" });
  const saved = storedRecovery();
  expect(saved).not.toBeNull();
  expect(screen.getByRole("button", { name: "Refresh preview" })).toBeDisabled();
  expect(screen.getByLabelText("Retained template")).toBeDisabled();
  expect(screen.getByRole("button", { name: "Capture another version" })).toBeDisabled();
  unmount();

  service.listClosePreparationSources.mockResolvedValue([{ ...sourceWorkflow, workflowId: preparedResult.workflowId, periodId: targetPeriod.periodId }, sourceWorkflow]);
  render(<MemoryRouter><PrepareNextPeriodPanel initialWorkflowId={preparedResult.workflowId} /></MemoryRouter>);
  await user.click(screen.getByRole("button", { name: "Prepare next period" }));
  await user.click(await screen.findByRole("button", { name: "Recover pending creation" }));
  await screen.findByText("Existing preparation recovered");
  expect(screen.getByLabelText("Source plan")).toHaveValue(sourceWorkflow.workflowId);
  expect(service.createPreparedClosePlan.mock.calls[1][0]).toEqual(service.createPreparedClosePlan.mock.calls[0][0]);
  expect(service.previewClosePreparation).toHaveBeenCalledTimes(1);
  expect(storedRecovery()).toBeNull();
});

it.each([
  { sourceWorkflowId: "other-source" }, { sourceLedgerBookId: "other-book" }, { fundProfileId: "other-fund" },
  { targetLedgerBookId: "foreign-target" }, { templateVersion: 99 },
])("ignores stored recovery outside the freshly authorized source/template/book scope: %j", async mismatch => {
  sessionStorage.setItem(recoveryKey, JSON.stringify({ ...pendingCreation(), ...mismatch }));
  const { user } = await openTemplate();
  expect(screen.queryByRole("button", { name: "Recover pending creation" })).not.toBeInTheDocument();
  expect(screen.getByLabelText("Target book")).toBeEnabled();
  await user.selectOptions(screen.getByLabelText("Target book"), targetBook.ledgerBookId);
  expect(service.createPreparedClosePlan).not.toHaveBeenCalled();
});

it.each([
  new ApiError({ path: "/prepare/create", status: 409, detail: "The original preview expired before creation.", responseBody: JSON.stringify({ code: "PREPARATION_PREVIEW_STALE" }) }),
  new ApiError({ path: "/prepare/create", status: 404, detail: "The unclaimed preview was not found." }),
])("recovers without cached authority and clears terminal stale or missing preview rejection: %s", async failure => {
  sessionStorage.setItem(recoveryKey, JSON.stringify(pendingCreation()));
  service.createPreparedClosePlan.mockRejectedValueOnce(failure);
  const user = userEvent.setup();
  render(<MemoryRouter><PrepareNextPeriodPanel initialWorkflowId={sourceWorkflow.workflowId} /></MemoryRouter>);
  await user.click(screen.getByRole("button", { name: "Prepare next period" }));
  const recover = await screen.findByRole("button", { name: "Recover pending creation" });
  expect(screen.queryByRole("region", { name: "Next-period preview" })).not.toBeInTheDocument();
  expect(screen.getByLabelText("Target book")).toBeDisabled();
  await user.click(recover);
  await screen.findByText(/Refresh the preview before creating the plan/);
  expect(service.createPreparedClosePlan).toHaveBeenCalledWith({ previewId: preparationPreview.previewId, idempotencyKey: "retained-request-key" }, expect.any(AbortSignal));
  expect(storedRecovery()).toBeNull();
  expect(screen.queryByRole("button", { name: "Recover pending creation" })).not.toBeInTheDocument();
  expect(screen.getByLabelText("Target book")).toBeEnabled();
});

it("does not post a creation when recovery identity cannot be persisted", async () => {
  const { user } = await selectTarget();
  await user.click(screen.getByRole("button", { name: "Preview next period" }));
  const write = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("Storage disabled"); });
  try {
    await user.click(await screen.findByRole("button", { name: "Create next-period plan" }));
    expect(await screen.findByText(/Enable browser session storage before creating the plan/)).toBeInTheDocument();
    expect(service.createPreparedClosePlan).not.toHaveBeenCalled();
  } finally { write.mockRestore(); }
});

it("never replaces a different pending command retained for the same authorized source", async () => {
  const { user } = await selectTarget();
  await user.click(screen.getByRole("button", { name: "Preview next period" }));
  sessionStorage.setItem(recoveryKey, JSON.stringify(pendingCreation()));
  await user.click(await screen.findByRole("button", { name: "Create next-period plan" }));
  expect(await screen.findByText(/A pending creation already exists for this source plan/)).toBeInTheDocument();
  expect(service.createPreparedClosePlan).not.toHaveBeenCalled();
  expect(storedRecovery()?.idempotencyKey).toBe("retained-request-key");
});

it("discards a late preview after the operator changes an owner", async () => {
  let resolvePreview!: (value: typeof preparationPreview) => void;
  service.previewClosePreparation.mockReturnValue(new Promise(resolve => { resolvePreview = resolve; }));
  const { user } = await selectTarget();
  await user.click(screen.getByRole("button", { name: "Preview next period" }));
  await user.type(screen.getByLabelText("Target owner for Alex"), " updated");
  await act(async () => { resolvePreview(preparationPreview); });
  expect(screen.queryByRole("region", { name: "Next-period preview" })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "Create next-period plan" })).not.toBeInTheDocument();
});

it("invalidates a displayed preview when target configuration changes", async () => {
  const { user } = await selectTarget();
  await user.click(screen.getByRole("button", { name: "Preview next period" }));
  await screen.findByRole("button", { name: "Create next-period plan" });
  await user.click(screen.getByLabelText("I have reviewed policy changes for this target book and period"));
  expect(screen.queryByRole("region", { name: "Next-period preview" })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "Create next-period plan" })).not.toBeInTheDocument();
});

it("requires a refresh when the authoritative preview has expired", async () => {
  service.previewClosePreparation.mockResolvedValue({ ...preparationPreview, expiresAtUtc: "2020-01-01T00:00:00Z" });
  const { user } = await selectTarget();
  await user.click(screen.getByRole("button", { name: "Preview next period" }));
  expect(await screen.findByText("Preview expired")).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Create next-period plan" })).toBeDisabled();
  expect(service.createPreparedClosePlan).not.toHaveBeenCalled();
});

it("leaves a failed authoritative source read unavailable with a retry", async () => {
  service.listClosePreparationSources.mockRejectedValueOnce(new Error("Authoritative source unavailable"));
  const user = userEvent.setup();
  render(<MemoryRouter><PrepareNextPeriodPanel /></MemoryRouter>);
  await user.click(screen.getByRole("button", { name: "Prepare next period" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("Authoritative source unavailable");
  expect(screen.queryByLabelText("Source plan")).not.toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: "Retry source plans" }));
  await screen.findByLabelText("Source plan");
});

it("shows retained template version and creation history when a prepared plan is reloaded", async () => {
  service.listClosePreparationSources.mockResolvedValue([{ ...sourceWorkflow, workflowId: preparedResult.workflowId, periodId: targetPeriod.periodId }]);
  service.getClosePreparationSource.mockResolvedValue({ ...preparedResult.plan, configuration: {
    workflowId: preparedResult.workflowId, materialityPolicy: sourcePlan.materialityPolicy,
    preparation: { templateId: retainedTemplate.templateId, templateVersion: 3, sourceWorkflowId: sourceWorkflow.workflowId, targetPeriodId: targetPeriod.periodId,
      periodStart: targetPeriod.startDate, periodEnd: targetPeriod.endDate, createdAtUtc: preparedResult.createdAtUtc, createdBy: preparedResult.createdBy, history: preparedResult.history },
  } });
  service.listClosePlanTemplates.mockResolvedValue([]);
  const user = userEvent.setup();
  render(<MemoryRouter><PrepareNextPeriodPanel initialWorkflowId={preparedResult.workflowId} /></MemoryRouter>);
  await user.click(screen.getByRole("button", { name: "Prepare next period" }));
  const provenance = await screen.findByRole("region", { name: "Retained plan creation" });
  expect(within(provenance).getByText(`Template ${retainedTemplate.templateId} · v3`)).toBeInTheDocument();
  expect(within(provenance).getByText(/Created by Morgan.*2026-10-01 – 2026-10-31/)).toBeInTheDocument();
  await user.click(within(provenance).getByText("Retained creation history (1)"));
  expect(within(provenance).getByText("Prepared October with fresh tasks and required sign-offs.")).toBeVisible();
});
