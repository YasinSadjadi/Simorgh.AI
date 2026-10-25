using Microsoft.Extensions.AI;
using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Agent.Cancellation;
using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Core.Agent;
using Simorgh.AI.Extensions.Cancellation;

namespace Simorgh.AI.Tests;

public class AgentCancellationTests
{
    private static AgentService CreateAgent(IChatClient chatClient, IAgentRunRegistry? registry = null)
    {
        return AgentService.Initialize(chatClient, new NoToolsStrategy(), new AgentServiceOptions { RunRegistry = registry });
    }

    private static AgentRunRequest StreamingRequest()
    {
        return new AgentRunRequest([AiMessage.FromText(AiMessageRole.User, "hello")], stream: true);
    }

    [Fact]
    public async Task CancelAsync_cancels_streaming_run_and_raises_cancelled_update()
    {
        var chatClient = new SlowStreamingChatClient(deltas: 50, deltaDelayMs: 20);
        var registry = new InMemoryAgentRunRegistry();
        var agent = CreateAgent(chatClient, registry);

        var updates = new List<AgentUpdate>();
        var run = agent.Submit(StreamingRequest());
        run.Updated += updates.Add;

        await registry.CancelAsync(run.RunId, "user requested");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => run.Completion);
        Assert.Contains(updates, u => u is AgentRunCancelledUpdate c && c.Reason == "user requested");
        Assert.DoesNotContain(updates, u => u is AgentRunCompletedUpdate);
        Assert.True(chatClient.CancelledWhileStreaming);
        // The run is removed from the registry once it finishes.
        Assert.Empty(registry.ActiveRunIds);
    }

    [Fact]
    public async Task CancelAsync_returns_false_for_unknown_run()
    {
        var registry = new InMemoryAgentRunRegistry();
        Assert.False(await registry.CancelAsync("does-not-exist"));
    }

    [Fact]
    public async Task Registry_tracks_active_runs_and_raises_lifecycle_events()
    {
        var chatClient = new SlowStreamingChatClient(deltas: 50, deltaDelayMs: 20);
        var registry = new InMemoryAgentRunRegistry();
        var agent = CreateAgent(chatClient, registry);

        var registered = new List<string>();
        var removed = new List<string>();
        registry.RunRegistered += registered.Add;
        registry.RunRemoved += removed.Add;

        var run = agent.Submit(StreamingRequest());
        Assert.Contains(run.RunId, registry.ActiveRunIds);
        Assert.Contains(run.RunId, registered);

        await registry.CancelAsync(run.RunId);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => run.Completion);

        Assert.DoesNotContain(run.RunId, registry.ActiveRunIds);
        Assert.Contains(run.RunId, removed);
    }

    [Fact]
    public async Task Broadcaster_publish_reaches_registry_on_same_and_other_server()
    {
        // Two registries (one per "server") sharing one broadcaster stand in for a
        // multi-server deployment: a cancel issued through one registry cancels the
        // run owned by the other.
        var broadcaster = new InMemoryAgentCancellationBroadcaster();
        var serverA = new InMemoryAgentRunRegistry(broadcaster);
        var serverB = new InMemoryAgentRunRegistry(broadcaster);

        var chatClient = new SlowStreamingChatClient(deltas: 50, deltaDelayMs: 20);
        var agentOnA = CreateAgent(chatClient, serverA);
        var run = agentOnA.Submit(StreamingRequest());

        // Server B has never seen the run, so from its perspective the run is not local…
        Assert.False(await serverB.CancelAsync(run.RunId, "stop"));
        // …but the broadcast still cancelled the run executing on server A.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => run.Completion);
        Assert.True(chatClient.CancelledWhileStreaming);
    }

    [Fact]
    public async Task Broadcaster_publish_raises_cancel_requested_event()
    {
        var broadcaster = new InMemoryAgentCancellationBroadcaster();
        string? receivedRunId = null;
        string? receivedReason = null;
        broadcaster.CancelRequested += (runId, reason) => (receivedRunId, receivedReason) = (runId, reason);

        await broadcaster.PublishCancelAsync("run-1", "why");

        Assert.Equal("run-1", receivedRunId);
        Assert.Equal("why", receivedReason);
    }

    [Fact]
    public async Task Caller_token_still_cancels_run()
    {
        var chatClient = new SlowStreamingChatClient(deltas: 50, deltaDelayMs: 20);
        using var cts = new CancellationTokenSource(50);
        var agent = CreateAgent(chatClient);

        var updates = new List<AgentUpdate>();
        var run = agent.Submit(StreamingRequest(), cts.Token);
        run.Updated += updates.Add;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => run.Completion);
        // Cancelling through the caller's token is not an explicit cancel, so no
        // AgentRunCancelledUpdate is raised — behavior is unchanged from before.
        Assert.DoesNotContain(updates, u => u is AgentRunCancelledUpdate);
        Assert.True(chatClient.CancelledWhileStreaming);
    }

    [Fact]
    public async Task WithRunRegistry_returns_copy_that_registers_runs()
    {
        var chatClient = new SlowStreamingChatClient(deltas: 50, deltaDelayMs: 20);
        var registry = new InMemoryAgentRunRegistry();
        var agent = (AgentService)CreateAgent(chatClient).WithRunRegistry(registry);

        var run = agent.Submit(StreamingRequest());
        Assert.Contains(run.RunId, registry.ActiveRunIds);

        await registry.CancelAsync(run.RunId);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => run.Completion);
    }

    [Fact]
    public async Task Caller_supplied_run_id_is_used_as_is()
    {
        var chatClient = new SlowStreamingChatClient(deltas: 50, deltaDelayMs: 20);
        var registry = new InMemoryAgentRunRegistry();
        var agent = CreateAgent(chatClient, registry);

        var request = new AgentRunRequest(
            [AiMessage.FromText(AiMessageRole.User, "hello")], stream: true, runId: "my-custom-run-1");
        var run = agent.Submit(request);
        Assert.Equal("my-custom-run-1", run.RunId);
        Assert.Contains("my-custom-run-1", registry.ActiveRunIds);

        // The run can be cancelled through the caller-managed id.
        await registry.CancelAsync("my-custom-run-1");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => run.Completion);
    }

    [Fact]
    public async Task Run_id_normalizes_to_generated_when_blank()
    {
        var chatClient = new SlowStreamingChatClient(deltas: 2, deltaDelayMs: 5);
        var agent = CreateAgent(chatClient);
        var run = agent.Submit(new AgentRunRequest(
            [AiMessage.FromText(AiMessageRole.User, "hi")], stream: false, runId: "   "));
        Assert.NotEqual("   ", run.RunId);
        Assert.False(string.IsNullOrWhiteSpace(run.RunId));
        await run.Completion;
    }

    [Fact]
    public async Task Registry_rejects_blank_and_duplicate_run_ids()
    {
        var registry = new InMemoryAgentRunRegistry();
        await Assert.ThrowsAsync<ArgumentException>(
            () => registry.CancelAsync("  "));
        Assert.Throws<ArgumentException>(() => registry.Register(" ", _ => { }));

        registry.Register("dup-1", _ => { });
        Assert.Throws<InvalidOperationException>(() => registry.Register("dup-1", _ => { }));
        registry.Register("dup-2", _ => { }); // distinct id is fine
    }

    [Fact]
    public async Task Uncancelled_streaming_run_completes_normally()
    {
        var chatClient = new SlowStreamingChatClient(deltas: 3, deltaDelayMs: 5);
        var registry = new InMemoryAgentRunRegistry();
        var agent = CreateAgent(chatClient, registry);

        var updates = new List<AgentUpdate>();
        var run = agent.Submit(StreamingRequest());
        run.Updated += updates.Add;

        var reply = await run.Completion;
        Assert.NotNull(reply);
        Assert.Contains(updates, u => u is AgentStreamUpdate);
        Assert.Contains(updates, u => u is AgentRunCompletedUpdate);
        Assert.Empty(registry.ActiveRunIds);
        Assert.False(chatClient.CancelledWhileStreaming);
    }
}
