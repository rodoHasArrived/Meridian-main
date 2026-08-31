// CallerArgumentExpression lives here and is not covered by ImplicitUsings.
using System.Runtime.CompilerServices;

namespace Meridian.Contracts.Text;

/// <summary>
/// The shared home for the trivial string helpers that were previously redeclared as
/// <c>private static</c> in hundreds of files.
/// </summary>
/// <remarks>
/// <para>
/// This lives in <c>Meridian.Contracts</c> rather than <c>Meridian.Core</c> for a dependency
/// reason, not a taxonomic one: most of the files that need it are in projects that do not
/// reference Core, and one of them is Contracts itself, which sits below Core and must not
/// reference upward. Contracts has no project references at all, so it is the only layer every
/// caller can already see.
/// </para>
/// <para>
/// These are one-liners, so the argument for centralising them is not the line count — it is that
/// copy-paste had become the normal way to obtain one, and a copied helper is free to drift. That
/// is not hypothetical here: before this type existed, <c>NormalizeOptional</c> had already split
/// into three behaviours under one name and signature. Most copies trimmed to null, one also
/// lower-cased, and one also upper-cased, so the same call spelled the same way meant different
/// things in different files.
/// </para>
/// <para>
/// Anything that is not purely mechanical belongs with its domain instead of here. Currency,
/// symbol, and fund-profile canonicalisation encode real rules and deserve an owner and a test next
/// to the code those rules serve; putting them in a general text bag would repeat the mistake at a
/// larger scale.
/// </para>
/// </remarks>
public static class TextPrimitives
{
    /// <summary>
    /// Returns <paramref name="value"/> trimmed, or <see langword="null"/> when it is null, empty,
    /// or entirely white space.
    /// </summary>
    /// <remarks>
    /// Case is deliberately preserved. A caller that needs case folding should say so at the call
    /// site rather than rely on a helper name that does not mention it.
    /// </remarks>
    public static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Returns <paramref name="value"/> trimmed, throwing when it is null, empty, or entirely white
    /// space.
    /// </summary>
    /// <param name="value">The candidate text.</param>
    /// <param name="paramName">
    /// The parameter name reported on failure. Supplied automatically from the caller's expression
    /// so the thrown message names the offending argument rather than <c>value</c>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> is null, empty, or entirely white space.
    /// </exception>
    public static string RequireText(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must be non-empty text.", paramName);
        }

        return value.Trim();
    }

    /// <summary>
    /// Returns the first entry of <paramref name="values"/> that is not null, empty, or entirely
    /// white space, trimmed; or <see langword="null"/> when every entry is blank.
    /// </summary>
    public static string? FirstNonBlank(params string?[] values)
    {
        if (values is null)
        {
            return null;
        }

        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
