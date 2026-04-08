---
name: csharp-tunit
description: 'Get best practices for TUnit unit testing, including data-driven tests'
---

# TUnit Best Practices

Your goal is to help me write effective unit tests with TUnit, covering both standard and data-driven testing approaches.

- Requires .NET 8.0+.
- Use `[Test]` and async fluent assertions (`await Assert.That(...)`).
- Use lifecycle hooks such as `[Before(Test)]`, `[After(Test)]`, `[Before(Class)]`, and `[After(Class)]`.
- Use `[Arguments]`, `[MethodData]`, and `[ClassData]` for data-driven tests.
- Leverage advanced features like `[Retry]`, `[Repeat]`, `[ParallelLimit]`, and `[DependsOn]` where appropriate.
- Keep tests isolated; parallel behavior should be explicitly managed with `[NotInParallel]` or limits.
