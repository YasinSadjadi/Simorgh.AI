using Microsoft.Extensions.AI;
using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Core.Agent;

namespace Simorgh.AI.Tests;

/// <summary>
///     Non-streaming chat client that reports usage, a finish reason, and captures the
/// <see cref="ChatOptions" /> it was called with.
/// </summary>
public sealed class ReportingChatClient(string? defaultModelId = null) : IChatClient
{
    public const long ReportedInputTokens = 100;
    public const long ReportedOutputTokens = 42;
    public const string ReportedFinishReason = "stop";

    public ChatOptions? LastOptions { get; private set; }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return defaultModelId is null
            ? null
            : serviceType == typeof(ChatClientMetadata)
                ? new ChatClientMetadata("test-provider", defaultModelId: defaultModelId)
                : null;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        LastOptions = options;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"))
        {
            Usage = new UsageDetails
            {
                InputTokenCount = ReportedInputTokens,
                OutputTokenCount = ReportedOutputTokens,
                CachedInputTokenCount = 10
            },
            FinishReason = ChatFinishReason.Stop
        });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        LastOptions = options;
        yield return new ChatResponseUpdate { Contents = [new TextContent("hi")] };
        yield return new ChatResponseUpdate
        {
            Contents = [new UsageContent
            {
                Details = new UsageDetails { InputTokenCount = ReportedInputTokens, OutputTokenCount = ReportedOutputTokens }
            }],
            FinishReason = new ChatFinishReason("length")
        };
    }

    public void Dispose()
    {
    }
}

public class ResponseMetadataTests
{
    [Fact]
    public async Task Non_streaming_response_reports_usage_to_after_response_hook()
    {
        var middleware = new RecordingUsageMiddleware();
        var agent = AgentService.Initialize(
            new ReportingChatClient(), new NoToolsStrategy(), new AgentServiceOptions { Middlewares = [middleware] });

        var run = agent.Submit(
            new AgentRunRequest([AiMessage.FromText(AiMessageRole.User, "hello")], stream: false));
        var reply = await run.Completion;

        var usage = Assert.Single(middleware.Usages);
        Assert.Equal(ReportingChatClient.ReportedInputTokens, usage!.InputTokens);
        Assert.Equal(ReportingChatClient.ReportedOutputTokens, usage.OutputTokens);
        Assert.Equal(10, usage.CachedInputTokens);
        Assert.Equal(ReportingChatClient.ReportedInputTokens, reply.Usage!.InputTokens);
    }

    [Fact]
    public async Task Non_streaming_finish_reason_is_preserved_on_the_reply()
    {
        var agent = AgentService.Initialize(new ReportingChatClient(), new NoToolsStrategy());
        var run = agent.Submit(
            new AgentRunRequest([AiMessage.FromText(AiMessageRole.User, "hello")], stream: false));
        var reply = await run.Completion;
        Assert.Equal(ReportingChatClient.ReportedFinishReason, reply.FinishReason);
    }

    [Fact]
    public async Task Streaming_usage_and_finish_reason_are_lifted_out_of_contents()
    {
        var middleware = new RecordingUsageMiddleware();
        var agent = AgentService.Initialize(
            new ReportingChatClient(), new NoToolsStrategy(), new AgentServiceOptions { Middlewares = [middleware] });

        var run = agent.Submit(
            new AgentRunRequest([AiMessage.FromText(AiMessageRole.User, "hello")], stream: true));
        var reply = await run.Completion;

        Assert.Equal("length", reply.FinishReason);
        Assert.Equal(ReportingChatClient.ReportedOutputTokens, reply.Usage!.OutputTokens);
        // Usage is response metadata, never a content part of the message.
        Assert.Empty(reply.Message.Contents.OfType<AiErrorContent>());
        Assert.DoesNotContain(reply.Message.Contents, c => c is AiTextContent t && string.IsNullOrEmpty(t.Text));
        Assert.Equal(ReportingChatClient.ReportedInputTokens, Assert.Single(middleware.Usages)!.InputTokens);
    }

    [Fact]
    public async Task Reply_with_usage_and_finish_reason_maps_back_to_provider_input()
    {
        // A reply carrying usage/finish reason goes into the next round's history; mapping
        // it back to provider input must not throw (usage is no longer a content type).
        var agent = AgentService.Initialize(new ReportingChatClient(), new NoToolsStrategy());
        var run = agent.Submit(
            new AgentRunRequest([AiMessage.FromText(AiMessageRole.User, "hello")], stream: false));
        var reply = await run.Completion;

        var mapped = AiMessageMapper.ToChatMessage(reply.Message);
        Assert.All(mapped.Contents, c => Assert.NotNull(c));
    }

    [Fact]
    public async Task MaxOutputTokens_reaches_the_model_call()
    {
        var chatClient = new ReportingChatClient();
        var agent = AgentService.Initialize(chatClient, new NoToolsStrategy());
        var run = agent.Submit(new AgentRunRequest(
            [AiMessage.FromText(AiMessageRole.User, "hello")], stream: false, maxOutputTokens: 512));
        await run.Completion;
        Assert.Equal(512, chatClient.LastOptions!.MaxOutputTokens);
    }

    [Fact]
    public async Task MaxOutputTokens_absent_leaves_options_null()
    {
        var chatClient = new ReportingChatClient();
        var agent = AgentService.Initialize(chatClient, new NoToolsStrategy());
        var run = agent.Submit(
            new AgentRunRequest([AiMessage.FromText(AiMessageRole.User, "hello")], stream: false));
        await run.Completion;
        Assert.Null(chatClient.LastOptions);
    }
}
