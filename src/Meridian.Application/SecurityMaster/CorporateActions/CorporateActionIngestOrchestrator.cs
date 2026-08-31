using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster.CorporateActions;

/// <summary>
/// Request for a corporate-action ingest sweep. When <see cref="Symbols"/> is null or empty
/// the sweep covers every active security with a ticker identifier. Announcements whose
/// distinct agreeing sources reach <see cref="MinimumSourcesToApply"/> are confidence-scored,
/// but production ingest still records a durable proposal for explicit operator acceptance.
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
    bool AutoApplied,
    Guid? ProposalId = null,
    long? Version = null,
    string? ProposalState = null,
    CorporateActionSourceProposalActionAvailabilityDto? ActionAvailability = null,
    string? ObservationSource = null,
    string? ObservationDescription = null,
    string? SourceEventId = null,
    string? SourceEventVersion = null,
    DateTimeOffset? ObservationObservedAtUtc = null,
    string? EvidenceHash = null,
    string? EvidenceReference = null,
    IReadOnlyList<CorporateActionDissentFieldDto>? DissentingFields = null,
    CorporateActionProviderReleaseStatusDto ProviderReleaseStatus = CorporateActionProviderReleaseStatusDto.ReviewOnly);

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
/// normalizes announcements, computes cross-provider consensus over full economic value keys,
/// and records every distinct observation in the durable proposal inbox. Provider agreement is
/// retained as evidence and never appends or approves accounting by itself. The legacy append path
/// remains only for hosts/tests that have not registered durable operations.
/// </summary>
public sealed class CorporateActionIngestOrchestrator
{
    private readonly IReadOnlyList<ICorporateActionProvider> _providers;
    private readonly ISecurityMasterStore _store;
    private readonly ISecurityMasterEventStore _eventStore;
    private readonly ISecurityMasterCorporateActionCommandService _commandService;
    private readonly ILogger<CorporateActionIngestOrchestrator> _logger;
    private readonly ICorporateActionOperationsService? _operationsService;

    public CorporateActionIngestOrchestrator(
        IEnumerable<ICorporateActionProvider> providers,
        ISecurityMasterStore store,
        ISecurityMasterEventStore eventStore,
        ISecurityMasterCorporateActionCommandService commandService,
        ILogger<CorporateActionIngestOrchestrator> logger,
        ICorporateActionOperationsService? operationsService = null)
    {
        _providers = (providers ?? throw new ArgumentNullException(nameof(providers))).ToArray();
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _operationsService = operationsService;
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

            var existingKeys = _operationsService is null
                ? (await _eventStore.LoadCorporateActionsAsync(record.SecurityId, ct).ConfigureAwait(false))
                    .Select(static action => (CorporateActionEventTypes.Normalize(action.EventType).ToUpperInvariant(), action.ExDate))
                    .ToHashSet()
                : [];

            var commands = new List<CorporateActionCommand>();
            foreach (var provider in _providers)
            {
                try
                {
                    var fetched = await provider.FetchAsync(ticker!, record.SecurityId, ct).ConfigureAwait(false);
                    foreach (var command in fetched)
                    {
                        if (command.SecurityId != record.SecurityId)
                        {
                            errors.Add(
                                $"{provider.ProviderId}/{ticker}: provider returned SecurityId '{command.SecurityId:D}' for mastered security '{record.SecurityId:D}'; observation quarantined.");
                            continue;
                        }

                        if (!string.Equals(command.SourceProvider, provider.ProviderId, StringComparison.Ordinal))
                        {
                            errors.Add(
                                $"{provider.ProviderId}/{ticker}: provider returned spoofed SourceProvider '{command.SourceProvider}'; observation quarantined.");
                            continue;
                        }

                        // Release eligibility belongs to the registered adapter, not to upstream
                        // command content that could otherwise attempt to elevate itself.
                        commands.Add(command with { ReleaseStatus = provider.ReleaseStatus });
                    }
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

            foreach (var eventGroup in commands
                         .Select(command => Normalize(command, record.Currency))
                         .Where(static command => !string.IsNullOrWhiteSpace(command.ActionType))
                         .GroupBy(static command =>
                             (ActionType: command.ActionType.ToUpperInvariant(), command.ExDate)))
            {
                if (_operationsService is null && existingKeys.Contains(eventGroup.Key))
                {
                    duplicates++;
                    continue;
                }

                var normalizedCandidates = eventGroup.ToArray();
                var candidateProposals = _operationsService is null
                    ? BuildProposals(
                        record.SecurityId,
                        ticker!,
                        normalizedCandidates,
                        request.MinimumSourcesToApply)
                    : BuildProviderObservationProposals(
                        record.SecurityId,
                        ticker!,
                        normalizedCandidates,
                        request.MinimumSourcesToApply);
                var ambiguousSyntheticKeys = candidateProposals
                    .Where(static proposal => string.IsNullOrWhiteSpace(proposal.SourceEventId))
                    .GroupBy(SyntheticProviderEventKey, StringComparer.Ordinal)
                    .Where(static group => group.Count() > 1)
                    .Select(static group => group.Key)
                    .ToHashSet(StringComparer.Ordinal);
                var reportedAmbiguities = new HashSet<string>(StringComparer.Ordinal);
                foreach (var candidate in candidateProposals)
                {
                    var proposal = candidate;
                    if (_operationsService is not null)
                    {
                        proposal = proposal with { AutoApplied = false };
                        var syntheticKey = SyntheticProviderEventKey(proposal);
                        if (string.IsNullOrWhiteSpace(proposal.SourceEventId)
                            && ambiguousSyntheticKeys.Contains(syntheticKey))
                        {
                            if (reportedAmbiguities.Add(syntheticKey))
                            {
                                errors.Add(
                                    $"{proposal.ObservationSource ?? proposal.WinningSource}/{proposal.Ticker}: " +
                                    "multiple provider observations have identical synthesized identity fields; " +
                                    "a native SourceEventId or stable observation discriminator is required.");
                            }

                            continue;
                        }

                        var proposedAction = ToDto(proposal);
                        var economicFingerprint = CorporateActionEconomicFingerprint.Compute(proposedAction);
                        if (!TryResolveProviderIdentity(
                                proposal,
                                economicFingerprint,
                                out var providerIdentity,
                                out var identityError))
                        {
                            errors.Add(
                                $"{proposal.ObservationSource ?? proposal.WinningSource}/{proposal.Ticker}: {identityError}");
                            continue;
                        }

                        if (!request.DryRun)
                        {
                            var proposedId = Guid.NewGuid();
                            CorporateActionSourceProposalDto durable;
                            try
                            {
                                durable = await _operationsService.RecordSourceProposalAsync(
                                    new RecordCorporateActionSourceProposalRequestDto(
                                        proposedAction,
                                        providerIdentity,
                                        Actor: string.IsNullOrWhiteSpace(request.Actor)
                                            ? "corporate-action-ingest-orchestrator"
                                            : request.Actor,
                                        ProposalId: proposedId,
                                        Reason: "Corporate-action provider observation staged for canonical-fact review.",
                                        CorrelationId: request.CorrelationId,
                                        ClaimedEconomicFingerprint: economicFingerprint,
                                        DisplayMetadata: new CorporateActionSourceDisplayMetadataDto(
                                            proposal.Ticker,
                                            proposal.WinningSource,
                                            proposal.AgreeingSources,
                                            proposal.DissentingSources,
                                            proposal.DissentingFields)),
                                    ct).ConfigureAwait(false);
                            }
                            catch (CorporateActionOperationException exception)
                            {
                                errors.Add(
                                    $"{providerIdentity.ProviderId}/{proposal.Ticker}: {exception.Message}");
                                _logger.LogWarning(
                                    exception,
                                    "Corporate action proposal persistence failed for {Ticker} from {ProviderId}",
                                    proposal.Ticker,
                                    providerIdentity.ProviderId);
                                continue;
                            }
                            if (durable.ProposalId != proposedId)
                            {
                                duplicates++;
                            }
                            else
                            {
                                staged++;
                            }

                            proposal = proposal with
                            {
                                ProposalId = durable.ProposalId,
                                Version = durable.Version,
                                ProposalState = durable.State,
                                ActionAvailability = durable.ActionAvailability,
                            };
                        }
                        else
                        {
                            staged++;
                        }

                        proposals.Add(proposal);
                        continue;
                    }

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
                                    Reason: "Corporate-action provider consensus ingest (legacy compatibility path).",
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

    private static IReadOnlyList<CorporateActionProposal> BuildProposals(
        Guid securityId,
        string ticker,
        IReadOnlyList<CorporateActionCommand> candidates,
        int minimumSourcesToApply)
    {
        // Preserve every economically distinct block. Consensus ranks presentation order, while
        // the other blocks remain dissent evidence rather than being collapsed into one row.
        var blocks = candidates
            .GroupBy(static command => ValueKey(command))
            .OrderByDescending(static block => block
                .Select(static command => command.SourceProvider)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count())
            .ThenByDescending(static block => block.Key, StringComparer.Ordinal)
            .ToArray();

        return blocks.Select(block =>
        {
            var representative = block.Last();
            var releaseStatus = block.All(HasAcceptanceGradeProviderEvidence)
                ? CorporateActionProviderReleaseStatusDto.AcceptanceEligible
                : CorporateActionProviderReleaseStatusDto.ReviewOnly;
            var winningValue = block.Key;
            var agreeing = block
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
                AutoApplied: releaseStatus == CorporateActionProviderReleaseStatusDto.AcceptanceEligible
                             && dissenting.Length == 0
                             && agreeing.Length >= Math.Max(1, minimumSourcesToApply),
                ProviderReleaseStatus: releaseStatus);
        }).ToArray();
    }

    private static IReadOnlyList<CorporateActionProposal> BuildProviderObservationProposals(
        Guid securityId,
        string ticker,
        IReadOnlyList<CorporateActionCommand> candidates,
        int minimumSourcesToApply)
    {
        // Provider event/version is the durable replay identity. Consensus is retained as display
        // evidence on every observation, but agreeing providers are never collapsed into one
        // synthetic source row: one provider fact may later be corrected independently of another.
        var blocks = candidates
            .GroupBy(static command => ValueKey(command))
            .OrderByDescending(static block => block
                .Select(static command => command.SourceProvider)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count())
            .ThenByDescending(static block => block.Key, StringComparer.Ordinal)
            .ToArray();

        var observations = new List<CorporateActionProposal>(candidates.Count);
        var dissentingFields = BuildDissentFields(candidates);
        foreach (var block in blocks)
        {
            var winningValue = block.Key;
            var agreeing = block
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
            var consensusSource = block.Last().SourceProvider;

            foreach (var observation in block.OrderBy(
                         static command => command.SourceProvider,
                         StringComparer.OrdinalIgnoreCase))
            {
                observations.Add(new CorporateActionProposal(
                    SecurityId: securityId,
                    Ticker: ticker,
                    ActionType: observation.ActionType,
                    ExDate: observation.ExDate,
                    RecordDate: observation.RecordDate,
                    PayableDate: observation.PayableDate,
                    Amount: observation.Amount,
                    Currency: observation.Currency,
                    SplitFromFactor: observation.SplitFromFactor,
                    SplitToFactor: observation.SplitToFactor,
                    WinningSource: consensusSource,
                    AgreeingSources: agreeing,
                    DissentingSources: dissenting,
                    AutoApplied: dissenting.Length == 0
                                 && agreeing.Length >= Math.Max(1, minimumSourcesToApply),
                    ObservationSource: observation.SourceProvider,
                    ObservationDescription: observation.Description,
                    SourceEventId: observation.SourceEventId,
                    SourceEventVersion: observation.SourceEventVersion,
                    ObservationObservedAtUtc: observation.ObservedAtUtc,
                    EvidenceHash: observation.EvidenceHash,
                    EvidenceReference: observation.EvidenceReference,
                    DissentingFields: dissenting.Length == 0 ? [] : dissentingFields,
                    ProviderReleaseStatus: observation.ReleaseStatus));
            }
        }

        return observations;
    }

    private static IReadOnlyList<CorporateActionDissentFieldDto> BuildDissentFields(
        IReadOnlyList<CorporateActionCommand> candidates)
    {
        var fields = new List<CorporateActionDissentFieldDto>();

        AddField(
            nameof(CorporateActionDto.DividendPerShare),
            static command => command.Amount?.ToString("G29", CultureInfo.InvariantCulture) ?? "null",
            static command => command.Amount);
        AddField(
            nameof(CorporateActionDto.Currency),
            static command => command.Currency ?? "null",
            static command => command.Currency);
        AddField(
            "SplitFromFactor",
            static command => command.SplitFromFactor?.ToString("G29", CultureInfo.InvariantCulture) ?? "null",
            static command => command.SplitFromFactor);
        AddField(
            "SplitToFactor",
            static command => command.SplitToFactor?.ToString("G29", CultureInfo.InvariantCulture) ?? "null",
            static command => command.SplitToFactor);
        AddField(
            nameof(CorporateActionDto.RecordDate),
            static command => command.RecordDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "null",
            static command => command.RecordDate);
        AddField(
            nameof(CorporateActionDto.PayDate),
            static command => command.PayableDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "null",
            static command => command.PayableDate);
        return fields;

        void AddField(
            string field,
            Func<CorporateActionCommand, string> canonicalValue,
            Func<CorporateActionCommand, object?> value)
        {
            if (candidates.Select(canonicalValue).Distinct(StringComparer.Ordinal).Take(2).Count() < 2)
            {
                return;
            }

            fields.Add(new CorporateActionDissentFieldDto(
                field,
                candidates
                    .OrderBy(static command => command.SourceProvider, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(canonicalValue, StringComparer.Ordinal)
                    .Select(command => new CorporateActionConflictCandidateDto(
                        command.SourceProvider,
                        JsonSerializer.SerializeToElement(value(command)),
                        ResolveCandidateEvidenceReference(command)))
                    .ToArray()));
        }
    }

    private static string? ResolveCandidateEvidenceReference(CorporateActionCommand command)
        => string.IsNullOrWhiteSpace(command.EvidenceReference)
            ? null
            : command.EvidenceReference.Trim();

    private static string ValueKey(CorporateActionCommand command)
        => string.Join(
            "|",
            command.Amount?.ToString("G29", CultureInfo.InvariantCulture) ?? "-",
            command.Currency?.ToUpperInvariant() ?? "-",
            command.SplitFromFactor?.ToString("G29", CultureInfo.InvariantCulture) ?? "-",
            command.SplitToFactor?.ToString("G29", CultureInfo.InvariantCulture) ?? "-",
            command.RecordDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-",
            command.PayableDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-");

    private static string SyntheticProviderEventKey(CorporateActionProposal proposal)
    {
        var source = (proposal.ObservationSource ?? proposal.WinningSource).Trim().ToUpperInvariant();
        var description = string.IsNullOrWhiteSpace(proposal.ObservationDescription)
            ? "-"
            : proposal.ObservationDescription.Trim().ToUpperInvariant();
        var observedAt = proposal.ObservationObservedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                         ?? "-";
        return string.Join(
            "|",
            source,
            proposal.Ticker.Trim().ToUpperInvariant(),
            CorporateActionEventTypes.Normalize(proposal.ActionType),
            proposal.ExDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            description,
            observedAt);
    }

    private static bool TryResolveProviderIdentity(
        CorporateActionProposal proposal,
        string economicFingerprint,
        out CorporateActionProviderEventIdentityDto identity,
        out string? error)
    {
        var providerId = (proposal.ObservationSource ?? proposal.WinningSource).Trim();
        if (string.IsNullOrWhiteSpace(providerId))
        {
            identity = null!;
            error = "ProviderId is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(proposal.SourceEventId)
            && !string.IsNullOrWhiteSpace(proposal.SourceEventVersion))
        {
            identity = null!;
            error = "SourceEventVersion cannot be supplied without a native SourceEventId.";
            return false;
        }

        var syntheticKey = SyntheticProviderEventKey(proposal);
        var syntheticKeyHash = Sha256Digest.ComputeUtf8(
            $"corporate-action:provider-observation-key:v2:{syntheticKey}");
        var observationHash = Sha256Digest.ComputeUtf8(
            string.Join(
                "|",
                "corporate-action:provider-observation:v2",
                syntheticKey,
                economicFingerprint,
                proposal.Amount?.ToString("G29", CultureInfo.InvariantCulture) ?? "-",
                proposal.Currency?.Trim().ToUpperInvariant() ?? "-",
                proposal.SplitFromFactor?.ToString("G29", CultureInfo.InvariantCulture) ?? "-",
                proposal.SplitToFactor?.ToString("G29", CultureInfo.InvariantCulture) ?? "-",
                proposal.RecordDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-",
                proposal.PayableDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-"));
        var hasRetainedEvidenceIdentity = !string.IsNullOrWhiteSpace(proposal.EvidenceHash)
                                          && !string.IsNullOrWhiteSpace(proposal.EvidenceReference);
        var sourceEventId = !string.IsNullOrWhiteSpace(proposal.SourceEventId)
            ? proposal.SourceEventId.Trim()
            : hasRetainedEvidenceIdentity
                ? $"evidence-{Sha256Digest.ComputeUtf8(proposal.EvidenceReference!.Trim())[..32]}"
                : $"synthetic-{syntheticKeyHash[..32]}";
        var sourceEventVersion = !string.IsNullOrWhiteSpace(proposal.SourceEventVersion)
            ? proposal.SourceEventVersion.Trim()
            : hasRetainedEvidenceIdentity
                ? $"evidence-{proposal.EvidenceHash!.Trim()}"
                : $"unverified-content-{observationHash[..24]}";
        identity = new CorporateActionProviderEventIdentityDto(
            providerId,
            sourceEventId,
            sourceEventVersion,
            proposal.ObservationObservedAtUtc ?? DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(proposal.EvidenceHash) ? null : proposal.EvidenceHash.Trim(),
            string.IsNullOrWhiteSpace(proposal.EvidenceReference) ? null : proposal.EvidenceReference.Trim(),
            proposal.ProviderReleaseStatus);
        error = null;
        return true;
    }

    private static CorporateActionCommand Normalize(CorporateActionCommand command, string defaultCurrency)
        => command with
        {
            ActionType = CorporateActionEventTypes.Normalize(command.ActionType),
            Currency = NormalizeCurrency(command.Currency) ??
                (command.Amount.HasValue ? NormalizeCurrency(defaultCurrency) : null)
        };

    private static bool HasAcceptanceGradeProviderEvidence(CorporateActionCommand command) =>
        command.ReleaseStatus == CorporateActionProviderReleaseStatusDto.AcceptanceEligible
        && !string.IsNullOrWhiteSpace(command.SourceEventId)
        && !string.IsNullOrWhiteSpace(command.SourceEventVersion)
        && command.EvidenceHash is { Length: 64 } evidenceHash
        && evidenceHash.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f')
        && CorporateActionEvidenceKinds.IsTrustedReference(command.EvidenceReference);

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
