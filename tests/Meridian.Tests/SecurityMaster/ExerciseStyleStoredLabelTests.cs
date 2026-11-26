using System.Reflection;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.FSharp.Domain;
using Microsoft.FSharp.Core;
using Microsoft.FSharp.Reflection;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The exercise-style label stored for a listed option is spelled out per union case, so renaming a
/// case in code cannot change stored values, and a new case cannot ship without a label.
/// </summary>
public sealed class ExerciseStyleStoredLabelTests
{
    private static readonly MethodInfo Canonical = typeof(SecurityMasterMapping)
        .GetMethod("CanonicalExerciseStyle", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void EveryExerciseStyleCase_HasItsPinnedStoredLabel()
    {
        var stored = FSharpType.GetUnionCases(typeof(ExerciseStyle), null)
            .Select(unionCase => (ExerciseStyle)FSharpValue.MakeUnion(unionCase, [], null))
            .Select(style => ((FSharpOption<string>)Canonical.Invoke(null, [FSharpOption<ExerciseStyle>.Some(style)])!).Value);

        stored.Should().Equal("American", "European", "Bermudan");
    }

    [Fact]
    public void AnUndeclaredExerciseStyle_StaysUndeclared()
        => Canonical.Invoke(null, [FSharpOption<ExerciseStyle>.None]).Should().BeNull();
}
