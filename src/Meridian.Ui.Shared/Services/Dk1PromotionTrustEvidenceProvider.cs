using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;

namespace Meridian.Ui.Shared.Services;

/// <summary>Uses the retained DK1 packet selected by the source run as promotion trust evidence.</summary>
public sealed class Dk1PromotionTrustEvidenceProvider : IPromotionTrustEvidenceProvider
{
    private readonly Dk1TrustGateReadinessService _readiness;
    private readonly Dk1TrustGateReadinessOptions _options;

    public Dk1PromotionTrustEvidenceProvider(
        Dk1TrustGateReadinessService readiness,
        Dk1TrustGateReadinessOptions options)
    {
        _readiness = readiness;
        _options = options;
    }

    public async Task<PromotionTrustEvidence?> GetEvidenceAsync(StrategyRunEntry run, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(run.InputHashSha256))
        {
            return null;
        }

        PromotionTrustEvidence? selected = null;
        foreach (var reference in run.RetainedEvidenceReferences)
        {
            var packetReference = reference.Trim().Replace('\\', '/');
            var prefix = $"{PromotionApprovalChecklist.Dk1TrustPacketReviewed}:";
            if (packetReference.StartsWith(prefix, StringComparison.Ordinal))
            {
                packetReference = packetReference[prefix.Length..];
            }

            if (!packetReference.EndsWith("dk1-pilot-parity-packet.json", StringComparison.Ordinal))
            {
                continue;
            }

            var packet = await _readiness.GetRetainedPacketAsync(packetReference, ct).ConfigureAwait(false);
            if (packet is null || string.IsNullOrWhiteSpace(packet.PacketPath))
            {
                continue;
            }

            var evidence = BuildEvidence(run, packet);
            if (selected is null || evidence.ObservedAt > selected.ObservedAt)
            {
                selected = evidence;
            }
        }

        return selected;
    }

    private PromotionTrustEvidence BuildEvidence(
        StrategyRunEntry run,
        Meridian.Contracts.Workstation.TradingTrustGateReadinessDto packet)
    {
        var maximumAge = _options.PromotionEvidenceMaximumAge ?? TimeSpan.FromHours(24);
        var observedAt = packet.GeneratedAt?.ToUniversalTime() ?? default;
        var validUntil = maximumAge > TimeSpan.Zero && maximumAge.Ticks <= DateTimeOffset.MaxValue.UtcTicks - observedAt.UtcTicks
            ? observedAt + maximumAge
            : observedAt;
        return new PromotionTrustEvidence(
            run.RunId,
            run.InputHashSha256!,
            packet.PacketPath!,
            observedAt,
            validUntil,
            IsComplete: packet.ReadyForOperatorReview &&
                packet.Blockers.Count == 0 &&
                packet.RequiredSampleCount > 0 &&
                packet.ReadySampleCount >= packet.RequiredSampleCount &&
                packet.SampleReviews.All(sample => string.Equals(sample.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
                    sample.Observed &&
                    sample.MissingRequirements.Count == 0 && sample.EvidenceAnchors.Count > 0) &&
                packet.ValidatedEvidenceDocumentCount > 0 &&
                packet.EvidenceDocuments.All(document => string.Equals(document.Status, "validated", StringComparison.OrdinalIgnoreCase) &&
                    document.Exists &&
                    document.MissingRequirements.Count == 0));
    }

}
