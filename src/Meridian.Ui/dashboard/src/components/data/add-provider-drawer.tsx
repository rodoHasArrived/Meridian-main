import { useState, type FormEvent } from "react";
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
import type { ProviderModuleCatalogueEntry, UpsertProviderModuleRequest } from "@/types/provider-setup";

interface Props {
  open: boolean;
  catalogue: ProviderModuleCatalogueEntry[];
  onClose: () => void;
  onAdd: (request: UpsertProviderModuleRequest) => Promise<boolean>;
}

export function AddProviderDrawer({ open, catalogue, onClose, onAdd }: Props) {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [credentials, setCredentials] = useState<Record<string, string>>({});
  const [enabled, setEnabled] = useState(true);
  const [priority, setPriority] = useState(100);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const selected = catalogue.find(e => e.moduleId === selectedId) ?? null;

  const available = catalogue.filter(e => !e.alreadyConfigured);

  const handleSelect = (entry: ProviderModuleCatalogueEntry) => {
    setSelectedId(entry.moduleId);
    setCredentials({});
    setError(null);
  };

  const handleClose = () => {
    setSelectedId(null);
    setCredentials({});
    setEnabled(true);
    setPriority(100);
    setError(null);
    onClose();
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!selected) return;

    setBusy(true);
    setError(null);
    try {
      const credentialValues: Record<string, string | null> = {};
      for (const key of selected.credentialKeyHints) {
        credentialValues[key] = credentials[key]?.trim() || null;
      }
      const success = await onAdd({
        moduleId: selected.moduleId,
        enabled,
        priority,
        credentialValues: Object.keys(credentialValues).length > 0 ? credentialValues : undefined,
      });
      if (success) {
        handleClose();
      } else {
        setError("Failed to add provider. Check credentials and try again.");
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
          <SheetTitle>Add provider module</SheetTitle>
          <SheetDescription>
            Select a market data provider and enter credentials.
          </SheetDescription>
          <SheetCloseButton onClick={handleClose} />
        </SheetHeader>

        <SheetBody>
          <form id="add-provider-form" onSubmit={(e) => void handleSubmit(e)} className="space-y-5">
            {error && (
              <StatusBanner tone="danger" title={error} />
            )}

            <section aria-labelledby="catalogue-label">
              <p id="catalogue-label" className="mb-2 text-xs font-medium text-muted-foreground uppercase tracking-wide">
                Available providers
              </p>
              {available.length === 0 ? (
                <p className="text-sm text-muted-foreground">All known providers are already configured.</p>
              ) : (
                <div className="grid gap-2">
                  {available.map(entry => (
                    <button
                      key={entry.moduleId}
                      type="button"
                      onClick={() => handleSelect(entry)}
                      className={`w-full rounded-md border p-3 text-left text-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 ${
                        selectedId === entry.moduleId
                          ? "border-primary/60 bg-primary/8"
                          : "border-border/70 bg-background/35 hover:border-border hover:bg-secondary/40"
                      }`}
                      aria-pressed={selectedId === entry.moduleId}
                    >
                      <span className="block font-semibold text-foreground">{entry.displayName}</span>
                      <span className="mt-0.5 block font-mono text-xs text-muted-foreground">{entry.moduleId}</span>
                      {entry.capabilityNames.length > 0 && (
                        <ProviderCapabilityBadges capabilities={entry.capabilityNames} className="mt-2" />
                      )}
                    </button>
                  ))}
                </div>
              )}
            </section>

            {selected && (
              <>
                {selected.credentialKeyHints.length > 0 && (
                  <section aria-labelledby="credentials-label">
                    <p id="credentials-label" className="mb-2 text-xs font-medium text-muted-foreground uppercase tracking-wide">
                      Credentials
                    </p>
                    <div className="space-y-3">
                      {selected.credentialKeyHints.map(key => (
                        <div key={key}>
                          <label
                            htmlFor={`cred-${key}`}
                            className="mb-1 block text-sm font-medium text-foreground"
                          >
                            {key}
                          </label>
                          <Input
                            id={`cred-${key}`}
                            type="password"
                            autoComplete="off"
                            placeholder={`Enter ${key}`}
                            value={credentials[key] ?? ""}
                            onChange={e => setCredentials(prev => ({ ...prev, [key]: e.target.value }))}
                          />
                        </div>
                      ))}
                    </div>
                    <p className="mt-2 text-xs text-muted-foreground">
                      Credentials are stored server-side and never returned to the browser.
                    </p>
                  </section>
                )}

                {selected.requiresExternalConfig && (
                  <StatusBanner
                    tone="info"
                    title="External configuration required"
                    detail="This provider requires additional configuration outside the UI. Refer to the provider documentation."
                  />
                )}

                <section aria-labelledby="options-label">
                  <p id="options-label" className="mb-2 text-xs font-medium text-muted-foreground uppercase tracking-wide">
                    Options
                  </p>
                  <div className="space-y-3">
                    <div className="flex items-center justify-between">
                      <span className="text-sm text-foreground">Enable immediately</span>
                      <Toggle
                        checked={enabled}
                        onCheckedChange={setEnabled}
                        aria-label="Enable provider immediately"
                      />
                    </div>
                    <div>
                      <label htmlFor="priority-input" className="mb-1 block text-sm font-medium text-foreground">
                        Priority
                      </label>
                      <Input
                        id="priority-input"
                        type="number"
                        min={1}
                        max={999}
                        value={priority}
                        onChange={e => setPriority(Number(e.target.value))}
                        className="max-w-[8rem]"
                      />
                      <p className="mt-1 text-xs text-muted-foreground">
                        Lower numbers are preferred when multiple providers offer the same capability.
                      </p>
                    </div>
                  </div>
                </section>
              </>
            )}

            <div className="flex justify-end gap-2 pt-2">
              <Button type="button" variant="outline" onClick={handleClose} disabled={busy}>
                Cancel
              </Button>
              <Button
                type="submit"
                form="add-provider-form"
                disabled={!selected || busy}
                busy={busy}
                busyLabel="Adding…"
              >
                Add provider
              </Button>
            </div>
          </form>
        </SheetBody>
      </SheetContent>
    </Sheet>
  );
}
