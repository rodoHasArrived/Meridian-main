# Task context packet

Insert Pilot handoffs identify unresolved assumptions before another agent resumes work. immediately after ## Useful Commands with a blank line before and after, then synchronize the shared context to both host mirrors without changing other bytes.

Packets provide partial context and never override applicable instructions.

## Scope

- `.agents/skills/_shared/project-context.md`
- `.claude/skills/_shared/project-context.md`
- `.codex/skills/_shared/project-context.md`

## Instructions to read in full

- `.codex/skills/_shared/project-context.md`
- `AGENTS.md`

## Validation candidates (not executed)

- `python build/scripts/docs/check-ai-navigation-freshness.py --max-age-days 14`

## Warnings

- No matching source-module owner; inspect the nearest README for ownership and proof.
- Excerpts are partial context. Read applicable instructions in full; packets never override them.

## Omitted context

- `src/Meridian.Application/Pipeline/EventPipeline.cs`: source missing
- `src/Meridian.Contracts`: source missing
- `src/Meridian.Contracts/Lifecycle`: source missing
- `src/Meridian.Contracts/Operations`: source missing
- `src/Meridian.Core/Serialization/MarketDataJsonContext.cs`: source missing
- `src/Meridian.Launcher/StartupOutcomeReceiptMonitor.cs`: source missing
- `src/Meridian.Setup/InstallationTransaction.cs`: source missing
- `src/Meridian.Setup/PayloadPackage.cs`: source missing
- `src/Meridian/Program.cs`: source missing
- `docs/operators/browser-workstation-installer.md`: source missing
- `docs/reference/lifecycle-control-plane.md`: source missing
- `docs/reference/verified-operation-outcomes.md`: source missing
- `docs/ai/README.md`: source missing
- `docs/ai/ai-known-errors.md`: source missing
- `docs/ai/navigation/README.md`: source missing
- `src/Meridian.Application/Composition`: source missing
- `src/Meridian.Application/Pipeline`: source missing
- `src/Meridian.Core/Serialization`: source missing
- `src/Meridian.Launcher/LauncherCommandPolicy.cs`: source missing
- `src/Meridian.Launcher/Program.cs`: source missing
- `src/Meridian.LifecycleSupervisor/LifecycleStartupOutcome.cs`: source missing
- `src/Meridian.LifecycleSupervisor/LifecycleSupervisorRuntime.cs`: source missing
- `src/Meridian.LifecycleSupervisor/Program.cs`: source missing
- `src/Meridian.Setup/Program.cs`: source missing

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

### .codex/skills/_shared/project-context.md:1

target file

````
# Meridian — Shared Project Context

> Codex copy. Use this file for Meridian-specific terminology, product direction, commands, and
> architecture when a Codex skill needs repository grounding without repeating the same facts in
> every `SKILL.md`.
>
> **Last verified:** 2026-09-28
> **Primary grounding docs:** `README.md`, `docs/roadmap/data/*.yml`,
> `docs/roadmap/generated/ROADMAP_SUMMARY.md`, `docs/product/meridian-design-document.md`,
> `docs/architecture/meridian-development-intelligence-framework.md`
>
> The section between the `shared-context` markers is identical in all three copies:
> `.claude/skills/_shared/project-context.md` (canonical, edit it first),
> `.codex/skills/_shared/project-context.md`, and `.agents/skills/_shared/project-context.md`.
> After editing the canonical section, run `python build/scripts/ai/generate-guidance.py --write`.
> Check freshness with `python build/scripts/ai/generate-guidance.py --check`; the existing
> `build/scripts/docs/check-ai-inventory.py` gate also detects drift. Host-specific guidance stays
> outside the generated section and is preserved byte-for-byte.

<!-- shared-context:begin — identical in .claude/, .codex/, and .agents/ copies; check-ai-inventory.py enforces it -->

````

