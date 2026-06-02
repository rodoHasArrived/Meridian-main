namespace Meridian.FSharp.Operations

open System
open System.Collections.Generic

type ProviderConnectionHealth = {
    IsConnected: bool
    MissedHeartbeats: int
    ReconnectCount: int
    UptimeHours: float
}

type ProviderLatencyHealth = {
    P95LatencyMs: float
}

type ProviderErrorHealth = {
    ErrorRate: float
}

type ProviderScoringConfig = {
    DecisionSchemaVersion: string
    KernelVersion: string
    EvaluationThreshold: float
    LatencyThresholdMs: float
    LatencyMaxMs: float
    ErrorRateThreshold: float
    MaxMissedHeartbeatsForFullDegradation: int
    MaxReconnectsPerHour: float
    ConnectionWeight: double
    LatencyWeight: double
    ErrorWeight: double
    ReconnectWeight: double
}

[<CLIMutable>]
type ProviderComponentScore = {
    ConnectionScore: double
    LatencyScore: double
    ErrorScore: double
    ReconnectScore: double
    CompositeScore: double
}

[<CLIMutable>]
type ProviderScoringResult = {
    ProviderName: string
    Score: ProviderComponentScore
    IsDegraded: bool
    Reasons: string array
    IsPromotionGatePass: bool
}

[<CLIMutable>]
type ProviderPromotionDecision = {
    CandidateKernelVersion: string
    CandidateThreshold: float
    BaselineThreshold: float
    PromotionAllowed: bool
    BlockingReasons: string array
}

[<RequireQualifiedAccess>]
module ProviderScoringConfig =

    let defaultConfig =
        {
            DecisionSchemaVersion = "provider-degradation-fsharp-v1"
            KernelVersion = "provider-degradation-fsharp-v1"
            EvaluationThreshold = 0.6
            LatencyThresholdMs = 200.0
            LatencyMaxMs = 2000.0
            ErrorRateThreshold = 0.05
            MaxMissedHeartbeatsForFullDegradation = 5
            MaxReconnectsPerHour = 10.0
            ConnectionWeight = 0.35
            LatencyWeight = 0.25
            ErrorWeight = 0.25
            ReconnectWeight = 0.15
        }

    let withThreshold (threshold: float) config =
        { config with EvaluationThreshold = threshold }

[<RequireQualifiedAccess>]
module ProviderDegradationScoring =

    let private clamp01 (value: float) =
        if value < 0.0 then 0.0
        else if value > 1.0 then 1.0
        else value

    let private connectionComponent (config: ProviderScoringConfig) (health: ProviderConnectionHealth) =
        if not health.IsConnected then
            1.0
        else
            let missed = float health.MissedHeartbeats
            let full = float config.MaxMissedHeartbeatsForFullDegradation
            clamp01 (missed / full)

    let private latencyComponent (config: ProviderScoringConfig) (health: ProviderLatencyHealth) =
        if health.P95LatencyMs <= config.LatencyThresholdMs then
            0.0
        else
            clamp01 ((health.P95LatencyMs - config.LatencyThresholdMs) / (config.LatencyMaxMs - config.LatencyThresholdMs))

    let private errorComponent (config: ProviderScoringConfig) (health: ProviderErrorHealth) =
        if health.ErrorRate <= config.ErrorRateThreshold then
            0.0
        else
            clamp01 ((health.ErrorRate - config.ErrorRateThreshold) / (1.0 - config.ErrorRateThreshold))

    let private reconnectComponent (config: ProviderScoringConfig) (health: ProviderConnectionHealth) =
        if health.UptimeHours <= 0.0 then
            0.0
        else
            let rate = float health.ReconnectCount / health.UptimeHours
            clamp01 (rate / config.MaxReconnectsPerHour)

    let score
            (providerName: string)
            (connectionHealth: ProviderConnectionHealth)
            (latencyHealth: ProviderLatencyHealth)
            (errorHealth: ProviderErrorHealth)
            (config: ProviderScoringConfig) =

        let connectionScore = connectionComponent config connectionHealth
        let latencyScore = latencyComponent config latencyHealth
        let errorScore = errorComponent config errorHealth
        let reconnectScore = reconnectComponent config connectionHealth

        let componentReason (code: string) (score: double) =
            if score > 0.0 then sprintf "%s=%.4f" code score else String.Empty

        let reasons =
            [|
                if not connectionHealth.IsConnected then
                    yield "provider-degradation.connection-disconnected"
                if connectionHealth.MissedHeartbeats > 0 then
                    yield componentReason "provider-degradation.connection-heartbeats" connectionScore
                if latencyHealth.P95LatencyMs > config.LatencyThresholdMs then
                    yield componentReason "provider-degradation.latency" latencyScore
                if errorHealth.ErrorRate > config.ErrorRateThreshold then
                    yield componentReason "provider-degradation.error-rate" errorScore
                if connectionHealth.ReconnectCount > 0 then
                    yield componentReason "provider-degradation.reconnect-rate" reconnectScore
            |] |> Array.filter (String.IsNullOrWhiteSpace >> not)

        let score =
            clamp01 (
                connectionScore * config.ConnectionWeight
                + latencyScore * config.LatencyWeight
                + errorScore * config.ErrorWeight
                + reconnectScore * config.ReconnectWeight)

        let isDegraded = score >= config.EvaluationThreshold
        let component = {
            ConnectionScore = connectionScore
            LatencyScore = latencyScore
            ErrorScore = errorScore
            ReconnectScore = reconnectScore
            CompositeScore = score
        }

        {
            ProviderName = providerName
            Score = component
            IsDegraded = isDegraded
            Reasons = reasons
            IsPromotionGatePass = not isDegraded
        }

    let evaluatePromotionGate
            (candidateThreshold: float)
            (currentThreshold: float)
            (candidateKernelVersion: string)
            (candidateScore: double)
            (baselineScore: double) =

        let blocking = List<string>()
        if candidateScore > baselineScore then
            blocking.Add "Candidate score regressed versus baseline kernel."
        if candidateThreshold > 1.0 || candidateThreshold < 0.0 then
            blocking.Add "Candidate threshold must be within 0.0 and 1.0."
        if String.IsNullOrWhiteSpace candidateKernelVersion then
            blocking.Add "Candidate kernel version is required."
        if currentThreshold <= 0.0 then
            blocking.Add "Current threshold must be positive."

        let allowed = blocking.Count = 0 && candidateScore <= currentThreshold
        {
            CandidateKernelVersion = candidateKernelVersion
            CandidateThreshold = candidateThreshold
            BaselineThreshold = currentThreshold
            PromotionAllowed = allowed
            BlockingReasons = blocking |> List.ofSeq |> List.toArray
        }

    let evaluateSeverityGate
            (candidateThreshold: float)
            (config: ProviderScoringConfig) =
        if candidateThreshold >= config.EvaluationThreshold then
            Error "Candidate threshold cannot be equal to or exceed the degradation threshold."
        else
            Ok candidateThreshold

