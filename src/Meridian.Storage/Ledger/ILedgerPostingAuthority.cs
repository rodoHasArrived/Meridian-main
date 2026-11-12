using Meridian.Contracts.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

/// <summary>Checks governed facts while retaining their locks until the ledger transaction completes.</summary>
public interface ILedgerPostingAuthority
{
    string ControlName => string.Empty;
    Task<IAsyncDisposable> AcquireAsync(IReadOnlyList<LedgerPostingAuthorityContext> contexts, CancellationToken ct = default);
}

public sealed record LedgerPostingAuthorityContext(
    LedgerJournalEntryWrite Write,
    LedgerBookRecord Book,
    LedgerBookControlProfileDto Controls,
    string? TenantId)
{
    public IList<LedgerControlDecisionDto> Decisions { get; } = new List<LedgerControlDecisionDto>();
}

public interface ILedgerBookControlStore
{
    Task<LedgerBookControlProfileDto?> GetBookControlsAsync(Guid bookId, CancellationToken ct = default);
    Task<LedgerBookControlPreflightDto> PrepareBookControlsAsync(Guid bookId, CancellationToken ct = default);
    Task<LedgerBookControlProfileDto> ActivateBookControlsAsync(Guid bookId, long expectedVersion,
        string preflightDigest, string actor, string reason, string evidenceReference, CancellationToken ct = default);
}

/// <summary>Preflight participants must resolve their own authoritative facts, never caller claims.</summary>
public interface ILedgerBookControlPreflight
{
    string ControlName => string.Empty;
    Task<LedgerBookControlPreflightLease> CheckAsync(LedgerBookRecord book, string? tenantId, CancellationToken ct = default);
}

public sealed record LedgerBookControlPreflightLease(IReadOnlyList<LedgerControlDecisionDto> Decisions, IAsyncDisposable Lease);

public sealed class EmptyLedgerAuthorityLease : IAsyncDisposable
{
    public static EmptyLedgerAuthorityLease Instance { get; } = new();
    private EmptyLedgerAuthorityLease() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
