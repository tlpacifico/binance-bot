using BinanceBot.Core.Enums;

namespace BinanceBot.Core;

public sealed class BotControlState
{
    private volatile BotRunState _runState = BotRunState.Running;
    private volatile bool _rebalanceRequested;
    private readonly object _lock = new();

    public BotRunState RunState
    {
        get => _runState;
        set { lock (_lock) _runState = value; }
    }

    public bool IsRunning => _runState == BotRunState.Running;

    public bool ConsumeRebalanceRequest()
    {
        lock (_lock)
        {
            if (!_rebalanceRequested) return false;
            _rebalanceRequested = false;
            return true;
        }
    }

    public void RequestRebalance()
    {
        lock (_lock) _rebalanceRequested = true;
    }

    public void Start() => RunState = BotRunState.Running;
    public void Pause() => RunState = BotRunState.Paused;
}
