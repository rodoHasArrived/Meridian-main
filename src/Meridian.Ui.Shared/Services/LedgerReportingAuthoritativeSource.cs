using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed record ReportingAuthoritativeSourceCapture(
    ReportingAuthoritativeSourceCheckpoint Checkpoint,
    ImmutableArray<IReadOnlyDictionary<string, string>> DatasetRows,
    ReportingCertifiedLedgerPresentationInput? CertifiedLedgerPresentation = null);

/// <summary>
/// Template identity supplied by the governed certification/revalidation path when an
/// authoritative source capture may require template-specific presentation evidence.
/// </summary>
public sealed record ReportingAuthoritativeSourceCaptureIntent(
    string TemplateId)
{
    public bool RequiresCertifiedLedgerPresentation { get; init; }

    public static ReportingAuthoritativeSourceCaptureIntent FromTemplate(
        ReportingTemplateMetadata template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new ReportingAuthoritativeSourceCaptureIntent(template.TemplateId)
        {
            RequiresCertifiedLedgerPresentation =
                template.Family == ReportingTemplateFamily.CapitalAccountStatement
        };
    }
}

/// <summary>
/// Durable, server-owned source boundary used by certification. Implementations must return the
/// exact rows represented by the checkpoint hash; callers cannot contribute or replace rows.
/// </summary>
public interface IReportingAuthoritativeSource
{
    ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(
        ReportingRunParametersDto parameters,
        ReportAccessQueryContext accessContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures the same template-neutral authoritative checkpoint while also retaining any
    /// explicitly required template presentation evidence. Existing sources that do not provide
    /// template-specific presentations retain their normal capture behavior.
    /// </summary>
    ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(
        ReportingRunParametersDto parameters,
        ReportAccessQueryContext accessContext,
        ReportingAuthoritativeSourceCaptureIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return CaptureAsync(parameters, accessContext, cancellationToken);
    }
}

public class ReportingAuthoritativeSourceUnavailableException : InvalidOperationException
{
    public ReportingAuthoritativeSourceUnavailableException(string message) : base(message)
    {
    }
}

/// <summary>
/// Captures an immutable as-of view from the durable ledger journal. Tenant ownership, fund
/// structure, organization, book, period, accounting basis, currency, selected dimensions, and
/// line-level dimensional stamps are all verified before a checkpoint is issued.
/// </summary>
public sealed class LedgerReportingAuthoritativeSource : IReportingAuthoritativeSource
{
    private const string SourceKind = "durable-ledger-journal";

    private readonly ILedgerJournalStore _journalStore;
    private readonly IFundProfileTenancyRegistry _tenancyRegistry;
    private readonly IFundStructureService _fundStructure;
    private readonly TimeProvider _timeProvider;
    private readonly ILedgerTaxLotDisposalHistory? _taxLotDisposalHistory;

    public LedgerReportingAuthoritativeSource(
        ILedgerJournalStore journalStore,
        IFundProfileTenancyRegistry tenancyRegistry,
        IFundStructureService fundStructure,
        TimeProvider? timeProvider = null,
        ILedgerTaxLotDisposalHistory? taxLotDisposalHistory = null)
    {
        _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
        _tenancyRegistry = tenancyRegistry ?? throw new ArgumentNullException(nameof(tenancyRegistry));
        _fundStructure = fundStructure ?? throw new ArgumentNullException(nameof(fundStructure));
        _timeProvider = timeProvider ?? TimeProvider.System;

        // Optional: a store without retained tax-lot history contributes no realized-gain rows, which
        // is exactly the behavior the pack had before. Falling back to the journal store keeps the
        // common case (one Postgres store implementing both) wired without extra registration.
        _taxLotDisposalHistory = taxLotDisposalHistory ?? journalStore as ILedgerTaxLotDisposalHistory;
    }

    public ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(
        ReportingRunParametersDto parameters,
        ReportAccessQueryContext accessContext,
        CancellationToken cancellationToken = default) =>
        CaptureCoreAsync(parameters, accessContext, intent: null, cancellationToken);

    public ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(
        ReportingRunParametersDto parameters,
        ReportAccessQueryContext accessContext,
        ReportingAuthoritativeSourceCaptureIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (string.IsNullOrWhiteSpace(intent.TemplateId))
        {
            throw new ArgumentException(
                "A governed template identity is required for a template-scoped authoritative capture.",
                nameof(intent));
        }

        return CaptureCoreAsync(parameters, accessContext, intent, cancellationToken);
    }

    private async ValueTask<ReportingAuthoritativeSourceCapture> CaptureCoreAsync(
        ReportingRunParametersDto parameters,
        ReportAccessQueryContext accessContext,
        ReportingAuthoritativeSourceCaptureIntent? intent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(accessContext);
        RequireBoundAccess(accessContext);
        ValidateParameterEnums(parameters);

        var tenantId = accessContext.TenantId!.Trim();
        var companyId = accessContext.CompanyId!.Trim();
        var fundId = Require(parameters.Scope.FundProfileId, "fund profile");
        var ownership = await _tenancyRegistry.ResolveAsync(fundId, cancellationToken).ConfigureAwait(false)
            ?? throw Unavailable($"Fund profile '{fundId}' has no durable tenant ownership record.");
        if (!ownership.IsHeldBy(tenantId)
            || !SameRequired(ownership.CompanyId, companyId))
        {
            throw new UnauthorizedAccessException(
                $"Fund profile '{fundId}' is not owned by the authenticated tenant and company.");
        }

        var book = await ResolveBookAsync(parameters, fundId, cancellationToken).ConfigureAwait(false);
        var basis = MapAccountingBasis(parameters.AccountingBasis);
        if (book.AccountingBasis != basis)
        {
            throw Unavailable(
                $"Ledger book '{book.LedgerBookId:D}' uses {book.AccountingBasis}, not requested basis {basis}.");
        }

        if (!string.Equals(book.BaseCurrency, parameters.PresentationCurrency.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw Unavailable(
                $"Presentation currency '{parameters.PresentationCurrency}' requires an authoritative FX snapshot; ledger book '{book.LedgerBookId:D}' is '{book.BaseCurrency}' and no FX source was certified.");
        }

        var cutoffUtc = EndOfUtcDate(parameters.AsOfDate);
        var graph = await _fundStructure.GetOrganizationStructureAsync(
            new OrganizationStructureQuery(
                ActiveOnly: true,
                AsOf: cutoffUtc),
            cancellationToken).ConfigureAwait(false);
        var organization = ResolveOrganization(graph, book.FundStructureNodeId);
        if (!string.Equals(organization.BaseCurrency, parameters.PresentationCurrency.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw Unavailable(
                $"Presentation currency '{parameters.PresentationCurrency}' does not match authoritative organization currency '{organization.BaseCurrency}', and no FX snapshot was certified.");
        }

        ValidateSelectedScope(parameters, graph, book.FundStructureNodeId, organization.OrganizationId);
        var period = await ResolvePeriodAsync(parameters, book, cancellationToken).ConfigureAwait(false);
        ValidatePeriod(parameters, period, book);

        var requiredDimensions = BuildRequiredDimensions(parameters, fundId, book.LedgerBookId, organization.OrganizationId);
        IReadOnlyList<LedgerJournalEntryRecord> records;
        try
        {
            records = await _journalStore.QueryAsync(
                new LedgerJournalEntryQuery(
                    LedgerBookId: book.LedgerBookId,
                    PeriodId: period.PeriodId,
                    LineDimensions: requiredDimensions,
                    OccurredTo: cutoffUtc),
                cancellationToken).ConfigureAwait(false);
        }
        catch (NotSupportedException exception)
        {
            throw Unavailable(
                $"The configured ledger journal store cannot provide an authoritative scoped as-of query: {exception.Message}");
        }

        var ordered = records
            .OrderBy(static record => record.GlobalSequence)
            .ThenBy(static record => record.Entry.JournalEntryId)
            .ToArray();
        ordered = FilterToCertifiedDimensions(
            ordered,
            period,
            book,
            basis,
            cutoffUtc,
            requiredDimensions);
        ValidateRecords(
            ordered,
            period,
            book,
            basis,
            cutoffUtc,
            requiredDimensions);

        var rows = BuildRows(ordered, book, period, fundId, organization.OrganizationId);
        if (parameters.Finality == ReportingFinalityDto.Final
            && rows.IsDefaultOrEmpty)
        {
            throw Unavailable(
                "Final reporting is blocked because the exact fund/book/period/basis/as-of source contains no ledger rows.");
        }

        var canonicalReportPack = ReportingCertifiedLedgerPresentationBinding.IsRequired(
                intent,
                parameters.OutputFormat)
            ? await BuildCanonicalLedgerReportPackAsync(
                    parameters,
                    fundId,
                    book,
                    period,
                    basis,
                    cutoffUtc,
                    requiredDimensions,
                    cancellationToken)
                .ConfigureAwait(false)
            : null;
        var highestSequence = ordered.Length == 0 ? 0 : ordered.Max(static record => record.GlobalSequence);
        var capturedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        var sourceId = $"ledger:{book.LedgerBookId:D}:{period.PeriodId:D}";
        var checkpointHash = ComputeCheckpointHash(
            tenantId,
            organization.OrganizationId.ToString("D"),
            companyId,
            fundId,
            book,
            period,
            basis,
            parameters,
            cutoffUtc,
            highestSequence,
            rows);
        var checkpointId = $"ledger-checkpoint-{checkpointHash[..32]}";
        var evidence = ImmutableArray.CreateBuilder<string>();
        evidence.Add($"reporting-source-checkpoint:{checkpointId}:{checkpointHash}");
        evidence.Add(
            $"ledger-source:{tenantId}:{organization.OrganizationId:D}:{companyId}:{fundId}:{book.LedgerBookId:D}:{period.PeriodId:D}:{basis}:{parameters.AsOfDate:yyyy-MM-dd}");
        evidence.Add($"ledger-sequence:{highestSequence.ToString(CultureInfo.InvariantCulture)}");
        evidence.Add($"ledger-rows:{rows.Length.ToString(CultureInfo.InvariantCulture)}");
        if (canonicalReportPack is not null)
        {
            evidence.Add(ReportingCertifiedLedgerPresentationBinding.BuildEvidenceId(canonicalReportPack));
        }
        var checkpoint = new ReportingAuthoritativeSourceCheckpoint(
            SourceKind,
            sourceId,
            tenantId,
            organization.OrganizationId.ToString("D"),
            companyId,
            fundId,
            book.LedgerBookId.ToString("D"),
            period.PeriodId.ToString("D"),
            basis.ToString(),
            parameters.AsOfDate,
            cutoffUtc,
            highestSequence,
            ordered.Length,
            rows.Length,
            checkpointId,
            checkpointHash,
            capturedAtUtc,
            evidence.ToImmutable());
        var presentation = canonicalReportPack is null
            ? null
            : new ReportingCertifiedLedgerPresentationInput(
                checkpointId,
                checkpointHash,
                DeterministicReportingCertifiedArtifactProducer.ComputeCertifiedRowsHash(rows),
                canonicalReportPack);
        return new ReportingAuthoritativeSourceCapture(checkpoint, rows, presentation);
    }

    private async Task<LedgerFinancialReportPack> BuildCanonicalLedgerReportPackAsync(
        ReportingRunParametersDto parameters,
        string fundId,
        LedgerBookRecord book,
        LedgerAccountingPeriod period,
        AccountingBasisKindDto basis,
        DateTimeOffset cutoffUtc,
        LedgerLineDimensionSet selectedDimensions,
        CancellationToken cancellationToken)
    {
        var baseDimensions = new LedgerLineDimensionSet(
            FundId: fundId,
            OrganizationId: selectedDimensions.OrganizationId,
            BookId: book.LedgerBookId.ToString("D"));
        IReadOnlyList<LedgerJournalEntryRecord> historicalRecords;
        try
        {
            historicalRecords = await _journalStore.QueryAsync(
                    new LedgerJournalEntryQuery(
                        LedgerBookId: book.LedgerBookId,
                        LineDimensions: baseDimensions,
                        OccurredTo: cutoffUtc),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException exception)
        {
            throw Unavailable(
                $"The configured ledger journal store cannot build the canonical partners-capital presentation from complete as-of history: {exception.Message}");
        }

        var orderedHistory = historicalRecords
            .OrderBy(static record => record.GlobalSequence)
            .ThenBy(static record => record.Entry.JournalEntryId)
            .ToArray();
        var sequences = new HashSet<long>();
        var ledger = new Meridian.Ledger.Ledger();
        foreach (var record in orderedHistory)
        {
            if (record.AccountingBasis != basis
                || record.Entry.Timestamp > cutoffUtc
                || record.GlobalSequence <= 0
                || !sequences.Add(record.GlobalSequence))
            {
                throw Unavailable(
                    $"Ledger journal record '{record.Entry.JournalEntryId:D}' is outside the canonical book/basis/as-of presentation checkpoint or has an invalid sequence.");
            }

            foreach (var line in record.Entry.Lines)
            {
                var dimensions = line.Dimensions
                    ?? throw Unavailable(
                        $"Ledger line '{line.EntryId:D}' lacks the immutable dimensional scope required for the canonical client presentation.");
                EnsureDimensionsMatch(dimensions, baseDimensions, line.EntryId);
            }

            try
            {
                ledger.Post(record.Entry);
            }
            catch (LedgerValidationException exception)
            {
                throw Unavailable(
                    $"Ledger journal record '{record.Entry.JournalEntryId:D}' cannot be replayed into the canonical client presentation: {exception.Message}");
            }
        }

        var periodStart = StartOfUtcDate(period.StartDate);
        var periodEnd = EndOfUtcDate(period.EndDate);
        var lockedPeriod = parameters.Finality == ReportingFinalityDto.Final
            ? new LockedAccountingPeriod(
                new LedgerBookKey(fundId, book.LedgerBookId.ToString("D")),
                period.PeriodId.ToString("D"),
                periodStart,
                periodEnd,
                period.ClosedAt ?? periodEnd,
                "accounting-close",
                "Durable hard close retained for certified reporting.")
            : null;
        var generatedAtUtc = period.ClosedAt?.ToUniversalTime() ?? cutoffUtc;
        var reportRequest = new LedgerReportPackRequest(
            reportId:
                $"ledger-report-pack-{book.LedgerBookId:N}-{period.PeriodId:N}-{parameters.AsOfDate:yyyyMMdd}",
            fundId,
            period.PeriodId.ToString("D"),
            periodStart,
            periodEnd,
            cutoffUtc,
            book.BaseCurrency,
            generatedBy: "meridian-reporting-authority",
            generatedAtUtc,
            lockedPeriod,
            lineDimensions: selectedDimensions);

        // The pack's tax-lot artifact has always accepted relief projections but never been given
        // any, so it shipped as a header with no rows. Rebuilding them from retained disposal
        // history against the same journals this checkpoint was taken over is what makes the
        // realized-gain and wash-sale columns report real numbers.
        var taxLotReliefProjections = await BuildTaxLotReliefProjectionsAsync(
                book.LedgerBookId,
                orderedHistory,
                cancellationToken)
            .ConfigureAwait(false);

        return LedgerReportPackBuilder.Build(
            ledger,
            reportRequest,
            taxLotReliefProjections: taxLotReliefProjections);
    }

    /// <summary>
    /// Rebuilds realized-gain relief projections for the disposals recorded against
    /// <paramref name="journals"/>. A disposal whose retained economics cannot produce a well-formed
    /// projection is omitted rather than failing the capture: an unreconstructable historical row
    /// must not block a governed pack from being produced at all.
    /// </summary>
    private async Task<IReadOnlyList<LedgerTaxLotReliefProjection>> BuildTaxLotReliefProjectionsAsync(
        Guid ledgerBookId,
        IReadOnlyList<LedgerJournalEntryRecord> journals,
        CancellationToken cancellationToken)
    {
        if (_taxLotDisposalHistory is null || journals.Count == 0)
        {
            return [];
        }

        IReadOnlyList<LedgerTaxLotDisposalHistoryRecord> disposals;
        try
        {
            disposals = await _taxLotDisposalHistory
                .GetTaxLotDisposalHistoryAsync(
                    ledgerBookId,
                    journals.Select(static record => record.Entry.JournalEntryId).ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            return [];
        }

        if (disposals.Count == 0)
        {
            return [];
        }

        var journalsById = journals
            .GroupBy(static record => record.Entry.JournalEntryId)
            .ToDictionary(static group => group.Key, static group => group.First().Entry);

        var projections = new List<LedgerTaxLotReliefProjection>(disposals.Count);
        foreach (var disposal in disposals)
        {
            if (!journalsById.TryGetValue(disposal.JournalEntryId, out var entry))
            {
                continue;
            }

            var projection = LedgerTaxLotReliefHistoryProjector.Project(new LedgerTaxLotDisposalHistory(
                disposal.MutationBatchId,
                disposal.JournalEntryId,
                disposal.Account,
                entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(entry.Timestamp.UtcDateTime),
                disposal.ReliefMethod,
                disposal.Lots,
                ResolveRecognizedGainOrLoss(entry),
                disposal.WashSaleBasisIncreases,
                disposal.MatchedReplacementQuantity));

            if (projection is not null)
            {
                projections.Add(projection);
            }
        }

        return projections;
    }

    /// <summary>
    /// The gain or loss this journal actually booked, read from its realized gain/loss lines. Any
    /// wash-sale deferral is excluded by construction (only the allowed portion was ever posted),
    /// which is why the rebuild adds the retained deferral back to recover economic proceeds.
    /// Accounts are matched by name so both the unscoped and per-financial-account variants count.
    /// </summary>
    private static decimal ResolveRecognizedGainOrLoss(JournalEntry entry)
    {
        var gain = 0m;
        var loss = 0m;
        foreach (var line in entry.Lines)
        {
            if (string.Equals(line.Account.Name, LedgerAccounts.RealizedGain.Name, StringComparison.Ordinal))
            {
                gain += line.Credit - line.Debit;
            }
            else if (string.Equals(line.Account.Name, LedgerAccounts.RealizedLoss.Name, StringComparison.Ordinal))
            {
                loss += line.Debit - line.Credit;
            }
        }

        return gain - loss;
    }

    private async Task<LedgerBookRecord> ResolveBookAsync(
        ReportingRunParametersDto parameters,
        string fundId,
        CancellationToken cancellationToken)
    {
        if (parameters.LedgerBook.LedgerBookId is { } bookId)
        {
            if (bookId == Guid.Empty)
            {
                throw Unavailable("The selected ledger book id is empty.");
            }

            var book = await _journalStore.GetLedgerBookAsync(bookId, cancellationToken).ConfigureAwait(false)
                ?? throw Unavailable($"Ledger book '{bookId:D}' was not found in the durable ledger store.");
            return EnsureBookFund(book, fundId);
        }

        var code = Require(parameters.LedgerBook.LedgerBookCode, "ledger book id or code");
        var books = await _journalStore.ListLedgerBooksAsync(
            fundProfileId: fundId,
            ct: cancellationToken).ConfigureAwait(false);
        var matches = books.Where(book =>
                string.Equals(book.LedgerBookId.ToString("D"), code, StringComparison.OrdinalIgnoreCase)
                || string.Equals(book.DisplayName, code, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            throw Unavailable(
                $"Ledger book selector '{code}' resolved {matches.Length} durable books for fund '{fundId}'; an exact unique book is required.");
        }

        return EnsureBookFund(matches[0], fundId);
    }

    private async Task<LedgerAccountingPeriod> ResolvePeriodAsync(
        ReportingRunParametersDto parameters,
        LedgerBookRecord book,
        CancellationToken cancellationToken)
    {
        var requested = Require(parameters.PeriodId, "accounting period");
        if (Guid.TryParse(requested, out var periodId))
        {
            var period = await _journalStore.GetPeriodAsync(periodId, cancellationToken).ConfigureAwait(false)
                ?? throw Unavailable($"Accounting period '{periodId:D}' was not found in the durable ledger store.");
            return period;
        }

        var periods = await _journalStore.ListPeriodsAsync(
            ledgerBookId: book.LedgerBookId,
            ct: cancellationToken).ConfigureAwait(false);
        var matches = periods.Where(period =>
                string.Equals(period.Label, requested, StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    $"{period.FiscalYear.ToString(CultureInfo.InvariantCulture)}-{period.PeriodNo.ToString("00", CultureInfo.InvariantCulture)}",
                    requested,
                    StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            throw Unavailable(
                $"Accounting period selector '{requested}' resolved {matches.Length} durable periods for ledger book '{book.LedgerBookId:D}'; an exact unique period is required.");
        }

        return matches[0];
    }

    private static LedgerBookRecord EnsureBookFund(LedgerBookRecord book, string fundId)
    {
        if (!string.Equals(book.FundProfileId, fundId, StringComparison.Ordinal))
        {
            throw Unavailable(
                $"Ledger book '{book.LedgerBookId:D}' belongs to fund '{book.FundProfileId}', not requested fund '{fundId}'.");
        }

        return book;
    }

    private static OrganizationSummaryDto ResolveOrganization(
        OrganizationStructureGraphDto graph,
        Guid scopedNodeId)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (!graph.Nodes.Any(node => node.NodeId == scopedNodeId))
        {
            throw Unavailable(
                $"Fund-structure node '{scopedNodeId:D}' was not found in the authoritative as-of structure graph.");
        }

        var nodes = graph.Nodes.ToDictionary(static node => node.NodeId);
        var parentLinks = graph.OwnershipLinks
            .GroupBy(static link => link.ChildNodeId)
            .ToDictionary(static group => group.Key, static group => group.Select(static link => link.ParentNodeId).ToArray());
        var pending = new Queue<Guid>();
        var visited = new HashSet<Guid>();
        var organizations = new HashSet<Guid>();
        pending.Enqueue(scopedNodeId);
        while (pending.TryDequeue(out var nodeId))
        {
            if (!visited.Add(nodeId))
            {
                continue;
            }

            if (nodes.TryGetValue(nodeId, out var node) && node.Kind == FundStructureNodeKindDto.Organization)
            {
                organizations.Add(node.NodeId);
            }

            if (parentLinks.TryGetValue(nodeId, out var parents))
            {
                foreach (var parent in parents)
                {
                    pending.Enqueue(parent);
                }
            }
        }

        if (organizations.Count != 1)
        {
            throw Unavailable(
                $"Fund-structure node '{scopedNodeId:D}' resolves {organizations.Count} authoritative organizations; exactly one is required.");
        }

        var organizationId = organizations.Single();
        return graph.Organizations.SingleOrDefault(organization => organization.OrganizationId == organizationId)
            ?? throw Unavailable(
                $"Organization '{organizationId:D}' has no authoritative organization summary.");
    }

    private static void ValidateSelectedScope(
        ReportingRunParametersDto parameters,
        OrganizationStructureGraphDto graph,
        Guid bookNodeId,
        Guid organizationId)
    {
        var expectedKind = parameters.Scope.EntityScopeKind switch
        {
            ReportingEntityScopeKindDto.Entity => FundStructureNodeKindDto.Entity,
            ReportingEntityScopeKindDto.Portfolio => FundStructureNodeKindDto.InvestmentPortfolio,
            ReportingEntityScopeKindDto.Investor => FundStructureNodeKindDto.Client,
            _ => (FundStructureNodeKindDto?)null
        };
        var selectedId = parameters.Scope.EntityScopeKind switch
        {
            ReportingEntityScopeKindDto.Entity => parameters.Scope.EntityId,
            ReportingEntityScopeKindDto.Portfolio => parameters.Scope.PortfolioId,
            ReportingEntityScopeKindDto.Investor => parameters.Scope.InvestorId,
            _ => null
        };

        if (expectedKind is null)
        {
            if (parameters.ConsolidationLevel != ReportingConsolidationLevelDto.Fund)
            {
                throw Unavailable(
                    $"Consolidation level '{parameters.ConsolidationLevel}' requires a matching entity, portfolio, or investor scope.");
            }
            return;
        }

        if (parameters.ConsolidationLevel.ToString() != parameters.Scope.EntityScopeKind.ToString())
        {
            throw Unavailable(
                $"Consolidation level '{parameters.ConsolidationLevel}' does not match selected scope '{parameters.Scope.EntityScopeKind}'.");
        }

        var selected = Require(selectedId, parameters.Scope.EntityScopeKind.ToString());
        if (!Guid.TryParse(selected, out var selectedNodeId))
        {
            // Investor and external portfolio identifiers may be ledger-native rather than graph
            // GUIDs. Their exact membership is still enforced by the mandatory line dimension.
            return;
        }

        var selectedNode = graph.Nodes.SingleOrDefault(node => node.NodeId == selectedNodeId)
            ?? throw Unavailable(
                $"Selected {parameters.Scope.EntityScopeKind} '{selected}' is not present in the authoritative as-of structure graph.");
        if (selectedNode.Kind != expectedKind)
        {
            throw Unavailable(
                $"Selected scope '{selected}' is {selectedNode.Kind}, not expected {expectedKind}.");
        }

        var organizationNode = graph.Nodes.SingleOrDefault(node =>
            node.NodeId == organizationId && node.Kind == FundStructureNodeKindDto.Organization)
            ?? throw Unavailable($"Organization '{organizationId:D}' is absent from the authoritative graph.");
        _ = organizationNode;
        if (!SharesAncestor(graph, selectedNodeId, organizationId)
            || (graph.Nodes.Single(node => node.NodeId == bookNodeId).Kind == FundStructureNodeKindDto.Fund
                && !SharesAncestor(graph, selectedNodeId, bookNodeId)))
        {
            throw Unavailable(
                $"Selected scope '{selected}' is not a member of the authoritative ledger-book fund branch.");
        }
    }

    private static bool SharesAncestor(
        OrganizationStructureGraphDto graph,
        Guid nodeId,
        Guid requiredAncestorId)
    {
        var parentLinks = graph.OwnershipLinks
            .GroupBy(static link => link.ChildNodeId)
            .ToDictionary(static group => group.Key, static group => group.Select(static link => link.ParentNodeId).ToArray());
        var pending = new Queue<Guid>();
        var visited = new HashSet<Guid>();
        pending.Enqueue(nodeId);
        while (pending.TryDequeue(out var current))
        {
            if (!visited.Add(current))
            {
                continue;
            }
            if (current == requiredAncestorId)
            {
                return true;
            }
            if (parentLinks.TryGetValue(current, out var parents))
            {
                foreach (var parent in parents)
                {
                    pending.Enqueue(parent);
                }
            }
        }
        return false;
    }

    private static void ValidatePeriod(
        ReportingRunParametersDto parameters,
        LedgerAccountingPeriod period,
        LedgerBookRecord book)
    {
        if (period.LedgerBookId != book.LedgerBookId)
        {
            throw Unavailable(
                $"Accounting period '{period.PeriodId:D}' is not bound to ledger book '{book.LedgerBookId:D}'.");
        }
        if (parameters.AsOfDate < period.StartDate || parameters.AsOfDate > period.EndDate)
        {
            throw Unavailable(
                $"As-of date '{parameters.AsOfDate:yyyy-MM-dd}' is outside accounting period '{period.PeriodId:D}' ({period.StartDate:yyyy-MM-dd} through {period.EndDate:yyyy-MM-dd}).");
        }
        if (parameters.Finality == ReportingFinalityDto.Final
            && !string.Equals(period.Status, "HardClosed", StringComparison.OrdinalIgnoreCase))
        {
            throw Unavailable(
                $"Final reporting is blocked because accounting period '{period.PeriodId:D}' is '{period.Status}', not HardClosed.");
        }
        if (parameters.Finality == ReportingFinalityDto.Final && !parameters.IncludeEvidenceAppendix)
        {
            throw Unavailable("Final reporting requires the immutable evidence appendix output.");
        }
    }

    private static LedgerLineDimensionSet BuildRequiredDimensions(
        ReportingRunParametersDto parameters,
        string fundId,
        Guid ledgerBookId,
        Guid organizationId)
    {
        var dimensions = parameters.Scope.Dimensions;
        EnsureOptionalMatches(dimensions?.FundId, fundId, "fund dimension");
        EnsureOptionalMatches(dimensions?.BookId, ledgerBookId.ToString("D"), "book dimension");
        EnsureOptionalMatches(dimensions?.OrganizationId, organizationId.ToString("D"), "organization dimension");

        var entityId = parameters.Scope.EntityScopeKind == ReportingEntityScopeKindDto.Entity
            ? Require(parameters.Scope.EntityId, "entity id")
            : dimensions?.EntityId;
        var portfolioId = parameters.Scope.EntityScopeKind == ReportingEntityScopeKindDto.Portfolio
            ? Require(parameters.Scope.PortfolioId, "portfolio id")
            : dimensions?.PortfolioId;
        var investorId = parameters.Scope.EntityScopeKind == ReportingEntityScopeKindDto.Investor
            ? Require(parameters.Scope.InvestorId, "investor id")
            : dimensions?.InvestorId;

        return new LedgerLineDimensionSet(
            fundId,
            entityId,
            dimensions?.SleeveId,
            dimensions?.StrategyId,
            investorId,
            dimensions?.CapitalAccountId,
            dimensions?.InstrumentId,
            dimensions?.TaxLotId,
            dimensions?.CostCenterId,
            dimensions?.CounterpartyId,
            dimensions?.ExternalGlDimensions,
            organizationId.ToString("D"),
            portfolioId,
            ledgerBookId.ToString("D"),
            dimensions?.AccountId,
            dimensions?.CustomerId,
            dimensions?.VendorId,
            dimensions?.ProjectId)
        {
            PositionId = dimensions?.PositionId
        };
    }

    private static void ValidateRecords(
        IReadOnlyList<LedgerJournalEntryRecord> records,
        LedgerAccountingPeriod period,
        LedgerBookRecord book,
        AccountingBasisKindDto basis,
        DateTimeOffset cutoffUtc,
        LedgerLineDimensionSet requiredDimensions)
    {
        var sequences = new HashSet<long>();
        foreach (var record in records)
        {
            if (record.PeriodId != period.PeriodId
                || record.AccountingBasis != basis
                || record.Entry.Timestamp > cutoffUtc
                || record.GlobalSequence <= 0
                || !sequences.Add(record.GlobalSequence))
            {
                throw Unavailable(
                    $"Ledger journal record '{record.Entry.JournalEntryId:D}' is outside the exact period/basis/as-of checkpoint or has an invalid sequence.");
            }

            foreach (var line in record.Entry.Lines)
            {
                var dimensions = line.Dimensions
                    ?? throw Unavailable(
                        $"Ledger line '{line.EntryId:D}' lacks the immutable dimensional scope required for certified reporting.");
                EnsureDimensionsMatch(dimensions, requiredDimensions, line.EntryId);
            }
        }
    }

    private static LedgerJournalEntryRecord[] FilterToCertifiedDimensions(
        IReadOnlyList<LedgerJournalEntryRecord> records,
        LedgerAccountingPeriod period,
        LedgerBookRecord book,
        AccountingBasisKindDto basis,
        DateTimeOffset cutoffUtc,
        LedgerLineDimensionSet requiredDimensions)
    {
        var sequences = new HashSet<long>();
        var filtered = new List<LedgerJournalEntryRecord>(records.Count);
        foreach (var record in records)
        {
            if (record.PeriodId != period.PeriodId
                || record.AccountingBasis != basis
                || record.Entry.Timestamp > cutoffUtc
                || record.GlobalSequence <= 0
                || !sequences.Add(record.GlobalSequence))
            {
                throw Unavailable(
                    $"Ledger journal record '{record.Entry.JournalEntryId:D}' is outside the exact period/basis/as-of checkpoint or has an invalid sequence.");
            }

            var matchingLines = new List<LedgerEntry>(record.Entry.Lines.Count);
            foreach (var line in record.Entry.Lines)
            {
                var dimensions = line.Dimensions
                    ?? throw Unavailable(
                        $"Ledger line '{line.EntryId:D}' lacks the immutable dimensional scope required for certified reporting.");
                EnsureRequiredMatches(dimensions.FundId, requiredDimensions.FundId!, line.EntryId, "fund");
                EnsureRequiredMatches(dimensions.OrganizationId, requiredDimensions.OrganizationId!, line.EntryId, "organization");
                EnsureRequiredMatches(dimensions.BookId, requiredDimensions.BookId!, line.EntryId, "book");
                if (MatchesSelectedDimensions(dimensions, requiredDimensions))
                {
                    matchingLines.Add(line);
                }
            }

            if (matchingLines.Count == 0)
            {
                continue;
            }

            filtered.Add(record with
            {
                Entry = new JournalEntry(
                    record.Entry.JournalEntryId,
                    record.Entry.Timestamp,
                    record.Entry.Description,
                    matchingLines,
                    record.Entry.Metadata)
            });
        }

        return filtered.ToArray();
    }

    private static bool MatchesSelectedDimensions(
        LedgerLineDimensionSet actual,
        LedgerLineDimensionSet expected) =>
        MatchesOptional(actual.EntityId, expected.EntityId)
        && MatchesOptional(actual.SleeveId, expected.SleeveId)
        && MatchesOptional(actual.StrategyId, expected.StrategyId)
        && MatchesOptional(actual.InvestorId, expected.InvestorId)
        && MatchesOptional(actual.CapitalAccountId, expected.CapitalAccountId)
        && MatchesOptional(actual.TaxLotId, expected.TaxLotId)
        && MatchesOptional(actual.CostCenterId, expected.CostCenterId)
        && MatchesOptional(actual.CounterpartyId, expected.CounterpartyId)
        && MatchesOptional(actual.PortfolioId, expected.PortfolioId)
        && MatchesOptional(actual.AccountId, expected.AccountId)
        && MatchesOptional(actual.CustomerId, expected.CustomerId)
        && MatchesOptional(actual.VendorId, expected.VendorId)
        && MatchesOptional(actual.ProjectId, expected.ProjectId)
        && (expected.InstrumentId is null || actual.InstrumentId == expected.InstrumentId)
        && (expected.PositionId is null || actual.PositionId == expected.PositionId)
        && expected.ExternalGlDimensions.All(pair =>
            actual.ExternalGlDimensions.TryGetValue(pair.Key, out var value)
            && string.Equals(value?.Trim(), pair.Value?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool MatchesOptional(string? actual, string? expected) =>
        string.IsNullOrWhiteSpace(expected)
        || string.Equals(actual?.Trim(), expected.Trim(), StringComparison.Ordinal);

    private static void EnsureDimensionsMatch(
        LedgerLineDimensionSet actual,
        LedgerLineDimensionSet expected,
        Guid lineId)
    {
        EnsureOptionalRequiredMatch(actual.FundId, expected.FundId, lineId, "fund");
        EnsureOptionalRequiredMatch(actual.EntityId, expected.EntityId, lineId, "entity");
        EnsureOptionalRequiredMatch(actual.SleeveId, expected.SleeveId, lineId, "sleeve");
        EnsureOptionalRequiredMatch(actual.StrategyId, expected.StrategyId, lineId, "strategy");
        EnsureOptionalRequiredMatch(actual.InvestorId, expected.InvestorId, lineId, "investor");
        EnsureOptionalRequiredMatch(actual.CapitalAccountId, expected.CapitalAccountId, lineId, "capital account");
        EnsureOptionalRequiredMatch(actual.TaxLotId, expected.TaxLotId, lineId, "tax lot");
        EnsureOptionalRequiredMatch(actual.CostCenterId, expected.CostCenterId, lineId, "cost center");
        EnsureOptionalRequiredMatch(actual.CounterpartyId, expected.CounterpartyId, lineId, "counterparty");
        EnsureOptionalRequiredMatch(actual.OrganizationId, expected.OrganizationId, lineId, "organization");
        EnsureOptionalRequiredMatch(actual.PortfolioId, expected.PortfolioId, lineId, "portfolio");
        EnsureOptionalRequiredMatch(actual.BookId, expected.BookId, lineId, "book");
        EnsureOptionalRequiredMatch(actual.AccountId, expected.AccountId, lineId, "account");
        EnsureOptionalRequiredMatch(actual.CustomerId, expected.CustomerId, lineId, "customer");
        EnsureOptionalRequiredMatch(actual.VendorId, expected.VendorId, lineId, "vendor");
        EnsureOptionalRequiredMatch(actual.ProjectId, expected.ProjectId, lineId, "project");
        if (expected.InstrumentId is not null && actual.InstrumentId != expected.InstrumentId)
        {
            throw Unavailable(
                $"Ledger line '{lineId:D}' instrument dimension '{actual.InstrumentId?.ToString("D") ?? "<missing>"}' does not match certified value '{expected.InstrumentId.Value:D}'.");
        }
        if (expected.PositionId is not null && actual.PositionId != expected.PositionId)
        {
            throw Unavailable(
                $"Ledger line '{lineId:D}' position dimension '{actual.PositionId?.ToString("D") ?? "<missing>"}' does not match certified value '{expected.PositionId.Value:D}'.");
        }
        foreach (var pair in expected.ExternalGlDimensions)
        {
            if (!actual.ExternalGlDimensions.TryGetValue(pair.Key, out var value)
                || !string.Equals(value?.Trim(), pair.Value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw Unavailable(
                    $"Ledger line '{lineId:D}' external GL dimension '{pair.Key}' does not match the certified value.");
            }
        }
    }

    private static void EnsureOptionalRequiredMatch(
        string? actual,
        string? expected,
        Guid lineId,
        string label)
    {
        if (!string.IsNullOrWhiteSpace(expected))
        {
            EnsureRequiredMatches(actual, expected.Trim(), lineId, label);
        }
    }

    private static ImmutableArray<IReadOnlyDictionary<string, string>> BuildRows(
        IReadOnlyList<LedgerJournalEntryRecord> records,
        LedgerBookRecord book,
        LedgerAccountingPeriod period,
        string fundId,
        Guid organizationId)
    {
        var rows = ImmutableArray.CreateBuilder<IReadOnlyDictionary<string, string>>();
        foreach (var record in records)
        {
            foreach (var line in record.Entry.Lines.OrderBy(static line => line.EntryId))
            {
                var dimensions = line.Dimensions!;
                var row = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["account"] = line.Account.Name,
                    ["accountType"] = line.Account.AccountType.ToString(),
                    ["accountingBasis"] = record.AccountingBasis.ToString(),
                    ["accountingPeriodId"] = period.PeriodId.ToString("D"),
                    ["aggregateId"] = record.AggregateId.ToString("D"),
                    ["bookId"] = book.LedgerBookId.ToString("D"),
                    ["companyFundId"] = fundId,
                    ["credit"] = line.Credit.ToString("G29", CultureInfo.InvariantCulture),
                    ["debit"] = line.Debit.ToString("G29", CultureInfo.InvariantCulture),
                    ["description"] = record.Entry.Description,
                    ["entryId"] = line.EntryId.ToString("D"),
                    ["fundId"] = fundId,
                    ["globalSequence"] = record.GlobalSequence.ToString(CultureInfo.InvariantCulture),
                    ["journalEntryId"] = record.Entry.JournalEntryId.ToString("D"),
                    ["netAmount"] = (line.Debit - line.Credit).ToString("G29", CultureInfo.InvariantCulture),
                    ["organizationId"] = organizationId.ToString("D"),
                    ["periodId"] = period.PeriodId.ToString("D"),
                    ["postingKind"] = record.PostingKind.ToString(),
                    ["timestampUtc"] = record.Entry.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                };
                AddOptional(row, "symbol", line.Account.Symbol);
                AddOptional(row, "financialAccountId", line.Account.FinancialAccountId);
                AddOptional(row, "entityId", dimensions.EntityId);
                AddOptional(row, "portfolioId", dimensions.PortfolioId);
                AddOptional(row, "investorId", dimensions.InvestorId);
                AddOptional(row, "sleeveId", dimensions.SleeveId);
                AddOptional(row, "strategyId", dimensions.StrategyId);
                AddOptional(row, "capitalAccountId", dimensions.CapitalAccountId);
                AddOptional(row, "instrumentId", dimensions.InstrumentId?.ToString("D"));
                AddOptional(row, "positionId", dimensions.PositionId?.ToString("D"));
                AddOptional(row, "taxLotId", dimensions.TaxLotId);
                AddOptional(row, "costCenterId", dimensions.CostCenterId);
                AddOptional(row, "counterpartyId", dimensions.CounterpartyId);
                AddOptional(row, "sourceEventId", record.SourceEventId?.ToString("D"));
                AddOptional(row, "sourceJournalEntryId", record.SourceJournalEntryId?.ToString("D"));
                AddOptional(row, "ruleId", record.RuleId);
                AddOptional(row, "ruleVersion", record.RuleVersion);
                foreach (var external in dimensions.ExternalGlDimensions.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    row[$"externalGl.{external.Key}"] = external.Value;
                }
                rows.Add(row);
            }
        }
        // The builder is created without a fixed capacity and filled dynamically, so its Capacity
        // over-allocates and does not equal Count; MoveToImmutable would throw. ToImmutable freezes
        // the exact contents regardless of capacity.
        return rows.ToImmutable();
    }

    private static string ComputeCheckpointHash(
        string tenantId,
        string organizationId,
        string companyId,
        string fundId,
        LedgerBookRecord book,
        LedgerAccountingPeriod period,
        AccountingBasisKindDto basis,
        ReportingRunParametersDto parameters,
        DateTimeOffset cutoffUtc,
        long highestSequence,
        ImmutableArray<IReadOnlyDictionary<string, string>> rows)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("sourceKind", SourceKind);
            writer.WriteString("tenantId", tenantId);
            writer.WriteString("organizationId", organizationId);
            writer.WriteString("companyId", companyId);
            writer.WriteString("fundId", fundId);
            writer.WriteString("ledgerBookId", book.LedgerBookId);
            writer.WriteString("accountingPeriodId", period.PeriodId);
            writer.WriteString("accountingBasis", basis.ToString());
            writer.WriteString("asOfDate", parameters.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteString("cutoffUtc", cutoffUtc.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteNumber("periodVersion", period.Version);
            writer.WriteNumber("highestGlobalSequence", highestSequence);
            writer.WriteStartArray("rows");
            foreach (var row in rows)
            {
                writer.WriteStartObject();
                foreach (var pair in row.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WriteString(pair.Key, pair.Value);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void ValidateParameterEnums(ReportingRunParametersDto parameters)
    {
        if (!Enum.IsDefined(parameters.Scope.EntityScopeKind)
            || !Enum.IsDefined(parameters.AccountingBasis)
            || !Enum.IsDefined(parameters.ConsolidationLevel)
            || !Enum.IsDefined(parameters.OutputFormat)
            || !Enum.IsDefined(parameters.Finality))
        {
            throw Unavailable("Reporting parameters contain an unsupported enum value.");
        }
        if (string.IsNullOrWhiteSpace(parameters.PresentationCurrency))
        {
            throw Unavailable("A presentation currency is required for authoritative reporting.");
        }
    }

    private static AccountingBasisKindDto MapAccountingBasis(ReportingAccountingBasisDto basis) => basis switch
    {
        ReportingAccountingBasisDto.Gaap => AccountingBasisKindDto.Gaap,
        ReportingAccountingBasisDto.Tax => AccountingBasisKindDto.Tax,
        ReportingAccountingBasisDto.Cash => AccountingBasisKindDto.Cash,
        ReportingAccountingBasisDto.Statutory => AccountingBasisKindDto.Statutory,
        _ => AccountingBasisKindDto.Primary
    };

    private static DateTimeOffset EndOfUtcDate(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc), TimeSpan.Zero);

    private static DateTimeOffset StartOfUtcDate(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), TimeSpan.Zero);

    private static void RequireBoundAccess(ReportAccessQueryContext accessContext)
    {
        if (!accessContext.RequireBoundScope
            || string.IsNullOrWhiteSpace(accessContext.ActorPrincipalId)
            || string.IsNullOrWhiteSpace(accessContext.TenantId)
            || string.IsNullOrWhiteSpace(accessContext.CompanyId))
        {
            throw new UnauthorizedAccessException(
                "A server-bound actor, tenant, and company are required for authoritative reporting.");
        }
    }

    private static void EnsureOptionalMatches(string? value, string expected, string label)
    {
        if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value.Trim(), expected, StringComparison.Ordinal))
        {
            throw Unavailable($"Requested {label} '{value}' does not match authoritative value '{expected}'.");
        }
    }

    private static void EnsureRequiredMatches(string? value, string expected, Guid lineId, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value.Trim(), expected, StringComparison.Ordinal))
        {
            throw Unavailable(
                $"Ledger line '{lineId:D}' {label} dimension '{value ?? "<missing>"}' does not match certified value '{expected}'.");
        }
    }

    private static bool SameRequired(string? left, string right) =>
        !string.IsNullOrWhiteSpace(left)
        && string.Equals(left.Trim(), right, StringComparison.Ordinal);

    private static string Require(string? value, string label)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? throw Unavailable($"A {label} is required for authoritative reporting.")
            : normalized;
    }

    private static void AddOptional(IDictionary<string, string> row, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            row[key] = value.Trim();
        }
    }

    private static ReportingAuthoritativeSourceUnavailableException Unavailable(string message) => new(message);
}
