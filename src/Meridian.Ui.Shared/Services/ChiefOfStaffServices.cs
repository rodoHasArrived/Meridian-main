using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Application.OperationsContinuity;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meridian.Ui.Shared.Services;

public interface IChiefOfStaffRuntimeClient
{
    Task<ChiefOfStaffRuntimeResponseDto> ExecuteIntentAsync(
        ChiefOfStaffRuntimeRequestDto request,
        CancellationToken ct = default);

    Task<ChiefOfStaffRuntimeHealthDto> GetHealthAsync(CancellationToken ct = default);
}

public interface IChiefOfStaffApprovalRouter
{
    Task<ChiefOfStaffApprovalResultDto> RouteApprovalAsync(
        ChiefOfStaffApprovalCommandDto command,
        CancellationToken ct = default);
}

public interface IChiefOfStaffTraceStore
{
    Task SaveAsync(ChiefOfStaffTraceEnvelopeDto envelope, CancellationToken ct = default);

    Task<ChiefOfStaffTraceEnvelopeDto?> GetAsync(Guid sessionId, CancellationToken ct = default);
}

public interface IChiefOfStaffSessionService
{
    Task<ChiefOfStaffSessionDto> StartSessionAsync(ChiefOfStaffStartRequestDto request, CancellationToken ct = default);

    Task<ChiefOfStaffSessionDto?> GetSessionAsync(Guid sessionId, CancellationToken ct = default);

    Task<IReadOnlyList<ChiefOfStaffSessionSummaryDto>> ListSessionsAsync(ChiefOfStaffSessionQueryDto query, CancellationToken ct = default);

    Task<ChiefOfStaffSessionDto> SubmitDecisionAsync(Guid sessionId, ChiefOfStaffDecisionRequestDto request, CancellationToken ct = default);

    Task<ChiefOfStaffEvidenceExportDto> ExportTraceAsync(Guid sessionId, ChiefOfStaffTraceExportRequestDto request, CancellationToken ct = default);
}

public sealed class ChiefOfStaffRuntimeClient : IChiefOfStaffRuntimeClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<ChiefOfStaffOptions> _optionsMonitor;
    private readonly ILogger<ChiefOfStaffRuntimeClient> _logger;

    public ChiefOfStaffRuntimeClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<ChiefOfStaffOptions> optionsMonitor,
        ILogger<ChiefOfStaffRuntimeClient> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ChiefOfStaffRuntimeResponseDto> ExecuteIntentAsync(
        ChiefOfStaffRuntimeRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = _optionsMonitor.CurrentValue;
        var client = CreateClient(options);
        using var response = await client
            .PostAsJsonAsync("execute", request, ChiefOfStaffJsonContext.Default.ChiefOfStaffRuntimeRequestDto, ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning(
                "Chief of Staff runtime call failed with {StatusCode}: {Error}",
                (int)response.StatusCode,
                error);
            throw new InvalidOperationException($"Chief of Staff runtime returned {(int)response.StatusCode}.");
        }

        var payload = await response.Content
            .ReadFromJsonAsync(ChiefOfStaffJsonContext.Default.ChiefOfStaffRuntimeResponseDto, ct)
            .ConfigureAwait(false);
        return payload ?? throw new InvalidOperationException("Chief of Staff runtime returned an empty response.");
    }

    public async Task<ChiefOfStaffRuntimeHealthDto> GetHealthAsync(CancellationToken ct = default)
    {
        var options = _optionsMonitor.CurrentValue;
        try
        {
            var client = CreateClient(options);
            using var response = await client.GetAsync("health", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new ChiefOfStaffRuntimeHealthDto(
                    ChiefOfStaffRuntimeHealthStatusDto.Degraded,
                    $"Runtime returned {(int)response.StatusCode}.",
                    DateTimeOffset.UtcNow);
            }

            var health = await response.Content
                .ReadFromJsonAsync(ChiefOfStaffJsonContext.Default.ChiefOfStaffRuntimeHealthDto, ct)
                .ConfigureAwait(false);
            return health ?? new ChiefOfStaffRuntimeHealthDto(
                ChiefOfStaffRuntimeHealthStatusDto.Degraded,
                "Runtime returned an empty health payload.",
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Chief of Staff runtime health probe failed.");
            return new ChiefOfStaffRuntimeHealthDto(
                ChiefOfStaffRuntimeHealthStatusDto.Unavailable,
                ex.Message,
                DateTimeOffset.UtcNow);
        }
    }

    private HttpClient CreateClient(ChiefOfStaffOptions options)
    {
        var client = _httpClientFactory.CreateClient(nameof(ChiefOfStaffRuntimeClient));
        client.BaseAddress = new Uri(options.RuntimeBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        client.Timeout = options.RequestTimeout;
        return client;
    }
}

public sealed class ChiefOfStaffApprovalRouter : IChiefOfStaffApprovalRouter
{
    private readonly IOperationsContinuityWorkflowService _operationsWorkflowService;
    private readonly IReconciliationRunService? _reconciliationRunService;
    private readonly PromotionService? _promotionService;
    private readonly ILogger<ChiefOfStaffApprovalRouter> _logger;

    public ChiefOfStaffApprovalRouter(
        IOperationsContinuityWorkflowService operationsWorkflowService,
        ILogger<ChiefOfStaffApprovalRouter> logger,
        IReconciliationRunService? reconciliationRunService = null,
        PromotionService? promotionService = null)
    {
        _operationsWorkflowService = operationsWorkflowService ?? throw new ArgumentNullException(nameof(operationsWorkflowService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reconciliationRunService = reconciliationRunService;
        _promotionService = promotionService;
    }

    public async Task<ChiefOfStaffApprovalResultDto> RouteApprovalAsync(
        ChiefOfStaffApprovalCommandDto command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OperationsContinuityWorkflowId is Guid workflowId)
        {
            if (command.OperationsWorkflowVersion is null)
            {
                return new ChiefOfStaffApprovalResultDto(
                    Success: false,
                    Routed: false,
                    Status: "ValidationFailed",
                    Detail: "Operations workflow version is required for continuity approval routing.");
            }

            var request = new OperationsApprovalDecisionRequestDto(
                ExpectedVersion: command.OperationsWorkflowVersion.Value,
                Actor: command.Actor,
                Reviewer: command.Actor,
                Rationale: command.Rationale ?? "Approved via Chief of Staff agent.",
                ReportPackId: command.ReportPackId ?? "chief-of-staff");
            var result = await _operationsWorkflowService
                .ApproveWorkflowAsync(workflowId, request, ct)
                .ConfigureAwait(false);
            if (!result.Success)
            {
                return new ChiefOfStaffApprovalResultDto(
                    Success: false,
                    Routed: true,
                    Status: result.ErrorCode ?? "WorkflowRejected",
                    Detail: result.ErrorMessage ?? "Operations continuity approval request was rejected.");
            }

            return new ChiefOfStaffApprovalResultDto(
                Success: true,
                Routed: true,
                Status: "Approved",
                Detail: "Operations continuity workflow approved.",
                AuditReference: result.Workflow?.Approvals.LastOrDefault()?.ApprovalId,
                WorkflowReference: result.Workflow?.WorkflowId.ToString());
        }

        if (_promotionService is not null && string.Equals(command.TargetWorkflow, "paper-trading-readiness", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Chief of Staff approval routed to promotion workflow action {ActionId} for session {SessionId}.",
                command.ActionId,
                command.SessionId);
            return new ChiefOfStaffApprovalResultDto(
                Success: true,
                Routed: true,
                Status: "Accepted",
                Detail: "Trading readiness handoff accepted for promotion workflow.");
        }

        if (_reconciliationRunService is not null &&
            string.Equals(command.TargetWorkflow, "accounting-reconciliation-review", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Chief of Staff approval acknowledged reconciliation workflow action {ActionId} for session {SessionId}.",
                command.ActionId,
                command.SessionId);
            return new ChiefOfStaffApprovalResultDto(
                Success: true,
                Routed: true,
                Status: "Accepted",
                Detail: "Reconciliation workflow action accepted.");
        }

        return new ChiefOfStaffApprovalResultDto(
            Success: false,
            Routed: false,
            Status: "UnsupportedRoute",
            Detail: "The selected action cannot be routed by the Chief of Staff approval router.");
    }
}

public sealed class FileChiefOfStaffTraceStore : IChiefOfStaffTraceStore
{
    private readonly string _traceDirectory;
    private readonly ILogger<FileChiefOfStaffTraceStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileChiefOfStaffTraceStore(
        string dataRoot,
        ILogger<FileChiefOfStaffTraceStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _traceDirectory = Path.Combine(dataRoot, "workstation", "chief-of-staff", "traces");
    }

    public async Task SaveAsync(ChiefOfStaffTraceEnvelopeDto envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var tracePath = ResolveTracePath(envelope.SessionId);
            var json = JsonSerializer.Serialize(envelope, ChiefOfStaffJsonContext.Default.ChiefOfStaffTraceEnvelopeDto);
            await AtomicFileWriter.WriteAsync(tracePath, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ChiefOfStaffTraceEnvelopeDto?> GetAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (sessionId == Guid.Empty)
        {
            return null;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var tracePath = ResolveTracePath(sessionId);
            if (!File.Exists(tracePath))
            {
                return null;
            }

            await using var stream = File.OpenRead(tracePath);
            return await JsonSerializer
                .DeserializeAsync(stream, ChiefOfStaffJsonContext.Default.ChiefOfStaffTraceEnvelopeDto, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to read Chief of Staff trace for session {SessionId}.", sessionId);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string ResolveTracePath(Guid sessionId)
        => Path.Combine(_traceDirectory, $"{sessionId:N}.json");
}

public sealed class ChiefOfStaffSessionService : IChiefOfStaffSessionService
{
    private readonly IChiefOfStaffRuntimeClient _runtimeClient;
    private readonly IChiefOfStaffApprovalRouter _approvalRouter;
    private readonly IChiefOfStaffTraceStore _traceStore;
    private readonly IEvidenceArtifactStore _evidenceArtifactStore;
    private readonly EvidenceGraphService _evidenceGraphService;
    private readonly IOperatorInboxService _operatorInboxService;
    private readonly IOptionsMonitor<ChiefOfStaffOptions> _optionsMonitor;
    private readonly ILogger<ChiefOfStaffSessionService> _logger;
    private readonly ConcurrentDictionary<Guid, ChiefOfStaffSessionDto> _sessions = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _sessionLocks = new();

    public ChiefOfStaffSessionService(
        IChiefOfStaffRuntimeClient runtimeClient,
        IChiefOfStaffApprovalRouter approvalRouter,
        IChiefOfStaffTraceStore traceStore,
        IEvidenceArtifactStore evidenceArtifactStore,
        EvidenceGraphService evidenceGraphService,
        IOperatorInboxService operatorInboxService,
        IOptionsMonitor<ChiefOfStaffOptions> optionsMonitor,
        ILogger<ChiefOfStaffSessionService> logger)
    {
        _runtimeClient = runtimeClient ?? throw new ArgumentNullException(nameof(runtimeClient));
        _approvalRouter = approvalRouter ?? throw new ArgumentNullException(nameof(approvalRouter));
        _traceStore = traceStore ?? throw new ArgumentNullException(nameof(traceStore));
        _evidenceArtifactStore = evidenceArtifactStore ?? throw new ArgumentNullException(nameof(evidenceArtifactStore));
        _evidenceGraphService = evidenceGraphService ?? throw new ArgumentNullException(nameof(evidenceGraphService));
        _operatorInboxService = operatorInboxService ?? throw new ArgumentNullException(nameof(operatorInboxService));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ChiefOfStaffSessionDto> StartSessionAsync(ChiefOfStaffStartRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OperatorRequest))
        {
            throw new ArgumentException("Operator request is required.", nameof(request));
        }

        var sessionId = Guid.NewGuid();
        var intent = ClassifyIntent(request.OperatorRequest);
        var runtimeRequest = new ChiefOfStaffRuntimeRequestDto(
            SessionId: sessionId,
            IntentKind: intent,
            OperatorRequest: request.OperatorRequest.Trim(),
            OperatorId: request.OperatorId,
            Workspace: request.Workspace,
            FundProfileId: request.FundProfileId,
            FundAccountId: request.FundAccountId,
            CorrelationId: request.CorrelationId,
            ContextTags: request.ContextTags ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var fallbackWarnings = new List<string>();
        ChiefOfStaffRuntimeResponseDto runtimeResponse;
        try
        {
            runtimeResponse = await _runtimeClient.ExecuteIntentAsync(runtimeRequest, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Chief of Staff runtime execution failed for session {SessionId}.", sessionId);
            fallbackWarnings.Add($"Runtime execution failed: {ex.Message}");
            var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["status"] = "runtime-unavailable",
                ["detail"] = ex.Message
            };
            runtimeResponse = new ChiefOfStaffRuntimeResponseDto(
                IntentKind: intent,
                MarkdownSummary: "Chief of Staff runtime is unavailable. Resolve runtime connectivity and retry.",
                StructuredPayload: JsonSerializer.SerializeToElement(
                    payload,
                    ChiefOfStaffJsonContext.Default.DictionaryStringString),
                Recommendations: [],
                Actions: [],
                EvidenceBundle: null,
                GeneratedAt: DateTimeOffset.UtcNow,
                Warnings: fallbackWarnings);
        }

        var evidenceBundle = await BuildEvidenceBundleAsync(intent, runtimeResponse, ct).ConfigureAwait(false);
        var allWarnings = runtimeResponse.Warnings.Concat(evidenceBundle.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var traceSummary = new ChiefOfStaffTraceSummaryDto(
            TraceId: $"cos-{sessionId:N}",
            RuntimeName: "chief-of-staff-runtime",
            RuntimeVersion: null,
            CapturedAt: runtimeResponse.GeneratedAt,
            WarningCodes: allWarnings);
        var status = ResolveStatus(evidenceBundle.CompletenessStatus, runtimeResponse.Warnings.Count > 0);
        var session = new ChiefOfStaffSessionDto(
            SessionId: sessionId,
            IntentKind: runtimeResponse.IntentKind,
            OperatorRequest: runtimeRequest.OperatorRequest,
            MarkdownSummary: runtimeResponse.MarkdownSummary,
            StructuredPayload: runtimeResponse.StructuredPayload,
            EvidenceBundle: evidenceBundle,
            Recommendations: runtimeResponse.Recommendations,
            Actions: runtimeResponse.Actions,
            TraceSummary: traceSummary,
            FreshnessAsOf: runtimeResponse.GeneratedAt,
            Status: status,
            PendingApproval: runtimeResponse.Actions.Any(static action => action.ApprovalRequired),
            RoutedWorkflowReferences: evidenceBundle.RelatedWorkflowIds,
            Warnings: allWarnings);

        if (_optionsMonitor.CurrentValue.EnableTraceRetention)
        {
            var traceEnvelope = new ChiefOfStaffTraceEnvelopeDto(
                SessionId: sessionId,
                TraceId: traceSummary.TraceId,
                RuntimeName: traceSummary.RuntimeName,
                RuntimeVersion: traceSummary.RuntimeVersion,
                CapturedAt: traceSummary.CapturedAt,
                RuntimeRequest: runtimeRequest,
                RuntimeResponse: runtimeResponse,
                WarningCodes: allWarnings);
            await _traceStore.SaveAsync(traceEnvelope, ct).ConfigureAwait(false);
        }

        _sessions[sessionId] = session;
        return session;
    }

    public Task<ChiefOfStaffSessionDto?> GetSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_sessions.TryGetValue(sessionId, out var session) ? session : null);
    }

    public Task<IReadOnlyList<ChiefOfStaffSessionSummaryDto>> ListSessionsAsync(ChiefOfStaffSessionQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var limit = Math.Clamp(query.Limit, 1, 200);
        var summaries = _sessions.Values
            .Where(session => query.Status is null || session.Status == query.Status)
            .Where(session => string.IsNullOrWhiteSpace(query.Workspace) ||
                              string.Equals(
                                  query.Workspace,
                                  ResolveWorkspaceForIntent(session.IntentKind),
                                  StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static session => session.FreshnessAsOf)
            .Take(limit)
            .Select(static session => new ChiefOfStaffSessionSummaryDto(
                session.SessionId,
                session.IntentKind,
                session.OperatorRequest,
                session.Status,
                session.FreshnessAsOf,
                session.PendingApproval,
                session.Warnings))
            .ToArray();
        return Task.FromResult<IReadOnlyList<ChiefOfStaffSessionSummaryDto>>(summaries);
    }

    public async Task<ChiefOfStaffSessionDto> SubmitDecisionAsync(
        Guid sessionId,
        ChiefOfStaffDecisionRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_sessions.TryGetValue(sessionId, out var existing))
        {
            throw new KeyNotFoundException($"Chief of Staff session '{sessionId}' was not found.");
        }

        var gate = _sessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(sessionId, out existing))
            {
                throw new KeyNotFoundException($"Chief of Staff session '{sessionId}' was not found.");
            }

            var routedReferences = existing.RoutedWorkflowReferences.ToList();
            var warnings = existing.Warnings.ToList();
            var status = request.Decision switch
            {
                ChiefOfStaffDecisionKindDto.Approve => ChiefOfStaffSessionStatusDto.Approved,
                ChiefOfStaffDecisionKindDto.Reject => ChiefOfStaffSessionStatusDto.Rejected,
                _ => ChiefOfStaffSessionStatusDto.Deferred
            };

            if (request.Decision == ChiefOfStaffDecisionKindDto.Approve && _optionsMonitor.CurrentValue.EnableDecisionRouting)
            {
                if (string.IsNullOrWhiteSpace(request.SelectedActionId))
                {
                    throw new ArgumentException("SelectedActionId is required when approving a session.", nameof(request));
                }

                var action = existing.Actions.FirstOrDefault(candidate =>
                    string.Equals(candidate.ActionId, request.SelectedActionId, StringComparison.OrdinalIgnoreCase));
                if (action is null)
                {
                    throw new KeyNotFoundException($"Action '{request.SelectedActionId}' was not found in session '{sessionId}'.");
                }

                var routing = await _approvalRouter
                    .RouteApprovalAsync(
                        new ChiefOfStaffApprovalCommandDto(
                            SessionId: sessionId,
                            ActionId: action.ActionId,
                            Actor: request.Actor,
                            Rationale: request.Rationale,
                            CorrelationId: request.CorrelationId,
                            TargetWorkflow: action.TargetWorkflow,
                            TargetRoute: action.TargetRoute,
                            RequiredSignoffRole: action.RequiredSignoffRole),
                        ct)
                    .ConfigureAwait(false);
                if (!routing.Success)
                {
                    warnings.Add(routing.Detail);
                    status = ChiefOfStaffSessionStatusDto.ReviewRequired;
                }
                else if (!string.IsNullOrWhiteSpace(routing.WorkflowReference))
                {
                    routedReferences.Add(routing.WorkflowReference);
                }
            }

            var updated = existing with
            {
                Status = status,
                PendingApproval = false,
                RoutedWorkflowReferences = routedReferences.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
            _sessions[sessionId] = updated;
            return updated;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ChiefOfStaffEvidenceExportDto> ExportTraceAsync(
        Guid sessionId,
        ChiefOfStaffTraceExportRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new KeyNotFoundException($"Chief of Staff session '{sessionId}' was not found.");
        }

        var trace = await _traceStore.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (trace is null)
        {
            throw new InvalidOperationException($"No trace envelope is available for session '{sessionId}'.");
        }

        var subject = new EvidenceSubjectDto(
            SubjectId: sessionId.ToString("N"),
            SubjectKind: EvidenceSubjectResolver.ChiefOfStaffSessionKind,
            Label: $"Chief of Staff session {sessionId:N}",
            Workspace: ResolveWorkspaceForIntent(session.IntentKind),
            Route: session.Actions.FirstOrDefault()?.TargetRoute,
            PageTag: "ChiefOfStaffPanel");
        var traceNode = new EvidenceNodeDto(
            EvidenceId: $"chief-of-staff:{sessionId:N}:trace",
            Subject: subject,
            Kind: "chief-of-staff-trace",
            Status: session.Status is ChiefOfStaffSessionStatusDto.Blocked
                ? EvidenceStatusDto.Blocked
                : EvidenceStatusDto.Ready,
            Freshness: new EvidenceFreshnessDto(trace.CapturedAt, false, null),
            SourceSystem: trace.RuntimeName,
            Summary: "Chief of Staff runtime trace envelope.",
            ArtifactRefs: [],
            RelatedWorkItemIds: session.EvidenceBundle.WorkItems.Select(static item => item.WorkItemId).ToArray());
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes: [traceNode],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(
                Score: 100,
                Status: traceNode.Status,
                RequiredIds: [traceNode.EvidenceId],
                ReadyIds: [traceNode.EvidenceId],
                MissingIds: [],
                StaleIds: [],
                BlockingWorkItemIds: []),
            Actions: [],
            Warnings: request.IncludeWarnings ? session.Warnings : []);
        var manifest = await _evidenceArtifactStore
            .WriteManifestAsync(packet, new EvidencePacketExportRequest(request.RequestedBy, request.Reason, request.IncludeWarnings), ct)
            .ConfigureAwait(false);
        return new ChiefOfStaffEvidenceExportDto(sessionId, manifest, session.TraceSummary);
    }

    private async Task<ChiefOfStaffEvidenceBundleDto> BuildEvidenceBundleAsync(
        ChiefOfStaffIntentKindDto intent,
        ChiefOfStaffRuntimeResponseDto runtimeResponse,
        CancellationToken ct)
    {
        var workItems = await _operatorInboxService.GetItemsAsync(ct).ConfigureAwait(false);
        var targetedWorkItems = FilterWorkItems(intent, workItems);
        var subjectKind = ResolveSubjectKind(intent);
        var subjectId = ResolveSubjectId(intent);
        var packet = await _evidenceGraphService.GetPacketAsync(subjectKind, subjectId, ct).ConfigureAwait(false);

        var runtimeBundle = runtimeResponse.EvidenceBundle;
        if (runtimeBundle is null)
        {
            return new ChiefOfStaffEvidenceBundleDto(
                Subjects: packet is null ? [] : [packet.Subject],
                Packets: packet is null ? [] : [packet],
                WorkItems: targetedWorkItems,
                RelatedReconciliationRunIds: [],
                RelatedWorkflowIds: [],
                TraceArtifacts: [],
                CompletenessStatus: packet?.Completeness.Status ?? EvidenceStatusDto.ReviewRequired,
                Warnings: packet?.Warnings ?? []);
        }

        return runtimeBundle with
        {
            Subjects = runtimeBundle.Subjects.Count > 0
                ? runtimeBundle.Subjects
                : packet is null ? [] : [packet.Subject],
            Packets = runtimeBundle.Packets.Count > 0
                ? runtimeBundle.Packets
                : packet is null ? [] : [packet],
            WorkItems = runtimeBundle.WorkItems.Count > 0
                ? runtimeBundle.WorkItems
                : targetedWorkItems,
            CompletenessStatus = packet?.Completeness.Status ?? runtimeBundle.CompletenessStatus
        };
    }

    private static IReadOnlyList<OperatorWorkItemDto> FilterWorkItems(
        ChiefOfStaffIntentKindDto intent,
        IReadOnlyList<OperatorWorkItemDto> items)
    {
        return intent switch
        {
            ChiefOfStaffIntentKindDto.AccountingReconciliationReview => items
                .Where(static item => item.Kind is OperatorWorkItemKindDto.ReconciliationBreak or OperatorWorkItemKindDto.LedgerPeriodClose)
                .ToArray(),
            ChiefOfStaffIntentKindDto.TradingReadinessReview => items
                .Where(static item => item.Kind is OperatorWorkItemKindDto.PaperReplay
                                                   or OperatorWorkItemKindDto.ExecutionControl
                                                   or OperatorWorkItemKindDto.BrokerageSync)
                .ToArray(),
            _ => items
        };
    }

    private static ChiefOfStaffSessionStatusDto ResolveStatus(EvidenceStatusDto completenessStatus, bool hasWarnings)
        => completenessStatus switch
        {
            EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing => ChiefOfStaffSessionStatusDto.Blocked,
            EvidenceStatusDto.ReviewRequired or EvidenceStatusDto.Stale => ChiefOfStaffSessionStatusDto.ReviewRequired,
            _ when hasWarnings => ChiefOfStaffSessionStatusDto.ReviewRequired,
            _ => ChiefOfStaffSessionStatusDto.AwaitingOperatorDecision
        };

    private static ChiefOfStaffIntentKindDto ClassifyIntent(string operatorRequest)
    {
        if (operatorRequest.Contains("reconcil", StringComparison.OrdinalIgnoreCase))
        {
            return ChiefOfStaffIntentKindDto.AccountingReconciliationReview;
        }

        if (operatorRequest.Contains("readiness", StringComparison.OrdinalIgnoreCase)
            || operatorRequest.Contains("paper", StringComparison.OrdinalIgnoreCase))
        {
            return ChiefOfStaffIntentKindDto.TradingReadinessReview;
        }

        if (operatorRequest.Contains("report", StringComparison.OrdinalIgnoreCase))
        {
            return ChiefOfStaffIntentKindDto.ReportPackApproval;
        }

        return ChiefOfStaffIntentKindDto.GeneralOperatorAssistance;
    }

    private static string ResolveSubjectKind(ChiefOfStaffIntentKindDto intent)
        => intent switch
        {
            ChiefOfStaffIntentKindDto.AccountingReconciliationReview => EvidenceSubjectResolver.ReconciliationReviewKind,
            ChiefOfStaffIntentKindDto.TradingReadinessReview => EvidenceSubjectResolver.PaperReadinessKind,
            ChiefOfStaffIntentKindDto.ReportPackApproval => EvidenceSubjectResolver.ReportPackKind,
            _ => EvidenceSubjectResolver.PaperReadinessKind
        };

    private static string ResolveSubjectId(ChiefOfStaffIntentKindDto intent)
        => intent switch
        {
            ChiefOfStaffIntentKindDto.AccountingReconciliationReview => "current",
            ChiefOfStaffIntentKindDto.TradingReadinessReview => "current",
            ChiefOfStaffIntentKindDto.ReportPackApproval => "current",
            _ => "current"
        };

    private static string ResolveWorkspaceForIntent(ChiefOfStaffIntentKindDto intent)
        => intent switch
        {
            ChiefOfStaffIntentKindDto.AccountingReconciliationReview => "Accounting",
            ChiefOfStaffIntentKindDto.TradingReadinessReview => "Trading",
            ChiefOfStaffIntentKindDto.ReportPackApproval => "Reporting",
            _ => "Strategy"
        };
}
