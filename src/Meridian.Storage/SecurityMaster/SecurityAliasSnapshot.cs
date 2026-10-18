using System.Collections;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// The current aliases of one security as the durable store read them, stamped with the number of
/// alias revisions recorded for that security at the read. Revisions are append-only, so the stamp
/// only grows, and it moves whenever an alias is created, corrected, retired or reinstated.
/// </summary>
/// <remarks>
/// Projection writers (amendments, rebuilds, warms) do not author aliases; they carry the alias list
/// they read back into the record they write. The stamp lets the store and the projection cache tell
/// that echo apart from current state: a projection replacement whose stamp no longer matches the
/// recorded history keeps the current alias rows instead of writing a superseded snapshot back, and
/// the cache keeps an installed alias list read at a newer stamp. A plain list (one a caller built
/// itself) carries no stamp and is applied as given.
/// </remarks>
internal sealed class SecurityAliasSnapshot : IReadOnlyList<SecurityAliasDto>
{
    private readonly IReadOnlyList<SecurityAliasDto> _aliases;

    public SecurityAliasSnapshot(IReadOnlyList<SecurityAliasDto> aliases, long revisionStamp)
    {
        _aliases = aliases;
        RevisionStamp = revisionStamp;
    }

    /// <summary>Alias revisions recorded for the security when the aliases were read.</summary>
    public long RevisionStamp { get; }

    public int Count => _aliases.Count;

    public SecurityAliasDto this[int index] => _aliases[index];

    public IEnumerator<SecurityAliasDto> GetEnumerator() => _aliases.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Returns <paramref name="incoming"/> with <paramref name="installed"/>'s aliases when both carry
    /// a stamp and the installed aliases were read at a newer one; otherwise <paramref name="incoming"/>.
    /// </summary>
    public static SecurityProjectionRecord KeepNewerAliases(
        SecurityProjectionRecord incoming,
        SecurityProjectionRecord installed)
        => incoming.Aliases is SecurityAliasSnapshot incomingAliases
           && installed.Aliases is SecurityAliasSnapshot installedAliases
           && installedAliases.RevisionStamp > incomingAliases.RevisionStamp
            ? incoming with { Aliases = installed.Aliases }
            : incoming;
}
