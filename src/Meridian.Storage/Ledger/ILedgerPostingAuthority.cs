using Meridian.Contracts.Ledger;
using Meridian.Contracts.FundStructure;
using Npgsql;

namespace Meridian.Storage.Ledger;

/// <summary>Checks governed facts while retaining their locks until the ledger transaction completes.</summary>
public interface ILedgerPostingAuthority
{
    string ControlName => string.Empty;
    Task<IAsyncDisposable> AcquireAsync(IReadOnlyList<LedgerPostingAuthorityContext> contexts, CancellationToken ct = default);
}

/// <summary>Lets native producers retain structure authority before taking global security locks.</summary>
public interface ILedgerStructurePostingAuthority
{
    Task<IAsyncDisposable> AcquireStructureAuthorityAsync(CancellationToken ct = default);
}

/// <summary>Read-only journal review using the same retained entity/legal/mapping authority as durable append.</summary>
public interface ILedgerEntityControlJournalValidation
{
    Task ValidateEntityPostingControlsAsync(LedgerJournalEntryWrite write, CancellationToken ct = default);
}

public sealed record LedgerPostingAuthorityContext(
    LedgerJournalEntryWrite Write,
    LedgerBookRecord Book,
    LedgerBookControlProfileDto Controls,
    string? TenantId)
{
    public IList<LedgerControlDecisionDto> Decisions { get; } = new List<LedgerControlDecisionDto>();
    public LegalEntityReadAuthorityProof? StructureAuthorityProof { get; private set; }
    public bool IsReviewOnly { get; internal set; }
    public IReadOnlyList<IntercompanyAgreementRevisionDto> VerifiedCanonicalAgreementRevisions { get; internal set; } = [];
    public void RetainStructureAuthority(IAsyncDisposable lease)
    {
        if (lease is ILegalEntityReadAuthorityLease authority && authority.StructureAuthorityProof.IsActive)
            StructureAuthorityProof = authority.StructureAuthorityProof;
    }
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
