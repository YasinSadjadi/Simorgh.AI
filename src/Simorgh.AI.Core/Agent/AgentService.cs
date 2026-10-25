using Microsoft.Extensions.AI;
using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Agent.Cancellation;
using Simorgh.AI.Abstractions.Agent.Middleware;
using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Abstractions.Tools;
using Simorgh.AI.Core.Tools;

namespace Simorgh.AI.Core.Agent;

/// <summary>
///     Default <see cref="IAgentService" /> that runs agents against an <see cref="IChatClient" />,
///     with tools, a pluggable tool-calling strategy, and middleware hooks around the run.
///     Submitted runs can be cancelled by id through an <see cref="IAgentRunRegistry" />.
///     Use <see cref="Initialize" /> to create instances without any DI container.
///     Built-in strategies such as instant tool calling are available as extension methods
///     in the <c>Simorgh.AI.Extensions</c> package.
/// </summary>
public sealed class AgentService(
    IChatClient chatClient,
    IToolCallingStrategy toolCallingStrategy,
    ToolRegistry? tools = null,
    IEnumerable<AgentMiddleware>? middlewares = null,
    string? instructions = null,
    IAgentRunRegistry? runRegistry = null)
    : IAgentService
{
    private readonly IChatClient _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    private readonly AgentPipeline _pipeline = new(middlewares);

    private readonly IToolCallingStrategy _toolCallingStrategy =
        toolCallingStrategy ?? throw new ArgumentNullException(nameof(toolCallingStrategy));

    private readonly IAgentRunRegistry? _runRegistry = runRegistry;

    /// <summary>
    ///     Creates an agent service with the given tool-calling strategy.
    ///     No DI container is required.
    /// </summary>
    /// <param name="middlewares">Optional middleware hooks around the run.</param>
    /// <param name="instructions">
    ///     Optional system instructions that shape the agent's behavior; sent as the
    ///     system message at the start of every run.
    /// </param>
    /// <param name="runRegistry">
    ///     Optional registry through which submitted runs can be cancelled by id,
    ///     including from other servers in a multi-server deployment.
    /// </param>
    public static AgentService Initialize(
        IChatClient chatClient,
        IToolCallingStrategy toolCallingStrategy,
        ToolRegistry? tools = null,
        IEnumerable<AgentMiddleware>? middlewares = null,
        string? instructions = null,
        IAgentRunRegistry? runRegistry = null)
    {
        if (chatClient is null) throw new ArgumentNullException(nameof(chatClient));
        return toolCallingStrategy is null
            ? throw new ArgumentNullException(nameof(toolCallingStrategy))
            : new AgentService(chatClient, toolCallingStrategy, tools, middlewares, instructions, runRegistry);
    }

    /// <inheritdoc />
    public IAgentService WithToolCallingStrategy(IToolCallingStrategy strategy)
    {
        return strategy is null
            ? throw new ArgumentNullException(nameof(strategy))
            : new AgentService(_chatClient, strategy, tools, _pipeline.Middlewares, instructions, _runRegistry);
    }

    /// <inheritdoc />
    public IAgentService WithInstructions(string? instructions)
    {
        return new AgentService(_chatClient, _toolCallingStrategy, tools, _pipeline.Middlewares, instructions, _runRegistry);
    }

    /// <inheritdoc />
    public IAgentService WithRunRegistry(IAgentRunRegistry runRegistry)
    {
        return runRegistry is null
            ? throw new ArgumentNullException(nameof(runRegistry))
            : new AgentService(_chatClient, _toolCallingStrategy, tools, _pipeline.Middlewares, instructions, runRegistry);
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
        return request is null
            ? throw new ArgumentNullException(nameof(request))
            : AgentRun.Start(
                _chatClient, tools, _toolCallingStrategy, _pipeline, instructions, request, cancellationToken,
                _runRegistry);
    }
}
