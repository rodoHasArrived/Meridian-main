---
name: csharp-xunit
description: 'Get best practices for XUnit unit testing, including data-driven tests'
---

# XUnit Best Practices

Your goal is to help me write effective unit tests with XUnit, covering both standard and data-driven testing approaches.

- Use a separate `[ProjectName].Tests` project and standard test packages.
- Use `[Fact]` for single-case tests and `[Theory]` with data source attributes for data-driven tests.
- Follow AAA (Arrange-Act-Assert).
- Prefer clear, focused, independent tests.
- Use constructor setup and `IDisposable` teardown patterns.
- Use fixtures (`IClassFixture`, `ICollectionFixture`) where shared context is needed.
- Use appropriate assertions (`Assert.Equal`, `Assert.ThrowsAsync`, etc.).
- Categorize with `[Trait]` and use `ITestOutputHelper` for diagnostics.
