using Microsoft.Extensions.AI;
using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Agent.Middleware;
using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Core.Agent;

namespace Simorgh.AI.Tests;

/// <summary>
///     Streaming chat client that emits some content (including a usage report) and then
///     throws, simulating a provider failing or being cancelled mid-stream after usage
///     was already reported.
/// </summary>
public sealed class FailingAfterUsageChatClient : IChatClient
{
    public const long ReportedInputTokens = 11;
    public const long ReportedOutputTokens = 7;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("model call failed");
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new ChatResponseUpdate
        {
            Contents = [new TextContent("partial ")]
        };
        yield return new ChatResponseUpdate
        {
            Contents =
            [
                new UsageContent
                {
                    Details = new UsageDetails
                    {
                        InputTokenCount = ReportedInputTokens,
                        OutputTokenCount = ReportedOutputTokens,
                        CachedInputTokenCount = 4
                    }
                }
            ]
        };
        throw new OperationCanceledException(cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public void Dispose()
    {
    }
}

/// <summary>Middleware that records every after-response invocation.</summary>
public sealed class RecordingUsageMiddleware : AgentMiddleware
{
    public List<AgentResponseUsage?> Usages { get; } = [];

    public override Task<AiMessage> OnAfterAgentResponseAsync(
        AgentRunContext context, AiMessage response, AgentResponseUsage? usage,
        CancellationToken cancellationToken = default)
    {
        Usages.Add(usage);
        return Task.FromResult(response);
    }
}

public class AgentUsageHookTests
{
    [Fact]
    public async Task After_response_hook_runs_with_usage_when_stream_is_cancelled_after_usage()
    {
        var middleware = new RecordingUsageMiddleware();
        var agent = AgentService.Initialize(
            new FailingAfterUsageChatClient(), new NoToolsStrategy(), new AgentServiceOptions { Middlewares = [middleware] });

        var run = agent.Submit(
            new AgentRunRequest([AiMessage.FromText(AiMessageRole.User, "hello")], stream: true));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.Completion);

        var usage = Assert.Single(middleware.Usages);
        Assert.Equal(FailingAfterUsageChatClient.ReportedInputTokens, usage!.InputTokens);
        Assert.Equal(FailingAfterUsageChatClient.ReportedOutputTokens, usage.OutputTokens);
        Assert.Equal(4, usage.CachedInputTokens);
    }

    [Fact]
    public async Task After_response_hook_runs_when_model_call_throws()
    {
        var middleware = new RecordingUsageMiddleware();
        var agent = AgentService.Initialize(
            new FailingAfterUsageChatClient(), new NoToolsStrategy(), new AgentServiceOptions { Middlewares = [middleware] });

        var run = agent.Submit(
            new AgentRunRequest([AiMessage.FromText(AiMessageRole.User, "hello")], stream: false));
        await Assert.ThrowsAsync<AgentRunException>(() => run.Completion);

        // The hook ran even though no response was ever produced; no usage was reported either.
        Assert.Null(Assert.Single(middleware.Usages));
    }
}
