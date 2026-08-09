namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Serializes every test class that reaches the process-wide <c>ApiClientService.Instance</c>,
/// directly or through a service built on it.
///
/// <para><c>ApiClientService.Configure</c> now publishes immutable endpoint generations and drains
/// request leases before retiring a generation's clients. Tests still require serialization because
/// compatibility callers share <c>ApiClientService.Instance</c> and <c>ApiClientSession.Cookies</c>;
/// one test changing the endpoint or session cookies would otherwise change another test's inputs.</para>
///
/// <para>Only a small subset of tests mutates the singleton, but every class listed against this
/// collection can observe its endpoint and cookie state. Membership is deliberately over-inclusive:
/// a class that merely names one of these services costs nothing by being serialized, whereas
/// omitting a real consumer reintroduces cross-test state leakage.</para>
///
/// <para>An assembly-level <c>CollectionBehavior(DisableTestParallelization = true)</c> would not
/// work here: <c>tests/xunit.runner.json</c> is copied into every test project by
/// <c>tests/Directory.Build.props</c> and sets <c>parallelizeTestCollections: true</c>, and xUnit
/// gives the configuration file precedence over assembly attributes. Collection membership is
/// honoured regardless of that setting.</para>
/// </summary>
[CollectionDefinition("ApiClientService singleton serial", DisableParallelization = true)]
public sealed class ApiClientSingletonCollection
{
}
