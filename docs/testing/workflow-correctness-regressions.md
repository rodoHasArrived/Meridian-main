# Workflow correctness regression matrix

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-15

This matrix maps the nine corrections in PR #3168 to their regression coverage. It describes
assertions and reproducible checks; it is not a record of CI results or release acceptance.
Use [Engineering](../engineering/README.md#buildtestrun) for tooling setup and integration gates.

## Invariants and regression sources

| # | Required behavior and exercised boundary | Regression sources |
| --- | --- | --- |
| 1 | Incompatible transaction currencies cannot become exact, tolerance, external-ID, or split matches. CSV intake through `StatementRunWorkflowService.CreateAsync` leaves EUR/USD rows unmatched without FX; a retained EUR/USD rate permits the corresponding base-currency match in both split directions. Assertions inspect breaks and retained match groups. | [Statement workflow policy tests](../../tests/Meridian.Tests/Reconciliation/StatementRunWorkflowServiceTests.MatchingPolicy.cs) |
| 2 | Cash +90, expense 10, capital 100 exports operating −10 and financing +100. Posted journals reach report-pack CSV and scheduled CSV/XLSX exports. Cases cover reversal, line order, exact decimal amounts, offsetting cash legs, cashless journals, transfers, dimension/account isolation, and a reconciliation variance for incomplete scope. | [Cash-flow export tests](../../tests/Meridian.Tests/Ledger/LedgerCashFlowExportTests.cs); [period-statement regressions](../../tests/Meridian.Tests/Ledger/LedgerPeriodStatementsTests.cs) |
| 3 | Actual provider construction preserves the selected credential record. Partial, empty, cleared, deleted, and foreign-scope records cannot borrow ambient fields. Tests inspect outbound credential headers, absence of requests when credentials are incomplete, module/vault precedence, rotation, and the selected Alpaca trading environment. | [Provider construction isolation](../../tests/Meridian.Tests/Infrastructure/Providers/ProviderFactoryCredentialIsolationTests.cs); [composed module and environment isolation](../../tests/Meridian.Tests/Infrastructure/Providers/ProviderFactoryCredentialIsolationTests.Modules.cs) |
| 4 | Payment list, bank-evidence read, approval, and rejection requests carry the selected `entityId`. Request tests inspect fetch URLs, encoded identifiers, methods, bodies, signals, and surfaced failures. Component tests require an entity, discard pending decisions on changes, and ignore prior-scope reads and decision completions. | [Payment API request tests](../../src/Meridian.Ui/dashboard/src/lib/api/banking-payments.api.test.ts); [payment approval component tests](../../src/Meridian.Ui/dashboard/src/screens/accounting-screen.payment-approvals.test.tsx) |
| 5 | Complete tolerance profiles reach the workflow matcher: exact matches precede configured rule order, the first admissible rule is retained, and an empty rule list admits only exact matches. Cases cover cash basis points/date windows, transaction settlement/price conditions, position prices, and both split directions. Real journal projection retains valid execution prices and excludes commission-only audit tags. | [Statement workflow policy tests](../../tests/Meridian.Tests/Reconciliation/StatementRunWorkflowServiceTests.MatchingPolicy.cs); [journal-price projection and workflow tests](../../tests/Meridian.Tests/Reconciliation/LedgerJournalInternalTransactionSourceTests.Price.cs) |
| 6 | Cash Ladder balances, thresholds, scenario state, and drill selections cannot survive as evidence for another account. Deferred responses exercise A→B→A switches, failed account loads, and threshold refresh failures; old responses cannot restore evidence or overwrite the new threshold. | [Cash Ladder scoped-evidence tests](../../src/Meridian.Ui/dashboard/src/screens/cash-ladder-screen.test.tsx) |
| 7 | Live price arrivals reach the freshness monitor independently of storage acceptance; heartbeat and historical events do not refresh it. Composed ingress and controlled timer ticks drive the Prometheus metrics handler through stall, recovery, and market close. Promtool fixtures assert alert firing and clearing, including zero freshness and an empty monitored population. | [Composed freshness workflow](../../tests/Meridian.Tests/Application/Monitoring/LiveFreshnessWorkflowTests.cs); [ingress publisher tests](../../tests/Meridian.Tests/Application/DataQuality/QualityMonitoringPublisherTests.cs); [Prometheus alert fixtures](../../deploy/monitoring/alert-rules.test.yml) |
| 8 | Attribute removal preserves array suffixes and top-level nullability. Parser and checker tests cover attributed, nullable, jagged, and nullable-element arrays, comments, and quoted attribute content; genuine collection drift still fails. Exemption diagnostics remain reviewable, and a repaired pair must move into enforcement. | [Contract-parity checker tests](../../tests/scripts/test_check_contract_type_parity.py); [exemption reassessment](../../build/config/contracts/README.md) |
| 9 | Migration scaffolding emits a repository-relative POSIX path even when generated with Windows path semantics. The CLI regression checks the created SQL file, printed path, updated migration documentation, and subsequent documentation check. External backslash, absolute, traversal, and escaping-link paths remain rejected. | [Schema CLI tests](../../tests/scripts/test_schema_control_cli.py); [reservation/path protection tests](../../tests/scripts/test_schema_control_reservations.py) |

## Reproduce the focused checks

Run from the repository root in Bash after installing the repository's .NET, npm, and Python
dependencies. Run .NET commands sequentially when sharing a checkout. These selections exercise
the test classes and files above; capture the command, commit, result, and any skips separately.

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release \
  /p:EnableWindowsTargeting=true /p:NodeReuse=false \
  --filter 'FullyQualifiedName~StatementRunWorkflowServiceTests|FullyQualifiedName~LedgerJournalInternalTransactionSourceTests|FullyQualifiedName~LedgerCashFlowExportTests|FullyQualifiedName~LedgerPeriodStatementsTests|FullyQualifiedName~ProviderFactoryCredentialIsolationTests|FullyQualifiedName~LiveFreshnessWorkflowTests|FullyQualifiedName~QualityMonitoringPublisherTests'

npm --prefix src/Meridian.Ui/dashboard run test:vitest -- \
  src/lib/api/banking-payments.api.test.ts \
  src/screens/accounting-screen.payment-approvals.test.tsx \
  src/screens/cash-ladder-screen.test.tsx

python3 -m unittest \
  tests/scripts/test_check_contract_type_parity.py \
  tests/scripts/test_schema_control_reservations.py \
  tests/scripts/test_schema_control_cli.py

python3 build/scripts/ci/check-contract-type-parity.py \
  --json-output build/reports/contract-type-parity.json
```

With `promtool` and Docker Compose available, validate the actual alert rules and deployment
configuration. The validator treats missing tools as failures:

```bash
python3 build/scripts/ci/validate-monitoring-deployment.py
```

Focused checks complement the canonical `bash scripts/ci.sh` and required GitHub Actions checks;
they do not replace those integration gates.

## Interpretation limits

- **Price evidence:** prices are compared only when both sides retain them. Missing prices keep
  the existing optional-evidence behavior; net cash, which can include fees, is not substituted
  for execution price. See the [reconciliation module](../../src/Meridian.FinancialOperations/README.md).
- **Cash-flow classification:** categories derive from signed counterparts in a cash-bearing
  journal. Unrelated cashless subtransactions bundled into that journal have no separate
  settlement linkage. Incomplete report scope retains its reconciliation variance.
- **Transport evidence:** provider construction and payment request tests use local stores and
  mocked HTTP/fetch responses. They establish credential and request isolation, not live broker
  authorization. Deferred UI tests establish rendering behavior under controlled response order.
- **Freshness:** monitoring measures arrival age per symbol, not exchange-event latency or
  independent provider availability. Market-hours tests cover the existing weekday/UTC window;
  exchange holidays and daylight-saving calendars are outside that policy. See
  [freshness objectives](../operators/service-level-objectives.md#slo-df-001).
- **Parity:** the 2026-10-15 reassessment records **103 enforced pairs and 37 retained exemptions**
  after reevaluating all 38 original exemptions. These are declaration-checker findings, not 37
  proven contract defects. Positional-only parsing, omission/default semantics, and serialization
  require further review as described in the [registry assessment](../../build/config/contracts/README.md).
- **Windows:** the CLI test injects `PureWindowsPath` for generated paths while filesystem
  operations remain native to the test host. This simulation does not establish native Windows
  execution; run the same Python suites on Windows and retain that result separately.
