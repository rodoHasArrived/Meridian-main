param(
    [Parameter(Mandatory = $true)][string]$WorkspaceName,
    [Parameter(Mandatory = $true)][string]$DomainArea,
    [string]$PrimaryGridType = 'DataGrid',
    [string[]]$DetailTabs = @('Summary', 'Evidence'),
    [string[]]$Commands = @('Refresh'),
    [string[]]$ServicesNeeded = @(),
    [switch]$Apply,
    [string]$MarkdownPath
)

. "$PSScriptRoot/_codex-scan-lib.ps1"

$root = Get-CodexRepoRoot
Set-Location $root
$safeName = ($WorkspaceName -replace '[^A-Za-z0-9]', '')
if (-not $safeName) {
    throw 'WorkspaceName must contain at least one letter or digit.'
}

$baseDir = "src/Meridian.Wpf/Workstation/$safeName"
$files = @(
    "$baseDir/${safeName}WorkspaceView.xaml",
    "$baseDir/${safeName}WorkspaceView.xaml.cs",
    "$baseDir/${safeName}WorkspaceViewModel.cs",
    "$baseDir/I${safeName}WorkspaceService.cs",
    "tests/Meridian.Wpf.Tests/Workstation/${safeName}WorkspaceViewModelTests.cs"
)

$plan = New-Object System.Collections.Generic.List[string]
$plan.Add("# Desktop Workspace Generator Plan")
$plan.Add("")
$plan.Add("- Workspace: $WorkspaceName")
$plan.Add("- Domain area: $DomainArea")
$plan.Add("- Primary grid: $PrimaryGridType")
$plan.Add("- Detail tabs: $($DetailTabs -join ', ')")
$plan.Add("- Commands: $($Commands -join ', ')")
$plan.Add("- Services needed: $($ServicesNeeded -join ', ')")
$plan.Add("- Mode: $(if ($Apply) { 'apply' } else { 'dry-run' })")
$plan.Add("")
$plan.Add("## Files")
foreach ($file in $files) {
    $plan.Add("- ``$file``")
}
$plan.Add("")
$plan.Add("## Required follow-up")
$plan.Add("- Wire shell navigation and DI registration manually after reviewing existing patterns.")
$plan.Add("- Replace placeholder DTOs with shared read models before production use.")
$plan.Add("- Run focused WPF tests and MVVM/resource scans.")

if ($Apply) {
    foreach ($file in $files) {
        $fullPath = Join-Path $root $file
        $parent = Split-Path -Parent $fullPath
        if (-not (Test-Path -LiteralPath $parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }
    }

    Set-Content -LiteralPath (Join-Path $root "$baseDir/${safeName}WorkspaceView.xaml") -Encoding UTF8 -Value @"
<UserControl x:Class="Meridian.Wpf.Workstation.$safeName.${safeName}WorkspaceView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <TextBlock Text="$WorkspaceName" />
    </Grid>
</UserControl>
"@
    Set-Content -LiteralPath (Join-Path $root "$baseDir/${safeName}WorkspaceView.xaml.cs") -Encoding UTF8 -Value @"
namespace Meridian.Wpf.Workstation.$safeName;

public partial class ${safeName}WorkspaceView
{
    public ${safeName}WorkspaceView()
    {
        InitializeComponent();
    }
}
"@
    Set-Content -LiteralPath (Join-Path $root "$baseDir/${safeName}WorkspaceViewModel.cs") -Encoding UTF8 -Value @"
namespace Meridian.Wpf.Workstation.$safeName;

public sealed class ${safeName}WorkspaceViewModel
{
    public string Title => "$WorkspaceName";
}
"@
    Set-Content -LiteralPath (Join-Path $root "$baseDir/I${safeName}WorkspaceService.cs") -Encoding UTF8 -Value @"
namespace Meridian.Wpf.Workstation.$safeName;

public interface I${safeName}WorkspaceService
{
}
"@
    Set-Content -LiteralPath (Join-Path $root "tests/Meridian.Wpf.Tests/Workstation/${safeName}WorkspaceViewModelTests.cs") -Encoding UTF8 -Value @"
using FluentAssertions;
using Meridian.Wpf.Workstation.$safeName;

namespace Meridian.Wpf.Tests.Workstation;

public sealed class ${safeName}WorkspaceViewModelTests
{
    [Fact]
    public void Title_UsesWorkspaceName()
    {
        var viewModel = new ${safeName}WorkspaceViewModel();

        viewModel.Title.Should().Be("$WorkspaceName");
    }
}
"@
}

foreach ($line in $plan) {
    Write-Host $line
}

if ($MarkdownPath) {
    $resolved = if ([System.IO.Path]::IsPathRooted($MarkdownPath)) { $MarkdownPath } else { Join-Path $root $MarkdownPath }
    $parent = Split-Path -Parent $resolved
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    Set-Content -LiteralPath $resolved -Value $plan -Encoding UTF8
    Write-Host "Wrote report: $resolved"
}

exit 0
