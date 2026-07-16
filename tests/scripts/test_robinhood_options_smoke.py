from __future__ import annotations

import re
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "scripts" / "dev" / "robinhood-options-smoke.ps1"


class RobinhoodOptionsSmokeScriptTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.script = SCRIPT_PATH.read_text(encoding="utf-8")
        function_start = cls.script.index("function Invoke-DesktopBuild")
        function_end = cls.script.index("\nfunction ", function_start + 1)
        cls.build_function = cls.script[function_start:function_end]

    def test_restore_uses_isolation_without_overriding_project_target_frameworks(self) -> None:
        restore_match = re.search(
            r"& dotnet restore .*?@\((.*?)\)",
            self.build_function,
            flags=re.DOTALL,
        )

        self.assertIsNotNone(restore_match)
        restore_arguments = restore_match.group(1)
        self.assertIn("Get-MeridianBuildArguments", restore_arguments)
        self.assertIn("-IsolationKey $BuildIsolationKey", restore_arguments)
        self.assertIn("-EnableFullWpfBuild", restore_arguments)
        self.assertNotIn("-TargetFramework", restore_arguments)

    def test_build_pins_the_wpf_framework_against_the_matching_restore(self) -> None:
        build_match = re.search(
            r"& dotnet build .*?@\((.*?)\)",
            self.build_function,
            flags=re.DOTALL,
        )

        self.assertIsNotNone(build_match)
        build_arguments = build_match.group(1)
        self.assertIn("Get-MeridianBuildArguments", build_arguments)
        self.assertIn("-IsolationKey $BuildIsolationKey", build_arguments)
        self.assertIn("-TargetFramework $Framework", build_arguments)
        self.assertIn("-EnableFullWpfBuild", build_arguments)
        self.assertIn("--no-restore", self.build_function)

    def test_fixture_launch_uses_the_development_environment(self) -> None:
        fixture_environment = re.search(
            r'if \(\$FixtureMode\) \{\s*\$startProcessArgs\["Environment"\] = @\{(.*?)\}\s*\}',
            self.script,
            flags=re.DOTALL,
        )

        self.assertIsNotNone(fixture_environment)
        environment = fixture_environment.group(1)
        self.assertIn('MDC_FIXTURE_MODE       = "1"', environment)
        self.assertIn('DOTNET_ENVIRONMENT     = "Development"', environment)
        self.assertIn('ASPNETCORE_ENVIRONMENT = "Development"', environment)

    def test_startup_continues_through_optional_development_authentication(self) -> None:
        self.assertIn('ContinuationInvoked = $false', self.script)
        self.assertIn('Find-ElementByExactName -Root $activeRoot -Name "Continue without credentials"', self.script)
        self.assertIn('Invoke-OrClickElement -Element $continueWithoutCredentials', self.script)
        self.assertIn('[System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)', self.script)
        self.assertIn('$root = Wait-ForShellReady', self.script)

    def test_empty_operating_context_seeds_samples_before_entering(self) -> None:
        self.assertIn('$selectionMarkers = @("Operating Context",', self.script)
        self.assertIn('@("Seed Sample Contexts", "Seed Sample Profiles")', self.script)
        self.assertIn('Enter workstation control did not become enabled after seeding sample contexts.', self.script)
        self.assertIn('ContextSeedInvoked = $false', self.script)
        self.assertIn('ContextEnterInvoked = $false', self.script)
        self.assertIn('Enabled operating context detected. Entering the workstation.', self.script)

    def test_primary_shell_command_palette_fallback_is_automation_safe(self) -> None:
        self.assertIn('function Invoke-CommandPaletteNavigation', self.script)
        self.assertIn('[MeridianSmokeNative]::SetForegroundWindow($Process.MainWindowHandle)', self.script)
        self.assertIn('function Find-FirstVisibleElementByAutomationIds', self.script)
        self.assertIn('function Find-FirstVisibleElementByNames', self.script)
        self.assertIn('AutomationIds @("ShellCommandPaletteButton")', self.script)
        self.assertIn('Names @("Search", "Open Command Palette")', self.script)
        self.assertIn('-not $element.Current.IsOffscreen -and $element.Current.IsEnabled', self.script)
        self.assertIn('Opening command palette via automation id', self.script)
        self.assertIn('Invoke-OrClickElement -Element $paletteButton', self.script)
        self.assertIn('$Root = Get-WindowAutomationRoot -Process $Process', self.script)
        self.assertIn('Send-WindowKeys -Process $Process -Keys "^k"', self.script)
        self.assertIn('AutomationIds @("CommandPaletteInput")', self.script)
        self.assertIn('AutomationIds @("CommandPaletteResults")', self.script)
        self.assertIn('-not $candidate.Current.IsOffscreen', self.script)
        self.assertIn('Set-ValuePatternText -Element $paletteInput -Text $PageTag', self.script)
        self.assertIn('Find-ElementByExactName -Root $results -Name $ResultName', self.script)
        self.assertIn('function Click-ElementAtCenter', self.script)
        self.assertIn('Click-ElementAtCenter -Element $paletteResult', self.script)
        self.assertIn(
            '$root = Get-WindowAutomationRoot -Process $process',
            self.script,
        )
        self.assertIn('PaletteResultName = "Add provider wizard"', self.script)

    def test_seeded_state_starts_on_workspace_home_before_deep_navigation(self) -> None:
        self.assertIn('function Get-WorkspaceShellPageTag', self.script)
        self.assertIn('"data-operations" { return "DataShell" }', self.script)
        self.assertIn('$seedPageTag = Get-WorkspaceShellPageTag -WorkspaceId $Case.WorkspaceId', self.script)
        self.assertIn('-PageTag $seedPageTag', self.script)
        self.assertIn('-PageTitle $seedPageTitle', self.script)

    def test_each_case_forwards_the_supported_desktop_page_launch_contract_after_startup(self) -> None:
        self.assertIn('function Invoke-DesktopPageNavigation', self.script)
        self.assertIn('ArgumentList     = @("--page=$PageTag")', self.script)
        self.assertIn('$navigationProcess.WaitForExit(10000)', self.script)
        self.assertIn(
            'Invoke-DesktopPageNavigation -ExecutablePath $ExecutablePath -PageTag $Case.PageTag -FixtureMode $FixtureMode',
            self.script,
        )

    def test_workspace_activation_skips_reselecting_an_active_shell(self) -> None:
        activation_start = self.script.index('function Try-ActivateWorkspaceShell')
        activation_end = self.script.index('\nfunction ', activation_start + 1)
        activation_function = self.script[activation_start:activation_end]
        marker_check = activation_function.index('Find-FirstElementByNames -Root $Root -Names @($shellMarker)')
        tile_lookup = activation_function.index('$tile = Find-FirstElementByNames')
        self.assertLess(marker_check, tile_lookup)


if __name__ == "__main__":
    unittest.main()
