# Central Package Management (CPM) Guide

## Overview

This repository uses **Central Package Management (CPM)** to ensure consistent package versions across all projects in the solution. All package versions are defined centrally in `Directory.Packages.props`.

## Why Central Package Management?

1. **Consistency**: All projects use the same version of each package
2. **Maintainability**: Update versions in one place instead of multiple .csproj files
3. **Clarity**: Easy to see all dependencies and their versions at a glance
4. **Safety**: Prevents version conflicts between projects

## Configuration

### Directory.Packages.props

This file at the repository root defines all package versions:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup Label="Example">
    <PackageVersion Include="Serilog" Version="4.3.0" />
    <PackageVersion Include="System.Text.Json" Version="10.0.2" />
  </ItemGroup>
</Project>
```

### Project Files (.csproj, .fsproj)

Project files reference packages **WITHOUT** version numbers:

```xml
<!-- ✅ CORRECT -->
<ItemGroup>
  <PackageReference Include="Serilog" />
  <PackageReference Include="System.Text.Json" />
</ItemGroup>

<!-- ❌ INCORRECT - Will cause NU1008 error -->
<ItemGroup>
  <PackageReference Include="Serilog" Version="4.3.0" />
  <PackageReference Include="System.Text.Json" Version="10.0.2" />
</ItemGroup>
```

## Common Error: NU1008

### Error Message

```
error NU1008: Projects that use central package version management should not define 
the version on the PackageReference items but on the PackageVersion items: 
PackageName1;PackageName2;...
```

### Cause

A project file has `Version` attributes on `<PackageReference>` items when CPM is enabled.

### Fix

1. **Remove version attributes** from all `<PackageReference>` items in the project file
2. **Add missing packages** to `Directory.Packages.props` if they don't exist there
3. **Verify** the build succeeds: `dotnet restore`

### Example Fix

**Before (incorrect):**
```xml
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
<PackageReference Include="MaterialDesignThemes" Version="5.1.0" />
```

**After (correct):**
```xml
<PackageReference Include="CommunityToolkit.Mvvm" />
<PackageReference Include="MaterialDesignThemes" />
```

And ensure these exist in `Directory.Packages.props`:
```xml
<PackageVersion Include="CommunityToolkit.Mvvm" Version="8.2.2" />
<PackageVersion Include="MaterialDesignThemes" Version="5.2.0" />
```

## Adding a New Package

### Step 1: Add to Directory.Packages.props

```xml
<ItemGroup Label="YourCategory">
  <PackageVersion Include="NewPackage" Version="1.0.0" />
</ItemGroup>
```

### Step 2: Reference in Project File

```xml
<ItemGroup>
  <PackageReference Include="NewPackage" />
</ItemGroup>
```

### Step 3: Restore and Verify

```bash
dotnet restore
dotnet build
```

## Verification Script

To verify all projects comply with CPM:

```bash
# Check for any PackageReference items with Version attributes
find . -name "*.csproj" -o -name "*.fsproj" | \
  grep -v "/obj/" | grep -v "/bin/" | \
  xargs grep -n 'PackageReference.*Version='
```

If this returns any results (except commented lines), those projects need to be fixed.

## Best Practices

1. **Always check** `Directory.Packages.props` before adding a package reference
2. **Reuse existing packages** when possible instead of adding new ones
3. **Group packages** logically in `Directory.Packages.props` using Label attributes
4. **Update versions** in `Directory.Packages.props`, never in project files
5. **Run `dotnet restore`** after modifying `Directory.Packages.props`

## Historical Context

### Issue: NU1008 in Meridian.Wpf (Feb 2026)

**Problem**: The nightly test workflow failed on Windows with NU1008 error because `Meridian.Wpf.csproj` had explicit version numbers on PackageReference items.

**Solution**: Removed all `Version` attributes from PackageReference items in the project file. The versions were already correctly defined in `Directory.Packages.props`.

**Reference**: [GitHub Actions Run 21609937328](https://github.com/rodoHasArrived/Meridian/actions/runs/21609937328/job/62276086336)

## Related Documentation

- [NuGet Central Package Management Docs](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management)
- [Directory.Packages.props](../../Directory.Packages.props) - Central version definitions
- [Build Observability Guide](build-observability.md) - CI/CD pipeline monitoring and diagnostics

## Troubleshooting

### Build fails with NU1008

1. Find the project mentioned in the error message
2. Open the `.csproj` or `.fsproj` file
3. Remove `Version="..."` from all `<PackageReference>` items
4. Verify the packages exist in `Directory.Packages.props`
5. Run `dotnet restore` to verify the fix

### Package version conflicts

If you see version conflicts, check:
1. Is the package listed multiple times in `Directory.Packages.props`?
2. Are there transitive dependencies causing conflicts?
3. Use `dotnet list package --include-transitive` to diagnose

### Different version needed for specific project

This is an anti-pattern with CPM. Consider:
1. Is this really necessary, or can all projects use the same version?
2. If truly necessary, you may need to disable CPM for that specific project
3. Document the reason for the exception

## See Also

- [DEPENDENCIES.md](../DEPENDENCIES.md) - Complete package list and documentation
- [Directory.Build.props](../../Directory.Build.props) - Common build properties
- [Provider Implementation Guide](provider-implementation.md) - Adding new data providers
