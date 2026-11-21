import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { getExecutionAlertSettings, saveExecutionAlertSettings, type ExecutionAlertSettings } from "@/lib/api/execution-workflows.api";
import { ExecutionAlertSettingsPanel } from "./settings-screen.execution-alerts";

vi.mock("@/lib/api/execution-workflows.api", () => ({
  getExecutionAlertSettings: vi.fn(),
  saveExecutionAlertSettings: vi.fn()
}));

const accountA = "8c6b0029-9422-4961-ad61-a0ab6cbb242d";
const accountB = "53bf0251-17f6-4fb7-8dbe-6fb4966e2749";

function settings(accountId = accountA, dailyLossLimitUsd = 100): ExecutionAlertSettings {
  return { accountId, dailyLossLimitUsd, staleOrderMinutes: 15, webhookEnabled: false, webhookUrl: null, version: 12 };
}

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((complete, fail) => { resolve = complete; reject = fail; });
  return { promise, resolve, reject };
}

async function loadedForm() {
  return screen.findByRole("form", { name: "Execution alert settings" });
}

describe("ExecutionAlertSettingsPanel", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(getExecutionAlertSettings).mockImplementation(async (accountId) => settings(accountId));
    vi.mocked(saveExecutionAlertSettings).mockResolvedValue(undefined);
  });

  afterEach(cleanup);

  it("distinguishes no account, a pending read and an empty response", async () => {
    const read = deferred<ExecutionAlertSettings>();
    vi.mocked(getExecutionAlertSettings).mockReturnValueOnce(read.promise);
    const { rerender } = render(<ExecutionAlertSettingsPanel />);
    expect(screen.getByText("Select an execution account before configuring alerts.")).toBeVisible();
    expect(getExecutionAlertSettings).not.toHaveBeenCalled();

    rerender(<ExecutionAlertSettingsPanel accountId={accountA} />);
    expect(screen.getByRole("status")).toHaveTextContent("Loading execution alert settings");
    expect(screen.queryByRole("form")).not.toBeInTheDocument();

    await act(async () => read.resolve(null as unknown as ExecutionAlertSettings));
    expect(screen.getByRole("status")).toHaveTextContent("No execution alert settings are available for this account.");
    expect(screen.queryByRole("form")).not.toBeInTheDocument();
  });

  it("normalizes account identity and saves the reviewed operands and server version with accessible feedback", async () => {
    const user = userEvent.setup();
    const { rerender } = render(<ExecutionAlertSettingsPanel accountId={` ${accountA.toUpperCase()} `} />);
    const form = await loadedForm();
    expect(getExecutionAlertSettings).toHaveBeenCalledWith(accountA, { signal: expect.any(AbortSignal) });
    rerender(<ExecutionAlertSettingsPanel accountId={accountA} />);
    expect(getExecutionAlertSettings).toHaveBeenCalledTimes(1);

    fireEvent.change(screen.getByLabelText("Daily loss limit (USD; blank disables)"), { target: { value: "99.5" } });
    fireEvent.change(screen.getByLabelText("Stale order threshold (eligible market minutes)"), { target: { value: "30" } });
    const webhook = screen.getByRole("checkbox", { name: "Enable external webhook delivery" });
    webhook.focus();
    await user.keyboard("[Space]");
    expect(webhook).toBeChecked();
    fireEvent.change(screen.getByLabelText("Webhook HTTPS URL"), { target: { value: "https://alerts.example.test/meridian" } });
    fireEvent.submit(form);

    expect(saveExecutionAlertSettings).toHaveBeenCalledWith({ ...settings(), dailyLossLimitUsd: 99.5,
      staleOrderMinutes: 30, webhookEnabled: true, webhookUrl: "https://alerts.example.test/meridian" });
    expect(await screen.findByRole("status")).toHaveTextContent("Alert settings saved. External delivery occurs only when enabled.");
    expect(screen.getByRole("button", { name: "Save execution alert settings" })).toBeEnabled();
  });

  it("retains nullable disabled limits and reports a rejected save for an uppercase selected account", async () => {
    vi.mocked(saveExecutionAlertSettings).mockRejectedValueOnce(new Error("External delivery requires reviewed HTTPS settings."));
    render(<ExecutionAlertSettingsPanel accountId={accountA.toUpperCase()} />);
    const form = await loadedForm();
    fireEvent.change(screen.getByLabelText("Daily loss limit (USD; blank disables)"), { target: { value: "" } });
    fireEvent.submit(form);

    expect(await screen.findByRole("alert")).toHaveTextContent("External delivery requires reviewed HTTPS settings.");
    expect(saveExecutionAlertSettings).toHaveBeenCalledWith({ ...settings(), dailyLossLimitUsd: null });
    expect(screen.getByLabelText("Daily loss limit (USD; blank disables)")).toHaveValue(null);
    expect(screen.getByRole("button", { name: "Save execution alert settings" })).toBeEnabled();
  });

  it("shows failed reads and refuses settings returned for another account", async () => {
    vi.mocked(getExecutionAlertSettings)
      .mockRejectedValueOnce(new Error("Account alert access was denied."))
      .mockResolvedValueOnce(settings(accountA));
    const { rerender } = render(<ExecutionAlertSettingsPanel accountId={accountA} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Account alert access was denied.");
    expect(screen.queryByRole("form")).not.toBeInTheDocument();

    rerender(<ExecutionAlertSettingsPanel accountId={accountB} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Alert settings did not match the selected execution account.");
    expect(screen.queryByRole("form")).not.toBeInTheDocument();
    expect(saveExecutionAlertSettings).not.toHaveBeenCalled();
  });

  it("aborts a previous account read and ignores its late success", async () => {
    const readA = deferred<ExecutionAlertSettings>();
    const readB = deferred<ExecutionAlertSettings>();
    vi.mocked(getExecutionAlertSettings).mockReturnValueOnce(readA.promise).mockReturnValueOnce(readB.promise);
    const { rerender } = render(<ExecutionAlertSettingsPanel accountId={accountA} />);
    const signalA = vi.mocked(getExecutionAlertSettings).mock.calls[0][1]?.signal;
    rerender(<ExecutionAlertSettingsPanel accountId={accountB} />);
    expect(signalA?.aborted).toBe(true);
    await act(async () => readB.resolve(settings(accountB, 250)));
    await loadedForm();
    await act(async () => readA.resolve(settings(accountA, 999)));

    expect(screen.getByLabelText("Daily loss limit (USD; blank disables)")).toHaveValue(250);
    fireEvent.submit(screen.getByRole("form"));
    await waitFor(() => expect(saveExecutionAlertSettings).toHaveBeenCalledWith(settings(accountB, 250)));
  });

  it("keeps the new account save busy when an older account save finishes and blocks duplicate commands", async () => {
    const saveA = deferred<void>();
    const saveB = deferred<void>();
    vi.mocked(saveExecutionAlertSettings).mockReturnValueOnce(saveA.promise).mockReturnValueOnce(saveB.promise);
    const { rerender } = render(<ExecutionAlertSettingsPanel accountId={accountA} />);
    fireEvent.submit(await loadedForm());
    rerender(<ExecutionAlertSettingsPanel accountId={accountB} />);
    const formB = await loadedForm();
    fireEvent.submit(formB);
    expect(saveExecutionAlertSettings).toHaveBeenNthCalledWith(2, settings(accountB));

    await act(async () => saveA.resolve());
    expect(screen.queryByText(/Alert settings saved/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Saving execution alert settings…" })).toBeDisabled();
    expect(screen.getByRole("checkbox")).toBeDisabled();
    expect(screen.getByLabelText("Daily loss limit (USD; blank disables)")).toBeDisabled();
    expect(screen.getByLabelText("Stale order threshold (eligible market minutes)")).toBeDisabled();
    expect(screen.getByLabelText("Webhook HTTPS URL")).toBeDisabled();
    fireEvent.submit(formB);
    expect(saveExecutionAlertSettings).toHaveBeenCalledTimes(2);

    await act(async () => saveB.reject(new Error("Current account settings could not be retained.")));
    expect(screen.getByRole("alert")).toHaveTextContent("Current account settings could not be retained.");
    expect(screen.getByRole("button", { name: "Save execution alert settings" })).toBeEnabled();
  });

  it("does not apply an old save error after leaving and returning to the same account", async () => {
    const oldSave = deferred<void>();
    vi.mocked(saveExecutionAlertSettings).mockReturnValueOnce(oldSave.promise);
    vi.mocked(getExecutionAlertSettings)
      .mockResolvedValueOnce(settings(accountA, 100))
      .mockResolvedValueOnce(settings(accountB, 200))
      .mockResolvedValueOnce(settings(accountA, 300));
    const { rerender } = render(<ExecutionAlertSettingsPanel accountId={accountA} />);
    fireEvent.submit(await loadedForm());
    rerender(<ExecutionAlertSettingsPanel accountId={accountB} />);
    await loadedForm();
    rerender(<ExecutionAlertSettingsPanel accountId={accountA} />);
    const currentForm = await loadedForm();
    await act(async () => oldSave.reject(new Error("Old account visit failed.")));

    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Daily loss limit (USD; blank disables)")).toHaveValue(300);
    fireEvent.submit(currentForm);
    expect(saveExecutionAlertSettings).toHaveBeenLastCalledWith(settings(accountA, 300));
    expect(await screen.findByRole("status")).toHaveTextContent("Alert settings saved");
  });

  it("aborts the outstanding read on unmount and handles its later rejection", async () => {
    const read = deferred<ExecutionAlertSettings>();
    vi.mocked(getExecutionAlertSettings).mockReturnValueOnce(read.promise);
    const { unmount } = render(<ExecutionAlertSettingsPanel accountId={accountA} />);
    const signal = vi.mocked(getExecutionAlertSettings).mock.calls[0][1]?.signal;
    expect(signal?.aborted).toBe(false);
    unmount();
    expect(signal?.aborted).toBe(true);
    await act(async () => read.reject(new DOMException("Navigation cancelled", "AbortError")));
    expect(saveExecutionAlertSettings).not.toHaveBeenCalled();
  });
});
