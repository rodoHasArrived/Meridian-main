# Desktop App XAML Compiler Errors

> **Note: Historical Reference**
>
> The UWP desktop application (`Meridian.Uwp`) has been fully removed from the codebase. WPF is the sole desktop client.
> This document is retained as a historical reference for XAML compiler troubleshooting. The general diagnostic steps
> and WindowsAppSDK version guidance may still be useful for WPF XAML compilation issues, but UWP-specific paths
> and project references below no longer apply.

## Issue: XamlCompiler.exe exits with code 1

### Symptoms
- Build fails during XAML compilation phase with error: `MSB3073: The command "XamlCompiler.exe" exited with code 1`
- Error occurs across all platforms (x64, arm64) and build configurations (Debug, Release)
- XAML compiler provides no detailed error output, making diagnosis difficult
- Build log shows: `[4/5] Compiling XAML resources...` followed by immediate failure
- Affects builds with WindowsAppSDK 1.7.x preview releases

### Root Causes

#### 1. Preview/Experimental WindowsAppSDK Versions
**Problem**: Using preview or experimental builds of WindowsAppSDK (e.g., `1.7.250310001`) with .NET 9 can cause XAML compiler instability.

**Version Number Format**:
- Stable releases: `1.7.251107005` (1.7.7 from November 2025)
- Preview releases: `1.7.250310001` (1.7.0 preview from March 2025)
- The middle digits (2503 vs 2511) indicate the year/month of release

**Solution**: Downgrade to the latest stable WindowsAppSDK 1.7.x release.

```xml
<!-- Directory.Packages.props -->
<ItemGroup Label="WinUI / Desktop">
  <!-- Use stable 1.7.7 release instead of preview -->
  <PackageVersion Include="Microsoft.WindowsAppSDK" Version="1.7.251107005" />
</ItemGroup>
```

#### 2. Missing Explicit TargetPlatformVersion
**Problem**: MSBuild and the XAML compiler may fail with ambiguous platform targeting when `TargetPlatformVersion` is not explicitly set in the project file.

**Solution**: Add explicit `TargetPlatformVersion` property to match your `TargetFramework`.

```xml
<!-- Example .csproj (previously applied to Meridian.Uwp, now removed) -->
<PropertyGroup Condition="'$(IsWindows)' == 'true'">
  <TargetFramework>net9.0-windows10.0.19041.0</TargetFramework>
  <TargetPlatformVersion>10.0.19041.0</TargetPlatformVersion>
  <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
</PropertyGroup>
```

#### 3. Duplicate Path Segments in IntermediateOutputPath (FIXED)
**Problem**: When building with a RuntimeIdentifier specified (e.g., `dotnet publish -r win-x64`), the XAML compiler receives paths with duplicate TargetFramework\RuntimeIdentifier segments:

```
obj\x64\Debug\net9.0-windows10.0.19041.0\win-x64\net9.0-windows10.0.19041.0\win-x64\\input.json
```

This occurs because MSBuild automatically appends these to intermediate paths, and the XAML compiler adds them again.

**Solution**: Disable automatic path appending in the project file.

```xml
<!-- Example .csproj (previously applied to Meridian.Uwp, now removed) -->
<PropertyGroup Condition="'$(IsWindows)' == 'true'">
  <!-- Prevent MSBuild from appending RuntimeIdentifier and TargetFramework to paths -->
  <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
  <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
</PropertyGroup>
```

**Status**: Fixed in commit 85a8861 (February 2026).

**References**:
- [Microsoft Docs: Change build output directory](https://learn.microsoft.com/en-us/visualstudio/ide/how-to-change-the-build-output-directory)
- GitHub Actions run: 21619579668

#### 4. .NET 9 Compatibility Issues
**Problem**: WindowsAppSDK 1.7.x was primarily tested with .NET 6/7/8. .NET 9 support is indirect and may require:
- Updated Visual Studio (17.12+)
- Explicit SDK version targeting
- Stable (not preview) WindowsAppSDK packages

**Recommendation**: For production use with .NET 9, consider:
- Using WindowsAppSDK 1.8+ when available (designed for .NET 9)
- Staying on .NET 8 LTS with WindowsAppSDK 1.7.x
- Ensuring all tooling is up-to-date

### Diagnostic Steps

1. **Check WindowsAppSDK Version**
   ```bash
   # Look in Directory.Packages.props
   grep "WindowsAppSDK" Directory.Packages.props
   ```
   
   Ensure you're using a stable release (e.g., `1.7.251107005` or higher, not `1.7.250310001`).

2. **Verify Project Targeting**
   ```bash
   # Check project file for explicit platform version
   grep -A 3 "TargetFramework" src/Meridian.Wpf/Meridian.Wpf.csproj
   ```

   Ensure both `TargetFramework` and `TargetPlatformVersion` are set.

3. **Review XAML Files**
   ```bash
   # Validate XAML syntax
   python3 -c "
   import os
   import xml.etree.ElementTree as ET

   for root, dirs, files in os.walk('src/Meridian.Wpf'):
       for file in files:
           if file.endswith('.xaml'):
               filepath = os.path.join(root, file)
               try:
                   ET.parse(filepath)
               except Exception as e:
                   print(f'Error in {filepath}: {e}')
   "
   ```

### Quick Fix Checklist

- [ ] Update WindowsAppSDK to stable 1.7.7+ (Version `1.7.251107005` or higher)
- [ ] Add explicit `TargetPlatformVersion` to project file
- [ ] Disable automatic path appending (add `AppendRuntimeIdentifierToOutputPath=false` and `AppendTargetFrameworkToOutputPath=false`)
- [ ] Verify .NET SDK version (9.0.310+ for .NET 9)
- [ ] Ensure Visual Studio is up-to-date (17.12+ for .NET 9 projects)
- [ ] Clean build directories: `dotnet clean && rd /s /q bin obj`
- [ ] Rebuild with verbose logging: `dotnet build -v detailed`

### Related Resources

- [WindowsAppSDK Release Channels](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels)
- [WindowsAppSDK Downloads](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)
- [WindowsAppSDK GitHub Issues](https://github.com/microsoft/WindowsAppSDK/issues)
- [.NET 9 Version Requirements](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/9.0/version-requirements)
- [UWP Development Roadmap](../archived/uwp-development-roadmap.md) (historical reference)

### Prevention

- **Always use stable releases** for production builds
- **Pin package versions** in `Directory.Packages.props` to avoid automatic upgrades
- **Test after package updates** with local builds before committing
- **Monitor release notes** for breaking changes in WindowsAppSDK updates
- **Use CI/CD** to catch build issues early

### Contact

If this issue persists after applying these fixes:
1. Check [GitHub Issues](https://github.com/microsoft/WindowsAppSDK/issues) for similar problems
2. Review CI/CD logs for additional error context
3. Consider reporting a new issue with full build logs and system information
