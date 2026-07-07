import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Briefcase, ClipboardList, Pencil, Plus, Save, Trash2, X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FieldSupportText, joinDescribedByIds } from "@/components/ui/field-support";
import { Input } from "@/components/ui/input";
import { DenseRowDetailPanel } from "@/components/meridian/dense-row-detail-accessibility";
import { DenseDataTable, EntitySummary, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { getOperatorOverrides as defaultGetOperatorOverrides, patchOperatorOverrides as defaultPatchOperatorOverrides } from "@/lib/api";
import { cn } from "@/lib/utils";
import {
  buildSecurityDetailsViewModel,
  buildLotsTrackerViewModel,
  parseLotNumber,
  type SecurityDetailFieldDef,
  type SecurityDetailFieldViewModel,
  type LotsTrackerDraftFieldViewModel,
  type LotsTrackerRowViewModel,
  type SecurityLot,
  type SecurityDetailsServerStatus
} from "./security-details-tracker.view-model";
import type {
  OperatorOverridesDto,
  OperatorOverridesPatchRequest,
  SecurityIdentityDrillIn,
  SecurityMasterEntry,
  TradingParameters
} from "@/types";

export interface OperatorOverridesService {
  get: (securityId: string) => Promise<OperatorOverridesDto>;
  patch: (securityId: string, request: OperatorOverridesPatchRequest) => Promise<OperatorOverridesDto>;
}

const defaultOperatorOverridesService: OperatorOverridesService = {
  get: defaultGetOperatorOverrides,
  patch: defaultPatchOperatorOverrides
};

const STORAGE_PREFIX_OVERRIDES = "meridian.security.overrides.";
const STORAGE_PREFIX_LOTS = "meridian.security.lots.";
const OVERRIDES_CHANGED_EVENT = "meridian:security-overrides-changed";

interface OverridesChangedDetail {
  securityId: string;
}

function dispatchOverridesChanged(securityId: string): void {
  if (typeof window === "undefined") return;
  try {
    window.dispatchEvent(new CustomEvent<OverridesChangedDetail>(OVERRIDES_CHANGED_EVENT, { detail: { securityId } }));
  } catch {
    // ignore environments without CustomEvent
  }
}

function sanitiseOverrideValues(values: Record<string, string> | null | undefined): Record<string, string> {
  if (!values) return {};
  const result: Record<string, string> = {};
  for (const [key, value] of Object.entries(values)) {
    if (typeof key !== "string" || key.length === 0) continue;
    if (typeof value !== "string") continue;
    result[key] = value;
  }
  return result;
}

function safeLocalStorage(): Storage | null {
  try {
    if (typeof window === "undefined") return null;
    return window.localStorage;
  } catch {
    return null;
  }
}

function storageKey(prefix: string, securityId: string): string {
  return `${prefix}${securityId}`;
}

function loadOverrides(securityId: string): Record<string, string> {
  const ls = safeLocalStorage();
  if (!ls) return {};
  try {
    const raw = ls.getItem(storageKey(STORAGE_PREFIX_OVERRIDES, securityId));
    if (!raw) return {};
    const parsed = JSON.parse(raw);
    return parsed && typeof parsed === "object" ? (parsed as Record<string, string>) : {};
  } catch {
    return {};
  }
}

function saveOverrides(securityId: string, overrides: Record<string, string>): void {
  const ls = safeLocalStorage();
  if (!ls) return;
  try {
    ls.setItem(storageKey(STORAGE_PREFIX_OVERRIDES, securityId), JSON.stringify(overrides));
  } catch {
    // ignore quota / serialization errors
  }
}

export interface SecurityDetailsPanelProps {
  entry: SecurityMasterEntry | null;
  identity: SecurityIdentityDrillIn | null;
  tradingParameters: TradingParameters | null;
  overridesService?: OperatorOverridesService;
}

export function SecurityDetailsPanel({
  entry,
  identity,
  tradingParameters,
  overridesService = defaultOperatorOverridesService
}: SecurityDetailsPanelProps) {
  const securityId = identity?.securityId ?? entry?.securityId ?? null;
  const [overrides, setOverrides] = useState<Record<string, string>>({});
  const [editingKey, setEditingKey] = useState<string | null>(null);
  const [draftValue, setDraftValue] = useState<string>("");
  const [serverStatus, setServerStatus] = useState<SecurityDetailsServerStatus>("idle");
  const [serverErrorText, setServerErrorText] = useState<string | null>(null);
  const [updatedBy, setUpdatedBy] = useState<string | null>(null);
  const [updatedAt, setUpdatedAt] = useState<string | null>(null);
  const loadGenerationRef = useRef(0);
  // Serializes optimistic override patches: only the most recently issued patch is
  // allowed to apply its server response, so a slow/failed earlier patch can neither
  // clobber a newer edit nor mask its own failure.
  const persistGenerationRef = useRef(0);

  useEffect(() => {
    if (!securityId) {
      setOverrides({});
      setEditingKey(null);
      setServerStatus("idle");
      setServerErrorText(null);
      setUpdatedBy(null);
      setUpdatedAt(null);
      return;
    }

    setEditingKey(null);
    setServerErrorText(null);
    setOverrides(loadOverrides(securityId));
    setServerStatus("loading");

    const generation = ++loadGenerationRef.current;
    overridesService.get(securityId)
      .then((dto) => {
        if (loadGenerationRef.current !== generation) return;
        const next = sanitiseOverrideValues(dto.values);
        setOverrides(next);
        saveOverrides(securityId, next);
        setUpdatedBy(dto.updatedBy || null);
        setUpdatedAt(dto.updatedAt || null);
        setServerStatus("synced");
        dispatchOverridesChanged(securityId);
      })
      .catch((err) => {
        if (loadGenerationRef.current !== generation) return;
        setServerStatus("offline");
        setServerErrorText(err instanceof Error ? err.message : "Failed to load server overrides.");
      });
  }, [securityId, overridesService]);

  const vm = useMemo(
    () => buildSecurityDetailsViewModel({
      entry,
      identity,
      tradingParameters,
      overrides,
      editingKey,
      serverStatus,
      serverErrorText,
      updatedBy,
      updatedAt
    }),
    [editingKey, entry, identity, overrides, serverErrorText, serverStatus, tradingParameters, updatedAt, updatedBy]
  );

  const beginEdit = useCallback((field: SecurityDetailFieldDef, currentValue: string) => {
    setEditingKey(field.key);
    setDraftValue(currentValue);
  }, []);

  const cancelEdit = useCallback(() => {
    setEditingKey(null);
    setDraftValue("");
  }, []);

  const persistPatch = useCallback(async (next: Record<string, string>, patch: OperatorOverridesPatchRequest) => {
    if (!securityId) return;
    // Capture the last-known-good state so a rejected patch can be rolled back instead
    // of leaving an optimistic value the server never accepted.
    const previous = overrides;
    const generation = ++persistGenerationRef.current;
    setOverrides(next);
    saveOverrides(securityId, next);
    dispatchOverridesChanged(securityId);
    setServerStatus("saving");
    setServerErrorText(null);
    try {
      const dto = await overridesService.patch(securityId, patch);
      // A newer patch has superseded this one; discard the stale response so it cannot
      // overwrite the newer optimistic value.
      if (persistGenerationRef.current !== generation) return;
      const merged = sanitiseOverrideValues(dto.values);
      setOverrides(merged);
      saveOverrides(securityId, merged);
      setUpdatedBy(dto.updatedBy || null);
      setUpdatedAt(dto.updatedAt || null);
      setServerStatus("synced");
      dispatchOverridesChanged(securityId);
    } catch (err) {
      if (persistGenerationRef.current !== generation) return;
      // Roll the optimistic change back to the last-known-good values so a failed patch
      // is not silently presented as applied.
      setOverrides(previous);
      saveOverrides(securityId, previous);
      dispatchOverridesChanged(securityId);
      setServerStatus("offline");
      setServerErrorText(err instanceof Error ? err.message : "Failed to save override on the server. Reverted to the last saved values.");
    }
  }, [overrides, overridesService, securityId]);

  const commitEdit = useCallback((field: SecurityDetailFieldDef) => {
    if (!securityId) return;
    const next = { ...overrides };
    const trimmed = draftValue.trim();
    const patch: OperatorOverridesPatchRequest = trimmed.length === 0
      ? { removeKeys: [field.key] }
      : { setValues: { [field.key]: trimmed } };
    if (trimmed.length === 0) {
      delete next[field.key];
    } else {
      next[field.key] = trimmed;
    }
    setEditingKey(null);
    setDraftValue("");
    void persistPatch(next, patch);
  }, [draftValue, overrides, persistPatch, securityId]);

  const clearOverride = useCallback((field: SecurityDetailFieldDef) => {
    if (!securityId) return;
    if (!(field.key in overrides)) return;
    const next = { ...overrides };
    delete next[field.key];
    void persistPatch(next, { removeKeys: [field.key] });
  }, [overrides, persistPatch, securityId]);

  if (!securityId) {
    return null;
  }

  const overrideCount = vm.overrideCount;

  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <ClipboardList className="h-4 w-4 text-primary" />
          Security details
          {overrideCount > 0 && (
            <Badge variant="outline" className="ml-2 font-mono text-[10px]">
              {overrideCount} override{overrideCount === 1 ? "" : "s"}
            </Badge>
          )}
        </CardTitle>
        <CardDescription>
          Extended reference data for <span className="font-mono">{securityId}</span>. Operator overrides sync to the
          server when available and fall back to local storage when offline.
        </CardDescription>
        <div className="mt-2 flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
          {vm.syncStatus ? (
            <span
              className={cn("rounded-sm border px-2 py-0.5 font-mono text-[10px] uppercase tracking-[0.12em]", vm.syncStatus.className)}
              aria-label={vm.syncStatus.ariaLabel}
            >
              {vm.syncStatus.label}
            </span>
          ) : null}
          {vm.hiddenOverridesLabel && vm.hiddenOverridesTitle && (
            <Badge
              variant="warning"
              title={vm.hiddenOverridesTitle}
            >
              {vm.hiddenOverridesLabel}
            </Badge>
          )}
          {vm.updatedLabel && (
            <span className="font-mono">
              {vm.updatedLabel}
            </span>
          )}
          {vm.serverError && (
            <span role="alert" aria-label={vm.serverError.ariaLabel} className="font-mono text-warning">
              {vm.serverError.text}
            </span>
          )}
        </div>
      </CardHeader>
      <CardContent className="space-y-6">
        {vm.groups.map((group) => (
          <section key={group.id} aria-label={group.title} className="space-y-2">
            <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{group.title}</div>
            <dl className="grid gap-2 sm:grid-cols-2">
              {group.fields.map((field) => {
                return (
                  <div
                    key={field.key}
                    className={cn(
                      "grid grid-cols-[minmax(0,0.7fr)_minmax(0,1fr)_auto] items-start gap-3 rounded-md border px-3 py-2",
                      field.isOverridden ? "border-primary/40 bg-primary/5" : "border-border/60 bg-secondary/25"
                    )}
                  >
                    <dt className="min-w-0 text-xs text-muted-foreground">
                      {field.label}
                      {field.isOverridden && (
                        <span className="ml-1 font-mono text-[9px] uppercase tracking-[0.12em] text-primary">override</span>
                      )}
                    </dt>
                    <dd className="min-w-0 break-words font-mono text-xs text-foreground">
                      {field.isEditing ? (
                        <SecurityDetailFieldEditor
                          field={field}
                          value={draftValue}
                          onChange={setDraftValue}
                          onSubmit={() => commitEdit(field.def)}
                          onCancel={cancelEdit}
                        />
                      ) : (
                        <span className={cn(field.value === "" ? "text-muted-foreground" : "")}>{field.displayValue}</span>
                      )}
                    </dd>
                    <div className="flex shrink-0 items-center gap-1">
                      {field.isEditing ? (
                        <>
                          <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            aria-label={field.saveCommand.ariaLabel}
                            disabled={field.saveCommand.disabled}
                            disabledReason={field.saveCommand.disabledReason}
                            onClick={() => commitEdit(field.def)}
                          >
                            <Save className="h-3.5 w-3.5" />
                          </Button>
                          <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            aria-label={field.cancelCommand.ariaLabel}
                            disabled={field.cancelCommand.disabled}
                            disabledReason={field.cancelCommand.disabledReason}
                            onClick={cancelEdit}
                          >
                            <X className="h-3.5 w-3.5" />
                          </Button>
                        </>
                      ) : (
                        <>
                          <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            aria-label={field.editCommand.ariaLabel}
                            disabled={field.editCommand.disabled}
                            disabledReason={field.editCommand.disabledReason}
                            onClick={() => beginEdit(field.def, field.isOverridden ? field.overrideValue ?? "" : "")}
                          >
                            <Pencil className="h-3.5 w-3.5" />
                          </Button>
                          {field.clearCommand && (
                            <Button
                              type="button"
                              size="sm"
                              variant="ghost"
                              aria-label={field.clearCommand.ariaLabel}
                              disabled={field.clearCommand.disabled}
                              disabledReason={field.clearCommand.disabledReason}
                              onClick={() => clearOverride(field.def)}
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                            </Button>
                          )}
                        </>
                      )}
                    </div>
                  </div>
                );
              })}
            </dl>
          </section>
        ))}
      </CardContent>
    </Card>
  );
}

interface SecurityDetailFieldEditorProps {
  field: SecurityDetailFieldViewModel;
  value: string;
  onChange: (value: string) => void;
  onSubmit: () => void;
  onCancel: () => void;
}

function SecurityDetailFieldEditor({ field, value, onChange, onSubmit, onCancel }: SecurityDetailFieldEditorProps) {
  const def = field.def;
  const editor = field.editor;
  const disabledReasonId = `${editor.id}-disabled-reason`;
  const baseClass = "w-full rounded-md border border-border bg-background px-2 py-1 text-xs focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40";
  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement | HTMLSelectElement>) => {
    if (e.key === "Enter") {
      e.preventDefault();
      onSubmit();
    } else if (e.key === "Escape") {
      e.preventDefault();
      onCancel();
    }
  };
  if (def.kind === "select" && def.options) {
    return (
      <span className="block space-y-1">
        <select
          id={editor.id}
          className={baseClass}
          value={value}
          autoFocus
          disabled={editor.disabled}
          onChange={(e) => onChange(e.target.value)}
          onKeyDown={handleKeyDown}
          aria-label={editor.ariaLabel}
          aria-describedby={joinDescribedByIds(editor.describedBy, disabledReasonId)}
        >
          <option value="">—</option>
          {def.options.map((opt) => (
            <option key={opt} value={opt}>{opt}</option>
          ))}
        </select>
        <FieldSupportText
          helpId={editor.helperId}
          helpText={editor.helperText}
          helpClassName="block text-[11px] leading-4"
          disabledReason={editor.disabledReason}
          disabledReasonId={disabledReasonId}
          disabledReasonClassName="block"
        />
      </span>
    );
  }
  const inputType = def.kind === "number" ? "number" : def.kind === "date" ? "date" : "text";
  return (
    <span className="block space-y-1">
      <input
        id={editor.id}
        type={inputType}
        className={baseClass}
        value={value}
        autoFocus
        disabled={editor.disabled}
        placeholder={def.placeholder}
        onChange={(e) => onChange(e.target.value)}
        onKeyDown={handleKeyDown}
        aria-label={editor.ariaLabel}
        aria-describedby={joinDescribedByIds(editor.describedBy, disabledReasonId)}
        step={def.kind === "number" ? "any" : undefined}
      />
      <FieldSupportText
        helpId={editor.helperId}
        helpText={editor.helperText}
        helpClassName="block text-[11px] leading-4"
        disabledReason={editor.disabledReason}
        disabledReasonId={disabledReasonId}
        disabledReasonClassName="block"
      />
    </span>
  );
}

function loadLots(securityId: string): SecurityLot[] {
  const ls = safeLocalStorage();
  if (!ls) return [];
  try {
    const raw = ls.getItem(storageKey(STORAGE_PREFIX_LOTS, securityId));
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((l): l is SecurityLot =>
      l && typeof l.lotId === "string"
      && typeof l.tradeDate === "string"
      && typeof l.quantity === "number"
      && typeof l.price === "number"
    ).map((l) => ({ ...l, fees: l.fees ?? 0, note: l.note ?? "" }));
  } catch {
    return [];
  }
}

function saveLots(securityId: string, lots: SecurityLot[]): void {
  const ls = safeLocalStorage();
  if (!ls) return;
  try {
    ls.setItem(storageKey(STORAGE_PREFIX_LOTS, securityId), JSON.stringify(lots));
  } catch {
    // ignore quota errors
  }
}

function newLotId(): string {
  return `lot-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}

function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

export interface LotsTrackerPanelProps {
  securityId: string | null;
  currency: string | null;
}

function readMarketPriceOverride(securityId: string | null): number | null {
  if (!securityId) return null;
  const overrides = loadOverrides(securityId);
  const raw = overrides.marketPrice;
  if (raw === undefined || raw === "") return null;
  const num = Number(raw);
  return Number.isFinite(num) ? num : null;
}

export function LotsTrackerPanel({ securityId, currency }: LotsTrackerPanelProps) {
  const initialLots = useMemo(() => securityId ? loadLots(securityId) : [], [securityId]);
  const [lots, setLots] = useState<SecurityLot[]>(initialLots);
  const [marketPriceOverride, setMarketPriceOverride] = useState<number | null>(() => readMarketPriceOverride(securityId));
  const [selectedLotId, setSelectedLotId] = useState<string | null>(initialLots[0]?.lotId ?? null);
  const [draftDate, setDraftDate] = useState(todayIso());
  const [draftQty, setDraftQty] = useState("");
  const [draftPrice, setDraftPrice] = useState("");
  const [draftFees, setDraftFees] = useState("");
  const [draftNote, setDraftNote] = useState("");
  const [pendingRemoveLotId, setPendingRemoveLotId] = useState<string | null>(null);

  useEffect(() => {
    if (!securityId) {
      setLots([]);
      setMarketPriceOverride(null);
      setSelectedLotId(null);
      setPendingRemoveLotId(null);
      return;
    }
    const loadedLots = loadLots(securityId);
    setLots(loadedLots);
    setSelectedLotId(loadedLots[0]?.lotId ?? null);
    setMarketPriceOverride(readMarketPriceOverride(securityId));
    setPendingRemoveLotId(null);
  }, [securityId]);

  useEffect(() => {
    if (!securityId || typeof window === "undefined") return;
    const handler = (event: Event) => {
      const detail = (event as CustomEvent<OverridesChangedDetail>).detail;
      if (!detail || detail.securityId === securityId) {
        setMarketPriceOverride(readMarketPriceOverride(securityId));
      }
    };
    window.addEventListener(OVERRIDES_CHANGED_EVENT, handler);
    return () => window.removeEventListener(OVERRIDES_CHANGED_EVENT, handler);
  }, [securityId]);

  const vm = useMemo(() => {
    if (!securityId) return null;
    return buildLotsTrackerViewModel({
      securityId,
      currency,
      lots,
      marketPriceOverride,
      draft: {
        tradeDate: draftDate,
        quantity: draftQty,
        price: draftPrice,
        fees: draftFees,
        note: draftNote
      },
      selectedLotId,
      pendingRemoveLotId
    });
  }, [currency, draftDate, draftFees, draftNote, draftPrice, draftQty, lots, marketPriceOverride, pendingRemoveLotId, securityId, selectedLotId]);

  const addLot = useCallback(() => {
    if (!securityId || !vm || vm.addCommand.disabled) return;
    const quantity = parseLotNumber(draftQty);
    const price = parseLotNumber(draftPrice);
    const lot: SecurityLot = {
      lotId: newLotId(),
      tradeDate: draftDate,
      quantity,
      price,
      fees: parseLotNumber(draftFees),
      note: draftNote.trim()
    };
    const next = [...lots, lot].sort((a, b) => a.tradeDate.localeCompare(b.tradeDate));
    setLots(next);
    setSelectedLotId(lot.lotId);
    saveLots(securityId, next);
    setDraftQty("");
    setDraftPrice("");
    setDraftFees("");
    setDraftNote("");
    setDraftDate(todayIso());
    setPendingRemoveLotId(null);
  }, [draftDate, draftFees, draftNote, draftPrice, draftQty, lots, securityId, vm]);

  const removeLot = useCallback((lotId: string) => {
    if (!securityId) return;
    if (pendingRemoveLotId !== lotId) {
      setPendingRemoveLotId(lotId);
      setSelectedLotId(lotId);
      return;
    }
    const next = lots.filter((l) => l.lotId !== lotId);
    setLots(next);
    setSelectedLotId((current) => current === lotId ? next[0]?.lotId ?? null : current);
    setPendingRemoveLotId(null);
    saveLots(securityId, next);
  }, [lots, pendingRemoveLotId, securityId]);

  if (!securityId || !vm) {
    return null;
  }

  const lotColumns: DenseDataTableColumn<LotsTrackerRowViewModel>[] = [
    {
      id: "trade-date",
      label: "Trade date",
      render: (row) => <span className="font-mono text-muted-foreground">{row.tradeDateLabel}</span>
    },
    {
      id: "quantity",
      label: "Quantity",
      align: "right",
      render: (row) => <span className="font-mono tabular-nums text-foreground">{row.quantityLabel}</span>
    },
    {
      id: "price",
      label: "Price",
      align: "right",
      render: (row) => <span className="font-mono tabular-nums text-foreground">{row.priceLabel}</span>
    },
    {
      id: "fees",
      label: "Fees",
      align: "right",
      render: (row) => <span className="font-mono tabular-nums text-muted-foreground">{row.feesLabel}</span>
    },
    {
      id: "cost",
      label: "Cost",
      align: "right",
      render: (row) => <span className="font-mono tabular-nums text-foreground">{row.costLabel}</span>
    },
    {
      id: "note",
      label: "Note",
      render: (row) => <span className="text-muted-foreground">{row.noteLabel}</span>
    },
    {
      id: "actions",
      label: "",
      align: "right",
      render: (row) => (
        <Button
          type="button"
          size="sm"
          variant={row.removeConfirmationPending ? "secondary" : "ghost"}
          aria-label={row.removeAriaLabel}
          onClick={() => removeLot(row.lotId)}
        >
          <Trash2 className="h-3.5 w-3.5" />
          {row.removeConfirmationPending && <span className="ml-1">{row.removeLabel}</span>}
        </Button>
      )
    }
  ];

  return (
    <Card className="panel-surface">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Briefcase className="h-4 w-4 text-primary" />
          {vm.title}
        </CardTitle>
        <CardDescription>
          {vm.description}
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div role="group" aria-label={vm.formLabel} className="grid gap-2 sm:grid-cols-6">
          <LotDraftField
            field={vm.draftFields.tradeDate}
            value={draftDate}
            className="sm:col-span-1"
            onChange={(value) => {
              setDraftDate(value);
              setPendingRemoveLotId(null);
            }}
          />
          <LotDraftField
            field={vm.draftFields.quantity}
            value={draftQty}
            className="sm:col-span-1"
            onChange={(value) => {
              setDraftQty(value);
              setPendingRemoveLotId(null);
            }}
          />
          <LotDraftField
            field={vm.draftFields.price}
            value={draftPrice}
            className="sm:col-span-1"
            onChange={(value) => {
              setDraftPrice(value);
              setPendingRemoveLotId(null);
            }}
          />
          <LotDraftField
            field={vm.draftFields.fees}
            value={draftFees}
            className="sm:col-span-1"
            onChange={(value) => {
              setDraftFees(value);
              setPendingRemoveLotId(null);
            }}
          />
          <LotDraftField
            field={vm.draftFields.note}
            value={draftNote}
            className="sm:col-span-2"
            onChange={(value) => {
              setDraftNote(value);
              setPendingRemoveLotId(null);
            }}
          />
        </div>
        <p
          id={vm.draftStatus.id}
          role={vm.draftStatus.role}
          className={cn(
            "rounded-md border px-3 py-2 text-xs leading-5",
            vm.draftStatus.tone === "success"
              ? "border-success/30 bg-success/10 text-success"
              : "border-warning/30 bg-warning/10 text-warning"
          )}
        >
          {vm.draftStatus.text}
        </p>
        <div>
          <Button
            type="button"
            size="sm"
            onClick={addLot}
            disabled={vm.addCommand.disabled}
            disabledReason={vm.addCommand.disabledReason}
            aria-describedby={vm.draftStatus.id}
            aria-label={vm.addCommand.ariaLabel}
          >
            <Plus className="mr-1 h-3.5 w-3.5" />
            {vm.addCommand.label}
          </Button>
        </div>

        <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
          {vm.metrics.map((metric) => (
            <LotsMetric key={metric.id} label={metric.label} value={metric.value} tone={metric.tone} />
          ))}
        </div>

        {vm.rows.length === 0 ? (
          <p role="status" className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">
            {vm.emptyText}
          </p>
        ) : (
          <>
            <DenseDataTable
              columns={lotColumns}
              rows={vm.rows}
              getRowId={(row) => row.lotId}
              getRowAriaLabel={(row) => row.ariaLabel}
              getRowSelectAriaLabel={(row) => row.selectAriaLabel}
              getRowAriaControls={(row) => row.detailPanelId}
              getRowAriaExpanded={(row) => row.expanded}
              selectedRowId={vm.selectedLotId}
              onRowSelect={(row) => {
                setSelectedLotId(row.lotId);
                setPendingRemoveLotId(null);
              }}
              emptyText={vm.emptyText}
              ariaLabel={vm.tableLabel}
              caption={vm.tableCaption}
            />
            {vm.selectedDetail ? (
              <DenseRowDetailPanel
                id={vm.selectedDetail.panelId}
                ariaLabel={vm.selectedDetail.ariaLabel}
                className="min-w-0"
              >
                <EntitySummary
                  eyebrow={vm.selectedDetail.eyebrow}
                  title={vm.selectedDetail.title}
                  subtitle={vm.selectedDetail.subtitle}
                  description={vm.selectedDetail.description}
                  status={<Badge variant={vm.selectedDetail.statusBadgeVariant} dot>{vm.selectedDetail.statusLabel}</Badge>}
                  fields={vm.selectedDetail.fields}
                  ariaLabel={`${vm.selectedDetail.ariaLabel} summary`}
                />
              </DenseRowDetailPanel>
            ) : null}
          </>
        )}
      </CardContent>
    </Card>
  );
}

function LotDraftField({
  field,
  value,
  onChange,
  className
}: {
  field: LotsTrackerDraftFieldViewModel;
  value: string;
  onChange: (value: string) => void;
  className?: string;
}) {
  return (
    <div className={cn("min-w-0 space-y-1", className)}>
      <label htmlFor={field.id} className="text-[10px] font-semibold uppercase tracking-[0.12em] text-muted-foreground">
        {field.label}
      </label>
      <Input
        id={field.id}
        type={field.type}
        step={field.step}
        value={value}
        placeholder={field.placeholder}
        error={field.invalid}
        aria-describedby={field.describedBy}
        aria-errormessage={field.errorText ? field.errorId : undefined}
        className="min-h-8 px-2 py-1 text-xs"
        onChange={(event) => onChange(event.target.value)}
      />
      <p id={field.helperId} className="text-[11px] leading-4 text-muted-foreground">
        {field.helperText}
      </p>
      {field.errorText ? (
        <p id={field.errorId} className="text-[11px] leading-4 text-danger">
          {field.errorText}
        </p>
      ) : null}
    </div>
  );
}

function LotsMetric({ label, value, tone }: { label: string; value: string; tone?: "success" | "danger" }) {
  return (
    <div className="rounded-md border border-border/60 bg-secondary/25 px-3 py-2">
      <div className="text-[10px] font-semibold uppercase tracking-[0.12em] text-muted-foreground">{label}</div>
      <div className={cn(
        "mt-1 font-mono text-sm font-semibold tabular-nums",
        tone === "success" ? "text-success" : tone === "danger" ? "text-danger" : "text-foreground"
      )}>
        {value}
      </div>
    </div>
  );
}
