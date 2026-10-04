using FluentAssertions;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Structural guards for migration 030. Live PostgreSQL upgrade behavior is covered by the
/// Security Master database suite when its external database fixture is available.
/// </summary>
public sealed class CorporateActionOperationsMigrationTests
{
    private const string MigrationFile = "030_security_master_corporate_action_operations.sql";

    [Fact]
    public void Migration_PreflightsLegacySupersedeLineageBeforeAddingConstraints()
    {
        var sql = ReadMigration();
        var lockIndex = sql.IndexOf(
            "lock table __SCHEMA__.corporate_actions in share row exclusive mode",
            StringComparison.Ordinal);
        var orphanIndex = sql.IndexOf(
            "cannot enforce corporate-action parentage",
            StringComparison.Ordinal);
        var branchIndex = sql.IndexOf(
            "cannot enforce a single corporate-action successor",
            StringComparison.Ordinal);
        var cycleIndex = sql.IndexOf(
            "cannot enforce a linear corporate-action lineage",
            StringComparison.Ordinal);
        var crossSecurityIndex = sql.IndexOf(
            "cross-security supersede link(s) exist",
            StringComparison.Ordinal);
        var changedEventTypeIndex = sql.IndexOf(
            "changed-event-type supersede link(s) exist",
            StringComparison.Ordinal);
        var lifecycleIndex = sql.IndexOf(
            "invalid or backward-lifecycle supersede link(s) exist",
            StringComparison.Ordinal);
        var foreignKeyIndex = sql.IndexOf(
            "add constraint fk_corporate_actions_superseded_action",
            StringComparison.Ordinal);
        var uniqueIndex = sql.IndexOf(
            "create unique index if not exists ux_corporate_actions_single_successor",
            StringComparison.Ordinal);

        lockIndex.Should().BeGreaterThanOrEqualTo(0);
        orphanIndex.Should().BeGreaterThan(lockIndex);
        cycleIndex.Should().BeGreaterThan(orphanIndex);
        branchIndex.Should().BeGreaterThan(cycleIndex);
        crossSecurityIndex.Should().BeGreaterThan(branchIndex);
        changedEventTypeIndex.Should().BeGreaterThan(crossSecurityIndex);
        lifecycleIndex.Should().BeGreaterThan(changedEventTypeIndex);
        foreignKeyIndex.Should().BeGreaterThan(lifecycleIndex);
        uniqueIndex.Should().BeGreaterThan(foreignKeyIndex);
        sql.Should().Contain("errcode = '23514'");
        sql.Should().Contain("order by child.corp_act_id");
        sql.Should().Contain("order by supersedes_corp_act_id");
        sql.Should().Contain("with recursive lineage_walk as");
        sql.Should().Contain("parent.corp_act_id = any(walk.visited) as is_cycle");
        sql.Should().Contain("count(distinct current_id)");
        sql.Should().Contain("where child.security_id <> parent.security_id");
        sql.Should().Contain("where child.event_type <> parent.event_type");
        sql.Should().Contain("coalesce(nullif(child.lifecycle_state, ''), 'Confirmed') as child_state");
        sql.Should().Contain("child_state not in ('Announced', 'Confirmed', 'Ex', 'Paid', 'Cancelled')");
        (sql.Split("or parent_state = 'Cancelled'", StringSplitOptions.None).Length - 1)
            .Should().Be(2, "both the count and deterministic sample predicates must reject successors after Cancelled");
        sql.Should().Contain("when 'Cancelled' then 2147483647");
        sql.Should().Contain("Migration 030 does not infer or delete canonical lineage");
        sql.Should().Contain("Migration 030 does not select or delete a canonical successor");
        sql.Should().Contain("Migration 030 does not choose which historical link to discard");
        sql.Should().Contain("Migration 030 does not infer cross-security lineage");
        sql.Should().Contain("Cancelled is an absorbing terminal state");
    }

    [Fact]
    public void Migration_DefersFingerprintRequirementForRollingWriterCompatibility()
    {
        var sql = ReadMigration();

        sql.Should().Contain("Keep the new column nullable for rolling-upgrade compatibility");
        sql.Should().Contain("add column if not exists economic_fingerprint char(64) null");
        sql.Should().NotContain("ck_corporate_actions_economic_fingerprint_required");
        sql.Should().NotContain("check (economic_fingerprint is not null) not valid");
    }

    [Fact]
    public void Migration_BoundsEveryIndexedExternalIdentityInUtf8Bytes()
    {
        var sql = ReadMigration();

        foreach (var constraint in new[]
                 {
                     "ck_corporate_action_source_provider_id_length",
                     "ck_corporate_action_source_event_id_length",
                     "ck_corporate_action_source_event_version_length",
                     "ck_corporate_action_canonical_source_provider_id_length",
                     "ck_corporate_action_canonical_source_event_id_length",
                     "ck_corporate_action_canonical_source_event_version_length",
                     "ck_corporate_action_processing_case_scope_identity_lengths",
                     "ck_corporate_action_processing_case_scope_identity_total",
                     "ck_corporate_action_processing_option_code_length",
                     "ck_corporate_action_case_transition_operation_kind_length",
                     "ck_corporate_action_case_transition_idempotency_key_length",
                     "ck_corporate_action_restatement_tenant_id_length",
                     "ck_corporate_action_restatement_company_id_length",
                     "ck_corporate_action_command_receipt_operation_kind_length",
                     "ck_corporate_action_command_receipt_idempotency_key_length",
                 })
        {
            sql.Should().Contain($"constraint {constraint}");
        }

        sql.Should().Contain("octet_length(provider_id) between 1 and 256");
        sql.Should().Contain("octet_length(source_event_id) between 1 and 256");
        sql.Should().Contain("octet_length(source_event_version) between 1 and 256");
        sql.Should().Contain("octet_length(operation_kind) between 1 and 64");
        sql.Should().Contain("octet_length(idempotency_key) between 1 and 256");
        sql.Should().Contain("+ coalesce(octet_length(jurisdiction), 0) <= 2048");
    }

    private static string ReadMigration()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(
            root,
            "src",
            "Meridian.Storage",
            "SecurityMaster",
            "Migrations",
            MigrationFile);
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Meridian repository root.");
    }
}
