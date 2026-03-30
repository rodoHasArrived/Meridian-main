using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for managing symbol groups and portfolios.
/// </summary>
public sealed class SymbolGroupService
{
    private static readonly Lazy<SymbolGroupService> _instance = new(() => new SymbolGroupService());

    private readonly ConfigService _configService;
    private SymbolGroupsConfig? _groupsConfig;

    // Predefined templates
    private static readonly Dictionary<string, (string Name, string[] Symbols, string Color, string Icon)> Templates = new()
    {
        ["FAANG"] = ("FAANG", new[] { "META", "AAPL", "AMZN", "NFLX", "GOOGL" }, "#E53935", "\uE80F"),
        ["MagnificentSeven"] = ("Magnificent 7", new[] { "AAPL", "MSFT", "GOOGL", "AMZN", "NVDA", "META", "TSLA" }, "#8E24AA", "\uE734"),
        ["MajorETFs"] = ("Major ETFs", new[] { "SPY", "QQQ", "IWM", "DIA", "VTI" }, "#1E88E5", "\uE9D9"),
        ["Semiconductors"] = ("Semiconductors", new[] { "NVDA", "AMD", "INTC", "TSM", "AVGO", "QCOM" }, "#43A047", "\uE950"),
        ["Financials"] = ("Financials", new[] { "JPM", "BAC", "WFC", "GS", "MS", "C" }, "#FB8C00", "\uE825"),
        ["Technology"] = ("Technology", new[] { "AAPL", "MSFT", "GOOGL", "META", "CRM", "ORCL", "IBM" }, "#00ACC1", "\uE772"),
        ["Healthcare"] = ("Healthcare", new[] { "JNJ", "UNH", "PFE", "MRK", "ABBV", "LLY" }, "#E91E63", "\uE8EA"),
        ["Energy"] = ("Energy", new[] { "XOM", "CVX", "COP", "SLB", "EOG", "PSX" }, "#FF5722", "\uE945")
    };

    public static SymbolGroupService Instance => _instance.Value;

    private SymbolGroupService()
    {
        _configService = new ConfigService();
    }

    /// <summary>
    /// Loads symbol groups configuration.
    /// </summary>
    public async Task<SymbolGroupsConfig> LoadGroupsAsync(CancellationToken ct = default)
    {
        var config = await _configService.LoadConfigAsync();
        _groupsConfig = config?.SymbolGroups ?? new SymbolGroupsConfig();

        if (_groupsConfig.Groups == null)
        {
            _groupsConfig.Groups = Array.Empty<SymbolGroup>();
        }

        return _groupsConfig;
    }

    /// <summary>
    /// Gets all symbol groups.
    /// </summary>
    public async Task<SymbolGroup[]> GetGroupsAsync(CancellationToken ct = default)
    {
        if (_groupsConfig == null)
        {
            await LoadGroupsAsync();
        }
        return _groupsConfig?.Groups ?? Array.Empty<SymbolGroup>();
    }

    /// <summary>
    /// Gets a symbol group by ID.
    /// </summary>
    public async Task<SymbolGroup?> GetGroupByIdAsync(string id, CancellationToken ct = default)
    {
        var groups = await GetGroupsAsync();
        return groups.FirstOrDefault(g => g.Id == id);
    }

    /// <summary>
    /// Creates a new symbol group.
    /// </summary>
    public async Task<SymbolGroup> CreateGroupAsync(
        string name,
        string? description = null,
        string? color = null,
        string? icon = null,
        string[]? symbols = null, CancellationToken ct = default)
    {
        var groups = (await GetGroupsAsync()).ToList();

        var newGroup = new SymbolGroup
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Description = description,
            Color = color ?? "#0078D4",
            Icon = icon ?? "\uE8D2",
            Symbols = symbols ?? Array.Empty<string>(),
            SortOrder = groups.Count,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        groups.Add(newGroup);
        await SaveGroupsAsync(groups.ToArray());

        GroupCreated?.Invoke(this, new SymbolGroupEventArgs { Group = newGroup });

        return newGroup;
    }

    /// <summary>
    /// Creates a group from a predefined template.
    /// </summary>
    public async Task<SymbolGroup?> CreateGroupFromTemplateAsync(string templateId, CancellationToken ct = default)
    {
        if (!Templates.TryGetValue(templateId, out var template))
        {
            return null;
        }

        return await CreateGroupAsync(
            template.Name,
            $"Predefined {template.Name} template",
            template.Color,
            template.Icon,
            template.Symbols);
    }

    /// <summary>
    /// Updates an existing symbol group.
    /// </summary>
    public async Task<bool> UpdateGroupAsync(SymbolGroup group, CancellationToken ct = default)
    {
        var groups = (await GetGroupsAsync()).ToList();
        var index = groups.FindIndex(g => g.Id == group.Id);

        if (index < 0)
            return false;

        group.UpdatedAt = DateTime.UtcNow;
        groups[index] = group;

        await SaveGroupsAsync(groups.ToArray());

        GroupUpdated?.Invoke(this, new SymbolGroupEventArgs { Group = group });

        return true;
    }

    /// <summary>
    /// Deletes a symbol group.
    /// </summary>
    public async Task<bool> DeleteGroupAsync(string id, CancellationToken ct = default)
    {
        var groups = (await GetGroupsAsync()).ToList();
        var group = groups.FirstOrDefault(g => g.Id == id);

        if (group == null)
            return false;

        groups.RemoveAll(g => g.Id == id);
        await SaveGroupsAsync(groups.ToArray());

        GroupDeleted?.Invoke(this, new SymbolGroupEventArgs { Group = group });

        return true;
    }

    /// <summary>
    /// Adds a symbol to a group.
    /// </summary>
    public async Task<bool> AddSymbolToGroupAsync(string groupId, string symbol, CancellationToken ct = default)
    {
        var group = await GetGroupByIdAsync(groupId);
        if (group == null)
            return false;

        if (group.Symbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            return true; // Already in group

        var symbols = group.Symbols.ToList();
        symbols.Add(symbol.ToUpper());
        group.Symbols = symbols.ToArray();

        return await UpdateGroupAsync(group);
    }

    /// <summary>
    /// Removes a symbol from a group.
    /// </summary>
    public async Task<bool> RemoveSymbolFromGroupAsync(string groupId, string symbol, CancellationToken ct = default)
    {
        var group = await GetGroupByIdAsync(groupId);
        if (group == null)
            return false;

        var symbols = group.Symbols.ToList();
        symbols.RemoveAll(s => s.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        group.Symbols = symbols.ToArray();

        return await UpdateGroupAsync(group);
    }

    /// <summary>
    /// Moves a symbol between groups.
    /// </summary>
    public async Task<bool> MoveSymbolAsync(string symbol, string? fromGroupId, string toGroupId, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(fromGroupId))
        {
            await RemoveSymbolFromGroupAsync(fromGroupId, symbol);
        }

        return await AddSymbolToGroupAsync(toGroupId, symbol);
    }

    /// <summary>
    /// Gets all groups that contain a symbol.
    /// </summary>
    public async Task<SymbolGroup[]> GetGroupsForSymbolAsync(string symbol, CancellationToken ct = default)
    {
        var groups = await GetGroupsAsync();
        return groups.Where(g =>
            g.Symbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>
    /// Gets symbols that are not in any group.
    /// </summary>
    public async Task<string[]> GetUngroupedSymbolsAsync(CancellationToken ct = default)
    {
        var config = await _configService.LoadConfigAsync();
        var allSymbols = config?.Symbols?.Select(s => s.Symbol?.ToUpper())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet() ?? new HashSet<string?>();

        var groups = await GetGroupsAsync();
        var groupedSymbols = groups.SelectMany(g => g.Symbols)
            .Select(s => s.ToUpper())
            .ToHashSet();

        return allSymbols.Where(s => !groupedSymbols.Contains(s!))
            .Select(s => s!)
            .ToArray();
    }

    /// <summary>
    /// Reorders groups.
    /// </summary>
    public async Task ReorderGroupsAsync(int oldIndex, int newIndex, CancellationToken ct = default)
    {
        var groups = (await GetGroupsAsync()).ToList();

        if (oldIndex < 0 || oldIndex >= groups.Count ||
            newIndex < 0 || newIndex >= groups.Count)
            return;

        var group = groups[oldIndex];
        groups.RemoveAt(oldIndex);
        groups.Insert(newIndex, group);

        // Update sort orders
        for (int i = 0; i < groups.Count; i++)
        {
            groups[i].SortOrder = i;
        }

        await SaveGroupsAsync(groups.ToArray());

        GroupsReordered?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets available templates.
    /// </summary>
    public IEnumerable<(string Id, string Name, string[] Symbols, string Color)> GetTemplates()
    {
        return Templates.Select(kvp => (kvp.Key, kvp.Value.Name, kvp.Value.Symbols, kvp.Value.Color));
    }

    private async Task SaveGroupsAsync(SymbolGroup[] groups, CancellationToken ct = default)
    {
        var config = await _configService.LoadConfigAsync() ?? new AppConfig();

        config.SymbolGroups ??= new SymbolGroupsConfig();
        config.SymbolGroups.Groups = groups;

        await _configService.SaveConfigAsync(config);
        _groupsConfig = config.SymbolGroups;
    }

    /// <summary>
    /// Event raised when a group is created.
    /// </summary>
    public event EventHandler<SymbolGroupEventArgs>? GroupCreated;

    /// <summary>
    /// Event raised when a group is updated.
    /// </summary>
    public event EventHandler<SymbolGroupEventArgs>? GroupUpdated;

    /// <summary>
    /// Event raised when a group is deleted.
    /// </summary>
    public event EventHandler<SymbolGroupEventArgs>? GroupDeleted;

    /// <summary>
    /// Event raised when groups are reordered.
    /// </summary>
    public event EventHandler? GroupsReordered;
}

/// <summary>
/// Symbol group event args.
/// </summary>
public sealed class SymbolGroupEventArgs : EventArgs
{
    public SymbolGroup? Group { get; set; }
}
