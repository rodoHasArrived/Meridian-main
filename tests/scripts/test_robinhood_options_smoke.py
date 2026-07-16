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

    def test_forwarded_page_launch_inherits_fixture_environment(self) -> None:
        self.assertIn('Invoke-ForwardedLaunch -ExecutablePath $ExecutablePath', self.script)
        self.assertIn('-FixtureMode $FixtureMode', self.script)
        forwarded_start = self.script.index('function Invoke-ForwardedLaunch')
        forwarded_end = self.script.index('\nfunction ', forwarded_start + 1)
        forwarded_function = self.script[forwarded_start:forwarded_end]
        self.assertIn('DOTNET_ENVIRONMENT     = "Development"', forwarded_function)
        self.assertIn('ASPNETCORE_ENVIRONMENT = "Development"', forwarded_function)


if __name__ == "__main__":
    unittest.main()
