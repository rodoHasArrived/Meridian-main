using System.Runtime.CompilerServices;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Resilience;

[assembly: TypeForwardedTo(typeof(IProviderConnectionDiagnosticsSource))]
[assembly: TypeForwardedTo(typeof(ProviderConnectionLifecycleState))]
[assembly: TypeForwardedTo(typeof(ProviderFailureKind))]
[assembly: TypeForwardedTo(typeof(WebSocketConnectionDiagnostics))]
