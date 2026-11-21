using Meridian.Core.Config;
using Meridian.Application.UI;
using Microsoft.Extensions.Options;

namespace Meridian.Wpf.Features;

/// <summary>
/// Resolves feature capability availability from monitored options and persisted overrides, and publishes changes.
/// </summary>
public sealed class FeatureCapabilityGateService : IFeatureCapabilityGate, IDisposable
{
    private readonly IOptionsMonitor<FeatureCapabilityOptions> _options;
    private readonly ConfigStore _configStore;
    private readonly Dictionary<string, FeatureCapabilityDescriptor> _descriptors;
    private readonly Dictionary<string, bool> _overrides;
    private readonly CapabilityChangeObservable _changes = new();
    private readonly IDisposable? _optionsSubscription;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public FeatureCapabilityGateService(
        IOptionsMonitor<FeatureCapabilityOptions> options,
        ConfigStore configStore,
        IEnumerable<FeatureCapabilityDescriptor> descriptors)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        ArgumentNullException.ThrowIfNull(descriptors);

        _descriptors = descriptors
            .GroupBy(static descriptor => descriptor.CapabilityKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        _overrides = NormalizeOverrides(_options.CurrentValue.Overrides);
        _optionsSubscription = _options.OnChange(OnOptionsChanged);
    }

    public IObservable<CapabilityChangedEvent> Changes => _changes;

    public bool IsEnabled(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        lock (_sync)
        {
            if (_descriptors.TryGetValue(key, out var descriptor) && descriptor.IsPermanent)
            {
                return true;
            }

            if (_overrides.TryGetValue(key, out var overrideValue))
            {
                return overrideValue;
            }

            return _descriptors.TryGetValue(key, out descriptor) && descriptor.DefaultEnabled;
        }
    }

    public void SetEnabled(string key, bool enabled)
    {
        var change = ApplyOverride(key, enabled, out var snapshot);

        _configStore.SaveFeatureCapabilityOverridesAsync(snapshot).GetAwaiter().GetResult();

        if (change is not null)
        {
            _changes.Publish(change);
        }
    }

    public async Task SetEnabledAsync(string key, bool enabled, CancellationToken ct = default)
    {
        var change = ApplyOverride(key, enabled, out _);

        // Saves are serialized and each one writes the overrides as they stand when its turn
        // comes, so rapid toggles cannot leave an older snapshot as the last one on disk. The
        // write runs on the thread pool: acquiring the configuration file lock can take seconds
        // and must never be done on the caller's (UI) thread. The awaits deliberately keep the
        // caller's context so the change notification is published where the caller expects it.
        await _saveGate.WaitAsync(ct);
        try
        {
            Dictionary<string, bool> latest;
            lock (_sync)
            {
                latest = new Dictionary<string, bool>(_overrides, StringComparer.OrdinalIgnoreCase);
            }

            await Task.Run(() => _configStore.SaveFeatureCapabilityOverridesAsync(latest, ct), ct);
        }
        finally
        {
            _saveGate.Release();
        }

        if (change is not null)
        {
            _changes.Publish(change);
        }
    }

    private CapabilityChangedEvent? ApplyOverride(string key, bool enabled, out Dictionary<string, bool> snapshot)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Capability key is required.", nameof(key));
        }

        lock (_sync)
        {
            if (_descriptors.TryGetValue(key, out var descriptor) && descriptor.IsPermanent)
            {
                enabled = true;
            }

            var previous = IsEnabled(key);
            _overrides[key] = enabled;
            var current = IsEnabled(key);
            snapshot = new Dictionary<string, bool>(_overrides, StringComparer.OrdinalIgnoreCase);

            return previous != current ? new CapabilityChangedEvent(key, current) : null;
        }
    }

    public void Dispose()
    {
        _optionsSubscription?.Dispose();
        _saveGate.Dispose();
        _changes.Complete();
    }

    private void OnOptionsChanged(FeatureCapabilityOptions options)
    {
        List<CapabilityChangedEvent> changes = [];

        lock (_sync)
        {
            var next = NormalizeOverrides(options.Overrides);
            var keys = _descriptors.Keys
                .Concat(_overrides.Keys)
                .Concat(next.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var before = keys.ToDictionary(static key => key, IsEnabled, StringComparer.OrdinalIgnoreCase);

            _overrides.Clear();
            foreach (var entry in next)
            {
                _overrides[entry.Key] = entry.Value;
            }

            foreach (var key in keys)
            {
                var after = IsEnabled(key);
                if (before[key] != after)
                {
                    changes.Add(new CapabilityChangedEvent(key, after));
                }
            }
        }

        foreach (var change in changes)
        {
            _changes.Publish(change);
        }
    }

    private static Dictionary<string, bool> NormalizeOverrides(IReadOnlyDictionary<string, bool>? overrides)
        => overrides is null
            ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            : overrides
                .Where(static entry => !string.IsNullOrWhiteSpace(entry.Key))
                .ToDictionary(static entry => entry.Key, static entry => entry.Value, StringComparer.OrdinalIgnoreCase);

    private sealed class CapabilityChangeObservable : IObservable<CapabilityChangedEvent>
    {
        private readonly object _sync = new();
        private readonly List<IObserver<CapabilityChangedEvent>> _observers = [];
        private bool _completed;

        public IDisposable Subscribe(IObserver<CapabilityChangedEvent> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            lock (_sync)
            {
                if (_completed)
                {
                    observer.OnCompleted();
                    return new Subscription(this, observer);
                }

                _observers.Add(observer);
                return new Subscription(this, observer);
            }
        }

        public void Publish(CapabilityChangedEvent value)
        {
            IObserver<CapabilityChangedEvent>[] observers;
            lock (_sync)
            {
                observers = _observers.ToArray();
            }

            foreach (var observer in observers)
            {
                observer.OnNext(value);
            }
        }

        public void Complete()
        {
            IObserver<CapabilityChangedEvent>[] observers;
            lock (_sync)
            {
                if (_completed)
                {
                    return;
                }

                _completed = true;
                observers = _observers.ToArray();
                _observers.Clear();
            }

            foreach (var observer in observers)
            {
                observer.OnCompleted();
            }
        }

        private void Unsubscribe(IObserver<CapabilityChangedEvent> observer)
        {
            lock (_sync)
            {
                _observers.Remove(observer);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private CapabilityChangeObservable? _owner;
            private readonly IObserver<CapabilityChangedEvent> _observer;

            public Subscription(CapabilityChangeObservable owner, IObserver<CapabilityChangedEvent> observer)
            {
                _owner = owner;
                _observer = observer;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _owner, null)?.Unsubscribe(_observer);
            }
        }
    }
}
