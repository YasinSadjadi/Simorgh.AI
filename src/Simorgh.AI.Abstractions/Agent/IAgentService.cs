using Simorgh.AI.Abstractions.Agent.Cancellation;
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
    ///     Returns a copy of this service that runs with the given system instructions.
    ///     The instructions are sent as the system message at the start of every run.
    ///     All other configuration (chat client, tools, middleware) is carried over.
    /// </summary>
    IAgentService WithInstructions(string instructions);

    /// <summary>
    ///     Returns a copy of this service whose submitted runs are registered in the given
    ///     registry, so they can be cancelled by id — including from other servers when the
    ///     registry's broadcaster uses a distributed transport.
    /// </summary>
    IAgentService WithRunRegistry(IAgentRunRegistry runRegistry);

    /// <summary>
    ///     Runs the agent to completion and returns its final answer with the response
    ///     metadata (usage, finish reason). Intermediate activity is not surfaced.
    /// </summary>
    /// <exception cref="AgentRunException">The run failed.</exception>
    Task<AiResponse> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Submits a run for execution and returns immediately.
    ///     Progress is reported through <see cref="IAgentRun.Updated" />;
    ///     the final answer is available from <see cref="IAgentRun.Completion" />.
    /// </summary>
    /// <param name="request">The run to execute.</param>
    /// <param name="cancellationToken">Cancels the run's execution.</param>
    /// <param name="subscribe">
    ///     Optional callback invoked synchronously — before the run starts executing —
    ///     with the run handle itself, so handlers can be attached to
    ///     <see cref="IAgentRun.Updated" /> before any update is raised. Subscribing here
    ///     guarantees the handler sees every update, starting with
    ///     <see cref="AgentRunStartedUpdate" />; attaching to the event after
    ///     <c>Submit</c> returns may miss updates from runs that start quickly.
    /// </param>
    IAgentRun Submit(
        AgentRunRequest request,
        CancellationToken cancellationToken = default,
        Action<IAgentRun>? subscribe = null);
}