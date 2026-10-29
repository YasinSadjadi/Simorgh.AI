using Microsoft.Extensions.AI;

namespace Simorgh.AI.Routing.Models;

/// <summary>A chat client and its optional identifying information.</summary>
public sealed class RoutingTarget
{
    public RoutingTarget(IChatClient client, string? name = null, string? provider = null, string? model = null)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        Name = name;
        Provider = provider;
        Model = model;
    }

    public IChatClient Client { get; }
    public string? Name { get; }
    public string? Provider { get; }
    public string? Model { get; }
}
