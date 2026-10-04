namespace Meridian.Application.Composition;

/// <summary>
/// Thrown by a startup guard that has decided this composition must not run at all — as distinct
/// from a component that merely failed to start.
/// </summary>
/// <remarks>
/// <para><b>Why the distinction needs a type.</b> Hosts routinely tolerate a worker whose startup
/// throws: a projection or outbox pump that cannot reach its database is a degraded feature, and
/// taking the whole application down over it would be worse than continuing without it. The WPF
/// shell does exactly that, and the comment on its catch says so. A governance guard throwing from
/// the same <c>IHostedService.StartAsync</c> is the opposite case — it is not reporting that
/// something broke, it is refusing to serve — but with both raising a bare
/// <see cref="InvalidOperationException"/> the host has nothing to tell them apart by, so the
/// tolerant catch swallows the refusal and the deployment runs on precisely the posture the guard
/// exists to reject.</para>
///
/// <para>Derives from <see cref="InvalidOperationException"/> so every existing catch and assertion
/// that named that type keeps matching; the added type only lets a host that wants to escalate do
/// so.</para>
/// </remarks>
public class StartupRefusedException : InvalidOperationException
{
    public StartupRefusedException()
    {
    }

    public StartupRefusedException(string message)
        : base(message)
    {
    }

    public StartupRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
