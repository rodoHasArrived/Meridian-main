using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
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
        // Projectors that return a projection carrying journal lines, not the lines themselves.
        Assert.Contains("PartnershipWaterfallProjector.Project", discovered);
        Assert.Contains("MultiCurrencyJournalProjector.Project", discovered);
        // Financial-operations services and their contracts.
        Assert.Contains("IAccountingJournalDraftService.BuildDraftAsync", discovered);
        // Instance methods on ledger aggregates.
        Assert.Contains("JournalTemplateBook.Instantiate", discovered);
    }

    [Fact]
    public void ACallerThatIsItselfUnused_DoesNotConnectACalculation()
    {
        // A static validator that no other production file names cannot launder a calculation it calls;
        // once something references it, its calls count again.
        (string Path, string Code) guard = ("src/OrphanGuard.cs",
            "public static class OrphanGuard { public static void Check() => Projector.Lines(); }");
        (string Path, string Code) unrelated = ("src/Other.cs", "public sealed class Other { }");
        (string Path, string Code) referencing = ("src/Workflow.cs",
            "public sealed class Workflow { public void Run() => OrphanGuard.Check(); }");
        (string Path, string Code) extensions = ("src/Extensions.cs",
            "public static class ProjectorExtensions { public static void Use(this Other o) => Projector.Lines(); }");

        Assert.True(IsUnreferencedStaticHolder(guard, [guard, unrelated]));
        Assert.False(IsUnreferencedStaticHolder(guard, [guard, unrelated, referencing]));
        // Extension methods are invoked without their class name, so their holder is never treated as unused.
        Assert.False(IsUnreferencedStaticHolder(extensions, [extensions, unrelated]));
    }

    [Fact]
    public void RetiredDirectPostingPaths_AreAbsentFromProduction()
    {
        var application = typeof(SecurityMasterCashFlowService).Assembly;
        Assert.Null(application.GetType("Meridian.Application.SecurityMaster.SecurityMasterLedgerBridge"));
        Assert.Null(application.GetType("Meridian.Application.SecurityMaster.ISecurityMasterLedgerBridge"));
        Assert.Null(application.GetType("Meridian.Application.SecurityMaster.CashFlow.StructuredCashFlowLedgerBridge"));
        Assert.Null(typeof(ISecurityMasterCashFlowService).GetMethod("BuildLedgerPostingsAsync"));
        Assert.Null(typeof(SecurityMasterCashFlowService).GetMethod("BuildLedgerPostingsAsync"));
    }

    private static IEnumerable<MethodInfo> DiscoverPostingCalculations()
    {
        // Journal builders and projectors: every public or internal method, static or instance, in the
        // ledger kernel and the financial-operations services that returns journal drafts, journal
        // lines, or a projection carrying them.
        var builders = new[] { typeof(FundEconomicsJournalFactory).Assembly, typeof(BondCouponRecognitionService).Assembly }
            .SelectMany(LoadableTypes)
            .Where(type => !IsCompilerGenerated(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                                                 BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => (method.IsPublic || method.IsAssembly) && !method.IsSpecialName &&
                             !IsCompilerGenerated(method) && method.DeclaringType!.GetInterfaces().All(contract =>
                                 !IsInterfaceImplementation(method, contract)) &&
                             BuildsJournalOrProjection(method.ReturnType));
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

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    private static bool IsCompilerGenerated(MemberInfo member)
        => member.Name.Contains('<') || member.GetCustomAttribute<CompilerGeneratedAttribute>() is not null;

    // An implementation of an interface member is reached through the interface, which the bridge
    // and posting-builder rules cover; it is not a separate calculation to connect.
    private static bool IsInterfaceImplementation(MethodInfo method, Type contract)
        => !method.IsStatic && !method.DeclaringType!.IsInterface &&
           method.DeclaringType.GetInterfaceMap(contract).TargetMethods.Contains(method);

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

    // A projection record that carries journal lines, as a property or through a parameterless
    // conversion such as ToLedgerLines(), is a posting calculation's result as well.
    private static bool BuildsJournalOrProjection(Type type)
    {
        if (BuildsJournal(type))
            return true;
        var candidate = Unwrap(type);
        if (candidate.IsGenericType || candidate.Namespace?.StartsWith("Meridian", StringComparison.Ordinal) != true)
            return false;
        return candidate.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                   .Any(property => BuildsJournal(property.PropertyType)) ||
               candidate.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                   .Any(method => !method.IsSpecialName && method.GetParameters().Length == 0 && BuildsJournal(method.ReturnType));
    }

    private static string Describe(MethodInfo method) => $"{method.DeclaringType!.Name}.{method.Name}";

    private static bool HasProductionCaller(MethodInfo method, IReadOnlyList<(string Path, string Code)> sources)
    {
        var typeName = method.DeclaringType!.Name;
        var declaration = new Regex($@"\b(class|interface|record|struct)\s+{Regex.Escape(typeName)}\b", RegexOptions.Compiled);
        // A static builder is called through its type; a bridge is called on an instance.
        // A static builder may also be passed as a method group (Select(Type.Method)); nameof is not a call.
        var call = new Regex(method.IsStatic
            ? $@"(?<!nameof\(\s*)\b{Regex.Escape(typeName)}\s*\.\s*{Regex.Escape(method.Name)}\b(?!\s*=[^=>])"
            : $@"\.\s*{Regex.Escape(method.Name)}\s*[(<]", RegexOptions.Compiled);
        // An instance call only counts where the caller also names the declaring type or one of its
        // contracts; otherwise any method sharing a common name (Project, Build) would connect it.
        var instanceOwners = method.IsStatic
            ? []
            : method.DeclaringType.GetInterfaces().Append(method.DeclaringType)
                .Select(type => new Regex($@"\b{Regex.Escape(StripArity(type.Name))}\b", RegexOptions.Compiled))
                .ToArray();
        // A sibling implementation or the interface declaring the method is not a caller.
        var implementers = method.IsStatic
            ? []
            : LoadableTypes(method.DeclaringType.Assembly)
                .Where(type => type != method.DeclaringType &&
                    (method.DeclaringType.IsAssignableFrom(type) || type.IsAssignableFrom(method.DeclaringType) ||
                     method.DeclaringType.GetInterfaces().Any(contract => contract.IsAssignableFrom(type))))
                .Concat(method.DeclaringType.GetInterfaces())
                .Where(type => type.Namespace?.StartsWith("Meridian", StringComparison.Ordinal) == true)
                .Select(type => new Regex($@"\b(class|interface)\s+{Regex.Escape(StripArity(type.Name))}\b", RegexOptions.Compiled))
                .ToArray();
        return sources.Any(source =>
            !declaration.IsMatch(source.Code) &&
            !implementers.Any(implementer => implementer.IsMatch(source.Code)) &&
            call.IsMatch(source.Code) &&
            (method.IsStatic || instanceOwners.Any(owner => owner.IsMatch(source.Code))) &&
            !IsUnreferencedStaticHolder(source, sources));
    }

    private static string StripArity(string name)
    {
        var tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

    private static readonly Regex StaticClassDeclaration = new(
        @"\bstatic\s+(?:partial\s+)?class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex AnyTypeDeclaration = new(
        @"\b(?:class|record|struct|interface)\s+[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
    private static readonly Regex ExtensionMethod = new(@"\(\s*this\s+", RegexOptions.Compiled);

    /// <summary>
    /// A caller that is itself unused does not connect a calculation. A file whose only declared types
    /// are static classes that no other production file names (and that define no extension methods,
    /// which are invoked without their class name) is a validator or helper nothing reaches.
    /// </summary>
    private static bool IsUnreferencedStaticHolder((string Path, string Code) caller, IReadOnlyList<(string Path, string Code)> sources)
    {
        var staticNames = StaticClassDeclaration.Matches(caller.Code).Select(match => match.Groups["name"].Value).Distinct().ToArray();
        if (staticNames.Length == 0 || AnyTypeDeclaration.Matches(caller.Code).Count != staticNames.Length ||
            ExtensionMethod.IsMatch(caller.Code))
            return false;
        return staticNames.All(name =>
        {
            var reference = new Regex($@"\b{Regex.Escape(name)}\b");
            return !sources.Any(other => !string.Equals(other.Path, caller.Path, StringComparison.Ordinal) &&
                                         reference.IsMatch(other.Code));
        });
    }

    private static IReadOnlyList<(string Path, string Code)> LoadProductionSources()
    {
        var root = Path.Combine(FindRepositoryRoot(), "src");
        var comment = new Regex(@"^\s*//.*$", RegexOptions.Multiline | RegexOptions.Compiled);
        var stringLiteral = new Regex(@"@?""(?:\\.|[^""\\\r\n])*""", RegexOptions.Compiled);
        return EnumerateCSharpFiles(root)
            // Comments, XML docs and string literals (marker reasons, messages) mention calculations
            // and their callers without calling or referencing them.
            .Select(file => (file, stringLiteral.Replace(comment.Replace(File.ReadAllText(file), string.Empty), "\"\"")))
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
