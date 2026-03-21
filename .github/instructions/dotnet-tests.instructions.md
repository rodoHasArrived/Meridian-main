---
applyTo: "tests/**/*.cs"
---
# .NET test instructions

When editing C# test files in this repository:

1. Keep tests deterministic (no time/network/external dependency flakiness).
2. Prefer clear Arrange-Act-Assert structure.
3. Use existing test utilities and fixtures before introducing new helpers.
4. Add or update assertions to capture the reported regression path.
5. Ensure naming communicates behavior (method + condition + expectation).
6. Run at least the nearest test project and report exact command used.
