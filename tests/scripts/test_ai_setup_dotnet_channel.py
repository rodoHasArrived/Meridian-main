import json
import re
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
GLOBAL_JSON = REPO_ROOT / "global.json"
SETUP_SCRIPT = REPO_ROOT / "scripts" / "ai" / "setup.sh"
SETUP_AI_AGENT_SCRIPT = REPO_ROOT / "scripts" / "ai" / "setup-ai-agent.sh"


def _expected_channel() -> str:
    version = json.loads(GLOBAL_JSON.read_text(encoding="utf-8"))["sdk"]["version"]
    major, minor = version.split(".")[:2]
    return f"{major}.{minor}"


class AiSetupDotnetChannelTests(unittest.TestCase):
    """Guard against the AI setup scripts drifting away from global.json's SDK.

    The scripts must derive the .NET install channel from global.json rather than
    hardcoding a version, so agents always install the SDK the repo actually
    requires.
    """

    @classmethod
    def setUpClass(cls) -> None:
        cls.setup = SETUP_SCRIPT.read_text(encoding="utf-8")
        cls.setup_ai_agent = SETUP_AI_AGENT_SCRIPT.read_text(encoding="utf-8")
        cls.expected_channel = _expected_channel()

    def test_global_json_targets_net10_or_later(self) -> None:
        major = int(self.expected_channel.split(".")[0])
        self.assertGreaterEqual(major, 10)

    def test_setup_derives_channel_from_global_json(self) -> None:
        self.assertIn("dotnet_channel_from_global_json", self.setup)
        self.assertIn(
            'DOTNET_CHANNEL="$(dotnet_channel_from_global_json)"', self.setup
        )

    def test_setup_ai_agent_derives_channel_from_global_json(self) -> None:
        self.assertIn("dotnet_channel_from_global_json", self.setup_ai_agent)
        self.assertIn(
            'bash /tmp/dotnet-install.sh --channel "$channel"', self.setup_ai_agent
        )

    def test_scripts_do_not_hardcode_a_stale_channel(self) -> None:
        # No literal `--channel 9.0` / `--channel 8.0` style flags should remain;
        # the channel must come from the global.json-derived variable.
        pattern = re.compile(r"--channel\s+\d+\.\d+")
        for name, content in (
            ("setup.sh", self.setup),
            ("setup-ai-agent.sh", self.setup_ai_agent),
        ):
            self.assertIsNone(
                pattern.search(content),
                msg=f"{name} hardcodes a literal --channel version",
            )


if __name__ == "__main__":
    unittest.main()
