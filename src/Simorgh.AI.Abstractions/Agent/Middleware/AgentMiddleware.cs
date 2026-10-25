using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;

namespace Simorgh.AI.Abstractions.Agent.Middleware;

/// <summary>
///     Extends agent execution. Each middleware receives the output of a stage (the model
///     response, a tool result) as an input parameter and returns what the agent service
///     should use from that point on. Middleware execute in registration order.
/// </summary>
public abstract class AgentMiddleware : IDisposable
{
    /// <summary>Releases resources held by the middleware; called when the owning client or run is disposed.</summary>
    public virtual void Dispose()
    {
    }

    /// <summary>Called once when the run starts, before anything else.</summary>
    public virtual Task OnStartupAsync(
        AgentRunContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Called before each model round; may adjust <see cref="AgentRunContext.Messages" />
    ///     ahead of the model call.
    /// </summary>
    public virtual Task OnBeforeAgentResponseAsync(
        AgentRunContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Called after each model round with the response the model produced and the usage it
    ///     reported (<see cref="AgentResponseUsage" />, <c>null</c> when the provider reports none);
    ///     return the response the agent service should use instead.
    /// </summary>
    public virtual Task<AiMessage> OnAfterAgentResponseAsync(
        AgentRunContext context, AiMessage response, AgentResponseUsage? usage,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(response);
    }

    /// <summary>
    ///     Called before each tool invocation with the call the model issued; return the
    ///     call the agent service should execute instead.
    /// </summary>
    public virtual Task<AiToolCallContent> OnBeforeToolCallAsync(
        AgentRunContext context, AiToolCallContent call, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(call);
    }

    /// <summary>
    ///     Called after each tool invocation with the result the tool produced; return the
    ///     result the agent service should feed back to the model instead.
    /// </summary>
    public virtual Task<AiToolResultContent> OnAfterToolCallAsync(
        AgentRunContext context, AiToolCallContent call, AiToolResultContent result,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(result);
    }

    /// <summary>Called once when the run ends, whether it succeeded, failed, or was cancelled.</summary>
    public virtual Task OnEndAsync(
        AgentRunContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
