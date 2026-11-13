// Alpaca and Robinhood brokerage connection sections of the Settings screen.
import { SeverityBadge } from "@/components/operations";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { FieldSupportText, joinDescribedByIds } from "@/components/ui/field-support";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { usesTrackedCaps } from "@/design-system/label-case";
import { badgeVariantToOperatorSeverity } from "@/lib/shared-tone-mappings";
import { cn } from "@/lib/utils";
import { type SettingsAlpacaCredentialFieldState } from "@/screens/settings-screen.view-model";
import { ArrowRight, ExternalLink, KeyRound, ShieldCheck, Trash2 } from "lucide-react";
import { type ReactNode } from "react";
import { Link } from "react-router-dom";
import { diagnosticToneClass, SettingsFieldRow } from "./settings-provider-integration";
import type { SettingsAlpacaConnectionFormViewModel, SettingsRobinhoodConnectionFormViewModel, SettingsScreenViewModel } from "@/screens/settings-screen.view-model";
import { SettingsChip, settingsBannerTone, setupStepToneClass } from "@/screens/settings-screen.section-primitives";

interface SettingsAlpacaSectionProps {
  vm: SettingsScreenViewModel;
  alpacaForm: SettingsAlpacaConnectionFormViewModel;
}

/** Alpaca brokerage connection setup. */
export function SettingsAlpacaSection({ vm, alpacaForm }: SettingsAlpacaSectionProps) {
  return (
    <Card
      id="alpaca-provider-setup"
      className={cn("panel-surface scroll-mt-6 border", diagnosticToneClass[vm.alpacaConnectionPanel.statusTone])}
    >
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <div className="eyebrow-label">Brokerage connection</div>
            <CardTitle className="mt-2 flex items-center gap-2 text-base">
              <KeyRound className="h-4 w-4 text-primary" />
              Alpaca paper API keys
            </CardTitle>
            <CardDescription className="mt-2">{vm.alpacaConnectionPanel.statusDetail}</CardDescription>
          </div>
          <SeverityBadge
            status={badgeVariantToOperatorSeverity(vm.alpacaConnectionPanel.statusTone)}
            label={vm.alpacaConnectionPanel.stateLabel}
          />
        </div>
      </CardHeader>
      <CardContent className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(280px,0.8fr)]">
        <form className="grid gap-3" onSubmit={alpacaForm.connect} noValidate aria-describedby={alpacaForm.formPanelId}>
          <div className="grid gap-3 md:grid-cols-2">
            <AlpacaCredentialField
              field={alpacaForm.keyIdField}
              value={alpacaForm.keyId}
              onValueChange={alpacaForm.setKeyId}
              leadingIcon={<KeyRound className="h-4 w-4" />}
            />
            <AlpacaCredentialField
              field={alpacaForm.secretKeyField}
              value={alpacaForm.secretKey}
              onValueChange={alpacaForm.setSecretKey}
              leadingIcon={<ShieldCheck className="h-4 w-4" />}
            />
            <fieldset className="grid gap-1 text-xs font-medium text-muted-foreground md:col-span-2">
              <legend>{alpacaForm.environmentLegend}</legend>
              <div
                className="grid gap-2 sm:grid-cols-2"
                role="radiogroup"
                aria-label={alpacaForm.environmentLegend}
                aria-describedby={`${alpacaForm.fieldHelpIds.environment} ${alpacaForm.formPanelId}`}
              >
                {alpacaForm.environmentOptions.map((option) => (
                  <label
                    key={option.id}
                    htmlFor={option.id}
                    className={cn(
                      "relative grid min-h-[4.75rem] cursor-pointer gap-1 rounded-md border px-3 py-2 transition-colors",
                      option.isSelected ? environmentOptionClass[option.tone].selected : environmentOptionClass[option.tone].idle,
                      option.disabled && "cursor-not-allowed opacity-60"
                    )}
                  >
                    <input
                      id={option.id}
                      type="radio"
                      name="alpaca-environment"
                      value={option.value}
                      checked={option.isSelected}
                      disabled={option.disabled}
                      onChange={() => alpacaForm.setEnvironment(option.value)}
                      aria-label={option.ariaLabel}
                      aria-describedby={joinDescribedByIds(
                        option.descriptionId,
                        alpacaForm.fieldHelpIds.environment,
                        option.disabledReasonId,
                        alpacaForm.formPanelId
                      )}
                      className="peer sr-only"
                    />
                    <span className="pointer-events-none absolute inset-0 rounded-md peer-focus-visible:ring-2 peer-focus-visible:ring-primary/40" aria-hidden="true" />
                    <span className="flex items-center justify-between gap-2">
                      <span className="font-semibold text-foreground">{option.label}</span>
                      <span className={cn("rounded-sm border px-2 py-0.5 font-mono text-[10px] uppercase", environmentOptionClass[option.tone].badge)}>
                        {option.badgeLabel}
                      </span>
                    </span>
                    <span id={option.descriptionId} className="text-[11px] font-normal leading-4 text-muted-foreground">
                      {option.description}
                    </span>
                  </label>
                ))}
              </div>
              <FieldSupportText
                helpId={alpacaForm.fieldHelpIds.environment}
                helpText={alpacaForm.environmentHelpText}
                helpClassName="text-[11px] leading-4"
                disabledReason={alpacaForm.environmentOptions[0]?.disabledReason}
                disabledReasonId={alpacaForm.environmentOptions[0]?.disabledReasonId ?? undefined}
                disabledReasonClassName="text-[11px] leading-4"
              />
              <TechnicalDetails
                label="Service details"
                description="Provider endpoints are shown only for advanced troubleshooting."
                className="mt-2"
              >
                <dl className="grid gap-2">
                  {alpacaForm.environmentOptions.map((option) => (
                    <SettingsFieldRow
                      key={`${option.id}-endpoint`}
                      label={`${option.label} endpoint`}
                      value={option.endpointLabel}
                      tone="muted"
                    />
                  ))}
                </dl>
              </TechnicalDetails>
            </fieldset>
          </div>
          {alpacaForm.liveAcknowledgement.visible ? (
            <Checkbox
                id={alpacaForm.liveAcknowledgement.id}
                checked={alpacaForm.liveAcknowledgement.checked}
                disabled={alpacaForm.liveAcknowledgement.disabled}
                required={alpacaForm.liveAcknowledgement.required}
                onCheckedChange={alpacaForm.setLiveAcknowledged}
                aria-label={alpacaForm.liveAcknowledgement.ariaLabel}
                aria-describedby={joinDescribedByIds(
                  alpacaForm.liveAcknowledgement.descriptionId,
                  alpacaForm.liveAcknowledgement.disabledReasonId,
                  alpacaForm.formPanelId
                )}
                className="rounded-md border border-live-env/35 bg-live-env/10 px-3 py-3 text-live-env"
                label={alpacaForm.liveAcknowledgement.label}
                hint={
                  <>
                    <span id={alpacaForm.liveAcknowledgement.descriptionId} className="block">
                      {alpacaForm.liveAcknowledgement.detail}
                    </span>
                    <FieldSupportText
                      disabledReason={alpacaForm.liveAcknowledgement.disabledReason}
                      disabledReasonId={alpacaForm.liveAcknowledgement.disabledReasonId ?? undefined}
                      disabledReasonClassName="mt-1 block"
                    />
                  </>
                }
              />
          ) : null}
          <StatusBanner
            id={alpacaForm.formPanelId}
            role={alpacaForm.formPanelRole}
            aria-live={alpacaForm.formPanelAriaLive}
            tone={settingsBannerTone(alpacaForm.formPanelTone)}
            title={alpacaForm.formPanelTitle}
            detail={
              <div className="grid gap-3">
                <p className="leading-5">{alpacaForm.formPanelDetail}</p>
              <div className="flex flex-wrap gap-2" aria-label="Alpaca credential requirements">
                {alpacaForm.requirements.map((requirement) => (
                  <span
                    key={requirement.id}
                    className={cn("inline-flex items-center gap-2 rounded-sm border px-2 py-1 text-[11px] font-medium", requirementToneClass[requirement.tone])}
                  >
                    <span className="text-muted-foreground">{requirement.label}</span>
                    <span className="font-mono">{requirement.value}</span>
                  </span>
                ))}
              </div>
            </div>
            }
          />
          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="submit"
              size="sm"
              disabled={!alpacaForm.canSubmit}
              busy={alpacaForm.submitBusy}
              busyLabel="Testing Alpaca"
              disabledReason={alpacaForm.submitDisabledReason}
            >
              <ShieldCheck className="h-4 w-4" aria-hidden="true" />
              {alpacaForm.submitLabel}
            </Button>
            <Button
              type="button"
              size="sm"
              variant="destructive"
              onClick={alpacaForm.clear}
              disabled={alpacaForm.clearDisabledReason !== null}
              busy={alpacaForm.clearBusy}
              busyLabel="Clearing Alpaca"
              disabledReason={alpacaForm.clearDisabledReason}
            >
              <Trash2 className="h-4 w-4" aria-hidden="true" />
              {alpacaForm.clearLabel}
            </Button>
            {alpacaForm.actionMessage ? (
              <div aria-live={alpacaForm.statusRole === "alert" ? "assertive" : "polite"} className={alpacaForm.statusClassName}>
                <div>{alpacaForm.actionMessage}</div>
                {alpacaForm.statusDetails.length > 0 ? (
                  <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                    {alpacaForm.statusDetails.map((detail) => (
                      <li key={detail}>{detail}</li>
                    ))}
                  </ul>
                ) : null}
              </div>
            ) : null}
          </div>
        </form>

        <div className="grid gap-2">
          <div className="flex flex-wrap gap-2">
            <SettingsChip label="Provider" value={vm.alpacaConnectionPanel.providerLabel} />
            <SettingsChip label="Environment" value={vm.alpacaConnectionPanel.environmentLabel} />
          </div>
          <dl className="grid gap-2">
            <SettingsFieldRow label="Key ID" value={vm.alpacaConnectionPanel.maskedKeyIdLabel} tone="muted" />
            <SettingsFieldRow label="Account" value={vm.alpacaConnectionPanel.accountLabel} tone={vm.alpacaConnectionPanel.statusTone === "success" ? "success" : "muted"} />
            <SettingsFieldRow label="Verified" value={vm.alpacaConnectionPanel.verifiedAtLabel} tone={vm.alpacaConnectionPanel.verifiedAtTone} />
          </dl>
          {vm.alpacaConnectionPanel.warnings.length > 0 ? (
            <div className="rounded-md border border-warning/35 bg-warning/10 px-3 py-2 text-xs leading-5 text-warning">
              {vm.alpacaConnectionPanel.warnings[0]}
            </div>
          ) : null}
          <div className="grid gap-2">
            <div className="min-w-0">
              <h3 className={cn("text-xs font-semibold text-muted-foreground", usesTrackedCaps(vm.alpacaConnectionPanel.setupChecklistTitle) && "uppercase")}>
                {vm.alpacaConnectionPanel.setupChecklistTitle}
              </h3>
              <p className="mt-1 text-xs leading-5 text-muted-foreground">
                {vm.alpacaConnectionPanel.setupChecklistDetail}
              </p>
            </div>
            <div
              role="list"
              aria-label={vm.alpacaConnectionPanel.setupChecklistAriaLabel}
              className="grid gap-2"
            >
            {vm.alpacaConnectionPanel.setupChecklist.map((step) => (
              <div
                key={step.id}
                role="listitem"
                className={cn("rounded-md border px-3 py-2", setupStepToneClass[step.tone])}
              >
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <div className="text-sm font-medium text-foreground">{step.label}</div>
                    <p className="mt-1 text-xs leading-5 text-muted-foreground">{step.detail}</p>
                  </div>
                  <SeverityBadge
                    status={badgeVariantToOperatorSeverity(step.badgeVariant)}
                    label={step.statusLabel}
                    className="shrink-0"
                  />
                </div>
                {step.actionHref && step.actionLabel ? (
                  <Button asChild variant="outline" size="sm" className="mt-3">
                    <Link to={step.actionHref} aria-label={step.actionAriaLabel ?? step.actionLabel}>
                      {step.actionLabel}
                      <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
                    </Link>
                  </Button>
                ) : null}
              </div>
            ))}
            </div>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

interface SettingsRobinhoodSectionProps {
  vm: SettingsScreenViewModel;
  robinhoodAuthorizationUrl: string | null;
  robinhoodForm: SettingsRobinhoodConnectionFormViewModel;
}

/** Robinhood brokerage connection setup. */
export function SettingsRobinhoodSection({ vm, robinhoodAuthorizationUrl, robinhoodForm }: SettingsRobinhoodSectionProps) {
  return (
    <Card
      id="robinhood-provider-setup"
      className={cn("panel-surface scroll-mt-6 border", diagnosticToneClass[vm.robinhoodConnectionPanel.statusTone])}
    >
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <div className="eyebrow-label">Brokerage connection</div>
            <CardTitle className="mt-2 flex items-center gap-2 text-base">
              <ShieldCheck className="h-4 w-4 text-primary" />
              Robinhood (read-only)
            </CardTitle>
            <CardDescription className="mt-2">{vm.robinhoodConnectionPanel.statusDetail}</CardDescription>
          </div>
          <SeverityBadge
            status={badgeVariantToOperatorSeverity(vm.robinhoodConnectionPanel.statusTone)}
            label={vm.robinhoodConnectionPanel.stateLabel}
          />
        </div>
      </CardHeader>
      <CardContent className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(280px,0.8fr)]">
        <div className="grid gap-3">
          {!vm.robinhoodConnectionPanel.isConfigured ? (
            <div className="rounded-md border border-warning/35 bg-warning/10 px-3 py-2 text-xs leading-5 text-warning">
              Read-only Robinhood requires an authorized aggregation provider. Set the ROBINHOOD_BROKERAGE_* OAuth
              environment variables.
            </div>
          ) : null}
          {vm.robinhoodConnectionPanel.warnings.length > 0 ? (
            <div className="rounded-md border border-warning/35 bg-warning/10 px-3 py-2 text-xs leading-5 text-warning">
              {vm.robinhoodConnectionPanel.warnings[0]}
            </div>
          ) : null}
          {robinhoodAuthorizationUrl && vm.robinhoodConnectionPanel.canConnect ? (
            <div className="rounded-md border border-primary/35 bg-primary/10 px-3 py-2 text-xs leading-5">
              <p className="leading-5">Complete authorization in the opened tab to finish connecting Robinhood.</p>
              <a
                href={robinhoodAuthorizationUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="mt-2 inline-flex items-center gap-1 font-medium text-primary underline"
              >
                Open authorization page
                <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
              </a>
            </div>
          ) : null}
          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              size="sm"
              onClick={robinhoodForm.connect}
              disabled={
                robinhoodForm.busy
                || !vm.robinhoodConnectionPanel.canConnect
                || !vm.robinhoodConnectionPanel.isConfigured
              }
              busy={robinhoodForm.busyAction === "connect"}
              busyLabel="Connecting Robinhood"
            >
              <ShieldCheck className="h-4 w-4" aria-hidden="true" />
              Connect Robinhood
            </Button>
            <Button
              type="button"
              size="sm"
              variant="outline"
              onClick={robinhoodForm.disconnect}
              disabled={robinhoodForm.busy || !vm.robinhoodConnectionPanel.canDisconnect}
              busy={robinhoodForm.busyAction === "disconnect"}
              busyLabel="Disconnecting Robinhood"
            >
              <Trash2 className="h-4 w-4" aria-hidden="true" />
              Disconnect
            </Button>
            {robinhoodForm.actionMessage ? (
              <div
                role={robinhoodForm.statusRole}
                aria-live={robinhoodForm.statusRole === "alert" ? "assertive" : "polite"}
                className={robinhoodForm.statusClassName}
              >
                <div>{robinhoodForm.actionMessage}</div>
                {robinhoodForm.actionDetails.length > 0 ? (
                  <ul className="mt-2 list-disc space-y-1 pl-5 text-xs leading-5">
                    {robinhoodForm.actionDetails.map((detail) => (
                      <li key={detail}>{detail}</li>
                    ))}
                  </ul>
                ) : null}
              </div>
            ) : null}
          </div>
        </div>

        <div className="grid gap-2">
          <div className="flex flex-wrap gap-2">
            <SettingsChip label="Provider" value={vm.robinhoodConnectionPanel.providerLabel} />
          </div>
          <dl className="grid gap-2">
            <SettingsFieldRow
              label="Account"
              value={vm.robinhoodConnectionPanel.accountLabel}
              tone={vm.robinhoodConnectionPanel.statusTone === "success" ? "success" : "muted"}
            />
            <SettingsFieldRow label="Connected" value={vm.robinhoodConnectionPanel.connectedAtLabel} tone="muted" />
            <SettingsFieldRow label="Expires" value={vm.robinhoodConnectionPanel.expiresAtLabel} tone="muted" />
            <SettingsFieldRow label="Scopes" value={vm.robinhoodConnectionPanel.scopesLabel} tone="muted" />
          </dl>
        </div>
      </CardContent>
    </Card>
  );
}

function AlpacaCredentialField({
  field,
  value,
  onValueChange,
  leadingIcon
}: {
  field: SettingsAlpacaCredentialFieldState;
  value: string;
  onValueChange: (value: string) => void;
  leadingIcon: ReactNode;
}) {
  const disabledReasonId = `${field.id}-disabled-reason`;
  return (
    <label htmlFor={field.id} className="grid gap-1 text-xs font-medium text-muted-foreground">
      {field.label}
      <Input
        id={field.id}
        type={field.type}
        value={value}
        onChange={(event) => onValueChange(event.target.value)}
        autoComplete={field.autoComplete}
        placeholder={field.placeholder}
        leadingIcon={leadingIcon}
        disabled={field.disabled}
        error={field.error}
        aria-describedby={joinDescribedByIds(field.describedBy, field.disabledReason ? disabledReasonId : undefined)}
      />
      <FieldSupportText
        helpId={field.helpId}
        helpText={field.helpText}
        helpClassName={cn("text-[11px] leading-4", field.error ? "text-danger" : "text-muted-foreground")}
        disabledReason={field.disabledReason}
        disabledReasonId={field.disabledReason ? disabledReasonId : undefined}
        disabledReasonClassName="text-[11px] leading-4"
      />
    </label>
  );
}

const environmentOptionClass = {
  paper: {
    selected: "border-paper/40 bg-paper/10 text-[var(--state-paper-fg)]",
    idle: "border-border/70 bg-secondary/25 text-foreground hover:border-paper/35 hover:bg-paper/10",
    badge: "border-paper/30 bg-paper/10 text-[var(--state-paper-fg)]"
  },
  live: {
    selected: "border-live-env/40 bg-live-env/10 text-live-env",
    idle: "border-border/70 bg-secondary/25 text-foreground hover:border-live-env/35 hover:bg-live-env/10",
    badge: "border-live-env/35 bg-live-env/10 text-[var(--state-live-fg)]"
  }
} as const;

const requirementToneClass = {
  success: "border-success/30 bg-success/10 text-success",
  warning: "border-warning/35 bg-warning/10 text-warning",
  muted: "border-border/70 bg-secondary/25 text-muted-foreground"
} as const;
