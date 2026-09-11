using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.FundStructure;

namespace Meridian.Application.FundStructure;

public sealed record FundStructureTenantReadCount(string TenantId, int VisibleBefore, int VisibleAfter);

public sealed record FundStructureTenantBackfillPlan(
    string AlgorithmVersion, string CodeVersion, string SchemaVersion, string PlanHash,
    FundStructureTenantBackfillSnapshot Evidence,
    IReadOnlyList<FundStructureTenantBackfillStamp> Stamps,
    IReadOnlyList<FundStructureTenantBackfillException> Exceptions,
    IReadOnlyList<FundStructureTenantReadCount> StrictReadCounts,
    IReadOnlyList<string> BlockingReasons, bool AttributionComplete);

/// <summary>Conservative component attribution over the complete retained graph.</summary>
public static class FundStructureTenantBackfillPlanner
{
    public const string AlgorithmVersion = "fund-structure-tenant-backfill-v1";
    public const string SchemaVersion = "fund-structure-005";

    public static FundStructureTenantBackfillPlan Create(FundStructureTenantBackfillSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        // Canonical collection order is independent of provider enumeration. Retained PostgreSQL
        // JSONB rows preserve the complete evidence, including company IDs and source timestamps.
        snapshot = snapshot with
        {
            Rows = snapshot.Rows.OrderBy(row => row.Table, StringComparer.Ordinal).ThenBy(row => row.Id).ToArray(),
            Evidence = snapshot.Evidence.OrderBy(row => row.BookId)
                .ThenBy(row => row.RetainedRegistryEntry?.GetRawText(), StringComparer.Ordinal).ToArray(),
            RetainedQuarantine = snapshot.RetainedQuarantine.OrderBy(row => row.GetProperty("node_id").GetGuid()).ToArray()
        };
        var blockers = new List<string>();
        if (!snapshot.SupportsAtomicApply)
            blockers.Add("Ledger evidence and fund structure use separate databases; coordinated migration is required before apply.");
        var duplicateIds = snapshot.Rows.GroupBy(row => row.Id).Where(group => group.Count() != 1).Select(group => group.Key).ToHashSet();
        if (duplicateIds.Count > 0) blockers.Add("Retained row identities collide across fund-structure tables.");
        var nodes = snapshot.Rows.Where(row => row.IsNode && !duplicateIds.Contains(row.Id)).ToDictionary(row => row.Id);
        var edges = new HashSet<FundStructureOwnershipEdge>();
        var reasons = new Dictionary<Guid, SortedSet<string>>();

        void Reject(Guid id, string reason)
        {
            if (!reasons.TryGetValue(id, out var found)) reasons[id] = found = new(StringComparer.Ordinal);
            found.Add(reason);
        }

        void AddEdge(Guid parent, Guid child)
        {
            if (!nodes.ContainsKey(parent) || !nodes.ContainsKey(child))
            {
                if (nodes.ContainsKey(parent)) Reject(parent, "DanglingOwnershipReference");
                if (nodes.ContainsKey(child)) Reject(child, "DanglingOwnershipReference");
                return;
            }
            edges.Add(new(parent, child));
        }

        foreach (var row in snapshot.Rows)
        {
            if (row.IsNode)
            {
                foreach (var parent in row.Parents) AddEdge(parent, row.Id);
                foreach (var child in row.Children) AddEdge(row.Id, child);
            }
            else if (row.Kind == "OwnershipLink")
            {
                foreach (var parent in row.Parents)
                    foreach (var child in row.Children) AddEdge(parent, child);
                if (!row.RetainedRow.TryGetProperty("relationship_type", out var relationship) ||
                    !string.Equals(relationship.GetString(), "Owns", StringComparison.OrdinalIgnoreCase))
                    foreach (var id in row.Parents.Concat(row.Children).Where(nodes.ContainsKey))
                        Reject(id, "NonOwnershipRelationshipRequiresReview");
            }
        }

        var seeds = new Dictionary<Guid, string>();
        foreach (var group in snapshot.Evidence.GroupBy(row => row.NodeId))
        {
            if (!nodes.TryGetValue(group.Key, out var node))
            {
                blockers.Add($"Ledger ownership evidence references missing structure node {group.Key}.");
                continue;
            }

            var tenants = group.Select(row => NormalizeTenant(row.TenantId)).Where(tenant => tenant is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var invalid = group.Any(row => row.RetainedRegistryEntry is null || NormalizeTenant(row.TenantId) is null ||
                string.IsNullOrWhiteSpace(row.FundProfileId) ||
                !row.RetainedBook.TryGetProperty("fund_structure_node_kind", out var kind) ||
                !string.Equals(kind.GetString(), node.Kind, StringComparison.OrdinalIgnoreCase) ||
                !RegistryMatchesFund(row) || BookStampConflicts(row));
            if (invalid || tenants.Length != 1 || group.GroupBy(row => row.BookId).Any(book => book.Count() != 1))
                Reject(group.Key, "AmbiguousOrMissingLedgerOwnershipEvidence");
            else seeds[group.Key] = tenants[0]!;
        }

        var graph = new FundStructureTenantAttributionGraph(nodes.ToDictionary(row => row.Key, row => row.Value.Kind),
            edges.OrderBy(edge => edge.ParentNodeId).ThenBy(edge => edge.ChildNodeId).ToArray());
        // Adapt the derived adjacency to the existing structural validator; these temporary DTOs
        // carry no new retained ownership assertion or identity.
        var validationLinks = graph.Edges.Select(edge => new OwnershipLinkDto(Guid.Empty,
            edge.ParentNodeId, edge.ChildNodeId, OwnershipRelationshipTypeDto.Owns,
            null, false, DateTimeOffset.MinValue, null, null)).ToArray();
        foreach (var issue in OwnershipGraphValidation.Validate(validationLinks, nodes.ContainsKey))
            if (issue.NodeId is { } id) Reject(id, issue.Code);
        var derived = FundStructureTenantAttribution.Derive(graph, seeds);
        foreach (var item in derived.Quarantined) Reject(item.NodeId, item.Reason.ToString());
        foreach (var edge in graph.Edges)
        {
            if (derived.Attributions.TryGetValue(edge.ParentNodeId, out var parentTenant) &&
                derived.Attributions.TryGetValue(edge.ChildNodeId, out var childTenant) &&
                !string.Equals(parentTenant, childTenant, StringComparison.OrdinalIgnoreCase))
            {
                Reject(edge.ParentNodeId, "ConflictingAttributedOwnershipEdge");
                Reject(edge.ChildNodeId, "ConflictingAttributedOwnershipEdge");
            }
        }
        foreach (var node in nodes.Values)
        {
            var owner = NormalizeTenant(node.TenantId);
            if (owner is not null && (!derived.Attributions.TryGetValue(node.Id, out var tenant) ||
                !string.Equals(owner, tenant, StringComparison.OrdinalIgnoreCase))) Reject(node.Id, "ExistingTenantConflict");
            if (node.TenantId is not null && node.TenantId.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
                Reject(node.Id, "UnscopedTenantSentinel");
        }

        // A problematic edge/assignment contaminates its entire ownership component. No known
        // child is stamped by falling back around an ambiguous ancestor or dependent row.
        foreach (var row in snapshot.Rows.Where(row => !row.IsNode))
        {
            var endpoints = row.Parents.Concat(row.Children).ToArray();
            var owners = endpoints.Where(derived.Attributions.ContainsKey).Select(id => derived.Attributions[id])
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var existing = NormalizeTenant(row.TenantId);
            if (endpoints.Length == 0 || endpoints.Any(id => !nodes.ContainsKey(id)) || owners.Length != 1 ||
                (!string.IsNullOrWhiteSpace(row.TenantId) && existing is null) ||
                (existing is not null && !string.Equals(existing, owners.SingleOrDefault(), StringComparison.OrdinalIgnoreCase)))
                foreach (var id in endpoints.Where(nodes.ContainsKey)) Reject(id, "DependentRowOwnershipConflict");
        }

        var adjacency = nodes.Keys.ToDictionary(id => id, _ => new HashSet<Guid>());
        foreach (var edge in edges)
        {
            adjacency[edge.ParentNodeId].Add(edge.ChildNodeId);
            adjacency[edge.ChildNodeId].Add(edge.ParentNodeId);
        }
        var quarantined = new Dictionary<Guid, (string Reason, string[] Tenants)>();
        var visited = new HashSet<Guid>();
        foreach (var start in nodes.Keys.Order())
        {
            if (!visited.Add(start)) continue;
            var component = new List<Guid>();
            var pending = new Queue<Guid>();
            pending.Enqueue(start);
            while (pending.TryDequeue(out var id))
            {
                component.Add(id);
                foreach (var neighbor in adjacency[id]) if (visited.Add(neighbor)) pending.Enqueue(neighbor);
            }
            var componentReasons = component.Where(reasons.ContainsKey).SelectMany(id => reasons[id]).Distinct().Order().ToArray();
            if (componentReasons.Length == 0) continue;
            var candidates = component.Where(derived.Attributions.ContainsKey).Select(id => derived.Attributions[id])
                .Concat(derived.Quarantined.Where(item => component.Contains(item.NodeId)).SelectMany(item => item.CandidateTenantIds))
                .Concat(snapshot.Evidence.Where(item => component.Contains(item.NodeId)).Select(item => NormalizeTenant(item.TenantId)).OfType<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var id in component) quarantined[id] = (string.Join(";", componentReasons), candidates);
        }

        var stamps = new List<FundStructureTenantBackfillStamp>();
        var exceptions = new List<FundStructureTenantBackfillException>();
        foreach (var row in snapshot.Rows)
        {
            var dependencies = row.IsNode ? new[] { row.Id } : row.Parents.Concat(row.Children).ToArray();
            var bad = dependencies.Where(quarantined.ContainsKey).Select(id => quarantined[id]).ToArray();
            if (duplicateIds.Contains(row.Id) || dependencies.Length == 0 || dependencies.Any(id => !nodes.ContainsKey(id)) || bad.Length > 0)
            {
                exceptions.Add(new(row.Id, row.Kind,
                    bad.Length > 0 ? string.Join(";", bad.Select(item => item.Reason).Distinct().Order()) : "UnresolvedDependency",
                    bad.SelectMany(item => item.Tenants).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray()));
                continue;
            }
            var tenants = dependencies.Select(id => derived.Attributions[id]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (tenants.Length != 1) throw new InvalidOperationException("Attribution did not resolve a complete component.");
            if (string.IsNullOrWhiteSpace(row.TenantId)) stamps.Add(new(row.Table, row.Id, tenants[0]));
        }

        var resolvedQuarantine = snapshot.RetainedQuarantine.Where(row =>
            row.TryGetProperty("resolved_at_utc", out var resolved) && resolved.ValueKind != JsonValueKind.Null)
            .Select(row => row.GetProperty("node_id").GetGuid()).ToHashSet();
        if (exceptions.Any(item => resolvedQuarantine.Contains(item.NodeId)))
            blockers.Add("An existing quarantine resolution conflicts with the current evidence; review it separately.");
        var currentExceptionIds = exceptions.Select(item => item.NodeId).ToHashSet();
        if (snapshot.RetainedQuarantine.Any(row =>
                !resolvedQuarantine.Contains(row.GetProperty("node_id").GetGuid()) &&
                !currentExceptionIds.Contains(row.GetProperty("node_id").GetGuid())))
            blockers.Add("Retained unresolved quarantine requires explicit resolution review before newly derivable rows can be stamped.");

        var before = nodes.Values.Where(row => !string.IsNullOrWhiteSpace(row.TenantId)).ToDictionary(row => row.Id, row => row.TenantId!);
        var after = new Dictionary<Guid, string>(before);
        foreach (var stamp in stamps.Where(stamp => nodes.ContainsKey(stamp.Id))) after[stamp.Id] = stamp.TenantId;
        var tenantIds = before.Values.Concat(after.Values).Select(NormalizeTenant).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        var counts = tenantIds.Select(tenant => new FundStructureTenantReadCount(tenant,
            CountVisible(nodes.Keys, before, tenant), CountVisible(nodes.Keys, after, tenant))).ToArray();
        var plan = new FundStructureTenantBackfillPlan(AlgorithmVersion,
            typeof(FundStructureTenantBackfillPlanner).Module.ModuleVersionId.ToString("D"), SchemaVersion, "", snapshot,
            stamps.OrderBy(row => row.Table, StringComparer.Ordinal).ThenBy(row => row.Id).ToArray(),
            exceptions.OrderBy(row => row.NodeId).ToArray(), counts, blockers.Distinct().Order().ToArray(),
            exceptions.Count == 0 && blockers.Count == 0);
        return plan with { PlanHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(plan)))) };
    }

    private static int CountVisible(IEnumerable<Guid> ids, IReadOnlyDictionary<Guid, string> owners, string tenant)
    {
        var map = new FundStructureTenantMap(true, owners);
        return ids.Count(id => FundStructureTenantScope.IsVisible(map, tenant, id, TenantScopeEnforcementMode.FailClosed));
    }

    private static string? NormalizeTenant(string? tenant)
        => string.IsNullOrWhiteSpace(tenant) || tenant.Trim().Equals("all", StringComparison.OrdinalIgnoreCase) ? null : tenant.Trim();

    private static bool RegistryMatchesFund(FundStructureTenantBackfillEvidence row)
        => row.RetainedRegistryEntry is { } registry && registry.TryGetProperty("fund_profile_id", out var profile) &&
           string.Equals(profile.GetString()?.Trim(), row.FundProfileId.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool BookStampConflicts(FundStructureTenantBackfillEvidence row)
        => row.RetainedBook.TryGetProperty("tenant_id", out var stamp) && stamp.ValueKind == JsonValueKind.String &&
           NormalizeTenant(stamp.GetString()) is { } bookTenant &&
           !string.Equals(bookTenant, NormalizeTenant(row.TenantId), StringComparison.OrdinalIgnoreCase);
}
