from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = (
    Path(__file__).resolve().parents[2] / "build" / "scripts" / "ci" / "check-endpoint-cancellation.py"
)
SPEC = importlib.util.spec_from_file_location("check_endpoint_cancellation", SCRIPT_PATH)
assert SPEC and SPEC.loader
guard = importlib.util.module_from_spec(SPEC)
sys.modules["check_endpoint_cancellation"] = guard
SPEC.loader.exec_module(guard)

SCAN_DIR = "src/Meridian.Ui.Shared/Endpoints"


def scan(handler_body: str) -> list[tuple[str, int]]:
    """Run the guard over a throwaway tree holding exactly this one endpoint file."""
    with tempfile.TemporaryDirectory() as temp_dir:
        root = Path(temp_dir)
        path = root / SCAN_DIR / "SampleEndpoints.cs"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(handler_body, encoding="utf-8")
        return guard.find_violations(root, (SCAN_DIR,))


BARE_WITH_TOKEN = """
public static class SampleEndpoints
{
    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapPost("/thing", async (Service service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.RunAsync(request, ct);
                return Results.Json(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}
"""


class ViolationDetectionTests(unittest.TestCase):
    def test_bare_catch_reachable_by_cancellation_is_reported(self):
        found = scan(BARE_WITH_TOKEN)
        self.assertEqual([rel for rel, _ in found], [f"{SCAN_DIR}/SampleEndpoints.cs"])
        self.assertEqual(len(found), 1)

    def test_request_aborted_counts_as_a_token(self):
        found = scan(BARE_WITH_TOKEN.replace(
            "async (Service service, CancellationToken ct)", "async (Service service, HttpContext context)"
        ).replace("service.RunAsync(request, ct)", "service.RunAsync(request, context.RequestAborted)"))
        self.assertEqual(len(found), 1)

    def test_catch_without_a_variable_name_is_still_a_catch(self):
        found = scan(BARE_WITH_TOKEN
                     .replace("catch (Exception ex)", "catch (Exception)")
                     .replace("ex.Message", '"failed"'))
        self.assertEqual(len(found), 1)


class AlreadyCorrectShapesTests(unittest.TestCase):
    """Each of these already lets a cancelled request through, so none is a defect."""

    def test_type_filtered_catch_is_not_a_violation(self):
        found = scan(BARE_WITH_TOKEN.replace(
            "catch (Exception ex)",
            "catch (Exception ex) when (ex is ArgumentException or FormatException)",
        ))
        self.assertEqual(found, [])

    def test_catch_excluding_cancellation_is_not_a_violation(self):
        found = scan(BARE_WITH_TOKEN.replace(
            "catch (Exception ex)", "catch (Exception ex) when (ex is not OperationCanceledException)"
        ))
        self.assertEqual(found, [])

    def test_preceding_rethrow_clause_is_not_a_violation(self):
        found = scan(BARE_WITH_TOKEN.replace(
            "            catch (Exception ex)",
            "            catch (OperationCanceledException) when (ct.IsCancellationRequested)\n"
            "            {\n"
            "                throw;\n"
            "            }\n"
            "            catch (Exception ex)",
        ))
        self.assertEqual(found, [])

    def test_preceding_clause_answering_499_is_not_a_violation(self):
        """Answering 499 Client Closed Request is the repo's other accepted shape."""
        found = scan(BARE_WITH_TOKEN.replace(
            "            catch (Exception ex)",
            "            catch (OperationCanceledException)\n"
            "            {\n"
            "                return Results.StatusCode(499);\n"
            "            }\n"
            "            catch (Exception ex)",
        ))
        self.assertEqual(found, [])

    def test_synchronous_handler_is_not_a_violation(self):
        """Untidy, but no token flows in, so no disconnect can land in the catch."""
        found = scan(BARE_WITH_TOKEN
                     .replace("async (Service service, CancellationToken ct)", "(Service service)")
                     .replace("var result = await service.RunAsync(request, ct);", "var result = service.Run();"))
        self.assertEqual(found, [])

    def test_await_without_a_token_is_not_a_violation(self):
        found = scan(BARE_WITH_TOKEN.replace("service.RunAsync(request, ct)", "service.RunAsync(request)"))
        self.assertEqual(found, [])


class LiteralHandlingTests(unittest.TestCase):
    def test_catch_written_in_a_comment_is_ignored(self):
        found = scan(BARE_WITH_TOKEN.replace(
            "            catch (Exception ex)",
            "            // catch (Exception ex) — described, not written\n            catch (Exception ex) when (ex is ArgumentException)",
        ))
        self.assertEqual(found, [])

    def test_braces_inside_strings_do_not_skew_the_parse(self):
        found = scan(BARE_WITH_TOKEN.replace(
            "return Results.Json(result);",
            'var note = "} catch (Exception) { unbalanced";\n                return Results.Json(result);',
        ))
        self.assertEqual(len(found), 1)


class LiveTreeTests(unittest.TestCase):
    def test_endpoint_surface_has_no_unguarded_catches(self):
        """The regression guard for #2618: a new hand-rolled handler must not reintroduce this."""
        found = guard.find_violations(guard.REPO_ROOT)
        self.assertEqual(
            found,
            [],
            "These endpoint handlers can swallow a client disconnect; add a cancellation "
            "re-throw before the generic catch:\n"
            + "\n".join(f"  {rel}:{line}" for rel, line in found),
        )


if __name__ == "__main__":
    unittest.main()
