# Production-Certification Evidence Chain (PRD-000, PRD-013–PRD-017)

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-08-04
**Scope:** the six evidence-gated P0 rows in the
[production-readiness tracker](../product/implementation-todo-list.md) — what evidence exists,
what an agent can still generate, and exactly which decisions and activations require a human.

The release-gate snapshot shows 12 of 18 P0 rows implementation-complete, 6 evidence-gated, and
0 production-certified. This ledger is the working surface for closing the 6: it records hosted
evidence as it is minted (run links, artifacts, dates), keeps the remaining human actions
explicit, and must be refreshed whenever a hosted run creates or invalidates evidence. It does
not change the tracker's release-gate semantics; the tracker stays authoritative for row state.

## Ledger

| Row | Evidence gate (tracker) | Automation state | Hosted evidence to date | Remaining human action |
| --- | --- | --- | --- | --- |
| `PRD-000` | Core-team approval of ADR-019/ADR-020; clean installed publish/start/update/rollback receipts from the release commit | ADR-019 and ADR-020 are complete and implementation-linked; posture guard, lifecycle control plane, and receipts have focused proof | None — both ADRs remain **Proposed** | **Sign-off decision** (see [PRD-000 approval package](#prd-000-adr-019adr-020-approval-package)) |
| `PRD-013` | Successful `web-workstation`/`win-x64` hosted `Publish Smoke` run attached to the release commit | Workflow starts the exact published artifact with required auth and dedicated PostgreSQL, then fetches startup/health/shell/assets. Eight hosted attempts drove out the full defect chain (publish contract, PostgreSQL discovery, elevated initdb ACLs, exit mutex affinity, unbounded pg_ctl pipe drain, first-boot budgets, lifecycle-bridge DI); every fix is receipt-evidenced | **First green run exists:** [run #15 / 30347353295](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30347353295) on candidate `d64506650` (2026-07-28) — publish, install, supervised dedicated-database startup, and all endpoint probes passed | Re-dispatch on the frozen release commit and keep that run link here |
| `PRD-014` | Protected signing secret, native Windows ARM64 runner, prior release artifact, green tag workflow | Installer release creates SHA-256 checksums, SPDX SBOMs, GitHub attestations; tag release blocks on N-1 install/launch/update/repair/rollback/uninstall receipts for x64 and ARM64 | None — no release tag has run the hardened workflow | **Provision secrets + ARM64 runner, then tag** (see [PRD-014 activation](#prd-014-signing-secret-and-arm64-runner-activation)) |
| `PRD-015` | Dated `production-recovery-drill-*` artifact on the release commit plus operator replay/reconciliation review | `invoke-production-recovery.ps1` drill is exercised by the `Production Certification` recovery job; the connection-string parsing defect that failed every earlier drill was fixed in `ff620a73e` (after run #4) | Green candidate drill receipts now exist, including [first all-green certification run #19 / 30967937407](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30967937407) on `b3bd557876523b81a2791f4bfd814647035f389a` | **Operator review** of the frozen-release drill receipt (see [PRD-015 review](#prd-015-recovery-drill-operator-review)) |
| `PRD-016` | Green hosted `Production Certification` run on the release commit; repository administrator activates it as a required release check | The workflow runs deterministic PostgreSQL integrations with Cobertura coverage, a zero-skip TRX gate, post-test schema capture, and NuGet/npm scans. Candidate `b3bd557876523b81a2791f4bfd814647035f389a` completed the ordered harness repair without narrowing either integration filter | **First all-green candidate run:** [#19 / 30967937407](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30967937407) on 2026-08-05 — `Meridian.Tests` 781/0/0, Direct Lending 7/0/0, zero-skip aggregate 788/0/0 with `certifiable: true`, and all four jobs green. The successful post-test schema capture produced header-only table and migration-ledger inventories after teardown, proving cleanup rather than substantive migrated-schema coverage | Re-dispatch on the frozen release commit, then **required-check activation** by a repository administrator (see [PRD-016 activation](#prd-016-required-check-activation)) |
| `PRD-017` | Local docs/hash validators and the hosted documentation evidence job green on the final candidate commit | `run-docs-automation.py --profile core` plus drift rejection runs inside `Production Certification` | The same-commit documentation job passed in [run #19 / 30967937407](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30967937407) on candidate `b3bd557876523b81a2791f4bfd814647035f389a` | None beyond keeping the frozen release commit drift-free (agent-runnable; commands below) |

## Hosted-Run Diagnosis: Production Certification run #4 (2026-07-27)

Run [30283696811](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30283696811)
(`workflow_dispatch`, `main` @ `104171091`) failed all four jobs. Per-job root causes and their
dispositions:

| Job | Root cause in run #4 | Disposition |
| --- | --- | --- |
| deterministic PostgreSQL integration and coverage evidence | 541 failed / 218 passed / 759 total in 10s — the main-wide PostgreSQL test-harness debt (shared-fixture poisoning, destructive tests, reference-equality assertions) documented in the [2026-07-27 audit §2](production-readiness-audit-2026-07-27.md). Failure modes are heterogeneous (missing relations, FK violations, NREs, assertion failures), not one root cause. | **Remediated on candidate `b3bd557876523b81a2791f4bfd814647035f389a`.** [Run 30967937407](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30967937407) first proved both unchanged integration filters and the zero-skip gate green. The 541/218/759 result remains the historical baseline. |
| NuGet and npm dependency evidence | `npm audit --audit-level=high` exits non-zero while the risk-accepted `react-router` advisory (GHSA-qwww-vcr4-c8h2, no patched 7.x, unreachable server-side path) exists — the job could never pass as written, which would also have hidden any new advisory behind the expected failure. | **Fixed in this candidate.** Advisories now gate through `build/scripts/ci/validate-npm-audit.py` against the reviewed register `build/config/security/npm-audit-accepted-advisories.json` (mirroring `KV-2026-002` in [known-vulnerabilities](../security/known-vulnerabilities.md)). Fail-closed: unlisted, expired, or ceiling-exceeding advisories and missing/error audit reports all fail; stale acceptances fail until pruned. |
| encrypted backup and clean restore drill | `invoke-production-recovery.ps1` connection-string parsing defect (`DbConnectionStringBuilder` property assignment adapted as a dictionary write) — every backup/restore/drill failed at preflight. | **Fixed on `main`** in `ff620a73e` (after run #4); `ProductionRecoveryScriptTests` proves the parse end-to-end. Candidate run #5 then surfaced the next defect in the chain: the runner image ships PostgreSQL 16 client tools and `pg_dump` 16 refuses to dump the `postgres:17` service. **Fixed in this candidate** by installing `postgresql-client-17` (PATH-pinned) in both PostgreSQL-service jobs — the same skew would have voided the integrations job's schema-evidence capture once its tests pass. |
| same-commit documentation evidence | Generated-doc drift on `main` @ `104171091` (`ai-inventory-report.json`, `doc-health-dashboard.md` stale against sources). | **Fixed on `main`** by the subsequent regeneration commits; the core profile is drift-free on this candidate. |

Earlier context: runs #1 (2026-07-21), #2 (scheduled, 2026-07-26), and #3 (2026-07-27, branch)
also failed; run #4 remains the authoritative historical baseline diagnosis, while run 30967937407
is the first all-green candidate evidence.
`Publish Smoke` history: 7 runs; the two green runs
([#6](https://github.com/rodoHasArrived/Meridian-main/actions/runs/29457840313),
[#7](https://github.com/rodoHasArrived/Meridian-main/actions/runs/29458571835), 2026-07-15)
published `collector`/`win-x64`, so the `web-workstation`/`win-x64` evidence PRD-013 requires has
never been minted.

## Human Action Register

Everything in this section requires a decision or privilege an agent does not hold. Each item
names the actor, the exact steps, and where to record the outcome.

### PRD-000: ADR-019/ADR-020 approval package

**Actor:** core-team.
**Decision:** approve the v1 production envelope — the single-operator, single-company,
single-node local workstation (Windows 11 x64, .NET 10, installer lane, loopback browser
workstation plus WPF shell, `AuthenticationMode.Required`, local WAL/atomic stores with
supervisor-managed dedicated local PostgreSQL) — and its lifecycle control plane.

Approving [ADR-019](../adr/019-production-support-matrix-and-deployment-posture.md) ratifies:

1. The support matrix in §1, including that container (`deploy/docker/`, `deploy/k8s/`),
   systemd, remote/browser-hosted `ProductionApi`, and multi-node topologies remain
   **experimental, fail-closed** material outside the v1 envelope.
2. The recorded scope amendment in §1: the `PRD-000` completion-evidence item "a
   `ProductionApi` integration test that starts successfully" is re-scoped to "the supported
   local-workstation posture composes and starts; every experimental posture fails closed with
   a diagnostic naming the prohibited bindings".
3. The enforcement seam: one typed `MeridianDeploymentPosture`, one
   `ProductionServiceRegistrationPolicy`, and the final-graph
   `ProductionRegistrationGuardService` validating the complete collection at host start.

Approving [ADR-020](../adr/020-lifecycle-control-plane.md) ratifies the per-user supervisor /
host process split, dedicated-PostgreSQL ownership rules, readiness contract, deterministic
drain/flush ordering, and lifecycle receipts.

**To record sign-off (all three, same change):**

1. Flip the **Status** line of both ADRs to `Accepted (core-team sign-off YYYY-MM-DD)`.
2. Append a decision to `docs/roadmap/data/decision-log.yml` (adjust date and wording only):

   ```yaml
   - id: DEC-PRD000-SUPPORT-MATRIX-001
     title: Ratify ADR-019/ADR-020 v1 local-workstation production envelope
     status: accepted
     decided_on: YYYY-MM-DD
     owner_lane: Runtime Host and Architecture
     summary: Core-team sign-off on the ADR-019 support matrix and typed deployment posture and the ADR-020 lifecycle control plane. This is the PRD-000 signed support matrix artifact; container, systemd, remote ProductionApi, and multi-node topologies stay experimental and fail closed. The PRD-000 completion-evidence amendment recorded in ADR-019 §1 is ratified.
     related_roadmap_items:
       - W7-LIVE-001
   ```

3. Update the `PRD-000` rows in the
   [tracker](../product/implementation-todo-list.md) and this ledger: the sign-off half of the
   gate closes; the clean installed publish/start/update/rollback receipts from the release
   commit remain.

### PRD-014: signing secret and ARM64 runner activation

**Actor:** repository administrator (secrets, runners) plus release engineering (tag).

1. Create the protected signing secrets the installer workflow consumes
   (`.github/workflows/desktop-installer-packaging.yml`): `MDC_SIGNING_CERT_PFX_BASE64`
   (base64 PFX) and `MDC_SIGNING_CERT_PASSWORD`. The workflow only releases the password on
   `refs/tags/v*`; store both as repository or environment secrets protected by a tag ruleset
   so only release tags can read them.
2. ~~Register a native Windows ARM64 self-hosted runner.~~ **No longer required (2026-08-19).**
   The repository is public, so the GitHub-hosted `windows-11-arm` label is available at no cost;
   the ARM64 certification leg now targets it directly.
3. ~~Ensure the prior (N-1) release artifact is available.~~ **No longer required for the first
   release (2026-08-19).** The lane derives first-release mode from repository state: when no
   published predecessor exists, the update and rollback legs are recorded as `not-applicable`
   with a reason instead of failing closed. A failed release query stays fatal, so this cannot be
   mistaken for a missing predecessor. From the second release onward the N-1 artifact is required
   again, and is produced by the first release.
4. Push the release tag (`v*`) on the frozen release commit and drive
   `desktop-installer-packaging` to green; the tag run's install/launch/update/repair/
   rollback/uninstall receipts for x64 and ARM64 are the PRD-014 evidence. Record the run link
   here.

### PRD-015: recovery-drill operator review

**Actor:** operations owner.

1. After a green `Production Certification` run on the frozen release commit, download the dated
   `production-recovery-drill-<run id>` artifact (drill receipt JSON plus restored-state
   verification) from the run page.
2. Review the receipt's RPO/RTO measurements against the declared objectives and the restored
   business/file state (`recovery_probe` row, credential-vault probe) for replay and
   reconciliation correctness.
3. Record the review as a dated note in this ledger's [evidence log](#evidence-log), naming the
   run id, the reviewer, and the verdict. The gate needs the drill artifact from the **release
   commit**, so repeat on the frozen candidate.

### PRD-016: required-check activation

**Actor:** repository administrator.

1. A green `Production Certification` run on the release commit remains the precondition.
   Candidate `b3bd557876523b81a2791f4bfd814647035f389a` completed the
   [PostgreSQL harness-debt lanes](production-readiness-audit-2026-07-27.md) in the documented
   order, and run 30967937407 first proved all four jobs green without narrowing either integration
   filter. Re-dispatch on the frozen release commit before activation.
2. Then: repository **Settings → Rules → Rulesets** (or classic branch protection on `main`),
   add the workflow's job check runs as required status checks — `deterministic PostgreSQL
   integration and coverage evidence`, `NuGet and npm dependency evidence`, `encrypted backup
   and clean restore drill`, and `same-commit documentation evidence` — for the release
   branch/tag rules.
3. Record the activation (date, ruleset name) in the [evidence log](#evidence-log).

### PRD-013 / PRD-017: agent-runnable, human-verifiable

`Publish Smoke` (`project=web-workstation`, `runtime=win-x64`) and the documentation evidence
lane can be dispatched and kept green by an agent; they appear here because their **final**
evidence must be attached to the frozen release commit. Local pre-flight for PRD-017:

```bash
python3 build/scripts/docs/run-docs-automation.py --profile core
git diff --exit-code
bash scripts/ci.sh --lane verify-docs
```

## Evidence Log

Append-only; newest first. Every entry names the commit, the run or decision, and the outcome.

- **2026-08-19** — `Production Certification` **GREEN again after a three-run regression on `main`**
  ([run #27 / 32266755834](https://github.com/rodoHasArrived/Meridian-main/actions/runs/32266755834),
  candidate `25f73ed4`): all four jobs passed. This closes the regression that had failed
  [#23 / 31293316917](https://github.com/rodoHasArrived/Meridian-main/actions/runs/31293316917) (2026-08-09),
  [#24 / 31482487822](https://github.com/rodoHasArrived/Meridian-main/actions/runs/31482487822) (2026-08-11) and
  [#25 / 31924550471](https://github.com/rodoHasArrived/Meridian-main/actions/runs/31924550471) (2026-08-16) —
  none of which this ledger had recorded, in violation of its own Update Discipline.
  Baseline [#26 / 32264288888](https://github.com/rodoHasArrived/Meridian-main/actions/runs/32264288888)
  on `e18c7fb2` isolated it: integrations, the recovery drill and documentation were already green;
  only `dependency-evidence` was red. Cause was entirely upstream — two advisories published after
  the 2026-08-05 green run, against an unchanged dependency graph: SSH.NET 2025.1.0 via
  Testcontainers (GHSA-q939-rpr3-3284, fixed in 2026.0.0 per OSV) and nanoid 3.3.16 via postcss
  (GHSA-2v37-7h3g-55p8, fixed in 3.3.18). The KV-2026-002 react-router acceptance was retired
  rather than renewed, because 7.18.2 now exists and `validate-npm-audit.py` fails closed on an
  acceptance that no longer matches a reported advisory. The two dependency gates also ran
  fail-fast in sequence, so one ecosystem's advisories masked the other's and the evidence artifact
  was uploaded incomplete; both are now scored and asserted together.
- **2026-08-19** — `Publish Smoke` `web-workstation`/`win-x64` green on current `main` content
  ([run #17 / 32264273659](https://github.com/rodoHasArrived/Meridian-main/actions/runs/32264273659),
  `e18c7fb2`): publish, installed-startup preparation, and start-with-required-authentication all
  passed. The earlier green (#15, `d64506650`) sat on an abandoned branch commit that is not an
  ancestor of `main`, and #16 — the only green ever dispatched on the `main` ref — was `collector`,
  with both installed-startup steps skipped. The lane now names
  `web-workstation-installed-startup` in its evidence manifest only when those steps actually ran,
  so a `collector` or `win-arm64` dispatch can no longer be mistaken for `PRD-013` evidence, and
  release evidence is retained 90 days instead of 14.
- **2026-08-19** — `Desktop Installer Release` **first green packaging and first installed
  lifecycle certification in the repository's history**
  ([run #11 / 32272257476](https://github.com/rodoHasArrived/Meridian-main/actions/runs/32272257476),
  `963c0288`). Six of seven jobs passed: preflight, `package-desktop-msix` for both `win-x64` and
  `win-arm64` (MSIX, SPDX SBOM, evidence manifest and SHA-256 checksums produced for the first
  time), and `clean install lifecycle` for both architectures. The ARM64 leg ran on the
  GitHub-hosted `windows-11-arm` runner (job 96136145806, `labels: ["windows-11-arm"]`), which is
  the direct evidence that the self-hosted-runner action in this ledger is obsolete. Both
  lifecycle legs ran in first-release mode against a throwaway self-signed certificate, so they
  prove the mechanism, **not** release evidence — `PRD-014` still needs a tag run with the
  protected secret.
  The one remaining failure is `package-consumer-setup`: `Meridian.Setup` embeds the whole runtime
  and PostgreSQL payload as a single assembly resource, and Roslyn's PE writer throws
  `ArgumentOutOfRangeException (Parameter 'mappedFieldDataStreamRva')` emitting it. That is a
  payload-delivery design defect rather than a packaging-script bug, and it blocks only the
  one-click `Meridian-Setup.exe`, not the desktop MSIX.
- **2026-08-19** — `Desktop Installer Release` diagnosed end to end; **still not green**. The
  preflight gate that had blocked every earlier run is green on `e18c7fb2`
  ([32263751166](https://github.com/rodoHasArrived/Meridian-main/actions/runs/32263751166)) — the two
  WPF tests were fixed on `main` by `b3d51086`. That exposed the defects behind it, none of which
  had ever executed: `dotnet publish` failed with its error written to a diagnostic log no job
  uploaded (now printed and uploaded); crossgen2 aborts ReadyToRun with NETSDK1096 on Sharpino.Lib
  (ReadyToRun disabled for the package); `package-consumer-setup` was never given the PostgreSQL
  payload it requires and checked no `dotnet publish` exit codes; the artifact-collection step
  could not parse; the lifecycle gate launched a non-existent executable name; the ARM64 leg
  targeted a runner that does not exist; and the N-1 lookup made the first release impossible.
  See the tracker's `PRD-014` row for what remains.

- **2026-08-05** — `Production Certification` **FIRST ALL-GREEN RUN**
  ([run #19 / 30967937407](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30967937407),
  candidate `b3bd557876523b81a2791f4bfd814647035f389a`): all four jobs passed.
  The unchanged filters produced `Meridian.Tests` **781 passed / 0 failed / 0 skipped** and
  `Meridian.DirectLending.Tests` **7 passed / 0 failed / 0 skipped**; the zero-skip validator
  reported **788 passed / 0 failed / 0 skipped** with `certifiable: true`. Dependency evidence,
  the encrypted recovery drill, and same-commit documentation were also green. Post-test schema
  capture completed successfully, but deterministic teardown left the table and migration-ledger
  inventories header-only; this is cleanup evidence, not substantive migrated-schema coverage.
  The frozen-release rerun, operator review, and repository-administrator activation remain open.
- **2026-07-28** — `Publish Smoke` `web-workstation`/`win-x64` **FIRST GREEN RUN**
  ([run #15 / 30347353295](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30347353295),
  follow-up candidate `d64506650`): the all-fixes head passed end to end — dashboard built
  and verified inside the publish, artifact installed from its own bundle, lifecycle
  supervisor started, dedicated PostgreSQL initialized and served, the Meridian host
  reached exact Ready, and the smoke fetched `startupz`, `healthz`, `/workstation/`, and a
  referenced asset with required authentication before a clean shutdown. This is the first
  successful `web-workstation` publish-smoke evidence in the repository's history; the
  `PRD-013` gate now needs only the same run repeated on the frozen release commit.
  Supporting proof: Targeted Test runs 86–88 (windows-latest) green across the supervisor
  ACL, runtime, and pipe-drain regression suites.
- **2026-07-28** — `Publish Smoke` `web-workstation`/`win-x64` sixth attempt
  ([30346070925](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30346070925),
  follow-up candidate `33f08690e`): **the pipe-drain fix is proven — the Meridian host was
  spawned for the first time in any hosted run**, and the supervisor fail-fasted in 16s
  with a receipt naming the real defect: the host exited with code 1, and the host's own
  log (also a first — `_logs/meridian-*.log` now exists in the artifact) shows
  `LifecycleSupervisorBridgeHostedService` failing DI activation because it demanded
  `Serilog.ILogger`, which the workstation graph does not register. The bridge only
  registers when the supervisor pipe is present, which is why no dev run ever constructed
  it. Fixed on the candidate by moving the bridge to the composition's
  `ILogger<T>`/`LogWarning` idiom; a sweep confirmed it was the only DI-activated hosted
  service with a bare Serilog dependency. Automated review also drove two budget-contract
  fixes: the installed readiness ceiling now covers first boot (300s), and the smoke's
  outer budget is derived from every configured deadline on the valid cold-start path
  (initdb 60 + pg_ctl 65 + drain grace 5 + readiness 300 + launcher overhead 30 = 460).
- **2026-07-28** — review follow-up on the follow-up candidate: with the pipe-drain fix in
  place, startup can now actually reach `WaitForReadinessAsync`, whose deadline comes from
  the installed manifest's `startupTimeoutSeconds = 60` — too tight for a first boot that
  self-extracts the compressed single-file host and runs first migrations, and the exact
  wall a 300s outer probe cannot help with (flagged by automated review on PR #2540). The
  installer-written lifecycle manifest now sets a 300s first-boot readiness ceiling; the
  deadline remains hard and fail-closed.
- **2026-07-28** — `Publish Smoke` `web-workstation`/`win-x64` fifth attempt
  ([30344404735](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30344404735),
  follow-up candidate `b6ff1da2b`): the supervisor exit crash is gone (empty stderr — the
  mutex guard held) and the database again ran perfectly for the whole session, but the
  host never bound in 300s. Cross-referencing the receipt timing (blocked-at-stop at
  probe-budget+3s in both run 11 and run 12) against the supervisor source pinned the real
  defect: `RunToolAsync` bounds the tool's exit wait but then awaits a full stdout/stderr
  drain — and `pg_ctl start` hands its redirected pipe write-handles to the `postgres.exe`
  daemon it spawns, so the drain blocks for the database's entire lifetime. Startup froze
  before the host was ever spawned, no deadline could fire, and only the stop request's
  cancellation released the await. Fixed on the candidate with a grace-bounded drain
  (failing tools have no surviving child, so their pipes close and full diagnostics still
  arrive), plus a grandchild-holds-pipe regression test.
- **2026-07-28** — `Publish Smoke` `web-workstation`/`win-x64` fourth attempt
  ([30343467051](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30343467051),
  follow-up candidate `fdebfe711`): **the elevated-initdb ACE fix is proven** — the
  dedicated PostgreSQL initialized, started on its manifest port in ~2s, served for the
  whole session, and shut down cleanly (`postgresql.log` retained; receipt
  `databaseOutcome: Succeeded`). The 90s `startupz` budget then expired while host startup
  was legitimately still in progress (no host logs yet — the compressed single-file host
  self-extracts and runs first migrations before Kestrel binds), and the receipt recorded
  `SucceededWithWarnings: startup blocked by a requested stop`. The stop also exposed a
  real supervisor exit defect: `Mutex.ReleaseMutex` throws `ApplicationException` from the
  post-await finally (thread affinity), turning clean shutdown into an unhandled exception.
  Fixed on the candidate: guarded release (process exit plus the existing
  `AbandonedMutexException` ownership path make release semantically safe) and a 300s
  first-boot probe budget (probes still exit early on success).
- **2026-07-28** — `Publish Smoke` `web-workstation`/`win-x64` third attempt
  ([30339833870](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30339833870),
  merged content `7d6ccd1ff`): the bundle-carrying publish, the artifact-bundle install, and
  the supervisor launch all succeeded; the run reached the final `startupz` probe, which
  refused for 90s. The preserved diagnostics (first run with failure-time artifact upload)
  contain the lifecycle receipt that pins the cause: the supervisor session failed in 0.4s
  because `initdb.exe` exited 1 — "could not change permissions of directory
  `.../data.initializing-*`: Permission denied". On an elevated context (CI runner,
  admin-run installer) initdb re-executes with a restricted token that drops the
  Administrators group, so a directory reachable only through group ACEs fails its
  permission fixup. Fixed in the post-merge follow-up candidate:
  `LifecycleSupervisorDatabase` now grants an explicit inheritable current-user ACE on the
  `postgresql` root before initdb (explicit user ACEs survive token restriction), with
  focused ACL tests. Also the installed-startup defect class `PRD-000`'s clean
  publish/start receipts exist to catch — the fix equally covers real elevated installs.
- **2026-07-28** — `Production Certification` run #8
  ([30339835410](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30339835410),
  merged content `7d6ccd1ff`): recovery drill green for the second consecutive run;
  dependency evidence and documentation evidence green for the third consecutive run;
  deterministic-integrations red only at the harness-debt test step with schema evidence
  captured. The 3-of-4-green pattern is stable on the merged content.
- **2026-07-28** — `Production Certification` run #7
  ([30339057032](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30339057032),
  candidate `d10d5c003`): **first-ever green recovery drill** — encrypted backup, hash
  verification, clean restore, restored business/file-state verification, and the dated
  `production-recovery-drill-30339057032` receipt all completed. Dependency evidence and
  documentation evidence green again; only deterministic-integrations remains red on the
  known harness debt, and its schema-evidence capture now succeeds. 3 of 4 certification
  jobs green on the candidate.
- **2026-07-28** — `Publish Smoke` `web-workstation`/`win-x64` second attempt
  ([30339059109](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30339059109),
  candidate `d10d5c003`): the PostgreSQL-discovery fix held and the run advanced to
  installed startup, which exposed the core `PRD-013` gap — the workstation bundle is
  gitignored vite output that neither MSBuild nor `publish.ps1` carried into the published
  artifact, so a clean-checkout artifact cannot serve `/workstation/` and the installer's
  repo-bundle assert fails. Fixed on the candidate: `publish.ps1` now builds the dashboard
  when the bundle is absent and always includes and verifies `wwwroot/workstation` in the
  web-workstation output; the installer prefers the published artifact's own bundle when
  the repo tree is absent; the smoke workflow ships its `install.log`/receipts on failure.
- **2026-07-28** — `Production Certification` run #6
  ([30338821749](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30338821749),
  candidate `ab2a2e936`): with matching client tools the drill's encrypted backup, hash
  verification, and staging all succeeded for the first time; the restore then hit a
  `Set-StrictMode` defect in `invoke-production-recovery.ps1` (`.Count` on the `$null` an
  empty restore root yields). Fixed on the candidate with an array subexpression. The
  first-ever `Publish Smoke` `web-workstation`/`win-x64` run
  ([30338532774](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30338532774))
  published and packaged the workstation host successfully, then failed preparing installed
  startup: the runner's PostgreSQL directory is the major-only name `17`, which the
  workflow's `[version]` sort refuses to parse. Fixed on the candidate with a
  leading-integer sort. Both workflows re-dispatched.
- **2026-07-28** — `Production Certification` run #5
  ([30338531236](https://github.com/rodoHasArrived/Meridian-main/actions/runs/30338531236),
  candidate `be0b5e247`): first run with the acceptance-gated npm audit. The recovery drill
  failed one layer deeper than run #4 — the connection-string parse now succeeds and the
  drill reaches `pg_dump`, which refuses the version skew (client 16 vs `postgres:17`
  service). Fixed on the candidate by installing matching `postgresql-client-17` in both
  PostgreSQL-service jobs; re-dispatched. A first-ever `Publish Smoke`
  `web-workstation`/`win-x64` run was also dispatched on the candidate.
- **2026-07-28** — candidate `claude/production-certification-evidence-aoyrom`: diagnosed all
  four failing `Production Certification` jobs from run #4 (table above); replaced the
  permanently-red raw `npm audit --audit-level=high` gate with the fail-closed accepted-advisory gate
  (`validate-npm-audit.py`, register `KV-2026-002`); verified the recovery-script fix
  `ff620a73e` and the regenerated documentation baseline are on `main`; confirmed no green
  `web-workstation`/`win-x64` publish-smoke evidence exists. Dispatched fresh
  `Production Certification` and `Publish Smoke (web-workstation/win-x64)` runs on the
  candidate; run links and outcomes to be appended when they complete.

## Update Discipline

- Refresh this ledger in the same change whenever a hosted run mints or invalidates evidence,
  an ADR status changes, a secret/runner/ruleset is activated, or a tracker row's evidence
  state moves.
- The tracker's release-gate snapshot remains the authoritative row state; this ledger carries
  the working detail (run ids, artifacts, reviewer names, dates) that the tracker links to.
