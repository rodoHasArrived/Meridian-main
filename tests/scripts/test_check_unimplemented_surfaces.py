import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "build" / "scripts" / "ci" / "check-unimplemented-surfaces.py"
SPEC = importlib.util.spec_from_file_location("check_unimplemented_surfaces", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)

CANONICAL_STUB = """public static class StubEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/stub", (HttpContext context) =>
            ApiProblemDetails.NotImplemented(context, "No stub engine exists."))
        .Produces(StatusCodes.Status501NotImplemented);
    }
}
"""

TITLED_501 = """public static class AlignmentEndpoints
{
    private static IResult Unavailable() => Results.Problem(
        statusCode: StatusCodes.Status501NotImplemented,
        title: "Alignment is not implemented",
        detail: "No job was created.");
}
"""

MISSING_SERVICE_501 = """public static class OptionalEndpoints
{
    public static IResult Get(IOptionalService? service)
        => service is null
            ? Results.Problem("Optional service is not registered.", statusCode: StatusCodes.Status501NotImplemented)
            : Results.Ok();
}
"""

MAINTENANCE_STUB = """public sealed class Maintenance
{
    private Task<MaintenanceResult> RunDedupAsync()
        => Task.FromResult(BuildNotImplementedResult(task, dryRun, "No server-side implementation."));

    private static MaintenanceResult BuildNotImplementedResult(MaintenanceTask task, bool dryRun, string message)
        => new();
}
"""

COMMENT_ONLY = """public static class Notes
{
    // ApiProblemDetails.NotImplemented( is used elsewhere; Status501NotImplemented not implemented here.
}
"""


class FindSurfaceLinesTests(unittest.TestCase):
    def test_canonical_helper_call_is_a_surface(self) -> None:
        self.assertEqual(MODULE.find_surface_lines(CANONICAL_STUB), [6])

    def test_produces_declaration_alone_is_not_a_surface(self) -> None:
        text = 'app.MapGet("/x", Handler).Produces(StatusCodes.Status501NotImplemented); // not implemented'
        self.assertEqual(MODULE.find_surface_lines(text), [])

    def test_501_with_not_implemented_title_is_a_surface(self) -> None:
        self.assertEqual(MODULE.find_surface_lines(TITLED_501), [4])

    def test_missing_service_501_is_a_configuration_refusal(self) -> None:
        self.assertEqual(MODULE.find_surface_lines(MISSING_SERVICE_501), [])

    def test_maintenance_helper_call_counts_but_definition_does_not(self) -> None:
        self.assertEqual(MODULE.find_surface_lines(MAINTENANCE_STUB), [4])

    def test_comments_are_ignored(self) -> None:
        self.assertEqual(MODULE.find_surface_lines(COMMENT_ONLY), [])


class GateTests(unittest.TestCase):
    def _make_repo(self, root: Path, evidence_paths: list[str], baseline_entries: list[dict] | None) -> None:
        source = root / "src" / "Meridian.Example"
        source.mkdir(parents=True)
        (source / "StubEndpoints.cs").write_text(CANONICAL_STUB, encoding="utf-8")
        (source / "OptionalEndpoints.cs").write_text(MISSING_SERVICE_501, encoding="utf-8")
        obj = source / "obj"
        obj.mkdir()
        (obj / "Generated.cs").write_text(CANONICAL_STUB, encoding="utf-8")

        roadmap = root / "docs" / "roadmap" / "data"
        roadmap.mkdir(parents=True)
        evidence = "".join(f"      - type: source\n        path: {path}\n" for path in evidence_paths)
        (roadmap / "roadmap-items.yml").write_text(
            "items:\n  - id: W1-EXAMPLE-001\n    evidence:\n" + evidence, encoding="utf-8"
        )

        if baseline_entries is not None:
            baseline = root / "build" / "scripts" / "ci"
            baseline.mkdir(parents=True)
            (baseline / "unimplemented-surface-baseline.json").write_text(
                json.dumps({"entries": baseline_entries}), encoding="utf-8"
            )

    def _run(self, root: Path) -> tuple[int, dict]:
        output = root / "out.json"
        code = MODULE.main(["--root", str(root), "--summary", "--json-output", str(output)])
        return code, json.loads(output.read_text(encoding="utf-8"))

    def test_tracked_surface_passes(self) -> None:
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
            root = Path(tmp)
            self._make_repo(root, ["src/Meridian.Example/StubEndpoints.cs"], None)
            code, report = self._run(root)
        self.assertEqual(code, 0)
        self.assertEqual(report["classification"]["tracked"], ["src/Meridian.Example/StubEndpoints.cs"])
        self.assertEqual(list(report["surfaces"]), ["src/Meridian.Example/StubEndpoints.cs"])

    def test_untracked_surface_fails(self) -> None:
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
            root = Path(tmp)
            self._make_repo(root, [], None)
            code, report = self._run(root)
        self.assertEqual(code, 1)
        self.assertEqual(report["classification"]["untracked"], ["src/Meridian.Example/StubEndpoints.cs"])

    def test_baselined_surface_passes(self) -> None:
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
            root = Path(tmp)
            self._make_repo(
                root, [], [{"path": "src/Meridian.Example/StubEndpoints.cs", "reason": "Known gap."}]
            )
            code, report = self._run(root)
        self.assertEqual(code, 0)
        self.assertEqual(report["classification"]["baselined"], ["src/Meridian.Example/StubEndpoints.cs"])

    def test_baseline_without_reason_fails(self) -> None:
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
            root = Path(tmp)
            self._make_repo(root, [], [{"path": "src/Meridian.Example/StubEndpoints.cs", "reason": ""}])
            code, _ = self._run(root)
        self.assertEqual(code, 1)

    def test_stale_baseline_entry_fails(self) -> None:
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
            root = Path(tmp)
            self._make_repo(
                root,
                ["src/Meridian.Example/StubEndpoints.cs"],
                [{"path": "src/Meridian.Example/Removed.cs", "reason": "Gap that was closed."}],
            )
            code, report = self._run(root)
        self.assertEqual(code, 1)
        self.assertTrue(any("matches no unimplemented surface" in error for error in report["errors"]))

    def test_baseline_entry_already_tracked_fails(self) -> None:
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
            root = Path(tmp)
            self._make_repo(
                root,
                ["src/Meridian.Example/StubEndpoints.cs"],
                [{"path": "src/Meridian.Example/StubEndpoints.cs", "reason": "Now tracked."}],
            )
            code, report = self._run(root)
        self.assertEqual(code, 1)
        self.assertTrue(any("already tracked" in error for error in report["errors"]))


class RepositoryGateTests(unittest.TestCase):
    def test_repository_has_no_untracked_unimplemented_surfaces(self) -> None:
        self.assertEqual(MODULE.main(["--summary"]), 0)


if __name__ == "__main__":
    unittest.main()
