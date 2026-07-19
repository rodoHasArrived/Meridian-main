import { BookCheck, Network, RefreshCcw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { cn } from "@/lib/utils";
import {
  ChartAccountPathBuilder,
  ConfigureActivationRail,
  ConfigureChangePreviewPanel,
  ConfigureCommandBar,
  ConfigureProductionReadinessCard,
  LedgerBookSetupWizard
} from "@/screens/accounting-screen.configure-panel";
import {
  accountingToolingBadgeVariant,
  accountingToolingBorderClass,
  cashFlowTextClass
} from "@/screens/accounting-screen.styles";
import type {
  AccountingConfigurationViewModel,
  AccountingRulesStudioPromotionReadinessViewModel
} from "@/screens/accounting-screen.view-model";

export function AccountingConfigurationPanel({ view }: { view: AccountingConfigurationViewModel }) {
  return (
    <section className="workspace-section-band" aria-labelledby="accounting-configure-heading">
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">Configure</p>
          <h3 id="accounting-configure-heading" className="workspace-section-title">{view.title}</h3>
          <p className="workspace-section-summary">{view.description}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={view.statusTone === "success" ? "success" : view.statusTone === "danger" ? "danger" : view.statusTone === "warning" ? "warning" : "outline"} dot>
            {view.statusLabel}
          </Badge>
          <Button
            size="sm"
            variant="outline"
            disabled={view.loading}
            disabledReason={view.loading ? "Configuration refresh is already in progress." : null}
            onClick={() => void view.refresh()}
          >
            <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
            Refresh
          </Button>
          <Button
            size="sm"
            disabled={!view.canActivate}
            disabledReason={view.activateDisabledReason}
            busy={view.activateBusy}
            busyLabel={view.activateButtonLabel}
            onClick={() => void view.activate()}
          >
            {view.activateButtonLabel}
          </Button>
        </div>
      </div>

      <ConfigureCommandBar />

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_20rem]">
        <div className="min-w-0 space-y-4">
      {view.errorText ? (
        <div role="alert" className="rounded-lg border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">
          <div className="font-semibold">{view.errorText}</div>
          {view.errorDetails.length > 0 ? (
            <ul className="mt-2 list-disc pl-4">
              {view.errorDetails.map((detail) => <li key={detail}>{detail}</li>)}
            </ul>
          ) : null}
        </div>
      ) : null}

      <div id="configure-section-setup" className="configure-anchor scroll-mt-20 grid gap-4 md:grid-cols-2 xl:grid-cols-5">
        {view.metricRows.map((metric) => (
          <div key={metric.id} className="panel-surface px-4 py-3">
            <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{metric.label}</div>
            <div className={cn("mt-2 font-mono text-xl font-semibold", cashFlowTextClass(metric.tone))}>{metric.value}</div>
            <p className="mt-2 text-xs leading-5 text-muted-foreground">{metric.detail}</p>
          </div>
        ))}
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        {view.setupReadinessRows.map((row) => (
          <div key={row.id} className="rounded-md border border-border/70 bg-secondary/20 px-4 py-3">
            <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{row.label}</div>
            <div className={cn("mt-2 text-sm font-semibold", cashFlowTextClass(row.tone))}>{row.value}</div>
            <p className="mt-2 text-xs leading-5 text-muted-foreground">{row.detail}</p>
          </div>
        ))}
      </div>
      <div className="flex flex-wrap items-center gap-3 rounded-md border border-border/70 bg-background px-4 py-3">
        <Button
          size="sm"
          variant="secondary"
          disabled={!view.canCreateLedgerBook}
          disabledReason={view.createLedgerBookDisabledReason}
          busy={view.createLedgerBookBusy}
          busyLabel={view.createLedgerBookButtonLabel}
          onClick={() => void view.createLedgerBookFromSetupCandidate()}
        >
          {view.createLedgerBookButtonLabel}
        </Button>
        <LedgerBookSetupWizard view={view} />
        {view.createLedgerBookStatusText ? (
          <p className="text-xs leading-5 text-muted-foreground">{view.createLedgerBookStatusText}</p>
        ) : null}
      </div>

      <Card id="configure-section-books" className="panel-surface configure-anchor scroll-mt-20" aria-labelledby="accounting-ledger-books-heading">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle id="accounting-ledger-books-heading">Ledger book administration</CardTitle>
              <CardDescription>{view.ledgerBookSummaryLabel}</CardDescription>
            </div>
            <Badge variant={view.ledgerBookRows.some((book) => book.statusLabel === "Selected") ? "success" : view.ledgerBookRows.length > 0 ? "warning" : "outline"}>
              {view.ledgerBookRows.length} book{view.ledgerBookRows.length === 1 ? "" : "s"}
            </Badge>
          </div>
        </CardHeader>
        <CardContent>
          {view.ledgerBookEmptyText ? (
            <p className="text-sm text-muted-foreground">{view.ledgerBookEmptyText}</p>
          ) : (
            <div className="grid gap-3 lg:grid-cols-2" role="region" aria-label="Accounting ledger book catalog">
              {view.ledgerBookRows.map((book) => (
                <div key={book.id} className={cn("rounded-md border px-4 py-3", accountingToolingBorderClass(book.tone))}>
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="font-semibold text-foreground">{book.title}</div>
                      <div className="mt-1 font-mono text-[11px] uppercase tracking-[0.08em] text-muted-foreground">{book.id}</div>
                    </div>
                    <Badge variant={accountingToolingBadgeVariant(book.tone)}>{book.statusLabel}</Badge>
                  </div>
                  <p className="mt-2 text-xs leading-5 text-muted-foreground">{book.description}</p>
                  <dl className="mt-3 grid gap-2 text-xs md:grid-cols-2">
                    <div>
                      <dt className="font-semibold text-muted-foreground">Basis</dt>
                      <dd className="mt-1 text-foreground">{book.subtitle}</dd>
                    </div>
                    <div>
                      <dt className="font-semibold text-muted-foreground">Policy</dt>
                      <dd className="mt-1 text-foreground">{book.policyLabel}</dd>
                    </div>
                    <div>
                      <dt className="font-semibold text-muted-foreground">Currency</dt>
                      <dd className="mt-1 text-foreground">{book.currencyLabel}</dd>
                    </div>
                    <div>
                      <dt className="font-semibold text-muted-foreground">Scope</dt>
                      <dd className="mt-1 break-words text-foreground">{book.scopeLabel}</dd>
                    </div>
                    <div>
                      <dt className="font-semibold text-muted-foreground">Updated</dt>
                      <dd className="mt-1 text-foreground">{book.updatedLabel}</dd>
                    </div>
                  </dl>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      <Card id="configure-section-chart" className="panel-surface configure-anchor scroll-mt-20" aria-labelledby="accounting-chart-account-editor-heading">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle id="accounting-chart-account-editor-heading">Chart account setup</CardTitle>
              <CardDescription>Author ledger-book chart nodes through the shared accounting configuration API.</CardDescription>
            </div>
            <Button
              size="sm"
              disabled={!view.chartAccountEditor.canSave}
              disabledReason={view.chartAccountEditor.saveDisabledReason}
              busy={view.chartAccountEditor.saveBusy}
              busyLabel={view.chartAccountEditor.saveButtonLabel}
              onClick={() => void view.chartAccountEditor.save()}
            >
              {view.chartAccountEditor.saveButtonLabel}
            </Button>
          </div>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="grid gap-3 md:grid-cols-3">
            <FormRow label="Account path" labelFor="accounting-chart-path">
              <Input
                id="accounting-chart-path"
                value={view.chartAccountEditor.pathValue}
                onChange={(event) => view.chartAccountEditor.updateDraft({ path: event.currentTarget.value })}
              />
            </FormRow>
            <FormRow label="Account name" labelFor="accounting-chart-account-name">
              <Input
                id="accounting-chart-account-name"
                value={view.chartAccountEditor.accountNameValue}
                onChange={(event) => view.chartAccountEditor.updateDraft({ accountName: event.currentTarget.value })}
              />
            </FormRow>
            <FormRow label="Account type" labelFor="accounting-chart-account-type">
              <Input
                id="accounting-chart-account-type"
                value={view.chartAccountEditor.accountTypeValue}
                onChange={(event) => view.chartAccountEditor.updateDraft({ accountType: event.currentTarget.value })}
              />
            </FormRow>
          </div>
          <div className="grid gap-3 md:grid-cols-2">
            <FormRow label="Parent path" labelFor="accounting-chart-parent-path">
              <Input
                id="accounting-chart-parent-path"
                value={view.chartAccountEditor.parentPathValue}
                onChange={(event) => view.chartAccountEditor.updateDraft({ parentPath: event.currentTarget.value })}
              />
            </FormRow>
          </div>
          <TechnicalDetails
            label="Chart account identifiers and evidence"
            description="Stable node identity, linked financial account, and retained evidence reference for audit and support use."
          >
            <div className="grid gap-3 md:grid-cols-3">
              <FormRow label="Node id" labelFor="accounting-chart-node-id">
                <Input
                  id="accounting-chart-node-id"
                  value={view.chartAccountEditor.nodeIdValue}
                  onChange={(event) => view.chartAccountEditor.updateDraft({ nodeId: event.currentTarget.value })}
                />
              </FormRow>
              <FormRow label="Financial account id" labelFor="accounting-chart-financial-account-id">
                <Input
                  id="accounting-chart-financial-account-id"
                  value={view.chartAccountEditor.financialAccountIdValue}
                  onChange={(event) => view.chartAccountEditor.updateDraft({ financialAccountId: event.currentTarget.value })}
                />
              </FormRow>
              <FormRow label="Retained evidence" labelFor="accounting-chart-evidence">
                <Input
                  id="accounting-chart-evidence"
                  value={view.chartAccountEditor.evidenceValue}
                  onChange={(event) => view.chartAccountEditor.updateDraft({ evidenceText: event.currentTarget.value })}
                />
              </FormRow>
            </div>
          </TechnicalDetails>
          <ChartAccountPathBuilder editor={view.chartAccountEditor} />
          {view.chartAccountEditor.statusText ? (
            <p className="text-sm text-muted-foreground">{view.chartAccountEditor.statusText}</p>
          ) : null}
        </CardContent>
      </Card>

      <ConfigureProductionReadinessCard view={view} />

      <Card id="configure-section-rules" className="panel-surface configure-anchor scroll-mt-20">
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle className="flex items-center gap-2">
                <BookCheck className="h-5 w-5 text-primary" />
                Accounting Rules Studio
              </CardTitle>
              <CardDescription>Effective-dated posting rules, predicates, formulas, allocations, generated postings, versions, promotion approvals, and dry-run previews use the shared configuration API.</CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <Button
                size="sm"
                disabled={!view.canDryRun}
                disabledReason={view.dryRunDisabledReason}
                busy={view.dryRunBusy}
                busyLabel={view.dryRunButtonLabel}
                onClick={() => void view.dryRunSelectedRule()}
              >
                {view.dryRunButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="secondary"
                disabled={!view.canBuildJournalCandidate}
                disabledReason={view.journalCandidateDisabledReason}
                busy={view.journalCandidateBusy}
                busyLabel={view.journalCandidateButtonLabel}
                onClick={() => void view.buildJournalCandidate()}
              >
                {view.journalCandidateButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canApplyThreshold}
                disabledReason={view.applyThresholdDisabledReason}
                busy={view.applyThresholdBusy}
                busyLabel={view.applyThresholdButtonLabel}
                onClick={() => void view.applyDryRunAmountThreshold()}
              >
                {view.applyThresholdButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canApplyEventPredicate}
                disabledReason={view.applyEventPredicateDisabledReason}
                busy={view.applyEventPredicateBusy}
                busyLabel={view.applyEventPredicateButtonLabel}
                onClick={() => void view.applyDryRunEventPredicate()}
              >
                {view.applyEventPredicateButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canApplyEffectiveStart}
                disabledReason={view.applyEffectiveStartDisabledReason}
                busy={view.applyEffectiveStartBusy}
                busyLabel={view.applyEffectiveStartButtonLabel}
                onClick={() => void view.applyDryRunEffectiveStart()}
              >
                {view.applyEffectiveStartButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canCapturePostings}
                disabledReason={view.capturePostingsDisabledReason}
                busy={view.capturePostingsBusy}
                busyLabel={view.capturePostingsButtonLabel}
                onClick={() => void view.captureDryRunGeneratedPostings()}
              >
                {view.capturePostingsButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canApplyFormula}
                disabledReason={view.applyFormulaDisabledReason}
                busy={view.applyFormulaBusy}
                busyLabel={view.applyFormulaButtonLabel}
                onClick={() => void view.applyDryRunFormulaAmount()}
              >
                {view.applyFormulaButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canApplyAllocation}
                disabledReason={view.applyAllocationDisabledReason}
                busy={view.applyAllocationBusy}
                busyLabel={view.applyAllocationButtonLabel}
                onClick={() => void view.applyDryRunAllocationTargets()}
              >
                {view.applyAllocationButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canApplyScope}
                disabledReason={view.applyScopeDisabledReason}
                busy={view.applyScopeBusy}
                busyLabel={view.applyScopeButtonLabel}
                onClick={() => void view.applyDryRunScope()}
              >
                {view.applyScopeButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canDuplicateRule}
                disabledReason={view.duplicateRuleDisabledReason}
                busy={view.duplicateRuleBusy}
                busyLabel={view.duplicateRuleButtonLabel}
                onClick={() => void view.duplicateSelectedRule()}
              >
                {view.duplicateRuleButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canRaisePriority}
                disabledReason={view.raisePriorityDisabledReason}
                busy={view.raisePriorityBusy}
                busyLabel={view.raisePriorityButtonLabel}
                onClick={() => void view.raiseSelectedRulePriority()}
              >
                {view.raisePriorityButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canSaveDryRunAsRuleTest}
                disabledReason={view.saveDryRunAsRuleTestDisabledReason}
                busy={view.saveDryRunAsRuleTestBusy}
                busyLabel={view.saveDryRunAsRuleTestButtonLabel}
                onClick={() => void view.saveDryRunAsRuleTest()}
              >
                {view.saveDryRunAsRuleTestButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canArchiveRule}
                disabledReason={view.archiveRuleDisabledReason}
                busy={view.archiveRuleBusy}
                busyLabel={view.archiveRuleButtonLabel}
                onClick={() => void view.archiveSelectedRule()}
              >
                {view.archiveRuleButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={!view.canRunRuleTests}
                disabledReason={view.ruleTestDisabledReason}
                busy={view.ruleTestBusy}
                busyLabel={view.ruleTestButtonLabel}
                onClick={() => void view.runRuleTests()}
              >
                {view.ruleTestButtonLabel}
              </Button>
              <Button
                size="sm"
                variant="secondary"
                disabled={!view.canApproveRulePromotion}
                disabledReason={view.approveRulePromotionDisabledReason}
                busy={view.approveRulePromotionBusy}
                busyLabel={view.approveRulePromotionButtonLabel}
                onClick={() => void view.approveRulePromotion()}
              >
                {view.approveRulePromotionButtonLabel}
              </Button>
              {view.dryRunStatusText ? <span className="text-sm text-muted-foreground">{view.dryRunStatusText}</span> : null}
              {view.journalCandidateStatusText ? <span className="text-sm text-muted-foreground">{view.journalCandidateStatusText}</span> : null}
              {view.applyEventPredicateStatusText ? <span className="text-sm text-muted-foreground">{view.applyEventPredicateStatusText}</span> : null}
              {view.applyThresholdStatusText ? <span className="text-sm text-muted-foreground">{view.applyThresholdStatusText}</span> : null}
              {view.applyEffectiveStartStatusText ? <span className="text-sm text-muted-foreground">{view.applyEffectiveStartStatusText}</span> : null}
              {view.capturePostingsStatusText ? <span className="text-sm text-muted-foreground">{view.capturePostingsStatusText}</span> : null}
              {view.applyFormulaStatusText ? <span className="text-sm text-muted-foreground">{view.applyFormulaStatusText}</span> : null}
              {view.applyAllocationStatusText ? <span className="text-sm text-muted-foreground">{view.applyAllocationStatusText}</span> : null}
              {view.applyScopeStatusText ? <span className="text-sm text-muted-foreground">{view.applyScopeStatusText}</span> : null}
              {view.duplicateRuleStatusText ? <span className="text-sm text-muted-foreground">{view.duplicateRuleStatusText}</span> : null}
              {view.raisePriorityStatusText ? <span className="text-sm text-muted-foreground">{view.raisePriorityStatusText}</span> : null}
              {view.archiveRuleStatusText ? <span className="text-sm text-muted-foreground">{view.archiveRuleStatusText}</span> : null}
              {view.saveDryRunAsRuleTestStatusText ? <span className="text-sm text-muted-foreground">{view.saveDryRunAsRuleTestStatusText}</span> : null}
              {view.ruleTestStatusText ? <span className="text-sm text-muted-foreground">{view.ruleTestStatusText}</span> : null}
              {view.approveRulePromotionStatusText ? <span className="text-sm text-muted-foreground">{view.approveRulePromotionStatusText}</span> : null}
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {view.rules.length > 0 ? (
            <div className="grid gap-4 xl:grid-cols-[minmax(0,0.95fr)_minmax(0,1.05fr)]">
              <div className="space-y-2" role="list" aria-label="Accounting posting rules">
                {view.rules.map((rule) => (
                  <div key={rule.id} role="listitem">
                    <button
                      type="button"
                      aria-label={rule.selectAriaLabel}
                      aria-pressed={rule.isSelected}
                      className={cn(
                        "w-full rounded-lg border px-3 py-3 text-left transition hover:bg-secondary/35",
                        rule.isSelected ? "border-primary/45 bg-primary/10" : "border-border/70 bg-secondary/20"
                      )}
                      onClick={() => view.selectRule(rule.id)}
                    >
                      <div className="flex flex-wrap items-start justify-between gap-2">
                        <div className="min-w-0">
                          <div className="font-semibold text-foreground">{rule.title}</div>
                          <div className="mt-1 break-words font-mono text-xs text-muted-foreground">{rule.subtitle}</div>
                        </div>
                        <Badge variant={rule.statusTone}>{rule.statusLabel}</Badge>
                      </div>
                      <div className="mt-3 grid gap-2 text-xs text-muted-foreground sm:grid-cols-3">
                        <span className="font-mono">{rule.eventLabel}</span>
                        <span className="font-mono">{rule.effectiveLabel}</span>
                        <span className="font-mono">{rule.priorityLabel}</span>
                      </div>
                    </button>
                  </div>
                ))}
              </div>

              <div className="rounded-lg border border-border/70 bg-background/35 p-3">
                {view.selectedRule ? (
                  <div className="space-y-4">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div>
                        <div className="font-semibold text-foreground">{view.selectedRule.title}</div>
                        <div className="mt-1 font-mono text-xs text-muted-foreground">{view.selectedRule.eventLabel} | {view.selectedRule.effectiveLabel} | {view.selectedRule.priorityLabel}</div>
                      </div>
                      <Badge variant={view.selectedRule.promotionTone}>{view.selectedRule.promotionLabel}</Badge>
                    </div>
                    <div className="rounded-lg border border-border/70 bg-secondary/10 p-3" role="region" aria-label="Posting rule promotion readiness">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">Promotion readiness</div>
                        <Badge variant={accountingToolingBadgeVariant(hasPromotionReadinessBlocker(view.selectedRule.promotionReadiness) ? "warning" : "success")}>
                          {view.selectedRule.promotionReadiness.find((item) => item.id === "activation-gate")?.value ?? "Review"}
                        </Badge>
                      </div>
                      <div className="mt-3 grid gap-2 md:grid-cols-2">
                        {view.selectedRule.promotionReadiness.map((item) => (
                          <div key={item.id} className={cn("rounded-md border px-3 py-2 text-sm", accountingToolingBorderClass(item.tone))}>
                            <div className="flex flex-wrap items-center justify-between gap-2">
                              <span className="font-semibold text-foreground">{item.label}</span>
                              <Badge variant={accountingToolingBadgeVariant(item.tone)}>{item.value}</Badge>
                            </div>
                            <div className="mt-1 text-xs text-muted-foreground">{item.detail}</div>
                          </div>
                        ))}
                      </div>
                    </div>
                    <TechnicalDetails
                      label="Rule scope and posting internals"
                      description="Inspect effective scope, predicates, formula logic, allocations, generated postings, and retained versions for the selected rule."
                      contentClassName="space-y-4"
                    >
                      <RulesStudioList title="Scope" rows={view.selectedRule.scopeLabels.length > 0 ? view.selectedRule.scopeLabels : ["No dimensional scope configured."]} />
                      <RulesStudioList title="Predicates" rows={view.selectedRule.conditionRows} />
                      <RulesStudioList title="Formulas" rows={view.selectedRule.formulaRows} />
                      <RulesStudioList title="Allocations" rows={view.selectedRule.allocationRows} />
                      <RulesStudioList title="Generated postings" rows={view.selectedRule.generatedPostingRows} />
                      <RulesStudioList title="Version history" rows={view.selectedRule.versionRows} />
                    </TechnicalDetails>
                  </div>
                ) : (
                  <p className="text-sm text-muted-foreground">Select a posting rule to inspect its effective dating, predicates, formula logic, allocation behavior, generated postings, and promotion evidence.</p>
                )}
              </div>
            </div>
          ) : (
            <p role="status" className="rounded-lg border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">{view.emptyText}</p>
          )}

          {view.dryRunPreview ? (
            <div className="rounded-lg border border-primary/25 bg-primary/5 p-3" role="region" aria-label="Accounting rule dry-run preview">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <div className="font-semibold text-foreground">{view.dryRunPreview.title}</div>
                  <div className="mt-1 font-mono text-xs text-muted-foreground">{view.dryRunPreview.selectedRuleLabel}</div>
                </div>
                <Badge variant={view.dryRunPreview.balanceLabel.startsWith("Balanced") ? "success" : "warning"}>{view.dryRunPreview.balanceLabel}</Badge>
              </div>
              <div className="mt-3 grid gap-3 xl:grid-cols-3">
                <RulesStudioList title="Rule resolution" rows={view.dryRunPreview.matchRows.length > 0 ? view.dryRunPreview.matchRows : ["No rule matches were returned."]} />
                <RulesStudioList title="Generated journal lines" rows={view.dryRunPreview.generatedLineRows.length > 0 ? view.dryRunPreview.generatedLineRows : ["No generated journal lines were returned."]} />
                <RulesStudioList title="Generated posting metadata" rows={view.dryRunPreview.generatedPostingRows} />
              </div>
              {view.dryRunPreview.validationRows.length > 0 ? (
                <div className="mt-3 space-y-2">
                  {view.dryRunPreview.validationRows.map((issue) => (
                    <div key={issue.id} className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">
                      <div className="font-semibold">{issue.label}</div>
                      <div className="mt-1">{issue.message}</div>
                    </div>
                  ))}
                </div>
              ) : null}
            </div>
          ) : null}

          {view.journalCandidatePreview ? (
            <div className="rounded-lg border border-border/70 bg-secondary/15 p-3" role="region" aria-label="Accounting rule journal draft candidate">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <div className="font-semibold text-foreground">{view.journalCandidatePreview.title}</div>
                  <div className="mt-1 font-mono text-xs text-muted-foreground">{view.journalCandidatePreview.selectedRuleLabel}</div>
                </div>
                <Badge variant={view.journalCandidatePreview.balanceLabel.startsWith("Balanced") ? "success" : "warning"}>{view.journalCandidatePreview.balanceLabel}</Badge>
              </div>
              <div className="mt-3 grid gap-3 xl:grid-cols-3">
                <RulesStudioList title="Draft command" rows={[view.journalCandidatePreview.commandLabel, view.journalCandidatePreview.approvalLabel, view.journalCandidatePreview.evidenceLabel]} />
                <RulesStudioList title="Candidate posting lines" rows={view.journalCandidatePreview.generatedLineRows} />
                <RulesStudioList
                  title="Candidate issues"
                  rows={view.journalCandidatePreview.issueRows.length > 0
                    ? view.journalCandidatePreview.issueRows.map((issue) => `${issue.label}: ${issue.message}`)
                    : ["No blocking candidate issues returned."]}
                />
              </div>
            </div>
          ) : null}

          <div className="rounded-lg border border-border/70 bg-background p-3" role="region" aria-label="Saved accounting rule test cases">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div>
                <div className="font-semibold text-foreground">Saved regression cases</div>
                <div className="mt-1 text-xs text-muted-foreground">Persisted dry-run assertions used for rule promotion checks.</div>
              </div>
              <Badge variant={view.ruleTestCases.length > 0 ? "success" : "warning"}>{view.ruleTestCases.length} saved</Badge>
            </div>
            <div className="mt-3 grid gap-2 md:grid-cols-2">
              {view.ruleTestCases.length > 0 ? view.ruleTestCases.map((testCase) => (
                <div key={testCase.id} className="rounded-md border border-border/70 bg-secondary/10 px-3 py-2 text-sm">
                  <div className="font-semibold text-foreground">{testCase.title}</div>
                  <div className="mt-1 font-mono text-xs text-muted-foreground">{testCase.subtitle}</div>
                  <div className="mt-1 text-xs text-muted-foreground">{testCase.assertionLabel}</div>
                  <Badge variant={testCase.evidenceTone} className="mt-2">{testCase.evidenceLabel}</Badge>
                </div>
              )) : (
                <p className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning md:col-span-2">
                  No saved rule test cases. Running tests will generate temporary cases from active posting rules.
                </p>
              )}
            </div>
          </div>

          {view.ruleTestSuite ? (
            <div className="rounded-lg border border-border/70 bg-secondary/15 p-3" role="region" aria-label="Accounting rule test cases">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <div className="font-semibold text-foreground">{view.ruleTestSuite.title}</div>
                  <div className="mt-1 font-mono text-xs text-muted-foreground">{view.ruleTestSuite.executedLabel}</div>
                </div>
                <Badge variant={view.ruleTestSuite.statusTone}>{view.ruleTestSuite.summaryLabel}</Badge>
              </div>
              <div className="mt-3 grid gap-3 xl:grid-cols-2">
                <RulesStudioList title="Regression cases" rows={view.ruleTestSuite.resultRows} />
                <RulesStudioList
                  title="Assertion issues"
                  rows={view.ruleTestSuite.validationRows.length > 0
                    ? view.ruleTestSuite.validationRows.map((issue) => `${issue.label}: ${issue.message}`)
                    : ["No rule-test assertion issues returned."]}
                />
              </div>
            </div>
          ) : null}
        </CardContent>
      </Card>

      <div className="grid gap-4 xl:grid-cols-[1.1fr_0.9fr]">
        <Card className="panel-surface">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Network className="h-5 w-5 text-primary" />
              Journal templates and preview
            </CardTitle>
            <CardDescription>Preview uses accounting configuration services and does not persist journal entries.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="flex flex-wrap items-center gap-2">
              <Button
                size="sm"
                disabled={!view.canPreview}
                disabledReason={view.previewDisabledReason}
                busy={view.previewBusy}
                busyLabel={view.previewButtonLabel}
                onClick={() => void view.previewFirstTemplate()}
              >
                {view.previewButtonLabel}
              </Button>
              {view.previewStatusText ? <span className="text-sm text-muted-foreground">{view.previewStatusText}</span> : null}
            </div>

            <div className="space-y-2">
              {view.templates.length > 0 ? view.templates.map((template) => (
                <div key={template.id} className="rounded-lg border border-border/70 bg-secondary/25 px-3 py-2">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="font-semibold text-foreground">{template.title}</div>
                      <div className="mt-1 break-words font-mono text-xs text-muted-foreground">{template.subtitle}</div>
                    </div>
                    <Badge variant={template.statusLabel === "Balanced" ? "success" : template.statusLabel === "Archived" ? "outline" : "warning"}>
                      {template.statusLabel}
                    </Badge>
                  </div>
                  <div className="mt-2 flex flex-wrap gap-2 text-xs text-muted-foreground">
                    <span>{template.lineCountLabel}</span>
                    <span>{template.balanceLabel}</span>
                  </div>
                </div>
              )) : (
                <p role="status" className="rounded-lg border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">{view.emptyText}</p>
              )}
            </div>

            {view.preview ? (
              <div className="rounded-lg border border-border/70 bg-background/35 p-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <div className="font-semibold text-foreground">{view.preview.title}</div>
                    <div className="mt-1 font-mono tabular-nums text-xs text-muted-foreground">{view.preview.balanceLabel}</div>
                  </div>
                  <Badge variant={view.preview.statusLabel.startsWith("Balanced") ? "success" : "warning"}>{view.preview.statusLabel}</Badge>
                </div>
                <div className="mt-3 space-y-2">
                  {view.preview.lineRows.map((line) => (
                    <div key={line.id} className="grid gap-2 rounded border border-border/60 px-2 py-2 text-xs sm:grid-cols-[1fr_auto_auto]">
                      <span className="min-w-0 break-words font-mono text-foreground">{line.account}</span>
                      <span className="font-mono text-muted-foreground">{line.side}</span>
                      <span className="font-mono tabular-nums text-foreground">{line.amount}</span>
                    </div>
                  ))}
                </div>
              </div>
            ) : null}
          </CardContent>
        </Card>

        <Card className="panel-surface">
          <CardHeader>
            <CardTitle>Validation and audit trail</CardTitle>
            <CardDescription>Configuration readiness and append-only mutation evidence stay visible before activation.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              {view.validationIssues.length > 0 ? view.validationIssues.map((issue) => (
                <div key={issue.id} className={cn(
                  "rounded-lg border px-3 py-2 text-sm",
                  issue.tone === "danger" ? "border-danger/30 bg-danger/10 text-danger" : "",
                  issue.tone === "warning" ? "border-warning/30 bg-warning/10 text-warning" : "",
                  issue.tone === "default" ? "border-border/70 bg-secondary/25 text-muted-foreground" : ""
                )}>
                  <div className="font-semibold">{issue.label}</div>
                  <div className="mt-1">{issue.message}</div>
                  <div className="mt-1 font-mono text-xs">{issue.detail}</div>
                </div>
              )) : (
                <div className="rounded-lg border border-success/30 bg-success/10 px-3 py-2 text-sm text-success">
                  No critical configuration validation issues.
                </div>
              )}
            </div>

            <div className="space-y-2">
              <div className="eyebrow-label">Recent audit events</div>
              {view.auditTrail.length > 0 ? view.auditTrail.map((event) => (
                <div key={event.id} className="rounded-lg border border-border/70 bg-secondary/25 px-3 py-2 text-sm">
                  <div className="font-semibold text-foreground">{event.title}</div>
                  <div className="mt-1 break-words font-mono text-xs text-muted-foreground">{event.subtitle}</div>
                  <div className="mt-1 font-mono text-xs text-muted-foreground">{event.hashLabel}</div>
                </div>
              )) : (
                <p className="rounded-lg border border-warning/30 bg-warning/10 px-3 py-2 text-sm text-warning">{view.emptyText}</p>
              )}
            </div>
          </CardContent>
        </Card>
      </div>

          <ConfigureChangePreviewPanel view={view} />
        </div>
        <ConfigureActivationRail view={view} />
      </div>
    </section>
  );
}

function RulesStudioList({ title, rows }: { title: string; rows: string[] }) {
  return (
    <div>
      <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">{title}</div>
      <div className="mt-2 space-y-1.5">
        {rows.map((row, index) => (
          <div key={`${title}-${index}-${row}`} className="rounded border border-border/60 bg-secondary/20 px-2 py-1.5 text-xs leading-5 text-muted-foreground">
            {row}
          </div>
        ))}
      </div>
    </div>
  );
}

function hasPromotionReadinessBlocker(items: AccountingRulesStudioPromotionReadinessViewModel[]): boolean {
  return items.some((item) => item.tone === "danger" || (item.id === "activation-gate" && item.tone === "warning"));
}
