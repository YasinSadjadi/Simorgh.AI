using Microsoft.Extensions.AI;
using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Core.Agent;

namespace Simorgh.AI.Tests;

public class UpdateSubscriptionTests
{
    /// <summary>
    ///     A non-streaming run against a client that answers synchronously can finish
    ///     before <c>Submit</c> even returns; subscribing through the callback must still
    ///     capture every update, including the started and completed ones.
    /// </summary>
    [Fact]
    public async Task Subscribe_callback_receives_updates_of_instantly_completing_run()
    {
        var chatClient = new SlowStreamingChatClient(deltas: 1, deltaDelayMs: 0);
        var agent = AgentService.Initialize(chatClient, new NoToolsStrategy());

        var updates = new List<AgentUpdate>();
        var request = new AgentRunRequest([AiMessage.FromText(AiMessageRole.User, "hi")], stream: false);
        var run = agent.Submit(request, subscribe: r => r.Updated += updates.Add);

        await run.Completion;
        Assert.Contains(updates, u => u is AgentRunStartedUpdate);
        Assert.Contains(updates, u => u is AgentRunCompletedUpdate);
    }
}
