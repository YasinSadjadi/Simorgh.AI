namespace Simorgh.AI.Abstractions.Agent;

/// <summary>
///     An incremental piece of the agent's response text, emitted while the model is still
///     generating. Only raised for runs submitted with streaming enabled
///     (<see cref="AgentRunRequest.Stream" />).
/// </summary>
/// <param name="runId">Id of the run this update belongs to.</param>
/// <param name="textDelta">The text produced since the previous update.</param>
public sealed class AgentStreamUpdate(string runId, string textDelta) : AgentUpdate(runId)
{
    /// <summary>The text produced since the previous update.</summary>
    public string TextDelta { get; } = textDelta;
}
