using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Abstractions.Tools;

namespace Simorgh.AI.Abstractions.Agent;

/// <summary>
///     Executes agent runs in two modes: directly (awaited) or submitted (observed via updates).
/// </summary>
public interface IAgentService
{
    /// <summary>
    ///     Returns a copy of this service that runs with the given tool-calling strategy.
    ///     All other configuration (chat client, tools, middleware) is carried over.
    /// </summary>
    IAgentService WithToolCallingStrategy(IToolCallingStrategy strategy);

    /// <summary>
    ///     Runs the agent to completion and returns its final answer.
    ///     Intermediate activity is not surfaced.
    /// </summary>
    /// <exception cref="AgentRunException">The run failed.</exception>
    Task<AiMessage> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Submits a run for execution and returns immediately.
    ///     Progress is reported through <see cref="IAgentRun.Updated" />;
    ///     the final answer is available from <see cref="IAgentRun.Completion" />.
    /// </summary>
    IAgentRun Submit(AgentRunRequest request, CancellationToken cancellationToken = default);
}