using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Core.Agent;
using Simorgh.AI.Routing.Extensions;
using Simorgh.AI.Routing.Strategies;
using Simorgh.AI.Routing.Models;

namespace Simorgh.AI.Tests;

public class RoutingTests
{
    private static RoutingTarget Target(IChatClient client) =>
        new(client, name: "test", provider: "provider", model: "model");
    private static readonly ChatMessage[] Input = [new(ChatRole.User, "question")];
    private sealed class Client : IChatClient
    {
        public string Text = "answer";
        public bool Fail;
        public bool FailAfterUpdate;
        public int Calls;
        public ChatMessage[]? Received;
        public ChatOptions? Options;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++; Received = messages.ToArray(); Options = options;
            if (Fail) throw new TimeoutException("failed");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Text)));
        }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield(); cancellationToken.ThrowIfCancellationRequested(); Calls++; Received = messages.ToArray(); Options = options;
            if (Fail) throw new TimeoutException("failed");
            yield return new ChatResponseUpdate { Contents = [new TextContent(Text)] };
            if (FailAfterUpdate) throw new InvalidOperationException("late failure");
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
    [Fact]
    public async Task FallbackTriesNextAndPreservesOptions()
    {
        var first = new Client { Fail = true }; var second = new Client(); var options = new ChatOptions();
        var result = await new FallbackRoutingStrategy([Target(first), Target(second)]).GetResponseAsync(Input, options);
        Assert.Equal("answer", result.Text); Assert.Same(options, second.Options); Assert.Equal(1, first.Calls);
    }
    [Fact]
    public async Task StreamingFallbackStopsAfterFirstUpdate()
    {
        var first = new Client { FailAfterUpdate = true }; var second = new Client();
        var stream = new FallbackRoutingStrategy([Target(first), Target(second)]).GetStreamingResponseAsync(Input);
        await using var e = stream.GetAsyncEnumerator();
        Assert.True(await e.MoveNextAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await e.MoveNextAsync());
        Assert.Equal(0, second.Calls);
        first.Fail = true; first.FailAfterUpdate = false;
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in new FallbackRoutingStrategy([Target(first), Target(second)]).GetStreamingResponseAsync(Input)) updates.Add(update);
        Assert.Single(updates); Assert.Equal(1, second.Calls);
    }
    [Fact]
    public async Task FallbackDoesNotRetryPermanentErrorsUnlessConfigured()
    {
        var first = new ErrorClient(new InvalidOperationException("invalid request"));
        var second = new Client();
        var strategy = new FallbackRoutingStrategy([Target(first), Target(second)]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => strategy.GetResponseAsync(Input));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in strategy.GetStreamingResponseAsync(Input)) { }
        });
        Assert.Equal(0, second.Calls);

        var custom = new FallbackRoutingStrategy([Target(first), Target(second)], _ => true);
        Assert.Equal("answer", (await custom.GetResponseAsync(Input)).Text);
    }

    [Theory]
    [InlineData(401, false)]
    [InlineData(400, false)]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(503, true)]
    public async Task FallbackClassifiesHttpStatus(int status, bool retries)
    {
        var error = new System.Net.Http.HttpRequestException("http error", null, (System.Net.HttpStatusCode)status);
        var second = new Client();
        var strategy = new FallbackRoutingStrategy([Target(new ErrorClient(error)), Target(second)]);
        if (retries)
            Assert.Equal("answer", (await strategy.GetResponseAsync(Input)).Text);
        else
            await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(() => strategy.GetResponseAsync(Input));
        Assert.Equal(retries ? 1 : 0, second.Calls);
    }

    private sealed class ErrorClient(Exception error) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw error;
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw error;
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
    [Fact]
    public async Task WeightedAndValidation()
    {
        Assert.Throws<ArgumentException>(() => new WeightedRoutingStrategy([]));
        var target = Target(new Client());
        Assert.Equal("provider", target.Provider);
        Assert.Equal("model", target.Model);
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeightedTarget(target, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeightedTarget(target, double.NaN));
        Assert.Throws<ArgumentNullException>(() => new WeightedTarget(null!, 1));
        Assert.Throws<ArgumentException>(() => new WeightedRoutingStrategy([null!]));
        var a = new Client(); var b = new Client(); var strategy = new WeightedRoutingStrategy([new WeightedTarget(Target(a), 1), new WeightedTarget(Target(b), 3)]);
        for (var i = 0; i < 1000; i++) await strategy.GetResponseAsync(Input);
        Assert.InRange(a.Calls, 150, 350); Assert.Equal(1000, a.Calls + b.Calls);
    }
#if false
    [Fact]
    public async Task MixtureOnlyStreamsJudgeAndToleratesMemberFailures()
    {
        var good = new Client { Text = "member answer" }; var bad = new Client { Fail = true }; var judge = new Client { Text = "judged" };
        var strategy = new MixtureOfAgentsRoutingStrategy([Target(good), Target(bad)], judge, "combine");
        var result = await strategy.GetResponseAsync(Input);
        Assert.Equal("judged", result.Text); Assert.Contains("member answer", judge.Received!.Last().Text); Assert.Contains("combine", judge.Received!.Last().Text);
        var updates = new List<ChatResponseUpdate>(); await foreach (var update in strategy.GetStreamingResponseAsync(Input)) updates.Add(update);
        Assert.Single(updates); Assert.Equal("judged", ((TextContent)updates[0].Contents[0]).Text);
        await Assert.ThrowsAsync<AggregateException>(() => new MixtureOfAgentsRoutingStrategy([Target(bad)], judge, "combine").GetResponseAsync(Input));
    }
    private sealed class GateClient(TaskCompletionSource<bool> gate, Action started) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            started();
            await gate.Task.WaitAsync(cancellationToken);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer"));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
    [Fact]
    public async Task MixtureStartsMembersBeforeWaitingForEither()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var member = new GateClient(gate, () => Interlocked.Increment(ref started));
        var task = new MixtureOfAgentsRoutingStrategy([Target(member), Target(member)], new Client(), "combine").GetResponseAsync(Input);
        Assert.Equal(2, started);
        gate.SetResult(true);
        await task;
    }
#endif
    [Fact]
    public async Task RoutingClientWorksWithAgentAndCancellationPropagates()
    {
        var client = new Client(); var strategy = new FallbackRoutingStrategy([Target(client)]);
        var agent = AgentService.Initialize(strategy.AsChatClient(), new NoToolsStrategy());
        await agent.RunAsync(new AgentRunRequest([AiMessage.FromText(AiMessageRole.User, "hello")]));
        Assert.Equal(1, client.Calls);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => strategy.GetResponseAsync(Input, cancellationToken: cts.Token));
    }
}
