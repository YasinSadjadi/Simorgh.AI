using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Tools;
using Simorgh.AI.Extensions.Tools;

namespace Simorgh.AI.Extensions;

/// <summary>
///     Extension methods that attach the framework's built-in tool-calling strategies
///     to an <see cref="IAgentService" />.
/// </summary>
public static class ToolCallingStrategyExtensions
{
    /// <summary>
    ///     Returns a copy of this agent service that uses the instant tool-calling strategy:
    ///     every registered tool is advertised to the model in every round
    ///     (<see cref="InstantToolCallingStrategy" />).
    /// </summary>
    public static IAgentService WithInstantToolCalling(this IAgentService agent)
    {
        return agent is null
            ? throw new ArgumentNullException(nameof(agent))
            : agent.WithToolCallingStrategy(new InstantToolCallingStrategy());
    }

    /// <summary>
    ///     Returns a copy of this agent service that uses the lazy tool-calling strategy:
    ///     the model discovers tools via a <c>list_tools</c> meta-tool and pulls the ones
    ///     it needs into the conversation with <c>load_tool</c>
    ///     (<see cref="LazyToolCallingStrategy" />). Suited to large tool registries.
    /// </summary>
    public static IAgentService WithLazyToolCalling(this IAgentService agent)
    {
        return agent is null
            ? throw new ArgumentNullException(nameof(agent))
            : agent.WithToolCallingStrategy(new LazyToolCallingStrategy());
    }
}