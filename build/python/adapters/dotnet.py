from pathlib import Path

from core.utils import run_command


class DotnetAdapter:
    def __init__(self, root: Path, log_file: Path, verbose: bool) -> None:
        self.root = root
        self.log_file = log_file
        self.verbose = verbose

    def restore(self, project: str) -> tuple[int, str, float]:
        return run_command(["dotnet", "restore", project], self.root, self.log_file, self.verbose)

    def build(self, project: str, configuration: str) -> tuple[int, str, float]:
        return run_command(
            ["dotnet", "build", project, "-c", configuration, "--no-restore"],
            self.root,
            self.log_file,
            self.verbose,
        )

    def test(self, project: str) -> tuple[int, str, float]:
        return run_command(
            ["dotnet", "test", project, "--no-build", "--logger", "console;verbosity=normal"],
            self.root,
            self.log_file,
            self.verbose,
        )
