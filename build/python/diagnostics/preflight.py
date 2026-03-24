import os
import shutil
from pathlib import Path


def run_preflight(root: Path) -> tuple[bool, list[str]]:
    messages = []
    required = ["dotnet", "git"]
    missing = [tool for tool in required if shutil.which(tool) is None]
    if missing:
        messages.append(f"Missing tools: {', '.join(missing)}")

    # Skip config check in CI environments where the config file isn't needed for build
    is_ci = os.getenv("CI") == "true" or os.getenv("GITHUB_ACTIONS") == "true"
    if not is_ci:
        config = root / "config" / "appsettings.json"
        if not config.exists():
            messages.append("config/appsettings.json missing (run make setup-config)")

    return len(messages) == 0, messages
