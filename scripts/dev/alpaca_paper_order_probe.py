#!/usr/bin/env python3
"""Explicitly enabled, exclusive paper-only order probe. Never used by ordinary tests."""
from __future__ import annotations

import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
from decimal import Decimal, ROUND_DOWN
import hashlib
import json
import os
from pathlib import Path
import tempfile
import time
import urllib.error
import urllib.request
import uuid

PAPER = "https://paper-api.alpaca.markets"
DATA = "https://data.alpaca.markets"


class ProbeRefused(RuntimeError):
    """The requested probe cannot establish safe, exclusive paper scope."""


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise ProbeRefused("Provider redirects are refused.")


class PaperTransport:
    def __init__(self, key: str, secret: str):
        self._headers = {"APCA-API-KEY-ID": key, "APCA-API-SECRET-KEY": secret}
        self._opener = urllib.request.build_opener(NoRedirect)

    def request(self, method: str, path: str, payload=None, *, data=False):
        if not path.startswith("/v2/") or "://" in path:
            raise ProbeRefused("Only fixed paper/data API paths are supported.")
        if data and method != "GET":
            raise ProbeRefused("Market-data mutations are refused.")
        body = None if payload is None else json.dumps(payload).encode()
        request = urllib.request.Request((DATA if data else PAPER) + path, data=body,
            headers={**self._headers, "Content-Type": "application/json"}, method=method)
        try:
            with self._opener.open(request, timeout=15) as response:
                raw = response.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as error:
            if error.code == 404 and method == "GET":
                return None
            raise ProbeRefused(f"Paper provider request failed with HTTP {error.code}.") from None


@contextmanager
def account_lock(account_id: str, root: Path):
    root.mkdir(parents=True, exist_ok=True)
    identity = hashlib.sha256((PAPER + account_id).encode()).hexdigest()
    quarantine = root / (identity + ".unresolved")
    with (root / (identity + ".lock")).open("a+b") as handle:
        try:
            if os.name == "nt":
                import msvcrt
                handle.seek(0); handle.write(b"0"); handle.flush(); handle.seek(0)
                msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            raise ProbeRefused("Another probe owns this account lock.") from None
        if quarantine.exists():
            raise ProbeRefused("An earlier probe left unresolved activity. Reconcile it before removing the quarantine marker.")
        yield quarantine


def run_probe(transport, account_id: str, *, max_notional=Decimal("1000"),
              now=lambda: datetime.now(timezone.utc), pause=time.sleep):
    """Caller holds the account lock. Only one SPY share can be owned by this run."""
    account = transport.request("GET", "/v2/account")
    if not account or account.get("id") != account_id:
        raise ProbeRefused("Paper account does not match the explicit allowlist identity.")
    if account.get("trading_blocked") or account.get("account_blocked"):
        raise ProbeRefused("The paper account is blocked.")
    if transport.request("GET", "/v2/positions") or transport.request("GET", "/v2/orders?status=open"):
        raise ProbeRefused("The dedicated account must begin flat with no working orders.")
    if not transport.request("GET", "/v2/clock").get("is_open"):
        raise ProbeRefused("Probes require an open regular session so unexpected fills can be closed.")
    quote = transport.request("GET", "/v2/stocks/SPY/quotes/latest?feed=iex", data=True).get("quote", {})
    observed = datetime.fromisoformat(quote.get("t", "").replace("Z", "+00:00"))
    age = (now() - observed).total_seconds()
    bid, ask = Decimal(str(quote.get("bp", 0))), Decimal(str(quote.get("ap", 0)))
    if age < 0 or age >= 30 or bid <= 0 or ask < bid or ask > max_notional:
        raise ProbeRefused("Fresh quote and configured notional limits are required.")
    limit = (bid * Decimal("0.5")).quantize(Decimal("0.01"), rounding=ROUND_DOWN)
    if limit <= 0:
        raise ProbeRefused("A valid non-marketable limit could not be established.")
    client_id = "meridian-probe-" + uuid.uuid4().hex
    terminal = None
    submit_error = None
    broker_id = None
    try:
        acknowledgement = transport.request("POST", "/v2/orders", {"symbol": "SPY", "qty": "1", "side": "buy",
            "type": "limit", "limit_price": str(limit), "time_in_force": "day", "client_order_id": client_id})
        if acknowledgement:
            broker_id = acknowledgement.get("id")
    except Exception as error:
        # A lost acknowledgement is not permission to resubmit.
        submit_error = error
    finally:
        cancel_requested = False
        remaining_lookups = 30
        while remaining_lookups:
            remaining_lookups -= 1
            order = transport.request("GET", "/v2/orders:by_client_order_id?client_order_id=" + client_id)
            if order:
                order_id = order.get("id")
                if order.get("client_order_id") != client_id or not order_id or (broker_id and broker_id != order_id):
                    raise ProbeRefused("Provider returned an inconsistent probe identity.")
                broker_id = order_id
                if order.get("status") in {"filled", "canceled", "expired", "rejected"}:
                    terminal = order
                    break
                if not cancel_requested:
                    transport.request("DELETE", "/v2/orders/" + broker_id)
                    cancel_requested = True
                    # A late discovery still gets a full window to confirm cancellation.
                    remaining_lookups = 30
            pause(1)
        if terminal is None:
            raise ProbeRefused("Probe outcome is unresolved; retain quarantine and investigate.")
        filled = Decimal(str(terminal.get("filled_qty", "0")))
        if filled < 0 or filled > 1:
            raise ProbeRefused("Probe fill quantity exceeds the authorized share limit.")
        positions = transport.request("GET", "/v2/positions")
        if filled:
            if transport.request("GET", "/v2/account").get("id") != account_id:
                raise ProbeRefused("Account changed before fill cleanup.")
            if len(positions) != 1 or positions[0].get("symbol") != "SPY" or Decimal(str(positions[0].get("qty"))) != filled:
                raise ProbeRefused("Unexpected holdings cannot be attributed exclusively to this probe.")
            transport.request("POST", "/v2/orders", {"symbol": "SPY", "qty": str(filled), "side": "sell",
                "type": "market", "time_in_force": "day", "client_order_id": "meridian-exit-" + uuid.uuid4().hex})
        for _ in range(30):
            positions = transport.request("GET", "/v2/positions")
            working = transport.request("GET", "/v2/orders?status=open")
            if not positions and not working:
                if submit_error:
                    raise ProbeRefused("Submission acknowledgement failed; cleanup verified flat, but the probe did not pass.") from submit_error
                return {"status": "verified-flat", "filled_quantity": str(filled)}
            pause(1)
        raise ProbeRefused("Cleanup has not established a flat account with no working orders.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--enable-order-probe", action="store_true")
    parser.add_argument("--account-id", required=True)
    parser.add_argument("--lock-root", type=Path, default=Path(tempfile.gettempdir()) / "meridian-paper-probes")
    args = parser.parse_args()
    if not args.enable_order_probe or os.environ.get("MERIDIAN_RUN_ALPACA_ORDER_PROBE") != "1" or os.environ.get("ALPACA_TRADING_ENVIRONMENT") != "paper":
        parser.error("Explicit probe flag, MERIDIAN_RUN_ALPACA_ORDER_PROBE=1 and paper environment are required.")
    key, secret = os.environ.get("ALPACA_KEY_ID"), os.environ.get("ALPACA_SECRET_KEY")
    if not key or not secret:
        parser.error("Paper credentials are missing.")
    with account_lock(args.account_id, args.lock_root) as quarantine:
        # Persist uncertainty before the first provider request; process termination keeps it.
        quarantine.write_text("A paper-order probe may require account reconciliation.\n")
        try:
            result = run_probe(PaperTransport(key, secret), args.account_id)
        except Exception:
            raise ProbeRefused("Probe failed. Account quarantine remains until independently reconciled.") from None
        quarantine.unlink()
        print(json.dumps(result))


if __name__ == "__main__":
    main()
