"""Behavioral checks for the source-backed literal HTTP declaration reference."""

from __future__ import annotations

from contextlib import redirect_stderr, redirect_stdout
import importlib.util
import io
from pathlib import Path
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "render-endpoint-reference.py"
spec = importlib.util.spec_from_file_location("render_endpoint_reference_under_test", SCRIPT)
assert spec is not None and spec.loader is not None
reference = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = reference
spec.loader.exec_module(reference)


class EndpointReferenceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def write_source(self, text, name="Example.cs"):
        path = self.root / "src" / "Fixture" / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
        return path

    def test_nested_groups_constraints_empty_route_and_distinct_methods(self):
        self.write_source('''
public class Example {
 public void MapExample(WebApplication app) {
  var group = app.MapGroup("/api/example");
  var nested = group.MapGroup("/items");
  nested.MapGet("", () => Results.Ok());
  nested.MapGet("/{id:guid}", (Guid id) => Results.Ok(id));
  nested.MapPost("/{id:guid}", (Guid id, SaveRequest request) => Results.Created());
 }
}
''')
        declarations = reference.collect(self.root)
        self.assertEqual(
            [("GET", "/api/example/items"), ("GET", "/api/example/items/{id}"), ("POST", "/api/example/items/{id}")],
            [(item.method, item.route) for item in declarations],
        )
        self.assertFalse(any(item.relative for item in declarations))
        self.assertIn("SaveRequest request", declarations[2].handler)

    def test_comments_and_literals_do_not_create_endpoints(self):
        self.write_source('''
public class Example {
 // app.MapDelete("/api/fake", () => Results.Ok());
 string sample = """ app.MapPut("/api/fake", () => Results.Ok()); """;
 public void MapExample(WebApplication app) {
  app.MapGet("/api/real", () => Results.Ok());
 }
}
''')
        self.assertEqual(["/api/real"], [item.route for item in reference.collect(self.root)])

    def test_balanced_lambdas_keep_input_and_response_metadata_with_own_endpoint(self):
        self.write_source('''
public class Example {
 public void MapExample(WebApplication app) {
  app.MapPost("/api/save", async (JsonElement body, HttpContext context) => {
   var request = JsonSerializer.Deserialize<SaveRequest>(body.GetRawText());
   if (request is null) { return Results.BadRequest("a; } quote"); }
   return Results.Json(request);
  }).WithName("SaveItem").Produces<SavedDto>(201).Produces(400);
  app.MapGet("/api/status", StatusHandler).WithName("ReadStatus").Produces<StatusDto>(200);
 }
}
''')
        save, status = reference.collect(self.root)
        self.assertIn("Deserialize<SaveRequest>", save.inputs)
        self.assertIn("Produces<SavedDto>(201)", save.responses)
        self.assertIn("Results.BadRequest", save.responses)
        self.assertIn("Results.Json", save.responses)
        self.assertNotIn("Produces<StatusDto>(200)", save.responses)
        self.assertEqual("StatusHandler", status.handler)
        self.assertEqual(("Produces<StatusDto>(200)",), status.responses)
        self.assertEqual((), status.inputs)

    def test_parameter_groups_resolve_partial_calls_and_multiple_mounts(self):
        self.write_source('''
public static partial class Example {
 public static void MapExample(WebApplication app) {
  MapShared(app.MapGroup("/api/risk"));
  MapShared(app.MapGroup("/api/v1/risk").WithTags("Risk"));
 }
}
''')
        self.write_source('''
public static partial class Example {
 private static void MapShared(RouteGroupBuilder group) {
  group.MapGet("/rules", () => Results.Ok());
 }
}
''', "Example.Shared.cs")
        item, = reference.collect(self.root)
        self.assertTrue(item.relative)
        self.assertEqual("/rules", item.route)
        self.assertEqual(("/api/risk/rules", "/api/v1/risk/rules"), item.mounted_routes)
        rendered = reference.render([item])
        self.assertIn("relative declaration", rendered)
        self.assertIn("`/api/v1/risk/rules`", rendered)

    def test_dynamic_group_remains_explicit_relative_declaration(self):
        self.write_source('''
public class Example {
 public void MapExample(RouteGroupBuilder supplied) {
  supplied.MapGet("/read", () => Results.Ok());
 }
}
''')
        item, = reference.collect(self.root)
        self.assertTrue(item.relative)
        self.assertEqual((), item.mounted_routes)

    def test_metadata_literal_spacing_and_punctuation_are_preserved(self):
        self.write_source('''
public class Example {
 public void MapExample(WebApplication app) {
  app.MapGet("/api/metadata", Handler)
   .WithDescription("Returns metadata. Filter using ?type=streaming or ?type=backfill; call Handler (x).")
   .Produces(200, contentType: "text/csv; charset = utf-8");
 }
}
''')
        item, = reference.collect(self.root)
        description = 'WithDescription("Returns metadata. Filter using ?type=streaming or ?type=backfill; call Handler (x).")'
        self.assertEqual((description,), item.metadata)
        self.assertIn('"text/csv; charset = utf-8"', item.responses[0])
        self.assertIn(description, reference.render([item]))

    def test_empty_local_group_is_root_bound_and_other_method_groups_do_not_leak(self):
        self.write_source('''
public class Example {
 public void MapFirst(WebApplication app) {
  var group = app.MapGroup("/api/first");
  group.MapGet("/first", Handler);
 }
 public void MapSecond(WebApplication app) {
  var group = app.MapGroup("");
  group.MapGet("/api/root", Handler);
 }
 public void MapThird(RouteGroupBuilder group) {
  group.MapGet("/third", Handler);
 }
}
''')
        first, second, third = reference.collect(self.root)
        self.assertEqual("/api/first/first", first.route)
        self.assertEqual("/api/root", second.route)
        self.assertFalse(second.relative)
        self.assertEqual("/third", third.route)
        self.assertTrue(third.relative)

    def test_marker_update_preserves_hand_written_contract_and_rejects_broken_markers(self):
        original = "# Contract\n\nHand-written details.\n"
        generated = reference.BEGIN + "\nGenerated first\n" + reference.END
        first = reference.update_document(original, generated)
        second = reference.update_document(first, generated.replace("first", "second"))
        self.assertTrue(second.startswith(original))
        self.assertIn("Generated second", second)
        self.assertNotIn("Generated first", second)
        for broken in (reference.BEGIN, reference.END, reference.END + reference.BEGIN, reference.BEGIN * 2 + reference.END * 2):
            with self.subTest(broken=broken), self.assertRaises(ValueError):
                reference.update_document(broken, generated)

    def test_cli_check_detects_source_drift_without_overwriting_reference(self):
        source = self.write_source('public class Example { void MapExample(WebApplication app) { app.MapGet("/api/one", Handler); } }')
        document = self.root / "docs/reference/api-reference.md"
        document.parent.mkdir(parents=True)
        document.write_text("# Fixture contract\n", encoding="utf-8")
        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            self.assertEqual(0, reference.main(["--root", str(self.root)]))
            self.assertEqual(0, reference.main(["--root", str(self.root), "--check"]))
            before = document.read_bytes()
            source.write_text(source.read_text().replace("/api/one", "/api/two"), encoding="utf-8")
            self.assertEqual(1, reference.main(["--root", str(self.root), "--check"]))
            self.assertEqual(before, document.read_bytes())


if __name__ == "__main__":
    unittest.main()
