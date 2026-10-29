using Microsoft.Extensions.AI;

namespace Simorgh.AI.Routing.Abstractions;

/// <summary>A routing policy controls both regular and streaming requests.</summary>
public interface IChatRoutingStrategy
{
    Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default);
}