# AI Known Errors and Prevention Checklist

This file tracks repeated AI-agent mistakes so future agents can avoid introducing the same failures.

## How to use this file

1. Review this file before coding.
2. If your task intersects an item below, run its prevention checklist.
3. Add a new entry whenever an AI-caused issue is found and fixed.

## Automated issue intake workflow

AI regressions can be recorded automatically from GitHub issues:

1. Open an issue and apply the label `ai-known-error`.
2. Include these headings in the issue body so automation can extract fields:
   - `## Area`
   - `## Symptoms`
   - `## Root cause`
   - `## Prevention checklist`
   - `## Verification commands`
3. The `AI Known Errors Intake` job in `.github/workflows/documentation.yml` creates a PR that appends (or updates) an entry in this file.

If headings are missing, the workflow still creates an entry with safe defaults and links back to the source issue.

## Entry template

- **ID**: AI-YYYYMMDD-<short-tag>
- **Area**: (docs/build/tests/runtime/config)
- **Symptoms**: What failed.
- **Root cause**: Why the error was introduced.
- **Prevention checklist**:
  - [ ] Check 1
  - [ ] Check 2
- **Verification commands**:
  - `command`
- **Source issue**: #123
- **Status**: open | mitigated | closed

---

## Known issues

### AI-20260205-missing-error-memory
- **Area**: process/documentation
- **Symptoms**: Agents repeatedly reintroduced previously fixed mistakes because no persistent error memory existed.
- **Root cause**: No standardized location documenting recurrent AI errors and prevention steps.
- **Prevention checklist**:
  - [ ] Read this file at task start.
  - [ ] Cross-check your plan against existing known issues.
  - [ ] If a new AI-caused issue is fixed, add/update an entry before PR.
- **Verification commands**:
  - `test -f docs/ai/ai-known-errors.md`
  - `rg "AI-" docs/ai/ai-known-errors.md`
- **Source issue**: manual bootstrap
- **Status**: mitigated

### AI-20260205-wpf-grid-padding
- **Area**: build/WPF/XAML
- **Symptoms**: WPF builds fail with error MC3072: "The property 'Padding' does not exist in XML namespace". Build succeeds in UWP but fails in WPF.
- **Root cause**: Grid control doesn't support Padding property in WPF (unlike UWP/WinUI). This is a WPF/UWP API compatibility difference that agents may not be aware of when porting XAML code.
- **Prevention checklist**:
  - [ ] When working with WPF XAML, check that Grid elements don't use Padding, CornerRadius, BorderBrush, or BorderThickness properties
  - [ ] If padding is needed on a Grid in WPF, wrap it in a Border element instead
  - [ ] Search for `<Grid.*Padding=` pattern in WPF .xaml files before committing
  - [ ] Remember: Border, StackPanel, and DockPanel support Padding in WPF, but Grid does not
- **Verification commands**:
  - `grep -rn '<Grid.*Padding=' src/Meridian.Wpf --include="*.xaml"`
  - `dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --no-restore -p:TargetFramework=net9.0-windows`
- **Source issue**: https://github.com/rodoHasArrived/Meridian/actions/runs/21707017569/job/62600607213
- **Status**: fixed

### AI-20260205-nu1008-central-package-management
- **Area**: build/NuGet/CPM
- **Symptoms**: Build fails with error NU1008: "Projects that use central package version management should not define the version on the PackageReference items but on the PackageVersion items: <PackageName>". This occurs during `dotnet restore` or `dotnet build`.
- **Root cause**: The repository uses Central Package Management (CPM) with `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` in `Directory.Packages.props`. When a new package is added to a project's `.csproj` file without adding its version to `Directory.Packages.props`, NuGet restore fails with NU1008.
- **Prevention checklist**:
  - [ ] When adding a new `<PackageReference>` to any `.csproj` or `.fsproj` file, NEVER include a `Version` attribute
  - [ ] Always add the package version to `Directory.Packages.props` using `<PackageVersion Include="PackageName" Version="x.y.z" />`
  - [ ] Search for the appropriate section label in `Directory.Packages.props` (e.g., "Storage", "Testing", "WinUI / Desktop")
  - [ ] After adding package references, run `dotnet restore` to verify no NU1008 errors
  - [ ] Check existing packages in `Directory.Packages.props` for version compatibility before adding new ones
- **Verification commands**:
  - `dotnet restore Meridian.sln /p:EnableWindowsTargeting=true`
  - `dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true`
  - `grep -r 'PackageReference Include=".*" Version=' --include="*.csproj" --include="*.fsproj" src/ | grep -v '<!--'` (should return no results)
- **Source issue**: https://github.com/rodoHasArrived/Meridian/actions/runs/21707148084/job/62601061503
- **Status**: fixed

### AI-20260206-provider-sdk-cross-file-type-resolution
- **Area**: build/ProviderSdk
- **Symptoms**: Build fails with CS0246 errors in `IProviderMetadata.cs`: "The type or namespace name 'ProviderType' could not be found" and "The type or namespace name 'Backfill' could not be found", even though the types exist in the same project and namespace.
- **Root cause**: When `IProviderMetadata.cs` was moved from the main Meridian project to the ProviderSdk project, it lost access to `ProviderType` (previously in `ProviderRegistry.cs`) and `HistoricalDataCapabilities` (previously in `IHistoricalDataProvider.cs`). A prior fix moved those types into standalone files in ProviderSdk but the cross-file namespace resolution for the relative `Backfill.HistoricalDataCapabilities` reference failed. The fix is to co-locate the `ProviderType` enum in the same file as its consumer and use an explicit `using` directive for the Backfill namespace.
- **Prevention checklist**:
  - [ ] When moving types between projects, verify all type references in the destination project resolve correctly
  - [ ] Use explicit `using` directives for sibling namespaces instead of relative namespace prefixes (e.g., `using X.Y.Backfill;` + `HistoricalDataCapabilities` instead of `Backfill.HistoricalDataCapabilities`)
  - [ ] Co-locate small types (enums, records) with their primary consumer when they are tightly coupled
  - [ ] After moving types, build the specific project in isolation: `dotnet build src/Meridian.ProviderSdk`
- **Verification commands**:
  - `dotnet build src/Meridian.ProviderSdk/Meridian.ProviderSdk.csproj -c Release`
  - `dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true`
- **Source issue**: PR #860 (incomplete fix)
- **Status**: fixed

### AI-20260207-storage-namespace-circular-dependency
- **Area**: build/Infrastructure
- **Symptoms**: Build fails with CS0234: "The type or namespace name 'Storage' does not exist in the namespace 'Meridian'" in `DataGapRepair.cs`. Three errors on the `using Meridian.Storage` lines.
- **Root cause**: During the layer assembly split (commit `ac7bd35`), `DataGapRepair.cs` was placed in the Infrastructure project but retained `using` statements for `Meridian.Storage`. The Infrastructure project does not (and should not) reference the Storage project to avoid circular dependencies. A prior fix only commented out the lines rather than removing them.
- **Prevention checklist**:
  - [ ] When splitting code into separate assemblies, verify all `using` directives resolve against the project's actual references
  - [ ] Infrastructure layer must never reference Storage layer directly; use abstractions (e.g., `IStorageSink`) injected via DI
  - [ ] Remove dead commented-out code entirely rather than leaving `// using` statements that obscure the issue
  - [ ] After moving files between projects, build the specific project: `dotnet build src/Meridian.Infrastructure`
- **Verification commands**:
  - `dotnet build src/Meridian.Infrastructure/Meridian.Infrastructure.csproj -c Release`
  - `grep -rn 'Meridian\.Storage' src/Meridian.Infrastructure --include="*.cs"` (should return no results)
- **Source issue**: CI build failure on main branch
- **Status**: fixed

### AI-20260210-cs0738-type-collision
- **Area**: build/namespaces
- **Symptoms**: Build fails with error CS0738: "'ConfigService' does not implement interface member 'IConfigService.ValidateConfigAsync(CancellationToken)' because it does not have the matching return type of 'Task<ConfigValidationResult>'". The interface method clearly returns `Task<ConfigValidationResult>` and the implementation returns the same type, yet the compiler rejects it.
- **Root cause**: Two classes with the same name (`ConfigValidationResult`) exist in parent and child namespaces: `Meridian.Ui.Services.ConfigValidationResult` (in DiagnosticsService.cs) and `Meridian.Ui.Services.Contracts.ConfigValidationResult` (in IConfigService.cs). When `ConfigService` (in the parent namespace) implements `IConfigService`, the compiler cannot disambiguate which `ConfigValidationResult` to use, even though the interface requires the one from the child namespace.
- **Prevention checklist**:
  - [ ] When creating new types, search for existing types with the same name in both parent and child namespaces
  - [ ] Use fully qualified type names in return types when ambiguity is possible: `Task<Contracts.ConfigValidationResult>`
  - [ ] When naming collision is detected, rename the type in the parent namespace with a descriptive prefix (e.g., `DiagnosticConfigValidationResult`)
  - [ ] After refactoring, verify no CS0738 errors: `dotnet build src/Meridian.Ui.Services -c Release`
  - [ ] Check for similar patterns: `grep -rn "class ConfigValidationResult" src --include="*.cs"` should show only one result per type
- **Verification commands**:
  - `dotnet build src/Meridian.Ui.Services/Meridian.Ui.Services.csproj -c Release`
  - `dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release -p:TargetFramework=net9.0-windows` (on Windows)
  - `dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release -p:TargetFramework=net9.0-windows` (on Windows)
- **Source issue**: https://github.com/rodoHasArrived/Meridian/actions/runs/21851485930/job/63058846153
- **Status**: fixed (commit cec548e)

### AI-20260212-codecov-directory-mismatch
- **Area**: CI/GitHub Actions/codecov
- **Symptoms**: GitHub Actions step "Upload coverage reports" fails silently or reports no coverage found. The workflow completes but Codecov doesn't receive coverage data. In pr-checks.yml, step 5 (Upload coverage reports) fails to find coverage files.
- **Root cause**: The `dotnet test` command outputs coverage files to a directory specified by `--results-directory` parameter, but the `codecov-action` configuration uses a different directory path. For example, pr-checks.yml had `--results-directory ./artifacts/test-results` but codecov was configured with `directory: ./coverage`.
- **Prevention checklist**:
  - [ ] When modifying test commands with `--results-directory`, also update the codecov upload step
  - [ ] Use the `files:` parameter with glob pattern instead of `directory:` for codecov-action: `files: ./artifacts/test-results/**/coverage.cobertura.xml`
  - [ ] Verify consistency: the path in `files:` must match the path in `--results-directory`
  - [ ] Check that diagnostics artifact upload also references the correct coverage path
  - [ ] Search for all codecov-action usages: `grep -rn "codecov-action" .github/workflows/`
- **Verification commands**:
  - `grep -A10 "dotnet test" .github/workflows/pr-checks.yml | grep "results-directory"`
  - `grep -A3 "codecov-action" .github/workflows/pr-checks.yml | grep -E "(directory|files)"`
  - `dotnet test Meridian.sln --collect:"XPlat Code Coverage" --results-directory ./test-results && ls -la ./test-results/**/coverage.cobertura.xml`
- **Source issue**: https://github.com/rodoHasArrived/Meridian/actions/runs/21938525658/job/63358083776#step:5:1
- **Status**: fixed (commit ad97ee2)

### AI-20260212-conditional-job-dependency
- **Area**: workflows/GitHub Actions
- **Symptoms**: Workflow jobs with `if: always()` fail to run when depending on jobs with conditional execution (`if: startsWith(...)`). The dependent job is skipped even though `always()` is specified, because GitHub Actions treats unmet dependencies (skipped jobs) as a blocking condition.
- **Root cause**: GitHub Actions job dependencies (`needs:`) require all listed jobs to complete successfully or be explicitly handled. When a job is conditionally skipped (e.g., `if: startsWith(github.ref, 'refs/tags/v')`), any job depending on it will also be skipped unless proper conditional logic is used. The `if: always()` condition means "run regardless of previous job failures" but not "run even if dependencies are skipped."
- **Prevention checklist**:
  - [ ] When a job has `needs:` dependencies, verify all dependent jobs run in the same conditions or have proper handling
  - [ ] Never depend on conditionally-executed jobs (with `if:` conditions) from jobs that always run
  - [ ] If cleanup jobs need to run `always()`, only depend on jobs that also run unconditionally
  - [ ] Use conditional expressions in dependencies: `if: always() && needs.job-name.result != 'skipped'`
  - [ ] Document job dependencies with comments explaining conditional logic
  - [ ] Test workflows on both tag and non-tag branches to verify all jobs execute as expected
- **Verification commands**:
  - `grep -A5 "needs:" .github/workflows/*.yml | grep -B5 "if:"`
  - `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/desktop-builds.yml'))"`
- **Source issue**: https://github.com/rodoHasArrived/Meridian/actions/runs/21958711610/job/63429967664
- **Status**: fixed (commit 4f6088f)

---

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

### AI-20260213-nullable-value-property-misuse
- **Area**: build/C#/nullable types
- **Symptoms**: Build fails with CS1061 errors: "'double' does not contain a definition for 'Value' and no accessible extension method 'Value' accepting a first argument of type 'double' could be found". This occurs when accessing `.Value` on nullable value types after using the null-forgiving operator (`!`), or when using `out var` with generic methods that return `T?` where the compiler fails to properly infer the nullable type.
- **Root cause**: When `TryGetFromNewest` was called on a `CircularBuffer<double>`, the `out T? value` parameter became `out double? value`, making `fromValue` and `toValue` of type `double?` (nullable double). The Windows C# compiler has a bug where it incorrectly infers `out var` as `double` instead of `double?` when the out parameter is `T?` for a value type.
- **Structural fix (commit 49b2916)**: Changed API signature from `out T? value` to `out T value` with `[MaybeNullWhen(false)]` attribute. This eliminates the entire category of nullable value type inference issues because there's no longer a nullable generic out parameter. The bool return value is the presence/absence signal, following standard .NET TryX pattern. This is the recommended approach because:
  1. Eliminates Windows compiler inference bug entirely
  2. Follows idiomatic .NET patterns (like Dictionary.TryGetValue)
  3. Allows safe use of `out var` everywhere
  4. No defensive null checks needed when true is returned
  5. Prevents future regressions structurally
- **Prevention checklist**:
  - [ ] For Try pattern APIs, use `bool TryX(out T value)` with `[MaybeNullWhen(false)]` instead of `bool TryX(out T? value)`
  - [ ] The bool is the presence/absence signal; don't use nullable for that purpose
  - [ ] Assign `default!` on failure path to satisfy nullable reference types analyzer
  - [ ] When updating existing `out T?` APIs, this is a breaking change but eliminates entire bug class
  - [ ] Test builds on both Linux and Windows after any changes to nullable type handling
- **Structural fix pattern**:
  ```csharp
  public bool TryGetFromNewest(int offsetFromNewest, [MaybeNullWhen(false)] out T value)
  {
      if (offsetFromNewest < 0 || offsetFromNewest >= _count)
      {
          value = default!;  // Use default! to satisfy analyzer
          return false;
      }
      
      value = _buffer[index];
      return true;
  }
  
  // Call site - clean and simple, works everywhere
  if (!buffer.TryGetFromNewest(fromOffset, out var fromValue))
      return null;
  
  // fromValue is T (e.g., double), no nullable inference involved
  ```
- **Verification commands**:
  - `dotnet build src/Meridian.Ui.Services/Meridian.Ui.Services.csproj -c Release`
  - `dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release -p:TargetFramework=net9.0-windows`
  - `grep -n 'out T? value' src/Meridian.Ui.Services/Collections/CircularBuffer.cs` (should return no matches after fix)
- **Source issues**: 
  - https://github.com/rodoHasArrived/Meridian/actions/runs/21988186038/job/63527798918#step:5:1 (original)
  - https://github.com/rodoHasArrived/Meridian/actions/runs/21996212289/job/63556782046 (variant)
  - https://github.com/rodoHasArrived/Meridian/actions/runs/21998525615/job/63564824033#step:5:1 (regression)
- **Status**: fixed structurally (commit 49b2916 - changed API to eliminate nullable generic out parameter)
- **Note**: This issue regressed multiple times (1e2ea1d, 5756479, 1802ea9, bf67ed5, e920c34) when using workarounds. The structural fix eliminates the problem at the API design level.

---

### AI-20260214-preflight-config-check-ci-failure
- **ID**: AI-20260214-preflight-config-check-ci-failure
- **Area**: CI/build/preflight
- **Symptoms**: Build Observability workflow fails with error "FAILED preflight :: config/appsettings.json missing (run make setup-config)" even though the file is tracked in git. The preflight check fails in CI environments where the config file isn't needed.
- **Root cause**: The preflight check in `build/python/diagnostics/preflight.py` unconditionally requires `config/appsettings.json` to exist. This is appropriate for local development (where the file is needed for running the application) but not for CI environments where only building is required. The config file is gitignored but was previously committed, causing confusion.
- **Prevention checklist**:
  - [ ] When adding preflight checks that validate local development setup, make them CI-aware
  - [ ] Check for `CI=true` or `GITHUB_ACTIONS=true` environment variables to skip runtime-only checks
  - [ ] Distinguish between build-time requirements (tools, SDKs) and runtime requirements (config, secrets)
  - [ ] Test preflight checks both locally and in CI-like environments (with CI=true)
- **Verification commands**:
  - `CI=true python3 build/python/cli/buildctl.py build --configuration Release` (should pass preflight even without config file)
  - `python3 -c "import os; os.environ['CI']='true'; from pathlib import Path; import sys; sys.path.insert(0,'build/python'); from diagnostics.preflight import run_preflight; print(run_preflight(Path('.')))"` (should return (True, []))
- **Source issue**: https://github.com/rodoHasArrived/Meridian/actions/runs/22014850305/job/63614949528#step:5:1
- **Status**: fixed (commit 55d2827)
- **Fixed in**: build/python/diagnostics/preflight.py - Added CI detection to skip config check when CI=true or GITHUB_ACTIONS=true

### AI-20260215-maintenance-endpoint-500-for-missing-schedule
- **ID**: AI-20260215-maintenance-endpoint-500-for-missing-schedule
- **Area**: runtime/endpoints
- **Symptoms**: POST `/api/maintenance/schedules/{id}/run` returns HTTP 500 Internal Server Error instead of 404 when the schedule ID doesn't exist. Integration tests masked this by accepting 500 alongside 404 with a catch-all that passed unconditionally (`true.Should().BeTrue()`).
- **Root cause**: `ScheduledArchiveMaintenanceService.TriggerScheduleAsync()` throws `KeyNotFoundException` for non-existent schedules. The endpoint handler did not catch this exception, so it propagated as an unhandled 500 error. The test was written to tolerate this by accepting `InternalServerError` and using a tautological catch-all.
- **Prevention checklist**:
  - [ ] When implementing endpoints that accept entity IDs, always handle `KeyNotFoundException` and return 404
  - [ ] Never write tests with `true.Should().BeTrue()` inside catch blocks — these are tautological and mask real failures
  - [ ] Tighten endpoint test status code assertions: avoid accepting 4–5 different codes when only 1–2 are correct
  - [ ] Check that all CRUD endpoints consistently return 404 for non-existent entities
- **Verification commands**:
  - `dotnet test tests/Meridian.Tests --filter "FullyQualifiedName~MaintenanceEndpointTests"`
- **Status**: fixed
- **Fixed in**: `src/Meridian.Ui.Shared/Endpoints/MaintenanceScheduleEndpoints.cs` — added `catch (KeyNotFoundException)` returning `Results.NotFound()`. Tests tightened to assert 404 instead of 500.

### AI-20260215-tautological-test-assertions
- **ID**: AI-20260215-tautological-test-assertions
- **Area**: tests/quality
- **Symptoms**: Several tests contained `true.Should().BeTrue()` inside catch-all blocks, making them incapable of ever failing. Other tests asserted `ThrowAsync<Exception>()` (the base type) which is trivially satisfiable by any error.
- **Root cause**: When writing tests for endpoints or services that return implementation-dependent results, agents used overly permissive assertions to avoid test failures rather than fixing the underlying code.
- **Prevention checklist**:
  - [ ] Never use `true.Should().BeTrue()` or equivalent no-op assertions
  - [ ] Never catch exceptions in test bodies without asserting on the exception type or message
  - [ ] Assert specific exception types (`ThrowAsync<OperationCanceledException>()`) rather than `ThrowAsync<Exception>()`
  - [ ] Tests should fail when the system behaves incorrectly — if a test can't fail, it provides no value
  - [ ] Audit tests for bare `catch {}` blocks that silently pass
- **Status**: fixed
- **Fixed in**: `MaintenanceEndpointTests.cs`, `WpfDataQualityServiceTests.cs`, `ConnectionServiceTests.cs` — removed tautological assertions, tightened status code checks, added real assertions

### AI-20260216-github-actions-shell-powershell
- **ID**: AI-20260216-github-actions-shell-powershell
- **Area**: build/ci/github-actions
- **Symptoms**: GitHub Actions workflow fails on Windows runners with PowerShell parsing error: "Missing expression after unary operator '--'". The error occurs when a multi-line `run:` step uses backslash (`\`) for line continuation without specifying the shell.
- **Root cause**: GitHub Actions defaults to PowerShell on Windows runners. PowerShell uses backtick (`` ` ``) for line continuation, not backslash (`\`). When a workflow step uses bash-style backslash continuation without `shell: bash`, PowerShell interprets `--no-restore \` as attempting to use `--` as a unary operator, causing a parse error.
- **Prevention checklist**:
  - [ ] When writing multi-line `run:` steps with backslash (`\`) line continuation, always specify `shell: bash`
  - [ ] Check reusable workflows that run on multiple platforms (ubuntu, windows, macos) for missing shell specifications
  - [ ] Review existing steps in the same workflow file for consistency in shell specification
  - [ ] If using PowerShell-specific syntax, use `shell: pwsh` and backtick (`` ` ``) for line continuation
  - [ ] Validate workflow YAML syntax after changes: `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/file.yml'))"`
- **Verification commands**:
  - `grep -r "run: |" .github/workflows/*.yml | grep -v "shell:"` (should return no multi-line bash commands without shell specification)
  - `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/reusable-dotnet-build.yml'))"`
- **Source issue**: https://github.com/rodoHasArrived/Meridian/actions/runs/22047221887/job/63698175553
- **Status**: fixed
- **Fixed in**: `.github/workflows/reusable-dotnet-build.yml` line 121 — added `shell: bash` to the Build step

### AI-20260220-cs0104-backfill-result-ambiguity
- **ID**: AI-20260220-cs0104-backfill-result-ambiguity
- **Area**: build/namespaces
- **Symptoms**: Build fails with CS0104: "'BackfillResult' is an ambiguous reference between 'Meridian.Contracts.Api.BackfillResult' and 'Meridian.Application.Backfill.BackfillResult'". Using alias directives (`using BackfillResult = ...`) fail to resolve the ambiguity when the conflicting namespace is also imported via a `using` namespace directive.
- **Root cause**: Both `Meridian.Contracts.Api` and `Meridian.Application.Backfill` define types named `BackfillResult` and `BackfillRequest`. When both namespaces are imported via `using` directives alongside `using` alias directives for disambiguation, the compiler still reports CS0104. The fix is to remove the `using Meridian.Application.Backfill;` namespace directive entirely, keeping only the explicit aliases for the specific types needed.
- **Prevention checklist**:
  - [ ] When two imported namespaces contain types with the same name, do NOT import both namespaces — import only one and use aliases for types from the other
  - [ ] Search for duplicate type names across namespaces before adding new `using` directives: `grep -rn "class TypeName\|record TypeName" src/ --include="*.cs"`
  - [ ] When adding a new type to `Contracts.Api`, check if a type with the same name exists in `Application.Backfill` (or vice versa)
  - [ ] Prefer fully qualified names or using aliases over importing both conflicting namespaces
- **Verification commands**:
  - `dotnet build src/Meridian.Ui.Shared/Meridian.Ui.Shared.csproj -c Release`
  - `grep -rn "using Meridian.Application.Backfill;" src/Meridian.Ui.Shared --include="*.cs"` (should return no results after fix)
- **Source issue**: CI build failure
- **Status**: fixed
- **Fixed in**: `BackfillEndpoints.cs` and `BackfillCoordinator.cs` — removed `using Meridian.Application.Backfill;` namespace directive, kept explicit `using` aliases for `BackfillRequest` and `BackfillResult`, and fully qualified `HistoricalBackfillService`

### AI-20260305-cs0266-narrowing-type-cast
- **ID**: AI-20260305-cs0266-narrowing-type-cast
- **Area**: build/types
- **Symptoms**: Build fails with `error CS0266: Cannot implicitly convert type 'int' to 'ushort'. An explicit conversion exists (are you missing a cast?)`. This occurred in `TradeDataCollector.cs` inside `RemoveFromRollingWindow` when assigning `Math.Max(0, state.TradeCount - 1)` to a `ushort` field.
- **Root cause**: C# arithmetic on `ushort` operands promotes both operands to `int` before performing the operation. `Math.Max(int, int)` also returns `int`. The result cannot be implicitly narrowed back to `ushort` even when the value is guaranteed to fit. An explicit `(ushort)` cast is required. This is the same narrowing rule that applies to `byte`, `sbyte`, and `short` fields.
- **Prevention checklist**:
  - [ ] When decrementing (or performing arithmetic on) fields typed `ushort`, `short`, `byte`, or `sbyte`, always add an explicit narrowing cast: `(ushort)(value - 1)` or `(ushort)Math.Max(0, value - 1)`
  - [ ] When an enum backing type of `byte` is used and values exceed `byte.MaxValue` (255), change the backing type to `int` instead of keeping `byte`
  - [ ] After changing field types to a narrower type, build immediately: `dotnet build src/Meridian.Domain -c Release /p:EnableWindowsTargeting=true`
  - [ ] Search for arithmetic expressions on narrow integer fields before committing: `grep -rn "ushort\|: byte" src/ --include="*.cs"`
- **Verification commands**:
  - `dotnet build Meridian.sln -c Release /p:EnableWindowsTargeting=true 2>&1 | grep -E "error CS0266|Error\(s\)"`
  - `dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true --filter "Domain"`
- **Source issue**: https://github.com/rodoHasArrived/Meridian/actions/runs/22709535738/job/65984383245#step:6:1
- **Status**: fixed
- **Fixed in**: `src/Meridian.Domain/Collectors/TradeDataCollector.cs` line 381 — added `(ushort)` cast: `state.TradeCount = (ushort)Math.Max(0, state.TradeCount - 1);`. Also `src/Meridian.Application/Results/ErrorCode.cs` — changed backing from `byte` to `int` since error codes use values up to 8005, which exceeds `byte.MaxValue` (255).

### AI-20260220-regex-hyphen-character-class
- **ID**: AI-20260220-regex-hyphen-character-class
- **Area**: build/ci/github-actions
- **Symptoms**: Ticker data collection workflow fails with "Input validation failed: one or more symbols contain unsupported characters" for hyphenated preferred share symbols like PCG-PA, PCG-PB, PCG-PC even after normalization to uppercase.
- **Root cause**: In the bash `grep -E` character class `[A-Z0-9.\-^=]`, the `\-` escape sequence is placed between `.` and `^`. POSIX ERE interprets this as a potential range `\-^`, which does not match the hyphen character in uppercase symbols like PCG-PA. Hyphens in a bracket expression are only guaranteed to be treated as literals when placed at the start or end of the character class.
- **Prevention checklist**:
  - [ ] In regex character classes, always place the literal hyphen at the **end** of the class (e.g., `[A-Z0-9.^=-]`) or at the start
  - [ ] Never use `\-` in the middle of a character class — use `-` at the end instead
  - [ ] When supporting stock tickers, the validation pattern must allow: uppercase letters (A-Z), digits (0-9), dot (BRK.A), hyphen (PCG-PA), caret (^GSPC), and equals (=SPX)
  - [ ] Test validation patterns with hyphenated preferred share symbols: `echo "PCG-PA" | grep -E '^[A-Z0-9.^=-]+$'`
- **Verification commands**:
  - `echo "PCG-PA" | grep -E '^[A-Z0-9.^=-]+$' && echo "OK" || echo "FAIL"`
  - `printf 'PCG-PA\nPCG-PB\nBRK.A\n^GSPC\n=SPX' | grep -Ev '^[A-Z0-9.^=-]+$' | wc -l` (should return 0)
- **Source issue**: https://github.com/rodoHasArrived/Meridian/actions/runs/21857738217/job/64242040198 (original, Feb 10) and https://github.com/rodoHasArrived/Meridian/actions/runs/22083541882/job/64238547291 (recurrence, Feb 20)
- **Status**: fixed
- **Fixed in**: `.github/workflows/ticker-data-collection.yml` line 97 — changed `grep -qEv '^[A-Z0-9.\-^=]+$'` to `grep -Ev '^[A-Z0-9.^=-]+$'` (hyphen moved to end of character class); also improved to capture and display the invalid symbols before rejecting

### AI-20260306-enum-byte-backing-overflow
- **ID**: AI-20260306-enum-byte-backing-overflow
- **Area**: build/types
- **Symptoms**: Build fails with CS0031 ("Constant value 'X' cannot be converted to a 'byte'") across many files. Category-ranged enum values (1000–8005) far exceed the byte range (0–255). All downstream consumers and tests also fail to compile.
- **Root cause**: An optimization pass changed `public enum ErrorCode : int` to `public enum ErrorCode : byte` without checking whether the existing values fit in a byte. The values in `ErrorCode` use a category-based numbering scheme (General=1000s, Validation=2000s, …, Storage=8000s) that requires at minimum a 16-bit backing type.
- **Prevention checklist**:
  - [ ] Before narrowing any enum backing type, grep its values and confirm max value ≤ type max: `grep -E '= [0-9]+' src/.../ErrorCode.cs | awk -F'= ' '{print $2+0}' | sort -n | tail -1`
  - [ ] Never use `byte` as a backing type for category-ranged error/status enums; prefer `int` (default) unless values are explicitly documented to fit in the smaller type
  - [ ] After changing an enum backing type, run a full build immediately: `dotnet build Meridian.sln -c Release /p:EnableWindowsTargeting=true`
  - [ ] Check all tests that cast integer literals to the enum (e.g., `(DataSourceKind)999`): literals must fit in the backing type
- **Verification commands**:
  - `dotnet build Meridian.sln -c Release /p:EnableWindowsTargeting=true`
  - `grep -E ': byte' src/Meridian.Application/Results/ErrorCode.cs` (should return no results — backing type must be int)
  - `grep -E 'ErrorCode\.' src/ -r --include="*.cs" | grep -v '//\|\.cs:' | head -5`
- **Source issue**: https://github.com/rodoHasArrived/Meridian/actions/runs/22710880501/job/65983564475#step:6:1
- **Status**: fixed
- **Fixed in**: `src/Meridian.Application/Results/ErrorCode.cs` — changed `: byte` to `: int`; `src/Meridian.Domain/Collectors/TradeDataCollector.cs` — added `(ushort)` cast on `Math.Max(0, TradeCount - 1)`; `tests/…/MarketDataClientFactoryTests.cs` — changed `(DataSourceKind)999` to `(DataSourceKind)200` (999 > 255); `tests/…/OptionContractSpecTests.cs` — removed `[InlineData(-1)]` (ushort cannot be negative); `tests/…/CanonicalizationGoldenFixtureTests.cs` — added `(byte)` cast for `canonicalizationVersion` comparison

### AI-20260317-cs0104-backtesting-marketevent-ambiguity
- **ID**: AI-20260317-cs0104-backtesting-marketevent-ambiguity
- **Area**: build/namespaces
- **Symptoms**: Build fails with 11 CS0104 errors: "'MarketEvent' is an ambiguous reference between 'Meridian.Contracts.Domain.Events.MarketEvent' and 'Meridian.Domain.Events.MarketEvent'" across `BacktestEngine.cs`, `IFillModel.cs`, `BarMidpointFillModel.cs`, `OrderBookFillModel.cs`, and `MultiSymbolMergeEnumerator.cs`. The entire `Meridian.Backtesting` project fails to compile, causing CI test suite failure.
- **Root cause**: `GlobalUsings.cs` in the Backtesting project imported `global using Meridian.Contracts.Domain.Events;`. Individual source files within the same project also imported `using Meridian.Domain.Events;`. Both namespaces define a `MarketEvent` type, producing an unresolvable ambiguity (CS0104) at every usage site. The Backtesting engine exclusively uses `Domain.Events.MarketEvent` (the richer type with `StampReceiveTime`, `EstimatedLatencyMs`, `EffectiveSymbol`, etc.), making the `Contracts.Domain.Events` global import redundant and harmful.
- **Prevention checklist**:
  - [ ] When editing `GlobalUsings.cs`, verify the namespace you are importing does not define types that already exist in other imported namespaces in the same project
  - [ ] When two namespaces define a type with the same name, import only one globally and use explicit `using` directives or fully qualified names for types from the other
  - [ ] After modifying `GlobalUsings.cs`, build the affected project in isolation: `dotnet build src/Meridian.Backtesting/Meridian.Backtesting.csproj -c Release /p:EnableWindowsTargeting=true`
  - [ ] Check for cross-namespace type name conflicts: `grep -rn "class MarketEvent\|record MarketEvent" src/ --include="*.cs"`
  - [ ] The authoritative streaming type is `Meridian.Domain.Events.MarketEvent`; `Contracts.Domain.Events.MarketEvent` is a lighter DTO — never import both globally in the same project
- **Verification commands**:
  - `dotnet build src/Meridian.Backtesting/Meridian.Backtesting.csproj -c Release /p:EnableWindowsTargeting=true`
  - `dotnet test tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj -c Release /p:EnableWindowsTargeting=true`
  - `grep "Contracts.Domain.Events" src/Meridian.Backtesting/GlobalUsings.cs` (should return no results after fix)
- **Source issue**: https://github.com/rodoHasArrived/Meridian/actions/runs/23176103636, #2005
- **Status**: fixed
- **Fixed in**: `src/Meridian.Backtesting/GlobalUsings.cs` — removed `global using Meridian.Contracts.Domain.Events;`; also added `[assembly: InternalsVisibleTo("Meridian.Backtesting.Tests")]` following the pattern in `Application/GlobalUsings.cs`

### AI-20260318-cs0433-compile-include-contracts-ambiguity
- **ID**: AI-20260318-cs0433-compile-include-contracts-ambiguity
- **Area**: build/project-structure
- **Symptoms**: WPF desktop build fails with dozens of CS0433 errors: "The type 'X' exists in both 'Meridian.Ui.Services' and 'Meridian.Contracts'" across every Contracts type referenced in the WPF project.
- **Root cause**: `Ui.Services.csproj` used `<Compile Include>` to source-link all Contracts `.cs` files directly into the `Ui.Services` assembly. When the WPF project referenced both `Ui.Services` AND `Backtesting` (which brings in `Contracts.dll` transitively), the same types existed in two assemblies simultaneously, causing unresolvable CS0433 ambiguity at every usage site.
- **Prevention checklist**:
  - [ ] Never use `<Compile Include>` to copy source files from a referenced project into a consuming library — this creates duplicate type definitions when the source project is also referenced transitively elsewhere
  - [ ] Always express library-to-library dependencies as `<ProjectReference>` so MSBuild deduplicates the assembly graph
  - [ ] After wiring a new `<ProjectReference>`, build the full solution: `dotnet build Meridian.sln -c Release /p:EnableWindowsTargeting=true`
  - [ ] If a project conditionally excludes all source on non-Windows (`EnableDefaultCompileItems=false`), ensure the `<ProjectReference>` carries the same platform condition so the stub still compiles
- **Verification commands**:
  - `dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release /p:EnableWindowsTargeting=true`
  - `dotnet build src/Meridian.Ui.Services/Meridian.Ui.Services.csproj -c Release /p:EnableWindowsTargeting=true`
  - `grep -n "<Compile Include=" src/Meridian.Ui.Services/Meridian.Ui.Services.csproj` (should return no results after fix; the `=` distinguishes the XML element from prose comments that mention it)
- **Source issue**: PR "Fix CS0433 type ambiguity errors breaking WPF desktop build"
- **Status**: fixed
- **Fixed in**: `src/Meridian.Ui.Services/Meridian.Ui.Services.csproj` — replaced the entire `<Compile Include>` block sourcing Contracts files with a single `<ProjectReference Include="..\Meridian.Contracts\Meridian.Contracts.csproj" />`
