using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Agent.Middleware;
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;

namespace Simorgh.AI.Core.Agent;

/// <summary>
///     Builds the nested middleware chain and exposes entry points for each pipeline point.
///     Middleware execute in registration order on the way in, and reverse order on the way out.
/// </summary>
/// <param name="middlewares">Middleware to wrap the execution chain with, in registration order.</param>
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

    /// <summary>Runs one agent round through the middleware chain into <paramref name="terminal" />.</summary>
    public Task<AiMessage> InvokeResponseAsync(AgentRunContext context, AgentResponseDelegate terminal)
    {
        for (var i = Middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = Middlewares[i];
            var next = terminal;
            terminal = ctx => middleware.InvokeResponseAsync(ctx, next);
        }

        return terminal(context);
    }

    /// <summary>Runs one tool invocation through the middleware chain into <paramref name="terminal" />.</summary>
    public Task<AiToolResultContent> InvokeToolAsync(
        AgentRunContext context, AiToolCallContent call, AgentToolInvokeDelegate terminal)
    {
        var chain = terminal;
        for (var i = Middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = Middlewares[i];
            var next = chain;
            chain = (ctx, c) => middleware.InvokeToolAsync(ctx, c, next);
        }

        return chain(context, call);
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