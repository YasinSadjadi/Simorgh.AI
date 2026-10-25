using Simorgh.AI.Abstractions.Messages;

namespace Simorgh.AI.Abstractions.Agent;

/// <summary>
///     Input to an agent run.
/// </summary>
/// <param name="messages">The conversation so far, including the new user input as the last message.</param>
/// <param name="properties">Free-form options interpreted by the agent implementation.</param>
/// <param name="stream">
///     When <c>true</c>, model responses are streamed and incremental text is reported through
///     <see cref="AgentStreamUpdate" /> updates while the run is in progress.
/// </param>
public sealed class AgentRunRequest(
    IList<AiMessage> messages,
    IReadOnlyDictionary<string, object?>? properties = null,
    bool stream = false)
{
    /// <summary>The conversation so far, including the new user input as the last message.</summary>
    public IList<AiMessage> Messages { get; } = messages;

    /// <summary>Free-form options interpreted by the agent implementation.</summary>
    public IReadOnlyDictionary<string, object?> Properties { get; } =
        properties ?? new Dictionary<string, object?>();

    /// <summary>
    ///     When <c>true</c>, model responses are streamed and incremental text is reported through
    ///     <see cref="AgentStreamUpdate" /> updates while the run is in progress.
    /// </summary>
    public bool Stream { get; } = stream;
}