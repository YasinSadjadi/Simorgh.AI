using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Agent.Middleware;
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;

namespace Simorgh.AI.Core.Agent;

/// <summary>
///     Carries a middleware's failing result out of the chain so the run breaks and ends
///     in the failed state; the middleware's own error is the inner exception.
/// </summary>
internal sealed class MiddlewareFailedException(Exception error) : Exception(error.Message, error)
{
}

/// <summary>
///     Runs the registered middleware in registration order, threading each stage's output
///     through them so the agent service consumes the final value. Middleware hooks return
///     <see cref="AgentResult" />s: a warning is recorded on the run context (and reaches
///     run consumers as a warning update) while the chain continues, and a failing result
///     breaks the run and moves it to the end state. A stray exception that escapes a hook
///     anyway is treated as a warning, so the chain and the run carry on. A cancelled run
///     token never aborts a chain on its own: each middleware receives the token and
///     decides for itself.
/// </summary>
/// <param name="middlewares">Middleware to execute, in registration order.</param>
public sealed class AgentPipeline(IEnumerable<AgentMiddleware>? middlewares)
{
    /// <summary>Middleware to execute, in registration order.</summary>
    public readonly IReadOnlyList<AgentMiddleware> Middlewares = middlewares?.ToArray() ?? [];

    /// <summary>Runs <see cref="AgentMiddleware.OnStartupAsync" /> on every middleware, in registration order.</summary>
    public async Task OnStartupAsync(AgentRunContext context, CancellationToken cancellationToken)
    {
        foreach (var middleware in Middlewares)
        {
            try
            {
                Apply(context, middleware,
                    await middleware.OnStartupAsync(context, cancellationToken).ConfigureAwait(false));
            }
            catch (MiddlewareFailedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ReportWarning(context, middleware, ex);
            }
        }
    }

    /// <summary>
    ///     Runs <see cref="AgentMiddleware.OnEndAsync" /> on every middleware, in registration
    ///     order, with the reason the run ended. The run is already over, so even a failing
    ///     result cannot break anything here — it is recorded as a warning like everything else.
    /// </summary>
    public async Task OnEndAsync(
        AgentRunContext context, AgentRunEnd end, CancellationToken cancellationToken)
    {
        foreach (var middleware in Middlewares)
        {
            try
            {
                var result = await middleware.OnEndAsync(context, end, cancellationToken).ConfigureAwait(false);
                ReportWarning(context, middleware, result);
            }
            catch (Exception ex)
            {
                ReportWarning(context, middleware, ex);
            }
        }
    }

    /// <summary>
    ///     Runs <see cref="AgentMiddleware.OnBeforeAgentResponseAsync" /> on every middleware,
    ///     in registration order, before the model call.
    /// </summary>
    public async Task OnBeforeAgentResponseAsync(AgentRunContext context, CancellationToken cancellationToken)
    {
        foreach (var middleware in Middlewares)
        {
            try
            {
                Apply(context, middleware,
                    await middleware.OnBeforeAgentResponseAsync(context, cancellationToken).ConfigureAwait(false));
            }
            catch (MiddlewareFailedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ReportWarning(context, middleware, ex);
            }
        }
    }

    /// <summary>
    ///     Passes the model response, its usage, and any error that failed the call through
    ///     <see cref="AgentMiddleware.OnAfterAgentResponseAsync" /> on every middleware, in
    ///     registration order, and returns the final response.
    /// </summary>
    public async Task<AiMessage> OnAfterAgentResponseAsync(
        AgentRunContext context, AiMessage response, AgentResponseUsage? usage,
        Exception? error, CancellationToken cancellationToken)
    {
        foreach (var middleware in Middlewares)
        {
            try
            {
                var result = await middleware
                    .OnAfterAgentResponseAsync(context, response, usage, error, cancellationToken)
                    .ConfigureAwait(false);
                Apply(context, middleware, result);
                response = result.Value!;
            }
            catch (MiddlewareFailedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ReportWarning(context, middleware, ex);
            }
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
            try
            {
                var result = await middleware.OnBeforeToolCallAsync(context, call, cancellationToken)
                    .ConfigureAwait(false);
                Apply(context, middleware, result);
                call = result.Value!;
            }
            catch (MiddlewareFailedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ReportWarning(context, middleware, ex);
            }
        }

        return call;
    }

    /// <summary>
    ///     Passes the tool result — or the exception that prevented one — through
    ///     <see cref="AgentMiddleware.OnAfterToolCallAsync" /> on every middleware, in
    ///     registration order, and returns the final result.
    /// </summary>
    public async Task<AiToolResultContent?> OnAfterToolCallAsync(
        AgentRunContext context, AiToolCallContent call, AiToolResultContent? result,
        Exception? error, CancellationToken cancellationToken)
    {
        foreach (var middleware in Middlewares)
        {
            try
            {
                var result2 = await middleware
                    .OnAfterToolCallAsync(context, call, result, error, cancellationToken)
                    .ConfigureAwait(false);
                Apply(context, middleware, result2);
                result = result2.Value;
            }
            catch (MiddlewareFailedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ReportWarning(context, middleware, ex);
            }
        }

        return result;
    }

    /// <summary>
    ///     Applies a hook's result: a failure breaks the run (the exception carries the
    ///     middleware's own error), and a warning is recorded on the run context so it
    ///     reaches run consumers while the chain continues.
    /// </summary>
    private static void Apply(AgentRunContext context, AgentMiddleware middleware, AgentResult result)
    {
        if (result.Error is not null)
            throw new MiddlewareFailedException(result.Error);

        if (result.HasWarning)
            context.ReportWarning(middleware.GetType().Name, result.WarningMessage!, result.WarningDetail);
    }

    /// <summary>
    ///     Records a hook's end result — and anything else that went wrong while ending the
    ///     run — as a warning; there is nothing left to break at this point.
    /// </summary>
    private static void ReportWarning(AgentRunContext context, AgentMiddleware middleware, AgentResult result)
    {
        if (result.Error is not null)
        {
            context.ReportWarning(middleware.GetType().Name, result.Error.Message, result.Error);
            return;
        }

        if (result.HasWarning)
            context.ReportWarning(middleware.GetType().Name, result.WarningMessage!, result.WarningDetail);
    }

    private static void ReportWarning(
        AgentRunContext context, AgentMiddleware middleware, Exception exception)
    {
        context.ReportWarning(middleware.GetType().Name, exception.Message, exception);
    }
}
