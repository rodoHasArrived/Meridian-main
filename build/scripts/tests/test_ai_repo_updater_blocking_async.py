from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "ai-repo-updater.py"
SPEC = importlib.util.spec_from_file_location("ai_repo_updater", SCRIPT_PATH)
assert SPEC and SPEC.loader
updater = importlib.util.module_from_spec(SPEC)
sys.modules["ai_repo_updater"] = updater
SPEC.loader.exec_module(updater)


def findings_for(source: str, category: str) -> list:
    """Run audit_code over a single synthetic C# file and return matching findings."""
    with tempfile.TemporaryDirectory() as temp_dir:
        root = Path(temp_dir)
        src = root / "src" / "Sample"
        src.mkdir(parents=True)
        (src / "Sample.cs").write_text(source, encoding="utf-8")

        report = updater.AuditReport()
        updater.audit_code(root, report)
        return [f for f in report.findings if f.category == category]


class BlockingAsyncDetectorTests(unittest.TestCase):
    """The detector previously reported only false positives; these pin the fixes."""

    def test_results_collection_property_is_not_flagged(self) -> None:
        # `.Results` is a collection, not Task.Result — the old regex lacked \b.
        source = """
public sealed class Gateway
{
    public async Task LoadAsync()
    {
        var optionDetails = await Task.WhenAll(optResult.Results.Select(async p => await FetchAsync(p)));
    }
}
"""
        self.assertEqual(findings_for(source, "blocking-async"), [])

    def test_result_guarded_by_completion_check_is_not_flagged(self) -> None:
        # Accessing .Result after IsCompletedSuccessfully cannot deadlock.
        source = """
public sealed class Runner
{
    public void Read(Task<int> resultTask)
    {
        if (resultTask.IsCompletedSuccessfully)
        {
            var result = resultTask.Result;
        }
    }
}
"""
        self.assertEqual(findings_for(source, "blocking-async"), [])

    def test_non_task_result_property_is_not_flagged(self) -> None:
        # `Evaluate(...)` returns a record struct whose member is named Result.
        # The old `"Task" in line` proxy let Task.FromResult vouch for it.
        source = """
public sealed class Throttle
{
    public Task<RiskValidationResult> EvaluateAsync(OrderRequest request)
    {
        return Task.FromResult(Evaluate(reserve: false).Result);
    }
}
"""
        self.assertEqual(findings_for(source, "blocking-async"), [])

    def test_genuine_unguarded_task_result_is_flagged(self) -> None:
        source = """
public sealed class Runner
{
    public void Read(Task<int> pendingTask)
    {
        var value = pendingTask.Result;
    }
}
"""
        found = findings_for(source, "blocking-async")
        self.assertEqual(len(found), 1)
        self.assertEqual(found[0].severity, "critical")

    def test_task_wait_is_still_flagged(self) -> None:
        source = """
public sealed class Runner
{
    public void Read(Task pendingTask)
    {
        pendingTask.Wait();
    }
}
"""
        self.assertEqual(len(findings_for(source, "blocking-async")), 1)


class SyncOverAsyncDetectorTests(unittest.TestCase):
    """`.GetAwaiter().GetResult()` was invisible to the old regex entirely."""

    def test_task_run_sync_over_async_is_critical(self) -> None:
        source = """
public sealed class StrategyFeatureModule
{
    private static object CreateEngine(IServiceProvider sp)
    {
        var config = Task.Run(() => configService.LoadConfigAsync()).GetAwaiter().GetResult();
        return config;
    }
}
"""
        found = findings_for(source, "sync-over-async")
        self.assertEqual(len(found), 1)
        self.assertEqual(found[0].severity, "critical")

    def test_plain_sync_over_async_is_warning(self) -> None:
        source = """
public sealed class BacktestProxy
{
    public BacktestResult Run(Action onProgress)
    {
        return RunAsync(onProgress).GetAwaiter().GetResult();
    }
}
"""
        found = findings_for(source, "sync-over-async")
        self.assertEqual(len(found), 1)
        self.assertEqual(found[0].severity, "warning")

    # The next three isolate one half of the suppression rule each, so a
    # regression in either half fails a distinct test instead of hiding.

    def test_dispose_bridge_is_suppressed_by_both_signals(self) -> None:
        source = """
public sealed class Watcher
{
    public void Dispose()
        => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
"""
        self.assertEqual(findings_for(source, "sync-over-async"), [])

    def test_non_dispose_callee_inside_dispose_is_suppressed(self) -> None:
        # Enclosing-method signal only: the callee is not named Dispose*.
        source = """
public sealed class ConnectionManager
{
    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }
}
"""
        self.assertEqual(findings_for(source, "sync-over-async"), [])

    def test_dispose_callback_in_lambda_is_suppressed(self) -> None:
        # Callee signal only: inside a DI lambda, no enclosing method name.
        source = """
public static class BrokerageServiceRegistration
{
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            lifetime.Register(() =>
            {
                store.DisposeAsync().AsTask().GetAwaiter().GetResult();
            });
        });
    }
}
"""
        self.assertEqual(findings_for(source, "sync-over-async"), [])

    def test_commented_and_string_occurrences_are_ignored(self) -> None:
        source = '''
public sealed class Docs
{
    // Never write RunAsync().GetAwaiter().GetResult() in new code.
    public string Hint() => "call .GetAwaiter().GetResult() is forbidden";
}
'''
        self.assertEqual(findings_for(source, "sync-over-async"), [])


if __name__ == "__main__":
    unittest.main()
