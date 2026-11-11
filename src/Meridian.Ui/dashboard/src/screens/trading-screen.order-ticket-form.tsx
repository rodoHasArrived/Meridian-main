import type { ReactNode } from "react";
import { XCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { CardContent } from "@/components/ui/card";
import { FieldSupportText, joinDescribedByIds } from "@/components/ui/field-support";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { usesTrackedCaps } from "@/design-system/label-case";
import { cn } from "@/lib/utils";
import type { useOrderTicketViewModel } from "@/screens/trading-screen.view-model";

/** The screen owns draft state and supplies its financial preview; this component renders the form. */
export function TradingOrderTicketForm({ orderTicket, preview }: {
  orderTicket: ReturnType<typeof useOrderTicketViewModel>;
  preview: ReactNode;
}) {
  return (
    <CardContent id={orderTicket.formId} className="border-b border-border/60 pb-6">
      <form onSubmit={(event) => { event.preventDefault(); void orderTicket.submitOrder(); }} className="space-y-4" aria-describedby={orderTicket.requirementId}>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          <div className="space-y-1">
            <label htmlFor={orderTicket.controls.symbol.id} className={cn("text-xs font-medium text-muted-foreground", usesTrackedCaps(orderTicket.controls.symbol.label) && "uppercase tracking-[0.14em]")}>{orderTicket.controls.symbol.label}</label>
            <Input
              id={orderTicket.controls.symbol.id}
              type="text"
              placeholder="AAPL"
              value={orderTicket.controls.symbol.value}
              onChange={(e) => orderTicket.updateField(orderTicket.controls.symbol.field, e.target.value)}
              onBlur={orderTicket.normalizeSymbol}
              aria-label={orderTicket.controls.symbol.ariaLabel}
              aria-describedby={orderTicket.controls.symbol.describedBy}
              error={orderTicket.controls.symbol.invalid}
              className="font-mono"
              required={orderTicket.controls.symbol.required}
            />
          </div>
          <div className="space-y-1">
            <label htmlFor={orderTicket.controls.side.id} className={cn("text-xs font-medium text-muted-foreground", usesTrackedCaps(orderTicket.controls.side.label) && "uppercase tracking-[0.14em]")}>{orderTicket.controls.side.label}</label>
            <Select
              id={orderTicket.controls.side.id}
              value={orderTicket.controls.side.value}
              onChange={(e) => orderTicket.updateField(orderTicket.controls.side.field, e.target.value)}
              aria-label={orderTicket.controls.side.ariaLabel}
              aria-describedby={orderTicket.controls.side.describedBy}
              required={orderTicket.controls.side.required}
            >
              {orderTicket.controls.side.options.map((option) => (
                <option key={option.value} value={option.value}>{option.label}</option>
              ))}
            </Select>
          </div>
          <div className="space-y-1">
            <label htmlFor={orderTicket.controls.type.id} className={cn("text-xs font-medium text-muted-foreground", usesTrackedCaps(orderTicket.controls.type.label) && "uppercase tracking-[0.14em]")}>{orderTicket.controls.type.label}</label>
            <Select
              id={orderTicket.controls.type.id}
              value={orderTicket.controls.type.value}
              onChange={(e) => orderTicket.updateField(orderTicket.controls.type.field, e.target.value)}
              aria-label={orderTicket.controls.type.ariaLabel}
              aria-describedby={orderTicket.controls.type.describedBy}
              required={orderTicket.controls.type.required}
            >
              {orderTicket.controls.type.options.map((option) => (
                <option key={option.value} value={option.value}>{option.label}</option>
              ))}
            </Select>
          </div>
          <div className="space-y-1">
            <label htmlFor={orderTicket.controls.quantity.id} className={cn("text-xs font-medium text-muted-foreground", usesTrackedCaps(orderTicket.controls.quantity.label) && "uppercase tracking-[0.14em]")}>{orderTicket.controls.quantity.label}</label>
            <Input
              id={orderTicket.controls.quantity.id}
              type="number"
              min={1}
              step={1}
              value={orderTicket.controls.quantity.value}
              onChange={(e) => orderTicket.updateField(orderTicket.controls.quantity.field, e.target.value)}
              aria-label={orderTicket.controls.quantity.ariaLabel}
              aria-describedby={orderTicket.controls.quantity.describedBy}
              error={orderTicket.controls.quantity.invalid}
              className="font-mono"
              required={orderTicket.controls.quantity.required}
            />
          </div>
          {orderTicket.controls.limitPrice && (
            <div className="space-y-1">
              <label htmlFor={orderTicket.controls.limitPrice.id} className={cn("text-xs font-medium text-muted-foreground", usesTrackedCaps(orderTicket.controls.limitPrice.label) && "uppercase tracking-[0.14em]")}>
                {orderTicket.controls.limitPrice.label}
              </label>
              <Input
                id={orderTicket.controls.limitPrice.id}
                type="number"
                min={0}
                step={0.01}
                value={orderTicket.controls.limitPrice.value}
                onChange={(e) => orderTicket.controls.limitPrice && orderTicket.updateField(orderTicket.controls.limitPrice.field, e.target.value)}
                aria-label={orderTicket.controls.limitPrice.ariaLabel}
                aria-describedby={orderTicket.controls.limitPrice.describedBy}
                error={orderTicket.controls.limitPrice.invalid}
                className="font-mono"
                required={orderTicket.controls.limitPrice.required}
              />
            </div>
          )}
        </div>

        <p id={orderTicket.requirementId} className="text-xs text-muted-foreground">
          {orderTicket.requirementText}
        </p>
        <span className="sr-only" aria-live="polite">{orderTicket.statusAnnouncement}</span>

        {preview}

        <label
          htmlFor={orderTicket.acknowledgement.id}
          className="flex items-start gap-3 rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-sm"
        >
          <input
            id={orderTicket.acknowledgement.id}
            type="checkbox"
            checked={orderTicket.acknowledgement.checked}
            disabled={orderTicket.acknowledgement.disabled}
            onChange={(event) => orderTicket.setAcknowledged(event.target.checked)}
            aria-describedby={joinDescribedByIds(
              `${orderTicket.acknowledgement.id}-description`,
              `${orderTicket.acknowledgement.id}-disabled-reason`
            )}
            className="mt-1 h-4 w-4 accent-primary"
          />
          <span>
            <span className="block font-medium text-foreground">{orderTicket.acknowledgement.label}</span>
            <span id={`${orderTicket.acknowledgement.id}-description`} className="mt-1 block text-xs leading-5 text-muted-foreground">
              {orderTicket.acknowledgement.description}
            </span>
            <FieldSupportText
              disabledReason={orderTicket.acknowledgement.disabledReason}
              disabledReasonId={`${orderTicket.acknowledgement.id}-disabled-reason`}
              disabledReasonClassName="mt-1 block"
            />
          </span>
        </label>

        {orderTicket.errorText && (
          <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger flex items-center gap-2">
            <XCircle className="h-4 w-4 shrink-0" />
            {orderTicket.errorText}
          </div>
        )}

        <div className="panel-action-zone justify-start">
          <Button
            type="submit"
            size="sm"
            disabled={!orderTicket.canSubmit}
            disabledReason={orderTicket.submitDisabledReason}
            busy={orderTicket.submitBusy}
            busyLabel={orderTicket.submitBusyLabel}
            aria-label={orderTicket.submitAriaLabel}
            aria-describedby="order-ticket-requirements"
          >
            {orderTicket.submitButtonLabel}
          </Button>
          <Button
            type="button"
            size="sm"
            variant="outline"
            onClick={orderTicket.closeTicket}
            disabled={!orderTicket.canClose}
            disabledReason={orderTicket.closeDisabledReason}
            aria-label={orderTicket.closeAriaLabel}
          >
            {orderTicket.closeButtonLabel}
          </Button>
        </div>
      </form>
    </CardContent>
  );
}
