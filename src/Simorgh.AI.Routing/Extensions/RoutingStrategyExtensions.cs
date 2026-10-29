using Microsoft.Extensions.AI;
using Simorgh.AI.Routing.Abstractions;

namespace Simorgh.AI.Routing.Extensions;

/// <summary>Adapts routing strategies to the standard chat client interface.</summary>
public static class RoutingStrategyExtensions
{
    /// <summary>Creates a chat client that delegates requests to the supplied strategy.</summary>
    public static IChatClient AsChatClient(this IChatRoutingStrategy strategy)
    {
        return new RoutingChatClient(strategy);
    }
}
