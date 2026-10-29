using Simorgh.AI.Routing.Models;

namespace Simorgh.AI.Routing.Internal;

internal static class RoutingValidation
{
    public static RoutingTarget[] Targets(IEnumerable<RoutingTarget> targets)
    {
        var result = targets?.ToArray() ?? throw new ArgumentNullException(nameof(targets));
        if (result.Length == 0 || result.Any(target => target is null))
            throw new ArgumentException("At least one non-null target is required.", nameof(targets));
        return result;
    }
}