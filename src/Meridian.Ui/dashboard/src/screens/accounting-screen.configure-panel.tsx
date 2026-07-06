import { ArrowRight, ListChecks, Plus, Search, Trash2, Wand2 } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Combobox } from "@/components/ui/combobox";
import { Drawer, DrawerBody, DrawerFooter } from "@/components/ui/drawer";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Stepper } from "@/components/ui/stepper";
import { cn } from "@/lib/utils";
import { accountingToolingBadgeVariant } from "@/screens/accounting-screen.styles";
import type {
  AccountingChartAccountEditorViewModel,
  AccountingConfigurationViewModel
} from "@/screens/accounting-screen.view-model";
import {
  CONFIGURE_SECTION_LINKS,
  buildChartPathSegments,
  buildConfigureActivationSummary,
  buildConfigureChangePreview,
  filterConfigureSearch,
  parseConfigureKeyValuePairs,
  serializeConfigureKeyValuePairs,
  appendChartPathSegment,
  type ConfigureKeyValuePair,
  type ConfigureTone
} from "@/screens/accounting-screen.configure-panel.view-model";

function toneTextClass(tone: ConfigureTone): string {
  switch (tone) {
    case "success":
      return "text-success";
    case "warning":
      return "text-warning";
    case "danger":
      return "text-danger";
    default:
      return "text-muted-foreground";
  }
}

function toneDotClass(tone: ConfigureTone): string {
  switch (tone) {
    case "success":
      return "bg-success";
    case "warning":
      return "bg-warning";
    case "danger":
      return "bg-danger";
    default:
      return "bg-muted-foreground/50";
  }
}

/**
 * Scroll a Configure section into view and briefly highlight it. jsdom does not implement
 * `scrollIntoView`, so every call is guarded; the transient flash class is defined in
 * `accounting-screen.css`.
 */
function scrollToConfigureAnchor(anchorId: string): void {
  if (typeof document === "undefined") {
    return;
  }
  const target = document.getElementById(anchorId);
  if (!target) {
    return;
  }
  if (typeof target.scrollIntoView === "function") {
    target.scrollIntoView({ behavior: "smooth", block: "start" });
  }
  target.classList.add("configure-anchor-flash");
  if (typeof window !== "undefined" && typeof window.setTimeout === "function") {
    window.setTimeout(() => target.classList.remove("configure-anchor-flash"), 1200);
  }
}

/**
 * In-page section navigation plus a jump-to-setting search box for the Configure panel.
 * Turns the long single scroll into a navigable workspace without unmounting any section.
 */
export function ConfigureCommandBar(): JSX.Element {
  const [query, setQuery] = useState("");
  const results = useMemo(() => filterConfigureSearch(query), [query]);

  return (
    <div className="configure-command-bar sticky top-0 z-20 flex flex-col gap-2 rounded-md border border-border/70 bg-background/95 px-3 py-2 backdrop-blur supports-[backdrop-filter]:bg-background/80">
      <div className="flex flex-wrap items-center gap-2">
        <nav aria-label="Accounting configuration sections" className="flex flex-wrap items-center gap-1.5">
          {CONFIGURE_SECTION_LINKS.map((section) => (
            <button
              key={section.id}
              type="button"
              title={section.description}
              onClick={() => scrollToConfigureAnchor(section.anchorId)}
              className="rounded-full border border-border/70 bg-secondary/30 px-3 py-1 text-xs font-semibold text-muted-foreground transition-colors hover:border-primary/40 hover:bg-primary/10 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
            >
              {section.label}
            </button>
          ))}
        </nav>
        <div className="relative ml-auto w-full max-w-xs">
          <Search className="pointer-events-none absolute left-2 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
          <Input
            aria-label="Search accounting configuration settings"
            placeholder="Jump to a setting…"
            value={query}
            onChange={(event) => setQuery(event.currentTarget.value)}
            className="pl-7"
          />
          {results.length > 0 ? (
            <ul
              role="listbox"
              aria-label="Configuration setting search results"
              className="absolute left-0 right-0 top-full z-30 mt-1 max-h-64 overflow-auto rounded-md border border-border bg-popover p-1 shadow-md"
            >
              {results.map((entry) => (
                <li key={entry.id}>
                  <button
                    type="button"
                    onClick={() => {
                      scrollToConfigureAnchor(entry.anchorId);
                      setQuery("");
                    }}
                    className="flex w-full items-center justify-between gap-2 rounded px-2 py-1.5 text-left text-xs hover:bg-secondary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                  >
                    <span className="text-foreground">{entry.label}</span>
                    <span className="font-mono text-[10px] uppercase tracking-[0.08em] text-muted-foreground">{entry.sectionLabel}</span>
                  </button>
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      </div>
    </div>
  );
}

/**
 * Sticky activation checklist. Mirrors setup readiness and blockers as clickable items
 * that deep-link to the section that resolves them, and repeats the Activate action next
 * to the gates it depends on.
 */
export function ConfigureActivationRail({ view }: { view: AccountingConfigurationViewModel }): JSX.Element {
  const summary = useMemo(
    () => buildConfigureActivationSummary(view),
    [view]
  );

  return (
    <aside
      aria-label="Accounting configuration activation checklist"
      className="configure-activation-rail flex h-fit flex-col gap-3 rounded-lg border border-border/70 bg-secondary/15 p-3 xl:sticky xl:top-16"
    >
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <ListChecks className="h-4 w-4 text-primary" aria-hidden="true" />
          <span className="text-sm font-semibold text-foreground">Activation checklist</span>
        </div>
        <Badge variant={summary.tone === "success" ? "success" : summary.tone === "danger" ? "danger" : "warning"} dot>
          {summary.blockerCount > 0 ? `${summary.blockerCount} blocker${summary.blockerCount === 1 ? "" : "s"}` : summary.readyCount > 0 ? `${summary.readyCount} ready` : "Review"}
        </Badge>
      </div>

      <p className={cn("text-xs font-semibold", toneTextClass(summary.tone))}>{summary.summaryLabel}</p>

      {summary.items.length > 0 ? (
        <ul className="flex flex-col gap-1.5">
          {summary.items.map((item) => (
            <li key={item.id} className="flex items-start gap-2 rounded-md border border-border/60 bg-background/50 px-2 py-1.5">
              <span className={cn("mt-1 h-2 w-2 shrink-0 rounded-full", toneDotClass(item.tone))} aria-hidden="true" />
              <div className="min-w-0 flex-1">
                <div className="truncate text-xs font-semibold text-foreground" title={item.label}>{item.label}</div>
                <div className="truncate text-[11px] text-muted-foreground" title={item.detail}>{item.detail}</div>
              </div>
              <button
                type="button"
                onClick={() => scrollToConfigureAnchor(item.anchorId)}
                className="mt-0.5 inline-flex shrink-0 items-center gap-0.5 rounded px-1 text-[11px] font-semibold text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                aria-label={`Go to ${item.label}`}
              >
                Go
                <ArrowRight className="h-3 w-3" aria-hidden="true" />
              </button>
            </li>
          ))}
        </ul>
      ) : (
        <p className="text-xs text-muted-foreground">No outstanding setup readiness items.</p>
      )}

      <Button
        size="sm"
        className="w-full"
        disabled={!view.canActivate}
        disabledReason={view.activateDisabledReason}
        busy={view.activateBusy}
        busyLabel={view.activateButtonLabel}
        onClick={() => void view.activate()}
      >
        {view.activateButtonLabel}
      </Button>
    </aside>
  );
}

interface ConfigureKeyValueFieldProps {
  id: string;
  label: string;
  value: string;
  onChange: (next: string) => void;
  keyPlaceholder?: string;
  valuePlaceholder?: string;
  addLabel?: string;
  description?: string;
}

/**
 * Row-based editor for the `key=value` maps that were previously hand-typed into a raw
 * textarea (dimension maps, external GL account mappings). Serializes back through the
 * exact same setter so persisted state is byte-compatible; the raw textarea remains
 * available beneath as an escape hatch.
 */
export function ConfigureKeyValueField({
  id,
  label,
  value,
  onChange,
  keyPlaceholder = "key",
  valuePlaceholder = "value",
  addLabel = "Add row",
  description
}: ConfigureKeyValueFieldProps): JSX.Element {
  const [rows, setRows] = useState<ConfigureKeyValuePair[]>(() => parseConfigureKeyValuePairs(value));

  // Re-sync from the canonical value when it changes outside this editor (refresh, save,
  // wizard) but ignore echoes of our own serialization so in-progress blank rows survive.
  useEffect(() => {
    if (serializeConfigureKeyValuePairs(rows) !== value) {
      setRows(parseConfigureKeyValuePairs(value));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value]);

  const commit = (next: ConfigureKeyValuePair[]): void => {
    setRows(next);
    onChange(serializeConfigureKeyValuePairs(next));
  };

  const updateRow = (rowId: string, patch: Partial<ConfigureKeyValuePair>): void => {
    commit(rows.map((row) => (row.id === rowId ? { ...row, ...patch } : row)));
  };

  const removeRow = (rowId: string): void => {
    commit(rows.filter((row) => row.id !== rowId));
  };

  const addRow = (): void => {
    setRows((current) => [...current, { id: `pair-new-${current.length}-${current.reduce((total, row) => total + row.key.length, 0)}`, key: "", value: "" }]);
  };

  return (
    <div id={id} className="space-y-2" role="group" aria-label={`${label} key value editor`}>
      <div className="flex items-center justify-between gap-2">
        <span className="text-xs font-semibold text-foreground">{label}</span>
        <Button type="button" size="sm" variant="ghost" onClick={addRow}>
          <Plus className="h-3.5 w-3.5" aria-hidden="true" />
          {addLabel}
        </Button>
      </div>
      {description ? <p className="text-[11px] text-muted-foreground">{description}</p> : null}
      {rows.length === 0 ? (
        <p className="text-[11px] text-muted-foreground">No entries yet. Use “{addLabel}” to add one.</p>
      ) : (
        <ul className="space-y-1.5">
          {rows.map((row, index) => (
            <li key={row.id} className="flex items-center gap-1.5">
              <Input
                aria-label={`${label} key ${index + 1}`}
                value={row.key}
                placeholder={keyPlaceholder}
                onChange={(event) => updateRow(row.id, { key: event.currentTarget.value })}
                className="font-mono text-xs"
              />
              <span aria-hidden="true" className="text-muted-foreground">=</span>
              <Input
                aria-label={`${label} value ${index + 1}`}
                value={row.value}
                placeholder={valuePlaceholder}
                onChange={(event) => updateRow(row.id, { value: event.currentTarget.value })}
                className="font-mono text-xs"
              />
              <Button
                type="button"
                size="sm"
                variant="ghost"
                aria-label={`Remove ${label} row ${index + 1}`}
                onClick={() => removeRow(row.id)}
              >
                <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
              </Button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

interface ConfigureComboFieldProps {
  id: string;
  label: string;
  value: string;
  onChange: (next: string) => void;
  options: string[];
  placeholder?: string;
}

/**
 * Free-text-with-suggestions field for closed-ish enums (approval role, workflow kind,
 * segregation policy). Keeps arbitrary values valid — the underlying setter still receives
 * a plain string — while surfacing the common choices.
 */
export function ConfigureComboField({ id, label, value, onChange, options, placeholder }: ConfigureComboFieldProps): JSX.Element {
  // Ensure the current value is always selectable/visible even when it isn't one of the
  // suggested options, so arbitrary persisted values keep displaying.
  const mergedOptions = value && !options.includes(value) ? [value, ...options] : options;
  return (
    <FormRow label={label} labelFor={id}>
      <Combobox
        id={id}
        options={mergedOptions}
        value={value}
        onChange={onChange}
        placeholder={placeholder ?? "Select or type…"}
      />
    </FormRow>
  );
}

/**
 * Interactive chart-account path builder. Renders the current path as a breadcrumb tree,
 * lets the operator append a segment, and — the key affordance — promote the current path
 * to the parent for the next child so hierarchies are built by clicking rather than
 * retyping slash/dot paths.
 */
export function ChartAccountPathBuilder({ editor }: { editor: AccountingChartAccountEditorViewModel }): JSX.Element {
  const [segment, setSegment] = useState("");
  const segments = useMemo(() => buildChartPathSegments(editor.pathValue), [editor.pathValue]);
  const parentSegments = useMemo(() => buildChartPathSegments(editor.parentPathValue), [editor.parentPathValue]);

  const addSegment = (): void => {
    const next = appendChartPathSegment(editor.pathValue, segment);
    if (next !== editor.pathValue) {
      editor.updateDraft({ path: next });
      setSegment("");
    }
  };

  return (
    <div className="space-y-2 rounded-md border border-border/70 bg-secondary/15 px-3 py-2" role="group" aria-label="Chart account path builder">
      <div className="flex items-center justify-between gap-2">
        <span className="text-xs font-semibold text-foreground">Path builder</span>
        <Button
          type="button"
          size="sm"
          variant="ghost"
          disabled={editor.pathValue.trim().length === 0}
          onClick={() => {
            editor.updateDraft({ parentPath: editor.pathValue, path: "" });
            setSegment("");
          }}
        >
          Use as parent for next child
        </Button>
      </div>

      <div className="text-[11px] text-muted-foreground">
        Parent:{" "}
        {parentSegments.length > 0 ? (
          <span className="font-mono text-foreground">
            {parentSegments.map((node, index) => (
              <span key={node.id}>
                {index > 0 ? <span aria-hidden="true"> › </span> : null}
                {node.label}
              </span>
            ))}
          </span>
        ) : (
          <span className="italic">root</span>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-1" aria-label="Current chart path breadcrumb">
        {segments.length > 0 ? (
          segments.map((node, index) => (
            <button
              key={node.id}
              type="button"
              onClick={() => editor.updateDraft({ path: node.path })}
              title={`Set path to ${node.path}`}
              className="inline-flex items-center gap-1 rounded border border-border/70 bg-background px-2 py-0.5 font-mono text-[11px] text-foreground hover:border-primary/40 hover:bg-primary/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
            >
              {index > 0 ? <span aria-hidden="true" className="text-muted-foreground">›</span> : null}
              {node.label}
            </button>
          ))
        ) : (
          <span className="text-[11px] italic text-muted-foreground">No path segments yet.</span>
        )}
      </div>

      <div className="flex items-center gap-1.5">
        <Input
          aria-label="New chart path segment"
          value={segment}
          placeholder="e.g. Investments"
          onChange={(event) => setSegment(event.currentTarget.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter") {
              event.preventDefault();
              addSegment();
            }
          }}
          className="font-mono text-xs"
        />
        <Button type="button" size="sm" variant="secondary" disabled={segment.trim().length === 0} onClick={addSegment}>
          <Plus className="h-3.5 w-3.5" aria-hidden="true" />
          Append
        </Button>
      </div>
    </div>
  );
}

const LEDGER_BOOK_WIZARD_STEPS = [
  { label: "Review setup" },
  { label: "Seed chart account" },
  { label: "Create book" }
];

/**
 * Guided drawer for standing up a ledger book. It walks the operator through the setup
 * candidate context, optional first chart-account authoring, and a final review, then calls
 * the existing `createLedgerBookFromSetupCandidate` command — a guided front-end over the
 * same action, no new API surface.
 */
export function LedgerBookSetupWizard({ view }: { view: AccountingConfigurationViewModel }): JSX.Element {
  const [open, setOpen] = useState(false);
  const [step, setStep] = useState(0);

  const close = (): void => {
    setOpen(false);
    setStep(0);
  };

  return (
    <>
      <Button type="button" size="sm" variant="outline" onClick={() => setOpen(true)}>
        <Wand2 className="h-3.5 w-3.5" aria-hidden="true" />
        Guided book setup
      </Button>

      <Drawer open={open} onClose={close} title="Guided ledger book setup" side="right" className="w-full max-w-lg">
        <Stepper steps={LEDGER_BOOK_WIZARD_STEPS} activeStep={step} onStepChange={setStep} />
        <DrawerBody className="space-y-4">
          {step === 0 ? (
            <div className="space-y-3" aria-label="Ledger book setup readiness review">
              <p className="text-sm text-muted-foreground">
                Confirm the setup candidate below before creating the book. These values come from the shared
                accounting configuration read model.
              </p>
              <div className="grid gap-2">
                {view.setupReadinessRows.map((row) => (
                  <div key={row.id} className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2">
                    <div className="text-xs font-semibold uppercase tracking-[0.12em] text-muted-foreground">{row.label}</div>
                    <div className={cn("mt-1 text-sm font-semibold", toneTextClass(row.tone))}>{row.value}</div>
                    <p className="mt-1 text-xs text-muted-foreground">{row.detail}</p>
                  </div>
                ))}
              </div>
            </div>
          ) : null}

          {step === 1 ? (
            <div className="space-y-3" aria-label="Ledger book seed chart account">
              <p className="text-sm text-muted-foreground">
                Optionally author the book’s first chart account now. You can also skip this and add accounts later
                from the Chart section.
              </p>
              <div className="grid gap-3 sm:grid-cols-2">
                <FormRow label="Account path" labelFor="wizard-chart-path">
                  <Input
                    id="wizard-chart-path"
                    value={view.chartAccountEditor.pathValue}
                    onChange={(event) => view.chartAccountEditor.updateDraft({ path: event.currentTarget.value })}
                  />
                </FormRow>
                <FormRow label="Account name" labelFor="wizard-chart-name">
                  <Input
                    id="wizard-chart-name"
                    value={view.chartAccountEditor.accountNameValue}
                    onChange={(event) => view.chartAccountEditor.updateDraft({ accountName: event.currentTarget.value })}
                  />
                </FormRow>
              </div>
              <ChartAccountPathBuilder editor={view.chartAccountEditor} />
            </div>
          ) : null}

          {step === 2 ? (
            <div className="space-y-3" aria-label="Ledger book create review">
              <p className="text-sm text-muted-foreground">Review and create the ledger book from the setup candidate.</p>
              <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-2 text-sm">
                <div className="font-semibold text-foreground">{view.createLedgerBookButtonLabel}</div>
                {view.createLedgerBookDisabledReason ? (
                  <p className="mt-1 text-xs text-warning">{view.createLedgerBookDisabledReason}</p>
                ) : (
                  <p className="mt-1 text-xs text-muted-foreground">The setup candidate is ready to be created.</p>
                )}
                {view.createLedgerBookStatusText ? (
                  <p className="mt-1 text-xs text-muted-foreground">{view.createLedgerBookStatusText}</p>
                ) : null}
              </div>
            </div>
          ) : null}
        </DrawerBody>
        <DrawerFooter className="flex items-center justify-between gap-2">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={step === 0}
            onClick={() => setStep((current) => Math.max(0, current - 1))}
          >
            Back
          </Button>
          {step < LEDGER_BOOK_WIZARD_STEPS.length - 1 ? (
            <Button type="button" size="sm" onClick={() => setStep((current) => Math.min(LEDGER_BOOK_WIZARD_STEPS.length - 1, current + 1))}>
              Next
            </Button>
          ) : (
            <Button
              type="button"
              size="sm"
              disabled={!view.canCreateLedgerBook}
              disabledReason={view.createLedgerBookDisabledReason}
              busy={view.createLedgerBookBusy}
              busyLabel={view.createLedgerBookButtonLabel}
              onClick={async () => {
                await view.createLedgerBookFromSetupCandidate();
                close();
              }}
            >
              Create ledger book
            </Button>
          )}
        </DrawerFooter>
      </Drawer>
    </>
  );
}

/**
 * Change-and-activation preview. Surfaces which editors hold unsaved-but-valid drafts and
 * what activation will do, so the operator can review before committing on a governed
 * append-only surface.
 */
export function ConfigureChangePreviewPanel({ view }: { view: AccountingConfigurationViewModel }): JSX.Element {
  const preview = useMemo(() => buildConfigureChangePreview(view), [view]);

  return (
    <div
      id="configure-section-activation"
      role="region"
      aria-label="Accounting configuration change preview"
      className="configure-anchor scroll-mt-20 space-y-3 rounded-lg border border-border/70 bg-background/40 p-3"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <div className="text-sm font-semibold text-foreground">Review changes before activation</div>
          <p className="text-xs text-muted-foreground">{preview.headline}</p>
        </div>
        <Badge variant={preview.activationTone === "success" ? "success" : "warning"} dot>
          {preview.activationTone === "success" ? "Activation ready" : "Activation blocked"}
        </Badge>
      </div>

      <ul className="grid gap-2 md:grid-cols-2">
        {preview.pendingRows.map((row) => (
          <li key={row.id} className="rounded-md border border-border/60 bg-secondary/15 px-3 py-2">
            <div className="flex items-center justify-between gap-2">
              <span className="text-sm font-semibold text-foreground">{row.label}</span>
              <Badge variant={accountingToolingBadgeVariant(row.tone)}>{row.tone === "warning" ? "Pending" : "Saved"}</Badge>
            </div>
            <p className="mt-1 text-xs text-muted-foreground">{row.statusLabel}</p>
          </li>
        ))}
      </ul>

      <p className={cn("text-xs font-semibold", toneTextClass(preview.activationTone))}>{preview.activationLabel}</p>
    </div>
  );
}
