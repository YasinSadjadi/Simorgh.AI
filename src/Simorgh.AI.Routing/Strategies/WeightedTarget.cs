using Simorgh.AI.Routing.Models;

namespace Simorgh.AI.Routing.Strategies;

/// <summary>A routing target and its relative weight for weighted routing.</summary>
public sealed class WeightedTarget
{
    public WeightedTarget(RoutingTarget target, double weight)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        if (weight <= 0 || double.IsNaN(weight) || double.IsInfinity(weight))
            throw new ArgumentOutOfRangeException(nameof(weight), "Weight must be positive and finite.");
        Weight = weight;
    }

    public RoutingTarget Target { get; }
    public double Weight { get; }
}
