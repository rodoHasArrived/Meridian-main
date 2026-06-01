# Publish Standalone EXE

**Status:** Active
**Owner:** Core Team
**Reviewed:** 2026-05-19

Use the repository publish script for local standalone executable output. Run it
from `D:\Meridian-main`.

```powershell
pwsh ./build/scripts/publish/publish.ps1 -Platform win-x64 -Project collector -OutputDir artifacts/publish/local-standalone
```

The publish script writes generated output under `artifacts/publish/`, which is
ignored by Git and should not be committed.
Use a run-specific output directory and leave retention enabled for ordinary
local smoke runs so old publish products are pruned automatically.

The Windows collector executable is written under:

```text
artifacts/publish/local-standalone/win-x64/collector/Meridian.exe
```

For the WPF desktop executable:

```powershell
pwsh ./build/scripts/publish/publish.ps1 -Platform win-x64 -Project desktop -OutputDir artifacts/publish/local-desktop
```

For the host-served browser workstation bundle:

```powershell
pwsh ./build/scripts/publish/publish.ps1 -Platform win-x64 -Project web-workstation -OutputDir artifacts/publish/local-web-workstation
```

Use `-SizeOptimized` for local size investigation or low-disk publish checks. It keeps the
standalone single-file publish shape, but suppresses publish-only debug/doc output and runs MSBuild
with lower parallelism:

```powershell
pwsh ./build/scripts/publish/publish.ps1 -Platform win-x64 -Project web-workstation -SizeOptimized -OutputDir artifacts/publish/local-size
```

To inspect the largest repo-local generated-output and source roots before or after a publish run:

```powershell
pwsh ./build/scripts/publish/measure-size.ps1
pwsh ./build/scripts/publish/measure-size.ps1 -AsJson
```

The manual `Publish Smoke` GitHub Actions workflow runs the same script on a
Windows runner and uploads the generated `artifacts/publish/publish-smoke/`
directory. It does not create a public release or deploy externally.

For the browser workstation local app installer, use:

```powershell
pwsh ./build/scripts/install/install.ps1 -Mode WebWorkstation -SkipInstall
```

For retained desktop MSIX packaging, use
[docs/operators/deployment-packaging.md](../operators/deployment-packaging.md). For the
browser workstation installer, use
[docs/operators/browser-workstation-installer.md](../operators/browser-workstation-installer.md).
