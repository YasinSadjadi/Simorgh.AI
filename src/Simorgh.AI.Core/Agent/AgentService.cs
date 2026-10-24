using Microsoft.Extensions.AI;
using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Agent.Middleware;
using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Abstractions.Tools;
using Simorgh.AI.Core.Tools;

namespace Simorgh.AI.Core.Agent;

/// <summary>
///     Default <see cref="IAgentService" /> that runs agents against an <see cref="IChatClient" />,
///     with tools, a pluggable tool-calling strategy, and middleware hooks around the run.
///     Use <see cref="Initialize" /> to create instances without any DI container.
///     Built-in strategies such as instant tool calling are available as extension methods
///     in the <c>Simorgh.AI.Extensions</c> package.
/// </summary>
public sealed class AgentService(
    IChatClient chatClient,
    IToolCallingStrategy toolCallingStrategy,
    ToolRegistry? tools = null,
    IEnumerable<AgentMiddleware>? middlewares = null)
    : IAgentService
{
    private readonly IChatClient _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    private readonly AgentPipeline _pipeline = new(middlewares);

    private readonly IToolCallingStrategy _toolCallingStrategy =
        toolCallingStrategy ?? throw new ArgumentNullException(nameof(toolCallingStrategy));

    /// <summary>
    ///     Creates an agent service with the given tool-calling strategy.
    ///     No DI container is required.
    /// </summary>
    public static AgentService Initialize(
        IChatClient chatClient,
        IToolCallingStrategy toolCallingStrategy,
        ToolRegistry? tools = null,
        IEnumerable<AgentMiddleware>? middlewares = null)
    {
        if (chatClient is null) throw new ArgumentNullException(nameof(chatClient));
        if (toolCallingStrategy is null) throw new ArgumentNullException(nameof(toolCallingStrategy));
        return new AgentService(chatClient, toolCallingStrategy, tools, middlewares);
    }

    /// <inheritdoc />
    public IAgentService WithToolCallingStrategy(IToolCallingStrategy strategy)
    {
        if (strategy is null) throw new ArgumentNullException(nameof(strategy));
        return new AgentService(_chatClient, strategy, tools, _pipeline.Middlewares);
    }

    /// <inheritdoc />
    public async Task<AiMessage> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
    {
        var run = Submit(request, cancellationToken);
        try
        {
            return await run.Completion.ConfigureAwait(false);
        }
        catch (AgentRunException ex)
        {
            throw new AgentRunException(ex.Message, ex.InnerException);
        }
    }

    /// <inheritdoc />
    public IAgentRun Submit(AgentRunRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return AgentRun.Start(_chatClient, tools, _toolCallingStrategy, _pipeline, request, cancellationToken);
    }
}
