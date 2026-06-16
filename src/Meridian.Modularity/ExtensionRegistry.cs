namespace Meridian.Modularity;

/// <summary>
/// A keyed registry of extensions sharing a single contract type. Generalizes the proven
/// "keyed catalog with case-insensitive de-duplication" pattern (for example the provider
/// <c>DataSourceRegistry</c>) so the platform's hard-coded catalogs — reporting templates,
/// evidence templates, risk-rule sets — can become "built-ins plus discoverable additions"
/// without each one reinventing keying, de-duplication, and ordered enumeration.
/// </summary>
/// <typeparam name="TContract">The extension contract type held by this registry.</typeparam>
public sealed class ExtensionRegistry<TContract>
    where TContract : notnull
{
    private readonly List<TContract> _ordered = new();
    private readonly Dictionary<string, TContract> _byKey;
    private readonly Func<TContract, string> _keySelector;

    /// <summary>
    /// Creates a registry that derives each extension's key via <paramref name="keySelector"/>.
    /// Keys are compared with <paramref name="keyComparer"/> (defaults to
    /// <see cref="StringComparer.OrdinalIgnoreCase"/>, matching existing catalog behaviour).
    /// </summary>
    public ExtensionRegistry(Func<TContract, string> keySelector, IEqualityComparer<string>? keyComparer = null)
    {
        _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
        _byKey = new Dictionary<string, TContract>(keyComparer ?? StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Extensions in insertion order.</summary>
    public IReadOnlyList<TContract> Entries => _ordered;

    /// <summary>Number of registered extensions.</summary>
    public int Count => _ordered.Count;

    /// <summary>
    /// Adds an extension. Throws if its key is already registered.
    /// </summary>
    /// <exception cref="InvalidOperationException">An extension with the same key exists.</exception>
    public ExtensionRegistry<TContract> Add(TContract extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        var key = KeyOf(extension);
        if (!_byKey.TryAdd(key, extension))
            throw new InvalidOperationException($"An extension with key '{key}' is already registered.");
        _ordered.Add(extension);
        return this;
    }

    /// <summary>
    /// Adds an extension only if its key is not already registered. Returns whether it was added.
    /// </summary>
    public bool TryAdd(TContract extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        var key = KeyOf(extension);
        if (!_byKey.TryAdd(key, extension))
            return false;
        _ordered.Add(extension);
        return true;
    }

    /// <summary>
    /// Adds many extensions in order. Throws on the first duplicate key (use <see cref="TryAdd"/>
    /// to tolerate duplicates).
    /// </summary>
    public ExtensionRegistry<TContract> AddRange(IEnumerable<TContract> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        foreach (var extension in extensions)
            Add(extension);
        return this;
    }

    /// <summary>Whether an extension with the given key is registered.</summary>
    public bool Contains(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _byKey.ContainsKey(key);
    }

    /// <summary>Gets the extension registered under <paramref name="key"/>.</summary>
    /// <exception cref="KeyNotFoundException">No extension is registered under the key.</exception>
    public TContract Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_byKey.TryGetValue(key, out var extension))
            throw new KeyNotFoundException($"No extension is registered under key '{key}'.");
        return extension;
    }

    /// <summary>Attempts to get the extension registered under <paramref name="key"/>.</summary>
    public bool TryGet(string key, out TContract extension)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _byKey.TryGetValue(key, out extension!);
    }

    private string KeyOf(TContract extension)
    {
        var key = _keySelector(extension);
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException($"Extension of type '{extension.GetType().Name}' produced a null or empty key.");
        return key;
    }
}
