import {
  CheckCircle2,
  ChevronDown,
  FlaskConical,
  Loader2,
  Pencil,
  Plus,
  RotateCcw,
  Trash2,
  XCircle,
} from "lucide-react";
import { useEffect, useLayoutEffect, useRef, useState, type RefObject } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import { Toggle } from "@/components/ui/checkbox";
import { ProviderCapabilityBadges } from "@/components/data/provider-capability-badges";
import { AddProviderDrawer } from "@/components/data/add-provider-drawer";
import { EditProviderDrawer } from "@/components/data/edit-provider-drawer";
import { EmptyState as ConcreteEmptyState, SkeletonTable } from "@/components/data/concrete";
import {
  OptimisticCell,
  OptimisticProvider,
  OptimisticRevertError,
  OptimisticRowBar,
  OptimisticUnlandedError,
  useOptimisticLedger,
  type OptimisticEdit,
} from "@/components/data/optimistic-cell";
import {
  PROVIDER_MODULE_PRIORITY_RULE,
  parseProviderModulePriority,
  validateProviderModulePriority,
} from "@/lib/provider-setup/provider-module-priority";
import {
  useProviderSetup,
  type ProviderModuleEditOutcome,
  type UseProviderSetupReturn,
} from "@/lib/provider-setup/use-provider-setup";
import type { ProviderModuleStatus } from "@/types/provider-setup";
import { cn } from "@/lib/utils";

const PRIORITY_FIELD = "priority";

// The provider module API takes no reason and returns no audit record for this write, so the
// revert reason cannot leave the browser, and the ledger holding it lasts only while this panel
// is mounted. Said wherever a reason is asked for or shown.
const PRIORITY_REASON_DISCLOSURE =
  "The provider API has no reason or audit field for this write. Your reason is held in this browser session only: " +
  "it is not sent to the server and is gone once you leave or reload this view.";

const PRIORITY_HINT =
  "Whole number from 1 to 999. Lower numbers are preferred when providers offer the same capability. " +
  "Enter saves; Escape cancels.";

/** A write to a module that changes or removes it, during which its priority must not be written. */
type ModuleWrite = "toggle" | "remove";

const MODULE_WRITE_BLOCK: Record<ModuleWrite, string> = {
  toggle: "This provider is being enabled or disabled. Wait for that to finish.",
  remove: "This provider is being removed. Wait for that to finish.",
};

/** Why a priority write did not land, as the ledger should record it. */
function priorityFailure(outcome: ProviderModuleEditOutcome, stored: number): Error {
  if (outcome.refused) {
    // The server answered and refused: a rejection, in the server's own words when it gave any.
    return new Error(outcome.error ?? "The server refused this priority and gave no reason.");
  }
  const detail = outcome.error ? ` (${outcome.error})` : "";
  // No answer is not a refusal. The hook read the modules again; say what that read showed.
  if (outcome.reread === "not-saved") {
    return new OptimisticUnlandedError(
      `No answer came back from the server. The stored priority was read again and is still ${stored}, ` +
      `so this change has not been saved.${detail}`
    );
  }
  return new OptimisticUnlandedError(
    "No confirmation came back from the server, so it is not known whether this priority was saved. " +
    `Use Refresh to read the stored value before trying again.${detail}`
  );
}

function failureMessage(error: unknown): string {
  return error instanceof Error && error.message ? error.message : String(error);
}

/**
 * Provider module setup. Priority is edited in place through the optimistic edit ledger: the
 * write is the same `PUT /api/providers/modules/{moduleId}` the edit drawer uses, and the drawer
 * stays the full-form path for credentials and the rest.
 */
export function ProviderSetupPanel() {
  const setup = useProviderSetup();
  const { modules, editModuleWithOutcome } = setup;
  // The ledger calls these handlers after awaits; read the modules of the latest render.
  const latestModules = useRef(modules);
  useLayoutEffect(() => {
    latestModules.current = modules;
  });
  // Modules with an enable, disable or remove in flight. The body records one before it starts
  // and clears it once the render that carries the outcome has committed.
  const moduleWrites = useRef<ReadonlyMap<string, ModuleWrite>>(new Map());

  const findModule = (rowId: string | number) => {
    const module = latestModules.current.find(candidate => candidate.moduleId === String(rowId));
    if (!module) {
      // The update endpoint creates a module that is missing, so never write to a removed one.
      throw new Error("This provider module is no longer configured, so its priority was not written.");
    }
    return module;
  };

  const writePriority = async (rowId: string | number, priority: number) => {
    const module = findModule(rowId);
    // A priority write resends `enabled` and creates a module that is missing. While the module
    // is being enabled, disabled or removed, the flag on screen is about to be out of date and
    // the module may be about to go, so nothing is sent.
    const inFlight = moduleWrites.current.get(module.moduleId);
    if (inFlight) {
      throw new Error(`${MODULE_WRITE_BLOCK[inFlight]} The priority was not written.`);
    }
    // The server request defaults `enabled` to true: always resend the module's current flag, or
    // a priority-only edit would silently re-enable a disabled provider.
    const outcome = await editModuleWithOutcome(module.moduleId, {
      moduleId: module.moduleId,
      enabled: module.enabled,
      priority,
    });
    if (!outcome.success) {
      throw priorityFailure(outcome, module.priority);
    }
  };

  const commitPriority = async (edit: OptimisticEdit) => {
    const priority = parseProviderModulePriority(edit.value);
    if (priority === null) {
      throw new Error(PROVIDER_MODULE_PRIORITY_RULE);
    }
    await writePriority(edit.rowId, priority);
  };

  // A revert is a second write of the prior value, newest edit first. The reason stays in the
  // ledger: the request has nowhere to carry it. Writes that land stay landed: a failure part of
  // the way through reports which reverts were written instead of putting them all back.
  const revertPriority = async (edits: OptimisticEdit[]) => {
    const landed: string[] = [];
    // What each module holds as far as this batch knows: the last read, then its own writes.
    const held = new Map<string, number>();
    for (const [index, edit] of edits.entries()) {
      try {
        const prior = Number(edit.prior);
        if (!Number.isInteger(prior)) {
          throw new Error("The prior priority is not a whole number, so it was not written back.");
        }
        const module = findModule(edit.rowId);
        const current = held.get(module.moduleId) ?? module.priority;
        if (String(current) !== String(edit.value)) {
          // Undoing an edit means putting back what it replaced. If the module no longer holds
          // the edit's value, the prior would overwrite something newer.
          throw new Error(
            `The last read of this provider shows priority ${current}, not ${String(edit.value)}, ` +
            "so writing the prior back would overwrite a newer value. Nothing was written. Use Refresh, then revert from what is shown."
          );
        }
        await writePriority(edit.rowId, prior);
        held.set(module.moduleId, prior);
        landed.push(edit.id);
      } catch (err) {
        const reason = failureMessage(err);
        const reasons: Record<string, string> = { [edit.id]: reason };
        for (const skipped of edits.slice(index + 1)) {
          reasons[skipped.id] = `Not attempted: an earlier revert in this batch did not land. ${reason}`;
        }
        throw new OptimisticRevertError(reason, {
          landed,
          unlanded: err instanceof OptimisticUnlandedError ? [edit.id] : [],
          reasons,
          cause: err,
        });
      }
    }
  };

  return (
    <OptimisticProvider
      commit={commitPriority}
      revert={revertPriority}
      reasonDisclosure={PRIORITY_REASON_DISCLOSURE}
    >
      <ProviderSetupPanelBody setup={setup} moduleWrites={moduleWrites} />
    </OptimisticProvider>
  );
}

function ProviderSetupPanelBody({
  setup,
  moduleWrites,
}: {
  setup: UseProviderSetupReturn;
  moduleWrites: RefObject<ReadonlyMap<string, ModuleWrite>>;
}) {
  const {
    modules,
    catalogue,
    loading,
    error,
    pendingRestart,
    refresh,
    addModule,
    editModule,
    removeModule,
    toggleEnabled,
    testModule,
    triggerRestart,
  } = setup;

  const ledger = useOptimisticLedger();
  const [addOpen, setAddOpen] = useState(false);
  const [editTargetId, setEditTargetId] = useState<string | null>(null);
  // The drawer edits the module as it is now, not as it was when the card last rendered.
  const editTarget = editTargetId ? modules.find(mod => mod.moduleId === editTargetId) ?? null : null;
  if (editTargetId && !editTarget) {
    // The module went away while its drawer was open; do not reopen it if the id comes back.
    setEditTargetId(null);
  }
  const [restartBusy, setRestartBusy] = useState(false);
  const [testingId, setTestingId] = useState<string | null>(null);
  // Sets, not one id each: two providers can be mid-write at once, and the first to finish must
  // not make the other look settled.
  const [togglingIds, setTogglingIds] = useState<ReadonlySet<string>>(new Set());
  const [removingIds, setRemovingIds] = useState<ReadonlySet<string>>(new Set());
  const [testResults, setTestResults] = useState<Record<string, boolean | null>>({});

  const handleRestart = async () => {
    setRestartBusy(true);
    try {
      await triggerRestart();
    } finally {
      setRestartBusy(false);
    }
  };

  // The priority write path reads `moduleWrites` between renders, so a write is recorded before
  // its request starts. It is cleared here, in the commit that also carries the outcome (the new
  // enabled flag, or the list without the module), so the write path never sees a stale module
  // as free to write.
  useLayoutEffect(() => {
    const inFlight = new Map<string, ModuleWrite>();
    for (const moduleId of togglingIds) inFlight.set(moduleId, "toggle");
    for (const moduleId of removingIds) inFlight.set(moduleId, "remove");
    moduleWrites.current = inFlight;
  }, [moduleWrites, togglingIds, removingIds]);
  const beginModuleWrite = (moduleId: string, write: ModuleWrite) => {
    moduleWrites.current = new Map(moduleWrites.current).set(moduleId, write);
    (write === "toggle" ? setTogglingIds : setRemovingIds)(current => new Set(current).add(moduleId));
  };
  const endModuleWrite = (moduleId: string, write: ModuleWrite) => {
    (write === "toggle" ? setTogglingIds : setRemovingIds)(current => {
      const next = new Set(current);
      next.delete(moduleId);
      return next;
    });
  };

  // A ledger entry belongs to the module it was written to. Once that module is gone its entries
  // leave too, so a module added again under the same id does not inherit an old priority edit.
  const ledgerEntries = ledger.entries;
  useEffect(() => {
    const configured = new Set(modules.map(mod => mod.moduleId));
    for (const entry of ledgerEntries) {
      const settled = entry.status !== "pending" && entry.status !== "reverting" && entry.status !== "dismissed";
      if (settled && !configured.has(String(entry.rowId))) {
        ledger.dismiss(entry.id);
      }
    }
  }, [ledger, ledgerEntries, modules]);

  // One batch revert can span every provider, so it waits for any of them.
  const moduleWriteBlock = togglingIds.size > 0
    ? "A provider is being enabled or disabled. Wait for that to finish."
    : removingIds.size > 0
      ? "A provider is being removed. Wait for that to finish."
      : null;

  const handleToggle = async (moduleId: string, enabled: boolean) => {
    beginModuleWrite(moduleId, "toggle");
    try {
      await toggleEnabled(moduleId, enabled);
    } finally {
      endModuleWrite(moduleId, "toggle");
    }
  };

  const handleTest = async (moduleId: string) => {
    setTestingId(moduleId);
    try {
      const result = await testModule(moduleId);
      setTestResults(prev => ({ ...prev, [moduleId]: result?.success ?? null }));
    } finally {
      setTestingId(null);
    }
  };

  const handleRemove = async (moduleId: string) => {
    beginModuleWrite(moduleId, "remove");
    try {
      await removeModule(moduleId);
    } finally {
      endModuleWrite(moduleId, "remove");
    }
  };

  return (
    <div className="space-y-4">
      {pendingRestart && (
        <StatusBanner
          tone="warning"
          title="Restart required"
          detail="Provider configuration has changed. Restart the provider host to apply changes."
        >
          <div className="mt-2">
            <Button
              size="sm"
              variant="outline"
              busy={restartBusy}
              busyLabel="Restarting…"
              onClick={() => void handleRestart()}
            >
              <RotateCcw className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
              Restart provider host
            </Button>
          </div>
        </StatusBanner>
      )}

      {error && (
        <StatusBanner
          tone="danger"
          title="Failed to load provider modules"
          detail={error}
        />
      )}

      <Card>
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <div className="eyebrow-label">Data ingestion</div>
              <CardTitle className="mt-2">Provider Modules</CardTitle>
              <CardDescription className="mt-1">
                Configure and manage market data provider adapters. Each provider can supply streaming quotes,
                historical backfill, symbol search, or brokerage execution.
              </CardDescription>
            </div>
            <div className="flex shrink-0 items-center gap-2">
              <Button
                size="sm"
                variant="outline"
                onClick={() => void refresh()}
                disabled={loading}
              >
                {loading ? (
                  <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                ) : (
                  <ChevronDown className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                )}
                Refresh
              </Button>
              <Button size="sm" onClick={() => setAddOpen(true)}>
                <Plus className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                Add provider
              </Button>
            </div>
          </div>
        </CardHeader>

        <CardContent>
          {loading && modules.length === 0 ? (
            <div role="status" aria-label="Loading provider modules" className="space-y-3 py-2">
              <p className="text-sm text-muted-foreground">Loading provider modules…</p>
              <SkeletonTable rows={3} columns={3} />
            </div>
          ) : modules.length === 0 ? (
            <ConcreteEmptyState
              icon="inbox"
              title="No provider modules configured"
              detail="Add a provider to start receiving market data."
              action="Add provider"
              onAction={() => setAddOpen(true)}
              compact
            />
          ) : (
            <div className="grid gap-3">
              <OptimisticRowBar
                rowIds={modules.map(mod => mod.moduleId)}
                label="priority edits"
                notePlacement="inline"
                revertBlockedReason={moduleWriteBlock}
              />
              {modules.map(mod => (
                <ProviderModuleCard
                  key={mod.moduleId}
                  module={mod}
                  pendingRestart={pendingRestart}
                  testResult={testResults[mod.moduleId] ?? undefined}
                  testingNow={testingId === mod.moduleId}
                  togglingNow={togglingIds.has(mod.moduleId)}
                  removingNow={removingIds.has(mod.moduleId)}
                  onToggle={(enabled) => void handleToggle(mod.moduleId, enabled)}
                  onTest={() => void handleTest(mod.moduleId)}
                  onEdit={() => setEditTargetId(mod.moduleId)}
                  onRemove={() => void handleRemove(mod.moduleId)}
                />
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      <AddProviderDrawer
        open={addOpen}
        catalogue={catalogue}
        onClose={() => setAddOpen(false)}
        onAdd={addModule}
      />

      {editTarget && (
        <EditProviderDrawer
          open={true}
          module={editTarget}
          onClose={() => setEditTargetId(null)}
          onEdit={(req) => editModule(editTarget.moduleId, req)}
        />
      )}
    </div>
  );
}

interface ModuleCardProps {
  module: ProviderModuleStatus;
  pendingRestart: boolean;
  testResult?: boolean;
  testingNow: boolean;
  togglingNow: boolean;
  removingNow: boolean;
  onToggle: (enabled: boolean) => void;
  onTest: () => void;
  onEdit: () => void;
  onRemove: () => void;
}

function ProviderModuleCard({
  module,
  pendingRestart,
  testResult,
  testingNow,
  togglingNow,
  removingNow,
  onToggle,
  onTest,
  onEdit,
  onRemove,
}: ModuleCardProps) {
  const configured = module.isConfigured;
  const running = module.isRunning;
  const ledger = useOptimisticLedger();
  // A priority write resends `enabled` and would recreate a removed module, and the edit drawer
  // would open on the priority from before the write. So the enable toggle, Edit and Remove wait
  // for it to settle. Every entry counts, not only the newest: an older edit can be reverting
  // beneath a newer note.
  const priorityInFlight = ledger
    .forRow(module.moduleId)
    .some(entry => entry.status === "pending" || entry.status === "reverting");
  const priorityWaitReason = priorityInFlight ? "Wait for the priority change to settle." : null;
  const moduleWriteBlock = togglingNow ? MODULE_WRITE_BLOCK.toggle : removingNow ? MODULE_WRITE_BLOCK.remove : null;

  return (
    <article className="rounded-md border border-border/70 bg-background/35 p-3">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0 flex-1 space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-sm font-semibold text-foreground">{module.displayName}</span>
            <span className="font-mono text-xs text-muted-foreground">{module.moduleId}</span>
            {running ? (
              <Badge variant="success" dot>Running</Badge>
            ) : configured ? (
              <Badge variant="warning">Stopped</Badge>
            ) : (
              <Badge variant="outline">Not configured</Badge>
            )}
            {testResult === true && (
              <span className="inline-flex items-center gap-1 text-xs text-success">
                <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                Connected
              </span>
            )}
            {testResult === false && (
              <span className="inline-flex items-center gap-1 text-xs text-danger">
                <XCircle className="h-3.5 w-3.5" aria-hidden="true" />
                Failed
              </span>
            )}
          </div>

          <ProviderCapabilityBadges capabilities={module.capabilityNames} />

          <CredentialStatusRow credentials={module.credentialStatus} />
        </div>

        <div className="flex shrink-0 flex-wrap items-center gap-2">
          <Toggle
            checked={module.enabled}
            disabled={togglingNow || priorityInFlight}
            onCheckedChange={onToggle}
            aria-label={module.enabled ? "Disable provider" : "Enable provider"}
          />
          <Button
            size="sm"
            variant="ghost"
            busy={testingNow}
            busyLabel="Testing…"
            onClick={onTest}
            aria-label="Test connection"
            title="Test connection"
          >
            <FlaskConical className="h-3.5 w-3.5" aria-hidden="true" />
          </Button>
          <Button
            size="sm"
            variant="ghost"
            disabled={priorityInFlight}
            disabledReason={priorityWaitReason}
            onClick={onEdit}
            aria-label="Edit provider"
            title="Edit provider"
          >
            <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
          </Button>
          <Button
            size="sm"
            variant="ghost"
            busy={removingNow}
            busyLabel="Removing…"
            disabled={priorityInFlight}
            disabledReason={priorityWaitReason}
            onClick={onRemove}
            aria-label="Remove provider"
            title="Remove provider"
            className="text-danger hover:text-danger"
          >
            <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
          </Button>
        </div>
      </div>

      <div className="mt-2 flex flex-wrap items-start gap-x-2 gap-y-1">
        <span className="pt-1 text-xs text-muted-foreground">Priority</span>
        <div className="min-w-0 flex-1">
          <OptimisticCell
            rowId={module.moduleId}
            field={PRIORITY_FIELD}
            label={`${module.displayName} priority`}
            value={module.priority}
            mode="number"
            validator={validateProviderModulePriority}
            hint={PRIORITY_HINT}
            disabled={togglingNow || removingNow}
            revertBlockedReason={moduleWriteBlock}
            notePlacement="inline"
            // Confirmed means the configuration write landed, not that routing changed.
            confirmedNote={
              pendingRestart
                ? "Saved to configuration. Not live until the provider host restarts."
                : "Saved to configuration."
            }
          />
        </div>
      </div>
    </article>
  );
}

function CredentialStatusRow({ credentials }: { credentials: ProviderModuleStatus["credentialStatus"] }) {
  if (credentials.length === 0) return null;
  return (
    <div className="flex flex-wrap items-center gap-2">
      {credentials.map(c => (
        <span key={c.key} className="inline-flex items-center gap-1 text-xs">
          <span
            className={cn(
              "inline-block h-2 w-2 rounded-full",
              c.hasValue ? "bg-success" : "bg-border"
            )}
            aria-hidden="true"
          />
          <span className={c.hasValue ? "text-foreground" : "text-muted-foreground"}>
            {c.key}
          </span>
        </span>
      ))}
    </div>
  );
}
