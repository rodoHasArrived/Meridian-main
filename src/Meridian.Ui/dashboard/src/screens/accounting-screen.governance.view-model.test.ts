import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  useAccountingConfigurationViewModel,
} from "@/screens/accounting-screen.view-model";
import type {
  AccountingConfigurationServices,
} from "@/screens/accounting-screen.view-model";
import type {
  AccountingConfigurationWorkspace,
  AccountingProductionReadiness,
  AccountingTenantAdministrationProfile,
  RuleDryRunResult,
} from "@/types";

describe("accounting governance view model", () => {  it("loads Accounting Rules Studio rules and runs shared dry-run previews", async () => {
    const workspace: AccountingConfigurationWorkspace = {
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      status: "Draft",
      configurationVersion: "v4",
      updatedAtUtc: "2026-06-30T12:00:00Z",
      ledgerBooks: [{
        ledgerBookId: "book-primary",
        fundProfileId: "fund-alpha",
        fundStructureNodeId: "entity-master",
        fundStructureNodeKind: "Entity",
        displayName: "Primary book",
        baseCurrency: "USD",
        createdAt: "2026-01-01T00:00:00Z",
        updatedAt: "2026-06-30T12:00:00Z",
        description: "Primary accounting book.",
        accountingBasis: "Gaap",
        accountingPolicyId: "policy-gaap",
        accountingPolicyVersion: "2026.06"
      }],
      chartOfAccounts: [
        {
          nodeId: "coa-cash",
          path: "1000.Cash",
          accountName: "Cash",
          accountType: "Asset",
          parentPath: null,
          isArchived: false
        },
        {
          nodeId: "coa-investment",
          path: "1200.Investments",
          accountName: "Investments",
          accountType: "Asset",
          parentPath: null,
          isArchived: false
        }
      ],
      journalTemplates: [{
        templateId: "template-trade-buy",
        displayName: "Trade buy settlement",
        description: "Balanced trade settlement posting.",
        isArchived: false,
        version: "v2",
        lines: [
          {
            lineId: "line-investment",
            accountPath: "1200.Investments",
            side: "Debit",
            amount: 250000,
            currency: "USD",
            description: "Investment cost"
          },
          {
            lineId: "line-cash",
            accountPath: "1000.Cash",
            side: "Credit",
            amount: 250000,
            currency: "USD",
            description: "Cash settlement"
          }
        ]
      }],
      postingRules: [{
        ruleId: "rule-trade-buy",
        displayName: "Trade buy posting",
        sourceEventType: "TradeExecuted",
        templateId: "template-trade-buy",
        ruleVersion: "v3",
        isArchived: false,
        description: "Generate trade buy settlement postings.",
        effectiveFrom: "2026-01-01",
        effectiveTo: "2026-12-31",
        priority: 10,
        scope: {
          fundId: "fund-alpha",
          entityId: "entity-master",
          strategyId: "strategy-long-only",
          counterpartyId: "cp-001",
          externalGlDimensions: {
            class: "FundAlpha"
          }
        },
        conditions: [
          {
            conditionId: "cond-event",
            field: "event.kind",
            operator: "Equals",
            value: "TradeExecuted",
            isRequired: true,
            description: "Only trade events use this rule."
          },
          {
            conditionId: "cond-amount",
            field: "event.notional",
            operator: "AmountGreaterThanOrEqual",
            value: "100000",
            isRequired: true,
            description: "Controller review threshold."
          }
        ],
        conditionGroups: [{
          groupId: "group-trade-source",
          operator: "Any",
          isRequired: true,
          description: "Allow broker or internal execution sources.",
          conditions: [
            {
              conditionId: "cond-broker-source",
              field: "event.source",
              operator: "Equals",
              value: "Broker",
              isRequired: false,
              description: "Broker feed."
            },
            {
              conditionId: "cond-ops-source",
              field: "event.source",
              operator: "Equals",
              value: "Operations",
              isRequired: false,
              description: "Operations upload."
            }
          ]
        }],
        formulas: [{
          formulaId: "formula-source",
          kind: "SourceAmount",
          value: 250000,
          currency: "USD",
          description: "Use source trade amount."
        }],
        allocations: [{
          allocationRuleId: "alloc-strategy",
          basis: "StrategyWeight",
          weight: 1,
          formulaId: "formula-source",
          targetDimensions: {
            sleeveId: "sleeve-core",
            strategyId: "strategy-long-only"
          },
          description: "Allocate to the core strategy sleeve."
        }],
        generatedPostings: [
          {
            lineId: "generated-investment",
            accountPath: "1200.Investments",
            side: "Debit",
            amountFormulaId: "formula-source",
            amount: 250000,
            currency: "USD",
            dimensions: {
              fundId: "fund-alpha",
              instrumentId: "AAPL"
            },
            description: "Debit investment cost."
          },
          {
            lineId: "generated-cash",
            accountPath: "1000.Cash",
            side: "Credit",
            amountFormulaId: "formula-source",
            amount: 250000,
            currency: "USD",
            dimensions: {
              fundId: "fund-alpha",
              counterpartyId: "cp-001"
            },
            description: "Credit cash settlement."
          }
        ],
        versions: [{
          version: "v3",
          createdAtUtc: "2026-06-15T10:00:00Z",
          createdBy: "controller",
          changeSummary: "Added counterparty scope and generated postings.",
          promotionApproval: null,
          evidenceLinks: ["evidence://rule/v3"]
        }],
        promotionApproval: {
          approvalId: "approval-rule-trade-buy",
          requestedBy: "controller",
          requestedAtUtc: "2026-06-15T10:00:00Z",
          approvalState: "Approved",
          approvedBy: "cfo",
          approvedAtUtc: "2026-06-15T11:00:00Z",
          notes: "Approved for production dry-run.",
          evidenceLinks: ["evidence://approval"]
        },
        requiresPromotionApproval: true
      }],
      validationIssues: [],
      rulesStudio: {
        summary: {
          totalRules: 1,
          activeRules: 1,
          archivedRules: 0,
          effectiveDatedRules: 1,
          generatedPostingRules: 1,
          templateMappingRules: 0,
          rulesWithConditions: 1,
          rulesWithFormulas: 1,
          rulesWithAllocations: 1,
          rulesRequiringPromotionApproval: 1,
          approvedPromotionRules: 1,
          pendingPromotionApprovalRules: 0,
          savedTestCaseCount: 1,
          rulesWithSavedRegressionTests: 1,
          rulesMissingCurrentVersionRegressionTests: 0,
          criticalIssueCount: 0,
          warningIssueCount: 0
        },
        rules: [{
          ruleId: "rule-trade-buy",
          displayName: "Trade buy posting",
          sourceEventType: "TradeExecuted",
          ruleVersion: "v3",
          priority: 10,
          effectiveFrom: "2026-01-01",
          effectiveTo: "2026-12-31",
          templateId: "template-trade-buy",
          isArchived: false,
          usesGeneratedPostings: true,
          conditionCount: 2,
          conditionGroupCount: 1,
          formulaCount: 1,
          allocationCount: 1,
          generatedPostingLineCount: 2,
          versionCount: 1,
          savedTestCaseCount: 1,
          savedTestEvidenceLinkCount: 1,
          requiresPromotionApproval: true,
          isPromotionApproved: true,
          promotionApprovalState: "Approved",
          promotionApprovalId: "approval-rule-trade-buy",
          criticalIssueCount: 0,
          warningIssueCount: 0,
          canDryRun: true,
          canRequestPromotion: false,
          canActivate: true
        }],
        promotionQueue: []
      },
      auditTrail: [{
        auditEventId: "audit-rule-trade-buy",
        recordedAtUtc: "2026-06-15T11:00:00Z",
        actor: "controller",
        action: "rule.promoted",
        fundProfileId: "fund-alpha",
        ledgerBookId: "book-primary",
        correlationId: "corr-rule",
        beforeHash: "before-hash-123456",
        afterHash: "after-hash-654321",
        validationIssues: [],
        evidenceLinks: ["evidence://approval"]
      }],
      ruleTestCases: [{
        testCaseId: "rule-test-trade-buy-saved",
        displayName: "Saved trade buy regression",
        request: {
          fundProfileId: "fund-alpha",
          ledgerBookId: "book-primary",
          sourceEventType: "TradeExecuted",
          eventAmount: 250000,
          currency: "USD",
          effectiveDate: "2026-06-30",
          actor: "controller",
          dimensions: {
            fundId: "fund-alpha",
            entityId: "entity-master",
            counterpartyId: "cp-001"
          },
          counterpartyId: "cp-001"
        },
        expectedRuleId: "rule-trade-buy",
        expectedRuleVersion: "v3",
        expectBalancedPosting: true,
        expectedIssueCodes: [],
        evidenceLinks: ["evidence://accounting/rule-tests/trade-buy"]
      }]
    };
    const dryRunResult: RuleDryRunResult = {
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      sourceEventType: "TradeExecuted",
      effectiveDate: "2026-01-01",
      eventAmount: 250000,
      currency: "USD",
      isPostingBalanced: true,
      selectedRuleId: "rule-trade-buy",
      ruleMatches: [{
        ruleId: "rule-trade-buy",
        displayName: "Trade buy posting",
        ruleVersion: "v3",
        priority: 10,
        isMatched: true,
        explanations: ["Effective date and source event predicates matched."],
        validationIssues: []
      }],
      generatedLines: [
        {
          accountPath: "1200.Investments",
          accountName: "Investments",
          side: "Debit",
          amount: 250000,
          currency: "USD",
          description: "Debit investment cost."
        },
        {
          accountPath: "1000.Cash",
          accountName: "Cash",
          side: "Credit",
          amount: 250000,
          currency: "USD",
          description: "Credit cash settlement."
        }
      ],
      generatedPostingLines: workspace.postingRules[0].generatedPostings,
      validationIssues: []
    };
    const productionReadiness: AccountingProductionReadiness = {
      generatedAtUtc: "2026-06-30T12:15:00Z",
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      status: "ReviewRequired",
      score: 78,
      externalGlProviderCount: 3,
      certifiedExternalGlMappingProfileCount: 1,
      externalGlLivePostingEnabled: false,
      criticalIssueCount: 0,
      warningIssueCount: 1,
      ledgerBookRollout: {
        generatedAtUtc: "2026-06-30T12:15:00Z",
        fundProfileId: "fund-alpha",
        fundStructureNodeId: "entity-master",
        fundStructureNodeKind: "Entity",
        accountingBasis: "Gaap",
        books: [],
        issues: [],
        isReady: true,
        criticalIssueCount: 0,
        warningIssueCount: 0,
        bookCount: 1,
        openPeriodCount: 1
      },
      rulesStudioSummary: workspace.rulesStudio.summary,
      ledgerBookWorkflows: {
        ledgerBookId: "book-primary",
        postingRulesLedgerBookNativeCertified: true,
        journalLifecycleLedgerBookNativeCertified: true,
        closeReportingLedgerBookNativeCertified: false,
        closePlanConfigurationLedgerBookNativeCertified: false,
        externalGlLedgerBookNativeCertified: false,
        reconciliationLedgerBookNativeCertified: false,
        directLendingLedgerBookNativeCertified: false,
        strategyLedgerReadLedgerBookNativeCertified: false,
        evidenceReferences: ["evidence://ledger-book/book-primary/workflow-certification"],
        completedControlCount: 4,
        requiredControlCount: 10,
        hasLedgerBookScope: true,
        hasRetainedEvidence: true,
        hasLedgerBookScopedEvidence: true
      },
      dimensionalReporting: {
        ledgerBookId: "book-primary",
        periodReportDimensionQueriesCertified: true,
        crossPeriodReportDimensionQueriesCertified: false,
        journalQueryDimensionFiltersCertified: true,
        externalExportDimensionMappingCertified: false,
        ledgerLineDimensionsPersistedCertified: false,
        trialBalanceDimensionFiltersCertified: false,
        reportPackageDimensionProvenanceCertified: false,
        evidenceReferences: ["evidence://ledger-book/book-primary/dimensions/reporting"],
        completedControlCount: 4,
        requiredControlCount: 9,
        hasLedgerBookScope: true,
        hasRetainedEvidence: true,
        hasLedgerBookScopedEvidence: true
      },
      tenantAdministration: {
        tenantId: "tenant-alpha",
        companyId: "company-alpha",
        tenantScopeConfigured: true,
        adminRoleProfileConfigured: true,
        scopedAccessPoliciesConfigured: true,
        reportingGroupsConfigured: false,
        accountingAdminSurfaceConfigured: false,
        browserAccountingAdminSurfaceConfigured: false,
        wpfAccountingAdminSurfaceConfigured: false,
        chartAdministrationStudioConfigured: false,
        ruleTestPromotionStudioConfigured: false,
        closeSetupStudioConfigured: false,
        providerMappingStudioConfigured: false,
        tenantCompanyReportGroupSetupStudioConfigured: false,
        auditReviewToolingConfigured: false,
        bulkImportExportSafeguardsConfigured: false,
        performanceValidationConfigured: false,
        disasterRecoveryRunbookConfigured: false,
        ledgerBookAdministrationStudioConfigured: false,
        postingRuleAuthoringStudioConfigured: false,
        approvalQueueStudioConfigured: false,
        dimensionMappingStudioConfigured: false,
        implementationSandboxConfigured: false,
        evidenceReferences: ["evidence://tenant-admin/gap"],
        completedControlCount: 5,
        requiredControlCount: 23,
        hasTenantScope: true,
        hasCompanyScope: true,
        hasRetainedEvidence: true
      },
      productionGaps: [
        {
          code: "multi-ledger-native-workflows",
          label: "Configurable multi-ledger accounting",
          status: "ReviewRequired",
          highestSeverity: "Warning",
          summary: "Ledger-book-native certification still needs end-to-end workflow evidence.",
          requiredAction: "Retain ledger-book-native workflow evidence for posting, JE lifecycle, reconciliation, close, and reporting.",
          areas: ["LedgerBooks", "PostingRules", "JournalLifecycle", "CloseReporting"],
          blockingIssueCodes: ["workflow.evidence.missing", "journal.lifecycle.missing"],
          issues: [
            {
              code: "workflow.evidence.missing",
              area: "LedgerBooks",
              severity: "Warning",
              message: "Ledger-book workflow evidence is missing for the selected book.",
              suggestedAction: "Retain selected-book workflow evidence before production rollout.",
              evidenceReferences: []
            }
          ],
          routes: ["/accounting/configure", "/accounting/journal-entries"]
        },
        {
          code: "enterprise-accounting-configuration-studio",
          label: "Enterprise accounting configuration studio",
          status: "ReviewRequired",
          highestSeverity: "Warning",
          summary: "Operator setup controls still need enterprise admin-studio coverage.",
          requiredAction: "Complete retained chart, rules, approval, tenant, and dimension setup controls.",
          areas: ["RulesStudio", "TenantAdministration"],
          blockingIssueCodes: ["tenant-admin.operator-surface-required"],
          routes: ["/accounting/configure", "/settings"]
        },
        {
          code: "external-gl-guarded-integration",
          label: "External GL guarded integration",
          status: "ReviewRequired",
          highestSeverity: "Info",
          summary: "External GL remains import-first with guarded export artifacts.",
          requiredAction: "Retain mapping, reconciliation, and export-package evidence while live posting remains disabled.",
          areas: ["ExternalGl"],
          blockingIssueCodes: ["external-gl.live-posting-disabled"],
          routes: ["/accounting/external-gl"]
        },
        {
          code: "dimensional-ledger-reporting",
          label: "Dimensional ledger and reporting",
          status: "ReviewRequired",
          highestSeverity: "Warning",
          summary: "Dimensional ledger/query/report/export controls need full certification.",
          requiredAction: "Certify ledger-line dimensions, trial-balance filters, report provenance, and export mappings.",
          areas: ["DimensionalAccounting", "ExternalGl", "CloseReporting"],
          blockingIssueCodes: ["dimensions.external-gl-missing"],
          routes: ["/accounting/ledger", "/reporting"]
        },
        {
          code: "production-controls-hardening",
          label: "Production controls and rollout hardening",
          status: "ReviewRequired",
          highestSeverity: "Warning",
          summary: "Migration, performance, disaster recovery, and bulk safeguard controls need completion.",
          requiredAction: "Retain certified migration runs, performance proof, disaster-recovery runbooks, and bulk import/export safeguards.",
          areas: ["MigrationRollout", "TenantAdministration", "CloseReporting"],
          blockingIssueCodes: ["migration.close-reporting-evidence-not-certified"],
          routes: ["/accounting/configure", "/settings", "/accounting/close"]
        }
      ],
      migrationRolloutPlan: [
        {
          kind: "LedgerBookScope",
          code: "ledger-book-scope",
          label: "Ledger-book migration scope",
          certified: true,
          status: "Ready",
          scopeLabel: "tenant tenant-alpha | company company-alpha | fund fund-alpha | book book-primary",
          requiredAction: "Ledger-book scope migration is retained.",
          latestRunId: "migration-run-ledger-book-scope-book-primary",
          latestRunStatus: "Certified",
          migratedRecordCount: 24,
          issueCount: 0,
          evidenceReferences: ["evidence://migration/ledger-book-scope/book-primary"],
          blockingIssueCodes: []
        },
        {
          kind: "HistoricalJournalBackfill",
          code: "historical-journal-backfill",
          label: "Historical journal backfill",
          certified: false,
          status: "Blocked",
          scopeLabel: "tenant tenant-alpha | company company-alpha | fund fund-alpha | book book-primary",
          requiredAction: "Run and retain historical journal backfill evidence before certifying ledger-book-native accounting.",
          latestRunId: null,
          latestRunStatus: null,
          migratedRecordCount: 0,
          issueCount: 0,
          evidenceReferences: [],
          blockingIssueCodes: ["migration.historical-journal-backfill-not-certified"]
        }
      ],
      components: [
        {
          area: "RulesStudio",
          label: "Rules Studio",
          status: "Ready",
          score: 92,
          summary: "Rule versions, dry-run regression cases, and promotion approvals are retained.",
          issues: [],
          evidenceReferences: ["evidence://rule/v3"],
          route: "/accounting/configure"
        },
        {
          area: "TenantAdministration",
          label: "Tenant administration",
          status: "ReviewRequired",
          score: 50,
          summary: "Tenant setup operator workflow still needs completion.",
          issues: [
            {
              code: "tenant-admin.operator-surface-required",
              area: "TenantAdministration",
              severity: "Warning",
              message: "Production rollout still needs tenant setup controls.",
              suggestedAction: "Bind admin setup screens to this shared readiness contract.",
              evidenceReferences: ["evidence://tenant-admin/gap"]
            }
          ],
          evidenceReferences: ["evidence://tenant-admin/gap"],
          route: "/settings"
        }
      ],
      issues: [
        {
          code: "tenant-admin.operator-surface-required",
          area: "TenantAdministration",
          severity: "Warning",
          message: "Production rollout still needs tenant setup controls.",
          suggestedAction: "Bind admin setup screens to this shared readiness contract.",
          evidenceReferences: ["evidence://tenant-admin/gap"]
        }
      ]
    };
    let retainedWorkspace = workspace;
    let retainedTenantAdministrationProfile: AccountingTenantAdministrationProfile = {
      tenantId: "tenant-alpha",
      companyId: "company-alpha",
      tenantScopeConfigured: true,
      adminRoleProfileConfigured: true,
      scopedAccessPoliciesConfigured: true,
      reportingGroupsConfigured: true,
      accountingAdminSurfaceConfigured: false,
      browserAccountingAdminSurfaceConfigured: false,
      wpfAccountingAdminSurfaceConfigured: false,
      chartAdministrationStudioConfigured: false,
      ruleTestPromotionStudioConfigured: false,
      closeSetupStudioConfigured: false,
      providerMappingStudioConfigured: false,
      tenantCompanyReportGroupSetupStudioConfigured: false,
      auditReviewToolingConfigured: false,
      bulkImportExportSafeguardsConfigured: false,
      performanceValidationConfigured: false,
      disasterRecoveryRunbookConfigured: false,
      ledgerBookAdministrationStudioConfigured: false,
      postingRuleAuthoringStudioConfigured: false,
      approvalQueueStudioConfigured: false,
      dimensionMappingStudioConfigured: false,
      implementationSandboxConfigured: false,
      updatedAtUtc: "2026-06-30T11:55:00Z",
      updatedBy: "controller",
      evidenceReferences: ["evidence://tenant-admin/setup"],
      correlationId: "tenant-admin-existing"
    };
    let retainedProductionCertificationProfile = {
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      tenantId: "tenant-alpha",
      companyId: "company-alpha",
      postingRulesLedgerBookNativeCertified: true,
      journalLifecycleLedgerBookNativeCertified: true,
      closeReportingLedgerBookNativeCertified: false,
      closePlanConfigurationLedgerBookNativeCertified: false,
      externalGlLedgerBookNativeCertified: false,
      periodReportDimensionQueriesCertified: true,
      crossPeriodReportDimensionQueriesCertified: false,
      journalQueryDimensionFiltersCertified: true,
      externalExportDimensionMappingCertified: false,
      ledgerLineDimensionsPersistedCertified: false,
      trialBalanceDimensionFiltersCertified: false,
      reportPackageDimensionProvenanceCertified: false,
      updatedAtUtc: "2026-06-30T11:50:00Z",
      updatedBy: "controller",
      evidenceReferences: ["evidence://ledger-book/book-primary/workflow-certification"],
      correlationId: "production-certification-existing"
    };
    const upsertRule = vi.fn(async (request: Parameters<AccountingConfigurationServices["upsertRule"]>[0]) => {
      const existingRuleIndex = retainedWorkspace.postingRules.findIndex((rule) => rule.ruleId === request.rule.ruleId);
      const postingRules = existingRuleIndex >= 0
        ? retainedWorkspace.postingRules.map((rule, index) => index === existingRuleIndex ? request.rule : rule)
        : [...retainedWorkspace.postingRules, request.rule];
      retainedWorkspace = {
        ...retainedWorkspace,
        postingRules,
        auditTrail: [
          ...retainedWorkspace.auditTrail,
          {
            auditEventId: `audit-rule-upsert-${retainedWorkspace.auditTrail.length + 1}`,
            action: "posting-rule.upsert",
            actor: request.actor,
            fundProfileId: request.fundProfileId,
            ledgerBookId: null,
            correlationId: request.correlationId ?? null,
            recordedAtUtc: "2026-06-30T12:10:00Z",
            beforeHash: "before-rule-upsert",
            afterHash: "after-rule-upsert",
            validationIssues: [],
            evidenceLinks: request.evidenceLinks ?? []
          }
        ]
      };
      return retainedWorkspace;
    });
    const services: AccountingConfigurationServices = {
      getConfiguration: vi.fn().mockResolvedValue(workspace),
      assessProductionReadiness: vi.fn().mockResolvedValue(productionReadiness),
      listMigrationRunArtifacts: vi.fn().mockResolvedValue({ fundProfileId: "fund-alpha", ledgerBookId: "book-primary", artifacts: productionReadiness.migrationRunArtifacts ?? [] }),
      listMigrationWorkerPlans: vi.fn().mockResolvedValue({
        fundProfileId: "fund-alpha",
        ledgerBookId: "book-primary",
        kind: null,
        plans: [{
          planId: "worker-plan-historical-book-primary",
          kind: "HistoricalJournalBackfill",
          fundProfileId: "fund-alpha",
          ledgerBookId: "book-primary",
          sourceRecordCount: 275,
          migratedRecordCount: 275,
          evidenceReferences: ["evidence://migration-worker-plan/historical/book-primary"],
          tenantId: "tenant-alpha",
          companyId: "company-alpha",
          summary: "Historical journal worker plan retained for primary book."
        }]
      }),
      listExternalGlMappingProfiles: vi.fn().mockResolvedValue([]),
      upsertExternalGlMappingProfile: vi.fn(async (request) => request.profile),
      getProductionCertificationProfile: vi.fn(async () => retainedProductionCertificationProfile),
      upsertProductionCertificationProfile: vi.fn(async (request) => {
        retainedProductionCertificationProfile = request.profile;
        return retainedProductionCertificationProfile;
      }),
      getTenantAdministrationProfile: vi.fn(async () => retainedTenantAdministrationProfile),
      upsertTenantAdministrationProfile: vi.fn(async (request) => {
        retainedTenantAdministrationProfile = request.profile;
        return retainedTenantAdministrationProfile;
      }),
      createLedgerBook: vi.fn(),
      previewTemplate: vi.fn().mockResolvedValue({
        templateId: "template-trade-buy",
        displayName: "Trade buy settlement",
        isBalanced: true,
        totalDebits: 250000,
        totalCredits: 250000,
        lines: dryRunResult.generatedLines,
        validationIssues: []
      }),
      upsertChartNode: vi.fn(async (request) => {
        retainedWorkspace = {
          ...retainedWorkspace,
          chartOfAccounts: [
            ...retainedWorkspace.chartOfAccounts.filter((node) => node.nodeId !== request.node.nodeId),
            request.node
          ],
          auditTrail: [
            ...retainedWorkspace.auditTrail,
            {
              auditEventId: `audit-chart-upsert-${retainedWorkspace.auditTrail.length + 1}`,
              action: "chart.upsert",
              actor: request.actor,
              fundProfileId: request.fundProfileId,
              ledgerBookId: request.ledgerBookId ?? null,
              correlationId: request.correlationId ?? null,
              recordedAtUtc: "2026-06-30T12:08:00Z",
              beforeHash: "before-chart-upsert",
              afterHash: "after-chart-upsert",
              validationIssues: [],
              evidenceLinks: request.evidenceLinks ?? []
            }
          ]
        };
        return retainedWorkspace;
      }),
      upsertRule,
      dryRunRule: vi.fn().mockResolvedValue(dryRunResult),
      buildJournalCandidate: vi.fn(),
      runRuleTests: vi.fn().mockResolvedValue({
        fundProfileId: "fund-alpha",
        ledgerBookId: "book-primary",
        executedAtUtc: "2026-06-30T12:05:00Z",
        actor: "browser-accounting-operator",
        totalCount: 1,
        passedCount: 1,
        failedCount: 0,
        results: [{
          testCaseId: "rule-test-rule-trade-buy",
          displayName: "Saved trade buy regression",
          passed: true,
          dryRunResult,
          assertionIssues: []
        }]
      }),
      saveRuleTestCase: vi.fn(async () => {
        retainedWorkspace = {
          ...retainedWorkspace,
          ruleTestCases: [
            ...(retainedWorkspace.ruleTestCases ?? []),
            {
              testCaseId: "rule-test-rule-trade-buy",
              displayName: "Trade buy posting retained dry-run regression",
              request: {
                fundProfileId: "fund-alpha",
                ledgerBookId: "book-primary",
                sourceEventType: "TradeExecuted",
                eventAmount: 250000,
                currency: "USD",
                effectiveDate: "2026-01-01",
                actor: "browser-accounting-operator",
                dimensions: {
                  fundId: "fund-alpha",
                  entityId: "entity-master",
                  strategyId: "strategy-long-only",
                  counterpartyId: "cp-001",
                  externalGlDimensions: {
                    class: "FundAlpha"
                  }
                },
                counterpartyId: "cp-001"
              },
              expectedRuleId: "rule-trade-buy",
              expectedRuleVersion: "v3",
              expectBalancedPosting: true,
              expectedIssueCodes: [],
              evidenceLinks: [
                "browser://accounting/rules-studio/dry-run/rule-trade-buy",
                "browser://accounting/rules-studio/test-case/rule-trade-buy"
              ]
            }
          ]
        };
        return retainedWorkspace;
      }),
      approveRulePromotion: vi.fn().mockResolvedValue(workspace),
      activate: vi.fn().mockResolvedValue(workspace)
    };

    const { result } = renderHook(() => useAccountingConfigurationViewModel(services));

    await waitFor(() => expect(result.current.rules).toHaveLength(1));
    expect(result.current.selectedRule).toMatchObject({
      id: "rule-trade-buy",
      title: "Trade buy posting",
      eventLabel: "TradeExecuted",
      effectiveLabel: "2026-01-01 -> 2026-12-31",
      priorityLabel: "Priority 10",
      promotionLabel: "Approved by cfo",
      statusLabel: "Generated postings"
    });
    expect(result.current.selectedRule?.scopeLabels).toEqual(expect.arrayContaining([
      "Fund: fund-alpha",
      "Entity: entity-master",
      "Counterparty: cp-001",
      "External class: FundAlpha"
    ]));
    expect(result.current.selectedRule?.conditionRows.join("\n")).toContain("event.kind Equals TradeExecuted");

    act(() => {
      result.current.chartAccountEditor.updateDraft({
        nodeId: "coa-management-fees",
        path: "Expenses:Management Fees",
        accountName: "Management Fees",
        accountType: "Expense",
        parentPath: "Expenses",
        financialAccountId: "gl-6100-management-fees",
        evidenceText: "evidence://chart/management-fees"
      });
    });
    expect(result.current.chartAccountEditor.canSave).toBe(true);

    act(() => {
      result.current.chartAccountEditor.updateDraft({ path: "   " });
    });
    expect(result.current.chartAccountEditor.canSave).toBe(false);
    expect(result.current.chartAccountEditor.saveDisabledReason).toBe("Account path is required.");

    await act(async () => {
      await result.current.chartAccountEditor.save();
    });

    expect(services.upsertChartNode).not.toHaveBeenCalled();
    expect(result.current.chartAccountEditor.statusText).toBe("Chart account is missing required fields.");

    act(() => {
      result.current.chartAccountEditor.updateDraft({ path: "Expenses:Management Fees" });
    });
    expect(result.current.chartAccountEditor.canSave).toBe(true);

    await act(async () => {
      await result.current.chartAccountEditor.save();
    });

    expect(services.upsertChartNode).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["evidence://chart/management-fees"],
      node: expect.objectContaining({
        nodeId: "coa-management-fees",
        path: "Expenses:Management Fees",
        accountName: "Management Fees",
        accountType: "Expense",
        parentPath: "Expenses",
        financialAccountId: "gl-6100-management-fees",
        isArchived: false
      })
    }));
    expect(result.current.metricRows.find((row) => row.id === "chart")?.value).toBe("3");
    expect(result.current.chartAccountEditor.statusText).toBe("Saved chart account Expenses:Management Fees.");
    expect(result.current.selectedRule?.conditionRows.join("\n")).toContain("group-trade-source: Any (required)");
    expect(result.current.selectedRule?.conditionRows.join("\n")).toContain("event.source Equals Broker");
    expect(result.current.selectedRule?.formulaRows.join("\n")).toContain("formula-source: SourceAmount $250,000 USD");
    expect(result.current.selectedRule?.allocationRows.join("\n")).toContain("alloc-strategy: StrategyWeight weight 1 via formula-source");
    expect(result.current.selectedRule?.generatedPostingRows.join("\n")).toContain("Debit 1200.Investments $250,000 USD via formula-source");
    expect(result.current.selectedRule?.versionRows.join("\n")).toContain("v3 by controller on 2026-06-15");
    expect(result.current.metricRows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "rules",
        value: "1",
        detail: "1 generated / 0 template mappings."
      }),
      expect.objectContaining({
        id: "rule-tests",
        value: "1",
        detail: "1 rule(s) covered; 0 current version gap(s)."
      })
    ]));
    expect(result.current.ledgerBookSummaryLabel).toBe("1 ledger book registered | selected Primary book.");
    expect(result.current.ledgerBookRows).toEqual([
      expect.objectContaining({
        id: "Selected ledger book",
        title: "Primary book",
        statusLabel: "Selected",
        subtitle: "Gaap basis | USD",
        policyLabel: "Gaap policy | version 2026.06",
        scopeLabel: "Entity scope",
        updatedLabel: "Updated 2026-06-30",
        tone: "success"
      })
    ]);
    expect(services.assessProductionReadiness).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      requiredLedgerBookScopes: null
    }));
    expect(result.current.productionReadiness).toMatchObject({
      statusLabel: "Review required",
      scoreLabel: "78/100",
      issueSummaryLabel: "1 warning requires review",
      externalGlLabel: "3 providers | 1 certified mapping | live posting disabled",
      ledgerBookRolloutLabel: "1 book | 1 open period | 0 rollout blockers | 4/10 workflow controls",
      dimensionalReportingLabel: "4/9 ledger/query/report/export dimension controls | ledger book book-primary",
      dimensionalReportingEvidenceLabel: "1 retained dimensional evidence reference",
      tenantAdministrationLabel: "5/23 admin controls | tenant tenant-alpha | company company-alpha",
      tenantAdministrationEvidenceLabel: "1 retained setup evidence reference",
      migrationWorkerPlanSummaryLabel: "1/1 retained worker plan reconciled"
    });
    expect(result.current.productionReadiness.migrationWorkerPlanRows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "worker-plan-historical-book-primary",
        kindLabel: "Historical journal backfill",
        countLabel: "275 source records -> 275 migrated records",
        evidenceLabel: "1 evidence reference",
        tone: "success"
      })
    ]));
    expect(services.listMigrationWorkerPlans).toHaveBeenCalledWith({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary"
    });
    expect(result.current.productionReadiness.productionGapRows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "multi-ledger-native-workflows",
        label: "Configurable multi-ledger accounting",
        statusLabel: "Review required",
        severityLabel: "Warning",
        areaLabel: "Ledger Books, Posting Rules, Journal Lifecycle, Close Reporting",
        blockingIssueLabel: "workflow.evidence.missing, journal.lifecycle.missing",
        issueDetailLabel: "workflow.evidence.missing: Ledger-book workflow evidence is missing for the selected book. -> Retain selected-book workflow evidence before production rollout.",
        routeLabel: "/accounting/configure, /accounting/journal-entries",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "external-gl-guarded-integration",
        label: "External GL guarded integration",
        severityLabel: "Info",
        areaLabel: "External GL",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "production-controls-hardening",
        label: "Production controls and rollout hardening",
        blockingIssueLabel: "migration.close-reporting-evidence-not-certified",
        routeLabel: "/accounting/configure, /settings, /accounting/close"
      })
    ]));
    expect(result.current.productionReadiness.tenantAdministrationControls).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "tenant-scope", statusLabel: "Ready", tone: "success" }),
      expect.objectContaining({ id: "reporting-groups", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "operator-surface", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "browser-admin-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "wpf-admin-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "chart-administration-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "rule-test-promotion-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "provider-mapping-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "audit-review-tooling", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "bulk-import-export-safeguards", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "performance-validation", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "disaster-recovery-runbook", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "ledger-book-administration-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "posting-rule-authoring-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "approval-queue-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "dimension-mapping-studio", statusLabel: "Missing", tone: "danger" }),
      expect.objectContaining({ id: "implementation-sandbox", statusLabel: "Missing", tone: "danger" })
    ]));
    expect(result.current.productionReadiness.migrationPlanRows).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "ledger-book-scope",
        statusLabel: "Ready",
        certificationLabel: "Certified",
        latestRunLabel: "migration-run-ledger-book-scope-book-primary | Certified",
        metricsLabel: "24 records | 0 issues",
        tone: "success"
      }),
      expect.objectContaining({
        id: "historical-journal-backfill",
        statusLabel: "Blocked",
        certificationLabel: "Not certified",
        latestRunLabel: "No retained run",
        blockingIssueLabel: "migration.historical-journal-backfill-not-certified",
        tone: "danger"
      })
    ]));
    expect(result.current.productionReadiness.components).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "RulesStudio",
        statusLabel: "Ready",
        tone: "success"
      }),
      expect.objectContaining({
        id: "TenantAdministration",
        statusLabel: "Review required",
        tone: "warning"
      })
    ]));
    expect(result.current.productionReadiness.blockerIssues).toEqual([
      expect.objectContaining({
        label: "Tenant Administration | Warning",
        message: "Production rollout still needs tenant setup controls.",
        tone: "warning"
      })
    ]);
    expect(result.current.productionCertificationProfile.scopeLabel).toBe("Tenant tenant-alpha | company company-alpha | fund fund-alpha | ledger book book-primary");
    expect(result.current.productionCertificationProfile.controls).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "posting-rules-book", checked: true }),
      expect.objectContaining({ id: "close-reporting-book", checked: false }),
      expect.objectContaining({ id: "close-plan-configuration-book", checked: false }),
      expect.objectContaining({ id: "reconciliation-book", checked: false }),
      expect.objectContaining({ id: "direct-lending-book", checked: false }),
      expect.objectContaining({ id: "strategy-ledger-reads-book", checked: false }),
      expect.objectContaining({ id: "ledger-line-dimensions", checked: false }),
      expect.objectContaining({ id: "trial-balance-dimensions", checked: false }),
      expect.objectContaining({ id: "report-package-dimensions", checked: false }),
      expect.objectContaining({ id: "cross-period-dimensions", checked: false })
    ]));
    expect(result.current.productionCertificationProfile.canSave).toBe(true);

    act(() => {
      result.current.productionCertificationProfile.updateControl("close-reporting-book", true);
      result.current.productionCertificationProfile.updateControl("close-plan-configuration-book", true);
      result.current.productionCertificationProfile.updateControl("cross-period-dimensions", true);
      result.current.productionCertificationProfile.updateEvidence("   ");
    });

    expect(result.current.productionCertificationProfile.canSave).toBe(false);
    expect(result.current.productionCertificationProfile.saveDisabledReason).toBe("Retained evidence is required before saving production certification controls.");

    await act(async () => {
      await result.current.productionCertificationProfile.save();
    });

    expect(services.upsertProductionCertificationProfile).not.toHaveBeenCalled();
    expect(result.current.productionCertificationProfile.statusText).toBe("Retained evidence is required before saving production certification controls.");

    act(() => {
      result.current.productionCertificationProfile.updateEvidence("evidence://ledger-book/book-primary/workflow-certification\nevidence://ledger-book/book-primary/close-reporting");
    });

    await act(async () => {
      await result.current.productionCertificationProfile.save();
    });

    expect(services.upsertProductionCertificationProfile).toHaveBeenCalledWith(expect.objectContaining({
      actor: "browser-accounting-operator",
      profile: expect.objectContaining({
        fundProfileId: "fund-alpha",
        ledgerBookId: "book-primary",
        closeReportingLedgerBookNativeCertified: true,
        closePlanConfigurationLedgerBookNativeCertified: true,
        crossPeriodReportDimensionQueriesCertified: true,
        evidenceReferences: expect.arrayContaining([
          "evidence://ledger-book/book-primary/workflow-certification",
          "evidence://ledger-book/book-primary/close-reporting",
          "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/posting-candidate",
          "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/journal-lifecycle",
          "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/close-reporting",
          "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/close-plan-configuration",
          "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/dimensions/period-report/dimension-scope/canonical-production",
          "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/dimensions/cross-period/dimension-scope/canonical-production",
          "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/dimensions/journal-query/dimension-scope/canonical-production"
        ]),
        workflowCertificationArtifacts: expect.arrayContaining([
          expect.objectContaining({
            status: "Certified",
            tenantId: "tenant-alpha",
            companyId: "company-alpha",
            fundProfileId: "fund-alpha",
            ledgerBookId: "book-primary",
            sourceService: "browser-accounting-configure",
            lanes: expect.arrayContaining([
              expect.objectContaining({ kind: "PostingRules", status: "Passed" }),
              expect.objectContaining({ kind: "JournalLifecycle", status: "Passed" }),
              expect.objectContaining({ kind: "CloseReporting", status: "Passed" }),
              expect.objectContaining({ kind: "ClosePlanConfiguration", status: "Passed" })
            ])
          })
        ]),
        dimensionalCertificationArtifacts: expect.arrayContaining([
          expect.objectContaining({
            status: "Certified",
            dimensionScopeEvidenceKey: "canonical-production",
            sourceService: "browser-accounting-configure",
            lanes: expect.arrayContaining([
              expect.objectContaining({ kind: "PeriodReports", status: "Passed" }),
              expect.objectContaining({ kind: "CrossPeriodReports", status: "Passed" }),
              expect.objectContaining({ kind: "JournalFilters", status: "Passed" })
            ])
          })
        ]),
        tenantAdminCertificationArtifacts: expect.arrayContaining([
          expect.objectContaining({
            status: "Certified",
            tenantId: "tenant-alpha",
            companyId: "company-alpha",
            fundProfileId: "fund-alpha",
            ledgerBookId: "book-primary",
            sourceService: "browser-accounting-configure",
            lanes: expect.arrayContaining([
              expect.objectContaining({ kind: "TenantScope", status: "Passed" }),
              expect.objectContaining({ kind: "AdminRoleProfile", status: "Passed" }),
              expect.objectContaining({ kind: "ScopedAccessPolicies", status: "Passed" })
            ])
          })
        ])
      }),
      evidenceLinks: expect.arrayContaining([
        "evidence://ledger-book/book-primary/workflow-certification",
        "evidence://ledger-book/book-primary/close-reporting",
        "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/posting-candidate",
        "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/journal-lifecycle",
        "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/close-reporting",
        "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/close-plan-configuration",
        "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/dimensions/period-report/dimension-scope/canonical-production",
        "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/dimensions/cross-period/dimension-scope/canonical-production",
        "evidence://tenant/tenant-alpha/company/company-alpha/fund/fund-alpha/ledger-book/book-primary/production-certification/dimensions/journal-query/dimension-scope/canonical-production"
      ])
    }));
    expect(result.current.productionCertificationProfile.statusText).toBe("Production certification profile saved; readiness refreshed from retained book and dimension controls.");
    expect(result.current.tenantAdministrationProfile.scopeLabel).toBe("Tenant tenant-alpha | company company-alpha");
    expect(result.current.tenantAdministrationProfile.controls).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "operator-surface", checked: false }),
      expect.objectContaining({ id: "chart-administration-studio", checked: false }),
      expect.objectContaining({ id: "rule-test-promotion-studio", checked: false }),
      expect.objectContaining({ id: "close-setup-studio", checked: false }),
      expect.objectContaining({ id: "provider-mapping-studio", checked: false }),
      expect.objectContaining({ id: "tenant-company-report-group-studio", checked: false }),
      expect.objectContaining({ id: "audit-review-tooling", checked: false }),
      expect.objectContaining({ id: "bulk-import-export-safeguards", checked: false }),
      expect.objectContaining({ id: "performance-validation", checked: false }),
      expect.objectContaining({ id: "disaster-recovery-runbook", checked: false }),
      expect.objectContaining({ id: "ledger-book-administration-studio", checked: false }),
      expect.objectContaining({ id: "posting-rule-authoring-studio", checked: false }),
      expect.objectContaining({ id: "approval-queue-studio", checked: false }),
      expect.objectContaining({ id: "dimension-mapping-studio", checked: false }),
      expect.objectContaining({ id: "implementation-sandbox", checked: false })
    ]));
    expect(result.current.tenantAdministrationProfile.canSave).toBe(true);
    expect(result.current.tenantAdministrationProfile.canRetainSandboxProof).toBe(true);

    act(() => {
      result.current.tenantAdministrationProfile.updateEvidence("   ");
    });
    expect(result.current.tenantAdministrationProfile.canSave).toBe(false);
    expect(result.current.tenantAdministrationProfile.saveDisabledReason)
      .toBe("Retained setup evidence is required before saving tenant administration controls.");

    await act(async () => {
      await result.current.tenantAdministrationProfile.save();
    });

    expect(services.upsertTenantAdministrationProfile).not.toHaveBeenCalled();
    expect(result.current.tenantAdministrationProfile.statusText)
      .toBe("Retained setup evidence is required before saving tenant administration controls.");

    act(() => {
      result.current.tenantAdministrationProfile.updateEvidence("evidence://tenant-admin/setup");
      result.current.tenantAdministrationProfile.updateControl("approval-queue-studio", true);
      result.current.tenantAdministrationProfile.updateControl("dimension-mapping-studio", true);
      result.current.tenantAdministrationProfile.updateApprovalQueueSetup({ queueId: "" });
    });
    expect(result.current.tenantAdministrationProfile.canSave).toBe(false);
    expect(result.current.tenantAdministrationProfile.saveDisabledReason)
      .toBe("Complete approval queue id, workflow kind, approval role/count, segregation policy, and evidence requirement before saving approval queue setup.");
    expect(result.current.tenantAdministrationProfile.canRetainSandboxProof).toBe(false);
    expect(result.current.tenantAdministrationProfile.sandboxDisabledReason)
      .toBe("Complete approval queue id, workflow kind, approval role/count, segregation policy, and evidence requirement before retaining implementation sandbox proof.");

    await act(async () => {
      await result.current.tenantAdministrationProfile.save();
    });

    expect(services.upsertTenantAdministrationProfile).not.toHaveBeenCalled();
    expect(result.current.tenantAdministrationProfile.statusText)
      .toBe("Complete approval queue id, workflow kind, approval role/count, segregation policy, and evidence requirement before saving approval queue setup.");

    act(() => {
      result.current.tenantAdministrationProfile.updateApprovalQueueSetup({
        queueId: "sandbox-configuration-approval",
        displayName: "Sandbox configuration approval",
        workflowKind: "ConfigurationPromotion",
        requiredApprovalRole: "Controller",
        requiredApprovalCount: "2",
        segregationPolicy: "Preparer cannot approve own sandbox configuration proof.",
        evidenceRequirement: "sandbox-proof;configuration-approval;segregation-review"
      });
      result.current.tenantAdministrationProfile.updateDimensionMappingSetup({ mappingId: "" });
    });
    expect(result.current.tenantAdministrationProfile.canSave).toBe(false);
    expect(result.current.tenantAdministrationProfile.saveDisabledReason)
      .toBe("Complete dimension mapping id, display name, provider id, Meridian dimensions, provider dimensions, and evidence requirement before saving dimension mapping setup.");
    expect(result.current.tenantAdministrationProfile.canRetainSandboxProof).toBe(false);
    expect(result.current.tenantAdministrationProfile.sandboxDisabledReason)
      .toBe("Complete dimension mapping id, display name, provider id, Meridian dimensions, provider dimensions, and evidence requirement before retaining implementation sandbox proof.");

    await act(async () => {
      await result.current.tenantAdministrationProfile.save();
    });

    expect(services.upsertTenantAdministrationProfile).not.toHaveBeenCalled();
    expect(result.current.tenantAdministrationProfile.statusText)
      .toBe("Complete dimension mapping id, display name, provider id, Meridian dimensions, provider dimensions, and evidence requirement before saving dimension mapping setup.");

    act(() => {
      result.current.tenantAdministrationProfile.updateDimensionMappingSetup({
        mappingId: "sandbox-qbo-dimension-map",
        displayName: "Sandbox QuickBooks dimensions",
        providerId: "quickbooks-fixture",
        meridianDimensionsText: "fundId=fund-alpha\nbookId=book-primary\ncostCenterId=sandbox-accounting",
        providerDimensionsText: "Class=fund-alpha\nBook=book-primary\nDepartment=sandbox-accounting",
        evidenceRequirement: "sandbox-proof;dimension-mapping;controller-approval"
      });
    });
    expect(result.current.tenantAdministrationProfile.canSave).toBe(true);
    expect(result.current.tenantAdministrationProfile.canRetainSandboxProof).toBe(true);

    await act(async () => {
      await result.current.tenantAdministrationProfile.retainSandboxProof();
    });

    expect(services.upsertTenantAdministrationProfile).toHaveBeenCalledWith(expect.objectContaining({
      actor: "browser-accounting-operator",
      profile: expect.objectContaining({
        tenantId: "tenant-alpha",
        companyId: "company-alpha",
        approvalQueueStudioConfigured: true,
        approvalQueueConfigurations: [
          expect.objectContaining({
            queueId: "sandbox-configuration-approval",
            displayName: "Sandbox configuration approval",
            workflowKind: "ConfigurationPromotion",
            requiredApprovalRole: "Controller",
            requiredApprovalCount: 2,
            segregationPolicy: "Preparer cannot approve own sandbox configuration proof.",
            evidenceRequirement: "sandbox-proof;configuration-approval;segregation-review"
          })
        ],
        dimensionMappingStudioConfigured: true,
        dimensionMappingConfigurations: [
          expect.objectContaining({
            mappingId: "sandbox-qbo-dimension-map",
            displayName: "Sandbox QuickBooks dimensions",
            providerId: "quickbooks-fixture",
            meridianDimensions: expect.objectContaining({
              fundId: "fund-alpha",
              bookId: "book-primary",
              costCenterId: "sandbox-accounting"
            }),
            providerDimensions: expect.objectContaining({
              externalGlDimensions: expect.objectContaining({
                Class: "fund-alpha",
                Book: "book-primary",
                Department: "sandbox-accounting"
              })
            }),
            evidenceRequirement: "sandbox-proof;dimension-mapping;controller-approval"
          })
        ],
        implementationSandboxConfigured: true,
        evidenceReferences: expect.arrayContaining([
          "evidence://tenant-admin/tenant-alpha/company-alpha/implementation-sandbox/ledgerBookId=book-primary",
          "evidence://tenant-admin/tenant-alpha/company-alpha/sandbox-validation/ledgerBookId=book-primary",
          "evidence://tenant-admin/tenant-alpha/company-alpha/fixture-validation/ledgerBookId=book-primary",
          "evidence://tenant-admin/tenant-alpha/company-alpha/implementation-fixture/ledgerBookId=book-primary"
        ])
      }),
      evidenceLinks: expect.arrayContaining([
        "evidence://tenant-admin/tenant-alpha/company-alpha/implementation-sandbox/ledgerBookId=book-primary",
        "evidence://tenant-admin/tenant-alpha/company-alpha/sandbox-validation/ledgerBookId=book-primary",
        "evidence://tenant-admin/tenant-alpha/company-alpha/fixture-validation/ledgerBookId=book-primary",
        "evidence://tenant-admin/tenant-alpha/company-alpha/implementation-fixture/ledgerBookId=book-primary"
      ])
    }));
    expect(result.current.tenantAdministrationProfile.sandboxStatusText)
      .toBe("Implementation sandbox proof retained; readiness refreshed from validation and ledger-book evidence.");

    act(() => {
      result.current.tenantAdministrationProfile.updateControl("operator-surface", true);
      result.current.tenantAdministrationProfile.updateControl("chart-administration-studio", true);
      result.current.tenantAdministrationProfile.updateControl("rule-test-promotion-studio", true);
      result.current.tenantAdministrationProfile.updateControl("close-setup-studio", true);
      result.current.tenantAdministrationProfile.updateControl("provider-mapping-studio", true);
      result.current.tenantAdministrationProfile.updateControl("tenant-company-report-group-studio", true);
      result.current.tenantAdministrationProfile.updateControl("audit-review-tooling", true);
      result.current.tenantAdministrationProfile.updateControl("bulk-import-export-safeguards", true);
      result.current.tenantAdministrationProfile.updateControl("performance-validation", true);
      result.current.tenantAdministrationProfile.updateControl("disaster-recovery-runbook", true);
      result.current.tenantAdministrationProfile.updateControl("ledger-book-administration-studio", true);
      result.current.tenantAdministrationProfile.updateControl("posting-rule-authoring-studio", true);
      result.current.tenantAdministrationProfile.updateControl("approval-queue-studio", true);
      result.current.tenantAdministrationProfile.updateControl("dimension-mapping-studio", true);
      result.current.tenantAdministrationProfile.updateControl("implementation-sandbox", true);
      result.current.tenantAdministrationProfile.updateApprovalQueueSetup({
        queueId: "configuration-promotion-queue",
        displayName: "Configuration promotion queue",
        workflowKind: "ConfigurationPromotion",
        requiredApprovalRole: "Controller",
        requiredApprovalCount: "2",
        segregationPolicy: "Preparer cannot approve own configuration change.",
        evidenceRequirement: "approval-queue;configuration-approval;segregation-review"
      });
      result.current.tenantAdministrationProfile.updateDimensionMappingSetup({
        mappingId: "qbo-fund-alpha-dimension-map",
        displayName: "QuickBooks fund alpha dimensions",
        providerId: "quickbooks-fixture",
        meridianDimensionsText: "fundId=fund-alpha\nbookId=book-primary\ncostCenterId=fund-accounting",
        providerDimensionsText: "Class=fund-alpha\nBook=book-primary\nDepartment=fund-accounting",
        evidenceRequirement: "dimension-mapping;provider-segment-review;controller-approval"
      });
      result.current.tenantAdministrationProfile.updateEvidence("evidence://tenant-admin/setup\nevidence://tenant-admin/operator-surface\nevidence://tenant-admin/chart-administration\nevidence://tenant-admin/rules-studio\nevidence://tenant-admin/close-setup\nevidence://tenant-admin/provider-mapping\nevidence://tenant-admin/tenant-company-report-group\nevidence://tenant-admin/audit-review\nevidence://tenant-admin/bulk-import-export\nevidence://tenant-admin/performance-validation\nevidence://tenant-admin/disaster-recovery\nevidence://tenant-admin/ledger-book-administration\nevidence://tenant-admin/posting-rule-authoring\nevidence://tenant-admin/approval-queue\nevidence://tenant-admin/dimension-mapping\nevidence://tenant-admin/implementation-sandbox");
    });

    await act(async () => {
      await result.current.tenantAdministrationProfile.save();
    });

    expect(services.upsertTenantAdministrationProfile).toHaveBeenCalledWith(expect.objectContaining({
      actor: "browser-accounting-operator",
      profile: expect.objectContaining({
        tenantId: "tenant-alpha",
        companyId: "company-alpha",
        accountingAdminSurfaceConfigured: true,
        browserAccountingAdminSurfaceConfigured: true,
        wpfAccountingAdminSurfaceConfigured: false,
        chartAdministrationStudioConfigured: true,
        ruleTestPromotionStudioConfigured: true,
        closeSetupStudioConfigured: true,
        providerMappingStudioConfigured: true,
        tenantCompanyReportGroupSetupStudioConfigured: true,
        auditReviewToolingConfigured: true,
        bulkImportExportSafeguardsConfigured: true,
        performanceValidationConfigured: true,
        disasterRecoveryRunbookConfigured: true,
        ledgerBookAdministrationStudioConfigured: true,
        postingRuleAuthoringStudioConfigured: true,
        approvalQueueStudioConfigured: true,
        approvalQueueConfigurations: [
          expect.objectContaining({
            queueId: "configuration-promotion-queue",
            displayName: "Configuration promotion queue",
            workflowKind: "ConfigurationPromotion",
            requiredApprovalRole: "Controller",
            requiredApprovalCount: 2,
            segregationPolicy: "Preparer cannot approve own configuration change.",
            evidenceRequirement: "approval-queue;configuration-approval;segregation-review"
          })
        ],
        dimensionMappingStudioConfigured: true,
        dimensionMappingConfigurations: [
          expect.objectContaining({
            mappingId: "qbo-fund-alpha-dimension-map",
            displayName: "QuickBooks fund alpha dimensions",
            providerId: "quickbooks-fixture",
            meridianDimensions: expect.objectContaining({
              fundId: "fund-alpha",
              bookId: "book-primary",
              costCenterId: "fund-accounting"
            }),
            providerDimensions: expect.objectContaining({
              externalGlDimensions: expect.objectContaining({
                Class: "fund-alpha",
                Book: "book-primary",
                Department: "fund-accounting"
              })
            }),
            evidenceRequirement: "dimension-mapping;provider-segment-review;controller-approval"
          })
        ],
        implementationSandboxConfigured: true,
        evidenceReferences: expect.arrayContaining([
          "evidence://tenant-admin/setup",
          "evidence://tenant-admin/operator-surface",
          "evidence://tenant-admin/chart-administration",
          "evidence://tenant-admin/rules-studio",
          "evidence://tenant-admin/close-setup",
          "evidence://tenant-admin/provider-mapping",
          "evidence://tenant-admin/tenant-company-report-group",
          "evidence://tenant-admin/audit-review",
          "evidence://tenant-admin/bulk-import-export",
          "evidence://tenant-admin/performance-validation",
          "evidence://tenant-admin/disaster-recovery",
          "evidence://tenant-admin/ledger-book-administration",
          "evidence://tenant-admin/posting-rule-authoring",
          "evidence://tenant-admin/approval-queue",
          "evidence://tenant-admin/dimension-mapping",
          "evidence://tenant-admin/implementation-sandbox",
          "evidence://tenant-admin/tenant-alpha/company-alpha/ledger-book-administration/ledgerBookId=book-primary",
          "evidence://tenant-admin/tenant-alpha/company-alpha/implementation-sandbox/ledgerBookId=book-primary",
          "evidence://tenant-admin/tenant-alpha/company-alpha/sandbox-validation/ledgerBookId=book-primary",
          "evidence://tenant-admin/tenant-alpha/company-alpha/fixture-validation/ledgerBookId=book-primary",
          "evidence://tenant-admin/tenant-alpha/company-alpha/implementation-fixture/ledgerBookId=book-primary"
        ])
      }),
      evidenceLinks: expect.arrayContaining([
        "evidence://tenant-admin/setup",
        "evidence://tenant-admin/operator-surface",
        "evidence://tenant-admin/chart-administration",
        "evidence://tenant-admin/rules-studio",
        "evidence://tenant-admin/close-setup",
        "evidence://tenant-admin/provider-mapping",
        "evidence://tenant-admin/tenant-company-report-group",
        "evidence://tenant-admin/audit-review",
        "evidence://tenant-admin/bulk-import-export",
        "evidence://tenant-admin/performance-validation",
        "evidence://tenant-admin/disaster-recovery",
        "evidence://tenant-admin/ledger-book-administration",
        "evidence://tenant-admin/posting-rule-authoring",
        "evidence://tenant-admin/approval-queue",
        "evidence://tenant-admin/dimension-mapping",
        "evidence://tenant-admin/implementation-sandbox",
        "evidence://tenant-admin/tenant-alpha/company-alpha/ledger-book-administration/ledgerBookId=book-primary",
        "evidence://tenant-admin/tenant-alpha/company-alpha/implementation-sandbox/ledgerBookId=book-primary",
        "evidence://tenant-admin/tenant-alpha/company-alpha/sandbox-validation/ledgerBookId=book-primary",
        "evidence://tenant-admin/tenant-alpha/company-alpha/fixture-validation/ledgerBookId=book-primary",
        "evidence://tenant-admin/tenant-alpha/company-alpha/implementation-fixture/ledgerBookId=book-primary"
      ])
    }));
    expect(result.current.tenantAdministrationProfile.statusText).toBe("Tenant administration setup profile saved; production readiness refreshed from retained controls.");
    expect(result.current.externalGlMappingProfile.scopeLabel).toBe("Fund fund-alpha | ledger book book-primary");
    expect(result.current.externalGlMappingProfile.canSave).toBe(true);

    act(() => {
      result.current.externalGlMappingProfile.updateProviderId("quickbooks-fixture");
      result.current.externalGlMappingProfile.updateProfileId("qbo-fund-alpha-book-primary");
      result.current.externalGlMappingProfile.updateDisplayName("Fund Alpha QuickBooks mapping");
      result.current.externalGlMappingProfile.updateMeridianDimensions("fundId=fund-alpha\nbookId=book-primary\ncustomerId=investor-alpha\nProject=direct-lending");
      result.current.externalGlMappingProfile.updateExternalDimensions("bookId=Book:book-primary\ncustomerId=qbo-customer-alpha\nProject=qbo-project-credit");
      result.current.externalGlMappingProfile.updateEvidence("approval:external-gl-mapping:qbo-fund-alpha-book-primary");
      result.current.externalGlMappingProfile.updateCertified(true);
      result.current.externalGlMappingProfile.updateAccountMappings("   ");
    });
    expect(result.current.externalGlMappingProfile.canSave).toBe(false);
    expect(result.current.externalGlMappingProfile.saveDisabledReason).toBe("At least one account mapping is required.");

    await act(async () => {
      await result.current.externalGlMappingProfile.save();
    });

    expect(services.upsertExternalGlMappingProfile).not.toHaveBeenCalled();
    expect(result.current.externalGlMappingProfile.statusText)
      .toBe("Provider, profile id, display name, account mappings, and retained evidence are required before saving an external GL mapping profile.");

    act(() => {
      result.current.externalGlMappingProfile.updateAccountMappings("Assets:Cash:Operating=qbo-1000\nIncome:Investment Income=qbo-4000");
    });
    expect(result.current.externalGlMappingProfile.canSave).toBe(true);

    await act(async () => {
      await result.current.externalGlMappingProfile.save();
    });

    expect(services.upsertExternalGlMappingProfile).toHaveBeenCalledWith(expect.objectContaining({
      actor: "browser-accounting-operator",
      providerId: "quickbooks-fixture",
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      tenantId: "tenant-alpha",
      companyId: "company-alpha",
      actionOrigin: "HumanOperator",
      profile: expect.objectContaining({
        profileId: "qbo-fund-alpha-book-primary",
        providerId: "quickbooks-fixture",
        displayName: "Fund Alpha QuickBooks mapping",
        certificationState: "Certified",
        accountMappings: expect.objectContaining({
          "Assets:Cash:Operating": "qbo-1000",
          "Income:Investment Income": "qbo-4000"
        }),
        dimensionMappings: [
          expect.objectContaining({
            certificationState: "Certified",
            meridianDimensions: expect.objectContaining({
              fundId: "fund-alpha",
              bookId: "book-primary",
              customerId: "investor-alpha",
              externalGlDimensions: expect.objectContaining({
                Project: "direct-lending"
              })
            }),
            externalDimensions: expect.objectContaining({
              bookId: "Book:book-primary",
              customerId: "qbo-customer-alpha",
              externalGlDimensions: expect.objectContaining({
                Project: "qbo-project-credit"
              })
            })
          })
        ]
      }),
      evidenceLinks: expect.arrayContaining([
        "approval:external-gl-mapping:qbo-fund-alpha-book-primary",
        "evidence://external-gl/mapping-certification/provider/quickbooks-fixture/fund/fund-alpha/profile/qbo-fund-alpha-book-primary",
        "evidence://ledger-book/book-primary/external-gl/mapping-certification/qbo-fund-alpha-book-primary"
      ])
    }));
    expect(result.current.externalGlMappingProfile.statusText).toBe("External GL mapping profile qbo-fund-alpha-book-primary saved as Certified; readiness refreshed from retained provider mapping.");
    expect(result.current.selectedRule?.promotionReadiness).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "server-readiness",
        value: "Ready",
        detail: "0 critical, 0 warning, 1 saved test case(s).",
        tone: "success"
      }),
      expect.objectContaining({
        id: "promotion-approval",
        value: "Approved",
        tone: "success"
      }),
      expect.objectContaining({
        id: "saved-regression",
        value: "1 saved",
        tone: "success"
      }),
      expect.objectContaining({
        id: "latest-suite",
        value: "Not run",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "activation-gate",
        value: "Ready",
        tone: "success"
      })
    ]));
    expect(result.current.canDryRun).toBe(true);
    expect(result.current.ruleTestCases).toEqual([
      expect.objectContaining({
        id: "rule-test-trade-buy-saved",
        title: "Saved trade buy regression",
        assertionLabel: "Expect rule-trade-buy version v3, balanced, no expected issue codes, no expected generated posting lines.",
        evidenceLabel: "1 evidence link",
        evidenceTone: "success"
      })
    ]);

    await act(async () => {
      await result.current.dryRunSelectedRule();
    });

    expect(services.dryRunRule).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      sourceEventType: "TradeExecuted",
      eventAmount: 250000,
      currency: "USD",
      effectiveDate: "2026-01-01",
      actor: "browser-accounting-operator",
      counterpartyId: "cp-001",
      dimensions: expect.objectContaining({
        fundId: "fund-alpha",
        strategyId: "strategy-long-only"
      })
    }));
    await waitFor(() => expect(result.current.dryRunPreview).not.toBeNull());
    expect(result.current.dryRunPreview).toMatchObject({
      title: "TradeExecuted dry run",
      balanceLabel: "Balanced $250,000 USD",
      selectedRuleLabel: "Selected rule rule-trade-buy"
    });
    expect(result.current.dryRunPreview?.matchRows.join("\n")).toContain("Trade buy posting matched at priority 10");
    expect(result.current.dryRunPreview?.generatedLineRows.join("\n")).toContain("Debit 1200.Investments $250,000 USD");
    expect(result.current.dryRunPreview?.generatedPostingRows.join("\n")).toContain("Credit 1000.Cash $250,000 USD via formula-source");

    await act(async () => {
      await result.current.applyDryRunEventPredicate();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/event-predicate/rule-trade-buy"],
      rule: expect.objectContaining({
        ruleId: "rule-trade-buy",
        ruleVersion: "v3.event",
        requiresPromotionApproval: true,
        promotionApproval: null,
        conditions: expect.arrayContaining([
          expect.objectContaining({
            conditionId: "rule-trade-buy-source-event",
            field: "event.kind",
            operator: "Equals",
            value: "TradeExecuted",
            isRequired: true
          })
        ])
      })
    }));
    expect(result.current.applyEventPredicateStatusText).toBe("Applied event predicate to rule-trade-buy.");
    await waitFor(() => expect(result.current.selectedRule?.subtitle).toContain("v3.event"));

    await act(async () => {
      await result.current.applyDryRunEffectiveStart();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/effective-start/rule-trade-buy"],
      rule: expect.objectContaining({
        ruleId: "rule-trade-buy",
        ruleVersion: "v3.event.effective",
        effectiveFrom: "2026-01-01",
        effectiveTo: "2026-12-31",
        requiresPromotionApproval: true,
        promotionApproval: null
      })
    }));
    expect(result.current.applyEffectiveStartStatusText).toBe("Applied effective start to rule-trade-buy.");
    await waitFor(() => expect(result.current.selectedRule?.subtitle).toContain("v3.event.effective"));

    await act(async () => {
      await result.current.captureDryRunGeneratedPostings();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/generated-postings/rule-trade-buy"],
      rule: expect.objectContaining({
        ruleId: "rule-trade-buy",
        ruleVersion: "v3.event.effective.postings",
        requiresPromotionApproval: true,
        promotionApproval: null,
        generatedPostings: expect.arrayContaining([
          expect.objectContaining({
            lineId: "generated-cash",
            accountPath: "1000.Cash",
            side: "Credit",
            amountFormulaId: "formula-source",
            amount: 250000,
            dimensions: expect.objectContaining({
              fundId: "fund-alpha",
              counterpartyId: "cp-001"
            })
          })
        ])
      })
    }));
    expect(result.current.capturePostingsStatusText).toBe("Captured generated postings for rule-trade-buy.");
    await waitFor(() => expect(result.current.selectedRule?.subtitle).toContain("v3.event.effective.postings"));

    await act(async () => {
      await result.current.applyDryRunScope();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/scope/rule-trade-buy"],
      rule: expect.objectContaining({
        ruleId: "rule-trade-buy",
        ruleVersion: "v3.event.effective.postings.scope",
        requiresPromotionApproval: true,
        promotionApproval: null,
        scope: expect.objectContaining({
          fundId: "fund-alpha",
          entityId: "entity-master",
          strategyId: "strategy-long-only",
          instrumentId: "AAPL",
          counterpartyId: "cp-001",
          externalGlDimensions: expect.objectContaining({
            class: "FundAlpha"
          })
        })
      })
    }));
    expect(result.current.applyScopeStatusText).toBe("Applied dry-run scope to rule-trade-buy.");
    await waitFor(() => expect(result.current.selectedRule?.subtitle).toContain("v3.event.effective.postings.scope"));

    await act(async () => {
      await result.current.saveDryRunAsRuleTest();
    });

    expect(services.saveRuleTestCase).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      testCase: expect.objectContaining({
        testCaseId: "rule-test-rule-trade-buy",
        expectedRuleId: "rule-trade-buy",
        expectedRuleVersion: "v3.event.effective.postings.scope",
        expectBalancedPosting: true,
        evidenceLinks: expect.arrayContaining([
          "browser://accounting/rules-studio/dry-run/rule-trade-buy",
          "browser://accounting/rules-studio/test-case/rule-trade-buy"
        ])
      })
    }));
    expect(result.current.ruleTestCases).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "rule-test-rule-trade-buy",
        title: "Trade buy posting retained dry-run regression",
        evidenceTone: "success"
      })
    ]));

    await act(async () => {
      await result.current.applyDryRunAmountThreshold();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/threshold/rule-trade-buy"],
      rule: expect.objectContaining({
        ruleId: "rule-trade-buy",
        ruleVersion: "v3.event.effective.postings.scope.threshold",
        requiresPromotionApproval: true,
        promotionApproval: null,
        conditions: expect.arrayContaining([
          expect.objectContaining({
            conditionId: "rule-trade-buy-minimum-amount",
            field: "event.amount",
            operator: "AmountGreaterThanOrEqual",
            value: "250000",
            isRequired: true
          })
        ])
      })
    }));
    expect(result.current.applyThresholdStatusText).toBe("Applied amount threshold to rule-trade-buy.");
    expect(result.current.selectedRule?.conditionRows.join("\n")).toContain("event.amount AmountGreaterThanOrEqual 250000");

    await act(async () => {
      await result.current.applyDryRunFormulaAmount();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/formula/rule-trade-buy"],
      rule: expect.objectContaining({
        ruleId: "rule-trade-buy",
        ruleVersion: "v3.event.effective.postings.scope.threshold.formula",
        requiresPromotionApproval: true,
        promotionApproval: null,
        formulas: expect.arrayContaining([
          expect.objectContaining({
            formulaId: "formula-source",
            value: 250000,
            currency: "USD",
            description: "Formula amount retained from dry-run 2026-01-01."
          })
        ])
      })
    }));
    expect(result.current.applyFormulaStatusText).toBe("Applied formula amount to rule-trade-buy.");
    expect(result.current.selectedRule?.formulaRows.join("\n")).toContain("formula-source: SourceAmount $250,000 USD - Formula amount retained from dry-run 2026-01-01.");

    await act(async () => {
      await result.current.applyDryRunAllocationTargets();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/allocation/rule-trade-buy"],
      rule: expect.objectContaining({
        ruleId: "rule-trade-buy",
        ruleVersion: "v3.event.effective.postings.scope.threshold.formula.allocation",
        requiresPromotionApproval: true,
        promotionApproval: null,
        allocations: expect.arrayContaining([
          expect.objectContaining({
            allocationRuleId: "alloc-strategy",
            targetDimensions: expect.objectContaining({
              fundId: "fund-alpha",
              sleeveId: "sleeve-core",
              strategyId: "strategy-long-only"
            })
          })
        ])
      })
    }));
    expect(result.current.applyAllocationStatusText).toBe("Applied allocation targets to rule-trade-buy.");
    expect(result.current.selectedRule?.allocationRows.join("\n")).toContain("Fund: fund-alpha");

    await act(async () => {
      await result.current.raiseSelectedRulePriority();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/priority/rule-trade-buy"],
      rule: expect.objectContaining({
        ruleId: "rule-trade-buy",
        ruleVersion: "v3.event.effective.postings.scope.threshold.formula.allocation.priority",
        priority: 11,
        requiresPromotionApproval: true,
        promotionApproval: null
      })
    }));
    expect(result.current.raisePriorityStatusText).toBe("Raised priority for rule-trade-buy.");
    expect(result.current.selectedRule?.priorityLabel).toBe("Priority 11");

    await act(async () => {
      await result.current.duplicateSelectedRule();
    });

    expect(services.upsertRule).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/duplicate/rule-trade-buy"],
      rule: expect.objectContaining({
        displayName: "Trade buy posting draft",
        sourceEventType: "TradeExecuted",
        templateId: "template-trade-buy",
        ruleVersion: "v3.event.effective.postings.scope.threshold.formula.allocation.priority.draft",
        isArchived: false,
        priority: 12,
        requiresPromotionApproval: true,
        promotionApproval: null
      })
    }));
    const duplicateRequest = vi.mocked(services.upsertRule).mock.calls
      .map((call) => call[0])
      .find((request) => request.evidenceLinks?.includes("browser://accounting/rules-studio/duplicate/rule-trade-buy"));
    expect(duplicateRequest).toBeDefined();
    if (!duplicateRequest) {
      throw new Error("Duplicate rule request was not captured.");
    }
    expect(duplicateRequest.ledgerBookId).toBe("book-primary");
    expect(duplicateRequest.rule.ruleId).toMatch(/^rule-trade-buy-draft-\d+$/);
    expect(duplicateRequest.rule.scope).toMatchObject({
      fundId: "fund-alpha",
      entityId: "entity-master",
      externalGlDimensions: {
        class: "FundAlpha"
      }
    });
    expect(duplicateRequest.rule.generatedPostings?.[0].dimensions).toEqual(workspace.postingRules[0].generatedPostings?.[0].dimensions ?? null);
    expect(result.current.selectedRuleId).toBe(duplicateRequest.rule.ruleId);
    expect(result.current.duplicateRuleStatusText).toContain(duplicateRequest.rule.ruleId);

    await act(async () => {
      await result.current.runRuleTests();
    });

    expect(services.runRuleTests).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      testCases: null
    }));
    await waitFor(() => expect(result.current.ruleTestSuite).not.toBeNull());
    expect(result.current.ruleTestSuite).toMatchObject({
      title: "Accounting rule regression tests",
      summaryLabel: "1/1 passed",
      statusTone: "success"
    });
    expect(result.current.selectedRule?.promotionReadiness).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "latest-suite",
        value: "1/1 passed",
        tone: "success"
      }),
      expect.objectContaining({
        id: "activation-gate",
        value: "Blocked",
        detail: "Promotion approval is required before activation.",
        tone: "warning"
      })
    ]));
    expect(result.current.ruleTestSuite?.resultRows.join("\n")).toContain("Pass: Saved trade buy regression selected rule-trade-buy, balanced");

    await act(async () => {
      await result.current.archiveSelectedRule();
    });

    expect(services.upsertRule).toHaveBeenLastCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      actor: "browser-accounting-operator",
      evidenceLinks: [`browser://accounting/rules-studio/archive/${duplicateRequest.rule.ruleId}`],
      rule: expect.objectContaining({
        ruleId: duplicateRequest.rule.ruleId,
        isArchived: true,
        requiresPromotionApproval: true
      })
    }));
    expect(result.current.archiveRuleStatusText).toBe(`Archived posting rule ${duplicateRequest.rule.ruleId}.`);
    expect(result.current.rules.map((rule) => rule.id)).not.toContain(duplicateRequest.rule.ruleId);
    expect(result.current.selectedRuleId).toBe("rule-trade-buy");
  });

  it("blocks activation when promotion-gated rules are not approved or covered by saved tests", async () => {
    const workspace: AccountingConfigurationWorkspace = {
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      status: "Draft",
      configurationVersion: "v4",
      updatedAtUtc: "2026-06-30T12:00:00Z",
      ledgerBooks: [],
      chartOfAccounts: [
        { nodeId: "cash", path: "1000.Cash", accountName: "Cash", accountType: "Asset", parentPath: null, isArchived: false },
        { nodeId: "income", path: "4000.Interest", accountName: "Interest", accountType: "Revenue", parentPath: null, isArchived: false }
      ],
      journalTemplates: [{
        templateId: "template-interest",
        displayName: "Interest accrual",
        description: "Balanced interest accrual.",
        isArchived: false,
        version: "v1",
        lines: [
          { lineId: "debit-cash", accountPath: "1000.Cash", side: "Debit", amount: 100, currency: "USD", description: "Cash" },
          { lineId: "credit-income", accountPath: "4000.Interest", side: "Credit", amount: 100, currency: "USD", description: "Interest" }
        ]
      }],
      postingRules: [{
        ruleId: "rule-interest",
        displayName: "Interest accrual",
        sourceEventType: "InterestAccrual",
        templateId: "template-interest",
        ruleVersion: "v2",
        isArchived: false,
        priority: 10,
        requiresPromotionApproval: true
      }],
      validationIssues: [],
      auditTrail: [],
      ruleTestCases: []
    };
    const services: AccountingConfigurationServices = {
      getConfiguration: vi.fn().mockResolvedValue(workspace),
      assessProductionReadiness: vi.fn().mockResolvedValue(null),
      listMigrationRunArtifacts: vi.fn().mockResolvedValue({ fundProfileId: "fund-alpha", ledgerBookId: "book-primary", artifacts: [] }),
      listMigrationWorkerPlans: vi.fn().mockResolvedValue({ fundProfileId: "fund-alpha", ledgerBookId: "book-primary", kind: null, plans: [] }),
      listExternalGlMappingProfiles: vi.fn().mockResolvedValue([]),
      upsertExternalGlMappingProfile: vi.fn(),
      getProductionCertificationProfile: vi.fn(),
      upsertProductionCertificationProfile: vi.fn(),
      getTenantAdministrationProfile: vi.fn(),
      upsertTenantAdministrationProfile: vi.fn(),
      createLedgerBook: vi.fn(),
      previewTemplate: vi.fn(),
      upsertChartNode: vi.fn().mockResolvedValue(workspace),
      upsertRule: vi.fn(),
      dryRunRule: vi.fn(),
      buildJournalCandidate: vi.fn(),
      runRuleTests: vi.fn(),
      saveRuleTestCase: vi.fn(),
      approveRulePromotion: vi.fn().mockResolvedValue({
        ...workspace,
        postingRules: [{
          ...workspace.postingRules[0],
          promotionApproval: {
            approvalId: "approval-rule-interest",
            requestedBy: "browser-accounting-operator",
            requestedAtUtc: "2026-06-15T10:00:00Z",
            approvalState: "Approved",
            approvedBy: "browser-accounting-operator",
            approvedAtUtc: "2026-06-15T11:00:00Z",
            evidenceLinks: ["browser://accounting/rules-studio/promotion/rule-interest"]
          }
        }]
      }),
      activate: vi.fn()
    };

    const { result } = renderHook(() => useAccountingConfigurationViewModel(services));
    await waitFor(() => expect(result.current.rules).toHaveLength(1));
    expect(result.current.activateDisabledReason).toBe("Approve promotion for 1 required posting rule before activation.");

    await act(async () => {
      await result.current.activate();
    });

    expect(services.activate).not.toHaveBeenCalled();
    expect(result.current.activateDisabledReason).toBe("Approve promotion for 1 required posting rule before activation.");
    expect(result.current.selectedRule?.promotionReadiness).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "promotion-approval",
        value: "Required",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "activation-gate",
        value: "Blocked",
        detail: "Promotion approval is required before activation.",
        tone: "warning"
      })
    ]));

    await act(async () => {
      await result.current.approveRulePromotion();
    });

    expect(services.approveRulePromotion).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-primary",
      ruleId: "rule-interest",
      ruleVersion: "v2",
      actor: "browser-accounting-operator",
      evidenceLinks: ["browser://accounting/rules-studio/promotion-review/rule-interest/v2"],
      requestedBy: "browser-accounting-operator",
      notes: "Approved Interest accrual v2 from Accounting Rules Studio."
    }));
    expect(result.current.approveRulePromotionStatusText).toBe("Approved promotion for Interest accrual.");

    const approvedServices: AccountingConfigurationServices = {
      ...services,
      approveRulePromotion: vi.fn(),
      getConfiguration: vi.fn().mockResolvedValue({
      ...workspace,
      postingRules: [{
        ...workspace.postingRules[0],
        promotionApproval: {
          approvalId: "approval-rule-interest",
          requestedBy: "controller",
          requestedAtUtc: "2026-06-15T10:00:00Z",
          approvalState: "Approved",
          approvedBy: "cfo",
          approvedAtUtc: "2026-06-15T11:00:00Z",
          evidenceLinks: ["evidence://rule-approval"]
        }
      }]
      })
    };

    const approved = renderHook(() => useAccountingConfigurationViewModel(approvedServices));
    await waitFor(() => expect(approved.result.current.rules).toHaveLength(1));
    expect(approved.result.current.activateDisabledReason).toBe("Save regression test cases for 1 promotion-gated posting rule before activation.");

    await act(async () => {
      await approved.result.current.approveRulePromotion();
    });

    expect(approvedServices.approveRulePromotion).not.toHaveBeenCalled();
    expect(approved.result.current.approveRulePromotionStatusText).toBe("Selected posting rule already has an approved promotion.");
    expect(approved.result.current.selectedRule?.promotionReadiness).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "promotion-approval",
        value: "Approved",
        tone: "success"
      }),
      expect.objectContaining({
        id: "saved-regression",
        value: "Missing",
        tone: "warning"
      }),
      expect.objectContaining({
        id: "activation-gate",
        value: "Blocked",
        detail: "A saved regression case is required before activation.",
        tone: "warning"
      })
    ]));

    const noRuleServices: AccountingConfigurationServices = {
      ...services,
      getConfiguration: vi.fn().mockResolvedValue({
        ...workspace,
        postingRules: [],
        rulesStudio: {
          summary: {
            activeRules: 0,
            templateMappingRules: 0,
            generatedPostingRules: 0,
            rulesRequiringPromotionApproval: 0,
            rulesWithApprovedPromotion: 0,
            pendingPromotionApprovalRules: 0,
            savedTestCaseCount: 0,
            rulesWithSavedRegressionTests: 0,
            rulesMissingCurrentVersionRegressionTests: 0
          },
          rules: [],
          promotionQueue: []
        }
      }),
      upsertRule: vi.fn(),
      approveRulePromotion: vi.fn()
    };

    const noRules = renderHook(() => useAccountingConfigurationViewModel(noRuleServices));
    await waitFor(() => expect(noRules.result.current.loading).toBe(false));
    expect(noRules.result.current.rules).toHaveLength(0);

    await act(async () => {
      await noRules.result.current.duplicateSelectedRule();
      await noRules.result.current.archiveSelectedRule();
      await noRules.result.current.approveRulePromotion();
    });

    expect(noRuleServices.upsertRule).not.toHaveBeenCalled();
    expect(noRuleServices.approveRulePromotion).not.toHaveBeenCalled();
    expect(noRules.result.current.duplicateRuleStatusText).toBe("Select an active posting rule before drafting a copy.");
    expect(noRules.result.current.archiveRuleStatusText).toBe("Select an active posting rule before archiving.");
    expect(noRules.result.current.approveRulePromotionStatusText).toBe("Select an active posting rule before approving promotion.");
  });

  it("surfaces missing ledger-book setup as configuration setup readiness", async () => {
    const workspace: AccountingConfigurationWorkspace = {
      fundProfileId: "fund-alpha",
      ledgerBookId: "book-missing",
      status: "Draft",
      configurationVersion: "v4",
      updatedAtUtc: "2026-06-30T12:00:00Z",
      ledgerBooks: [],
      chartOfAccounts: [
        { nodeId: "cash", path: "1000.Cash", accountName: "Cash", accountType: "Asset", parentPath: null, isArchived: false },
        { nodeId: "income", path: "4000.Interest", accountName: "Interest", accountType: "Revenue", parentPath: null, isArchived: false }
      ],
      journalTemplates: [{
        templateId: "template-interest",
        displayName: "Interest accrual",
        description: "Balanced interest accrual.",
        isArchived: false,
        version: "v1",
        lines: [
          { lineId: "debit-cash", accountPath: "1000.Cash", side: "Debit", amount: 100, currency: "USD", description: "Cash" },
          { lineId: "credit-income", accountPath: "4000.Interest", side: "Credit", amount: 100, currency: "USD", description: "Interest" }
        ]
      }],
      postingRules: [{
        ruleId: "rule-interest",
        displayName: "Interest accrual",
        sourceEventType: "InterestAccrual",
        templateId: "template-interest",
        ruleVersion: "v1",
        isArchived: false,
        priority: 10
      }],
      validationIssues: [{
        code: "configuration.ledger-book-missing",
        severity: "Critical",
        message: "Accounting configuration targets ledger book 'book-missing', but no matching ledger book setup was found.",
        targetId: "book-missing",
        suggestedAction: "Create or select the ledger book before activating book-scoped accounting configuration."
      }],
      auditTrail: [],
      ruleTestCases: [],
      ledgerBookSetupCandidate: {
        fundProfileId: "fund-alpha",
        fundStructureNodeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        fundStructureNodeKind: "Fund",
        displayName: "Alpha Fund primary book",
        baseCurrency: "USD",
        accountingBasis: "Primary",
        accountingPolicyId: "legacy-v1",
        accountingPolicyVersion: "legacy-v1",
        suggestedAction: "Create a ledger book using the registered fund-structure scope before activating book-scoped accounting configuration.",
        description: "Created from Accounting Configure setup readiness for requested ledger book book-missing.",
        sourceLedgerBookId: "book-template",
        requestedLedgerBookId: "book-missing"
      }
    };
    const createdBook = {
      ledgerBookId: "book-created",
      fundProfileId: "fund-alpha",
      fundStructureNodeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      fundStructureNodeKind: "Fund",
      displayName: "Alpha Fund primary book",
      baseCurrency: "USD",
      createdAt: "2026-06-30T12:00:00Z",
      updatedAt: "2026-06-30T12:00:00Z",
      description: "Created from Accounting Configure setup readiness for requested ledger book book-missing.",
      accountingBasis: "Primary" as const,
      accountingPolicyId: "legacy-v1",
      accountingPolicyVersion: "legacy-v1"
    };
    const updatedWorkspace: AccountingConfigurationWorkspace = {
      ...workspace,
      ledgerBookId: "book-created",
      ledgerBooks: [createdBook],
      validationIssues: [],
      ledgerBookSetupCandidate: null
    };
    const getConfiguration = vi.fn()
      .mockResolvedValueOnce(workspace)
      .mockResolvedValue(updatedWorkspace);
    const createLedgerBook = vi.fn().mockResolvedValue(createdBook);
    const services: AccountingConfigurationServices = {
      getConfiguration,
      assessProductionReadiness: vi.fn().mockResolvedValue(null),
      listMigrationRunArtifacts: vi.fn().mockResolvedValue({ fundProfileId: "fund-alpha", ledgerBookId: "book-missing", artifacts: [] }),
      listMigrationWorkerPlans: vi.fn().mockResolvedValue({ fundProfileId: "fund-alpha", ledgerBookId: "book-missing", kind: null, plans: [] }),
      listExternalGlMappingProfiles: vi.fn().mockResolvedValue([]),
      upsertExternalGlMappingProfile: vi.fn(),
      getProductionCertificationProfile: vi.fn(),
      upsertProductionCertificationProfile: vi.fn(),
      getTenantAdministrationProfile: vi.fn(),
      upsertTenantAdministrationProfile: vi.fn(),
      createLedgerBook,
      previewTemplate: vi.fn(),
      upsertChartNode: vi.fn().mockResolvedValue(workspace),
      upsertRule: vi.fn(),
      dryRunRule: vi.fn(),
      buildJournalCandidate: vi.fn(),
      runRuleTests: vi.fn(),
      saveRuleTestCase: vi.fn(),
      approveRulePromotion: vi.fn(),
      activate: vi.fn()
    };

    const { result } = renderHook(() => useAccountingConfigurationViewModel(services));

    await waitFor(() => expect(result.current.validationIssues).toHaveLength(1));
    expect(result.current.setupReadinessRows).toEqual([
      expect.objectContaining({
        id: "selected-ledger-book",
        value: "Missing",
        tone: "danger",
        detail: "Accounting configuration targets ledger book 'book-missing', but no matching ledger book setup was found."
      }),
      expect.objectContaining({
        id: "activation-readiness",
        value: "Blocked",
        tone: "danger",
        detail: "Create or select the ledger book before activating book-scoped accounting configuration."
      })
    ]);
    expect(result.current.canCreateLedgerBook).toBe(true);
    expect(result.current.createLedgerBookStatusText).toBe("Create a ledger book using the registered fund-structure scope before activating book-scoped accounting configuration.");

    await act(async () => {
      await result.current.createLedgerBookFromSetupCandidate();
    });

    expect(createLedgerBook).toHaveBeenCalledWith(expect.objectContaining({
      fundProfileId: "fund-alpha",
      fundStructureNodeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      fundStructureNodeKind: "Fund",
      displayName: "Alpha Fund primary book",
      baseCurrency: "USD",
      accountingBasis: "Primary",
      accountingPolicyId: "legacy-v1",
      accountingPolicyVersion: "legacy-v1"
    }));
    await waitFor(() => expect(result.current.setupReadinessRows[0]).toEqual(expect.objectContaining({
      value: "Alpha Fund primary book",
      tone: "success"
    })));
    expect(result.current.createLedgerBookStatusText).toBe("Created Alpha Fund primary book.");
    expect(result.current.setupReadinessRows[1]).toEqual(expect.objectContaining({
      id: "activation-readiness",
      value: "Unavailable",
      tone: "danger",
      detail: "Production readiness could not be verified. Refresh the assessment before activation."
    }));
    expect(result.current.activateDisabledReason).toBe("Refresh production readiness successfully before activation.");
    expect(result.current.canActivate).toBe(false);

    await act(async () => {
      await result.current.activate();
    });
    expect(services.activate).not.toHaveBeenCalled();
  });

});
