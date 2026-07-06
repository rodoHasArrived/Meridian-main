using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// A machine-proposed security-master draft for an unmastered symbol. The operator reviews
/// and submits it through the normal create workflow — this service never writes.
/// </summary>
public sealed record SecurityMasterDraftProposalDto(
    string Symbol,
    bool Resolved,
    Guid ProposedSecurityId,
    string AssetClass,
    string? DisplayName,
    string? Exchange,
    string? Currency,
    IReadOnlyList<SecurityIdentifierDto> Identifiers,
    string SourceSystem,
    string Provenance,
    string? Notes);

/// <summary>
/// Pre-builds security-master drafts for coverage gaps flagged by the RC001 sweep, using the
/// shared symbol-resolution spine (canonical registry first, then OpenFIGI) for identifiers
/// and display metadata. Drafts are explicitly marked machine-proposed so downstream
/// provenance never mistakes them for operator-entered data.
/// </summary>
public sealed class SecurityMasterDraftProposalService
{
    internal const string DraftSourceSystem = "coverage-draft";
    internal const string MachineProposedProvenance = "machine-proposed";

    private readonly ISymbolResolver? _symbolResolver;
    private readonly ILogger<SecurityMasterDraftProposalService> _logger;

    public SecurityMasterDraftProposalService(
        ILogger<SecurityMasterDraftProposalService> logger,
        ISymbolResolver? symbolResolver = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _symbolResolver = symbolResolver;
    }

    public async Task<SecurityMasterDraftProposalDto> BuildDraftAsync(string symbol, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var normalized = symbol.Trim().ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;

        SymbolResolution? resolution = null;
        if (_symbolResolver is not null)
        {
            try
            {
                resolution = await _symbolResolver.ResolveAsync(normalized, ct: ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Symbol resolution failed while drafting {Symbol}; producing a minimal draft", normalized);
            }
        }

        var identifiers = new List<SecurityIdentifierDto>
        {
            new(SecurityIdentifierKind.Ticker, resolution?.Ticker ?? normalized, true, now, null, null)
        };
        AddIdentifierIfPresent(identifiers, SecurityIdentifierKind.Isin, resolution?.Isin, now);
        AddIdentifierIfPresent(identifiers, SecurityIdentifierKind.Cusip, resolution?.Cusip, now);
        AddIdentifierIfPresent(identifiers, SecurityIdentifierKind.Sedol, resolution?.Sedol, now);
        AddIdentifierIfPresent(identifiers, SecurityIdentifierKind.Figi, resolution?.Figi, now);

        return new SecurityMasterDraftProposalDto(
            Symbol: normalized,
            Resolved: resolution is not null,
            ProposedSecurityId: Guid.NewGuid(),
            AssetClass: "Equity",
            DisplayName: resolution?.Name,
            Exchange: resolution?.Exchange,
            Currency: resolution?.Currency,
            Identifiers: identifiers,
            SourceSystem: DraftSourceSystem,
            Provenance: MachineProposedProvenance,
            Notes: resolution is null
                ? "No external resolution was available; identifiers and terms must be completed manually before approval."
                : "Machine-proposed from the symbol-resolution spine; review identifiers and classification before approval.");
    }

    private static void AddIdentifierIfPresent(
        List<SecurityIdentifierDto> identifiers,
        SecurityIdentifierKind kind,
        string? value,
        DateTimeOffset validFrom)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            identifiers.Add(new SecurityIdentifierDto(kind, value.Trim(), false, validFrom, null, null));
        }
    }
}
