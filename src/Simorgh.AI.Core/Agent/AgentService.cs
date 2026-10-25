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
    AgentServiceOptions? options = null)
    : IAgentService
{
    private readonly IChatClient _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    private readonly AgentServiceOptions _options = options ?? new AgentServiceOptions();

    private readonly AgentPipeline _pipeline = new((options ?? new AgentServiceOptions()).Middlewares);

    private readonly IToolCallingStrategy _toolCallingStrategy =
        toolCallingStrategy ?? throw new ArgumentNullException(nameof(toolCallingStrategy));

    /// <summary>
    ///     Creates an agent service with the given tool-calling strategy.
    ///     No DI container is required.
    /// </summary>
    public static AgentService Initialize(
        IChatClient chatClient,
        IToolCallingStrategy toolCallingStrategy,
        AgentServiceOptions? options = null)
    {
        if (chatClient is null) throw new ArgumentNullException(nameof(chatClient));
        if (toolCallingStrategy is null) throw new ArgumentNullException(nameof(toolCallingStrategy));
        return new AgentService(chatClient, toolCallingStrategy, options);
    }

    /// <inheritdoc />
    public IAgentService WithToolCallingStrategy(IToolCallingStrategy strategy)
    {
        return strategy is null
            ? throw new ArgumentNullException(nameof(strategy))
            : new AgentService(_chatClient, strategy, _options);
    }

    /// <inheritdoc />
    public IAgentService WithInstructions(string? instructions)
    {
        return new AgentService(
            _chatClient, _toolCallingStrategy, new AgentServiceOptions
            {
                Tools = _options.Tools,
                Middlewares = _pipeline.Middlewares,
                Instructions = instructions,
                RunRegistry = _options.RunRegistry,
                ContextWindowTokens = _options.ContextWindowTokens
            });
    }

    /// <inheritdoc />
    public IAgentService WithRunRegistry(IAgentRunRegistry runRegistry)
    {
        return runRegistry is null
            ? throw new ArgumentNullException(nameof(runRegistry))
            : new AgentService(
                _chatClient, _toolCallingStrategy, new AgentServiceOptions
                {
                    Tools = _options.Tools,
                    Middlewares = _pipeline.Middlewares,
                    Instructions = _options.Instructions,
                    RunRegistry = runRegistry,
                    ContextWindowTokens = _options.ContextWindowTokens
                });
    }

    /// <inheritdoc />
    public async Task<AiResponse> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
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
    public IAgentRun Submit(
        AgentRunRequest request,
        CancellationToken cancellationToken = default,
        Action<IAgentRun>? subscribe = null)
    {
        return request is null
            ? throw new ArgumentNullException(nameof(request))
            : AgentRun.Start(
                _chatClient, _options.Tools, _toolCallingStrategy, _pipeline, _options.Instructions, request,
                cancellationToken, _options.RunRegistry, subscribe, _options.ContextWindowTokens);
    }
}
