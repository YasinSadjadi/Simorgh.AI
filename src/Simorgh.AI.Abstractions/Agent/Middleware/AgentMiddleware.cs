using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;

namespace Simorgh.AI.Abstractions.Agent.Middleware;

/// <summary>Executes a round of the agent (the model call) and returns its response.</summary>
public delegate Task<AiMessage> AgentResponseDelegate(AgentRunContext context);

/// <summary>Executes a tool invocation and returns its result.</summary>
public delegate Task<AiToolResultContent> AgentToolInvokeDelegate(
    AgentRunContext context, AiToolCallContent call);

/// <summary>
///     Extends agent execution by wrapping the call chain. Each middleware receives the next
///     stage in <see cref="InvokeResponseAsync" />/<see cref="InvokeToolAsync" /> and decides
///     whether to adjust the input, call it, short-circuit it, or adjust its output.
///     Middleware execute in registration order on the way in, and reverse order on the way out.
/// </summary>
public abstract class AgentMiddleware : IDisposable
{
    public virtual void Dispose()
    {
    }

    /// <summary>Called once when the run starts, before anything else.</summary>
    public virtual Task OnStartupAsync(
        AgentRunContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>Called before each round; may adjust <see cref="AgentRunContext.Messages" />.</summary>
    protected virtual Task BeforeResponseAsync(
        AgentRunContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>Called after each round; return the response to pass on, replaced if desired.</summary>
    protected virtual Task<AiMessage> AfterResponseAsync(
        AgentRunContext context, AiMessage response, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(response);
    }

    /// <summary>Called before a tool invocation; return the call that should run.</summary>
    protected virtual Task<AiToolCallContent> BeforeToolCallAsync(
        AgentRunContext context, AiToolCallContent call, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(call);
    }

    /// <summary>Called after a tool invocation; return the result to feed back to the model.</summary>
    protected virtual Task<AiToolResultContent> AfterToolCallAsync(
        AgentRunContext context, AiToolCallContent call, AiToolResultContent result,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(result);
    }

    /// <summary>
    ///     Wraps one round of the agent. The default implementation runs
    ///     <see cref="BeforeResponseAsync" />, then <paramref name="next" />, then
    ///     <see cref="AfterResponseAsync" />. Override it to take full control of the chain
    ///     (modify <paramref name="context" />, short-circuit by skipping <paramref name="next" />,
    ///     call it more than once) while still delegating to the hooks if desired.
    /// </summary>
    public virtual async Task<AiMessage> InvokeResponseAsync(
        AgentRunContext context, AgentResponseDelegate next)
    {
        await BeforeResponseAsync(context, context.CancellationToken).ConfigureAwait(false);
        var response = await next(context).ConfigureAwait(false);
        return await AfterResponseAsync(context, response, context.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Wraps one tool invocation. The default implementation runs
    ///     <see cref="BeforeToolCallAsync" />, then <paramref name="next" />, then
    ///     <see cref="AfterToolCallAsync" />. Override it to replace the <paramref name="call" />,
    ///     skip <paramref name="next" /> by producing a result themselves, or call it more than
    ///     once, while still delegating to the hooks if desired.
    /// </summary>
    public virtual async Task<AiToolResultContent> InvokeToolAsync(
        AgentRunContext context, AiToolCallContent call, AgentToolInvokeDelegate next)
    {
        var effectiveCall = await BeforeToolCallAsync(context, call, context.CancellationToken)
            .ConfigureAwait(false);
        var result = await next(context, effectiveCall).ConfigureAwait(false);
        return await AfterToolCallAsync(context, effectiveCall, result, context.CancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Called once when the run ends, whether it succeeded, failed, or was cancelled.</summary>
    public virtual Task OnEndAsync(
        AgentRunContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}