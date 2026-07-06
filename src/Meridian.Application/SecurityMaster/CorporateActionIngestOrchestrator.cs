using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Request for a corporate-action ingest sweep. When <see cref="Symbols"/> is null or empty
/// the sweep covers every active security with a ticker identifier. Announcements whose
/// distinct agreeing sources reach <see cref="MinimumSourcesToApply"/> are appended through
/// the Security Master corporate-action command service (skipped when <see cref="DryRun"/>);
/// everything else is returned as a staged proposal for operator review.
/// </summary>
public sealed record CorporateActionIngestRequest(
    IReadOnlyList<string>? Symbols = null,
    bool DryRun = false,
    int MinimumSourcesToApply = 2,
    string? Actor = null,
    string? CorrelationId = null);

/// <summary>
/// One normalized corporate-action announcement with its cross-provider consensus evidence.
/// </summary>
public sealed record CorporateActionProposal(
    Guid SecurityId,
    string Ticker,
    string ActionType,
    DateOnly ExDate,
    DateOnly? RecordDate,
    DateOnly? PayableDate,
    decimal? Amount,
    string? Currency,
    decimal? SplitFromFactor,
    decimal? SplitToFactor,
    string WinningSource,
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
/// normalizes announcements, computes cross-provider consensus per (security id, action type,
/// ex-date), deduplicates against corporate actions already recorded in the event store, and
/// appends announcements that reach the trusted-source threshold without dissent. Mirrors the
/// <see cref="EdgarIngestOrchestrator"/> orchestration pattern.
/// </summary>
public sealed class CorporateActionIngestOrchestrator
{
    private readonly IReadOnlyList<ICorporateActionProvider> _providers;
    private readonly ISecurityMasterStore _store;
    private readonly ISecurityMasterEventStore _eventStore;
    private readonly ISecurityMasterCorporateActionCommandService _commandService;
    private readonly ILogger<CorporateActionIngestOrchestrator> _logger;

    public CorporateActionIngestOrchestrator(
        IEnumerable<ICorporateActionProvider> providers,
        ISecurityMasterStore store,
        ISecurityMasterEventStore eventStore,
        ISecurityMasterCorporateActionCommandService commandService,
        ILogger<CorporateActionIngestOrchestrator> logger)
    {
        _providers = (providers ?? throw new ArgumentNullException(nameof(providers))).ToArray();
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
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
                .Select(static action => (CorporateActionEventTypes.Normalize(action.EventType).ToUpperInvariant(), action.ExDate))
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

            foreach (var group in commands
                         .Select(command => Normalize(command, record.Currency))
                         .Where(static command => !string.IsNullOrWhiteSpace(command.ActionType))
                         .GroupBy(static command =>
                             (ActionType: command.ActionType.ToUpperInvariant(), command.ExDate)))
            {
                if (existingKeys.Contains(group.Key))
                {
                    duplicates++;
                    continue;
                }

                var proposal = BuildProposal(record.SecurityId, ticker!, group.ToArray(), request.MinimumSourcesToApply);

                var appendSucceeded = true;
                if (proposal.AutoApplied && !request.DryRun)
                {
                    try
                    {
                        await _commandService.AppendAsync(
                            new SecurityMasterCorporateActionAppendRequestDto(
                                SecurityId: proposal.SecurityId,
                                CorporateAction: ToDto(proposal),
                                SourceSystem: proposal.WinningSource,
                                Actor: string.IsNullOrWhiteSpace(request.Actor)
                                    ? "corporate-action-ingest-orchestrator"
                                    : request.Actor,
                                SourceRecordId: $"{proposal.Ticker}:{proposal.ActionType}:{proposal.ExDate:yyyyMMdd}:{proposal.WinningSource}",
                                Reason: "Corporate-action provider consensus ingest.",
                                CorrelationId: request.CorrelationId),
                            ct).ConfigureAwait(false);
                    }
                    catch (ArgumentException ex)
                    {
                        appendSucceeded = false;
                        proposal = proposal with { AutoApplied = false };
                        errors.Add($"{proposal.WinningSource}/{proposal.Ticker}: {ex.Message}");
                        _logger.LogWarning(ex,
                            "Corporate action append failed for {Ticker} from {ProviderId}",
                            proposal.Ticker,
                            proposal.WinningSource);
                    }
                }

                if (proposal.AutoApplied && appendSucceeded)
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
        // Consensus block with the most distinct sources wins; source-count ties break
        // deterministically toward the ordinally-greatest value key (disputed proposals are
        // staged either way — the winner only selects which values the operator sees first).
        var blocks = candidates
            .GroupBy(static command => ValueKey(command))
            .OrderByDescending(static block => block
                .Select(static command => command.SourceProvider)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count())
            .ThenByDescending(static block => block.Key, StringComparer.Ordinal)
            .ToArray();

        var winner = blocks[0];
        var representative = winner.Last();
        var winningValue = ValueKey(representative);
        var agreeing = candidates
            .Where(command => string.Equals(ValueKey(command), winningValue, StringComparison.Ordinal))
            .Select(static command => command.SourceProvider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static source => source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var dissenting = candidates
            .Where(command => !string.Equals(ValueKey(command), winningValue, StringComparison.Ordinal))
            .Select(static command => command.SourceProvider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static source => source, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CorporateActionProposal(
            SecurityId: securityId,
            Ticker: ticker,
            ActionType: representative.ActionType,
            ExDate: representative.ExDate,
            RecordDate: representative.RecordDate,
            PayableDate: representative.PayableDate,
            Amount: representative.Amount,
            Currency: representative.Currency,
            SplitFromFactor: representative.SplitFromFactor,
            SplitToFactor: representative.SplitToFactor,
            WinningSource: representative.SourceProvider,
            AgreeingSources: agreeing,
            DissentingSources: dissenting,
            AutoApplied: dissenting.Length == 0 && agreeing.Length >= Math.Max(1, minimumSourcesToApply));
    }

    private static string ValueKey(CorporateActionCommand command)
        => string.Join(
            "|",
            command.Amount?.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) ?? "-",
            command.Currency?.ToUpperInvariant() ?? "-",
            command.SplitFromFactor?.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) ?? "-",
            command.SplitToFactor?.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) ?? "-");

    private static CorporateActionCommand Normalize(CorporateActionCommand command, string defaultCurrency)
        => command with
        {
            ActionType = CorporateActionEventTypes.Normalize(command.ActionType),
            Currency = NormalizeCurrency(command.Currency) ??
                (command.Amount.HasValue ? NormalizeCurrency(defaultCurrency) : null)
        };

    private static string? NormalizeCurrency(string? currency)
        => string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant();

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
    {
        var splitRatio = proposal.SplitFromFactor is > 0 && proposal.SplitToFactor is not null
            ? proposal.SplitToFactor / proposal.SplitFromFactor
            : null;
        var eventType = CorporateActionEventTypes.Normalize(proposal.ActionType);
        if (eventType == CorporateActionEventTypes.StockSplit && splitRatio is > 0m and < 1m)
            eventType = CorporateActionEventTypes.ReverseStockSplit;

        return new CorporateActionDto(
            CorpActId: Guid.NewGuid(),
            SecurityId: proposal.SecurityId,
            EventType: eventType,
            ExDate: proposal.ExDate,
            PayDate: proposal.PayableDate,
            DividendPerShare: proposal.Amount,
            Currency: proposal.Currency,
            SplitRatio: splitRatio,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null,
            RecordDate: proposal.RecordDate);
    }
}
