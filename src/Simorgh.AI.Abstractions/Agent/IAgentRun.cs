using Simorgh.AI.Abstractions.Messages;

namespace Simorgh.AI.Abstractions.Agent;

/// <summary>
///     A handle to an agent run that was submitted via <see cref="IAgentService.Submit" />.
///     Updates are raised on <see cref="Updated" /> as the run progresses.
/// </summary>
public interface IAgentRun
{
    /// <summary>Unique id of this run.</summary>
    string RunId { get; }

    /// <summary>
    ///     Completes with the agent's final answer, or faults with the failure that ended the run.
    /// </summary>
    Task<AiMessage> Completion { get; }

    /// <summary>Raised for every <see cref="AgentUpdate" /> produced by the run, on an unspecified thread.</summary>
    event Action<AgentUpdate>? Updated;
}