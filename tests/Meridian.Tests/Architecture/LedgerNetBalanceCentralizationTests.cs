using System.Text.RegularExpressions;
using Xunit;

namespace Meridian.Tests.Architecture;

/// <summary>
/// Architecture guard: the normal-balance rule (debit-normal accounts net as
/// <c>debits - credits</c>, credit-normal accounts as <c>credits - debits</c>) is owned by the
/// F# posting kernel (<c>Meridian.FSharp.Ledger.Posting.calculateNetBalance</c>) and exposed to C#
/// exclusively through <c>Meridian.Ledger.Ledger.CalculateNetBalance</c>. No C# source may
/// reimplement the account-type conditional locally the way the reconciliation projectors once did.
/// </summary>
public sealed class LedgerNetBalanceCentralizationTests
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        "node_modules",
    };

    // Matches the tell-tale reimplementation shape, whitespace/newline insensitive:
    //   ... Asset or LedgerAccountType.Expense ? a - b : b - a
    private static readonly Regex ReimplementationSignature = new(
        @"Asset\s+or\s+LedgerAccountType\.Expense\s*\?\s*\w+\s*-\s*\w+\s*:\s*\w+\s*-\s*\w+",
        RegexOptions.Singleline | RegexOptions.Compiled);

    [Fact]
    public void NoCSharpSource_Reimplements_NormalBalance_Netting()
    {
        var repoRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repoRoot, "src");

        var offenders = EnumerateCSharpFiles(sourceRoot)
            .Where(file => ReimplementationSignature.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(repoRoot, file))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Debit/credit normal-balance math must route through Ledger.CalculateNetBalance " +
            "(backed by the F# posting kernel) instead of being reimplemented inline:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> EnumerateCSharpFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }

            foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var childInfo = new DirectoryInfo(child);
                if (ExcludedDirectoryNames.Contains(childInfo.Name) ||
                    childInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                pending.Push(child);
            }
        }
    }

    private static string FindRepositoryRoot()
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

        throw new InvalidOperationException("Unable to locate Meridian repository root from test output directory.");
    }
}
