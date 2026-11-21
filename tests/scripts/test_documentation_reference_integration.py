"""Exercise source references and the existing coverage metric together on a tiny repo."""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "build/scripts/docs"
RENDERERS = (
    "render-public-type-reference.py",
    "render-endpoint-reference.py",
    "generate-configuration-reference.py",
)
BEGIN = "<!-- BEGIN GENERATED HTTP DECLARATION REFERENCE -->"
END = "<!-- END GENERATED HTTP DECLARATION REFERENCE -->"


def load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


coverage = load("reference_integration_coverage", SCRIPTS / "generate-coverage.py")
automation = load("reference_integration_automation", SCRIPTS / "run-docs-automation.py")
generation = load("reference_integration_generation", ROOT / "build/scripts/run-maintained-generation.py")


class DocumentationReferenceIntegrationTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.write("src/Example/Example.cs", '''namespace Example;
/// <summary>A source-backed example result.</summary>
public record ExampleResult(string Name, int Count = 2);
public enum ExampleState { Ready = 0, Blocked = 1 }
public interface IExampleService
{
    Task<ExampleResult> ReadAsync(CancellationToken cancellationToken);
}
[ImplementsAdr("ADR-001")]
public static class ExampleEndpoints
{
    public static void MapExample(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/examples");
        group.MapGet("", (IExampleService service) => Results.Ok(service.ReadAsync(default)));
        group.MapPost("/{id:guid}", (Guid id, ExampleResult request) => Results.Ok(request));
    }
}
''')
        self.write("config/appsettings.sample.json", '''{
  // Example settings, not runtime defaults.
  "Example": { "Enabled": false, "Count": 2 }
}
''')
        self.write("docs/reference/api-reference.md", f"# API Reference\n\nHandwritten guidance.\n\n{BEGIN}\n{END}\n")
        self.write("archive/docs/adr/001-example.md", "# Historical example decision\n\nStatus: accepted\n")

    def write(self, path: str, content: str):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")

    def run_renderer(self, name: str, *, check: bool = False):
        command = [sys.executable, str(SCRIPTS / name), "--root", str(self.root)]
        if check:
            command.append("--check")
        return subprocess.run(command, capture_output=True, text=True, timeout=90, check=False)

    def render(self):
        for name in RENDERERS:
            result = self.run_renderer(name)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def assert_full_coverage(self):
        report = coverage.build_report(self.root)
        self.assertGreater(report.overall_total, 0)
        self.assertEqual(report.overall_documented, report.overall_total,
                         [(c.category, [i.name for i in c.undocumented_items]) for c in report.categories])
        return report

    def test_references_close_real_gaps_and_rebuild_without_changes(self):
        baseline = coverage.build_report(self.root)
        self.assertLess(baseline.overall_documented, baseline.overall_total)
        self.render()
        report = self.assert_full_coverage()
        self.assertTrue(any(item.name == "ExampleResult" for item in report.categories[0].items))
        api = (self.root / "docs/reference/api-reference.md").read_text()
        self.assertIn("Handwritten guidance.", api)
        self.assertIn("/api/examples", api)
        self.assertIn("POST", api)
        snapshot = {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob("*.md")}
        self.render()
        for name in RENDERERS:
            result = self.run_renderer(name, check=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(snapshot, {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob("*.md")})

    def test_source_drift_fails_checks_without_rewriting_then_regenerates(self):
        self.render()
        source = self.root / "src/Example/Example.cs"
        source.write_text(source.read_text().replace(
            'group.MapGet("",', 'group.MapGet("/new-route", () => Results.Ok());\n        group.MapGet("",'
        ) + '\npublic record NewSourceResult(bool Enabled);\n')
        self.write("config/appsettings.sample.json", '{"Example": {"Enabled": false, "Count": 2, "NewOption": true}}\n')
        snapshot = {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob("*.md")}
        for name in RENDERERS:
            result = self.run_renderer(name, check=True)
            self.assertNotEqual(result.returncode, 0, name)
        self.assertEqual(snapshot, {p.relative_to(self.root): p.read_bytes() for p in self.root.rglob("*.md")})
        self.render()
        self.assert_full_coverage()


class ReferenceMaintenanceIntegrationTests(unittest.TestCase):
    def test_producers_precede_coverage_and_all_profiles_check_drift(self):
        producers = {step.command[1] for step in generation.DOCS_STEPS if not step.optional}
        for renderer in RENDERERS:
            self.assertIn("build/scripts/docs/" + renderer, producers)
        steps = [step.command[1] for step in generation.DOCS_STEPS]
        consumer = steps.index("build/scripts/docs/run-docs-automation.py")
        for renderer in RENDERERS:
            self.assertLess(steps.index("build/scripts/docs/" + renderer), consumer)
        checks = ("check-public-type-reference", "check-endpoint-reference", "check-configuration-reference")
        for profile, names in automation.PROFILE_CONFIG.items():
            for name in checks:
                self.assertEqual(names.count(name), 1, profile)
                self.assertEqual(automation.SCRIPT_CONFIG[name]["args"], ["--check"])
                if "generate-coverage" in names:
                    self.assertLess(names.index(name), names.index("generate-coverage"))

    def test_api_write_authority_preserves_handwritten_guidance(self):
        policy = json.loads((ROOT / "build/scripts/maintained-generation-outputs.json").read_text())
        path = "docs/reference/api-reference.md"
        original = f"Handwritten.\n{BEGIN}\nold generated text\n{END}\nMore handwritten.\n".encode()
        generated = original.replace(b"old generated text", b"new generated text")
        self.assertEqual(generation.validate_changes({path: original}, {path: generated}, policy), [])
        handwritten = generated.replace(b"Handwritten.", b"Overwritten.")
        self.assertEqual(generation.validate_changes({path: original}, {path: handwritten}, policy), [path])
        malformed = generated.replace(END.encode(), b"")
        self.assertEqual(generation.validate_changes({path: original}, {path: malformed}, policy), [path])


if __name__ == "__main__":
    unittest.main()
