import { BookCheck, RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import type {
  AccountingSystemImportDetail,
  AccountingSystemReconciliationEvidencePackage,
  AccountingSystemProvider,
  AccountingSystemReconciliationSummary,
  ExternalGlExportPackage,
  ExternalGlExportPackageManifest,
  ExternalGlMappingProfile
} from "@/types";

const accountingSystemStatusVariant = {
  Matched: "success",
  Variance: "warning",
  MissingExternal: "warning",
  MissingMeridian: "danger",
  ReviewRequired: "warning"
} as const;

const accountingSystemEvidencePackageVariant = {
  Ready: "success",
  ReviewRequired: "warning",
  Missing: "danger"
} as const;

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
  const rows = reconciliation?.rows.slice(0, 5) ?? [];
  const evidencePackages = reconciliation?.evidencePackages?.slice(0, 4) ?? [];
  const mappedProviderIds = new Set(mappingProfiles.map((profile) => profile.providerId));
  const selectedMappingProfile = activeProvider
    ? mappingProfiles.find((profile) => profile.providerId === activeProvider.providerId) ?? null
    : mappingProfiles[0] ?? null;
  const statementBalance = reconciliation ? reconciliation.totalExternalDebits - reconciliation.totalExternalCredits : 0;
  const ledgerBalance = reconciliation ? reconciliation.totalMeridianDebits - reconciliation.totalMeridianCredits : 0;
  const varianceBalance = statementBalance - ledgerBalance;
  const exportSafeguards = reconciliation
    ? buildExternalGlExportSafeguards(reconciliation, selectedMappingProfile, exportPackage, exportManifest)
    : [];
  const retainedExportPackages = exportPackages.slice(0, 5);
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
        ? `Certification requires Ready for review state; current state is ${certificationState ?? "missing"}.`
        : exportPackage.validationIssues.some((issue) => issue.severity === "Critical")
          ? "Critical export package validation issues must be cleared before certification."
          : "";

  return (
    <Card id="external-gl-reconciliation" className="panel-surface scroll-mt-6" role="region" aria-label="External GL reconciliation">
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <div className="eyebrow-label">External GL reconciliation</div>
            <CardTitle className="mt-2 flex items-center gap-2 text-base">
              <BookCheck className="h-4 w-4 text-primary" aria-hidden="true" />
              {activeProvider ? `${activeProvider.displayName} evidence` : "External GL evidence"}
            </CardTitle>
            <CardDescription className="mt-2">
              External accounting-system records are imported as read-only evidence against Meridian-owned ledger truth; posting back to the external GL remains disabled.
            </CardDescription>
            {selectedCompanyLabel ? (
              <div className="mt-2 text-xs text-muted-foreground">Selected company: {selectedCompanyLabel}</div>
            ) : null}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {activeProvider ? <Badge variant="success">{activeProvider.statusLabel}</Badge> : null}
            {quickBooksProvider && quickBooksProvider !== activeProvider ? <Badge variant="outline">{quickBooksProvider.statusLabel}</Badge> : null}
            <Button size="sm" variant="outline" onClick={onRefresh} disabled={loading} busy={loading} busyLabel="Refreshing GL evidence">
              <RefreshCcw className={cn("h-3.5 w-3.5", loading && "animate-spin")} aria-hidden="true" />
              Refresh
            </Button>
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {error ? (
          <div role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">
            {error}
          </div>
        ) : null}
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <ExternalGlValue label="Import state" value={importDetail?.summary.state ?? (loading ? "Loading" : "Not loaded")} />
          <ExternalGlValue label="Trial balance lines" value={String(importDetail?.summary.trialBalanceLineCount ?? 0)} />
          <ExternalGlValue label="Matched rows" value={String(reconciliation?.matchedCount ?? 0)} />
          <ExternalGlValue label="Break rows" value={String(reconciliation?.breakCount ?? 0)} />
        </div>
        {providers.length > 0 ? (
          <div className="grid gap-2 md:grid-cols-3" aria-label="External GL provider import posture">
            {providers.slice(0, 6).map((provider) => {
              const hasMappingProfile = mappedProviderIds.has(provider.providerId);
              const importKinds = [
                provider.supportsChartOfAccounts ? "chart" : null,
                provider.supportsJournalEntries ? "journals" : null,
                provider.supportsTrialBalance ? "trial balance" : null
              ].filter((item): item is string => Boolean(item));
              const connectionLabel = provider.connection?.statusLabel
                ?? (provider.requiresCredentials ? "Credentials required" : "No credentials required");
              const postingLabel = provider.supportsPosting
                ? "Posting gated by export certification"
                : "Live posting disabled";
              const mappingRequirementLabels = (provider.mappingRequirements ?? [])
                .filter((requirement) => requirement.requiredForGuardedExport)
                .slice(0, 2)
                .map((requirement) => requirement.label);

              return (
                <div
                  key={provider.providerId}
                  className={cn(
                    "rounded-md border bg-secondary/15 px-3 py-2 text-sm",
                    provider.providerId === activeProvider?.providerId ? "border-primary/45" : "border-border/70"
                  )}
                >
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="truncate font-semibold text-foreground">{provider.displayName}</div>
                      <div className="mt-1 truncate font-mono text-[11px] text-muted-foreground">{provider.providerId}</div>
                    </div>
                    <Badge variant={provider.state === "Available" ? "success" : provider.state === "Planned" ? "outline" : "warning"}>
                      {provider.state}
                    </Badge>
                  </div>
                  <div className="mt-2 space-y-1 text-[11px] leading-4 text-muted-foreground">
                    <div>{connectionLabel}</div>
                    <div>{importKinds.length > 0 ? `Import: ${importKinds.join(", ")}` : "Import: not configured"}</div>
                    <div>{hasMappingProfile ? "Mapping profile retained" : "Mapping profile needed"}</div>
                    <div>{mappingRequirementLabels.length > 0 ? `Requires: ${mappingRequirementLabels.join(", ")}` : "Requires: mapping evidence"}</div>
                    <div>{postingLabel}</div>
                  </div>
                </div>
              );
            })}
          </div>
        ) : null}
        {mappingProfiles.length > 0 ? (
          <div className="grid gap-2 md:grid-cols-2" aria-label="External GL mapping profiles">
            {mappingProfiles.slice(0, 4).map((profile) => {
              const accountMappingCount = Object.keys(profile.accountMappings).length;
              const externalDimensionCount = profile.dimensionMappings.reduce((count, mapping) => (
                count + Object.keys(mapping.externalDimensions.externalGlDimensions ?? {}).length
              ), 0);
              const isSelected = profile.profileId === selectedMappingProfile?.profileId;

              return (
                <div
                  key={profile.profileId}
                  className={cn(
                    "rounded-md border bg-secondary/20 px-3 py-2 text-sm",
                    isSelected ? "border-primary/45" : "border-border/70"
                  )}
                >
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="truncate font-semibold text-foreground">{profile.displayName}</div>
                      <div className="mt-1 truncate font-mono text-[11px] text-muted-foreground">{profile.profileId}</div>
                    </div>
                    <Badge variant={profile.certificationState === "Certified" ? "success" : "warning"}>{profile.certificationState}</Badge>
                  </div>
                  <div className="mt-2 grid gap-2 sm:grid-cols-3">
                    <ExternalGlValue label="Provider" value={profile.providerId} />
                    <ExternalGlValue label="Accounts" value={String(accountMappingCount)} />
                    <ExternalGlValue label="Dimensions" value={`${profile.dimensionMappings.length}/${externalDimensionCount}`} />
                  </div>
                </div>
              );
            })}
          </div>
        ) : (
          <p className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
            No external GL mapping profile is loaded for this provider.
          </p>
        )}
        {reconciliation ? (
          <div className="grid gap-3 xl:grid-cols-[minmax(0,1fr)_19rem]">
            <div className="overflow-hidden rounded-md border border-border/70 bg-background/70 shadow-sm">
              <div className="border-b border-border/70 bg-secondary/35 px-4 py-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                    Cash reconciliation - broker statement vs. ledger
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <Badge variant="success">{reconciliation.matchedCount} matched</Badge>
                    <Badge variant={reconciliation.breakCount > 0 ? "warning" : "success"}>{reconciliation.breakCount} open</Badge>
                  </div>
                </div>
              </div>
              <div className="grid grid-cols-2 border-b border-border/70 bg-secondary/20 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                <div className="border-r border-border/70 px-4 py-2">Statement</div>
                <div className="px-4 py-2">Ledger</div>
              </div>
              <div>
                {rows.map((row) => {
                  const isMatched = row.status === "Matched";
                  return (
                    <div key={row.rowId} className="grid min-h-[4.65rem] grid-cols-2 border-b border-border/60 last:border-b-0">
                      <div className={cn("border-r border-l-4 border-border/70 px-4 py-3", isMatched ? "border-l-success bg-background/50" : "border-l-warning bg-warning/5")}>
                        <div className="flex items-start justify-between gap-3">
                          <div className="min-w-0">
                            <div className="truncate text-sm font-semibold text-foreground">{row.accountName}</div>
                            <div className="mt-1 truncate font-mono text-[11px] text-muted-foreground">{row.accountCode} · {row.evidenceRef}</div>
                          </div>
                          <div className="shrink-0 whitespace-nowrap text-right font-mono text-sm tabular-nums text-foreground">
                            {formatGlAmount(row.externalDebit - row.externalCredit, row.currency)}
                          </div>
                        </div>
                      </div>
                      <div className={cn("px-4 py-3", isMatched ? "border-l-4 border-l-success bg-background/50" : "border-l-4 border-l-warning bg-warning/5")}>
                        <div className="flex items-start justify-between gap-3">
                          <div className="min-w-0">
                            <div className="truncate text-sm font-semibold text-foreground">{row.accountName}</div>
                            <div className="mt-1 truncate font-mono text-[11px] text-muted-foreground">Meridian ledger · {row.accountCode}</div>
                          </div>
                          <div className="shrink-0 text-right">
                            <div className="whitespace-nowrap font-mono text-sm tabular-nums text-foreground">
                              {formatGlAmount(row.meridianDebit - row.meridianCredit, row.currency)}
                            </div>
                            <Badge className="mt-1" variant={accountingSystemStatusVariant[row.status]}>{row.status}</Badge>
                          </div>
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
              <div className="grid gap-3 border-t border-border/80 bg-secondary/30 px-4 py-3 text-sm sm:grid-cols-[1fr_1fr_auto]">
                <ExternalGlValue label="Statement balance" value={formatGlAmount(statementBalance, rows[0]?.currency ?? "USD")} />
                <ExternalGlValue label="Ledger balance" value={formatGlAmount(ledgerBalance, rows[0]?.currency ?? "USD")} />
                <div className="flex items-center justify-start sm:justify-end">
                  <Badge variant={varianceBalance === 0 ? "success" : "warning"} dot>
                    {varianceBalance === 0 ? "Balanced" : `Out by ${formatGlAmount(Math.abs(varianceBalance), rows[0]?.currency ?? "USD")}`}
                  </Badge>
                </div>
              </div>
            </div>
            <div className="rounded-md border border-border/70 bg-secondary/20 px-3 py-3 text-sm">
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
                      <div className="truncate text-xs font-semibold text-foreground">{exportPackage.exportPackageId}</div>
                      <div className="mt-1 text-[11px] text-muted-foreground">
                        {exportPackage.periodStart}{" -> "}{exportPackage.periodEnd}
                      </div>
                    </div>
                    <Badge variant={exportPackage.postingEnabled ? "success" : "warning"}>
                      {exportPackage.postingEnabled ? "Posting enabled" : "Posting disabled"}
                    </Badge>
                  </div>
                  <p className="mt-2 text-[11px] leading-4 text-muted-foreground">{exportPackage.postingDisabledReason}</p>
                  <div className="mt-2 grid gap-2">
                    <ExternalGlValue label="Certification" value={exportPackage.certification?.state ?? "Not certified"} />
                    <ExternalGlValue label="Evidence" value={String(exportPackage.evidenceLinks.length)} />
                    <ExternalGlValue label="Validation issues" value={String(exportPackage.validationIssues.length)} />
                    <ExternalGlValue label="Manifest hash" value={exportManifest?.contentHash.slice(0, 12) ?? "Not loaded"} />
                    <ExternalGlValue label="Manifest lines" value={String(exportManifest?.generatedLines.length ?? exportPackage.generatedLines?.length ?? 0)} />
                    <ExternalGlValue label="External posting" value={exportManifest?.externalPostingAllowed === false ? "Disabled" : "Not allowed"} />
                  </div>
                </div>
              ) : null}
              <div className="mt-3 rounded-md border border-border/70 bg-background/50 px-2.5 py-2" aria-label="External GL export package history">
                <div className="flex items-start justify-between gap-2">
                  <div>
                    <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Retained package history</div>
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
                            <div className="truncate font-semibold text-foreground">{packageRow.exportPackageId}</div>
                            <div className="mt-0.5 font-mono text-[11px] text-muted-foreground">
                              {packageRow.periodStart}{" -> "}{packageRow.periodEnd} | {formatExternalGlDate(packageRow.createdAtUtc)}
                            </div>
                          </div>
                          <Badge variant={packageRow.certification?.state === "Certified" ? "success" : packageRow.certification?.state === "ReadyForReview" ? "warning" : "outline"}>
                            {packageRow.certification?.state ?? "Uncertified"}
                          </Badge>
                        </div>
                        <div className="mt-1 grid gap-1 sm:grid-cols-3">
                          <ExternalGlValue label="Evidence" value={String(packageRow.evidenceLinks.length)} />
                          <ExternalGlValue label="Issues" value={String(packageRow.validationIssues.length)} />
                          <ExternalGlValue label="Posting" value={packageRow.postingEnabled ? "Enabled" : "Disabled"} />
                        </div>
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
                <div className="mt-3 space-y-1" aria-label="External GL evidence references">
                  {reconciliation.evidenceReferences.map((evidence) => (
                    <div key={evidence} className="truncate font-mono text-[11px] text-muted-foreground">{evidence}</div>
                  ))}
                </div>
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

export function mergeExternalGlExportPackage(
  packages: ExternalGlExportPackage[],
  nextPackage: ExternalGlExportPackage
): ExternalGlExportPackage[] {
  const remaining = packages.filter((packageRow) => packageRow.exportPackageId !== nextPackage.exportPackageId);
  return [nextPackage, ...remaining].sort((left, right) => right.createdAtUtc.localeCompare(left.createdAtUtc));
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
          <div className="mt-1 text-[11px] text-muted-foreground">{evidencePackage.evidenceReferenceCount} retained evidence ref(s)</div>
        </div>
        <Badge variant={accountingSystemEvidencePackageVariant[evidencePackage.status]}>{evidencePackage.status}</Badge>
      </div>
      {requiredActions.length > 0 ? (
        <ul className="mt-2 list-disc space-y-1 pl-4 text-[11px] leading-4 text-muted-foreground">
          {requiredActions.map((action) => (
            <li key={action}>{action}</li>
          ))}
        </ul>
      ) : evidencePackage.evidenceReferences[0] ? (
        <div className="mt-2 truncate font-mono text-[11px] text-muted-foreground">{evidencePackage.evidenceReferences[0]}</div>
      ) : null}
    </div>
  );
}

function ExternalGlValue({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border border-border/70 bg-background/50 px-3 py-2" aria-label={`${label}: ${value}`}>
      <div className="text-[11px] uppercase text-muted-foreground">{label}</div>
      <div className="mt-1 font-mono text-sm text-foreground">{value}</div>
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
  const manifestHash = exportManifest?.contentHash ? exportManifest.contentHash.slice(0, 12) : "Not loaded";
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
      value: mappingProfile?.certificationState ?? "Missing",
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
      value: manifestHash,
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
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: currency || "USD",
    maximumFractionDigits: 2
  }).format(value);
}

function formatExternalGlDate(value: string | null | undefined): string {
  if (!value) {
    return "Not recorded";
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString("en-US", { timeZone: "UTC" });
}
