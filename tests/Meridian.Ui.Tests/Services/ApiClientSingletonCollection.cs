namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Serializes every test class that reaches the process-wide <c>ApiClientService.Instance</c>,
/// directly or through a service built on it.
///
/// <para><c>ApiClientService.Configure</c> replaces and <b>disposes</b> the shared
/// <c>HttpClient</c>. A consumer that has already read the old client into a local — as
/// <c>PostWithResponseAsync</c> does before awaiting <c>SendAsync</c> — then calls a disposed
/// client, and <c>HttpClient</c> raises <c>ObjectDisposedException</c> synchronously, ahead of any
/// cancellation handling. The transport wrapper only rethrows <c>OperationCanceledException</c>,
/// so the disposal is swallowed into a failed <c>ApiResponse</c> and surfaces as a null result
/// rather than a throw. That is what made
/// <c>SystemHealthServiceTests.TestConnectionAsync_WithProviderName_AcceptsValidProviders</c> fail
/// intermittently on CI while passing locally and on re-runs.</para>
///
/// <para>Only <c>ApiClientServiceTests</c> mutates the singleton, but every class listed against
/// this collection can observe the disposal, so they must not run concurrently with it. Membership
/// is deliberately over-inclusive: a class that merely names one of these services costs nothing by
/// being serialized, whereas omitting a real consumer reintroduces the race. The assembly runs in
/// roughly three seconds, so the throughput cost is immaterial.</para>
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
