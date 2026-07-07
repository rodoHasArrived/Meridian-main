using Meridian.Platform.Results;

namespace Meridian.Application.Commands;

internal sealed class CliCommandRouteTable
{
    private readonly IReadOnlyList<CliCommandRoute> routes;

    public CliCommandRouteTable(params CliCommandRoute[] routes)
    {
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    public bool CanHandle(string[] args)
        => routes.Any(route => route.Matches(args));

    public Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct)
    {
        var route = routes.FirstOrDefault(route => route.Matches(args));
        return route is null
            ? Task.FromResult(CliResult.Fail(ErrorCode.Unknown))
            : route.ExecuteAsync(args, ct);
    }
}

internal sealed class CliCommandRoute
{
    private readonly Func<string[], bool> predicate;
    private readonly Func<string[], CancellationToken, Task<CliResult>> handler;

    private CliCommandRoute(
        Func<string[], bool> predicate,
        Func<string[], CancellationToken, Task<CliResult>> handler)
    {
        this.predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public static CliCommandRoute Flag(
        string flag,
        Func<string[], CancellationToken, Task<CliResult>> handler)
        => When(args => CliArguments.HasFlag(args, flag), handler);

    public static CliCommandRoute Flag(
        string flag,
        Func<string[], CliResult> handler)
        => Flag(flag, (args, _) => Task.FromResult(handler(args)));

    public static CliCommandRoute Flags(
        IReadOnlyList<string> flags,
        Func<string[], CancellationToken, Task<CliResult>> handler)
        => When(args => flags.Any(flag => CliArguments.HasFlag(args, flag)), handler);

    public static CliCommandRoute Flags(
        IReadOnlyList<string> flags,
        Func<string[], CliResult> handler)
        => Flags(flags, (args, _) => Task.FromResult(handler(args)));

    public static CliCommandRoute When(
        Func<string[], bool> predicate,
        Func<string[], CancellationToken, Task<CliResult>> handler)
        => new(predicate, handler);

    public bool Matches(string[] args)
        => predicate(args);

    public Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct)
        => handler(args, ct);
}
