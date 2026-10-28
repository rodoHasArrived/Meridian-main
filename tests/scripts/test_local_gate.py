"""Exercise real isolated children to verify local gate scheduling and evidence."""
from __future__ import annotations

from contextlib import redirect_stdout
import importlib.util
import io
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch


REPO = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("local_gate", REPO / "build/scripts/ci/local_gate.py")
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)

STUB = '''import json, os, pathlib, sys, time
root = pathlib.Path.cwd()
name, scenario = sys.argv[1:]
def event(kind):
    with (root / "events.jsonl").open("a") as output:
        output.write(json.dumps({"lane":name,"kind":kind,"time":time.monotonic(),
          "artifacts":os.environ.get("MERIDIAN_CI_ARTIFACT_ROOT"),
          "isolationKey":os.environ.get("MERIDIAN_CI_ISOLATION_KEY")})+"\\n")
event("start")
(root / (name+".started")).touch()
if scenario == "overlap" and name in ("verify-dotnet","verify-browser"):
    peer = "verify-browser" if name == "verify-dotnet" else "verify-dotnet"
    deadline = time.monotonic()+5
    while not (root / (peer+".started")).exists():
        if time.monotonic()>deadline: sys.exit(9)
        time.sleep(.01)
if scenario == "interrupt" and name == "verify-dotnet":
    subprocess = __import__("subprocess")
    child = subprocess.Popen([sys.executable,"-c", "import time,pathlib; time.sleep(1.5); pathlib.Path('orphan-write').touch()"])
    (root / "descendant.pid").write_text(str(child.pid))
    time.sleep(30)
if scenario == "barrier":
    if name == "verify-dotnet":
        deadline = time.monotonic()+5
        while not (root / "browser-ready").exists():
            if time.monotonic()>deadline: sys.exit(10)
            time.sleep(.01)
    elif name == "verify-browser":
        event("tests-ready")
        (root / "browser-ready").touch()
        __import__("subprocess").run([sys.executable,os.environ["LOCAL_GATE_TEST_HELPER"],
            "--wait-for-dotnet",os.environ["MERIDIAN_CI_BROWSER_BUILD_BARRIER"]],check=True)
        assert (root / "verify-dotnet.finished").exists()
        event("bundle-build")
if name in ("verify-docs","verify-workflows"):
    assert (root / "verify-dotnet.finished").exists()
    assert (root / "verify-browser.finished").exists()
if name == "verify-workflows": assert (root / "verify-docs.finished").exists()
event("finish")
(root / (name+".finished")).touch()
sys.exit(7 if scenario == "failure" and name == "verify-dotnet" else 0)
'''


class LocalGateTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.stub = self.root / "lane.py"
        self.stub.write_text(STUB, encoding="utf-8")

    def commands(self, scenario="normal"):
        return {name: [sys.executable, str(self.stub), name, scenario] for name in gate.FULL_LANES}

    def run_gate(self, **kwargs):
        with redirect_stdout(io.StringIO()):
            return gate.run_gate(self.root, commands=self.commands(kwargs.pop("scenario", "normal")),
                                 prerequisite=[sys.executable, "-c", "pass"], **kwargs)

    def receipt(self):
        paths = list((self.root / ".ai/validation-runs").glob("*.json"))
        self.assertEqual(len(paths), 1)
        return json.loads(paths[0].read_text())

    def events(self):
        return [json.loads(line) for line in (self.root / "events.jsonl").read_text().splitlines()]

    def test_pair_overlaps_and_generation_waits_for_both(self):
        self.assertEqual(0, self.run_gate(jobs=2, scenario="overlap"))
        events = self.events()
        intervals = {lane: {event["kind"]: event["time"] for event in events if event["lane"] == lane}
                     for lane in gate.FULL_LANES}
        self.assertLess(intervals["verify-dotnet"]["start"], intervals["verify-browser"]["finish"])
        self.assertLess(intervals["verify-browser"]["start"], intervals["verify-dotnet"]["finish"])
        self.assertGreaterEqual(intervals["verify-docs"]["start"],
                                max(intervals[lane]["finish"] for lane in gate.FULL_LANES[:2]))
        receipt = self.receipt()
        self.assertEqual(receipt["status"], "passed")
        self.assertCountEqual(receipt["completedLanes"], gate.FULL_LANES)
        self.assertNotIn("coldFullGateSeconds", receipt)

    def test_jobs_one_serializes_pair_and_every_child_has_own_evidence_root(self):
        self.assertEqual(0, self.run_gate(jobs=1))
        events = self.events()
        self.assertEqual([(event["lane"], event["kind"]) for event in events],
                         [(lane, kind) for lane in gate.FULL_LANES for kind in ("start", "finish")])
        roots = [event["artifacts"] for event in events if event["kind"] == "start"]
        self.assertEqual(len(set(roots)), 4)
        self.assertTrue(all(Path(root).is_relative_to(self.root / "artifacts/local-gates") for root in roots))

    def test_failure_still_runs_all_lanes_and_cold_attempt_is_retained(self):
        self.assertEqual(7, self.run_gate(jobs=2, cold=True, scenario="failure"))
        receipt = self.receipt()
        self.assertEqual(receipt["status"], "failed")
        self.assertCountEqual(receipt["completedLanes"], gate.FULL_LANES)
        self.assertEqual(receipt["cacheState"], "cold-isolated")
        self.assertTrue(receipt["freshBuildOutputs"])
        self.assertGreater(receipt["coldFullGateSeconds"], 0)
        first = next(event for event in self.events() if event["lane"] == "verify-dotnet")
        self.assertEqual(first["isolationKey"], receipt["isolationKey"])
        self.assertEqual(len(receipt["buildOutputDirectories"]), 2)
        self.assertTrue(all(Path(path).is_dir() for path in receipt["buildOutputDirectories"]))

    def test_prerequisites_block_all_children_and_do_not_claim_cold_full_gate(self):
        with redirect_stdout(io.StringIO()):
            code = gate.run_gate(self.root, jobs=2, cold=True, commands=self.commands(),
                                 prerequisite=[sys.executable, "-c", "raise SystemExit(17)"])
        self.assertEqual(code, 17)
        self.assertFalse((self.root / "events.jsonl").exists())
        receipt = self.receipt()
        self.assertEqual(receipt["status"], "blocked")
        self.assertFalse(receipt["freshBuildOutputs"])
        self.assertNotIn("coldFullGateSeconds", receipt)

    def test_runs_never_share_artifact_roots(self):
        for _ in range(2):
            self.assertEqual(0, self.run_gate(jobs=1))
        receipts = [json.loads(path.read_text()) for path in (self.root / ".ai/validation-runs").glob("*.json")]
        self.assertEqual(len(receipts), 2)
        self.assertNotEqual(receipts[0]["outputDirectory"], receipts[1]["outputDirectory"])

    def test_another_aggregate_gate_blocks_without_launching_children(self):
        with gate.gate_lock(self.root), redirect_stdout(io.StringIO()):
            self.assertEqual(3, self.run_gate(jobs=2))
        self.assertFalse((self.root / "events.jsonl").exists())
        self.assertEqual(self.receipt()["failurePhase"], "local-gate-lock")

    def test_existing_buildctl_writer_blocks_pair_without_releasing_its_lock(self):
        lock_path = self.root / ".ai/locks/validation.lock"
        lock_path.parent.mkdir(parents=True)
        owner = {"runId": "existing-buildctl", "pid": os.getpid()}
        lock_path.write_text(json.dumps(owner))
        self.assertEqual(self.run_gate(jobs=2), 3)
        self.assertFalse((self.root / "events.jsonl").exists())
        self.assertEqual(json.loads(lock_path.read_text()), owner)
        self.assertEqual(self.receipt()["failurePhase"], "validation-lock")

    def test_verify_fast_has_only_pair_and_no_full_gate_metric(self):
        self.assertEqual(0, self.run_gate(lane="verify-fast", jobs=2, cold=True))
        receipt = self.receipt()
        self.assertCountEqual(receipt["completedLanes"], gate.FULL_LANES[:2])
        self.assertNotIn("coldFullGateSeconds", receipt)

    def test_stopped_children_cannot_launch_a_queued_process(self):
        children = gate.Children()
        children.stop()
        with self.assertRaises(InterruptedError):
            children.run([sys.executable, "-c", "raise SystemExit(0)"], self.root,
                         dict(os.environ), self.root / "queued.log")
        self.assertEqual(children.processes, [])

    def test_browser_asset_barrier_waits_for_dotnet_completion(self):
        marker = self.root / "dotnet-finished"
        self.assertEqual(gate.wait_for_dotnet(marker, timeout=0), 1)
        marker.touch()
        self.assertEqual(gate.wait_for_dotnet(marker, timeout=0), 0)

    def test_browser_tests_overlap_but_bundle_write_waits_for_dotnet_readers(self):
        with patch.dict(os.environ, {"LOCAL_GATE_TEST_HELPER": str(REPO / "build/scripts/ci/local_gate.py")}):
            self.assertEqual(self.run_gate(jobs=2, scenario="barrier"), 0)
        events = self.events()
        times = {(event["lane"], event["kind"]): event["time"] for event in events}
        self.assertLess(times[("verify-browser", "tests-ready")], times[("verify-dotnet", "finish")])
        self.assertGreaterEqual(times[("verify-browser", "bundle-build")], times[("verify-dotnet", "finish")])
        self.assertFalse((self.root / ".ai/locks/validation.lock").exists())

    @unittest.skipIf(os.name == "nt", "POSIX process-group signal contract")
    def test_interrupt_stops_descendants_and_never_launches_queued_lane(self):
        driver = self.root / "driver.py"
        driver.write_text(
            "import importlib.util,pathlib,sys\n"
            f"s=importlib.util.spec_from_file_location('gate',{str(REPO / 'build/scripts/ci/local_gate.py')!r})\n"
            "m=importlib.util.module_from_spec(s);s.loader.exec_module(m)\n"
            f"commands={{n:[sys.executable,{str(self.stub)!r},n,'interrupt'] for n in m.FULL_LANES}}\n"
            f"raise SystemExit(m.run_gate(pathlib.Path({str(self.root)!r}),jobs=1,cold=True,commands=commands,"
            "prerequisite=[sys.executable,'-c','pass']))\n", encoding="utf-8")
        with (self.root / "driver.log").open("w") as log:
            process = subprocess.Popen([sys.executable, str(driver)], stdout=log, stderr=subprocess.STDOUT)
            self.addCleanup(lambda: process.kill() if process.poll() is None else None)
            deadline = time.monotonic() + 10
            while not (self.root / "descendant.pid").exists():
                if process.poll() is not None or time.monotonic() > deadline:
                    self.fail((self.root / "driver.log").read_text())
                time.sleep(.01)
            process.send_signal(signal.SIGTERM)
            self.assertEqual(process.wait(timeout=7), 130)
        receipt = self.receipt()
        self.assertEqual(receipt["status"], "interrupted")
        self.assertNotIn("coldFullGateSeconds", receipt)
        self.assertFalse((self.root / "verify-browser.started").exists())
        self.assertFalse((self.root / ".ai/locks/validation.lock").exists())
        time.sleep(1.6)
        self.assertFalse((self.root / "orphan-write").exists())

    def test_auto_budget_is_bounded_and_unknown_memory_is_sequential(self):
        with patch.object(gate.os, "cpu_count", return_value=1):
            self.assertEqual(gate.automatic_jobs(), 1)
        self.assertIn(gate.automatic_jobs(), (1, 2))


if __name__ == "__main__":
    unittest.main()
