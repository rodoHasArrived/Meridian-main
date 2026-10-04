using Meridian.Application.Composition;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Decides whether a fault raised while starting hosted services must take the application down or
/// may be degraded past.
/// </summary>
/// <remarks>
/// <para>A host that starts background workers wants to tolerate one that fails: a projection pump
/// that cannot reach its database is a degraded feature, and taking the shell down over it serves
/// nobody. A startup guard raising from the same <c>IHostedService.StartAsync</c> is the opposite —
/// it has refused the composition — and tolerating it runs exactly the posture the guard exists to
/// reject.</para>
///
/// <para>Kept here rather than inline in the shell's catch clause so the rule is exercised by tests.
/// The desktop shell's startup path cannot be run off Windows, and an inline predicate there would
/// be reviewed but never executed — which is how the W9-GOV-008 multi-company refusal came to be
/// registered on that lane and swallowed by it in the same change.</para>
/// </remarks>
public static class HostStartupEscalation
{
    /// <summary>
    /// Whether <paramref name="exception"/> is a guard's refusal rather than a component's failure.
    /// </summary>
    /// <remarks>
    /// Unwraps aggregates and reflection wrappers: hosts may start services concurrently, and a
    /// refusal that arrives inside an <see cref="AggregateException"/> is still a refusal. Any
    /// refusal among several faults decides the whole batch, because there is no such thing as
    /// partially declining to serve.
    /// </remarks>
    public static bool IsRefusal(Exception? exception) => TryFindRefusal(exception) is not null;

    /// <summary>
    /// The refusal within <paramref name="exception"/>, or <c>null</c> when it is not one.
    /// </summary>
    /// <remarks>
    /// Returns the matched exception rather than a bool so a caller reporting the fault can show the
    /// guard's own message. The wrapper's message is not a substitute: an
    /// <see cref="AggregateException"/> reads "One or more errors occurred", and an ordinary wrapper
    /// carries whatever the wrapping layer said — neither is the remediation text the operator needs
    /// to act on. Keep logging the outer exception, which holds the context.
    /// </remarks>
    public static StartupRefusedException? TryFindRefusal(Exception? exception) => exception switch
    {
        null => null,
        StartupRefusedException refusal => refusal,
        AggregateException aggregate => aggregate.InnerExceptions
            .Select(TryFindRefusal)
            .FirstOrDefault(found => found is not null),
        _ => TryFindRefusal(exception.InnerException),
    };
}
