using BinanceBot.Core.Interfaces;

namespace BinanceBot.Core;

public sealed class StrategyResolver
{
    private readonly Dictionary<string, ITradingStrategy> _strategies = new(StringComparer.OrdinalIgnoreCase);
    private volatile string _activeKey = string.Empty;
    private readonly object _lock = new();

    public void Register(string key, ITradingStrategy strategy)
    {
        lock (_lock)
        {
            _strategies[key] = strategy;
            if (_activeKey == string.Empty)
                _activeKey = key;
        }
    }

    public ITradingStrategy CurrentStrategy
    {
        get
        {
            lock (_lock)
            {
                if (_strategies.TryGetValue(_activeKey, out var strategy))
                    return strategy;
                throw new InvalidOperationException($"No strategy registered with key '{_activeKey}'");
            }
        }
    }

    public string ActiveKey
    {
        get { lock (_lock) return _activeKey; }
    }

    public bool TrySetActive(string key)
    {
        lock (_lock)
        {
            if (!_strategies.ContainsKey(key)) return false;
            _activeKey = key;
            return true;
        }
    }

    public IReadOnlyDictionary<string, string> GetAvailable()
    {
        lock (_lock)
        {
            return _strategies.ToDictionary(kv => kv.Key, kv => kv.Value.Description);
        }
    }
}
