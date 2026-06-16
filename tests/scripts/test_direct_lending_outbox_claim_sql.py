from __future__ import annotations

import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
STORE_PATH = (
    REPO_ROOT
    / "src"
    / "Meridian.Storage"
    / "DirectLending"
    / "PostgresDirectLendingStateStore.Operations.cs"
)
README_PATH = REPO_ROOT / "src" / "Meridian.Storage" / "README.md"


class DirectLendingOutboxClaimSqlTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.store_source = STORE_PATH.read_text(encoding="utf-8")
        cls.storage_readme = README_PATH.read_text(encoding="utf-8")

    def test_pending_outbox_poll_claims_rows_with_postgres_skip_locked(self) -> None:
        self.assertIn("with next_messages as", self.store_source)
        self.assertIn("for update skip locked", self.store_source)
        self.assertIn('update {Qualified("outbox_message")} message', self.store_source)
        self.assertIn("where message.outbox_message_id = next_messages.outbox_message_id", self.store_source)

    def test_pending_outbox_poll_sets_visibility_lease_before_returning_rows(self) -> None:
        self.assertIn("set visible_after = now() + interval '5 minutes'", self.store_source)
        self.assertIn("returning message.outbox_message_id", self.store_source)
        self.assertNotIn("select outbox_message_id,\n                   topic,\n                   message_key", self.store_source)

    def test_storage_readme_documents_multi_host_outbox_claiming(self) -> None:
        self.assertIn("FOR UPDATE SKIP LOCKED", self.storage_readme)
        self.assertIn("short lease", self.storage_readme)
        self.assertIn("multiple hosted workers", self.storage_readme)


if __name__ == "__main__":
    unittest.main()
