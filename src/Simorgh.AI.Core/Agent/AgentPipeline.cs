using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Agent.Middleware;
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;

namespace Simorgh.AI.Core.Agent;

/// <summary>
///     Runs the registered middleware in registration order, threading each stage's output
///     through them so the agent service consumes the final value.
/// </summary>
/// <param name="middlewares">Middleware to execute, in registration order.</param>
public sealed class AgentPipeline(IEnumerable<AgentMiddleware>? middlewares)
{
    /// <summary>Middleware to execute, in registration order.</summary>
    public readonly IReadOnlyList<AgentMiddleware> Middlewares = middlewares?.ToArray() ?? [];

    /// <summary>Runs <see cref="AgentMiddleware.OnStartupAsync" /> on every middleware, in registration order.</summary>
    public Task OnStartupAsync(AgentRunContext context, CancellationToken cancellationToken)
    {
        return RunSequentially(m => m.OnStartupAsync(context, cancellationToken), cancellationToken);
    }

    /// <summary>Runs <see cref="AgentMiddleware.OnEndAsync" /> on every middleware, in registration order.</summary>
    public Task OnEndAsync(AgentRunContext context, CancellationToken cancellationToken)
    {
        return RunSequentially(m => m.OnEndAsync(context, cancellationToken), cancellationToken);
    }

    /// <summary>
    ///     Runs <see cref="AgentMiddleware.OnBeforeAgentResponseAsync" /> on every middleware,
    ///     in registration order, before the model call.
    /// </summary>
    public Task OnBeforeAgentResponseAsync(AgentRunContext context, CancellationToken cancellationToken)
    {
        return RunSequentially(
            m => m.OnBeforeAgentResponseAsync(context, cancellationToken), cancellationToken);
    }

    /// <summary>
    ///     Passes the model response and its usage through
    ///     <see cref="AgentMiddleware.OnAfterAgentResponseAsync" /> on every middleware, in
    ///     registration order, and returns the final response.
    /// </summary>
    public async Task<AiMessage> OnAfterAgentResponseAsync(
        AgentRunContext context, AiMessage response, AgentResponseUsage? usage,
        CancellationToken cancellationToken)
    {
        foreach (var middleware in Middlewares)
        {
            cancellationToken.ThrowIfCancellationRequested();
            response = await middleware.OnAfterAgentResponseAsync(context, response, usage, cancellationToken)
                .ConfigureAwait(false);
        }

        return response;
    }

    /// <summary>
    ///     Passes the tool call through <see cref="AgentMiddleware.OnBeforeToolCallAsync" /> on every
    ///     middleware, in registration order, and returns the call to execute.
    /// </summary>
    public async Task<AiToolCallContent> OnBeforeToolCallAsync(
        AgentRunContext context, AiToolCallContent call, CancellationToken cancellationToken)
    {
        foreach (var middleware in Middlewares)
        {
            cancellationToken.ThrowIfCancellationRequested();
            call = await middleware.OnBeforeToolCallAsync(context, call, cancellationToken)
                .ConfigureAwait(false);
        }

        return call;
    }

    /// <summary>
    ///     Passes the tool result through <see cref="AgentMiddleware.OnAfterToolCallAsync" /> on every
    ///     middleware, in registration order, and returns the final result.
    /// </summary>
    public async Task<AiToolResultContent> OnAfterToolCallAsync(
        AgentRunContext context, AiToolCallContent call, AiToolResultContent result,
        CancellationToken cancellationToken)
    {
        foreach (var middleware in Middlewares)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result = await middleware.OnAfterToolCallAsync(context, call, result, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    private async Task RunSequentially(
        Func<AgentMiddleware, Task> invoke, CancellationToken cancellationToken)
    {
        foreach (var middleware in Middlewares)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await invoke(middleware).ConfigureAwait(false);
        }
    }
}
