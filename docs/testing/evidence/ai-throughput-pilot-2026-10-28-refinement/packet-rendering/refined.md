# Task context packet

Update shared guidance under ## Useful Commands

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

## Validation (not executed)

Recommendations only; no commands executed. Focused checks do not replace required final checks.

- `python build/scripts/docs/check-ai-inventory.py --summary`: Task-to-proof matrix AI tooling/guidance check
- `python build/scripts/docs/check-codex-skills.py --summary`: Task-to-proof matrix AI tooling/guidance check
- `git diff --check -- .agents/skills/_shared/project-context.md .claude/skills/_shared/project-context.md .codex/skills/_shared/project-context.md`: Unstaged whitespace check
- `git diff --cached --check -- .agents/skills/_shared/project-context.md .claude/skills/_shared/project-context.md .codex/skills/_shared/project-context.md`: Staged whitespace check
- `python build/scripts/docs/check-ai-navigation-freshness.py --max-age-days 14`: Module or route validation requirement

Required final checks remain:

- `bash scripts/ci.sh`
- `Meridian CI / quality-gate`
- `Meridian CI / integration-gate`

## Warnings

- No matching source-module owner; inspect the nearest README for ownership and proof.
- Excerpts are partial context. Read applicable instructions in full; packets never override them.

## Target excerpts

### .claude/skills/_shared/project-context.md:110

target file

These sources share this exact excerpt:

- `.claude/skills/_shared/project-context.md:110` — SHA-256 `a5655d379c48de8c6919f3086cafbb7144f62bc4bacbac6624df772695613c92`
- `.agents/skills/_shared/project-context.md:110` — SHA-256 `e573414b6bea3dfd7badac09f6f198d4e4e7889eb7a998aa01aacf48293b93c9`
- `.codex/skills/_shared/project-context.md:111` — SHA-256 `8f733a1f19d01c99a3b66976dd94ce67bddd17e2111abc36e17644a65277f990`

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

## Additional context references

Full optional excerpts remain in the JSON packet; open these only when relevant.

- `.codex/skills/README.md:46`: nearest module README; SHA-256 `4e142869d9cf68cc9fa36d4dc6cc6b4895a64a192ae2df3053981daeb849a551`
- `README.md:243`: nearest module README; SHA-256 `735a0cc3a148421b9092248ba37a803908ce27bcdfd33435e2fd84dc5f1b42f9`
- `docs/ai/README.md:97`: subsystem required doc; SHA-256 `0951df3cc13bd6f970b5c57ec37f4437328dbe5df565e3f09d199fb61668c72a`
- `docs/ai/ai-known-errors.md:190`: route authoritative doc; SHA-256 `eced7f80c436bb43b92c957598c3279accbb048e0c70c7ce3e120e64d8a2e016`
- `docs/ai/navigation/README.md:30`: route authoritative doc; SHA-256 `45ac1f130489f92f752062bf509587b2730721f84b760c71b9dca001807fc8f7`
- `docs/ai/tooling/README.md:13`: AI tooling command and validation index; SHA-256 `9547d8c3116ff2e0e4a3e5a0d25ec58d2c85cde7dc210f143dd3aaf80630def8`
- `docs/reference/verified-operation-outcomes.md:9`: subsystem required doc; SHA-256 `ab6baa0612da0ac37b0a806530258efa9e5a484f9c7becbbe142a6ac87de76a0`

Omitted context: 23 entries; paths and reasons remain in the JSON packet.
