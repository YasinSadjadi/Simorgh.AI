using Simorgh.AI.Routing.Abstractions;
using Microsoft.Extensions.AI;

namespace Simorgh.AI.Routing;

/// <summary>Adapts a routing strategy to the standard chat client contract. Clients remain owned by the caller.</summary>
public sealed class RoutingChatClient(IChatRoutingStrategy strategy) : IChatClient
{
    private readonly IChatRoutingStrategy _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _strategy.GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        _strategy.GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}