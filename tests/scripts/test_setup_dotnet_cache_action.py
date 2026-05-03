from __future__ import annotations

import unittest
from pathlib import Path


ACTION_PATH = Path(__file__).resolve().parents[2] / ".github" / "actions" / "setup-dotnet-cache" / "action.yml"


class SetupDotnetCacheActionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.action = ACTION_PATH.read_text(encoding="utf-8")

    def test_macos_apphost_lookup_runs_after_dotnet_is_available(self) -> None:
        print_index = self.action.find("- name: Print SDK version")
        configure_index = self.action.find("- name: Configure macOS .NET apphost lookup")
        cache_index = self.action.find("- name: Cache NuGet packages")

        self.assertNotEqual(print_index, -1)
        self.assertNotEqual(configure_index, -1)
        self.assertNotEqual(cache_index, -1)
        self.assertLess(print_index, configure_index)
        self.assertLess(configure_index, cache_index)

    def test_macos_apphost_lookup_exports_arch_specific_dotnet_roots(self) -> None:
        configure_index = self.action.find("- name: Configure macOS .NET apphost lookup")
        next_step_index = self.action.find("\n    - name:", configure_index + 1)
        step_block = self.action[configure_index: next_step_index if next_step_index != -1 else None]

        self.assertIn("if: ${{ runner.os == 'macOS' }}", step_block)
        self.assertIn('dotnet_path="$(command -v dotnet)"', step_block)
        self.assertIn("os.path.realpath", step_block)
        self.assertIn('dotnet_realpath="$(python3 -c', step_block)
        self.assertIn('dotnet_dir="$(cd "$(dirname "$dotnet_realpath")" && pwd -P)"', step_block)
        self.assertIn('dotnet_root="$dotnet_dir"', step_block)
        self.assertIn('if [[ ! -d "$dotnet_root/host/fxr" ]]; then', step_block)
        self.assertIn('parent_root="$(cd "$dotnet_dir/.." && pwd -P)"', step_block)
        self.assertIn('if [[ -d "$parent_root/host/fxr" ]]; then', step_block)
        self.assertIn('echo "::error::Resolved dotnet root', step_block)
        self.assertIn("dotnet --info", step_block)
        self.assertIn('echo "DOTNET_ROOT=$dotnet_root"', step_block)
        self.assertIn('echo "DOTNET_ROOT_ARM64=$dotnet_root"', step_block)
        self.assertIn('echo "DOTNET_ROOT_X64=$dotnet_root"', step_block)
        self.assertIn('} >> "$GITHUB_ENV"', step_block)
        self.assertIn("xUnit v3 launches the generated test apphost directly", step_block)
        self.assertIn("Resolve symlinks first", step_block)


if __name__ == "__main__":
    unittest.main()
