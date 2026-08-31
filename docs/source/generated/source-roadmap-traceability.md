<!--
generated: true
generator: build/scripts/docs/render-source-docs.py
generator_version: 1.0.0
render_contract: meridian.generated-docs.v1
schema_versions:
  - meridian.source-modules@1.0.0
inputs:
  - docs/source/data/diagram-index.yml
  - docs/source/data/source-modules.yml
  - docs/source/data/source-readme-coverage.yml
  - docs/source/data/source-readme-ignore.yml
  - docs/source/data/source-todos.yml
do_not_edit: true
-->

# Source Roadmap Traceability

| Module | Name | Roadmap item | Roadmap title |
| --- | --- | --- | --- |
| `SRC-APP` | Meridian application layer | `W2-TRD-001` | Paper trading cockpit reliability |
| `SRC-APP` | Meridian application layer | `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `SRC-APP` | Meridian application layer | `W3-CONT-001` | Research to paper continuity |
| `SRC-APP` | Meridian application layer | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-APP` | Meridian application layer | `W9-GOV-008` | Route-level authorization, fail-closed tenancy, and hash-chained accounting audit |
| `SRC-APP` | Meridian application layer | `W10-MARK-001` | Fail-closed stale-mark policy and mark-age surfacing |
| `SRC-BACKTESTING` | Meridian backtesting | `W3-CONT-001` | Research to paper continuity |
| `SRC-BACKTESTING` | Meridian backtesting | `W5-MASSET-001` | Multi-asset operational coverage proof lane |
| `SRC-BACKTESTING` | Meridian backtesting | `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
| `SRC-BACKTESTING` | Meridian backtesting | `W10-PERF-001` | Portfolio and investor return measurement |
| `SRC-BACKTESTING-SDK` | Backtesting SDK | `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
| `SRC-CONTRACTS` | Meridian contracts | `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `SRC-CONTRACTS` | Meridian contracts | `W2-TRD-001` | Paper trading cockpit reliability |
| `SRC-CONTRACTS` | Meridian contracts | `W3-CONT-001` | Research to paper continuity |
| `SRC-CONTRACTS` | Meridian contracts | `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `SRC-CONTRACTS` | Meridian contracts | `W4-RPT-001` | Governed report pack readiness |
| `SRC-CONTRACTS` | Meridian contracts | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-CONTRACTS` | Meridian contracts | `W5X-CONNECT-001` | Custodian and broker statement connector library |
| `SRC-CONTRACTS` | Meridian contracts | `W5X-EVIDENCE-001` | Evidence Vault productization |
| `SRC-CONTRACTS` | Meridian contracts | `W5X-STMT-ONBOARD-001` | Statement reconciliation onboarding wedge |
| `SRC-CONTRACTS` | Meridian contracts | `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
| `SRC-CONTRACTS` | Meridian contracts | `W9-ASSET-010` | Asset Accounting Event Spine and atomic lot posting |
| `SRC-CONTRACTS` | Meridian contracts | `W10-MARK-001` | Fail-closed stale-mark policy and mark-age surfacing |
| `SRC-CONTRACTS` | Meridian contracts | `W10-RECON-001` | Durable break lineage identity and run-over-run break diff |
| `SRC-CONTRACTS` | Meridian contracts | `W10-PROV-001` | Ledger-amount evidence subject and shared proof drawer |
| `SRC-CONTRACTS` | Meridian contracts | `W10-RECON-002` | Break clustering and bulk-resolution activation |
| `SRC-CONTRACTS` | Meridian contracts | `W10-JRNL-001` | Durable recurring journal schedules and draft runner |
| `SRC-CONTRACTS` | Meridian contracts | `W10-TAX-001` | Tax character, wash-sale, and lot-relief operator surface |
| `SRC-CONTRACTS` | Meridian contracts | `W10-SEAM-001` | Unified close-readiness projection behind one shared contract |
| `SRC-CONTRACTS` | Meridian contracts | `W10-RECON-004` | Operator-taught match rules with promotion gate |
| `SRC-CONTRACTS` | Meridian contracts | `W10-PERF-001` | Portfolio and investor return measurement |
| `SRC-CONTRACTS` | Meridian contracts | `W10-CONSOL-001` | Intercompany elimination on consolidated ledger views |
| `SRC-CORE` | Meridian core | `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `SRC-CORE` | Meridian core | `W2-TRD-001` | Paper trading cockpit reliability |
| `SRC-CORE` | Meridian core | `W7-LIVE-001` | Live-readiness governance |
| `SRC-DESIGN-AUDIT` | Meridian Audit design module | `W4-RPT-001` | Governed report pack readiness |
| `SRC-DESIGN-AUDIT` | Meridian Audit design module | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-DESIGN-DATA-INTEGRATION` | Meridian Data Integration design module | `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `SRC-DESIGN-DATA-INTEGRATION` | Meridian Data Integration design module | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-DESIGN-DOCUMENTS` | Meridian Documents design module | `W4-RPT-001` | Governed report pack readiness |
| `SRC-DESIGN-DOCUMENTS` | Meridian Documents design module | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-DESIGN-ENTITIES` | Meridian Entities design module | `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `SRC-DESIGN-ENTITIES` | Meridian Entities design module | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-DESIGN-FINANCIAL-OPERATIONS` | Meridian Financial Operations design module | `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `SRC-DESIGN-FINANCIAL-OPERATIONS` | Meridian Financial Operations design module | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-DESIGN-FINANCIAL-OPERATIONS` | Meridian Financial Operations design module | `W5X-FINOPS-001` | Financial operations control center |
| `SRC-DESIGN-FINANCIAL-OPERATIONS` | Meridian Financial Operations design module | `W5X-CONNECT-001` | Custodian and broker statement connector library |
| `SRC-DESIGN-FINANCIAL-OPERATIONS` | Meridian Financial Operations design module | `W5X-STMT-ONBOARD-001` | Statement reconciliation onboarding wedge |
| `SRC-DESIGN-FINANCIAL-OPERATIONS` | Meridian Financial Operations design module | `W9-ASSET-010` | Asset Accounting Event Spine and atomic lot posting |
| `SRC-DESIGN-FINANCIAL-OPERATIONS` | Meridian Financial Operations design module | `W10-RECON-001` | Durable break lineage identity and run-over-run break diff |
| `SRC-DESIGN-FINANCIAL-OPERATIONS` | Meridian Financial Operations design module | `W10-RECON-002` | Break clustering and bulk-resolution activation |
| `SRC-DESIGN-FINANCIAL-OPERATIONS` | Meridian Financial Operations design module | `W10-JRNL-001` | Durable recurring journal schedules and draft runner |
| `SRC-DESIGN-FINANCIAL-OPERATIONS` | Meridian Financial Operations design module | `W10-SEAM-001` | Unified close-readiness projection behind one shared contract |
| `SRC-DESIGN-FINANCIAL-OPERATIONS` | Meridian Financial Operations design module | `W10-RECON-003` | Unified tolerance model and what-if replay workbench |
| `SRC-DESIGN-FINANCIAL-OPERATIONS` | Meridian Financial Operations design module | `W10-RECON-004` | Operator-taught match rules with promotion gate |
| `SRC-DESIGN-FINANCIAL-OPERATIONS` | Meridian Financial Operations design module | `W10-PERF-001` | Portfolio and investor return measurement |
| `SRC-DESIGN-FINANCIAL-OPERATIONS` | Meridian Financial Operations design module | `W10-CONSOL-001` | Intercompany elimination on consolidated ledger views |
| `SRC-DESIGN-IDENTITY` | Meridian Identity design module | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-DESIGN-INSTRUMENTS` | Meridian Instruments design module | `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `SRC-DESIGN-INSTRUMENTS` | Meridian Instruments design module | `W5-MASSET-001` | Multi-asset operational coverage proof lane |
| `SRC-DESIGN-INSTRUMENTS` | Meridian Instruments design module | `W9-ASSET-010` | Asset Accounting Event Spine and atomic lot posting |
| `SRC-DESIGN-PLATFORM` | Meridian Platform design module | `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `SRC-DESIGN-PLATFORM` | Meridian Platform design module | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-DESIGN-PORTFOLIO-RECORDS` | Meridian Portfolio Records design module | `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `SRC-DESIGN-PORTFOLIO-RECORDS` | Meridian Portfolio Records design module | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-DESIGN-PORTFOLIO-RECORDS` | Meridian Portfolio Records design module | `W5-MASSET-001` | Multi-asset operational coverage proof lane |
| `SRC-DESIGN-REFERENCE-DATA` | Meridian Reference Data design module | `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `SRC-DESIGN-REFERENCE-DATA` | Meridian Reference Data design module | `W5-MASSET-001` | Multi-asset operational coverage proof lane |
| `SRC-DESIGN-REPORTING` | Meridian Reporting design module | `W4-RPT-001` | Governed report pack readiness |
| `SRC-DESIGN-REPORTING` | Meridian Reporting design module | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-DESIGN-WORKFLOW` | Meridian Workflow design module | `W3-CONT-001` | Research to paper continuity |
| `SRC-DESIGN-WORKFLOW` | Meridian Workflow design module | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-DOMAIN` | Meridian domain | `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `SRC-DOMAIN` | Meridian domain | `W2-TRD-001` | Paper trading cockpit reliability |
| `SRC-DOMAIN` | Meridian domain | `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `SRC-DOMAIN` | Meridian domain | `W10-RECON-004` | Operator-taught match rules with promotion gate |
| `SRC-EXECUTION` | Meridian execution | `W2-TRD-001` | Paper trading cockpit reliability |
| `SRC-EXECUTION` | Meridian execution | `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `SRC-EXECUTION` | Meridian execution | `W7-LIVE-001` | Live-readiness governance |
| `SRC-EXECUTION` | Meridian execution | `W9-SAFETY-007` | Kill-switch cancel-all and fat-finger, notional, and collar rules |
| `SRC-EXECUTION-SDK` | Execution SDK | `W2-TRD-001` | Paper trading cockpit reliability |
| `SRC-EXECUTION-SDK` | Execution SDK | `W7-LIVE-001` | Live-readiness governance |
| `SRC-FSHARP` | Meridian FSharp | `W3-CONT-001` | Research to paper continuity |
| `SRC-FSHARP` | Meridian FSharp | `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
| `SRC-FSHARP` | Meridian FSharp | `W10-PERF-001` | Portfolio and investor return measurement |
| `SRC-FSHARP-DIRECTLENDING` | FSharp direct lending aggregates | `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `SRC-FSHARP-DIRECTLENDING` | FSharp direct lending aggregates | `W4-RPT-001` | Governed report pack readiness |
| `SRC-FSHARP-LEDGER` | FSharp ledger | `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `SRC-FSHARP-LEDGER` | FSharp ledger | `W4-RPT-001` | Governed report pack readiness |
| `SRC-FSHARP-TRADING` | FSharp trading | `W2-TRD-001` | Paper trading cockpit reliability |
| `SRC-FSHARP-TRADING` | FSharp trading | `W3-CONT-001` | Research to paper continuity |
| `SRC-HOST` | Meridian host | `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `SRC-HOST` | Meridian host | `W2-TRD-001` | Paper trading cockpit reliability |
| `SRC-HOST` | Meridian host | `W7-LIVE-001` | Live-readiness governance |
| `SRC-IBAPI-SMOKESTUB` | IB API smoke stub | `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `SRC-INFRASTRUCTURE` | Meridian infrastructure | `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `SRC-INFRASTRUCTURE` | Meridian infrastructure | `W2-TRD-001` | Paper trading cockpit reliability |
| `SRC-INFRASTRUCTURE` | Meridian infrastructure | `W7-LIVE-001` | Live-readiness governance |
| `SRC-LEDGER` | Meridian ledger | `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `SRC-LEDGER` | Meridian ledger | `W4-RPT-001` | Governed report pack readiness |
| `SRC-LEDGER` | Meridian ledger | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-LEDGER` | Meridian ledger | `W9-ASSET-010` | Asset Accounting Event Spine and atomic lot posting |
| `SRC-LEDGER` | Meridian ledger | `W10-MARK-001` | Fail-closed stale-mark policy and mark-age surfacing |
| `SRC-LEDGER` | Meridian ledger | `W10-JRNL-001` | Durable recurring journal schedules and draft runner |
| `SRC-LEDGER` | Meridian ledger | `W10-TAX-001` | Tax character, wash-sale, and lot-relief operator surface |
| `SRC-LEDGER` | Meridian ledger | `W10-PERF-001` | Portfolio and investor return measurement |
| `SRC-LEDGER` | Meridian ledger | `W10-CONSOL-001` | Intercompany elimination on consolidated ledger views |
| `SRC-MCP` | Meridian MCP host | `W7-LIVE-001` | Live-readiness governance |
| `SRC-PROVIDER-SDK` | Provider SDK | `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `SRC-PROVIDER-SDK` | Provider SDK | `W7-LIVE-001` | Live-readiness governance |
| `SRC-QUANTSCRIPT` | QuantScript | `W3-CONT-001` | Research to paper continuity |
| `SRC-QUANTSCRIPT` | QuantScript | `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
| `SRC-RISK` | Meridian risk | `W2-TRD-001` | Paper trading cockpit reliability |
| `SRC-RISK` | Meridian risk | `W7-LIVE-001` | Live-readiness governance |
| `SRC-RISK` | Meridian risk | `W9-SAFETY-007` | Kill-switch cancel-all and fat-finger, notional, and collar rules |
| `SRC-STORAGE` | Meridian storage | `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `SRC-STORAGE` | Meridian storage | `W2-TRD-001` | Paper trading cockpit reliability |
| `SRC-STORAGE` | Meridian storage | `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `SRC-STORAGE` | Meridian storage | `W4-RPT-001` | Governed report pack readiness |
| `SRC-STORAGE` | Meridian storage | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-STORAGE` | Meridian storage | `W9-ASSET-010` | Asset Accounting Event Spine and atomic lot posting |
| `SRC-STRATEGIES` | Meridian strategies | `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `SRC-STRATEGIES` | Meridian strategies | `W3-CONT-001` | Research to paper continuity |
| `SRC-STRATEGIES` | Meridian strategies | `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
| `SRC-STRATEGIES` | Meridian strategies | `W7-LIVE-001` | Live-readiness governance |
| `SRC-STRATEGIES` | Meridian strategies | `W10-RECON-001` | Durable break lineage identity and run-over-run break diff |
| `SRC-STRATEGIES` | Meridian strategies | `W10-RECON-002` | Break clustering and bulk-resolution activation |
| `SRC-STRATEGIES` | Meridian strategies | `W10-RECON-004` | Operator-taught match rules with promotion gate |
| `SRC-UI` | Meridian UI asset host | `W2-TRD-001` | Paper trading cockpit reliability |
| `SRC-UI` | Meridian UI asset host | `W3-CONT-001` | Research to paper continuity |
| `SRC-UI-DASHBOARD` | Browser workstation dashboard | `W2-TRD-001` | Paper trading cockpit reliability |
| `SRC-UI-DASHBOARD` | Browser workstation dashboard | `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `SRC-UI-DASHBOARD` | Browser workstation dashboard | `W3-CONT-001` | Research to paper continuity |
| `SRC-UI-DASHBOARD` | Browser workstation dashboard | `W4-RPT-001` | Governed report pack readiness |
| `SRC-UI-DASHBOARD` | Browser workstation dashboard | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-UI-DASHBOARD` | Browser workstation dashboard | `W5-MASSET-001` | Multi-asset operational coverage proof lane |
| `SRC-UI-DASHBOARD` | Browser workstation dashboard | `W5X-CONNECT-001` | Custodian and broker statement connector library |
| `SRC-UI-DASHBOARD` | Browser workstation dashboard | `W5X-EVIDENCE-001` | Evidence Vault productization |
| `SRC-UI-DASHBOARD` | Browser workstation dashboard | `W5X-STMT-ONBOARD-001` | Statement reconciliation onboarding wedge |
| `SRC-UI-DASHBOARD` | Browser workstation dashboard | `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
| `SRC-UI-SERVICES` | UI services | `W2-TRD-001` | Paper trading cockpit reliability |
| `SRC-UI-SERVICES` | UI services | `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `SRC-UI-SERVICES` | UI services | `W3-CONT-001` | Research to paper continuity |
| `SRC-UI-SERVICES` | UI services | `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `SRC-UI-SERVICES` | UI services | `W4-RPT-001` | Governed report pack readiness |
| `SRC-UI-SERVICES` | UI services | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-UI-SHARED` | UI shared contracts | `W2-TRD-001` | Paper trading cockpit reliability |
| `SRC-UI-SHARED` | UI shared contracts | `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `SRC-UI-SHARED` | UI shared contracts | `W4-RPT-001` | Governed report pack readiness |
| `SRC-UI-SHARED` | UI shared contracts | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-UI-SHARED` | UI shared contracts | `W5X-CONNECT-001` | Custodian and broker statement connector library |
| `SRC-UI-SHARED` | UI shared contracts | `W5X-EVIDENCE-001` | Evidence Vault productization |
| `SRC-UI-SHARED` | UI shared contracts | `W5X-STMT-ONBOARD-001` | Statement reconciliation onboarding wedge |
| `SRC-UI-SHARED` | UI shared contracts | `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
| `SRC-UI-SHARED` | UI shared contracts | `W9-ASSET-010` | Asset Accounting Event Spine and atomic lot posting |
| `SRC-UI-SHARED` | UI shared contracts | `W10-MARK-001` | Fail-closed stale-mark policy and mark-age surfacing |
| `SRC-UI-SHARED` | UI shared contracts | `W10-RECON-001` | Durable break lineage identity and run-over-run break diff |
| `SRC-UI-SHARED` | UI shared contracts | `W10-PROV-001` | Ledger-amount evidence subject and shared proof drawer |
| `SRC-UI-SHARED` | UI shared contracts | `W10-RECON-002` | Break clustering and bulk-resolution activation |
| `SRC-UI-SHARED` | UI shared contracts | `W10-JRNL-001` | Durable recurring journal schedules and draft runner |
| `SRC-UI-SHARED` | UI shared contracts | `W10-TAX-001` | Tax character, wash-sale, and lot-relief operator surface |
| `SRC-UI-SHARED` | UI shared contracts | `W10-SEAM-001` | Unified close-readiness projection behind one shared contract |
| `SRC-UI-SHARED` | UI shared contracts | `W10-RECON-003` | Unified tolerance model and what-if replay workbench |
| `SRC-UI-SHARED` | UI shared contracts | `W10-RECON-004` | Operator-taught match rules with promotion gate |
| `SRC-UI-SHARED` | UI shared contracts | `W10-PERF-001` | Portfolio and investor return measurement |
| `SRC-UI-SHARED` | UI shared contracts | `W10-CONSOL-001` | Intercompany elimination on consolidated ledger views |
| `SRC-UI-SHARED` | UI shared contracts | `W9-SAFETY-007` | Kill-switch cancel-all and fat-finger, notional, and collar rules |
| `SRC-WPF` | WPF workstation | `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `SRC-WPF` | WPF workstation | `W4-RPT-001` | Governed report pack readiness |
| `SRC-WPF` | WPF workstation | `W5-ACCT-001` | Accounting records and operational evidence |
| `SRC-WPF` | WPF workstation | `W5-MASSET-001` | Multi-asset operational coverage proof lane |
| `SRC-WPF` | WPF workstation | `W8-WPF-PARITY-001` | WPF desktop workstation reactivation and web-UI parity |
| `SRC-WPF` | WPF workstation | `W10-MARK-001` | Fail-closed stale-mark policy and mark-age surfacing |
| `SRC-WPF` | WPF workstation | `W10-RECON-001` | Durable break lineage identity and run-over-run break diff |
| `SRC-WPF` | WPF workstation | `W10-PROV-001` | Ledger-amount evidence subject and shared proof drawer |
| `SRC-WPF` | WPF workstation | `W10-RECON-002` | Break clustering and bulk-resolution activation |
| `SRC-WPF` | WPF workstation | `W10-JRNL-001` | Durable recurring journal schedules and draft runner |
| `SRC-WPF` | WPF workstation | `W10-TAX-001` | Tax character, wash-sale, and lot-relief operator surface |
| `SRC-WPF` | WPF workstation | `W10-SEAM-001` | Unified close-readiness projection behind one shared contract |
| `SRC-WPF` | WPF workstation | `W10-RECON-003` | Unified tolerance model and what-if replay workbench |
| `SRC-WPF` | WPF workstation | `W10-RECON-004` | Operator-taught match rules with promotion gate |
| `SRC-WPF` | WPF workstation | `W10-PERF-001` | Portfolio and investor return measurement |
| `SRC-WPF` | WPF workstation | `W10-CONSOL-001` | Intercompany elimination on consolidated ledger views |
