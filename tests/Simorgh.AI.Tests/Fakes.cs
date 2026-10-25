using Microsoft.Extensions.AI;
using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Abstractions.Tools;

namespace Simorgh.AI.Tests;

/// <summary>
///     Tool-calling strategy that never advertises tools and never produces tool rounds,
///     so the agent loop finishes after a single model response.
/// </summary>
public sealed class NoToolsStrategy : IToolCallingStrategy
{
    public int MaxToolRounds => 1;

    public Task<IReadOnlyList<AiToolDeclaration>> GetToolDeclarationsAsync(
        IReadOnlyList<AiToolDeclaration> availableTools,
        AgentRunContext context,
        int round,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<AiToolDeclaration>>(Array.Empty<AiToolDeclaration>());
    }

    public Task<IReadOnlyList<AiMessage>> InvokeToolsAsync(
        AgentRunContext context,
        IReadOnlyList<AiToolCallContent> calls,
        Func<AiToolCallContent, CancellationToken, Task<AiToolResultContent>> invokeNext,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<AiMessage>>(Array.Empty<AiMessage>());
    }
}

/// <summary>
///     Streaming chat client that emits one text delta per tick, honoring cancellation,
///     so a run can be cancelled mid-stream. The cancellation is observable through
///     <see cref="CancelledWhileStreaming" />.
/// </summary>
public sealed class SlowStreamingChatClient(int deltas = 10, int deltaDelayMs = 20) : IChatClient
{
    public bool CancelledWhileStreaming { get; private set; }
    public int DeltasEmitted { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < deltas; i++)
        {
            // The only cancellation-aware await: this is where a cancelled run observes the token.
            try
            {
                await Task.Delay(deltaDelayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                CancelledWhileStreaming = true;
                throw;
            }

            DeltasEmitted++;
            yield return new ChatResponseUpdate
            {
                Contents = [new TextContent($"delta-{i}")]
            };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    public void Dispose()
    {
    }
}
