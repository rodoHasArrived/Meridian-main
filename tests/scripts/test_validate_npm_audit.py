import datetime as _dt
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[2] / "build" / "scripts" / "ci" / "validate-npm-audit.py"
SPEC = importlib.util.spec_from_file_location("validate_npm_audit", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)

REPO_REGISTER = (
    Path(__file__).resolve().parents[2]
    / "build"
    / "config"
    / "security"
    / "npm-audit-accepted-advisories.json"
)

GHSA = "GHSA-qwww-vcr4-c8h2"


def audit_payload(vulnerabilities: dict, counts: dict | None = None) -> dict:
    return {
        "auditReportVersion": 2,
        "vulnerabilities": vulnerabilities,
        "metadata": {"vulnerabilities": counts or {}},
    }


def advisory(name: str, severity: str = "high", ghsa: str = GHSA) -> dict:
    return {
        "source": 1124282,
        "name": name,
        "severity": severity,
        "title": "example advisory",
        "url": f"https://github.com/advisories/{ghsa}",
        "range": ">=7.12.0 <8.3.0",
    }


def acceptance(
    ghsa: str = GHSA,
    package: str = "react-router",
    max_severity: str = "high",
    review_by: str = "2026-10-28",
) -> dict:
    return {
        "id": "KV-2026-002",
        "ghsa": ghsa,
        "package": package,
        "max_severity": max_severity,
        "reason": "unreachable code path",
        "owner": "core-team",
        "accepted_on": "2026-07-28",
        "review_by": review_by,
    }


class ValidateNpmAuditTests(unittest.TestCase):
    def run_gate(
        self,
        audit: dict | str,
        register: dict,
        today: str = "2026-07-28",
        fail_level: str = "high",
        write_audit: bool = True,
    ) -> int:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            audit_path = root / "npm-audit.json"
            if write_audit:
                content = audit if isinstance(audit, str) else json.dumps(audit)
                audit_path.write_text(content, encoding="utf-8")
            accepted_path = root / "accepted.json"
            accepted_path.write_text(json.dumps(register), encoding="utf-8")
            self.output_path = root / "gate.json"
            exit_code = MODULE.main(
                [
                    "--audit-json",
                    str(audit_path),
                    "--accepted",
                    str(accepted_path),
                    "--fail-level",
                    fail_level,
                    "--today",
                    today,
                    "--output",
                    str(self.output_path),
                ]
            )
            self.decision = json.loads(self.output_path.read_text(encoding="utf-8"))
            return exit_code

    def test_passes_on_clean_audit_with_empty_register(self):
        exit_code = self.run_gate(audit_payload({}), {"accepted": []})
        self.assertEqual(exit_code, 0)
        self.assertTrue(self.decision["passed"])

    def test_fails_on_unaccepted_high_advisory(self):
        audit = audit_payload(
            {"react-router": {"severity": "high", "isDirect": False, "via": [advisory("react-router")]}}
        )
        exit_code = self.run_gate(audit, {"accepted": []})
        self.assertEqual(exit_code, 1)
        self.assertEqual(self.decision["unaccepted"][0]["ghsa"], GHSA)

    def test_passes_when_advisory_is_accepted_and_unexpired(self):
        audit = audit_payload(
            {
                "react-router": {"severity": "high", "isDirect": False, "via": [advisory("react-router")]},
                "react-router-dom": {"severity": "high", "isDirect": True, "via": ["react-router"]},
            }
        )
        exit_code = self.run_gate(audit, {"accepted": [acceptance()]})
        self.assertEqual(exit_code, 0)
        self.assertTrue(self.decision["passed"])
        self.assertEqual(len(self.decision["accepted_in_use"]), 1)

    def test_chained_package_resolves_to_the_root_advisory(self):
        audit = audit_payload(
            {
                "react-router": {"severity": "high", "isDirect": False, "via": [advisory("react-router")]},
                "react-router-dom": {"severity": "high", "isDirect": True, "via": ["react-router"]},
            }
        )
        self.run_gate(audit, {"accepted": [acceptance()]})
        flagged_through = self.decision["accepted_in_use"][0]["flagged_through"]
        self.assertIn("react-router", flagged_through)
        self.assertIn("react-router-dom", flagged_through)

    def test_fails_when_acceptance_is_expired(self):
        audit = audit_payload(
            {"react-router": {"severity": "high", "isDirect": False, "via": [advisory("react-router")]}}
        )
        exit_code = self.run_gate(audit, {"accepted": [acceptance(review_by="2026-07-01")]})
        self.assertEqual(exit_code, 1)
        self.assertIn("expired", self.decision["unaccepted"][0]["failure"])

    def test_fails_when_advisory_severity_exceeds_accepted_ceiling(self):
        audit = audit_payload(
            {
                "react-router": {
                    "severity": "critical",
                    "isDirect": False,
                    "via": [advisory("react-router", severity="critical")],
                }
            }
        )
        exit_code = self.run_gate(audit, {"accepted": [acceptance(max_severity="high")]})
        self.assertEqual(exit_code, 1)
        self.assertIn("ceiling", self.decision["unaccepted"][0]["failure"])

    def test_fails_when_acceptance_is_stale(self):
        exit_code = self.run_gate(audit_payload({}), {"accepted": [acceptance()]})
        self.assertEqual(exit_code, 1)
        self.assertEqual(self.decision["stale_acceptances"], ["KV-2026-002"])

    def test_fails_closed_on_missing_audit_report(self):
        exit_code = self.run_gate(audit_payload({}), {"accepted": []}, write_audit=False)
        self.assertEqual(exit_code, 1)
        self.assertFalse(self.decision["passed"])

    def test_fails_closed_on_invalid_audit_json(self):
        exit_code = self.run_gate("not-json", {"accepted": []})
        self.assertEqual(exit_code, 1)
        self.assertFalse(self.decision["passed"])

    def test_fails_closed_on_npm_error_payload(self):
        exit_code = self.run_gate(
            {"error": {"code": "ENOAUDIT", "summary": "registry unavailable"}},
            {"accepted": []},
        )
        self.assertEqual(exit_code, 1)
        self.assertFalse(self.decision["passed"])

    def test_fails_closed_on_unsupported_report_version(self):
        exit_code = self.run_gate({"auditReportVersion": 1, "advisories": {}}, {"accepted": []})
        self.assertEqual(exit_code, 1)

    def test_moderate_advisories_do_not_fail_a_high_gate(self):
        audit = audit_payload(
            {
                "some-package": {
                    "severity": "moderate",
                    "isDirect": True,
                    "via": [advisory("some-package", severity="moderate", ghsa="GHSA-2222-3333-4444")],
                }
            }
        )
        exit_code = self.run_gate(audit, {"accepted": []})
        self.assertEqual(exit_code, 0)

    def test_rejects_register_missing_required_fields(self):
        broken = acceptance()
        del broken["review_by"]
        exit_code = self.run_gate(audit_payload({}), {"accepted": [broken]})
        self.assertEqual(exit_code, 1)

    def test_repository_register_is_structurally_valid(self):
        """The register's invariants, not one specific entry.

        This previously pinned the KV-2026-002 react-router acceptance. That made retiring an
        acceptance — the outcome the policy actually wants when upstream ships a fix — fail the
        script-test lane, so the test held the register at its 2026-07-28 contents. Assert what
        must always hold instead: the register parses, points at the human registry, and every
        entry it does contain is well-formed and unexpired. An empty register is a valid and
        preferred state.
        """
        register = json.loads(REPO_REGISTER.read_text(encoding="utf-8"))
        entries = MODULE.load_acceptances(REPO_REGISTER)
        self.assertEqual(register["registry"], "docs/security/known-vulnerabilities.md")
        self.assertEqual(len(entries), len(register["accepted"]))

        today = _dt.date.today()
        for entry in entries:
            for field in MODULE.REQUIRED_ACCEPTANCE_FIELDS:
                self.assertIn(field, entry)
                self.assertTrue(str(entry[field]).strip(), f"{entry['id']}.{field} is blank")
            self.assertEqual(entry["ghsa"], MODULE.canonical_ghsa(entry["ghsa"]))
            review_by = MODULE.parse_date(entry["review_by"], "review_by", entry["id"])
            self.assertGreaterEqual(
                review_by,
                today,
                f"acceptance {entry['id']} expired on {entry['review_by']}; "
                "retire it or renew the accepted-risk review.",
            )


if __name__ == "__main__":
    unittest.main()
