import { useEffect, useMemo, useState } from "react";
import {
  AlarmClock,
  Bell,
  BellOff,
  BellRing,
  Check,
  CheckCheck,
  LineChart,
  Plus,
  RotateCcw,
  Trash2,
  X
} from "lucide-react";
import { Link, useSearchParams } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { describeCondition } from "@/lib/price-alerts/evaluator";
import { usePriceAlerts } from "@/lib/price-alerts/service";
import type { PriceAlert, PriceAlertCondition, PriceAlertField } from "@/lib/price-alerts/types";
import {
  DEFAULT_PRICE_ALERT_FORM,
  PRICE_ALERT_CONDITION_OPTIONS,
  PRICE_ALERT_FIELD_OPTIONS,
  formatPriceAlertPrice,
  formatPriceAlertTimestamp,
  priceAlertDraftFromForm,
  validatePriceAlertForm,
  type PriceAlertFormState
} from "@/screens/price-alerts-screen.view-model";

const SNOOZE_MINUTES = 30;

export function PriceAlertsScreen() {
  const alerts = usePriceAlerts();
  const [searchParams, setSearchParams] = useSearchParams();
  const [form, setForm] = useState<PriceAlertFormState>(() => readFormFromSearchParams(searchParams));
  const [submitFeedback, setSubmitFeedback] = useState<string | null>(null);

  useEffect(() => {
    const seededSymbol = searchParams.get("symbol");
    if (seededSymbol) {
      setForm((current) => ({ ...current, symbol: seededSymbol.toUpperCase() }));
      const next = new URLSearchParams(searchParams);
      next.delete("symbol");
      setSearchParams(next, { replace: true });
    }
  }, [searchParams, setSearchParams]);

  const validation = useMemo(() => validatePriceAlertForm(form), [form]);
  const watchedSymbolCount = alerts.enabledCount;

  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const draft = priceAlertDraftFromForm(form);
    if (!draft) {
      return;
    }
    alerts.createAlert(draft);
    setForm({ ...DEFAULT_PRICE_ALERT_FORM, condition: form.condition, field: form.field });
    setSubmitFeedback(`Alert set: ${draft.symbol} ${describeCondition(draft.condition, draft.threshold, draft.field)}`);
  };

  const handleRequestNotifications = async () => {
    await alerts.requestNotificationPermission();
  };

  const sortedAlerts = useMemo(() => {
    const list = [...alerts.state.alerts];
    list.sort((a, b) => {
      if (a.enabled !== b.enabled) {
        return a.enabled ? -1 : 1;
      }
      return a.symbol.localeCompare(b.symbol) || a.threshold - b.threshold;
    });
    return list;
  }, [alerts.state.alerts]);

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <div className="eyebrow-label">Data Lane</div>
          <CardTitle className="flex items-center gap-2">
            <BellRing className="h-5 w-5 text-primary" aria-hidden="true" />
            Price alerts
          </CardTitle>
          <CardDescription>
            Get notified when a symbol crosses a price. Alerts are evaluated against live quotes every {Math.round(5_000 / 1000)}s while this tab is open.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="grid gap-3 sm:grid-cols-3">
            <SummaryStat label="Active alerts" value={String(watchedSymbolCount)} tone={watchedSymbolCount > 0 ? "default" : "muted"} />
            <SummaryStat label="Unacknowledged triggers" value={String(alerts.unacknowledgedCount)} tone={alerts.unacknowledgedCount > 0 ? "warning" : "muted"} />
            <SummaryStat label="Last poll" value={formatPriceAlertTimestamp(alerts.lastPollAt)} tone="muted" />
          </div>
          {alerts.pollErrorMessage ? (
            <p role="alert" className="mt-3 rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">
              Quote refresh failed: {alerts.pollErrorMessage}
            </p>
          ) : null}
          {alerts.notificationPermission === "default" ? (
            <div className="mt-3 flex flex-wrap items-center justify-between gap-2 rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
              <span>Browser notifications are off. You'll only see alerts when this tab is in the foreground.</span>
              <Button type="button" variant="outline" size="sm" onClick={handleRequestNotifications}>
                <Bell className="h-3.5 w-3.5" aria-hidden="true" />
                <span className="ml-1.5">Enable notifications</span>
              </Button>
            </div>
          ) : null}
          {alerts.notificationPermission === "denied" ? (
            <p className="mt-3 rounded-md border border-border/70 bg-secondary/25 px-3 py-2 text-xs text-muted-foreground">
              Browser notifications are blocked. Triggered alerts still appear in this screen and the masthead bell.
            </p>
          ) : null}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">New alert</CardTitle>
          <CardDescription>Pick a symbol, condition, and threshold. The alert fires once and then waits to be reset.</CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSubmit} className="grid gap-3 md:grid-cols-[1fr_1.4fr_1fr_auto] md:items-end" aria-label="Create price alert">
            <FormField label="Symbol" htmlFor="price-alert-symbol" error={form.symbol ? validation.symbolError : null}>
              <Input
                id="price-alert-symbol"
                value={form.symbol}
                onChange={(event) => setForm((c) => ({ ...c, symbol: event.target.value.toUpperCase() }))}
                placeholder="e.g. AAPL"
                autoComplete="off"
                spellCheck={false}
                error={Boolean(form.symbol && validation.symbolError)}
              />
            </FormField>
            <FormField label="Condition" htmlFor="price-alert-condition">
              <div className="grid grid-cols-[1.4fr_0.9fr] gap-2">
                <Select
                  id="price-alert-condition"
                  value={form.condition}
                  onChange={(event) => setForm((c) => ({ ...c, condition: event.target.value as PriceAlertCondition }))}
                >
                  {PRICE_ALERT_CONDITION_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </Select>
                <Select
                  id="price-alert-field"
                  value={form.field}
                  onChange={(event) => setForm((c) => ({ ...c, field: event.target.value as PriceAlertField }))}
                  aria-label="Price field"
                >
                  {PRICE_ALERT_FIELD_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </Select>
              </div>
            </FormField>
            <FormField label="Threshold" htmlFor="price-alert-threshold" error={form.threshold ? validation.thresholdError : null}>
              <Input
                id="price-alert-threshold"
                type="text"
                inputMode="decimal"
                value={form.threshold}
                onChange={(event) => setForm((c) => ({ ...c, threshold: event.target.value }))}
                placeholder="e.g. 200.50"
                error={Boolean(form.threshold && validation.thresholdError)}
                autoComplete="off"
                spellCheck={false}
              />
            </FormField>
            <Button type="submit" disabled={!validation.canSubmit} aria-label="Create price alert">
              <Plus className="h-4 w-4" aria-hidden="true" />
              <span className="ml-1.5">Add alert</span>
            </Button>
          </form>
          <FormField label="Note (optional)" htmlFor="price-alert-note" className="mt-3">
            <Input
              id="price-alert-note"
              value={form.note}
              onChange={(event) => setForm((c) => ({ ...c, note: event.target.value }))}
              placeholder="e.g. Watch for earnings call"
              maxLength={120}
              autoComplete="off"
            />
          </FormField>
          {submitFeedback ? (
            <p role="status" aria-live="polite" className="mt-3 flex items-center gap-1.5 text-xs text-positive">
              <Check className="h-3.5 w-3.5" aria-hidden="true" />
              <span>{submitFeedback}</span>
            </p>
          ) : null}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <div>
              <CardTitle className="text-base">Triggered alerts</CardTitle>
              <CardDescription>{alerts.state.triggers.length === 0 ? "No alerts have triggered yet." : `${alerts.state.triggers.length} historical trigger${alerts.state.triggers.length === 1 ? "" : "s"}.`}</CardDescription>
            </div>
            {alerts.state.triggers.length > 0 ? (
              <div className="flex flex-wrap items-center gap-2">
                <Button type="button" variant="outline" size="sm" onClick={alerts.acknowledgeAllTriggers} disabled={alerts.unacknowledgedCount === 0}>
                  <CheckCheck className="h-3.5 w-3.5" aria-hidden="true" />
                  <span className="ml-1.5">Acknowledge all</span>
                </Button>
                <Button type="button" variant="outline" size="sm" onClick={alerts.clearTriggers}>
                  <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                  <span className="ml-1.5">Clear history</span>
                </Button>
              </div>
            ) : null}
          </div>
        </CardHeader>
        <CardContent>
          {alerts.state.triggers.length === 0 ? (
            <p className="rounded-md border border-dashed border-border/70 bg-secondary/15 px-3 py-4 text-sm text-muted-foreground">
              Triggered alerts will appear here. Each entry stays acknowledged once you confirm it.
            </p>
          ) : (
            <ul className="space-y-2">
              {alerts.state.triggers.slice(0, 25).map((trigger) => (
                <li
                  key={trigger.id}
                  className={`flex flex-wrap items-center justify-between gap-3 rounded-md border px-3 py-2 text-sm ${trigger.acknowledged ? "border-border/60 bg-secondary/20 text-muted-foreground" : "border-warning/30 bg-warning/10"}`}
                >
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="font-mono font-semibold text-foreground">{trigger.symbol}</span>
                      <Badge variant={trigger.acknowledged ? "outline" : "warning"} dot>{describeCondition(trigger.condition, trigger.threshold, trigger.field)}</Badge>
                    </div>
                    <p className="mt-1 font-mono text-xs">
                      Fired at {formatPriceAlertPrice(trigger.triggeredPrice)} · {formatPriceAlertTimestamp(trigger.triggeredAt)}
                    </p>
                    {trigger.note ? <p className="mt-1 text-xs text-muted-foreground">{trigger.note}</p> : null}
                  </div>
                  <div className="flex flex-wrap items-center gap-2">
                    <Button asChild variant="outline" size="sm">
                      <Link to={`/data/quotes?symbol=${encodeURIComponent(trigger.symbol)}`} aria-label={`Open live quotes for ${trigger.symbol}`}>
                        <LineChart className="h-3.5 w-3.5" aria-hidden="true" />
                        <span className="ml-1">Quote</span>
                      </Link>
                    </Button>
                    {!trigger.acknowledged ? (
                      <Button type="button" variant="outline" size="sm" onClick={() => alerts.acknowledgeTrigger(trigger.id)}>
                        <Check className="h-3.5 w-3.5" aria-hidden="true" />
                        <span className="ml-1">Acknowledge</span>
                      </Button>
                    ) : null}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">All alerts</CardTitle>
          <CardDescription>{sortedAlerts.length === 0 ? "No alerts set." : `${sortedAlerts.length} alert${sortedAlerts.length === 1 ? "" : "s"} — ${watchedSymbolCount} watching live, the rest waiting to be reset.`}</CardDescription>
        </CardHeader>
        <CardContent>
          {sortedAlerts.length === 0 ? (
            <p className="rounded-md border border-dashed border-border/70 bg-secondary/15 px-3 py-4 text-sm text-muted-foreground">
              Add your first alert above. You can also click "Set price alert" from any symbol on the Live quotes screen.
            </p>
          ) : (
            <ul className="space-y-2">
              {sortedAlerts.map((alert) => (
                <AlertRow key={alert.id} alert={alert} />
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function AlertRow({ alert }: { alert: PriceAlert }) {
  const alerts = usePriceAlerts();
  const snoozedUntilMs = alert.snoozedUntil ? new Date(alert.snoozedUntil).getTime() : null;
  const isSnoozed = snoozedUntilMs !== null && snoozedUntilMs > Date.now();
  const variant: "default" | "warning" | "outline" = !alert.enabled ? "outline" : isSnoozed ? "warning" : "default";
  const statusLabel = !alert.enabled ? (alert.triggeredAt ? "Triggered" : "Disabled") : isSnoozed ? "Snoozed" : "Watching";

  return (
    <li className={`flex flex-wrap items-center justify-between gap-3 rounded-md border px-3 py-2 text-sm ${alert.enabled && !isSnoozed ? "border-border/70 bg-background/50" : "border-border/50 bg-secondary/25"}`}>
      <div className="min-w-0">
        <div className="flex items-center gap-2">
          <span className="font-mono font-semibold text-foreground">{alert.symbol}</span>
          <Badge variant={variant} dot>{statusLabel}</Badge>
          <span className="font-mono text-xs text-muted-foreground">{describeCondition(alert.condition, alert.threshold, alert.field)}</span>
        </div>
        <p className="mt-1 font-mono text-[11px] text-muted-foreground">
          Last seen {formatPriceAlertPrice(alert.lastObservedPrice)} · {formatPriceAlertTimestamp(alert.lastObservedAt)}
          {isSnoozed ? ` · snoozed until ${formatPriceAlertTimestamp(alert.snoozedUntil)}` : ""}
        </p>
        {alert.note ? <p className="mt-1 text-xs text-muted-foreground">{alert.note}</p> : null}
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <Button asChild variant="outline" size="sm">
          <Link to={`/data/quotes?symbol=${encodeURIComponent(alert.symbol)}`} aria-label={`Open live quotes for ${alert.symbol}`}>
            <LineChart className="h-3.5 w-3.5" aria-hidden="true" />
            <span className="ml-1">Quote</span>
          </Link>
        </Button>
        {alert.enabled ? (
          isSnoozed ? (
            <Button type="button" variant="outline" size="sm" onClick={() => alerts.snoozeAlert(alert.id, null)} aria-label={`Wake ${alert.symbol} alert`}>
              <Bell className="h-3.5 w-3.5" aria-hidden="true" />
              <span className="ml-1">Wake</span>
            </Button>
          ) : (
            <Button type="button" variant="outline" size="sm" onClick={() => alerts.snoozeAlert(alert.id, new Date(Date.now() + SNOOZE_MINUTES * 60_000))} aria-label={`Snooze ${alert.symbol} alert for ${SNOOZE_MINUTES} minutes`}>
              <AlarmClock className="h-3.5 w-3.5" aria-hidden="true" />
              <span className="ml-1">Snooze {SNOOZE_MINUTES}m</span>
            </Button>
          )
        ) : (
          <Button type="button" variant="outline" size="sm" onClick={() => alerts.resetAlert(alert.id)} aria-label={`Re-enable ${alert.symbol} alert`}>
            <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
            <span className="ml-1">Reset</span>
          </Button>
        )}
        <Button type="button" variant="outline" size="sm" onClick={() => alerts.updateAlertFields(alert.id, { enabled: !alert.enabled })} aria-label={alert.enabled ? `Pause ${alert.symbol} alert` : `Resume ${alert.symbol} alert`}>
          {alert.enabled ? <BellOff className="h-3.5 w-3.5" aria-hidden="true" /> : <Bell className="h-3.5 w-3.5" aria-hidden="true" />}
          <span className="ml-1">{alert.enabled ? "Pause" : "Resume"}</span>
        </Button>
        <Button type="button" variant="outline" size="sm" onClick={() => alerts.deleteAlert(alert.id)} aria-label={`Delete ${alert.symbol} alert`}>
          <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
          <span className="ml-1">Delete</span>
        </Button>
      </div>
    </li>
  );
}

interface FormFieldProps {
  label: string;
  htmlFor: string;
  error?: string | null;
  children: React.ReactNode;
  className?: string;
}

function FormField({ label, htmlFor, error, children, className }: FormFieldProps) {
  return (
    <div className={`flex flex-col gap-1 ${className ?? ""}`}>
      <label htmlFor={htmlFor} className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </label>
      {children}
      {error ? (
        <p role="alert" className="text-[11px] text-danger">
          <X className="-mt-0.5 mr-1 inline h-3 w-3" aria-hidden="true" />
          {error}
        </p>
      ) : null}
    </div>
  );
}

interface SummaryStatProps {
  label: string;
  value: string;
  tone: "default" | "muted" | "warning";
}

function SummaryStat({ label, value, tone }: SummaryStatProps) {
  const toneClass =
    tone === "warning"
      ? "border-warning/30 bg-warning/10 text-warning"
      : tone === "default"
        ? "border-primary/30 bg-primary/10 text-primary"
        : "border-border/60 bg-secondary/25 text-muted-foreground";
  return (
    <div className={`rounded-md border px-3 py-2.5 ${toneClass}`}>
      <div className="text-[10px] uppercase tracking-[0.14em]">{label}</div>
      <div className="mt-1 font-mono text-base text-foreground">{value}</div>
    </div>
  );
}

function readFormFromSearchParams(searchParams: URLSearchParams): PriceAlertFormState {
  const symbol = searchParams.get("symbol") ?? "";
  return {
    ...DEFAULT_PRICE_ALERT_FORM,
    symbol: symbol.toUpperCase()
  };
}
