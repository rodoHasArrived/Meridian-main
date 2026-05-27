---
name: csharp-nunit
description: 'Get best practices for NUnit unit testing, including data-driven tests'
---

# NUnit Best Practices

Your goal is to help me write effective unit tests with NUnit, covering both standard and data-driven testing approaches.

- Use `[TestFixture]` on classes and `[Test]` on methods.
- Follow AAA (Arrange-Act-Assert).
- Use setup/teardown attributes (`[SetUp]`, `[TearDown]`, `[OneTimeSetUp]`, `[OneTimeTearDown]`).
- Favor independent, single-behavior tests.
- Use data-driven attributes like `[TestCase]`, `[TestCaseSource]`, and `[ValueSource]`.
- Prefer `Assert.That` constraint model for readability.
- Use categories and metadata attributes (`[Category]`, `[Description]`, etc.) for organization.
