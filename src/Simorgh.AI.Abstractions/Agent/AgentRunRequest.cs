using Simorgh.AI.Abstractions.Messages;

namespace Simorgh.AI.Abstractions.Agent;

/// <summary>
///     Input to an agent run.
/// </summary>
/// <param name="messages">The conversation so far, including the new user input as the last message.</param>
/// <param name="properties">Free-form options interpreted by the agent implementation.</param>
public sealed class AgentRunRequest(
    IList<AiMessage> messages,
    IReadOnlyDictionary<string, object?>? properties = null)
{
    /// <summary>The conversation so far, including the new user input as the last message.</summary>
    public IList<AiMessage> Messages { get; } = messages;

    /// <summary>Free-form options interpreted by the agent implementation.</summary>
    public IReadOnlyDictionary<string, object?> Properties { get; } =
        properties ?? new Dictionary<string, object?>();
}