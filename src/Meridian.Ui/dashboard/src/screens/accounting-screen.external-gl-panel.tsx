// External GL / accounting-system reconciliation panel, extracted from accounting-screen.tsx.
import { BookCheck, RefreshCcw } from "lucide-react";
import { formatCurrency as formatCurrencyAmount, formatUtcDate, formatTimestamp } from "@/lib/format";
import { TableWorkbench } from "@/components/meridian/table-workbench";
import { DesignSystemStatus } from "@/design-system/status";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { cn } from "@/lib/utils";
import type { AccountingSystemImportDetail, AccountingSystemReconciliationEvidencePackage, ExternalGlExportPackage, ExternalGlExportPackageManifest, ExternalGlMappingProfile, AccountingSystemProvider, AccountingSystemReconciliationSummary } from "@/types";
import { AccountingValue } from "@/screens/accounting-screen.accounting-value";

const accountingSystemEvidencePackageVariant = {
  Ready: "success",
  ReviewRequired: "warning",
  Missing: "danger"
} as const;

const externalGlStateLabels: Record<string, string> = {
  available: "Available",
  blocked: "Blocked",
  certified: "Certified",
  disabled: "Disabled",
  draft: "Draft",
  failed: "Failed",
  imported: "Imported",
  matched: "Matched",
  missing: "Missing",
  missingevidence: "Evidence missing",
  missingexternal: "Missing from external GL",
  missingmeridian: "Missing from Meridian",
  notstarted: "Not started",
  planned: "Planned",
  previewed: "Previewed",
  ready: "Ready",
  readyforreview: "Ready for review",
  rejected: "Rejected",
  reviewrequired: "Review required",
  superseded: "Superseded",
  variance: "Amount variance"
};

function presentExternalGlState(value: string | null | undefined, fallback = "Not available"): string {
  const trimmed = value?.trim();
  if (!trimmed) {
    return fallback;
  }

  const normalized = trimmed.replace(/[^a-z0-9]+/gi, "").toLowerCase();
  if (externalGlStateLabels[normalized]) {
    return externalGlStateLabels[normalized];
  }

  const words = trimmed
    .replace(/[_-]+/g, " ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/\s+/g, " ")
    .trim()
    .toLowerCase();
  return words ? `${words.charAt(0).toUpperCase()}${words.slice(1)}` : fallback;
}

function presentExternalGlProviderName(displayName: string): string {
  return displayName.replace(/\s+fixture\b/gi, " import");
}

function presentExternalGlProviderEvidenceLabel(displayName: string): string {
  return /\s+fixture\b/i.test(displayName)
    ? `Imported ${displayName.replace(/\s+fixture\b/gi, "")} evidence`
    : `${presentExternalGlProviderName(displayName)} evidence`;
}

function presentExternalGlProviderStatus(value: string): string {
  const normalized = value.trim().toLowerCase();
  if (normalized.includes("fixture") && normalized.includes("import")) {
    return "Read-only import ready";
  }
  if (normalized.includes("oauth") && normalized.includes("planned")) {
    return "Direct connection unavailable";
  }
  return presentExternalGlState(value);
}

function formatExternalGlPeriodRange(start: string, end: string): string {
  return `${formatExternalGlPeriodDate(start)} – ${formatExternalGlPeriodDate(end)}`;
}

function formatExternalGlPeriodDate(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return formatUtcDate(date);
}

function formatExternalGlTimestamp(value: string | null | undefined): string {
  if (!value) {
    return "not recorded";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return formatTimestamp(date);
}

export function AccountingSystemReconciliationPanel({
  providers,
  importDetail,
  reconciliation,
  mappingProfiles,
  exportPackage,
  exportManifest,
  exportPackages,
  exportBusy,
  certifyBusy,
  actionMessage,
  actionTone,
  loading,
  error,
  onRefresh,
  onCreateExportPackage,
  onCertifyExportPackage
}: {
  providers: AccountingSystemProvider[];
  importDetail: AccountingSystemImportDetail | null;
  reconciliation: AccountingSystemReconciliationSummary | null;
  mappingProfiles: ExternalGlMappingProfile[];
  exportPackage: ExternalGlExportPackage | null;
  exportManifest: ExternalGlExportPackageManifest | null;
  exportPackages: ExternalGlExportPackage[];
  exportBusy: boolean;
  certifyBusy: boolean;
  actionMessage: string | null;
  actionTone: "success" | "warning" | "danger" | null;
  loading: boolean;
  error: string | null;
  onRefresh: () => void;
  onCreateExportPackage: () => void;
  onCertifyExportPackage: () => void;
}) {
  const activeProvider = providers.find((provider) => provider.providerId === importDetail?.summary.providerId)
    ?? providers.find((provider) => provider.providerId === "quickbooks" && provider.state === "Available")
    ?? providers.find((provider) => provider.providerId === "quickbooks-fixture")
    ?? providers[0]
    ?? null;
  const quickBooksProvider = providers.find((provider) => provider.providerId === "quickbooks");
  const selectedCompanyLabel = activeProvider?.connection?.companyName ?? activeProvider?.connection?.companyId ?? null;
  const rows = reconciliation?.rows ?? [];
  const evidencePackages = reconciliation?.evidencePackages?.slice(0, 4) ?? [];
  const mappedProviderIds = new Set(mappingProfiles.map((profile) => profile.providerId));
  const selectedMappingProfile = activeProvider
    ? mappingProfiles.find((profile) => profile.providerId === activeProvider.providerId) ?? null
    : mappingProfiles[0] ?? null;
  const statementBalance = reconciliation ? reconciliation.totalExternalDebits - reconciliation.totalExternalCredits : 0;
  const ledgerBalance = reconciliation ? reconciliation.totalMeridianDebits - reconciliation.totalMeridianCredits : 0;
  const varianceBalance = statementBalance - ledgerBalance;
  const reconciliationCleared = Boolean(reconciliation && varianceBalance === 0 && reconciliation.breakCount === 0);
  const exportSafeguards = reconciliation
    ? buildExternalGlExportSafeguards(reconciliation, selectedMappingProfile, exportPackage, exportManifest)
    : [];
  const retainedExportPackages = exportPackages.slice(0, 5);
  const reconciliationEvidenceReferences = reconciliation
    ? Array.from(new Set([
        ...reconciliation.evidenceReferences,
        ...reconciliation.rows.flatMap((row) => [
          row.evidenceRef,
          ...(row.externalEvidenceReferences ?? []),
          ...(row.meridianEvidenceReferences ?? []),
          ...(row.evidenceReferences ?? [])
        ])
      ].filter((reference): reference is string => Boolean(reference))))
    : [];
  const exportDisabledReason = !reconciliation
    ? "Load external GL reconciliation before creating an export package."
    : !selectedMappingProfile
      ? "Create or certify an external GL mapping profile before export package preparation."
      : "";
  const certificationState = exportPackage?.certification?.state ?? null;
  const certifyDisabledReason = !exportPackage
    ? "Create an external GL export package before certification."
    : certificationState === "Certified"
      ? "This external GL export package is already certified."
      : certificationState !== "ReadyForReview"
        ? `Certification requires Ready for review state; current state is ${presentExternalGlState(certificationState, "missing")}.`
        : exportPackage.validationIssues.some((issue) => issue.severity === "Critical")
          ? "Critical export package validation issues must be cleared before certification."
          : "";

  return (
    <Card id="external-gl-reconciliation" className="panel-surface scroll-mt-6" role="region" aria-label="External GL reconciliation">
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <div className="eyebrow-label eyebrow-label--sentence">External GL reconciliation</div>
            <CardTitle className="mt-2 flex items-center gap-2 text-base">
              <BookCheck className="h-4 w-4 text-primary" aria-hidden="true" />
              {activeProvider ? presentExternalGlProviderEvidenceLabel(activeProvider.displayName) : "External GL evidence"}
            </CardTitle>
            <CardDescription className="mt-2">
              External accounting-system records are imported as read-only evidence against Meridian-owned ledger truth; posting back to the external GL remains disabled.
            </CardDescription>
            {selectedCompanyLabel ? (
              <div className="mt-2 text-xs text-muted-foreground">
                Selected company: {activeProvider?.connection?.companyName ?? "Connected company"}
              </div>
            ) : null}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {activeProvider ? <Badge variant="success">{presentExternalGlProviderStatus(activeProvider.statusLabel)}</Badge> : null}
            {quickBooksProvider && quickBooksProvider !== activeProvider ? <Badge variant="outline">{presentExternalGlProviderStatus(quickBooksProvider.statusLabel)}</Badge> : null}
            <Button size="sm" variant="outline" onClick={onRefresh} disabled={loading} busy={loading} busyLabel="Refreshing GL evidence">
              <RefreshCcw className={cn("h-3.5 w-3.5", loading && "animate-spin")} aria-hidden="true" />
              Refresh
            </Button>
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {error ? (
          <StatusBanner tone="danger" role="alert" title={error} />
        ) : null}
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <AccountingValue label="Import state" value={presentExternalGlState(importDetail?.summary.state, loading ? "Loading" : "Not loaded")} />
          <AccountingValue label="Trial balance lines" value={String(importDetail?.summary.trialBalanceLineCount ?? 0)} />
          <AccountingValue label="Matched rows" value={String(reconciliation?.matchedCount ?? 0)} />
          <AccountingValue label="Break rows" value={String(reconciliation?.breakCount ?? 0)} />
        </div>
        {providers.length > 0 ? (
          <div className="overflow-x-auto rounded-md border border-border/70 bg-background/70 shadow-xs">
            <TableWorkbench<AccountingSystemProvider>
              workbenchId="accounting-external-gl-providers" sourceKey={importDetail?.summary.importId ?? "provider-catalog"}
              sourceDescription="All authorized External GL provider capabilities in the current catalog snapshot."
              completePopulation={!loading && !error} rows={providers} getRowId={(provider) => provider.providerId}
              getSearchText={(provider) => `${provider.displayName} ${provider.state} ${provider.connection?.statusLabel ?? ""}`}
              ariaLabel="External GL provider import posture" emptyText="No External GL providers are available."
              columns={[
                { id: "provider", label: "Provider", render: (provider) => presentExternalGlProviderName(provider.displayName), exportValue: (provider) => provider.displayName },
                { id: "state", label: "State", render: (provider) => <DesignSystemStatus status={provider.state === "Available" ? "ready" : provider.state === "Planned" ? "info" : "action"}>{presentExternalGlState(provider.state)}</DesignSystemStatus>, exportValue: (provider) => provider.state },
                { id: "connection", label: "Connection", render: (provider) => provider.connection?.statusLabel ?? (provider.state === "Planned" ? "Direct connection unavailable" : provider.requiresCredentials ? "Credentials required" : "No credentials required"), exportValue: (provider) => provider.connection?.statusLabel ?? null },
                { id: "imports", label: "Import", render: (provider) => [provider.supportsChartOfAccounts ? "chart" : null, provider.supportsJournalEntries ? "journals" : null, provider.supportsTrialBalance ? "trial balance" : null].filter(Boolean).join(", ") || "Import: not configured", exportValue: (provider) => [provider.supportsChartOfAccounts ? "chart" : null, provider.supportsJournalEntries ? "journals" : null, provider.supportsTrialBalance ? "trial balance" : null].filter(Boolean).join(", ") },
                { id: "mapping", label: "Mapping", render: (provider) => <><div>{mappedProviderIds.has(provider.providerId) ? "Mapping profile retained" : "Mapping profile needed"}</div><div className="mt-1 text-xs">Requires: {(provider.mappingRequirements ?? []).filter((requirement) => requirement.requiredForGuardedExport).map((requirement) => requirement.label).join(", ") || "mapping evidence"}</div></>, exportValue: (provider) => mappedProviderIds.has(provider.providerId) ? "Mapping profile retained" : "Mapping profile needed" },
                { id: "posting", label: "Posting", render: (provider) => provider.supportsPosting ? "Posting gated by export certification" : "Live posting disabled", exportValue: (provider) => provider.supportsPosting }
              ]}
            />
            <TechnicalDetails label="Provider identifiers" className="m-3">
              <dl className="grid gap-3 text-xs sm:grid-cols-2">
                {providers.slice(0, 6).map((provider) => (
                  <div key={provider.providerId}>
                    <dt className="font-semibold text-foreground">{presentExternalGlProviderName(provider.displayName)}</dt>
                    <dd className="mt-1 break-all font-mono text-muted-foreground">Provider: {provider.providerId}</dd>
                    {provider.connection?.companyId ? (
                      <dd className="mt-1 break-all font-mono text-muted-foreground">Company: {provider.connection.companyId}</dd>
                    ) : null}
                  </div>
                ))}
              </dl>
            </TechnicalDetails>
          </div>
        ) : null}
        {mappingProfiles.length > 0 ? (
          <div className="overflow-x-auto rounded-md border border-border/70 bg-background/70 shadow-xs">
            <TableWorkbench<ExternalGlMappingProfile>
              workbenchId="accounting-external-gl-mappings" sourceKey={activeProvider?.providerId ?? "none"}
              sourceDescription="All authorized retained External GL mapping profiles in this snapshot."
              completePopulation={!loading && !error} rows={mappingProfiles} getRowId={(profile) => profile.profileId}
              getSearchText={(profile) => `${profile.displayName} ${profile.providerId} ${profile.certificationState}`}
              ariaLabel="External GL mapping profiles" emptyText="No External GL mapping profiles are available."
              columns={[
                { id: "profile", label: "Profile", render: (profile) => profile.displayName, exportValue: (profile) => profile.displayName },
                { id: "provider", label: "Provider", render: (profile) => presentExternalGlProviderName(providers.find((provider) => provider.providerId === profile.providerId)?.displayName ?? "External GL provider"), exportValue: (profile) => profile.providerId },
                { id: "accounts", label: "Accounts", align: "right", render: (profile) => Object.keys(profile.accountMappings).length, exportValue: (profile) => Object.keys(profile.accountMappings).length },
                { id: "dimensions", label: "Dimensions", align: "right", render: (profile) => `${profile.dimensionMappings.length}/${profile.dimensionMappings.reduce((count, mapping) => count + Object.keys(mapping.externalDimensions.externalGlDimensions ?? {}).length, 0)}`, exportValue: (profile) => profile.dimensionMappings.length },
                { id: "certification", label: "Certification", render: (profile) => <DesignSystemStatus status={profile.certificationState}>{presentExternalGlState(profile.certificationState)}</DesignSystemStatus>, exportValue: (profile) => profile.certificationState }
              ]}
            />
            <TechnicalDetails label="Mapping profile identifiers" className="m-3">
              <dl className="grid gap-3 text-xs sm:grid-cols-2">
                {mappingProfiles.slice(0, 4).map((profile) => (
                  <div key={profile.profileId}>
                    <dt className="font-semibold text-foreground">{profile.displayName}</dt>
                    <dd className="mt-1 break-all font-mono text-muted-foreground">Profile: {profile.profileId}</dd>
                    <dd className="mt-1 break-all font-mono text-muted-foreground">Provider: {profile.providerId}</dd>
                  </div>
                ))}
              </dl>
            </TechnicalDetails>
          </div>
        ) : (
          <p className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
            No external GL mapping profile is loaded for this provider.
          </p>
        )}
        {reconciliation ? (
          <div className="grid min-w-0 gap-3 2xl:grid-cols-[minmax(0,1fr)_minmax(18rem,0.34fr)]">
            <div className="h-fit min-w-0 overflow-hidden rounded-md border border-border/70 bg-background/70 shadow-xs">
              <div className="border-b border-border/70 bg-secondary/35 px-4 py-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="text-xs font-semibold text-muted-foreground">
                    Cash reconciliation - broker statement vs. ledger
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <Badge variant="success">{reconciliation.matchedCount} matched</Badge>
                    <Badge variant={reconciliation.breakCount > 0 ? "warning" : "success"}>{reconciliation.breakCount} open</Badge>
                  </div>
                </div>
              </div>
              <div className="overflow-x-auto">
                <TableWorkbench<(typeof rows)[number]>
                  workbenchId="accounting-external-gl-reconciliation" sourceKey={reconciliation.reconciliationId}
                  sourceDescription="All rows in the selected retained External GL reconciliation. Source debit/credit amounts retain their own currency."
                  completePopulation rows={rows} getRowId={(row) => row.rowId}
                  getSearchText={(row) => `${row.accountName} ${row.accountCode} ${row.status} ${row.currency}`}
                  ariaLabel="External GL reconciliation rows" emptyText="No External GL reconciliation rows."
                  columns={[
                    { id: "statement", label: "Statement", render: (row) => <><div className="font-semibold">{row.accountName}</div><div className="font-mono text-xs text-muted-foreground">{row.accountCode}</div></>, exportValue: (row) => row.accountName },
                    { id: "statement-debit", label: "Statement debit", align: "right", render: (row) => formatGlAmount(row.externalDebit, row.currency), exportValue: (row) => row.externalDebit },
                    { id: "statement-credit", label: "Statement credit", align: "right", render: (row) => formatGlAmount(row.externalCredit, row.currency), exportValue: (row) => row.externalCredit },
                    { id: "ledger", label: "Ledger", render: (row) => <><div>{row.accountName}</div><div className="text-xs text-muted-foreground">Meridian ledger · {row.accountCode}</div></>, exportValue: (row) => row.accountCode },
                    { id: "ledger-debit", label: "Ledger debit", align: "right", render: (row) => formatGlAmount(row.meridianDebit, row.currency), exportValue: (row) => row.meridianDebit },
                    { id: "ledger-credit", label: "Ledger credit", align: "right", render: (row) => formatGlAmount(row.meridianCredit, row.currency), exportValue: (row) => row.meridianCredit },
                    { id: "currency", label: "Currency", render: (row) => row.currency || "Unavailable", exportValue: (row) => row.currency },
                    { id: "status", label: "Status", render: (row) => <DesignSystemStatus status={row.status === "MissingMeridian" ? "blocked" : row.status}>{presentExternalGlState(row.status)}</DesignSystemStatus>, exportValue: (row) => row.status }
                  ]}
                />
              </div>
              <TechnicalDetails label="Reconciliation technical details" className="m-3">
                <dl className="grid gap-3 text-xs sm:grid-cols-2">
                  <div>
                    <dt className="font-semibold text-foreground">Reconciliation</dt>
                    <dd className="mt-1 break-all font-mono text-muted-foreground">{reconciliation.reconciliationId}</dd>
                  </div>
                  <div>
                    <dt className="font-semibold text-foreground">Import</dt>
                    <dd className="mt-1 break-all font-mono text-muted-foreground">{reconciliation.importId}</dd>
                  </div>
                </dl>
                {reconciliationEvidenceReferences.length > 0 ? (
                  <div className="mt-3">
                    <div className="font-semibold text-foreground">Evidence references</div>
                    <ul className="mt-1 space-y-1">
                      {reconciliationEvidenceReferences.map((reference) => (
                        <li key={reference} className="break-all font-mono text-xs text-muted-foreground">{reference}</li>
                      ))}
                    </ul>
                  </div>
                ) : null}
              </TechnicalDetails>
              <div className="grid gap-3 border-t border-border/80 bg-secondary/30 px-4 py-3 text-sm sm:grid-cols-[1fr_1fr_auto]">
                <AccountingValue label="Statement balance" value={formatGlAmount(statementBalance, rows[0]?.currency ?? "USD")} />
                <AccountingValue label="Ledger balance" value={formatGlAmount(ledgerBalance, rows[0]?.currency ?? "USD")} />
                <div className="flex items-center justify-start sm:justify-end">
                  <Badge variant={reconciliationCleared ? "success" : "warning"} dot>
                    {reconciliationCleared
                      ? "Balanced"
                      : reconciliation.breakCount > 0
                        ? `${reconciliation.breakCount} open break${reconciliation.breakCount === 1 ? "" : "s"}`
                        : `Out by ${formatGlAmount(Math.abs(varianceBalance), rows[0]?.currency ?? "USD")}`}
                  </Badge>
                </div>
              </div>
            </div>
            <div className="row-detail-panel h-fit min-w-0 text-sm" data-selected-source="Selected external GL scope">
              <div className="flex items-start justify-between gap-2">
                <div>
                  <div className="font-semibold text-foreground">Posting/export</div>
                  <div className="mt-1 text-[11px] text-muted-foreground">
                    {selectedMappingProfile ? `Mapping: ${selectedMappingProfile.displayName}` : "Mapping profile required"}
                  </div>
                </div>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={onCreateExportPackage}
                  disabled={exportBusy || certifyBusy || Boolean(exportDisabledReason)}
                  disabledReason={exportDisabledReason}
                  busy={exportBusy}
                  busyLabel="Creating export package"
                >
                  Create export package
                </Button>
                <Button
                  size="sm"
                  variant="secondary"
                  onClick={onCertifyExportPackage}
                  disabled={certifyBusy || Boolean(certifyDisabledReason)}
                  disabledReason={certifyDisabledReason}
                  busy={certifyBusy}
                  busyLabel="Certifying export package"
                >
                  Certify export
                </Button>
              </div>
              <p className="mt-2 text-xs leading-5 text-muted-foreground">{reconciliation.postingDisabledReason}</p>
              <div className="mt-3 rounded-md border border-border/70 bg-background/50 px-2.5 py-2" aria-label="External GL export safeguards">
                <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Export safeguards</div>
                <div className="mt-2 grid gap-2">
                  {exportSafeguards.map((safeguard) => (
                    <div key={safeguard.id} className="flex items-start justify-between gap-3 rounded border border-border/60 bg-secondary/20 px-2 py-1.5 text-xs">
                      <div className="min-w-0">
                        <div className="font-semibold text-foreground">{safeguard.label}</div>
                        <div className="mt-0.5 text-muted-foreground">{safeguard.detail}</div>
                      </div>
                      <Badge variant={safeguard.tone}>{safeguard.value}</Badge>
                    </div>
                  ))}
                </div>
              </div>
              {actionMessage ? (
                <div
                  role={actionTone === "danger" ? "alert" : "status"}
                  className={cn(
                    "mt-3 rounded-md border px-2.5 py-2 text-xs leading-5",
                    actionTone === "danger" ? "border-danger/30 bg-danger/10 text-danger" : null,
                    actionTone === "warning" ? "border-warning/30 bg-warning/10 text-warning" : null,
                    actionTone === "success" ? "border-success/30 bg-success/10 text-success" : null
                  )}
                >
                  {actionMessage}
                </div>
              ) : null}
              {exportPackage ? (
                <div className="mt-3 rounded-md border border-border/70 bg-background/50 px-2.5 py-2" aria-label="External GL export package">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="text-xs font-semibold text-foreground">Guarded export package</div>
                      <div className="mt-1 text-[11px] text-muted-foreground">
                        {formatExternalGlPeriodRange(exportPackage.periodStart, exportPackage.periodEnd)}
                      </div>
                    </div>
                    <Badge variant={exportPackage.postingEnabled ? "success" : "warning"}>
                      {exportPackage.postingEnabled ? "Posting enabled" : "Posting disabled"}
                    </Badge>
                  </div>
                  <p className="mt-2 text-[11px] leading-4 text-muted-foreground">{exportPackage.postingDisabledReason}</p>
                  <div className="mt-2 grid gap-2">
                    <AccountingValue label="Certification" value={presentExternalGlState(exportPackage.certification?.state, "Not certified")} />
                    <AccountingValue label="Evidence" value={String(exportPackage.evidenceLinks.length)} />
                    <AccountingValue label="Validation issues" value={String(exportPackage.validationIssues.length)} />
                    <AccountingValue label="Manifest lines" value={String(exportManifest?.generatedLines.length ?? exportPackage.generatedLines?.length ?? 0)} />
                    <AccountingValue label="External posting" value={exportManifest?.externalPostingAllowed === false ? "Disabled" : "Not allowed"} />
                  </div>
                  <ExternalGlExportPackageTechnicalDetails exportPackage={exportPackage} exportManifest={exportManifest} />
                </div>
              ) : null}
              <div className="mt-3 rounded-md border border-border/70 bg-background/50 px-2.5 py-2" aria-label="External GL export package history">
                <div className="flex items-start justify-between gap-2">
                  <div>
                    <div className="text-xs font-semibold text-muted-foreground">Retained package history</div>
                    <div className="mt-1 text-[11px] text-muted-foreground">
                      {exportPackages.length} retained package{exportPackages.length === 1 ? "" : "s"} for the selected provider, fund, and ledger book.
                    </div>
                  </div>
                  <Badge variant={exportPackages.length > 0 ? "success" : "outline"}>{exportPackages.length}</Badge>
                </div>
                {retainedExportPackages.length > 0 ? (
                  <div className="mt-2 space-y-2">
                    {retainedExportPackages.map((packageRow) => (
                      <div key={packageRow.exportPackageId} className="rounded border border-border/60 bg-secondary/20 px-2 py-1.5 text-xs">
                        <div className="flex items-start justify-between gap-2">
                          <div className="min-w-0">
                            <div className="font-semibold text-foreground">Guarded export package</div>
                            <div className="mt-0.5 font-mono text-[11px] text-muted-foreground">
                              {formatExternalGlPeriodRange(packageRow.periodStart, packageRow.periodEnd)} | Created {formatExternalGlTimestamp(packageRow.createdAtUtc)}
                            </div>
                          </div>
                          <Badge variant={packageRow.certification?.state === "Certified" ? "success" : packageRow.certification?.state === "ReadyForReview" ? "warning" : "outline"}>
                            {presentExternalGlState(packageRow.certification?.state, "Uncertified")}
                          </Badge>
                        </div>
                        <div className="mt-1 grid gap-1 sm:grid-cols-3">
                          <AccountingValue label="Evidence" value={String(packageRow.evidenceLinks.length)} />
                          <AccountingValue label="Issues" value={String(packageRow.validationIssues.length)} />
                          <AccountingValue label="Posting" value={packageRow.postingEnabled ? "Enabled" : "Disabled"} />
                        </div>
                        <ExternalGlExportPackageTechnicalDetails exportPackage={packageRow} exportManifest={null} />
                      </div>
                    ))}
                  </div>
                ) : (
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">
                    No retained guarded export packages have been loaded for this scope.
                  </p>
                )}
              </div>
              {evidencePackages.length > 0 ? (
                <div className="mt-3 space-y-2" aria-label="External GL evidence packages">
                  {evidencePackages.map((evidencePackage) => (
                    <AccountingSystemEvidencePackageRow key={evidencePackage.packageId} evidencePackage={evidencePackage} />
                  ))}
                </div>
              ) : (
                <p className="mt-3 rounded-md border border-border/70 bg-background/50 px-2.5 py-2 text-xs text-muted-foreground">
                  {reconciliationEvidenceReferences.length > 0
                    ? `${reconciliationEvidenceReferences.length} technical evidence reference${reconciliationEvidenceReferences.length === 1 ? " is" : "s are"} retained in reconciliation details.`
                    : "No reconciliation evidence references are retained for this scope."}
                </p>
              )}
            </div>
          </div>
        ) : (
          <p className="rounded-md border border-border/70 bg-secondary/25 px-3 py-3 text-sm text-muted-foreground">
            External GL reconciliation has not been loaded yet.
          </p>
        )}
      </CardContent>
    </Card>
  );
}

function ExternalGlExportPackageTechnicalDetails({
  exportPackage,
  exportManifest
}: {
  exportPackage: ExternalGlExportPackage;
  exportManifest: ExternalGlExportPackageManifest | null;
}) {
  return (
    <TechnicalDetails label="Export package technical details" className="mt-2">
      <dl className="grid gap-3 text-xs sm:grid-cols-2">
        <div>
          <dt className="font-semibold text-foreground">Package identifier</dt>
          <dd className="mt-1 break-all font-mono text-muted-foreground">{exportPackage.exportPackageId}</dd>
        </div>
        <div>
          <dt className="font-semibold text-foreground">Provider identifier</dt>
          <dd className="mt-1 break-all font-mono text-muted-foreground">{exportPackage.providerId}</dd>
        </div>
        <div>
          <dt className="font-semibold text-foreground">Fund profile</dt>
          <dd className="mt-1 break-all font-mono text-muted-foreground">{exportPackage.fundProfileId}</dd>
        </div>
        <div>
          <dt className="font-semibold text-foreground">Ledger book</dt>
          <dd className="mt-1 break-all font-mono text-muted-foreground">{exportPackage.ledgerBookId ?? "Not assigned"}</dd>
        </div>
        <div>
          <dt className="font-semibold text-foreground">Mapping profile</dt>
          <dd className="mt-1 break-all font-mono text-muted-foreground">{exportPackage.mappingProfileId ?? "Not retained"}</dd>
        </div>
        <div>
          <dt className="font-semibold text-foreground">Reconciliation</dt>
          <dd className="mt-1 break-all font-mono text-muted-foreground">{exportPackage.reconciliationId ?? "Not retained"}</dd>
        </div>
        {exportManifest ? (
          <>
            <div>
              <dt className="font-semibold text-foreground">Manifest file</dt>
              <dd className="mt-1 break-all font-mono text-muted-foreground">{exportManifest.fileName}</dd>
            </div>
            <div>
              <dt className="font-semibold text-foreground">Manifest hash</dt>
              <dd className="mt-1 break-all font-mono text-muted-foreground">{exportManifest.contentHash}</dd>
            </div>
          </>
        ) : null}
      </dl>
      {exportPackage.evidenceLinks.length > 0 ? (
        <div className="mt-3">
          <div className="font-semibold text-foreground">Evidence references</div>
          <ul className="mt-1 space-y-1">
            {exportPackage.evidenceLinks.map((reference) => (
              <li key={reference} className="break-all font-mono text-xs text-muted-foreground">{reference}</li>
            ))}
          </ul>
        </div>
      ) : null}
    </TechnicalDetails>
  );
}

function AccountingSystemEvidencePackageRow({
  evidencePackage
}: {
  evidencePackage: AccountingSystemReconciliationEvidencePackage;
}) {
  const requiredActions = evidencePackage.requiredActions.slice(0, 2);

  return (
    <div className="rounded-md border border-border/60 bg-background/40 px-2.5 py-2">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="truncate text-xs font-semibold text-foreground">{evidencePackage.label}</div>
          <div className="mt-1 text-[11px] text-muted-foreground">
            {evidencePackage.evidenceReferenceCount} retained evidence item{evidencePackage.evidenceReferenceCount === 1 ? "" : "s"}
          </div>
        </div>
        <Badge variant={accountingSystemEvidencePackageVariant[evidencePackage.status]}>
          {presentExternalGlState(evidencePackage.status)}
        </Badge>
      </div>
      {requiredActions.length > 0 ? (
        <ul className="mt-2 list-disc space-y-1 pl-4 text-[11px] leading-4 text-muted-foreground">
          {requiredActions.map((action) => (
            <li key={action}>{action}</li>
          ))}
        </ul>
      ) : evidencePackage.evidenceReferenceCount > 0 ? (
        <p className="mt-2 text-[11px] leading-4 text-muted-foreground">Evidence is retained and ready for operator review.</p>
      ) : null}
      <TechnicalDetails label="Evidence package technical details" className="mt-2">
        <dl className="text-xs">
          <dt className="font-semibold text-foreground">Package identifier</dt>
          <dd className="mt-1 break-all font-mono text-muted-foreground">{evidencePackage.packageId}</dd>
        </dl>
        {evidencePackage.evidenceReferences.length > 0 ? (
          <div className="mt-3">
            <div className="font-semibold text-foreground">Evidence references</div>
            <ul className="mt-1 space-y-1">
              {evidencePackage.evidenceReferences.map((reference) => (
                <li key={reference} className="break-all font-mono text-xs text-muted-foreground">{reference}</li>
              ))}
            </ul>
          </div>
        ) : null}
      </TechnicalDetails>
    </div>
  );
}

function buildExternalGlExportSafeguards(
  reconciliation: AccountingSystemReconciliationSummary,
  mappingProfile: ExternalGlMappingProfile | null,
  exportPackage: ExternalGlExportPackage | null,
  exportManifest: ExternalGlExportPackageManifest | null
): Array<{ id: string; label: string; value: string; detail: string; tone: "outline" | "success" | "warning" | "danger" }> {
  const criticalIssueCount = exportPackage?.validationIssues.filter((issue) => issue.severity === "Critical").length ?? 0;
  const mappingCertified = mappingProfile?.certificationState === "Certified";
  const manifestLineCount = exportManifest?.generatedLines.length ?? exportPackage?.generatedLines?.length ?? 0;
  const externalPostingAllowed = exportManifest?.externalPostingAllowed === true || exportPackage?.postingEnabled === true;

  return [
    {
      id: "balanced-reconciliation",
      label: "Balanced reconciliation required",
      value: reconciliation.breakCount === 0 ? "Ready" : "Blocked",
      detail: reconciliation.breakCount === 0
        ? "No open reconciliation breaks are returned for this external GL period."
        : `${reconciliation.breakCount} reconciliation break(s) must clear before certification.`,
      tone: reconciliation.breakCount === 0 ? "success" : "warning"
    },
    {
      id: "mapping-certification",
      label: "Certified mapping profile",
      value: presentExternalGlState(mappingProfile?.certificationState, "Missing"),
      detail: mappingProfile
        ? `${mappingProfile.displayName} maps ${Object.keys(mappingProfile.accountMappings).length} account(s) and ${mappingProfile.dimensionMappings.length} dimension set(s).`
        : "A retained account and dimension mapping profile is required before export.",
      tone: mappingCertified ? "success" : "warning"
    },
    {
      id: "validation-blockers",
      label: "Critical validation blockers",
      value: String(criticalIssueCount),
      detail: exportPackage
        ? criticalIssueCount > 0
          ? "Critical package issues must clear before certification."
          : `${exportPackage.validationIssues.length} package validation issue(s) retained; none are critical.`
        : "No export package has been retained yet.",
      tone: criticalIssueCount > 0 ? "danger" : exportPackage ? "success" : "outline"
    },
    {
      id: "manifest-control",
      label: "Controlled export manifest",
      value: exportManifest ? "Retained" : "Not loaded",
      detail: `${manifestLineCount} generated mapped export line(s) retained for review.`,
      tone: exportManifest ? "success" : "outline"
    },
    {
      id: "live-posting-gate",
      label: "Live external posting gate",
      value: externalPostingAllowed ? "Enabled" : "Disabled",
      detail: exportManifest?.postingDisabledReason ?? exportPackage?.postingDisabledReason ?? reconciliation.postingDisabledReason,
      tone: externalPostingAllowed ? "danger" : "success"
    }
  ];
}

function formatGlAmount(value: number, currency: string): string {
  return formatCurrencyAmount(value, { currency });
}

export function mergeExternalGlExportPackage(
  packages: ExternalGlExportPackage[],
  nextPackage: ExternalGlExportPackage
): ExternalGlExportPackage[] {
  const remaining = packages.filter((packageRow) => packageRow.exportPackageId !== nextPackage.exportPackageId);
  return [nextPackage, ...remaining].sort((left, right) => right.createdAtUtc.localeCompare(left.createdAtUtc));
}
