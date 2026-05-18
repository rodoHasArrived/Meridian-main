# Build, Test, Run

**Status:** Active
**Owner:** Core Team
**Reviewed:** 2026-05-18

Use the narrowest command that covers your change.

## Build

```powershell
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln /p:EnableWindowsTargeting=true
```

For the active browser workstation lane, prefer the focused solution filter. It builds the
host-served workstation without WPF, benchmarks, MCP hosts, or optional integration projects:

```powershell
dotnet restore Meridian.WebWorkstation.slnf /p:EnableWindowsTargeting=true
dotnet build Meridian.WebWorkstation.slnf -c Debug --no-restore /p:EnableWindowsTargeting=true /p:UseAppHost=false
python3 build/python/cli/buildctl.py build --project Meridian.WebWorkstation.slnf --configuration Debug --isolation-key web-workstation-dev --property UseAppHost=false --verbosity quiet
```

For automation or concurrent local builds, prefer isolated output:

```powershell
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Release --isolation-key cleanup-pass
```

## Test

```powershell
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj --logger "console;verbosity=normal"
npm --prefix src/Meridian.Ui/dashboard run test
npm run ui:dashboard:test
```

Broaden to WPF, UI-service, MCP, or integration tests only when the touched
files require it.

## CI Equivalents

The automatic `CI` workflow mirrors these commands:

```powershell
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet format Meridian.sln --verify-no-changes --verbosity minimal --no-restore
dotnet build Meridian.WebWorkstation.slnf -c Release --no-restore /p:EnableWindowsTargeting=true /p:UseAppHost=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore --filter "Category!=Integration&Category!=Performance" /p:EnableWindowsTargeting=true
npm ci --prefix src/Meridian.Ui/dashboard
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
```

The `Windows Desktop Build` workflow mirrors a Windows-only WPF build and test pass:

```powershell
dotnet restore tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --no-restore /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release --no-restore --filter "Category!=Integration&FullyQualifiedName!~Integration" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
```

## Run

Main local host and CLI:

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080
```

Use `--mode desktop` only when you intentionally want the retained desktop-local host to start
the UI server and the streaming collector together.

Browser workstation development server:

```powershell
npm --prefix src/Meridian.Ui/dashboard run dev
```

The browser workstation route is `http://localhost:<vite-port>/workstation/`
for Vite and `http://localhost:8080/workstation/` when served by the Meridian
host.

Retained WPF shell:

```powershell
pwsh ./scripts/dev/run-desktop.ps1
```
