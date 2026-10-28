# C#/TypeScript contract parity

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-15

The [checker](../../build/scripts/ci/check-contract-type-parity.py) compares the explicit
[registry](../../build/config/contracts/type-parity-registry.json) on member names, outer
nullability, and collection shape. It enforces 103 pairs and retains 37 review candidates.
This is a structural source check; remaining exemptions are not a count of proven wire-contract
defects.

Run the focused regression suite and export the current findings with:

```bash
python -m unittest discover -s tests/scripts -p test_check_contract_type_parity.py
python build/scripts/ci/check-contract-type-parity.py --json-output artifacts/contract-parity.json
```

The report's `divergence_details` maps every exempt interface to its individual findings. An
exemption with no findings fails the gate until promoted into `pairs`. Keep `problem_count` as
review evidence; it does not suppress additional findings or certify their cause.

## Array parser repair and exemption reassessment

The previous attribute-removal expression also deleted array brackets from C# types. The parser
now removes only leading attribute sections, retaining `string[]`, `string[]?`, nullable element
types, and jagged arrays. Balanced sections also preserve array arguments inside attributes.
Tests cover matching arrays and rejected array/scalar drift through the gate itself.

Re-evaluating all 38 existing exemptions against baseline `93e0325d1` produced 166 findings before
the repair and 163 afterwards. `ProviderSetupResult` had two false collection findings on
`BindingIds` and `Warnings`; it now agrees and has been promoted. The false collection finding on
`ProviderSetupRequest.Capabilities` also disappeared, leaving three nullability differences.
Existing contract declarations were not changed to make this review pass.

The remaining findings need the following distinctions before any contract change:

- **Body:** the parser reads positional parameters, so public properties declared in record
  bodies are invisible. For example, `EvidencePacketExportResponse.VaultIdentity`,
  `OperatorWorkflowHomeSummary.AssuranceScore`, `ReconciliationRunSummary.BankEntityId`, and
  both added properties on `ReconciliationBulkCaseworkResult` exist in C#.
- **Optional:** TS `?` is treated like `| null`, while C# constructor defaults are ignored.
  `CreateLedgerBookRequest` supplies defaults for its three reported members; omitted request
  fields and explicit JSON nulls require separate compatibility checks.
- **Comment:** the leading C# comment on `StrategyRunDetail.GovernanceHooks` obscures its list
  prefix. Its two other reported members are public record-body properties.
- **Declaration:** a remaining member-set or nullability difference is observable in these
  declarations. Confirm the actual endpoint, serializer, request defaults, and any API adapter
  before treating it as a runtime defect. Name-matched declarations alone do not prove that.

All remaining entries stay exempt while those questions are unresolved; no blanket contract
rewrite or new ignore list was introduced. Counts below are parser findings, not defects.

| Original exemption | Before | After | Review evidence / next distinction |
| --- | ---: | ---: | --- |
| ActivateAccountingConfigurationRequest | 2 | 2 | Declaration (`tenantId`); optional `actionOrigin`. |
| ApprovePostingRulePromotionRequest | 2 | 2 | Declaration (`actionOrigin`, `tenantId`). |
| AttachManualJournalEntryEvidenceRequest | 2 | 2 | Optional `actionOrigin`, `periodIsLocked`. |
| CreateLedgerBookRequest | 3 | 3 | Optional members with C# defaults. |
| EvidencePacketExportRequest | 3 | 3 | Body (`Lifecycle`, `Linkage`); optional `includeWarnings`. |
| EvidencePacketExportResponse | 1 | 1 | Body (`VaultIdentity`). |
| LedgerSummary | 9 | 9 | Declaration (scope fields and dimensions). |
| LedgerTrialBalanceLine | 9 | 9 | Declaration (TS accounting/source metadata). |
| OperatorWorkflowHomeSummary | 1 | 1 | Body (`AssuranceScore`). |
| PlaidPublicTokenExchangeResult | 4 | 4 | Declaration (TS institution/link status metadata). |
| PortfolioPositionSummary | 8 | 8 | Declaration (scope fields). |
| PortfolioSummary | 15 | 15 | Declaration (scope and accounting/source fields). |
| PreviewJournalTemplateRequest | 2 | 2 | Declaration (`companyId`, `tenantId`). |
| ProviderSetupRequest | 4 | 3 | Array false positive removed; declaration nullability remains. |
| ProviderSetupResult | 2 | 0 | Array false positives removed; promoted to enforced pairs. |
| QualityDashboardResponse | 8 | 8 | Declaration (quality statistics and stale-symbol fields). |
| ReconciliationBreakQueueItem | 28 | 28 | Declaration and optional fields; endpoint projection needs review. |
| ReconciliationBulkCaseworkRequest | 2 | 2 | Optional `actionOrigin`, `maxCaseCount`. |
| ReconciliationBulkCaseworkResult | 2 | 2 | Body (`InputHashSha256`, `Outcome`). |
| ReconciliationCaseworkCommand | 3 | 3 | Optional `actionOrigin`, `privileged`, `visibility`. |
| ReconciliationRunSummary | 1 | 1 | Body (`BankEntityId`). |
| ResolveConflictRequest | 1 | 1 | Declaration (`chosenWinnerSource`). |
| ResolveReconciliationBreakRequest | 2 | 2 | Declaration (`actionOrigin`); optional `resolvedBy`. |
| ReviewReconciliationBreakRequest | 3 | 3 | Declaration (`team`); optional actor fields. |
| RunAttributionSummary | 1 | 1 | Declaration (`mode`). |
| RunFillSummary | 1 | 1 | Declaration (`mode`). |
| SaveManualJournalEntryDraftRequest | 1 | 1 | Optional `periodIsLocked`, with C# default `false`. |
| StrategyDesignRunBacktestRequest | 6 | 6 | All six reported members are body properties. |
| StrategyRunDetail | 3 | 3 | Comment (`GovernanceHooks`); body (`AcceptanceChecklist`, `EvidenceLoop`). |
| StrategyRunSummary | 12 | 12 | Declaration (execution, governance, fund, and sweep metadata). |
| SubmitManualJournalEntryApprovalRequest | 2 | 2 | Optional `actionOrigin`, `periodIsLocked`. |
| UpsertAccountingRuleTestCaseRequest | 1 | 1 | Declaration (`tenantId`). |
| UpsertChartOfAccountsNodeRequest | 1 | 1 | Declaration (`tenantId`). |
| UpsertJournalEntryTemplateRequest | 2 | 2 | Declaration (`ledgerBookId`, `tenantId`). |
| UpsertPostingRuleRequest | 3 | 3 | Declaration (`companyId`, `reportGroupPrincipalIds`, `tenantId`). |
| ValidateManualJournalEntryDraftRequest | 1 | 1 | Optional `periodIsLocked`, with C# default `false`. |
| VerifiedOperationOutcome | 4 | 4 | Body (`SchemaVersion`, `IsSuccessful`); declaration nullability on two IDs. |
| WorkstationSecurityReference | 11 | 11 | Declaration (security lookup, coverage, and reference fields). |

`ReconciliationBreakQueueItem`, `ResolveReconciliationBreakRequest`, and
`ReviewReconciliationBreakRequest` already had more findings on the review baseline than their
recorded counts (27, 1, and 1). Their counts are refreshed to 28, 2, and 3; that change is unrelated
to the array repair. The raw report retains the individual evidence for subsequent review.
