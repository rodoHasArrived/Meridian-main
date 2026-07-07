import { ArrowRight, ListChecks, Plus, Search, ShieldCheck, Trash2, Wand2 } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Combobox } from "@/components/ui/combobox";
import { Drawer, DrawerBody, DrawerFooter } from "@/components/ui/drawer";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Stepper } from "@/components/ui/stepper";
import { cn } from "@/lib/utils";
import { accountingToolingBadgeVariant, accountingToolingBorderClass } from "@/screens/accounting-screen.styles";
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
  const [isFocused, setIsFocused] = useState(false);
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
        <div
          className="relative ml-auto w-full max-w-xs"
          onFocus={() => setIsFocused(true)}
          onBlur={(event) => {
            if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
              setIsFocused(false);
            }
          }}
        >
          <Search className="pointer-events-none absolute left-2 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
          <Input
            aria-label="Search accounting configuration settings"
            placeholder="Jump to a setting…"
            value={query}
            onChange={(event) => setQuery(event.currentTarget.value)}
            className="pl-7"
          />
          {isFocused && results.length > 0 ? (
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
  // Track the last value we serialized outward so external changes re-seed the rows while
  // our own echoes do not — avoids depending on `rows` inside the sync effect (no stale
  // closure, no eslint bypass).
  const lastEmittedRef = useRef(value);
  // Monotonic id source for newly added rows; never reused even after deletions, so React
  // keys stay unique and stable.
  const nextRowIdRef = useRef(0);

  useEffect(() => {
    if (value !== lastEmittedRef.current) {
      lastEmittedRef.current = value;
      setRows(parseConfigureKeyValuePairs(value));
    }
  }, [value]);

  const commit = (next: ConfigureKeyValuePair[]): void => {
    const serialized = serializeConfigureKeyValuePairs(next);
    lastEmittedRef.current = serialized;
    setRows(next);
    onChange(serialized);
  };

  const updateRow = (rowId: string, patch: Partial<ConfigureKeyValuePair>): void => {
    commit(rows.map((row) => (row.id === rowId ? { ...row, ...patch } : row)));
  };

  const removeRow = (rowId: string): void => {
    commit(rows.filter((row) => row.id !== rowId));
  };

  const addRow = (): void => {
    nextRowIdRef.current += 1;
    setRows((current) => [...current, { id: `pair-new-${nextRowIdRef.current}`, key: "", value: "" }]);
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

/**
 * Production readiness + tenant administration + external GL card, extracted from
 * AccountingConfigurationPanel to keep the accounting screen under its file-size ratchet.
 * Pure presentation over the existing view-model.
 */
export function ConfigureProductionReadinessCard({ view }: { view: AccountingConfigurationViewModel }): JSX.Element {
  return (
      <Card id="configure-section-mappings" className="panel-surface configure-anchor scroll-mt-20" aria-labelledby="accounting-production-readiness-heading">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle id="accounting-production-readiness-heading" className="flex items-center gap-2">
                <ShieldCheck className="h-5 w-5 text-primary" aria-hidden="true" />
                {view.productionReadiness.title}
              </CardTitle>
              <CardDescription>{view.productionReadiness.scopeLabel}</CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={accountingToolingBadgeVariant(view.productionReadiness.components.length === 0 ? "default" : view.productionReadiness.components.some((component) => component.tone === "danger") ? "danger" : view.productionReadiness.components.some((component) => component.tone === "warning") ? "warning" : "success")} dot>
                {view.productionReadiness.statusLabel}
              </Badge>
              <Badge variant="outline">{view.productionReadiness.scoreLabel}</Badge>
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {view.productionReadiness.errorText ? (
            <div role="alert" className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
              <div className="font-semibold">{view.productionReadiness.errorText}</div>
              {view.productionReadiness.errorDetails.length > 0 ? (
                <ul className="mt-2 list-disc pl-4">
                  {view.productionReadiness.errorDetails.map((detail) => <li key={detail}>{detail}</li>)}
                </ul>
              ) : null}
            </div>
          ) : null}

          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-6">
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Readiness</div>
              <div className={cn("mt-2 text-sm font-semibold", view.productionReadiness.blockerIssues.some((issue) => issue.tone === "danger") ? "text-danger" : view.productionReadiness.blockerIssues.length > 0 ? "text-warning" : "text-success")}>{view.productionReadiness.issueSummaryLabel}</div>
              <p className="mt-1 text-xs text-muted-foreground">{view.productionReadiness.generatedAtLabel}</p>
            </div>
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Ledger books</div>
              <div className="mt-2 text-sm font-semibold text-foreground">{view.productionReadiness.ledgerBookRolloutLabel}</div>
              <p className="mt-1 text-xs text-muted-foreground">Rollout evidence remains service-owned.</p>
            </div>
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">External GL</div>
              <div className="mt-2 text-sm font-semibold text-foreground">{view.productionReadiness.externalGlLabel}</div>
              <p className="mt-1 text-xs text-muted-foreground">Import, mapping, reconciliation, and guarded export posture.</p>
            </div>
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Dimensions</div>
              <div className="mt-2 text-sm font-semibold text-foreground">{view.productionReadiness.dimensionalReportingLabel}</div>
              <p className="mt-1 text-xs text-muted-foreground">{view.productionReadiness.dimensionalReportingEvidenceLabel}</p>
            </div>
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Control plane</div>
              <div className="mt-2 text-sm font-semibold text-foreground">{view.productionReadiness.components.length} component checks</div>
              <p className="mt-1 text-xs text-muted-foreground">Rules, posting, JE lifecycle, dimensions, close, and admin readiness.</p>
            </div>
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Tenant admin</div>
              <div className="mt-2 text-sm font-semibold text-foreground">{view.productionReadiness.tenantAdministrationLabel}</div>
              <p className="mt-1 text-xs text-muted-foreground">{view.productionReadiness.tenantAdministrationEvidenceLabel}</p>
            </div>
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Migration evidence</div>
              <div className="mt-2 text-sm font-semibold text-foreground">{view.productionReadiness.migrationArtifactSummaryLabel}</div>
              <p className="mt-1 text-xs text-muted-foreground">Retained migration run artifacts from the shared Accounting System store.</p>
            </div>
          </div>

          {view.productionReadiness.productionGapRows.length > 0 ? (
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-5" role="region" aria-label="Accounting production gap checklist">
              {view.productionReadiness.productionGapRows.map((gap) => (
                <div key={gap.id} className={cn("rounded-md border px-3 py-3", accountingToolingBorderClass(gap.tone))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div>
                      <div className="font-semibold text-foreground">{gap.label}</div>
                      <div className="mt-1 font-mono text-[11px] text-muted-foreground">{gap.id}</div>
                    </div>
                    <Badge variant={accountingToolingBadgeVariant(gap.tone)}>{gap.statusLabel}</Badge>
                  </div>
                  <div className="mt-2 text-[11px] text-muted-foreground">{gap.severityLabel} | {gap.areaLabel}</div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{gap.summary}</p>
                  <p className="mt-2 text-xs leading-5 text-foreground">{gap.requiredAction}</p>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{gap.issueDetailLabel}</p>
                  <div className="mt-2 font-mono text-[11px] text-muted-foreground">{gap.blockingIssueLabel}</div>
                  <div className="mt-1 font-mono text-[11px] text-muted-foreground">{gap.routeLabel}</div>
                </div>
              ))}
            </div>
          ) : null}

          {view.productionReadiness.tenantAdministrationControls.length > 0 ? (
            <div className="grid gap-2 md:grid-cols-4" role="region" aria-label="Accounting tenant administration readiness controls">
              {view.productionReadiness.tenantAdministrationControls.map((control) => (
                <div key={control.id} className={cn("rounded-md border px-3 py-2", accountingToolingBorderClass(control.tone))}>
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-sm font-semibold text-foreground">{control.label}</span>
                    <Badge variant={accountingToolingBadgeVariant(control.tone)}>{control.statusLabel}</Badge>
                  </div>
                </div>
              ))}
            </div>
          ) : null}

          <div className="rounded-lg border border-border/70 bg-background/35 p-3" role="region" aria-label="Accounting production certification profile editor">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="font-semibold text-foreground">{view.productionCertificationProfile.title}</div>
                <div className="mt-1 font-mono text-xs text-muted-foreground">{view.productionCertificationProfile.scopeLabel}</div>
                <div className="mt-1 text-xs text-muted-foreground">{view.productionCertificationProfile.updatedLabel}</div>
              </div>
              <Button
                size="sm"
                disabled={!view.productionCertificationProfile.canSave}
                disabledReason={view.productionCertificationProfile.saveDisabledReason}
                busy={view.productionCertificationProfile.saveBusy}
                busyLabel={view.productionCertificationProfile.saveButtonLabel}
                onClick={() => void view.productionCertificationProfile.save()}
              >
                {view.productionCertificationProfile.saveButtonLabel}
              </Button>
            </div>
            <div className="mt-3 grid gap-2 md:grid-cols-4">
              {view.productionCertificationProfile.controls.map((control) => (
                <label key={control.id} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-sm">
                  <span className="flex items-center gap-2 font-semibold text-foreground">
                    <input
                      type="checkbox"
                      checked={control.checked}
                      onChange={(event) => view.productionCertificationProfile.updateControl(control.id, event.currentTarget.checked)}
                    />
                    {control.label}
                  </span>
                  <span className="mt-1 block text-xs text-muted-foreground">{control.description}</span>
                </label>
              ))}
            </div>
            <FormRow label="Retained certification evidence" labelFor="accounting-production-certification-evidence">
              <Input
                id="accounting-production-certification-evidence"
                value={view.productionCertificationProfile.evidenceValue}
                onChange={(event) => view.productionCertificationProfile.updateEvidence(event.currentTarget.value)}
                placeholder="evidence://accounting/production-certification"
              />
            </FormRow>
            {view.productionCertificationProfile.statusText ? (
              <p className={cn("text-sm", view.productionCertificationProfile.errorText ? "text-danger" : "text-muted-foreground")}>
                {view.productionCertificationProfile.statusText}
              </p>
            ) : null}
          </div>

          <div className="rounded-lg border border-border/70 bg-background/35 p-3" role="region" aria-label="Accounting tenant administration setup editor">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="font-semibold text-foreground">{view.tenantAdministrationProfile.title}</div>
                <div className="mt-1 font-mono text-xs text-muted-foreground">{view.tenantAdministrationProfile.scopeLabel}</div>
                <div className="mt-1 text-xs text-muted-foreground">{view.tenantAdministrationProfile.updatedLabel}</div>
              </div>
              <div className="flex flex-wrap gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  disabled={!view.tenantAdministrationProfile.canRetainSandboxProof}
                  disabledReason={view.tenantAdministrationProfile.sandboxDisabledReason}
                  busy={view.tenantAdministrationProfile.sandboxBusy}
                  busyLabel={view.tenantAdministrationProfile.sandboxButtonLabel}
                  onClick={() => void view.tenantAdministrationProfile.retainSandboxProof()}
                >
                  {view.tenantAdministrationProfile.sandboxButtonLabel}
                </Button>
                <Button
                  size="sm"
                  disabled={!view.tenantAdministrationProfile.canSave}
                  disabledReason={view.tenantAdministrationProfile.saveDisabledReason}
                  busy={view.tenantAdministrationProfile.saveBusy}
                  busyLabel={view.tenantAdministrationProfile.saveButtonLabel}
                  onClick={() => void view.tenantAdministrationProfile.save()}
                >
                  {view.tenantAdministrationProfile.saveButtonLabel}
                </Button>
              </div>
            </div>
            <div className="mt-3 grid gap-2 md:grid-cols-5">
              {view.tenantAdministrationProfile.controls.map((control) => (
                <label key={control.id} className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-sm">
                  <span className="flex items-center gap-2 font-semibold text-foreground">
                    <input
                      type="checkbox"
                      checked={control.checked}
                      onChange={(event) => view.tenantAdministrationProfile.updateControl(control.id, event.currentTarget.checked)}
                    />
                    {control.label}
                  </span>
                  <span className="mt-1 block text-xs text-muted-foreground">{control.description}</span>
                </label>
              ))}
            </div>
            <div className="mt-3 grid gap-3 rounded-md border border-border/70 bg-background/45 px-3 py-3" role="region" aria-label="Accounting approval queue setup editor">
              <div className="text-xs font-semibold uppercase text-muted-foreground">Approval queue setup</div>
              <div className="grid gap-3 md:grid-cols-3">
                <FormRow label="Queue id" labelFor="accounting-approval-queue-id">
                  <Input
                    id="accounting-approval-queue-id"
                    value={view.tenantAdministrationProfile.approvalQueueSetup.queueIdValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateApprovalQueueSetup({ queueId: event.currentTarget.value })}
                  />
                </FormRow>
                <FormRow label="Display name" labelFor="accounting-approval-queue-display-name">
                  <Input
                    id="accounting-approval-queue-display-name"
                    value={view.tenantAdministrationProfile.approvalQueueSetup.displayNameValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateApprovalQueueSetup({ displayName: event.currentTarget.value })}
                  />
                </FormRow>
                <ConfigureComboField
                  id="accounting-approval-queue-workflow-kind"
                  label="Workflow kind"
                  value={view.tenantAdministrationProfile.approvalQueueSetup.workflowKindValue}
                  onChange={(next) => view.tenantAdministrationProfile.updateApprovalQueueSetup({ workflowKind: next })}
                  options={["JournalApproval", "PostingApproval", "CloseApproval", "ReconciliationApproval"]}
                />
              </div>
              <div className="grid gap-3 md:grid-cols-[minmax(0,1fr)_8rem_minmax(0,1.5fr)]">
                <ConfigureComboField
                  id="accounting-approval-queue-role"
                  label="Approval role"
                  value={view.tenantAdministrationProfile.approvalQueueSetup.requiredApprovalRoleValue}
                  onChange={(next) => view.tenantAdministrationProfile.updateApprovalQueueSetup({ requiredApprovalRole: next })}
                  options={["Controller", "FundAccountant", "Reviewer", "Approver", "Administrator"]}
                />
                <FormRow label="Count" labelFor="accounting-approval-queue-count">
                  <Input
                    id="accounting-approval-queue-count"
                    type="number"
                    min={1}
                    value={view.tenantAdministrationProfile.approvalQueueSetup.requiredApprovalCountValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateApprovalQueueSetup({ requiredApprovalCount: event.currentTarget.value })}
                  />
                </FormRow>
                <FormRow label="Evidence requirement" labelFor="accounting-approval-queue-evidence">
                  <Input
                    id="accounting-approval-queue-evidence"
                    value={view.tenantAdministrationProfile.approvalQueueSetup.evidenceRequirementValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateApprovalQueueSetup({ evidenceRequirement: event.currentTarget.value })}
                  />
                </FormRow>
              </div>
              <ConfigureComboField
                id="accounting-approval-queue-segregation"
                label="Segregation policy"
                value={view.tenantAdministrationProfile.approvalQueueSetup.segregationPolicyValue}
                onChange={(next) => view.tenantAdministrationProfile.updateApprovalQueueSetup({ segregationPolicy: next })}
                options={["MakerChecker", "DualControl", "SegregatedDuties", "None"]}
              />
            </div>
            <div className="mt-3 grid gap-3 rounded-md border border-border/70 bg-background/45 px-3 py-3" role="region" aria-label="Accounting dimension mapping setup editor">
              <div className="text-xs font-semibold uppercase text-muted-foreground">Dimension mapping setup</div>
              <div className="grid gap-3 md:grid-cols-3">
                <FormRow label="Mapping id" labelFor="accounting-dimension-mapping-id">
                  <Input
                    id="accounting-dimension-mapping-id"
                    value={view.tenantAdministrationProfile.dimensionMappingSetup.mappingIdValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateDimensionMappingSetup({ mappingId: event.currentTarget.value })}
                  />
                </FormRow>
                <FormRow label="Display name" labelFor="accounting-dimension-mapping-display-name">
                  <Input
                    id="accounting-dimension-mapping-display-name"
                    value={view.tenantAdministrationProfile.dimensionMappingSetup.displayNameValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateDimensionMappingSetup({ displayName: event.currentTarget.value })}
                  />
                </FormRow>
                <FormRow label="Provider id" labelFor="accounting-dimension-mapping-provider-id">
                  <Input
                    id="accounting-dimension-mapping-provider-id"
                    value={view.tenantAdministrationProfile.dimensionMappingSetup.providerIdValue}
                    onChange={(event) => view.tenantAdministrationProfile.updateDimensionMappingSetup({ providerId: event.currentTarget.value })}
                  />
                </FormRow>
              </div>
              <div className="grid gap-3 lg:grid-cols-2">
                <div className="space-y-2">
                  <ConfigureKeyValueField
                    id="accounting-dimension-mapping-meridian-dimensions-editor"
                    label="Meridian dimensions"
                    value={view.tenantAdministrationProfile.dimensionMappingSetup.meridianDimensionsValue}
                    onChange={(next) => view.tenantAdministrationProfile.updateDimensionMappingSetup({ meridianDimensionsText: next })}
                    keyPlaceholder="fundId"
                    valuePlaceholder="fund-alpha"
                    addLabel="Add dimension"
                    description="Structured key=value dimension entries."
                  />
                  <details className="text-xs">
                    <summary className="cursor-pointer text-muted-foreground">Raw text</summary>
                    <FormRow label="Meridian dimensions" labelFor="accounting-dimension-mapping-meridian-dimensions">
                      <textarea
                        id="accounting-dimension-mapping-meridian-dimensions"
                        value={view.tenantAdministrationProfile.dimensionMappingSetup.meridianDimensionsValue}
                        onChange={(event) => view.tenantAdministrationProfile.updateDimensionMappingSetup({ meridianDimensionsText: event.currentTarget.value })}
                        className="min-h-28 w-full rounded-md border border-input bg-background px-3 py-2 font-mono text-sm text-foreground shadow-sm"
                        placeholder="fundId=fund-alpha&#10;bookId=book-primary&#10;costCenterId=fund-accounting"
                      />
                    </FormRow>
                  </details>
                </div>
                <div className="space-y-2">
                  <ConfigureKeyValueField
                    id="accounting-dimension-mapping-provider-dimensions-editor"
                    label="Provider dimensions"
                    value={view.tenantAdministrationProfile.dimensionMappingSetup.providerDimensionsValue}
                    onChange={(next) => view.tenantAdministrationProfile.updateDimensionMappingSetup({ providerDimensionsText: next })}
                    keyPlaceholder="Class"
                    valuePlaceholder="fund-alpha"
                    addLabel="Add dimension"
                    description="Structured key=value provider dimension entries."
                  />
                  <details className="text-xs">
                    <summary className="cursor-pointer text-muted-foreground">Raw text</summary>
                    <FormRow label="Provider dimensions" labelFor="accounting-dimension-mapping-provider-dimensions">
                      <textarea
                        id="accounting-dimension-mapping-provider-dimensions"
                        value={view.tenantAdministrationProfile.dimensionMappingSetup.providerDimensionsValue}
                        onChange={(event) => view.tenantAdministrationProfile.updateDimensionMappingSetup({ providerDimensionsText: event.currentTarget.value })}
                        className="min-h-28 w-full rounded-md border border-input bg-background px-3 py-2 font-mono text-sm text-foreground shadow-sm"
                        placeholder="Class=fund-alpha&#10;Book=book-primary&#10;Department=fund-accounting"
                      />
                    </FormRow>
                  </details>
                </div>
              </div>
              <FormRow label="Evidence requirement" labelFor="accounting-dimension-mapping-evidence">
                <Input
                  id="accounting-dimension-mapping-evidence"
                  value={view.tenantAdministrationProfile.dimensionMappingSetup.evidenceRequirementValue}
                  onChange={(event) => view.tenantAdministrationProfile.updateDimensionMappingSetup({ evidenceRequirement: event.currentTarget.value })}
                />
              </FormRow>
            </div>
            <FormRow label="Retained setup evidence" labelFor="accounting-tenant-admin-evidence">
              <Input
                id="accounting-tenant-admin-evidence"
                value={view.tenantAdministrationProfile.evidenceValue}
                onChange={(event) => view.tenantAdministrationProfile.updateEvidence(event.currentTarget.value)}
                placeholder="evidence://tenant-admin/setup"
              />
            </FormRow>
            {view.tenantAdministrationProfile.statusText ? (
              <p className={cn("text-sm", view.tenantAdministrationProfile.errorText ? "text-danger" : "text-muted-foreground")}>
                {view.tenantAdministrationProfile.statusText}
              </p>
            ) : null}
            {view.tenantAdministrationProfile.sandboxStatusText ? (
              <p className={cn("text-sm", view.tenantAdministrationProfile.sandboxStatusText.includes("failed") ? "text-danger" : "text-muted-foreground")}>
                {view.tenantAdministrationProfile.sandboxStatusText}
              </p>
            ) : null}
          </div>

          <div className="rounded-lg border border-border/70 bg-background/35 p-3" role="region" aria-label="Accounting external GL provider mapping editor">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <div className="font-semibold text-foreground">{view.externalGlMappingProfile.title}</div>
                <div className="mt-1 font-mono text-xs text-muted-foreground">{view.externalGlMappingProfile.scopeLabel}</div>
              </div>
              <Button
                size="sm"
                disabled={!view.externalGlMappingProfile.canSave}
                disabledReason={view.externalGlMappingProfile.saveDisabledReason}
                busy={view.externalGlMappingProfile.saveBusy}
                busyLabel={view.externalGlMappingProfile.saveButtonLabel}
                onClick={() => void view.externalGlMappingProfile.save()}
              >
                {view.externalGlMappingProfile.saveButtonLabel}
              </Button>
            </div>
            <div className="mt-3 grid gap-3 md:grid-cols-3">
              <FormRow label="Provider" labelFor="accounting-external-gl-mapping-provider">
                <Input
                  id="accounting-external-gl-mapping-provider"
                  value={view.externalGlMappingProfile.providerIdValue}
                  onChange={(event) => view.externalGlMappingProfile.updateProviderId(event.currentTarget.value)}
                />
              </FormRow>
              <FormRow label="Profile" labelFor="accounting-external-gl-mapping-profile">
                <Input
                  id="accounting-external-gl-mapping-profile"
                  value={view.externalGlMappingProfile.profileIdValue}
                  onChange={(event) => view.externalGlMappingProfile.updateProfileId(event.currentTarget.value)}
                />
              </FormRow>
              <FormRow label="Display name" labelFor="accounting-external-gl-mapping-display-name">
                <Input
                  id="accounting-external-gl-mapping-display-name"
                  value={view.externalGlMappingProfile.displayNameValue}
                  onChange={(event) => view.externalGlMappingProfile.updateDisplayName(event.currentTarget.value)}
                />
              </FormRow>
            </div>
            <div className="mt-3 grid gap-3 lg:grid-cols-2">
              <div className="space-y-2">
                <ConfigureKeyValueField
                  id="accounting-external-gl-account-mappings-editor"
                  label="Account mappings"
                  value={view.externalGlMappingProfile.accountMappingsValue}
                  onChange={(next) => view.externalGlMappingProfile.updateAccountMappings(next)}
                  keyPlaceholder="Meridian:Account"
                  valuePlaceholder="external-account-id"
                  addLabel="Add mapping"
                  description="Map each Meridian account to its external GL account id."
                />
                <details className="text-xs">
                  <summary className="cursor-pointer text-muted-foreground">Raw text</summary>
                  <FormRow label="Account mappings" labelFor="accounting-external-gl-account-mappings">
                    <textarea
                      id="accounting-external-gl-account-mappings"
                      value={view.externalGlMappingProfile.accountMappingsValue}
                      onChange={(event) => view.externalGlMappingProfile.updateAccountMappings(event.currentTarget.value)}
                      className="min-h-24 w-full rounded-md border border-input bg-background px-3 py-2 font-mono text-sm text-foreground shadow-sm"
                      placeholder="Meridian:Account=external-account-id"
                    />
                  </FormRow>
                </details>
              </div>
              <FormRow label="Retained mapping evidence" labelFor="accounting-external-gl-mapping-evidence">
                <textarea
                  id="accounting-external-gl-mapping-evidence"
                  value={view.externalGlMappingProfile.evidenceValue}
                  onChange={(event) => view.externalGlMappingProfile.updateEvidence(event.currentTarget.value)}
                  className="min-h-24 w-full rounded-md border border-input bg-background px-3 py-2 font-mono text-sm text-foreground shadow-sm"
                  placeholder="approval:external-gl-mapping:profile-id"
                />
              </FormRow>
            </div>
            <div className="mt-3 grid gap-3 lg:grid-cols-2">
              <div className="space-y-2">
                <ConfigureKeyValueField
                  id="accounting-external-gl-meridian-dimensions-editor"
                  label="Meridian dimensions"
                  value={view.externalGlMappingProfile.meridianDimensionsValue}
                  onChange={(next) => view.externalGlMappingProfile.updateMeridianDimensions(next)}
                  keyPlaceholder="fundId"
                  valuePlaceholder="fund-alpha"
                  addLabel="Add dimension"
                  description="Structured key=value dimension entries."
                />
                <details className="text-xs">
                  <summary className="cursor-pointer text-muted-foreground">Raw text</summary>
                  <FormRow label="Meridian dimensions" labelFor="accounting-external-gl-meridian-dimensions">
                    <textarea
                      id="accounting-external-gl-meridian-dimensions"
                      value={view.externalGlMappingProfile.meridianDimensionsValue}
                      onChange={(event) => view.externalGlMappingProfile.updateMeridianDimensions(event.currentTarget.value)}
                      className="min-h-28 w-full rounded-md border border-input bg-background px-3 py-2 font-mono text-sm text-foreground shadow-sm"
                      placeholder="fundId=fund-alpha&#10;bookId=book-primary&#10;Provider=quickbooks-fixture"
                    />
                  </FormRow>
                </details>
              </div>
              <div className="space-y-2">
                <ConfigureKeyValueField
                  id="accounting-external-gl-provider-dimensions-editor"
                  label="External dimensions"
                  value={view.externalGlMappingProfile.externalDimensionsValue}
                  onChange={(next) => view.externalGlMappingProfile.updateExternalDimensions(next)}
                  keyPlaceholder="Class"
                  valuePlaceholder="fund-alpha"
                  addLabel="Add dimension"
                  description="Structured key=value external dimension entries."
                />
                <details className="text-xs">
                  <summary className="cursor-pointer text-muted-foreground">Raw text</summary>
                  <FormRow label="External dimensions" labelFor="accounting-external-gl-provider-dimensions">
                    <textarea
                      id="accounting-external-gl-provider-dimensions"
                      value={view.externalGlMappingProfile.externalDimensionsValue}
                      onChange={(event) => view.externalGlMappingProfile.updateExternalDimensions(event.currentTarget.value)}
                      className="min-h-28 w-full rounded-md border border-input bg-background px-3 py-2 font-mono text-sm text-foreground shadow-sm"
                      placeholder="Class=fund-alpha&#10;Book=book-primary&#10;customerId=qbo-customer"
                    />
                  </FormRow>
                </details>
              </div>
            </div>
            <label className="mt-3 flex items-center gap-2 text-sm font-semibold text-foreground">
              <input
                type="checkbox"
                checked={view.externalGlMappingProfile.certified}
                onChange={(event) => view.externalGlMappingProfile.updateCertified(event.currentTarget.checked)}
              />
              Certify mapping profile for guarded export readiness
            </label>
            {view.externalGlMappingProfile.mappingRows.length > 0 ? (
              <div className="mt-3 grid gap-2 md:grid-cols-3" role="region" aria-label="Retained external GL mapping profiles">
                {view.externalGlMappingProfile.mappingRows.map((row) => (
                  <div key={row.id} className={cn("rounded-md border px-3 py-2", accountingToolingBorderClass(row.tone))}>
                    <div className="text-sm font-semibold text-foreground">{row.label}</div>
                    <div className="mt-1 text-xs text-muted-foreground">{row.statusLabel}</div>
                  </div>
                ))}
              </div>
            ) : null}
            {view.externalGlMappingProfile.statusText ? (
              <p className={cn("mt-3 text-sm", view.externalGlMappingProfile.errorText ? "text-danger" : "text-muted-foreground")}>
                {view.externalGlMappingProfile.statusText}
              </p>
            ) : null}
          </div>

          {view.productionReadiness.migrationPlanRows.length > 0 ? (
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3" role="region" aria-label="Accounting migration rollout plan">
              {view.productionReadiness.migrationPlanRows.map((item) => (
                <div key={item.id} className={cn("rounded-md border px-3 py-3", accountingToolingBorderClass(item.tone))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div>
                      <div className="font-semibold text-foreground">{item.title}</div>
                      <div className="mt-1 font-mono text-[11px] text-muted-foreground">{item.id}</div>
                    </div>
                    <Badge variant={accountingToolingBadgeVariant(item.tone)}>{item.statusLabel}</Badge>
                  </div>
                  <div className="mt-2 font-mono text-[11px] text-muted-foreground">{item.certificationLabel} | {item.latestRunLabel}</div>
                  <div className="mt-1 text-[11px] text-muted-foreground">{item.scopeLabel}</div>
                  <div className="mt-2 font-mono text-[11px] text-muted-foreground">{item.metricsLabel} | {item.evidenceLabel}</div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{item.requiredAction}</p>
                  <div className="mt-2 text-[11px] text-muted-foreground">{item.blockingIssueLabel}</div>
                </div>
              ))}
            </div>
          ) : null}

          {view.productionReadiness.migrationArtifactRows.length > 0 ? (
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3" role="region" aria-label="Retained accounting migration run artifacts">
              {view.productionReadiness.migrationArtifactRows.map((artifact) => (
                <div key={artifact.id} className={cn("rounded-md border px-3 py-3", accountingToolingBorderClass(artifact.tone))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div>
                      <div className="font-semibold text-foreground">{artifact.kindLabel}</div>
                      <div className="mt-1 font-mono text-[11px] text-muted-foreground">{artifact.id}</div>
                    </div>
                    <Badge variant={accountingToolingBadgeVariant(artifact.tone)}>{artifact.statusLabel}</Badge>
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{artifact.title}</p>
                  <div className="mt-2 font-mono text-[11px] text-muted-foreground">{artifact.recordCountLabel} | {artifact.issueCountLabel} | {artifact.evidenceLabel}</div>
                  <div className="mt-1 text-[11px] text-muted-foreground">{artifact.detail}</div>
                </div>
              ))}
            </div>
          ) : null}

          {view.productionReadiness.migrationWorkerPlanRows.length > 0 ? (
            <div className="space-y-2" role="region" aria-label="Retained accounting migration worker plans">
              <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                {view.productionReadiness.migrationWorkerPlanSummaryLabel}
              </div>
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                {view.productionReadiness.migrationWorkerPlanRows.map((plan) => (
                  <div key={plan.id} className={cn("rounded-md border px-3 py-3", accountingToolingBorderClass(plan.tone))}>
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <div>
                        <div className="font-semibold text-foreground">{plan.kindLabel}</div>
                        <div className="mt-1 font-mono text-[11px] text-muted-foreground">{plan.id}</div>
                      </div>
                      <Badge variant={accountingToolingBadgeVariant(plan.tone)}>{plan.tone === "success" ? "Reconciled" : "Mismatch"}</Badge>
                    </div>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{plan.title}</p>
                    <div className="mt-2 font-mono text-[11px] text-muted-foreground">{plan.countLabel} | {plan.evidenceLabel}</div>
                    <div className="mt-1 text-[11px] text-muted-foreground">{plan.scopeLabel}</div>
                    <div className="mt-1 text-[11px] text-muted-foreground">{plan.detail}</div>
                  </div>
                ))}
              </div>
            </div>
          ) : null}

          {view.productionReadiness.components.length > 0 ? (
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4" aria-label="Accounting production readiness components">
              {view.productionReadiness.components.map((component) => (
                <div key={component.id} className={cn("rounded-md border px-3 py-3", accountingToolingBorderClass(component.tone))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="font-semibold text-foreground">{component.label}</div>
                    <Badge variant={accountingToolingBadgeVariant(component.tone)}>{component.statusLabel}</Badge>
                  </div>
                  <div className="mt-2 font-mono text-xs text-muted-foreground">{component.scoreLabel} | {component.issueCountLabel}</div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{component.summary}</p>
                  <div className="mt-2 font-mono text-[11px] text-muted-foreground">{component.evidenceLabel} | {component.routeLabel}</div>
                </div>
              ))}
            </div>
          ) : null}

          {view.productionReadiness.blockerIssues.length > 0 ? (
            <div className="space-y-2" aria-label="Accounting production readiness blockers">
              {view.productionReadiness.blockerIssues.map((issue) => (
                <div key={issue.id} className={cn("rounded-md border px-3 py-2 text-sm", accountingToolingBorderClass(issue.tone))}>
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="font-semibold text-foreground">{issue.label}</span>
                    <Badge variant={accountingToolingBadgeVariant(issue.tone)}>{issue.tone === "danger" ? "Blocker" : "Review"}</Badge>
                  </div>
                  <p className="mt-1 text-muted-foreground">{issue.message}</p>
                  <p className="mt-1 text-xs text-muted-foreground">{issue.suggestedAction} | {issue.evidenceLabel}</p>
                </div>
              ))}
            </div>
          ) : (
            <p role="status" className="rounded-md border border-success/30 bg-success/10 px-3 py-2 text-sm text-success">No production-readiness blockers returned by the shared accounting control-plane assessment.</p>
          )}
        </CardContent>
      </Card>
  );
}
