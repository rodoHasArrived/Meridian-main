import importlib.util
from pathlib import Path
from datetime import datetime, timezone
import unittest

MODULE = Path(__file__).resolve().parents[2] / "scripts/dev/alpaca_paper_order_probe.py"
spec = importlib.util.spec_from_file_location("paper_probe", MODULE)
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)
NOW = datetime(2026, 11, 10, 15, tzinfo=timezone.utc)


class FakePaper:
    def __init__(self, fill=False, dirty=False, wrong_account=False, *, missing_order_lookups=0,
                 lost_acknowledgement=False, lookup_broker_id="broker"):
        self.fill = fill; self.dirty = dirty; self.wrong_account = wrong_account
        self.calls = []; self.client_id = None; self.cleaned = False
        self.missing_order_lookups = missing_order_lookups
        self.order_lookups = 0
        self.cancel_requested = False
        self.lost_acknowledgement = lost_acknowledgement
        self.lookup_broker_id = lookup_broker_id

    def request(self, method, path, payload=None, **kwargs):
        self.calls.append((method, path, payload))
        if path == "/v2/account": return {"id": "other" if self.wrong_account else "dedicated"}
        if path == "/v2/clock": return {"is_open": True}
        if path.startswith("/v2/stocks/"): return {"quote": {"t": NOW.isoformat(), "bp": 99, "ap": 100}}
        if path == "/v2/positions":
            if self.dirty: return [{"symbol": "OTHER", "qty": "1"}]
            return [{"symbol": "SPY", "qty": "1"}] if self.fill and self.client_id and not self.cleaned else []
        if path == "/v2/orders?status=open": return []
        if method == "POST":
            if payload["side"] == "buy":
                self.client_id = payload["client_order_id"]
                if self.lost_acknowledgement:
                    raise TimeoutError("lost acknowledgement")
            else:
                self.cleaned = True
            return {"id": "broker"}
        if path.startswith("/v2/orders:by_client_order_id"):
            self.order_lookups += 1
            if self.missing_order_lookups and not self.cancel_requested:
                if self.order_lookups <= self.missing_order_lookups:
                    return None
                return {"id": self.lookup_broker_id, "client_order_id": self.client_id, "status": "new", "filled_qty": "0"}
            return {"id": self.lookup_broker_id, "client_order_id": self.client_id, "status": "filled" if self.fill else "canceled", "filled_qty": "1" if self.fill else "0"}
        if method == "DELETE":
            self.cancel_requested = True
            return None
        raise AssertionError((method, path))


class PaperProbeTests(unittest.TestCase):
    def test_non_filling_probe_proves_flat_without_cleanup_order(self):
        transport = FakePaper()
        self.assertEqual("verified-flat", probe.run_probe(transport, "dedicated", now=lambda: NOW, pause=lambda _: None)["status"])
        submitted = [payload for method, _, payload in transport.calls if method == "POST"]
        self.assertEqual(1, len(submitted)); self.assertEqual("49.50", submitted[0]["limit_price"])

    def test_probe_cancels_when_delayed_lookup_first_finds_working_order(self):
        for missing_lookups in (1, 29):
            with self.subTest(missing_lookups=missing_lookups):
                transport = FakePaper(missing_order_lookups=missing_lookups)
                result = probe.run_probe(transport, "dedicated", now=lambda: NOW, pause=lambda _: None)
                self.assertEqual("verified-flat", result["status"])
                cleanup = [method for method, path, _ in transport.calls
                           if path.startswith("/v2/orders:by_client_order_id") or path == "/v2/orders/broker"]
                self.assertEqual(["GET"] * (missing_lookups + 1) + ["DELETE", "GET"], cleanup)

    def test_lost_acknowledgement_cancels_delayed_order_without_resubmitting(self):
        transport = FakePaper(missing_order_lookups=1, lost_acknowledgement=True)
        with self.assertRaisesRegex(probe.ProbeRefused, "cleanup verified flat"):
            probe.run_probe(transport, "dedicated", now=lambda: NOW, pause=lambda _: None)
        mutations = [(method, path) for method, path, _ in transport.calls if method != "GET"]
        self.assertEqual([("POST", "/v2/orders"), ("DELETE", "/v2/orders/broker")], mutations)

    def test_cleanup_refuses_to_cancel_a_different_broker_order_id(self):
        transport = FakePaper(missing_order_lookups=1, lookup_broker_id="different")
        with self.assertRaisesRegex(probe.ProbeRefused, "inconsistent probe identity"):
            probe.run_probe(transport, "dedicated", now=lambda: NOW, pause=lambda _: None)
        self.assertFalse(any(method == "DELETE" for method, _, _ in transport.calls))

    def test_unexpected_fill_is_closed_and_flatness_is_verified(self):
        transport = FakePaper(fill=True)
        result = probe.run_probe(transport, "dedicated", now=lambda: NOW, pause=lambda _: None)
        self.assertEqual("1", result["filled_quantity"]); self.assertTrue(transport.cleaned)
        self.assertTrue(all(len(payload["client_order_id"]) <= 48 for method, _, payload in transport.calls if method == "POST"))

    def test_dirty_account_refuses_before_mutation(self):
        transport = FakePaper(dirty=True)
        with self.assertRaises(probe.ProbeRefused): probe.run_probe(transport, "dedicated", now=lambda: NOW)
        self.assertFalse(any(method != "GET" for method, _, _ in transport.calls))

    def test_wrong_account_refuses_before_mutation(self):
        transport = FakePaper(wrong_account=True)
        with self.assertRaises(probe.ProbeRefused): probe.run_probe(transport, "dedicated", now=lambda: NOW)
        self.assertFalse(any(method != "GET" for method, _, _ in transport.calls))

    def test_stale_quote_refuses_before_mutation(self):
        transport = FakePaper()
        with self.assertRaises(probe.ProbeRefused): probe.run_probe(transport, "dedicated", now=lambda: NOW.replace(minute=1))
        self.assertFalse(any(method != "GET" for method, _, _ in transport.calls))

    def test_transport_cannot_target_live_or_mutate_data(self):
        transport = probe.PaperTransport("synthetic-key", "synthetic-secret")
        with self.assertRaises(probe.ProbeRefused): transport.request("POST", "https://api.alpaca.markets/v2/orders")
        with self.assertRaises(probe.ProbeRefused): transport.request("POST", "/v2/orders", data=True)


if __name__ == "__main__": unittest.main()
