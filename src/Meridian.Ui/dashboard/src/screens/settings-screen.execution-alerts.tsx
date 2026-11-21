import { useEffect, useRef, useState, type FormEvent } from "react";
import { getExecutionAlertSettings, saveExecutionAlertSettings, type ExecutionAlertSettings } from "@/lib/api/execution-workflows.api";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";

interface AlertSettingsState {
  accountKey: string;
  status: "idle" | "loading" | "ready" | "empty" | "error";
  settings: ExecutionAlertSettings | null;
  error: string | null;
  notice: string | null;
  saving: boolean;
}

function accountIdentity(accountId?: string | null) {
  return accountId?.trim().toLowerCase() ?? "";
}

function initialState(accountKey: string): AlertSettingsState {
  return { accountKey, status: accountKey ? "loading" : "idle", settings: null, error: null, notice: null, saving: false };
}

export function ExecutionAlertSettingsPanel({ accountId }: { accountId?: string | null }) {
  const accountKey = accountIdentity(accountId);
  const activeAccount = useRef(accountKey);
  activeAccount.current = accountKey;
  const requestGeneration = useRef(0);
  const saveSequence = useRef(0);
  const pendingSave = useRef<number | null>(null);
  const [state, setState] = useState<AlertSettingsState>(() => initialState(accountKey));

  useEffect(() => {
    const generation = ++requestGeneration.current;
    const controller = new AbortController();
    const isCurrent = () => !controller.signal.aborted
      && requestGeneration.current === generation && activeAccount.current === accountKey;
    pendingSave.current = null;
    setState(initialState(accountKey));

    if (accountKey) {
      void getExecutionAlertSettings(accountKey, { signal: controller.signal }).then((value) => {
        if (!isCurrent()) return;
        if (value == null) {
          setState({ ...initialState(accountKey), status: "empty" });
        } else if (accountIdentity(value.accountId) !== accountKey) {
          setState({ ...initialState(accountKey), status: "error", error: "Alert settings did not match the selected execution account." });
        } else {
          setState({ ...initialState(accountKey), status: "ready", settings: value });
        }
      }).catch((reason: unknown) => {
        if (isCurrent()) {
          setState({ ...initialState(accountKey), status: "error", error: reason instanceof Error ? reason.message : "Alert settings are unavailable." });
        }
      });
    }

    return () => {
      controller.abort();
      requestGeneration.current = generation + 1;
      pendingSave.current = null;
    };
  }, [accountKey]);

  const currentState = state.accountKey === accountKey ? state : null;
  const settings = currentState?.settings ?? null;
  const loading = Boolean(accountKey) && (!currentState || currentState.status === "loading");
  const saving = currentState?.saving ?? false;

  function updateSettings(change: Partial<ExecutionAlertSettings>) {
    setState((previous) => previous.accountKey === accountKey && previous.settings
      ? { ...previous, settings: { ...previous.settings, ...change }, notice: null }
      : previous);
  }

  async function saveSettings(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!settings || saving || pendingSave.current !== null) return;
    const generation = requestGeneration.current;
    const saveId = ++saveSequence.current;
    pendingSave.current = saveId;
    const isCurrent = () => requestGeneration.current === generation
      && activeAccount.current === accountKey && pendingSave.current === saveId;
    setState((previous) => ({ ...previous, saving: true, error: null, notice: null }));
    try {
      await saveExecutionAlertSettings(settings);
      if (isCurrent()) {
        setState((previous) => ({ ...previous, notice: "Alert settings saved. External delivery occurs only when enabled." }));
      }
    } catch (reason: unknown) {
      if (isCurrent()) {
        setState((previous) => ({ ...previous, error: reason instanceof Error ? reason.message : "Settings could not be saved." }));
      }
    } finally {
      if (isCurrent()) {
        pendingSave.current = null;
        setState((previous) => ({ ...previous, saving: false }));
      }
    }
  }

  return (
    <section aria-label="Execution alert settings" className="panel-surface space-y-3 rounded-lg border border-border p-4">
      <h3 className="font-semibold">Execution alerts</h3>
      <p className="text-xs text-muted-foreground">Account-scoped daily equity loss includes realized and unrealized changes, adjusted for external cash flows. Acknowledgement never clears execution controls.</p>
      {!accountKey ? <p>Select an execution account before configuring alerts.</p> : null}
      {loading ? <p role="status">Loading execution alert settings…</p> : null}
      {currentState?.status === "empty" ? <p role="status">No execution alert settings are available for this account.</p> : null}
      {currentState?.error ? <p role="alert">{currentState.error}</p> : null}
      {currentState?.notice ? <p role="status">{currentState.notice}</p> : null}
      {settings && accountIdentity(settings.accountId) === accountKey ? (
        <form aria-label="Execution alert settings" className="grid gap-3 text-sm md:grid-cols-2" onSubmit={(event) => void saveSettings(event)}>
          <label>Daily loss limit (USD; blank disables)
            <Input type="number" min="0.01" step="0.01" disabled={saving} value={settings.dailyLossLimitUsd ?? ""} onChange={(event) => updateSettings({ dailyLossLimitUsd: event.target.value ? Number(event.target.value) : null })} />
          </label>
          <label>Stale order threshold (eligible market minutes)
            <Input type="number" min="1" max="1440" disabled={saving} value={settings.staleOrderMinutes} onChange={(event) => updateSettings({ staleOrderMinutes: Number(event.target.value) })} />
          </label>
          <Checkbox className="min-h-6" label="Enable external webhook delivery" disabled={saving} checked={settings.webhookEnabled} onCheckedChange={(webhookEnabled) => updateSettings({ webhookEnabled })} />
          <label>Webhook HTTPS URL
            <Input type="url" disabled={saving} value={settings.webhookUrl ?? ""} onChange={(event) => updateSettings({ webhookUrl: event.target.value || null })} />
          </label>
          <Button type="submit" busy={saving} busyLabel="Saving execution alert settings…">Save execution alert settings</Button>
        </form>
      ) : null}
    </section>
  );
}
