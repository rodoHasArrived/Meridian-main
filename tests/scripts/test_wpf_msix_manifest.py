import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
MANIFEST_PATH = REPO_ROOT / "src" / "Meridian.Wpf" / "Package.appxmanifest"

NS = {
    "appx": "http://schemas.microsoft.com/appx/manifest/foundation/windows10",
    "uap": "http://schemas.microsoft.com/appx/manifest/uap/windows10",
    "desktop7": "http://schemas.microsoft.com/appx/manifest/desktop/windows10/7",
    "desktop10": "http://schemas.microsoft.com/appx/manifest/desktop/windows10/10",
    "rescap": "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities",
}


class WpfMsixManifestTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tree = ET.parse(MANIFEST_PATH)
        self.root = self.tree.getroot()

    def test_manifest_declares_full_trust_desktop_app(self) -> None:
        identity = self.root.find("appx:Identity", NS)
        self.assertIsNotNone(identity)
        self.assertEqual("Meridian.Desktop", identity.attrib["Name"])
        self.assertEqual("CN=Meridian", identity.attrib["Publisher"])

        application = self.root.find("appx:Applications/appx:Application", NS)
        self.assertIsNotNone(application)
        self.assertEqual("$targetnametoken$.exe", application.attrib["Executable"])
        self.assertEqual("Windows.FullTrustApplication", application.attrib["EntryPoint"])

        capability = self.root.find("appx:Capabilities/rescap:Capability", NS)
        self.assertIsNotNone(capability)
        self.assertEqual("runFullTrust", capability.attrib["Name"])

    def test_manifest_creates_desktop_shortcut(self) -> None:
        shortcut = self.root.find(
            "appx:Applications/appx:Application/appx:Extensions/"
            "desktop7:Extension/desktop7:Shortcut",
            NS,
        )
        self.assertIsNotNone(shortcut)
        self.assertEqual("$(Desktop)\\Meridian.lnk", shortcut.attrib["File"])
        self.assertEqual("Assets\\app.ico", shortcut.attrib["Icon"])
        self.assertEqual(
            "Meridian",
            shortcut.attrib[f"{{{NS['desktop10']}}}DisplayName"],
        )

    def test_shortcut_min_os_contract_is_explicit(self) -> None:
        device_family = self.root.find("appx:Dependencies/appx:TargetDeviceFamily", NS)
        self.assertIsNotNone(device_family)
        self.assertEqual("Windows.Desktop", device_family.attrib["Name"])
        self.assertEqual("10.0.19645.0", device_family.attrib["MinVersion"])


if __name__ == "__main__":
    unittest.main()
