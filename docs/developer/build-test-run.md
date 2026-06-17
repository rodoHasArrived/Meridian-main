# Build, Test, and Run (Current)

**Status:** active  
**Owner:** core-team  
**Reviewed:** 2026-06-14

Use this for routine verification from the root checkout.

Current local project path: `D:\Meridian-main`.

## Common Commands

```powershell
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --no-restore
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
```

## Validation by Scope

- **Core service logic**

  ```powershell
  dotnet build Meridian.WebWorkstation.slnf -c Debug /p:EnableWindowsTargeting=true /p:UseAppHost=false --no-restore
  dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --filter "Category!=Integration" --no-restore /p:EnableWindowsTargeting=true
  ```

- **Browser workstation slices**

  ```powershell
  npm --prefix src/Meridian.Ui/dashboard run test
  npm --prefix src/Meridian.Ui/dashboard run build
  ```

- **WPF desktop slices**

  ```powershell
  pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Production -BuildOnly
  dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj -c Release --filter "Category!=Integration" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:UseSharedCompilation=false
  ```

## Local Launch Commands

- Browser workstation host mode:

  ```powershell
  dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080
  npm --prefix src/Meridian.Ui/dashboard run dev
  ```

- WPF desktop (development):

  ```powershell
  pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Development
  ```

- WPF desktop (production build only):

  ```powershell
  pwsh ./scripts/dev/run-desktop.ps1 -LaunchMode Production -BuildOnly
  ```

## CI-Oriented Checks

- [`docs/HELP.md`](../HELP.md) lists the canonical automated verification lanes. `make help`
  exposes the same wrapper catalog only in shells where GNU Make is installed.
- Keep lane scopes narrow and choose the command set that matches changed surfaces.
