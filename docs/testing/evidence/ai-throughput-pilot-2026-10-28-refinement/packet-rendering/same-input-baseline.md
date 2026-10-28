# Task context packet

Update shared guidance under ## Useful Commands

## Focused validation plan (not executed)

Recommendations only; no commands executed. Focused checks do not replace required final checks.

- `python build/scripts/docs/check-ai-inventory.py --summary`: Task-to-proof matrix AI tooling/guidance check
- `python build/scripts/docs/check-codex-skills.py --summary`: Task-to-proof matrix AI tooling/guidance check
- `git diff --check -- .agents/skills/_shared/project-context.md .claude/skills/_shared/project-context.md .codex/skills/_shared/project-context.md`: Unstaged whitespace check
- `git diff --cached --check -- .agents/skills/_shared/project-context.md .claude/skills/_shared/project-context.md .codex/skills/_shared/project-context.md`: Staged whitespace check

Required final checks remain: `bash scripts/ci.sh`, `Meridian CI / quality-gate`, and `Meridian CI / integration-gate`.

Packets provide partial context and never override applicable instructions.

## Scope

- `.agents/skills/_shared/project-context.md`
- `.claude/skills/_shared/project-context.md`
- `.codex/skills/_shared/project-context.md`

## Instructions to read in full

- `.codex/AGENTS.md`
- `.codex/skills/_shared/codex-execution-contract.md`
- `.codex/skills/_shared/project-context.md`
- `AGENTS.md`
- `CLAUDE.md`

## Validation candidates (not executed)

- `python build/scripts/docs/check-ai-navigation-freshness.py --max-age-days 14`
- `python build/scripts/docs/check-ai-inventory.py --summary`
- `python build/scripts/docs/check-codex-skills.py --summary`
- `git diff --check -- .agents/skills/_shared/project-context.md .claude/skills/_shared/project-context.md .codex/skills/_shared/project-context.md`
- `git diff --cached --check -- .agents/skills/_shared/project-context.md .claude/skills/_shared/project-context.md .codex/skills/_shared/project-context.md`

## Warnings

- No matching source-module owner; inspect the nearest README for ownership and proof.
- Excerpts are partial context. Read applicable instructions in full; packets never override them.

## Omitted context

- `src/Meridian.Application/Pipeline/EventPipeline.cs`: outside explicit AI-tooling scope
- `src/Meridian.Contracts`: outside explicit AI-tooling scope
- `src/Meridian.Contracts/Lifecycle`: outside explicit AI-tooling scope
- `src/Meridian.Contracts/Operations`: outside explicit AI-tooling scope
- `src/Meridian.Core/Serialization/MarketDataJsonContext.cs`: outside explicit AI-tooling scope
- `src/Meridian.Launcher/StartupOutcomeReceiptMonitor.cs`: outside explicit AI-tooling scope
- `src/Meridian.Setup/InstallationTransaction.cs`: outside explicit AI-tooling scope
- `src/Meridian.Setup/PayloadPackage.cs`: outside explicit AI-tooling scope
- `src/Meridian/Program.cs`: outside explicit AI-tooling scope
- `src/Meridian.Application/Composition`: outside explicit AI-tooling scope
- `src/Meridian.Application/Pipeline`: outside explicit AI-tooling scope
- `src/Meridian.Contracts`: outside explicit AI-tooling scope
- `src/Meridian.Core/Serialization`: outside explicit AI-tooling scope
- `src/Meridian.Launcher/LauncherCommandPolicy.cs`: outside explicit AI-tooling scope
- `src/Meridian.Launcher/Program.cs`: outside explicit AI-tooling scope
- `src/Meridian.Launcher/StartupOutcomeReceiptMonitor.cs`: outside explicit AI-tooling scope
- `src/Meridian.LifecycleSupervisor/LifecycleStartupOutcome.cs`: outside explicit AI-tooling scope
- `src/Meridian.LifecycleSupervisor/LifecycleSupervisorRuntime.cs`: outside explicit AI-tooling scope
- `src/Meridian.LifecycleSupervisor/Program.cs`: outside explicit AI-tooling scope
- `src/Meridian.Setup/InstallationTransaction.cs`: outside explicit AI-tooling scope
- `src/Meridian.Setup/PayloadPackage.cs`: outside explicit AI-tooling scope
- `src/Meridian.Setup/Program.cs`: outside explicit AI-tooling scope
- `src/Meridian/Program.cs`: outside explicit AI-tooling scope

## Selected source excerpts

### .agents/skills/_shared/project-context.md:110

target file

````
## Useful Commands

```bash
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests -c Release /p:EnableWindowsTargeting=true
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
bash scripts/ci.sh
pwsh ./scripts/dev/desktop-dev.ps1
pwsh ./scripts/dev/run-desktop.ps1 -Fixture
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1
dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080
gh workflow run targeted-test.yml --ref <branch> -f mode=dotnet-filtered -f dotnet_project=tests/Meridian.Tests/Meridian.Tests.csproj -f dotnet_filter="FullyQualifiedName~<TestClassOrMethod>"
python3 build/scripts/ai-repo-updater.py known-errors
```

GNU Make targets are optional convenience wrappers. In Windows shells where `where.exe make` finds
nothing, use the direct `dotnet`, `npm`, `pwsh`, and `python` commands above instead of `make ...`.

Prefer the narrowest validation command that matches the touched files.
For completed PR-ready work, use `bash scripts/ci.sh`; GitHub Actions `Meridian CI / quality-gate`
is the authoritative merge result. Local work may happen on `main` when the user explicitly requests
it or the checkout is intentionally operating there, but PR-ready publishing should flow through a
`codex/<short-task-name>` branch and PR targeting `main` unless GitHub repository rules explicitly
allow the requested protected-branch flow.
When local CPU, memory, disk, dependency restore, or MSBuild lock contention makes validation
unreliable, push the branch and use the GitHub-hosted `Targeted Test` workflow as the remote proof
tool before retrying broad local scripts. Select a whitelisted `mode`; for .NET slices, use
`mode=dotnet-filtered` with a repo-relative test project under `tests/` plus `dotnet_filter`.
After a timed-out generation, build, or test attempt, run
`python build/python/cli/buildctl.py validation-status --summary`, then `dotnet build-server
shutdown`; stop only abandoned repo-owned `dotnet`, `MSBuild`, `testhost`, `csc`, or
`VBCSCompiler` PIDs whose command lines clearly point at this checkout before retrying local
validation.

---

````

### .claude/skills/_shared/project-context.md:110

target file

````
## Useful Commands

```bash
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests -c Release /p:EnableWindowsTargeting=true
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
bash scripts/ci.sh
pwsh ./scripts/dev/desktop-dev.ps1
pwsh ./scripts/dev/run-desktop.ps1 -Fixture
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1
dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080
gh workflow run targeted-test.yml --ref <branch> -f mode=dotnet-filtered -f dotnet_project=tests/Meridian.Tests/Meridian.Tests.csproj -f dotnet_filter="FullyQualifiedName~<TestClassOrMethod>"
python3 build/scripts/ai-repo-updater.py known-errors
```

GNU Make targets are optional convenience wrappers. In Windows shells where `where.exe make` finds
nothing, use the direct `dotnet`, `npm`, `pwsh`, and `python` commands above instead of `make ...`.

Prefer the narrowest validation command that matches the touched files.
For completed PR-ready work, use `bash scripts/ci.sh`; GitHub Actions `Meridian CI / quality-gate`
is the authoritative merge result. Local work may happen on `main` when the user explicitly requests
it or the checkout is intentionally operating there, but PR-ready publishing should flow through a
`codex/<short-task-name>` branch and PR targeting `main` unless GitHub repository rules explicitly
allow the requested protected-branch flow.
When local CPU, memory, disk, dependency restore, or MSBuild lock contention makes validation
unreliable, push the branch and use the GitHub-hosted `Targeted Test` workflow as the remote proof
tool before retrying broad local scripts. Select a whitelisted `mode`; for .NET slices, use
`mode=dotnet-filtered` with a repo-relative test project under `tests/` plus `dotnet_filter`.
After a timed-out generation, build, or test attempt, run
`python build/python/cli/buildctl.py validation-status --summary`, then `dotnet build-server
shutdown`; stop only abandoned repo-owned `dotnet`, `MSBuild`, `testhost`, `csc`, or
`VBCSCompiler` PIDs whose command lines clearly point at this checkout before retrying local
validation.

---

````

### .codex/skills/_shared/project-context.md:111

target file

````
## Useful Commands

```bash
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests -c Release /p:EnableWindowsTargeting=true
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
bash scripts/ci.sh
pwsh ./scripts/dev/desktop-dev.ps1
pwsh ./scripts/dev/run-desktop.ps1 -Fixture
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-wpf-dev.ps1
dotnet run --project src/Meridian/Meridian.csproj -- --mode desktop --http-port 8080
gh workflow run targeted-test.yml --ref <branch> -f mode=dotnet-filtered -f dotnet_project=tests/Meridian.Tests/Meridian.Tests.csproj -f dotnet_filter="FullyQualifiedName~<TestClassOrMethod>"
python3 build/scripts/ai-repo-updater.py known-errors
```

GNU Make targets are optional convenience wrappers. In Windows shells where `where.exe make` finds
nothing, use the direct `dotnet`, `npm`, `pwsh`, and `python` commands above instead of `make ...`.

Prefer the narrowest validation command that matches the touched files.
For completed PR-ready work, use `bash scripts/ci.sh`; GitHub Actions `Meridian CI / quality-gate`
is the authoritative merge result. Local work may happen on `main` when the user explicitly requests
it or the checkout is intentionally operating there, but PR-ready publishing should flow through a
`codex/<short-task-name>` branch and PR targeting `main` unless GitHub repository rules explicitly
allow the requested protected-branch flow.
When local CPU, memory, disk, dependency restore, or MSBuild lock contention makes validation
unreliable, push the branch and use the GitHub-hosted `Targeted Test` workflow as the remote proof
tool before retrying broad local scripts. Select a whitelisted `mode`; for .NET slices, use
`mode=dotnet-filtered` with a repo-relative test project under `tests/` plus `dotnet_filter`.
After a timed-out generation, build, or test attempt, run
`python build/python/cli/buildctl.py validation-status --summary`, then `dotnet build-server
shutdown`; stop only abandoned repo-owned `dotnet`, `MSBuild`, `testhost`, `csc`, or
`VBCSCompiler` PIDs whose command lines clearly point at this checkout before retrying local
validation.

---

````

### docs/ai/tooling/README.md:13

AI tooling command and validation index

````
## Fast Selection

| Need | Start here | Typical proof lane |
| --- | --- | --- |
| Validate Codex memory index, entries, task routing, and stale review dates | `build/scripts/docs/check-codex-memory.py` | `python build/scripts/docs/check-codex-memory.py --summary` |
| Inventory AI surfaces or catch missing indexes | `build/scripts/docs/check-ai-inventory.py` | `python build/scripts/docs/check-ai-inventory.py --summary` |
| Validate Codex skill metadata and docs links | `build/scripts/docs/check-codex-skills.py` | `python build/scripts/docs/check-codex-skills.py --summary` |
| Validate shared handoff/parallel guidance | `build/scripts/docs/check-ai-handoff.py` | `python build/scripts/docs/check-ai-handoff.py --strict` |
| Validate shared policy mirrors | `build/scripts/docs/check-ai-contract-drift.py` | `python build/scripts/docs/check-ai-contract-drift.py --canonical docs/ai/contract-policy.json --mirror docs/ai/copilot/contract-policy.mirror.json --mirror docs/ai/claude/contract-policy.mirror.json` |
| Route a prompt into lane, mode, and validation floor | `build/scripts/docs/prompt-route-linter.py` | `python build/scripts/docs/prompt-route-linter.py --summary` |
| Emit a compact handoff packet from route evidence | `build/scripts/docs/handoff-packet-generator.py` | `python build/scripts/docs/handoff-packet-generator.py --summary --route-json docs/status/prompt-route-lint-report.json` |
| Check handoff packet schema and validation floor | `build/scripts/docs/check-handoff-packet-schema.py`, `build/scripts/docs/check-validation-floor.py`, `build/scripts/docs/check-mode-escalation.py` | Run the matching `docs/status/*` checks after route or handoff changes |
| Refresh repo-navigation artifacts | `build/scripts/docs/generate-ai-navigation.py` | `python build/scripts/docs/generate-ai-navigation.py --json-output docs/ai/generated/repo-navigation.json --markdown-output docs/ai/generated/repo-navigation.md --recent-changes-output docs/ai/generated/recent-changes.md --summary` |
| Validate only AI-doc lifecycle metadata without repo-wide docs noise | `build/scripts/docs/validate-docs-structure.py` | `python build/scripts/docs/validate-docs-structure.py --top-level ai --summary` |
| Run local .NET tests without parallel-output collisions | `build/python/cli/buildctl.py test` | `python build/python/cli/buildctl.py test --project <project> --filter "<filter>" --queue` |
| Run tests on GitHub when local capacity blocks proof | `.github/workflows/targeted-test.yml` | `gh workflow run targeted-test.yml --ref <branch> -f mode=dotnet-filtered -f dotnet_project=<project> -f dotnet_filter="<filter>"` |
| Apply deterministic scoped text edits | `build/scripts/ai/ai-edit-tool.py` | Preview with `plan`, then apply the saved plan |
| Build token-capped orientation packs | `build/scripts/ai/context-budget.py` | `python build/scripts/ai/context-budget.py --task "<task>" --target-file <path> --summary` |
| Assemble hash-bound task context and check freshness | `build/s
````

### .codex/skills/README.md:46

nearest module README

````
## Routing Model

Pick the narrowest lane that answers the request, then hand off only when the next phase is
different work:

| Lane | Skill | Boundary |
| --- | --- | --- |
| Orient | `meridian-repo-navigation` | Route first, name owner files/docs, then exit. |
| Ideate | `meridian-brainstorm` | Generate options and tradeoffs; do not write specs. |
| Plan | `meridian-blueprint` | Turn one selected idea into a code-ready design. |
| Architecture | `meridian-code-architecture` | Check module boundaries, dependency direction, ADR/source-doc alignment, and public seams. |
| Contract governance | `meridian-contract-governance` | Trace DTO, route, provider-interface, and read-model impact across consumers. |
| Accounting posting controls | `meridian-accounting-posting-controls` | Review posting gates, approval, period locks, idempotency, double-entry, and reversal/rebook safeguards. |
| Event accounting architecture | `meridian-event-accounting-architecture` | Design event-based accounting, immutable journals, projection/replay, and evidence-backed posting controls. |
| Ledger projection replay | `meridian-ledger-projection-replay-review` | Review replay ordering, duplicate handling, projection rebuilds, versioning, drift, and report handoff. |
| Codex skill builder | `meridian-codex-skill-builder` | Create or audit Codex skill packages with scripts, evals, profiles, catalogs, and routes. |
| Implement or verify | `meridian-implementation-assurance` | Build or certify work with evidence and docs sync. |
| Review | `meridian-code-review` | Findings first; no implementation unless asked. |
| Docs | `meridian-docs` | Update docs, guidance, and indexes with current repo evidence. |
| Browser workstation | `meridian-browser-workstation` | Implement or review TypeScript/React dashboard changes in `src/Meridian.Ui/dashboard/`. |
| Test | `meridian-test-writer` | Add scenario-first tests in the right project. |
| Provider | `meridian-provider-builder` | Build provider adapters, then use assurance for rollout proof. |
| Archive | `meridian-archive-organizer` | Classify stale material and preserve useful history. |
| Roadmap | `meridian-roadmap-strategist` | Reconcile plans, delivery waves, and target state. |
| Cleanup | `meridian-cleanup` | Preserve behavior while improving maintainability. |
| Simulated user review | `meridian-simulated-user-panel` | Run evidence-led multi-persona critique; label simulation and hold release gates when proof is insufficient. |
| Desktop implementation | `modular-desktop-mvvm` | Implement WPF changes with MVVM, shared seams, tests, and resource guardrails. |
| Desktop composition | `workstation-screen-composition` | Shape new workspaces, tabs, panels, and command surfaces from shared primitives. |
| Component extraction | `shared-component-extraction` | Consolidate repeated controls, templates, commands, view models, and services. |
| Provider workflow | `provider-management-workflow` | Implement secure provider setup, health
````

### docs/ai/ai-known-errors.md:190

route authoritative doc

````
### AI-20260212-wpf-globalusings-missing-directives
- **Area**: build/desktop-ui
- **Symptoms**: WPF project fails to compile with 80 CS0246 errors like "The type or namespace name 'ShortcutInvokedEventArgs' could not be found", "The type or namespace name 'ApiClientService' could not be found". NotificationService fails with CS0535 "does not implement interface member".
- **Root cause**: GlobalUsings.cs was updated (commit d9b43dc) to remove `global using Meridian.Ui.Services.Services;` to avoid namespace conflicts with WPF-specific services. Files that reference types from this namespace now need explicit using directives.
- **Prevention checklist**:
  - [ ] After modifying GlobalUsings.cs, verify all files in the project still compile
  - [ ] When removing global usings, search for all usages of types from that namespace
  - [ ] Add explicit using directives to files that need removed global types
  - [ ] For WPF files: add `using Meridian.Wpf.Services;` for WPF services
  - [ ] For shared types: add `using UiServices = Meridian.Ui.Services.Services;` and type aliases
  - [ ] Handle type ambiguities (e.g., NotificationType) with explicit aliases
  - [ ] Test both local and CI builds after GlobalUsings changes
- **Verification commands**:
  - `dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj`
  - `grep -r "using Meridian.Wpf.Services;" src/Meridian.Wpf/Views/`
  - `grep -r "using UiServices" src/Meridian.Wpf/Services/`
- **Source issue**: https://github.com/rodoHasArrived/Meridian/actions/runs/21959216554/job/63431802067
- **Status**: fixed (commit 5ea62c8)

````

### docs/ai/navigation/README.md:30

route authoritative doc

````
## Routing Order

1. Identify the likely subsystem with the generated repo map or the MCP `find-subsystem` tool.
2. Use `route-task` to map the natural-language request to a high-signal route.
3. Read the authoritative docs for that route.
4. Inspect the first entrypoints and contracts named by the route.
5. Hand off to the relevant specialist skill or agent.

This keeps orientation-first work separate from deeper implementation or tracing.

For host tracing configuration or disconnected pipeline/backfill spans, start with
[Distributed Tracing Operations](../../operators/distributed-tracing.md) and the
[`Tracing` configuration contract](../../reference/appsettings-schema.md#tracing), then inspect
`src/Meridian.Platform/Tracing/`, shared host composition under
`src/Meridian.Application/Composition/`, and the pipeline/backfill owners in the source registry.

For Evidence Vault storage quotas, start with
[`Meridian.Documents`](../../../src/Meridian.Documents/README.md#evidence-storage-quota-configuration):
`EvidenceStorageQuotaCoordinator` owns admission, durable reservations, and recovery leases.
[`FileEvidenceArtifactStore`](../../../src/Meridian.Ui.Shared/Evidence/FileEvidenceArtifactStore.cs)
and its `StorageQuota`/`Export` partials adapt retained usage, staging cleanup, and index-last
publication. Intake/export coverage lives in `FileEvidenceArtifactStoreQuotaTests`; coordinator
concurrency and recovery coverage lives in `EvidenceStorageQuotaCoordinatorTests`.

For PRD-112 pipeline budgets, start with [the bounded benchmark lane](../../engineering/pipeline-benchmark.md).
`build/scripts/ci/benchmark-pipeline.py` records the profile and commit, runs the fixed
`PipelineBudgetBenchmarks` stages, and invokes `build/scripts/validate_budget.py`.
Failure propagation coverage lives in `tests/scripts/test_benchmark_pipeline.py`;
sustained-load soak remains separate.

---

````

### docs/ai/README.md:97

subsystem required doc

````
## AI Edit Contract (Required)

Before changing any AI/helping-agent guidance:

- Read and update `assistant-workflow-contract.md` first when shared behavior changes.
- Keep host-specific edits (`claude/`, `codex/`, `copilot/`, `agents/`, `skills/`) strictly aligned to the shared contract.
- Do not hand-edit generated AI outputs under `docs/ai/generated/*`; re-run generation command lanes.
- Update this index when adding/removing AI surfaces, entrypoints, checks, or ownership.
- For parallel work, initialize a manifest (`parallel-task-manifest-template.md`) and record lane transitions in `agent-handoff-checklist.md`.
- For concurrent implementation, keep a working-memory ledger (`working-memory.md`) for active
  claims, inspected files, assumptions, merge risks, and validation reuse.
- For lane transitions, include required context, optional context, validation reuse, and unresolved assumptions in the handoff packet.
- End every AI surface edit with matching validation outputs and a short residual-risk note.

---

````

### docs/reference/verified-operation-outcomes.md:9

subsystem required doc

````
# Verified Operation Outcomes

`VerifiedOperationOutcome` is Meridian's terminal receipt for admitted operations. Producers use
the shared contract in `src/Meridian.Contracts/Operations`; callers validate the receipt instead of
inferring success from a message, HTTP status, process exit code, filename, or partially updated
domain object.

````

### README.md:243

nearest module README

````
## Planning Source Of Truth

Use these documents together when planning or implementing new work:

- [Roadmap Registry](docs/roadmap/README.md) and `docs/roadmap/data/*.yml` for active wave and gate records.
- [Generated Roadmap Summary](docs/roadmap/generated/ROADMAP_SUMMARY.md) for rendered roadmap state.
- [Design Charter](docs/product/meridian-design-document.md) for the canonical product framing and evidence-backed operations model.
- [Implementation and Readiness Tracker](docs/product/implementation-todo-list.md) for current implementation, evidence, and readiness follow-up.
- [Provider Capability Matrix](docs/reference/provider-capability-matrix.md) and [Provider Validation Matrix](docs/reference/provider-validation-matrix.md) for provider-confidence scope.
- [Engineering Guide](docs/engineering/README.md) for execution architecture and shared-model guidance.
- [Operator Guide](docs/operators/README.md) for governance and support posture.
- [Source Documentation Mesh](docs/source/README.md) and `docs/source/data/source-modules.yml` for source README ownership and module traceability.

<!-- readme-tree start -->

The full repository tree is generated in [docs/generated/repository-structure.md](docs/generated/repository-structure.md). For maintained ownership and dependency boundaries, use [docs/architecture/project-structure.md](docs/architecture/project-structure.md) and [docs/architecture/module-map.md](docs/architecture/module-map.md).

<!-- readme-tree end -->
````

