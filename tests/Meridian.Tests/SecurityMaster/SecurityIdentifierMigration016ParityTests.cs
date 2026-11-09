using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Migration 016 backfilled the normalized identifier columns with SQL <c>case</c> arms that restate
/// <see cref="SecurityIdentifierNormalizer.NormalizeValue"/>'s kind lists. The migration is one-time,
/// so it cannot diverge retroactively, but nothing tied the two statements together. This pins the
/// SQL lists to the C# rules and forces every kind that strips characters but postdates the
/// migration to be named explicitly — a new stripping kind fails here until someone decides whether
/// existing rows need a backfill. Rows for kinds introduced after 016 have always been written with
/// the C#-computed normalized value by <c>PostgresSecurityMasterStore</c>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityIdentifierMigration016ParityTests
{
    private const string MigrationFile = "016_security_master_normalized_identifier_lookup.sql";

    /// <summary>Kinds added after migration 016 whose C# normalization strips characters.</summary>
    private static readonly SecurityIdentifierKind[] IntroducedAfterMigration016 =
    [
        SecurityIdentifierKind.LoanX,
        SecurityIdentifierKind.Clip,
        SecurityIdentifierKind.Uti,
        SecurityIdentifierKind.Upi,
        SecurityIdentifierKind.MarkitRedEntity,
        SecurityIdentifierKind.MarkitRedPair
    ];

    // A probe whose stripped forms differ from the trimmed/upper-cased form for both SQL classes.
    private const string Probe = " ab-12.c ";

    [Fact]
    public void Migration016KindLists_MatchTheCSharpNormalizer()
    {
        var sql = ReadMigration();

        var alphanumericLists = Regex.Matches(sql, @"_kind in \(([^)]*)\)\s*then regexp_replace\([^,]+,\s*'\[\^A-Z0-9\]'")
            .Select(static match => ParseKinds(match.Groups[1].Value))
            .ToArray();
        var digitKinds = Regex.Matches(sql, @"_kind = '(\w+)'\s*then regexp_replace\([^,]+,\s*'\[\^0-9\]'")
            .Select(static match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        alphanumericLists.Should().HaveCount(3, "016 backfills securities, security_identifiers and security_aliases");
        alphanumericLists.Should().AllSatisfy(list => list.Should().BeEquivalentTo(alphanumericLists[0]));
        var sqlAlphanumeric = alphanumericLists[0];
        digitKinds.Should().BeEquivalentTo(["Valoren"]);

        foreach (var kindName in sqlAlphanumeric)
        {
            var kind = Enum.Parse<SecurityIdentifierKind>(kindName);
            SecurityIdentifierNormalizer.NormalizeValue(kind, Probe).Should().Be("AB12C", $"016 strips {kind} to A-Z0-9");
        }

        foreach (var kind in Enum.GetValues<SecurityIdentifierKind>())
        {
            var normalized = SecurityIdentifierNormalizer.NormalizeValue(kind, Probe);
            var name = kind.ToString();
            if (normalized == "AB12C")
            {
                (sqlAlphanumeric.Contains(name) || IntroducedAfterMigration016.Contains(kind)).Should().BeTrue(
                    $"{kind} strips non-alphanumerics in C#; either migration 016 covered it or it must be listed as introduced after 016 (and its stored rows checked for a backfill need)");
            }
            else if (normalized == "12")
            {
                digitKinds.Should().Contain(name, $"{kind} strips non-digits in C#");
            }
            else
            {
                normalized.Should().Be("AB-12.C", $"{kind} falls through to trim/upper-case in both C# and migration 016");
                sqlAlphanumeric.Should().NotContain(name);
            }
        }

        IntroducedAfterMigration016.Select(static kind => kind.ToString())
            .Should().NotIntersectWith(sqlAlphanumeric, "kinds the migration already covers do not belong on the post-016 list");
    }

    private static HashSet<string> ParseKinds(string list)
        => list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(static item => item.Trim('\''))
            .ToHashSet(StringComparer.Ordinal);

    private static string ReadMigration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the repository root must be discoverable from the test output");
        return File.ReadAllText(Path.Combine(
            directory!.FullName, "src", "Meridian.Storage", "SecurityMaster", "Migrations", MigrationFile));
    }
}
