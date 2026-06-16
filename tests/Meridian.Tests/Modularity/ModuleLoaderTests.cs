using FluentAssertions;
using Meridian.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Modularity;

/// <summary>
/// Exercises the deterministic ordering, validation, and failure contracts of
/// <see cref="ModuleLoader"/> — the shared composition primitive the modularity refactor
/// converges every module pattern onto.
/// </summary>
public sealed class ModuleLoaderTests
{
    private sealed class FakeModule : IMeridianModule
    {
        private readonly string[] _dependsOn;
        private readonly Action<ModuleRegistrationContext>? _onRegister;
        private readonly ModuleValidationResult _validation;

        public FakeModule(
            string id,
            string[]? dependsOn = null,
            Action<ModuleRegistrationContext>? onRegister = null,
            ModuleValidationResult? validation = null)
        {
            ModuleId = id;
            _dependsOn = dependsOn ?? Array.Empty<string>();
            _onRegister = onRegister;
            _validation = validation ?? ModuleValidationResult.Valid;
        }

        public string ModuleId { get; }

        public IReadOnlyCollection<string> DependsOn => _dependsOn;

        public void Register(ModuleRegistrationContext context) => _onRegister?.Invoke(context);

        public ValueTask<ModuleValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_validation);
    }

    private static List<string> RegistrationOrder(ModuleLoader loader, List<string> sink)
    {
        loader.RegisterAll(new ModuleRegistrationContext(new ServiceCollection()));
        return sink;
    }

    [Fact]
    public void Order_PlacesDependenciesBeforeDependents()
    {
        var loader = new ModuleLoader()
            .Add(new FakeModule("c", dependsOn: new[] { "a", "b" }))
            .Add(new FakeModule("a"))
            .Add(new FakeModule("b", dependsOn: new[] { "a" }));

        var ordered = loader.Order().Select(m => m.ModuleId).ToList();

        ordered.Should().ContainInOrder("a", "b", "c");
    }

    [Fact]
    public void Order_PreservesInsertionOrder_ForIndependentModules()
    {
        var loader = new ModuleLoader()
            .Add(new FakeModule("first"))
            .Add(new FakeModule("second"))
            .Add(new FakeModule("third"));

        loader.Order().Select(m => m.ModuleId).Should().Equal("first", "second", "third");
    }

    [Fact]
    public void Order_Throws_OnUnknownDependency()
    {
        var loader = new ModuleLoader()
            .Add(new FakeModule("a", dependsOn: new[] { "missing" }));

        var act = () => loader.Order();

        act.Should().Throw<InvalidOperationException>().WithMessage("*unknown module 'missing'*");
    }

    [Fact]
    public void Order_Throws_OnCycle()
    {
        var loader = new ModuleLoader()
            .Add(new FakeModule("a", dependsOn: new[] { "b" }))
            .Add(new FakeModule("b", dependsOn: new[] { "a" }));

        var act = () => loader.Order();

        act.Should().Throw<InvalidOperationException>().WithMessage("*cycle*");
    }

    [Fact]
    public void Order_Throws_OnDuplicateId()
    {
        var loader = new ModuleLoader()
            .Add(new FakeModule("dup"))
            .Add(new FakeModule("dup"));

        var act = () => loader.Order();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate module id 'dup'*");
    }

    [Fact]
    public void Order_Throws_OnNullOrWhitespaceModuleId()
    {
        var loader = new ModuleLoader().Add(new FakeModule("  "));

        loader.Invoking(l => l.Order())
            .Should().Throw<InvalidOperationException>().WithMessage("*null or empty module id*");
    }

    [Fact]
    public void Order_Throws_OnNullOrWhitespaceDependencyId()
    {
        var loader = new ModuleLoader().Add(new FakeModule("a", dependsOn: new[] { "  " }));

        loader.Invoking(l => l.Order())
            .Should().Throw<InvalidOperationException>().WithMessage("*null or empty dependency id*");
    }

    [Fact]
    public void RegisterAll_InvokesEachModule_InDependencyOrder()
    {
        var calls = new List<string>();
        var loader = new ModuleLoader()
            .Add(new FakeModule("c", dependsOn: new[] { "b" }, onRegister: _ => calls.Add("c")))
            .Add(new FakeModule("a", onRegister: _ => calls.Add("a")))
            .Add(new FakeModule("b", dependsOn: new[] { "a" }, onRegister: _ => calls.Add("b")));

        RegistrationOrder(loader, calls);

        calls.Should().ContainInOrder("a", "b", "c");
    }

    [Fact]
    public async Task ValidateAllAsync_ReturnsResults_InOrder()
    {
        var loader = new ModuleLoader()
            .Add(new FakeModule("a"))
            .Add(new FakeModule("b", validation: ModuleValidationResult.Failure("bad")));

        var results = await loader.ValidateAllAsync();

        results.Should().HaveCount(2);
        results[0].IsValid.Should().BeTrue();
        results[1].IsValid.Should().BeFalse();
        results[1].FailureReason.Should().Be("bad");
    }
}
