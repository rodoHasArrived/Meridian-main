# AGENTS.md

This file is a compatibility shim for agents that look for root `AGENTS.md`.
Keep it short and prefer the canonical Meridian guidance sources:

- `CLAUDE.md` for the full repository guide.
- `.codex/skills/_shared/project-context.md` for current Codex project context.
- `docs/HELP.md` for verified operator and developer CLI workflows.
- `README.md` for top-level onboarding and planning links.

## Current Direction

- Meridian is a .NET 9 fund-management and trading-platform codebase.
- `src/Meridian.Wpf/` is the primary operator shell. Prefer desktop-first workflow guidance.
- Keep top-level operator navigation to `Research`, `Trading`, `Data Operations`, and `Governance`.
- Use the narrowest validation command that covers the files changed.

## Command Discovery

```bash
make help
dotnet run --project src/Meridian/Meridian.csproj -- --help
python3 build/python/cli/buildctl.py --help
```

## Build And Test

```bash
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Release
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Debug --verbosity quiet
make test
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj --logger "console;verbosity=normal"
```

For concurrent automation, use an isolation key so builds write under `artifacts/bin/<key>/`
and `artifacts/obj/<key>/` instead of shared project output folders:

```bash
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Release --isolation-key automation-run
```

## Desktop Workflows

```powershell
pwsh ./scripts/dev/run-desktop.ps1
pwsh ./scripts/dev/run-desktop.ps1 -NoBuild
pwsh ./scripts/dev/run-desktop.ps1 -Fixture
```

The desktop launcher builds the local host and WPF shell when needed, starts the host on
`http://localhost:8080`, waits for `/healthz`, launches the shell, and stops the owned host
when the desktop exits.

```bash
make desktop-build
make desktop-test
make desktop-test-position-blotter-route
```

`make desktop-test-position-blotter-route` runs
`scripts/dev/validate-position-blotter-route.ps1`, which builds and tests the focused WPF
position-blotter route slice with isolated output and writes validation artifacts under
`artifacts/wpf-validation/position-blotter-route/`.

TODO: `README.md` and `.codex/skills/_shared/project-context.md` mention `make desktop-run`,
but the current `make/desktop.mk` does not define that target. Use
`pwsh ./scripts/dev/run-desktop.ps1` unless the Make target is restored.

## Diagnostics And Docs

```bash
make doctor
make doctor-quick
make verify-setup
make collect-debug
make validate-data
make ai-arch-check
make ai-docs-freshness
make ai-docs-drift
make ai-docs-sync-report
make verify-adrs
make verify-tooling-metadata
make docs-all
make skill-list
make skill-discover
```

Do not add package versions directly to project files. Central package management lives in
`Directory.Packages.props`.
