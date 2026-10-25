using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;

namespace Simorgh.AI.Abstractions.Agent.Middleware;

/// <summary>
///     Extends agent execution. Each middleware receives the output of a stage (the model
///     response, a tool result) as an input parameter and returns what the agent service
///     should use from that point on. Middleware execute in registration order.
///     Hooks do not throw; they return an <see cref="AgentResult" />: <c>Ok</c> continues,
///     <c>Warn</c> records a warning and continues, and <c>Fail</c> breaks the run and
///     moves it to the end state. Model and tool failures are delivered to the after hooks
///     through their <c>error</c> parameter instead.
/// </summary>
public abstract class AgentMiddleware : IDisposable
{
    /// <summary>Releases resources held by the middleware; called when the owning client or run is disposed.</summary>
    public virtual void Dispose()
    {
    }

    /// <summary>Called once when the run starts, before anything else.</summary>
    public virtual Task<AgentResult> OnStartupAsync(
        AgentRunContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(AgentResult.Ok());
    }

    /// <summary>
    ///     Called before each model round; may adjust <see cref="AgentRunContext.Messages" />
    ///     ahead of the model call.
    /// </summary>
    public virtual Task<AgentResult> OnBeforeAgentResponseAsync(
        AgentRunContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(AgentResult.Ok());
    }

    /// <summary>
    ///     Called after each model round with the response the model produced and the usage it
    ///     reported (<see cref="AgentResponseUsage" />, <c>null</c> when the provider reports none);
    ///     return the response the agent service should use instead.
    ///     The hook always runs — even when the model call throws or is cancelled. In that
    ///     case <paramref name="error" /> carries the exception, <paramref name="response" />
    ///     holds whatever partial message arrived before the failure, and the run still
    ///     fails after every middleware has run — failing the result breaks it no further.
    /// </summary>
    public virtual Task<AgentResult<AiMessage>> OnAfterAgentResponseAsync(
        AgentRunContext context, AiMessage response, AgentResponseUsage? usage,
        Exception? error = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(AgentResult<AiMessage>.Ok(response));
    }

    /// <summary>
    ///     Called before each tool invocation with the call the model issued; return the
    ///     call the agent service should execute instead.
    /// </summary>
    public virtual Task<AgentResult<AiToolCallContent>> OnBeforeToolCallAsync(
        AgentRunContext context, AiToolCallContent call, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(AgentResult<AiToolCallContent>.Ok(call));
    }

    /// <summary>
    ///     Called after each tool invocation with the result the tool produced; return the
    ///     result the agent service should feed back to the model instead.
    ///     The hook always runs — even when the before hook, the tool itself, or the agent
    ///     service throws. In that case <paramref name="error" /> carries the exception and
    ///     <paramref name="result" /> is <c>null</c>; the base implementation turns the error
    ///     into an error result for the model, so a failing tool (or a stage that failed
    ///     before the tool ran) surfaces to the model instead of failing the run.
    /// </summary>
    public virtual Task<AgentResult<AiToolResultContent?>> OnAfterToolCallAsync(
        AgentRunContext context, AiToolCallContent call, AiToolResultContent? result,
        Exception? error = null, CancellationToken cancellationToken = default)
    {
        if (result is not null) return Task.FromResult(AgentResult<AiToolResultContent?>.Ok(result));
        if (error is null)
            return Task.FromResult(AgentResult<AiToolResultContent?>.Ok(
                new AiToolResultContent(call.CallId, "The tool produced no result.", isError: true)));

        return Task.FromResult(AgentResult<AiToolResultContent?>.Warn(
            new AiToolResultContent(call.CallId, error.Message, isError: true),
            $"The tool stage failed: {error.Message}",
            error));
    }

    /// <summary>
    ///     Called once when the run ends, whether it succeeded, failed, or was cancelled;
    ///     <paramref name="end" /> says which, carrying the exception that failed the run and
    ///     the reason it was cancelled. The run is already over: a failing result cannot
    ///     break it, but its warning (or error) is still recorded.
    /// </summary>
    public virtual Task<AgentResult> OnEndAsync(
        AgentRunContext context, AgentRunEnd end, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(AgentResult.Ok());
    }
}
