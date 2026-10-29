using Microsoft.Extensions.AI;
using Simorgh.AI.Routing.Abstractions;

namespace Simorgh.AI.Routing.Strategies;

/// <summary>Selects one target using its configured relative weight.</summary>
public sealed class WeightedRoutingStrategy : IChatRoutingStrategy
{
    private readonly WeightedTarget[] _targets;
    private readonly double _total;
    private readonly Random _random = new();
    private readonly object _lock = new();

    public WeightedRoutingStrategy(IEnumerable<WeightedTarget> targets)
    {
        _targets = targets?.ToArray() ?? throw new ArgumentNullException(nameof(targets));
        if (_targets.Length == 0 || _targets.Any(target => target is null))
            throw new ArgumentException("At least one non-null weighted target is required.", nameof(targets));
        _total = _targets.Sum(entry => entry.Weight);
        if (double.IsInfinity(_total))
            throw new ArgumentException("Total weight must be finite.", nameof(targets));
    }

    private IChatClient Select()
    {
        double value;
        lock (_lock) value = _random.NextDouble() * _total;
        foreach (var entry in _targets)
        {
            value -= entry.Weight;
            if (value < 0) return entry.Target.Client;
        }
        return _targets[_targets.Length - 1].Target.Client;
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Select().GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Select().GetStreamingResponseAsync(messages, options, cancellationToken);
}
