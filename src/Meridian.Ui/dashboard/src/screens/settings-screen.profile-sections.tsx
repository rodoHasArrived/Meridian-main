// Profile authentication, system posture, and appearance sections of the Settings screen.
import { SeverityBadge } from "@/components/operations";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { DensityToggle } from "@/components/ui/density-toggle";
import { StatusBanner } from "@/components/ui/status-banner";
import { ThemeToggle } from "@/components/ui/theme-toggle";
import { usesTrackedCaps } from "@/design-system/label-case";
import { badgeVariantToOperatorSeverity } from "@/lib/shared-tone-mappings";
import { cn } from "@/lib/utils";
import { type SettingsProfileAuthenticationStep } from "@/screens/settings-screen.view-model";
import type { SystemOverviewResponse } from "@/types";
import { ArrowRight, MonitorCheck, User } from "lucide-react";
import { Link } from "react-router-dom";
import { diagnosticToneClass, SettingsFieldRow } from "./settings-provider-integration";
import type { SettingsScreenViewModel } from "@/screens/settings-screen.view-model";
import type { ProviderHealthCount } from "@/lib/cross-screen-counts";
import { SettingsChip, settingsBannerTone, setupStepToneClass } from "@/screens/settings-screen.section-primitives";

interface SettingsAccessPostureSectionProps {
  vm: SettingsScreenViewModel;
  overview: SystemOverviewResponse | null;
  providerHealth: ProviderHealthCount | null;
}

/** Profile authentication and system posture cards. */
export function SettingsAccessPostureSection({ vm, overview, providerHealth }: SettingsAccessPostureSectionProps) {
  return (
    <section className="grid gap-4 xl:grid-cols-[0.95fr_1.05fr]">
      <Card
        id="profile-authentication"
        role="region"
        aria-label={vm.profileAuthenticationPanel.regionLabel}
        className={cn("panel-surface scroll-mt-6 border", diagnosticToneClass[vm.profileAuthenticationPanel.statusTone])}
      >
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <div className="eyebrow-label eyebrow-label--sentence">Profile and authentication</div>
              <CardTitle className="mt-2 flex items-center gap-2">
                <User className="h-5 w-5 text-primary" />
                {vm.profileAuthenticationPanel.title}
              </CardTitle>
              <CardDescription className="mt-2">{vm.profileAuthenticationPanel.summary}</CardDescription>
            </div>
            <SeverityBadge
              status={badgeVariantToOperatorSeverity(vm.profileAuthenticationPanel.statusTone)}
              label={vm.profileAuthenticationPanel.statusLabel}
            />
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-4 lg:grid-cols-[minmax(0,0.78fr)_minmax(0,1fr)]">
            <div className="rounded-md border border-border/70 bg-background/35 px-4 py-4">
              <div className="flex items-start gap-3">
                <div
                  aria-hidden="true"
                  className="grid h-12 w-12 shrink-0 place-items-center rounded-md border border-primary/35 font-mono text-sm font-semibold text-primary"
                >
                  {vm.profileAuthenticationPanel.avatarInitials}
                </div>
                <div className="min-w-0">
                  <div className="break-words text-sm font-semibold text-foreground">
                    {vm.profileAuthenticationPanel.operatorName}
                  </div>
                  <div className="mt-1 break-words text-xs text-muted-foreground">
                    {vm.profileAuthenticationPanel.roleLabel}
                  </div>
                  <div className="mt-3 flex flex-wrap gap-2">
                    <SettingsChip label="Mode" value={vm.profileAuthenticationPanel.environmentLabel} />
                    <SettingsChip label="Workspace" value={vm.profileAuthenticationPanel.workspaceLabel} />
                  </div>
                </div>
              </div>
              <dl className="mt-4 grid gap-2">
                <SettingsFieldRow label="Command trail" value={vm.profileAuthenticationPanel.commandCountLabel} tone="muted" />
                <SettingsFieldRow
                  label="Authority"
                  value={vm.profileAuthenticationPanel.authorityLabel}
                  tone={vm.profileAuthenticationPanel.statusTone === "danger" ? "danger" : vm.profileAuthenticationPanel.statusTone === "warning" ? "warning" : "default"}
                />
              </dl>
              <p className="mt-3 text-xs leading-5 text-muted-foreground">
                {vm.profileAuthenticationPanel.authorityDetail}
              </p>
            </div>

            <div className="grid gap-3">
              <dl className="grid gap-2" aria-label="Profile authentication facts">
                {vm.profileAuthenticationPanel.facts.map((fact) => (
                  <SettingsFieldRow key={fact.id} label={fact.label} value={fact.value} tone={fact.tone} />
                ))}
              </dl>
              {vm.profileAuthenticationPanel.notice ? (
                <StatusBanner
                  role={vm.profileAuthenticationPanel.notice.role}
                  tone={settingsBannerTone(vm.profileAuthenticationPanel.notice.tone)}
                  title={vm.profileAuthenticationPanel.notice.title}
                  detail={vm.profileAuthenticationPanel.notice.detail}
                />
              ) : null}
            </div>
          </div>

          <div className="grid gap-2">
            <h3 className={cn("text-xs font-semibold text-muted-foreground", usesTrackedCaps(vm.profileAuthenticationPanel.stepsTitle) && "uppercase")}>
              {vm.profileAuthenticationPanel.stepsTitle}
            </h3>
            <div role="list" aria-label={vm.profileAuthenticationPanel.stepsAriaLabel} className="grid gap-2">
              {vm.profileAuthenticationPanel.steps.map((step) => (
                <ProfileAuthenticationStepRow key={step.id} step={step} />
              ))}
            </div>
          </div>
        </CardContent>
      </Card>

      <Card className={cn("panel-surface border", systemToneClass[vm.systemTone])}>
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <div className="eyebrow-label">System posture</div>
              <CardTitle className="mt-2 flex items-center gap-2">
                <MonitorCheck className="h-5 w-5 text-primary" />
                {vm.systemTitle}
              </CardTitle>
              <CardDescription className="mt-2">{vm.systemSummary}</CardDescription>
            </div>
            <SeverityBadge
              status={badgeVariantToOperatorSeverity(vm.systemTone)}
              label={overview?.systemStatus ?? "Unavailable"}
            />
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-wrap gap-2">
            <SettingsChip label="Providers healthy" value={providerHealth ? `${providerHealth.healthy} / ${providerHealth.total}` : "—"} />
            <SettingsChip label="Runs" value={overview ? String(overview.activeRuns) : "—"} />
            <SettingsChip label="Positions" value={overview ? String(overview.openPositions) : "—"} />
            <SettingsChip label="Storage" value={overview?.storageHealth ?? "—"} />
          </div>
          {vm.hasOverview ? (
            <dl className="grid gap-2">
              {vm.systemItems.map((item) => (
                <SettingsFieldRow key={item.label} label={item.label} value={item.value} tone={item.tone} />
              ))}
            </dl>
          ) : (
            <p className="rounded-md border border-border/70 bg-secondary/25 px-4 py-4 text-center text-sm text-muted-foreground">
              System overview is unavailable. Check the API connection.
            </p>
          )}
        </CardContent>
      </Card>
    </section>
  );
}

/** Appearance preferences card. */
export function SettingsAppearanceSection() {
  return (
    <Card
      id="settings-appearance"
      role="region"
      aria-label="Appearance preferences"
      className="panel-surface scroll-mt-6 border border-border/70"
    >
      <CardHeader>
        <div className="eyebrow-label">Operator preferences</div>
        <CardTitle className="mt-2 flex items-center gap-2 text-base">
          <MonitorCheck className="h-4 w-4 text-primary" />
          Appearance
        </CardTitle>
        <CardDescription className="mt-2">
          Choose the workstation theme and screen density for this browser.
        </CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4 md:grid-cols-2">
        <div className="grid gap-2">
          <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Theme</div>
          <ThemeToggle fullWidth />
        </div>
        <div className="grid gap-2">
          <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Density</div>
          <DensityToggle persist="meridian.workstation.density.v1" fullWidth />
        </div>
      </CardContent>
    </Card>
  );
}

function ProfileAuthenticationStepRow({ step }: { step: SettingsProfileAuthenticationStep }) {
  return (
    <div role="listitem" className={cn("rounded-md border px-3 py-2", setupStepToneClass[step.tone])}>
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
  );
}

const systemToneClass = {
  default: "border-border/70",
  success: "border-success/30",
  warning: "border-warning/30",
  danger: "border-danger/30"
} as const;
