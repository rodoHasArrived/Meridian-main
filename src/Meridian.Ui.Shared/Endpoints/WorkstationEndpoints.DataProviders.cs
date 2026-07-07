using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Application.Monitoring;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Application.ProviderRouting;
using Meridian.DataIntegration.Monitoring;
using Meridian.Reporting;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Identity.Auth;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.StrategyEngine;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Collectors;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Instruments.AssetOperations;
using Meridian.QuantScript.Compilation;
using Meridian.Storage.Export;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Query;
using Meridian.Storage.Services;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ContractSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Data provider workstation helpers: provider record/diagnostic builders, routing-binding
/// and representative-connection/trust selection, provider trust rationale and gate-impact,
/// and provider key/score/timestamp formatting. Split out of the WorkstationEndpoints core
/// partial as a cohesive capability group. Using directives mirror the core header.
/// </summary>
public static partial class WorkstationEndpoints
{
    private static WorkstationDataProviderRecord[] BuildWorkstationDataProviderRecords(
        ProviderMetricsStatus? metricsStatus,
        IReadOnlyList<ProviderConnectionRowDto> connectionRows,
        IReadOnlyList<ProviderConnectionDto> routingConnections,
        IReadOnlyList<ProviderBindingDto> routingBindings,
        IReadOnlyList<ProviderTrustSnapshotDto> trustSnapshots,
        bool exposeConnectionSummaries)
    {
        var metricLookup = metricsStatus?.Providers.ToDictionary(
            static metric => NormalizeProviderKey(metric.ProviderId),
            static metric => metric,
            StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ProviderMetrics>(StringComparer.OrdinalIgnoreCase);
        var connectionLookup = connectionRows.ToDictionary(
            static connection => NormalizeProviderKey(connection.ProviderId),
            static connection => connection,
            StringComparer.OrdinalIgnoreCase);
        var routingLookup = routingConnections
            .GroupBy(static connection => NormalizeProviderKey(connection.ProviderFamilyId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<ProviderConnectionDto>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var trustLookup = trustSnapshots
            .GroupBy(static snapshot => NormalizeProviderKey(snapshot.ProviderFamilyId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<ProviderTrustSnapshotDto>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var bindingLookup = routingBindings
            .GroupBy(static binding => NormalizeProviderKey(binding.ConnectionId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => (IReadOnlyList<ProviderBindingDto>)group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var providerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var providerId in metricLookup.Keys)
        {
            providerIds.Add(providerId);
        }

        foreach (var providerId in connectionLookup.Keys)
        {
            providerIds.Add(providerId);
        }

        foreach (var providerId in routingLookup.Keys)
        {
            providerIds.Add(providerId);
        }

        foreach (var providerId in trustLookup.Keys)
        {
            providerIds.Add(providerId);
        }

        return providerIds
            .Select(providerId =>
            {
                metricLookup.TryGetValue(providerId, out var metrics);
                connectionLookup.TryGetValue(providerId, out var connection);
                routingLookup.TryGetValue(providerId, out var routingConnectionsForProvider);
                trustLookup.TryGetValue(providerId, out var trustSnapshotsForProvider);

                var routingConnection = SelectRepresentativeRoutingConnection(routingConnectionsForProvider);
                var trustSnapshot = SelectRepresentativeTrustSnapshot(trustSnapshotsForProvider, routingConnection?.ConnectionId);
                var bindings = ResolveRoutingBindings(bindingLookup, routingConnectionsForProvider);
                var rationale = metrics is not null
                    ? BuildProviderTrustRationale(metrics)
                    : BuildProviderTrustRationaleFromConnection(connection, routingConnection, trustSnapshot);
                connection ??= exposeConnectionSummaries
                    ? BuildMetricsConnectionSummary(metrics, rationale)
                    : null;
                var displayName = ResolveDataProviderDisplayName(providerId, connection, routingConnection, metrics);
                var capability = ResolveDataProviderCapability(connection, routingConnection, metrics);
                var latency = metrics is not null ? $"{metrics.AverageLatencyMs:F0}ms p50" : "Latency not reported";
                var note = BuildDataProviderNote(metrics, connection, trustSnapshot, rationale);
                return new WorkstationDataProviderRecord(
                    ProviderId: connection?.ProviderId ?? providerId,
                    DisplayName: displayName,
                    Status: connection is not null ? connection.Health.ToString() : rationale.Status,
                    Capability: capability,
                    Latency: latency,
                    Note: note,
                    TrustScore: trustSnapshot is not null ? FormatScore(NormalizeScore(trustSnapshot.Score)) : rationale.TrustScore,
                    SignalSource: trustSnapshot is not null && trustSnapshot.Signals.Length > 0
                        ? string.Join(", ", trustSnapshot.Signals)
                        : rationale.SignalSource,
                    ReasonCode: rationale.ReasonCode,
                    RecommendedAction: connection?.RecommendedAction ?? rationale.RecommendedAction,
                    GateImpact: rationale.GateImpact,
                    ConnectionSummary: connection,
                    RoutingSummary: new WorkstationDataProviderRoutingSummary(
                        ConnectionId: routingConnection?.ConnectionId,
                        ProviderFamilyId: routingConnection?.ProviderFamilyId ?? connection?.ProviderId,
                        ProductionReady: routingConnection?.ProductionReady,
                        CertificationFresh: trustSnapshot?.IsCertificationFresh,
                        BindingCount: bindings.Count,
                        FallbackRouteCount: bindings.Sum(static binding => binding.FailoverConnectionIds.Length),
                        HealthStatus: trustSnapshot?.HealthStatus ?? connection?.Health.ToString()),
                    Diagnostics: BuildWorkstationProviderDiagnostics(
                        providerId,
                        connection,
                        routingConnection,
                        trustSnapshot,
                        bindings,
                        metrics,
                        rationale));
            })
            .OrderBy(static provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ProviderConnectionRowDto? BuildMetricsConnectionSummary(
        ProviderMetrics? metrics,
        ProviderTrustRationalePayload rationale)
    {
        if (metrics is null)
        {
            return null;
        }

        var health = metrics.IsConnected
            ? ProviderContinuityHealthDto.Healthy
            : ProviderContinuityHealthDto.Degraded;
        return new ProviderConnectionRowDto(
            ProviderId: metrics.ProviderId,
            DisplayName: metrics.ProviderId,
            Capability: ProviderConnectionCapabilityDto.Data,
            CredentialState: ProviderCredentialStateDto.NotRequired,
            CredentialSource: ProviderCredentialSourceDto.None,
            VerificationState: metrics.IsConnected
                ? ProviderVerificationStateDto.Verified
                : ProviderVerificationStateDto.Failed,
            Health: health,
            FallbackActive: !metrics.IsConnected && metrics.ConnectionFailures > 0,
            LastVerifiedAt: null,
            LastSuccessfulAt: metrics.IsConnected ? metrics.Timestamp : null,
            LastFailureAt: !metrics.IsConnected && metrics.ConnectionFailures > 0 ? metrics.Timestamp : null,
            LastError: metrics.IsConnected ? null : rationale.RecommendedAction,
            MaskedKeyPreview: null,
            Environment: null,
            ExternalAccountId: null,
            AffectedWorkflows: ["Data"],
            RecommendedAction: rationale.RecommendedAction,
            ActionHref: "/settings/integrations");
    }

    private static WorkstationDataProviderRecord BuildFallbackDataProviderRecord(
        string providerId,
        string displayName,
        string status,
        string capability,
        string latency,
        string note,
        string trustScore,
        string signalSource,
        string reasonCode,
        string recommendedAction,
        string gateImpact)
        => new(
            ProviderId: providerId,
            DisplayName: displayName,
            Status: status,
            Capability: capability,
            Latency: latency,
            Note: note,
            TrustScore: trustScore,
            SignalSource: signalSource,
            ReasonCode: reasonCode,
            RecommendedAction: recommendedAction,
            GateImpact: gateImpact,
            ConnectionSummary: null,
            RoutingSummary: new WorkstationDataProviderRoutingSummary(
                ConnectionId: null,
                ProviderFamilyId: providerId,
                ProductionReady: null,
                CertificationFresh: null,
                BindingCount: 0,
                FallbackRouteCount: 0,
                HealthStatus: status),
            Diagnostics:
            [
                new WorkstationDataProviderDiagnostic("provider-health", "Provider health", status == "Healthy" ? "pass" : "warning", status == "Healthy" ? "Pass" : "Review", note),
                new WorkstationDataProviderDiagnostic("trust-state", "Trust state", status == "Healthy" ? "pass" : "warning", trustScore, $"{signalSource}. {recommendedAction}")
            ]);

    private static IReadOnlyList<ProviderBindingDto> ResolveRoutingBindings(
        IReadOnlyDictionary<string, IReadOnlyList<ProviderBindingDto>> bindingLookup,
        IReadOnlyList<ProviderConnectionDto>? routingConnections)
    {
        if (routingConnections is null || routingConnections.Count == 0)
        {
            return [];
        }

        var bindings = new List<ProviderBindingDto>();
        foreach (var routingConnection in routingConnections)
        {
            if (bindingLookup.TryGetValue(NormalizeProviderKey(routingConnection.ConnectionId), out var connectionBindings))
            {
                bindings.AddRange(connectionBindings);
            }
        }

        return bindings
            .DistinctBy(static binding => binding.BindingId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ProviderConnectionDto? SelectRepresentativeRoutingConnection(
        IReadOnlyList<ProviderConnectionDto>? routingConnections)
    {
        if (routingConnections is null || routingConnections.Count == 0)
        {
            return null;
        }

        return routingConnections
            .OrderByDescending(static connection => connection.Enabled)
            .ThenByDescending(static connection => connection.ProductionReady)
            .ThenBy(static connection => connection.ConnectionId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static ProviderTrustSnapshotDto? SelectRepresentativeTrustSnapshot(
        IReadOnlyList<ProviderTrustSnapshotDto>? trustSnapshots,
        string? preferredConnectionId)
    {
        if (trustSnapshots is null || trustSnapshots.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredConnectionId))
        {
            var exactMatch = trustSnapshots.FirstOrDefault(snapshot =>
                snapshot.ConnectionId.Equals(preferredConnectionId, StringComparison.OrdinalIgnoreCase));
            if (exactMatch is not null)
            {
                return exactMatch;
            }
        }

        return trustSnapshots
            .OrderByDescending(static snapshot => snapshot.IsHealthy)
            .ThenByDescending(static snapshot => snapshot.IsProductionReady)
            .ThenByDescending(static snapshot => snapshot.Score)
            .ThenBy(static snapshot => snapshot.ConnectionId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string ResolveDataProviderDisplayName(
        string providerId,
        ProviderConnectionRowDto? connection,
        ProviderConnectionDto? routingConnection,
        ProviderMetrics? metrics)
        => connection?.DisplayName
           ?? routingConnection?.DisplayName
           ?? metrics?.ProviderId
           ?? providerId;

    private static string ResolveDataProviderCapability(
        ProviderConnectionRowDto? connection,
        ProviderConnectionDto? routingConnection,
        ProviderMetrics? metrics)
    {
        if (connection is not null)
        {
            return connection.Capability switch
            {
                ProviderConnectionCapabilityDto.DataAndBrokerage => "Data + Brokerage",
                ProviderConnectionCapabilityDto.Brokerage => "Brokerage",
                ProviderConnectionCapabilityDto.AccountingSystem => "Accounting System",
                _ => "Data"
            };
        }

        return metrics?.ProviderType
            ?? routingConnection?.ConnectionType
            ?? "Provider";
    }

    private static string BuildDataProviderNote(
        ProviderMetrics? metrics,
        ProviderConnectionRowDto? connection,
        ProviderTrustSnapshotDto? trustSnapshot,
        ProviderTrustRationalePayload rationale)
    {
        if (metrics is not null)
        {
            return metrics.IsConnected
                ? $"Active subscriptions: {metrics.ActiveSubscriptions}. Quality score: {rationale.TrustScore}."
                : $"Provider disconnected. Last seen: {metrics.Timestamp:HH:mm} UTC.";
        }

        if (connection?.LastError is { Length: > 0 } error)
        {
            return error;
        }

        if (trustSnapshot is not null && trustSnapshot.Signals.Length > 0)
        {
            return $"Trust signals: {string.Join(", ", trustSnapshot.Signals)}.";
        }

        return rationale.RecommendedAction;
    }

    private static ProviderTrustRationalePayload BuildProviderTrustRationaleFromConnection(
        ProviderConnectionRowDto? connection,
        ProviderConnectionDto? routingConnection,
        ProviderTrustSnapshotDto? trustSnapshot)
    {
        if (connection is null)
        {
            if (trustSnapshot is not null)
            {
                var trustScore = FormatScore(NormalizeScore(trustSnapshot.Score));
                return new ProviderTrustRationalePayload(
                    Status: trustSnapshot.IsHealthy ? "Healthy" : "Warning",
                    TrustScore: trustScore,
                    SignalSource: trustSnapshot.Signals.Length > 0 ? string.Join(", ", trustSnapshot.Signals) : "Provider trust snapshot",
                    ReasonCode: trustSnapshot.IsHealthy ? "TRUST_SNAPSHOT_HEALTHY" : "TRUST_SNAPSHOT_REVIEW",
                    RecommendedAction: trustSnapshot.IsHealthy
                        ? "Provider trust snapshot is healthy."
                        : "Inspect routing trust signals before routing new workflow traffic.",
                    GateImpact: trustSnapshot.IsHealthy ? "Normal operation" : "Health gate needs review");
            }

            return new ProviderTrustRationalePayload(
                Status: "Warning",
                TrustScore: "Not reported",
                SignalSource: "Provider center bootstrap",
                ReasonCode: "PROVIDER_SUMMARY_PENDING",
                RecommendedAction: routingConnection?.Enabled == false
                    ? "Enable the routing connection before selecting this provider."
                    : "Configure provider credentials and routing before relying on this workflow.",
                GateImpact: routingConnection?.Enabled == false ? "Disabled for routing" : "No routing gate loaded");
        }

        return connection.Health switch
        {
            ProviderContinuityHealthDto.Healthy => new ProviderTrustRationalePayload(
                Status: "Healthy",
                TrustScore: trustSnapshot is not null ? FormatScore(NormalizeScore(trustSnapshot.Score)) : "100%",
                SignalSource: trustSnapshot is not null && trustSnapshot.Signals.Length > 0 ? string.Join(", ", trustSnapshot.Signals) : "Provider connection continuity health",
                ReasonCode: "CONNECTION_HEALTHY",
                RecommendedAction: connection.RecommendedAction,
                GateImpact: "Normal operation"),
            ProviderContinuityHealthDto.Degraded => new ProviderTrustRationalePayload(
                Status: "Degraded",
                TrustScore: trustSnapshot is not null ? FormatScore(NormalizeScore(trustSnapshot.Score)) : "70%",
                SignalSource: trustSnapshot is not null && trustSnapshot.Signals.Length > 0 ? string.Join(", ", trustSnapshot.Signals) : "Provider connection continuity health",
                ReasonCode: "CONNECTION_DEGRADED",
                RecommendedAction: connection.RecommendedAction,
                GateImpact: "Degraded"),
            ProviderContinuityHealthDto.Blocked => new ProviderTrustRationalePayload(
                Status: "Blocked",
                TrustScore: trustSnapshot is not null ? FormatScore(NormalizeScore(trustSnapshot.Score)) : "40%",
                SignalSource: trustSnapshot is not null && trustSnapshot.Signals.Length > 0 ? string.Join(", ", trustSnapshot.Signals) : "Provider connection continuity health",
                ReasonCode: "CONNECTION_BLOCKED",
                RecommendedAction: connection.RecommendedAction,
                GateImpact: "Critical"),
            _ => new ProviderTrustRationalePayload(
                Status: "Warning",
                TrustScore: trustSnapshot is not null ? FormatScore(NormalizeScore(trustSnapshot.Score)) : "80%",
                SignalSource: trustSnapshot is not null && trustSnapshot.Signals.Length > 0 ? string.Join(", ", trustSnapshot.Signals) : "Provider connection continuity health",
                ReasonCode: "CONNECTION_REVIEW",
                RecommendedAction: connection.RecommendedAction,
                GateImpact: "Watch")
        };
    }

    private static IReadOnlyList<WorkstationDataProviderDiagnostic> BuildWorkstationProviderDiagnostics(
        string providerId,
        ProviderConnectionRowDto? connection,
        ProviderConnectionDto? routingConnection,
        ProviderTrustSnapshotDto? trustSnapshot,
        IReadOnlyList<ProviderBindingDto> bindings,
        ProviderMetrics? metrics,
        ProviderTrustRationalePayload rationale)
    {
        var diagnostics = new List<WorkstationDataProviderDiagnostic>();
        var hasCredentials = connection is not null &&
            connection.CredentialState is not ProviderCredentialStateDto.Missing and not ProviderCredentialStateDto.Partial;

        diagnostics.Add(new WorkstationDataProviderDiagnostic(
            Id: "credential-presence",
            Label: "Credential presence",
            Status: !hasCredentials ? "warning" : "pass",
            StatusLabel: !hasCredentials ? "Review" : "Pass",
            Detail: connection is null
                ? "No provider credential summary is loaded for this provider."
                : connection.CredentialState switch
                {
                    ProviderCredentialStateDto.NotRequired => "No credentials are required for this provider.",
                    ProviderCredentialStateDto.Missing => "Required credential fields are missing.",
                    ProviderCredentialStateDto.Partial => "Credential setup is incomplete.",
                    ProviderCredentialStateDto.Invalid => "Stored credentials are invalid and must be replaced.",
                    _ => $"Credential state: {connection.CredentialState}."
                }));

        diagnostics.Add(new WorkstationDataProviderDiagnostic(
            Id: "credential-verification",
            Label: "Credential verification",
            Status: connection is null
                ? "pending"
                : connection.VerificationState is ProviderVerificationStateDto.Verified or ProviderVerificationStateDto.NotRequired ? "pass"
                : connection.VerificationState == ProviderVerificationStateDto.Failed ? "fail" : "warning",
            StatusLabel: connection is null
                ? "Pending"
                : connection.VerificationState is ProviderVerificationStateDto.Verified or ProviderVerificationStateDto.NotRequired ? "Pass"
                : connection.VerificationState == ProviderVerificationStateDto.Failed ? "Fail" : "Review",
            Detail: connection?.LastError
                ?? (connection is null
                    ? "Verification requires a provider credential summary."
                    : connection.VerificationState == ProviderVerificationStateDto.Verified
                        ? $"Verified at {FormatProviderTimestamp(connection.LastVerifiedAt)}."
                        : $"Verification state: {connection.VerificationState}.")));

        diagnostics.Add(new WorkstationDataProviderDiagnostic(
            Id: "provider-health",
            Label: "Provider health",
            Status: rationale.Status switch
            {
                "Healthy" => "pass",
                "Blocked" or "Degraded" => "fail",
                _ => "warning"
            },
            StatusLabel: rationale.Status,
            Detail: metrics is not null
                ? $"Latency {metrics.AverageLatencyMs:F0}ms p50; dropped messages {metrics.MessagesDropped}; subscriptions {metrics.ActiveSubscriptions}."
                : rationale.RecommendedAction));

        diagnostics.Add(new WorkstationDataProviderDiagnostic(
            Id: "routing-readiness",
            Label: "Routing readiness",
            Status: routingConnection is null
                ? "pending"
                : !routingConnection.Enabled || !routingConnection.ProductionReady
                    ? "warning"
                    : "pass",
            StatusLabel: routingConnection is null
                ? "Pending"
                : routingConnection.ProductionReady ? "Pass" : "Review",
            Detail: routingConnection is null
                ? "No routing connection is configured for this provider yet."
                : $"Bindings {bindings.Count}; fallback routes {bindings.Sum(static binding => binding.FailoverConnectionIds.Length)}; production ready {routingConnection.ProductionReady}."));

        diagnostics.Add(new WorkstationDataProviderDiagnostic(
            Id: "trust-state",
            Label: "Trust state",
            Status: trustSnapshot is null
                ? rationale.Status == "Healthy" ? "pass" : "warning"
                : trustSnapshot.IsHealthy ? "pass" : "warning",
            StatusLabel: trustSnapshot?.HealthStatus ?? rationale.TrustScore,
            Detail: trustSnapshot is not null
                ? trustSnapshot.Signals.Length > 0
                    ? string.Join(", ", trustSnapshot.Signals)
                    : "Trust snapshot is available with no active signals."
                : $"{rationale.SignalSource}. {rationale.RecommendedAction}"));

        return diagnostics;
    }

    private static string NormalizeProviderKey(string providerId)
        => providerId.Trim().ToLowerInvariant();

    private static string FormatProviderTimestamp(DateTimeOffset? value)
        => value?.ToString("MMM dd, yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "Never";

    private static ProviderTrustRationalePayload BuildProviderTrustRationale(ProviderMetrics metrics)
    {
        var trustScore = NormalizeScore(metrics.DataQualityScore);
        var successRate = NormalizeScore(metrics.ConnectionSuccessRate);
        var gateImpact = BuildProviderGateImpact(trustScore);

        if (!metrics.IsConnected)
        {
            return new ProviderTrustRationalePayload(
                Status: "Degraded",
                TrustScore: FormatScore(trustScore),
                SignalSource: "Provider quote/trade stream health telemetry",
                ReasonCode: "PROVIDER_STREAM_DEGRADED",
                RecommendedAction: "Verify provider connectivity and entitlements, then monitor for recovery before promotion decisions.",
                GateImpact: gateImpact);
        }

        if (metrics.ConnectionFailures > 0 && (metrics.ConnectionAttempts == 0 || successRate < 0.75d))
        {
            return new ProviderTrustRationalePayload(
                Status: "Degraded",
                TrustScore: FormatScore(trustScore),
                SignalSource: "Provider reconnect monitor",
                ReasonCode: "RECONNECT_INSTABILITY",
                RecommendedAction: "Keep run in observation mode; require a stable reconnect window before trusting parity-sensitive outputs.",
                GateImpact: gateImpact);
        }

        if (metrics.MessagesDropped > 0)
        {
            return new ProviderTrustRationalePayload(
                Status: "Degraded",
                TrustScore: FormatScore(trustScore),
                SignalSource: "Missing data completeness checker",
                ReasonCode: "DATA_COMPLETENESS_GAP",
                RecommendedAction: "Trigger targeted backfill or replay and block trust sign-off for impacted symbols or windows.",
                GateImpact: gateImpact);
        }

        if (metrics.AverageLatencyMs >= 250d)
        {
            return new ProviderTrustRationalePayload(
                Status: "Warning",
                TrustScore: FormatScore(trustScore),
                SignalSource: "Latency monitor",
                ReasonCode: "LATENCY_REGRESSION",
                RecommendedAction: "Delay operator promotion actions; review latency trend and compare against baseline window.",
                GateImpact: gateImpact);
        }

        if (trustScore < 0.90d)
        {
            return new ProviderTrustRationalePayload(
                Status: trustScore < 0.80d ? "Degraded" : "Warning",
                TrustScore: FormatScore(trustScore),
                SignalSource: "Cross-provider parity comparator",
                ReasonCode: "PARITY_DRIFT_DETECTED",
                RecommendedAction: "Re-run the parity packet and treat results as non-promotable until drift is explained or corrected.",
                GateImpact: gateImpact);
        }

        return new ProviderTrustRationalePayload(
            Status: "Healthy",
            TrustScore: FormatScore(trustScore),
            SignalSource: "Provider baseline health snapshot",
            ReasonCode: "HEALTHY_BASELINE",
            RecommendedAction: "Continue monitoring provider health; no DK1 action is required.",
            GateImpact: gateImpact);
    }

    private static double NormalizeScore(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0d;
        }

        var normalized = value > 1d ? value / 100d : value;
        return Math.Clamp(normalized, 0d, 1d);
    }

    private static string FormatScore(double score)
        => $"{(score * 100d).ToString("0", CultureInfo.InvariantCulture)}%";

    private static string BuildProviderGateImpact(double trustScore)
        => trustScore >= 0.90d
            ? "Normal operation"
            : trustScore >= 0.80d
                ? "Watch"
                : trustScore >= 0.70d
                    ? "Degraded"
                    : "Critical";
}
