using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Request for a corporate-action ingest sweep. When <see cref="Symbols"/> is null or empty
/// the sweep covers every active security with a ticker identifier. Announcements whose
/// distinct agreeing sources reach <see cref="MinimumSourcesToApply"/> are appended to the
/// event store (skipped when <see cref="DryRun"/>); everything else is returned as a staged
/// proposal for operator review.
/// </summary>
public sealed record CorporateActionIngestRequest(
    IReadOnlyList<string>? Symbols = null,
    bool DryRun = false,
    int MinimumSourcesToApply = 2);

/// <summary>
/// One normalized corporate-action announcement with its cross-provider consensus evidence.
/// </summary>
public sealed record CorporateActionProposal(
    Guid SecurityId,
    string Ticker,
    string ActionType,
    DateOnly ExDate,
    DateOnly? PayableDate,
    decimal? Amount,
    string? Currency,
    decimal? SplitFromFactor,
    decimal? SplitToFactor,
    IReadOnlyList<string> AgreeingSources,
    IReadOnlyList<string> DissentingSources,
    bool AutoApplied);

public sealed record CorporateActionIngestResult(
    int SecuritiesScanned,
    int ProvidersQueried,
    int Applied,
    int Staged,
    int DuplicatesSkipped,
    IReadOnlyList<CorporateActionProposal> Proposals,
    IReadOnlyList<string> Errors);

/// <summary>
/// Fans out to every registered <see cref="ICorporateActionProvider"/> for mastered symbols,
/// normalizes announcements, computes cross-provider consensus per (action type, ex-date),
/// deduplicates against corporate actions already recorded in the event store, and appends
/// announcements that reach the consensus threshold. Mirrors the
/// <see cref="EdgarIngestOrchestrator"/> orchestration pattern.
/// </summary>
public sealed class CorporateActionIngestOrchestrator
{
    private readonly IReadOnlyList<ICorporateActionProvider> _providers;
    private readonly ISecurityMasterStore _store;
    private readonly ISecurityMasterEventStore _eventStore;
    private readonly ILogger<CorporateActionIngestOrchestrator> _logger;

    public CorporateActionIngestOrchestrator(
        IEnumerable<ICorporateActionProvider> providers,
        ISecurityMasterStore store,
        ISecurityMasterEventStore eventStore,
        ILogger<CorporateActionIngestOrchestrator> logger)
    {
        _providers = (providers ?? throw new ArgumentNullException(nameof(providers))).ToArray();
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CorporateActionIngestResult> IngestAsync(
        CorporateActionIngestRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var symbolFilter = request.Symbols is { Count: > 0 }
            ? new HashSet<string>(request.Symbols, StringComparer.OrdinalIgnoreCase)
            : null;

        var securities = (await _store.LoadActiveAsync(ct).ConfigureAwait(false))
            .Select(static record => (Record: record, Ticker: ResolveTicker(record)))
            .Where(pair => pair.Ticker is not null
                && (symbolFilter is null || symbolFilter.Contains(pair.Ticker)))
            .ToArray();

        var proposals = new List<CorporateActionProposal>();
        var errors = new List<string>();
        var applied = 0;
        var staged = 0;
        var duplicates = 0;

        foreach (var (record, ticker) in securities)
        {
            ct.ThrowIfCancellationRequested();

            var existing = await _eventStore.LoadCorporateActionsAsync(record.SecurityId, ct).ConfigureAwait(false);
            var existingKeys = existing
                .Select(static action => (action.EventType.ToUpperInvariant(), action.ExDate))
                .ToHashSet();

            var commands = new List<CorporateActionCommand>();
            foreach (var provider in _providers)
            {
                try
                {
                    commands.AddRange(await provider.FetchAsync(ticker!, record.SecurityId, ct).ConfigureAwait(false));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add($"{provider.ProviderId}/{ticker}: {ex.Message}");
                    _logger.LogWarning(ex,
                        "Corporate action fetch failed for {Ticker} from {ProviderId}", ticker, provider.ProviderId);
                }
            }

            foreach (var group in commands.GroupBy(static command =>
                         (ActionType: command.ActionType.ToUpperInvariant(), command.ExDate)))
            {
                if (existingKeys.Contains(group.Key))
                {
                    duplicates++;
                    continue;
                }

                var proposal = BuildProposal(record.SecurityId, ticker!, group.ToArray(), request.MinimumSourcesToApply);

                if (proposal.AutoApplied && !request.DryRun)
                {
                    await _eventStore.AppendCorporateActionAsync(ToDto(proposal), ct).ConfigureAwait(false);
                }

                if (proposal.AutoApplied)
                {
                    applied++;
                }
                else
                {
                    staged++;
                }

                proposals.Add(proposal);
            }
        }

        _logger.LogInformation(
            "Corporate action ingest: {Securities} securities, {Providers} providers, {Applied} applied, {Staged} staged, {Duplicates} duplicates skipped, {Errors} errors.",
            securities.Length, _providers.Count, applied, staged, duplicates, errors.Count);

        return new CorporateActionIngestResult(
            SecuritiesScanned: securities.Length,
            ProvidersQueried: _providers.Count,
            Applied: applied,
            Staged: staged,
            DuplicatesSkipped: duplicates,
            Proposals: proposals,
            Errors: errors);
    }

    private static CorporateActionProposal BuildProposal(
        Guid securityId,
        string ticker,
        IReadOnlyList<CorporateActionCommand> candidates,
        int minimumSourcesToApply)
    {
        // Vote on the economic value: sources agreeing on the same normalized value form the
        // consensus block; the largest block wins (ties broken deterministically by value key).
        var blocks = candidates
            .GroupBy(static command => ValueKey(command))
            .OrderByDescending(static block => block.Select(static c => c.SourceProvider).Distinct(StringComparer.OrdinalIgnoreCase).Count())
            .ThenBy(static block => block.Key, StringComparer.Ordinal)
            .ToArray();

        var winner = blocks[0];
        var representative = winner.First();
        var agreeing = winner
            .Select(static command => command.SourceProvider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static source => source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var dissenting = candidates
            .Select(static command => command.SourceProvider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Except(agreeing, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static source => source, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CorporateActionProposal(
            SecurityId: securityId,
            Ticker: ticker,
            ActionType: representative.ActionType,
            ExDate: representative.ExDate,
            PayableDate: representative.PayableDate,
            Amount: representative.Amount,
            Currency: representative.Currency,
            SplitFromFactor: representative.SplitFromFactor,
            SplitToFactor: representative.SplitToFactor,
            AgreeingSources: agreeing,
            DissentingSources: dissenting,
            AutoApplied: dissenting.Length == 0 && agreeing.Length >= minimumSourcesToApply);
    }

    private static string ValueKey(CorporateActionCommand command)
        => string.Join(
            "|",
            command.Amount?.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) ?? "-",
            command.Currency?.ToUpperInvariant() ?? "-",
            command.SplitFromFactor?.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) ?? "-",
            command.SplitToFactor?.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) ?? "-");

    private static string? ResolveTicker(SecurityProjectionRecord record)
    {
        if (string.Equals(record.PrimaryIdentifierKind, "Ticker", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(record.PrimaryIdentifierValue))
        {
            return record.PrimaryIdentifierValue;
        }

        var now = DateTimeOffset.UtcNow;
        return record.Identifiers
            .FirstOrDefault(identifier => identifier.Kind == SecurityIdentifierKind.Ticker
                && identifier.ValidFrom <= now
                && (!identifier.ValidTo.HasValue || identifier.ValidTo.Value > now))
            ?.Value;
    }

    private static CorporateActionDto ToDto(CorporateActionProposal proposal)
        => new(
            CorpActId: Guid.NewGuid(),
            SecurityId: proposal.SecurityId,
            EventType: proposal.ActionType,
            ExDate: proposal.ExDate,
            PayDate: proposal.PayableDate,
            DividendPerShare: proposal.Amount,
            Currency: proposal.Currency,
            SplitRatio: proposal.SplitFromFactor is > 0 && proposal.SplitToFactor is not null
                ? proposal.SplitToFactor / proposal.SplitFromFactor
                : null,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);
}
