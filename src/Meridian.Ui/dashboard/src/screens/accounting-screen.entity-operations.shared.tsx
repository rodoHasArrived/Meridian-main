import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { ApiError, describeApiError } from "@/lib/api-errors";
import { getEntityOperation, postEntityOperation } from "@/lib/api/entity-operations.api";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";

export const today = () => new Date().toISOString().slice(0, 10);
export const effectiveUtc = (date: string) => `${date}T00:00:00Z`;
export const newIdentity = () => crypto.randomUUID();
export const nullable = (value: string) => value.trim() || null;

export function OperationField({ label, value, onChange, type = "text", required = false, children, hint, pattern, disabled = false }: {
  label: string; value: string; onChange: (value: string) => void; type?: string; required?: boolean;
  children?: ReactNode; hint?: string; pattern?: string; disabled?: boolean;
}) {
  const id = `entity-operation-${label.toLowerCase().replace(/[^a-z0-9]+/g, "-")}`;
  return <FormRow label={label} labelFor={id} hint={hint}>
    {children ? <Select id={id} value={value} required={required} disabled={disabled} onChange={(event) => onChange(event.target.value)}>{children}</Select>
      : <Input id={id} type={type} value={value} required={required} pattern={pattern} disabled={disabled} onChange={(event) => onChange(event.target.value)} autoComplete="off" />}
  </FormRow>;
}

export function OperationError({ error }: { error: string | null }) {
  return error ? <p role="alert" className="whitespace-pre-line rounded border border-danger/35 bg-danger/10 p-3 text-sm text-danger">{error}</p> : null;
}

function operationFailure(failure: unknown, fallback: string) {
  const display = describeApiError(failure, fallback);
  return [...new Set([display.summary, ...display.details])].join("\n");
}

export function EntityOperationTable<T>({ label, rows, columns, getRowId, emptyText }: {
  label: string; rows: T[]; columns: DenseDataTableColumn<T>[]; getRowId: (row: T) => string; emptyText: string;
}) {
  return <div role="region" aria-label={label} className="min-w-0 overflow-x-auto" tabIndex={0}>
    <DenseDataTable ariaLabel={label} caption={label} rows={rows} columns={columns} getRowId={getRowId} emptyText={emptyText} maxVisibleRows={50} />
  </div>;
}

export function useEntityData<T>(path: string | null, emptyWhenMissing = false) {
  const [generation, setGeneration] = useState(0);
  const [state, setState] = useState<{ path: string | null; data: T | null; loading: boolean; error: string | null }>({ path: null, data: null, loading: false, error: null });
  useEffect(() => {
    if (!path) return;
    const controller = new AbortController();
    setState({ path, data: null, loading: true, error: null });
    void getEntityOperation<T>(path, { signal: controller.signal }).then(
      (data) => { if (!controller.signal.aborted) setState({ path, data, loading: false, error: null }); },
      (error) => { if (!controller.signal.aborted) setState({ path, data: null, loading: false,
        error: emptyWhenMissing && error instanceof ApiError && error.status === 404 ? null : operationFailure(error, "This evidence could not be loaded.") }); }
    );
    return () => controller.abort();
  }, [path, generation, emptyWhenMissing]);
  return { data: state.path === path ? state.data : null, loading: path !== null && (state.path !== path || state.loading),
    error: state.path === path ? state.error : null, refresh: useCallback(() => setGeneration((value) => value + 1), []) };
}

export function useEntityMutation() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState("");
  const active = useRef<AbortController | null>(null);
  const requestIdentity = useRef<{ command: string; id: string } | null>(null);
  useEffect(() => () => active.current?.abort(), []);
  function identity(command: unknown) {
    const serialized = JSON.stringify(command);
    if (requestIdentity.current?.command !== serialized) requestIdentity.current = { command: serialized, id: newIdentity() };
    return requestIdentity.current.id;
  }
  async function run<T>(path: string, body: unknown, success: string): Promise<T | null> {
    if (active.current) return null;
    const controller = new AbortController();
    active.current = controller;
    setBusy(true); setError(null); setMessage("");
    try {
      const result = await postEntityOperation<T>(path, body, { signal: controller.signal });
      if (controller.signal.aborted) return null;
      setMessage(success);
      return result;
    } catch (failure) {
      if (!controller.signal.aborted) setError(operationFailure(failure, "The change was not confirmed. Refresh the retained record before retrying."));
      return null;
    } finally {
      if (!controller.signal.aborted) { active.current = null; setBusy(false); }
    }
  }
  return { busy, error, message, run, identity };
}

export function MutationFeedback({ state }: { state: ReturnType<typeof useEntityMutation> }) {
  return <><OperationError error={state.error} /><p role="status" aria-live="polite" className="text-sm text-muted-foreground">{state.busy ? "Retaining the reviewed change…" : state.message}</p></>;
}
