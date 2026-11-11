using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Meridian.Application.SecurityMaster;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Architecture;

/// <summary>
/// Architecture guard: a posting calculation is a method that builds journal drafts or journal
/// lines (or, for a ledger bridge, posts them). Each one must either be called from production
/// source outside its own file, or carry <see cref="PostingCalculationNotYetConnectedAttribute"/>
/// with the reason it is waiting. A finished calculation that only tests call fails this test.
/// </summary>
public sealed class PostingCalculationConnectionTests
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules",
    };

    [Fact]
    public void EveryPostingCalculation_HasAProductionCaller_OrIsMarkedNotYetConnected()
    {
        var sources = LoadProductionSources();
        var unconnected = new List<string>();
        var staleMarkers = new List<string>();

        foreach (var method in DiscoverPostingCalculations())
        {
            var name = Describe(method);
            // Overloads share one name and one decision, so a marker on any overload covers them.
            var marked = method.DeclaringType!.GetCustomAttribute<PostingCalculationNotYetConnectedAttribute>() is not null ||
                         method.DeclaringType.GetMember(method.Name).OfType<MethodInfo>()
                             .Any(overload => overload.GetCustomAttribute<PostingCalculationNotYetConnectedAttribute>() is not null);
            var connected = HasProductionCaller(method, sources);
            if (!connected && !marked)
                unconnected.Add(name);
            else if (connected && marked)
                staleMarkers.Add(name);
        }

        Assert.True(unconnected.Count == 0,
            "These posting calculations have no production caller. Connect each to a real posting path, or mark it " +
            $"[{nameof(PostingCalculationNotYetConnectedAttribute).Replace("Attribute", "")}(\"reason\")]:" +
            Environment.NewLine + string.Join(Environment.NewLine, unconnected.Order(StringComparer.Ordinal)));
        Assert.True(staleMarkers.Count == 0,
            "These posting calculations are marked not yet connected but now have a production caller; remove the marker:" +
            Environment.NewLine + string.Join(Environment.NewLine, staleMarkers.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void Discovery_FindsTheCalculationsThisGuardWasWrittenFor()
    {
        var discovered = DiscoverPostingCalculations().Select(Describe).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("FundEconomicsJournalFactory.BuildManagementFeeAccrualDraft", discovered);
        Assert.Contains("FundEconomicsJournalFactory.BuildPerformanceFeeAccrualDraft", discovered);
        Assert.Contains("MultiCurrencyLedgerTranslator.BuildUnrealizedFxRevaluationLines", discovered);
        Assert.Contains("ISecurityMasterAmortizationLedgerBridge.PostProjectedCashFlowsAsync", discovered);
        Assert.Contains("SecurityMasterCashFlowService.BuildLedgerPostingsAsync", discovered);
        Assert.Contains("StructuredCashFlowLedgerBridge.BuildCouponAccrualPostings", discovered);
    }

    private static IEnumerable<MethodInfo> DiscoverPostingCalculations()
    {
        var builders = typeof(FundEconomicsJournalFactory).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName && BuildsJournal(method.ReturnType));
        // Ledger bridges post into a ledger instead of returning lines.
        var bridges = typeof(ISecurityMasterAmortizationLedgerBridge).Assembly.GetExportedTypes()
            .Where(type => type.IsInterface && type.Name.EndsWith("LedgerBridge", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods())
            .Where(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(global::Meridian.Ledger.Ledger)));
        // Services that build ledger postings for a caller to post.
        var postingBuilders = typeof(ISecurityMasterAmortizationLedgerBridge).Assembly.GetExportedTypes()
            .Where(type => type.IsClass && !type.Name.StartsWith("Null", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName && Unwrap(method.ReturnType).Name.EndsWith("LedgerPostingResult", StringComparison.Ordinal));
        return builders.Concat(bridges).Concat(postingBuilders).DistinctBy(Describe);
    }

    private static Type Unwrap(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>) ? type.GetGenericArguments()[0] : type;

    private static bool BuildsJournal(Type type)
    {
        if (type == typeof(AutomatedJournalDraft))
            return true;
        if (type.Name.EndsWith("DraftResult", StringComparison.Ordinal))
            return true;
        if (typeof(ITuple).IsAssignableFrom(type) && type.IsGenericType)
            return type.GetGenericArguments().FirstOrDefault() == typeof(LedgerAccount);
        return type.IsGenericType && type.GetGenericArguments().Any(BuildsJournal);
    }

    private static string Describe(MethodInfo method) => $"{method.DeclaringType!.Name}.{method.Name}";

    private static bool HasProductionCaller(MethodInfo method, IReadOnlyList<(string Path, string Code)> sources)
    {
        var typeName = method.DeclaringType!.Name;
        var declaration = new Regex($@"\b(class|interface|record|struct)\s+{Regex.Escape(typeName)}\b", RegexOptions.Compiled);
        // A static builder is called through its type; a bridge is called on an instance.
        var call = new Regex(method.IsStatic
            ? $@"\b{Regex.Escape(typeName)}\s*\.\s*{Regex.Escape(method.Name)}\s*[(<]"
            : $@"\.\s*{Regex.Escape(method.Name)}\s*[(<]", RegexOptions.Compiled);
        // A sibling implementation or the interface declaring the method is not a caller.
        var implementers = method.IsStatic
            ? []
            : method.DeclaringType.Assembly.GetTypes()
                .Where(type => type != method.DeclaringType &&
                    (method.DeclaringType.IsAssignableFrom(type) || type.IsAssignableFrom(method.DeclaringType) ||
                     method.DeclaringType.GetInterfaces().Any(contract => contract.IsAssignableFrom(type))))
                .Concat(method.DeclaringType.GetInterfaces())
                .Where(type => type.Namespace?.StartsWith("Meridian", StringComparison.Ordinal) == true)
                .Select(type => new Regex($@"\b(class|interface)\s+{Regex.Escape(type.Name)}\b", RegexOptions.Compiled))
                .ToArray();
        return sources.Any(source =>
            !declaration.IsMatch(source.Code) &&
            !implementers.Any(implementer => implementer.IsMatch(source.Code)) &&
            call.IsMatch(source.Code));
    }

    private static IReadOnlyList<(string Path, string Code)> LoadProductionSources()
    {
        var root = Path.Combine(FindRepositoryRoot(), "src");
        var comment = new Regex(@"^\s*//.*$", RegexOptions.Multiline | RegexOptions.Compiled);
        return EnumerateCSharpFiles(root)
            // Comments and XML docs mention calculations without calling them.
            .Select(file => (file, comment.Replace(File.ReadAllText(file), string.Empty)))
            .ToArray();
    }

    private static IEnumerable<string> EnumerateCSharpFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly))
                yield return file;
            foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var info = new DirectoryInfo(child);
                if (!ExcludedDirectoryNames.Contains(info.Name) && !info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    pending.Push(child);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root (Meridian.sln).");
    }
}
