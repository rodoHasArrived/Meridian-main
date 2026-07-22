namespace Meridian.Ledger;

/// <summary>
/// Mutable, thread-safe registry of reusable <see cref="JournalTemplate"/>s keyed by template id.
/// Registering a template again under the same id replaces it, so administrators can amend a
/// template's lines while keeping its identity stable across the recurring journals that reference it.
/// </summary>
public sealed class JournalTemplateBook
{
    private readonly object _gate = new();
    private readonly Dictionary<string, JournalTemplate> _templates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers or replaces a template and returns it.</summary>
    public JournalTemplate Register(JournalTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        lock (_gate)
        {
            _templates[template.TemplateId] = template;
            return template;
        }
    }

    /// <summary>Attempts to resolve a template by id.</summary>
    public bool TryGet(string templateId, out JournalTemplate? template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        lock (_gate)
        {
            return _templates.TryGetValue(templateId.Trim(), out template);
        }
    }

    /// <summary>Resolves a template by id or throws when it is not registered.</summary>
    public JournalTemplate Get(string templateId)
    {
        if (TryGet(templateId, out var template) && template is not null)
            return template;

        throw new KeyNotFoundException($"Journal template '{templateId}' is not registered.");
    }

    /// <summary>All registered templates, ordered by name then id.</summary>
    public IReadOnlyList<JournalTemplate> Templates
    {
        get
        {
            lock (_gate)
            {
                return _templates.Values
                    .OrderBy(static template => template.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static template => template.TemplateId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    /// <summary>Instantiates a registered template against the supplied inputs.</summary>
    public JournalTemplateInstance Instantiate(string templateId, JournalTemplateInstantiation instantiation)
        => Get(templateId).Instantiate(instantiation);
}
