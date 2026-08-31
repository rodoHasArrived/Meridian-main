using FluentAssertions;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Structural guards for migration 031 (corporate-action accounting approval and posting lane).
/// Live PostgreSQL upgrade behavior is covered by the Security Master database suite when its
/// external database fixture is available.
/// </summary>
public sealed class CorporateActionAccountingMigrationTests
{
    private const string MigrationFile = "031_security_master_corporate_action_accounting_lane.sql";

    [Fact]
    public void Migration_KeepsExactlyOneCurrentProjectionAndOneActiveApprovalPerCase()
    {
        var sql = ReadMigration();

        sql.Should().Contain(
            "create unique index if not exists ux_corporate_action_case_accounting_projection_current",
            "the exact-version binding authority must be unambiguous per case");
        sql.Should().Contain("where is_current");
        sql.Should().Contain(
            "create unique index if not exists ux_corporate_action_case_accounting_approval_active",
            "posting must never choose between competing maker-checker approvals");
        sql.Should().Contain("where voided_at is null");
    }

    [Fact]
    public void Migration_RetainsOnlyBalancedPostedJournalRecords()
    {
        var sql = ReadMigration();

        sql.Should().Contain("constraint ck_corporate_action_case_accounting_posting_status check (posting_status = 'Posted')");
        sql.Should().Contain("total_debits = total_credits and total_debits > 0");
        sql.Should().Contain(
            "constraint ux_corporate_action_case_accounting_posting_projection unique (projection_id)",
            "a superseded binding can never be posted twice; corrections flow through restatement onto a fresh binding");
    }

    [Fact]
    public void Migration_BindsHashesVersionsAndSupersessionFailClosed()
    {
        var sql = ReadMigration();

        sql.Should().Contain("projection_input_hash ~ '^[0-9a-f]{64}$'");
        sql.Should().Contain("drafted_candidate_fingerprint ~ '^[0-9a-f]{64}$'");
        sql.Should().Contain("evidence_hash ~ '^[0-9a-f]{64}$'");
        sql.Should().Contain("bound_case_version > 0");
        sql.Should().Contain("expected_period_version > 0");
        sql.Should().Contain(
            "(is_current and superseded_at is null) or (not is_current and superseded_at is not null)",
            "a binding is either the current authority or carries its supersession timestamp");
        sql.Should().Contain(
            "(voided_at is null and voided_by is null) or (voided_at is not null and voided_by is not null)",
            "voiding an approval must be actor-attributed");
    }

    [Fact]
    public void Migration_BoundsEveryIndexedExternalIdentityInUtf8Bytes()
    {
        var sql = ReadMigration();

        foreach (var constraint in new[]
                 {
                     "ck_corporate_action_case_accounting_projection_identity_lengths",
                     "ck_corporate_action_case_accounting_approval_identity_lengths",
                     "ck_corporate_action_case_accounting_posting_identity_lengths",
                 })
        {
            sql.Should().Contain(constraint, "external identities must be byte-bounded at the store");
        }

        sql.Should().Contain("octet_length(posting_idempotency_key) between 1 and 256");
        sql.Should().Contain("octet_length(prepared_by) between 1 and 256");
        sql.Should().Contain("octet_length(approved_by) between 1 and 256");
        sql.Should().Contain("octet_length(posted_by) between 1 and 256");
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
