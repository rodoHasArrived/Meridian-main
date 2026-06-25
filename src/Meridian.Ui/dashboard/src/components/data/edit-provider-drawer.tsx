import { useState, type FormEvent } from "react";
import { Eye, EyeOff, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { Toggle } from "@/components/ui/checkbox";
import { ProviderCapabilityBadges } from "@/components/data/provider-capability-badges";
import {
  Sheet,
  SheetBody,
  SheetCloseButton,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import type { ProviderModuleStatus, UpsertProviderModuleRequest } from "@/types/provider-setup";

interface Props {
  open: boolean;
  module: ProviderModuleStatus;
  onClose: () => void;
  onEdit: (request: UpsertProviderModuleRequest) => Promise<boolean>;
}

export function EditProviderDrawer({ open, module, onClose, onEdit }: Props) {
  const [enabled, setEnabled] = useState(module.enabled);
  const [priority, setPriority] = useState(module.priority);
  const [replacing, setReplacing] = useState<Record<string, boolean>>({});
  const [newValues, setNewValues] = useState<Record<string, string>>({});
  const [showValues, setShowValues] = useState<Record<string, boolean>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleClose = () => {
    setEnabled(module.enabled);
    setPriority(module.priority);
    setReplacing({});
    setNewValues({});
    setShowValues({});
    setError(null);
    onClose();
  };

  const toggleReplace = (key: string) => {
    setReplacing(prev => ({ ...prev, [key]: !prev[key] }));
    setNewValues(prev => ({ ...prev, [key]: "" }));
  };

  const toggleShow = (key: string) => {
    setShowValues(prev => ({ ...prev, [key]: !prev[key] }));
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const credentialValues: Record<string, string | null> = {};
      for (const cred of module.credentialStatus) {
        if (replacing[cred.key]) {
          credentialValues[cred.key] = newValues[cred.key]?.trim() || null;
        }
      }

      const success = await onEdit({
        moduleId: module.moduleId,
        enabled,
        priority,
        credentialValues: Object.keys(credentialValues).length > 0 ? credentialValues : undefined,
      });
      if (success) {
        handleClose();
      } else {
        setError("Failed to update provider. Check the values and try again.");
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unexpected error");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Sheet open={open} onOpenChange={(v) => { if (!v) handleClose(); }}>
      <SheetContent>
        <SheetHeader>
          <SheetTitle>Edit {module.displayName}</SheetTitle>
          <SheetDescription>
            Update credentials, enable state, or priority for this provider.
          </SheetDescription>
          <SheetCloseButton onClick={handleClose} />
        </SheetHeader>

        <SheetBody>
          <form id="edit-provider-form" onSubmit={(e) => void handleSubmit(e)} className="space-y-5">
            {error && (
              <StatusBanner tone="danger" title={error} />
            )}

            <section aria-labelledby="module-info-label">
              <p id="module-info-label" className="mb-2 text-xs font-medium text-muted-foreground uppercase tracking-wide">
                Provider
              </p>
              <div className="rounded-md border border-border/70 bg-background/35 p-3">
                <p className="text-sm font-semibold text-foreground">{module.displayName}</p>
                <p className="mt-0.5 font-mono text-xs text-muted-foreground">{module.moduleId}</p>
                {module.capabilityNames.length > 0 && (
                  <ProviderCapabilityBadges capabilities={module.capabilityNames} className="mt-2" />
                )}
              </div>
            </section>

            {module.credentialStatus.length > 0 && (
              <section aria-labelledby="creds-edit-label">
                <p id="creds-edit-label" className="mb-2 text-xs font-medium text-muted-foreground uppercase tracking-wide">
                  Credentials
                </p>
                <div className="space-y-3">
                  {module.credentialStatus.map(cred => (
                    <CredentialField
                      key={cred.key}
                      credKey={cred.key}
                      hasValue={cred.hasValue}
                      replacing={replacing[cred.key] ?? false}
                      showValue={showValues[cred.key] ?? false}
                      newValue={newValues[cred.key] ?? ""}
                      onToggleReplace={() => toggleReplace(cred.key)}
                      onToggleShow={() => toggleShow(cred.key)}
                      onValueChange={(v) => setNewValues(prev => ({ ...prev, [cred.key]: v }))}
                    />
                  ))}
                </div>
                <p className="mt-2 text-xs text-muted-foreground">
                  Only fields marked for replacement will be updated. Credentials are stored server-side
                  and never returned to the browser.
                </p>
              </section>
            )}

            <section aria-labelledby="options-edit-label">
              <p id="options-edit-label" className="mb-2 text-xs font-medium text-muted-foreground uppercase tracking-wide">
                Options
              </p>
              <div className="space-y-3">
                <div className="flex items-center justify-between">
                  <span className="text-sm text-foreground">Enabled</span>
                  <Toggle
                    checked={enabled}
                    onCheckedChange={setEnabled}
                    aria-label="Enable provider"
                  />
                </div>
                <div>
                  <label htmlFor="edit-priority-input" className="mb-1 block text-sm font-medium text-foreground">
                    Priority
                  </label>
                  <Input
                    id="edit-priority-input"
                    type="number"
                    min={1}
                    max={999}
                    value={priority}
                    onChange={e => { const v = parseInt(e.target.value, 10); setPriority(isNaN(v) ? module.priority : v); }}
                    className="max-w-[8rem]"
                  />
                  <p className="mt-1 text-xs text-muted-foreground">
                    Lower numbers are preferred when multiple providers offer the same capability.
                  </p>
                </div>
              </div>
            </section>

            <div className="flex justify-end gap-2 pt-2">
              <Button type="button" variant="outline" onClick={handleClose} disabled={busy}>
                Cancel
              </Button>
              <Button
                type="submit"
                form="edit-provider-form"
                busy={busy}
                busyLabel="Saving…"
              >
                Save changes
              </Button>
            </div>
          </form>
        </SheetBody>
      </SheetContent>
    </Sheet>
  );
}

interface CredentialFieldProps {
  credKey: string;
  hasValue: boolean;
  replacing: boolean;
  showValue: boolean;
  newValue: string;
  onToggleReplace: () => void;
  onToggleShow: () => void;
  onValueChange: (v: string) => void;
}

function CredentialField({
  credKey,
  hasValue,
  replacing,
  showValue,
  newValue,
  onToggleReplace,
  onToggleShow,
  onValueChange,
}: CredentialFieldProps) {
  return (
    <div>
      <div className="mb-1 flex items-center justify-between gap-2">
        <label htmlFor={`edit-cred-${credKey}`} className="text-sm font-medium text-foreground">
          {credKey}
          {hasValue && (
            <span className="ml-2 inline-flex items-center gap-1 text-xs font-normal text-muted-foreground">
              <span className="inline-block h-2 w-2 rounded-full bg-success" aria-hidden="true" />
              Set
            </span>
          )}
          {!hasValue && (
            <span className="ml-2 text-xs font-normal text-muted-foreground">Not set</span>
          )}
        </label>
        <Button
          type="button"
          size="sm"
          variant="ghost"
          onClick={onToggleReplace}
          aria-label={replacing ? `Cancel replacing ${credKey}` : `Replace ${credKey}`}
        >
          <RefreshCw className="mr-1 h-3 w-3" aria-hidden="true" />
          {replacing ? "Cancel" : "Replace"}
        </Button>
      </div>

      {replacing ? (
        <Input
          id={`edit-cred-${credKey}`}
          type={showValue ? "text" : "password"}
          autoComplete="off"
          placeholder={`New ${credKey}`}
          value={newValue}
          onChange={e => onValueChange(e.target.value)}
          trailingIcon={
            <button
              type="button"
              onClick={onToggleShow}
              aria-label={showValue ? "Hide value" : "Show value"}
              className="pointer-events-auto cursor-pointer text-muted-foreground hover:text-foreground"
            >
              {showValue
                ? <EyeOff className="h-4 w-4" aria-hidden="true" />
                : <Eye className="h-4 w-4" aria-hidden="true" />}
            </button>
          }
        />
      ) : (
        <div
          id={`edit-cred-${credKey}`}
          className="flex min-h-9 items-center rounded-[var(--radius-button,0.375rem)] border border-border/80 bg-background/55 px-3 py-2 text-sm text-muted-foreground"
          aria-label={hasValue ? `${credKey}: value stored` : `${credKey}: not set`}
        >
          {hasValue ? "••••••••" : <span className="italic">Not configured</span>}
        </div>
      )}
    </div>
  );
}
