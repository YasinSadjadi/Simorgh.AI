using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Agent.Middleware;
using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Core.Agent;

namespace Simorgh.AI.Tests;

/// <summary>Sets the context window from OnStartupAsync and records what later hooks saw.</summary>
public sealed class WindowMiddleware(long windowTokens) : AgentMiddleware
{
    public AgentRunContext? LastContext { get; private set; }

    public override Task<AgentResult> OnStartupAsync(
        AgentRunContext context, CancellationToken cancellationToken = default)
    {
        if (windowTokens > 0)
            context.ContextWindowTokens = windowTokens;
        return Task.FromResult(AgentResult.Ok());
    }

    public override Task<AgentResult<AiMessage>> OnAfterAgentResponseAsync(
        AgentRunContext context, AiMessage response, AgentResponseUsage? usage,
        Exception? error = null, CancellationToken cancellationToken = default)
    {
        LastContext = context;
        return Task.FromResult(AgentResult<AiMessage>.Ok(response));
    }
}

public class ContextTrackingTests
{
    [Fact]
    public async Task Explicit_window_from_Initialize_reaches_the_run_context()
    {
        var middleware = new WindowMiddleware(windowTokens: 1000); // also overrides to the same value
        var agent = AgentService.Initialize(
            new ReportingChatClient(), new NoToolsStrategy(),
            new AgentServiceOptions { Middlewares = [middleware], ContextWindowTokens = 1000 });

        var run = agent.Submit(
            new AgentRunRequest([AiMessage.FromText(AiMessageRole.User, "hello")], stream: false));
        await run.Completion;

        var context = middleware.LastContext!;
        Assert.Equal(ReportingChatClient.ReportedInputTokens, context.ContextTokensUsed);
        Assert.Equal(10.0, context.ContextUsagePercent);
    }

    [Fact]
    public async Task Window_is_null_when_not_supplied()
    {
        var middleware = new WindowMiddleware(windowTokens: 0); // does not set anything
        var agent = AgentService.Initialize(
            new ReportingChatClient(defaultModelId: "gpt-4o"), new NoToolsStrategy(),
            new AgentServiceOptions { Middlewares = [middleware] });

        var run = agent.Submit(
            new AgentRunRequest([AiMessage.FromText(AiMessageRole.User, "hello")], stream: false));
        await run.Completion;

        var context = middleware.LastContext!;
        Assert.Null(context.ContextWindowTokens);
        Assert.Null(context.ContextUsagePercent);
        Assert.Equal(ReportingChatClient.ReportedInputTokens, context.ContextTokensUsed);
    }

    [Fact]
    public async Task Percent_is_null_without_a_window()
    {
        var middleware = new WindowMiddleware(windowTokens: 0); // 0 → treated as unknown
        var agent = AgentService.Initialize(
            new ReportingChatClient(), new NoToolsStrategy(), new AgentServiceOptions { Middlewares = [middleware] });

        var run = agent.Submit(
            new AgentRunRequest([AiMessage.FromText(AiMessageRole.User, "hello")], stream: false));
        await run.Completion;

        var context = middleware.LastContext!;
        Assert.Equal(ReportingChatClient.ReportedInputTokens, context.ContextTokensUsed);
        Assert.Null(context.ContextUsagePercent);
    }

    [Fact]
    public async Task Tokens_are_unknown_before_the_first_response()
    {
        var context = new AgentRunContext("run-1", []);
        Assert.Null(context.ContextTokensUsed);
        Assert.Null(context.ContextUsagePercent);
    }
}
