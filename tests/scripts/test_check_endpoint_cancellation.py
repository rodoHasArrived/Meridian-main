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


class CatchSpellingTests(unittest.TestCase):
    def test_fully_qualified_exception_is_detected(self):
        """`catch (System.Exception ex)` swallows cancellation exactly like the short form."""
        found = scan(BARE_WITH_TOKEN.replace("catch (Exception ex)", "catch (System.Exception ex)"))
        self.assertEqual(len(found), 1)

    def test_global_qualified_exception_is_detected(self):
        found = scan(BARE_WITH_TOKEN.replace("catch (Exception ex)", "catch (global::System.Exception ex)"))
        self.assertEqual(len(found), 1)


class GuardClauseTests(unittest.TestCase):
    def test_task_canceled_alone_does_not_count_as_a_guard(self):
        """TaskCanceledException derives from OperationCanceledException, so it cannot catch
        the base type that ct.ThrowIfCancellationRequested() throws."""
        found = scan(BARE_WITH_TOKEN.replace(
            "            catch (Exception ex)",
            "            catch (TaskCanceledException)\n"
            "            {\n"
            "                throw;\n"
            "            }\n"
            "            catch (Exception ex)",
        ))
        self.assertEqual(len(found), 1)

    def test_qualified_operation_canceled_counts_as_a_guard(self):
        found = scan(BARE_WITH_TOKEN.replace(
            "            catch (Exception ex)",
            "            catch (System.OperationCanceledException)\n"
            "            {\n"
            "                throw;\n"
            "            }\n"
            "            catch (Exception ex)",
        ))
        self.assertEqual(found, [])


class FilterTrustTests(unittest.TestCase):
    def test_opaque_predicate_filter_is_not_trusted(self):
        """`when (ShouldHandle(ex))` may well accept a cancellation, so it stays a violation."""
        found = scan(BARE_WITH_TOKEN.replace(
            "catch (Exception ex)", "catch (Exception ex) when (ShouldHandle(ex))"))
        self.assertEqual(len(found), 1)

    def test_negated_is_cancellation_requested_is_trusted(self):
        """A distinct but equally safe idiom: while cancelled, the catch declines entirely."""
        found = scan(BARE_WITH_TOKEN.replace(
            "catch (Exception ex)", "catch (Exception) when (!ct.IsCancellationRequested)"))
        self.assertEqual(found, [])

    def test_type_list_naming_a_base_type_is_not_trusted(self):
        found = scan(BARE_WITH_TOKEN.replace(
            "catch (Exception ex)", "catch (Exception ex) when (ex is Exception)"))
        self.assertEqual(len(found), 1)

    def test_disjunction_with_one_permissive_branch_is_not_trusted(self):
        """A safe branch does not redeem the filter — the permissive branch still admits it."""
        found = scan(BARE_WITH_TOKEN.replace(
            "catch (Exception ex)",
            "catch (Exception ex) when (ex is ArgumentException || ShouldHandle(ex))"))
        self.assertEqual(len(found), 1)

    def test_conjunction_containing_a_safe_term_is_trusted(self):
        found = scan(BARE_WITH_TOKEN.replace(
            "catch (Exception ex)",
            "catch (Exception ex) when (ShouldHandle(ex) && !ct.IsCancellationRequested)"))
        self.assertEqual(found, [])


class TokenReachesTheAwaitTests(unittest.TestCase):
    def test_token_on_a_later_unawaited_call_is_not_a_violation(self):
        """The token must belong to the awaited invocation, not merely share the try body."""
        found = scan(BARE_WITH_TOKEN.replace(
            "                var result = await service.RunAsync(request, ct);",
            "                var result = await service.RunAsync(request);\n"
            "                service.Register(ct);"))
        self.assertEqual(found, [])

    def test_named_token_argument_is_detected(self):
        found = scan(BARE_WITH_TOKEN.replace(
            "service.RunAsync(request, ct)", "service.RunAsync(request, cancellationToken: ct)"))
        self.assertEqual(len(found), 1)

    def test_unconventionally_named_token_is_detected(self):
        found = scan(BARE_WITH_TOKEN
                     .replace("CancellationToken ct", "CancellationToken requestCancellation")
                     .replace("service.RunAsync(request, ct)", "service.RunAsync(request, requestCancellation)"))
        self.assertEqual(len(found), 1)

    def test_token_across_a_multi_line_awaited_call_is_detected(self):
        found = scan(BARE_WITH_TOKEN.replace(
            "                var result = await service.RunAsync(request, ct);",
            "                var result = await service\n"
            "                    .RunAsync(request, ct)\n"
            "                    .ConfigureAwait(false);"))
        self.assertEqual(len(found), 1)


class BareCatchAllTests(unittest.TestCase):
    """`catch { }` catches cancellation exactly like `catch (Exception)`."""

    def test_bare_catch_all_is_detected(self):
        found = scan(BARE_WITH_TOKEN.replace("catch (Exception ex)", "catch")
                     .replace("return Results.BadRequest(new { error = ex.Message });",
                              "return Results.BadRequest();"))
        self.assertEqual(len(found), 1)

    def test_bare_catch_all_on_its_own_line_is_detected(self):
        found = scan(BARE_WITH_TOKEN.replace(
            "            catch (Exception ex)\n            {\n"
            "                return Results.BadRequest(new { error = ex.Message });\n",
            "            catch\n            {\n                return Results.BadRequest();\n"))
        self.assertEqual(len(found), 1)

    def test_typed_non_generic_catch_is_not_a_catch_all(self):
        """`catch (ArgumentException ex) { }` does not catch cancellation."""
        found = scan(BARE_WITH_TOKEN.replace("catch (Exception ex)", "catch (ArgumentException ex)"))
        self.assertEqual(found, [])

    def test_bare_catch_after_a_guard_is_not_a_violation(self):
        found = scan(BARE_WITH_TOKEN.replace(
            "            catch (Exception ex)\n            {\n"
            "                return Results.BadRequest(new { error = ex.Message });\n",
            "            catch (OperationCanceledException) when (ct.IsCancellationRequested)\n"
            "            {\n                throw;\n            }\n"
            "            catch\n            {\n                return Results.BadRequest();\n"))
        self.assertEqual(found, [])


class GuardFilterTests(unittest.TestCase):
    """An earlier OperationCanceledException clause only guards if its filter holds on abort."""

    def _with_guard(self, guard_clause: str) -> list:
        return scan(BARE_WITH_TOKEN.replace(
            "            catch (Exception ex)",
            f"            {guard_clause}\n"
            "            {\n"
            "                return Results.StatusCode(499);\n"
            "            }\n"
            "            catch (Exception ex)",
        ))

    def test_positive_is_cancellation_requested_filter_guards(self):
        self.assertEqual(
            self._with_guard("catch (OperationCanceledException) when (ct.IsCancellationRequested)"), [])

    def test_unfiltered_guard_guards(self):
        self.assertEqual(self._with_guard("catch (OperationCanceledException)"), [])

    def test_negated_filter_does_not_guard(self):
        """`when (!ct.IsCancellationRequested)` is false exactly when the caller hung up, so the
        cancellation falls through to the generic catch."""
        self.assertEqual(
            len(self._with_guard("catch (OperationCanceledException) when (!ct.IsCancellationRequested)")), 1)

    def test_opaque_filter_does_not_guard(self):
        self.assertEqual(
            len(self._with_guard("catch (OperationCanceledException) when (ShouldRethrow(ex))")), 1)


class ExplicitCancellationCheckTests(unittest.TestCase):
    def test_throw_if_cancellation_requested_is_a_cancellation_source(self):
        """The token need not reach an awaited call: an explicit check throws on its own."""
        found = scan(BARE_WITH_TOKEN.replace(
            "                var result = await service.RunAsync(request, ct);",
            "                ct.ThrowIfCancellationRequested();\n"
            "                var result = await service.RunAsync(request);"))
        self.assertEqual(len(found), 1)


class RawStringLiteralTests(unittest.TestCase):
    def test_quote_inside_a_raw_literal_does_not_hide_a_later_catch(self):
        """Scanning a raw literal as ordinary strings desyncs on an embedded quote and can
        swallow the following source — including the catch chain — as string content."""
        found = scan(BARE_WITH_TOKEN.replace(
            "                var result = await service.RunAsync(request, ct);",
            '                var note = """a " quoted " raw literal""";\n'
            "                var result = await service.RunAsync(request, ct);"))
        self.assertEqual(len(found), 1)

    def test_catch_written_inside_a_raw_literal_is_not_a_violation(self):
        found = scan(BARE_WITH_TOKEN.replace(
            "catch (Exception ex)", "catch (Exception ex) when (ex is ArgumentException)"
        ).replace(
            "                var result = await service.RunAsync(request, ct);",
            '                var sample = """try { } catch (Exception e) { }""";\n'
            "                var result = await service.RunAsync(request, ct);"))
        self.assertEqual(found, [])


class TokenNamingTests(unittest.TestCase):
    """Token names come from `CancellationToken foo` declarations, not from an allowlist."""

    def test_arbitrary_token_parameter_name_is_detected(self):
        found = scan(BARE_WITH_TOKEN
                     .replace("CancellationToken ct", "CancellationToken requestToken")
                     .replace("service.RunAsync(request, ct)", "service.RunAsync(request, requestToken)"))
        self.assertEqual(len(found), 1)

    def test_stopping_token_name_is_detected(self):
        found = scan(BARE_WITH_TOKEN
                     .replace("CancellationToken ct", "CancellationToken stoppingToken")
                     .replace("service.RunAsync(request, ct)", "service.RunAsync(request, stoppingToken)"))
        self.assertEqual(len(found), 1)

    def test_an_auth_token_is_not_mistaken_for_cancellation(self):
        """The old suffix allowlist would have matched `authToken` and flagged valid code."""
        found = scan(BARE_WITH_TOKEN
                     .replace("async (Service service, CancellationToken ct)",
                              "async (Service service, string authToken)")
                     .replace("service.RunAsync(request, ct)", "service.RunAsync(request, authToken)"))
        self.assertEqual(found, [])


class CatchAllTypeTests(unittest.TestCase):
    def test_system_exception_is_a_catch_all(self):
        """OperationCanceledException derives from SystemException."""
        found = scan(BARE_WITH_TOKEN.replace("catch (Exception ex)", "catch (SystemException ex)"))
        self.assertEqual(len(found), 1)

    def test_qualified_system_exception_is_a_catch_all(self):
        found = scan(BARE_WITH_TOKEN.replace("catch (Exception ex)", "catch (System.SystemException ex)"))
        self.assertEqual(len(found), 1)


class FilterPrecisionTests(unittest.TestCase):
    def test_excluding_only_task_canceled_is_not_trusted(self):
        """TaskCanceledException is a subclass, so a base OperationCanceledException still
        satisfies `is not TaskCanceledException` and reaches the generic handler."""
        found = scan(BARE_WITH_TOKEN.replace(
            "catch (Exception ex)", "catch (Exception ex) when (ex is not TaskCanceledException)"))
        self.assertEqual(len(found), 1)

    def test_excluding_operation_canceled_is_trusted(self):
        found = scan(BARE_WITH_TOKEN.replace(
            "catch (Exception ex)", "catch (Exception ex) when (ex is not OperationCanceledException)"))
        self.assertEqual(found, [])

    def test_nested_call_in_a_filter_is_still_parsed(self):
        """A filter the pattern cannot parse would drop the clause entirely rather than
        report it, so nesting has to be consumed."""
        found = scan(BARE_WITH_TOKEN.replace(
            "catch (Exception ex)", "catch (Exception ex) when (ShouldHandle(ex, GetContext()))"))
        self.assertEqual(len(found), 1)


class ExplicitThrowTests(unittest.TestCase):
    def test_hand_rolled_cancellation_throw_is_a_source(self):
        found = scan(BARE_WITH_TOKEN.replace(
            "                var result = await service.RunAsync(request, ct);",
            "                if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);\n"
            "                var result = await service.RunAsync(request);"))
        self.assertEqual(len(found), 1)


class ScanScopeTests(unittest.TestCase):
    def test_missing_scan_directory_is_an_error(self):
        """Silently scanning zero files would disable the gate exactly when scope drifts."""
        with tempfile.TemporaryDirectory() as temp_dir:
            with self.assertRaises(FileNotFoundError):
                guard.find_violations(Path(temp_dir), ("src/does/not/exist",))


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
