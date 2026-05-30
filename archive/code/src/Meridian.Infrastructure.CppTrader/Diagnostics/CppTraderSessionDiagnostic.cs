using Meridian.Infrastructure.CppTrader.Protocol;

namespace Meridian.Infrastructure.CppTrader.Diagnostics;

public sealed record CppTraderSessionDiagnostic(
    string SessionId,
    CppTraderSessionKind SessionKind,
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<string> Symbols,
    DateTimeOffset? LastHeartbeat,
    string? LastFault);
