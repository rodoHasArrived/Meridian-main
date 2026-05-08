---
name: csharp-mstest
description: 'Get best practices for MSTest 3.x/4.x unit testing, including modern assertion APIs and data-driven tests'
---

# MSTest Best Practices (MSTest 3.x/4.x)

Your goal is to help me write effective unit tests with modern MSTest, using current APIs and best practices.

- Prefer sealed test classes and `[TestMethod]` with AAA naming patterns.
- Prefer constructors over `[TestInitialize]` unless async setup is required.
- Use modern `Assert` APIs (including `Throws`, `ThrowsExactly`, async variants, collection/string helpers).
- Prefer exception asserts over `[ExpectedException]`.
- For data-driven tests, use `[DataRow]` and `[DynamicData]`; prefer `ValueTuple` or `TestDataRow` over raw `object[]`.
- Keep tests deterministic and isolated.

See MSTest documentation for detailed `TestContext` usage:
https://learn.microsoft.com/dotnet/core/testing/unit-testing-mstest-writing-tests-testcontext
