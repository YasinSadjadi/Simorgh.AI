using Simorgh.AI.Abstractions.Messages;

namespace Simorgh.AI.Abstractions.Agent;

/// <summary>
///     A progress notification emitted during an agent run.
/// </summary>
/// <param name="runId">Id of the run this update belongs to.</param>
/// <param name="timestampUtc">When the update was produced.</param>
public abstract class AgentUpdate(string runId, DateTimeOffset? timestampUtc = null)
{
    /// <summary>Id of the run this update belongs to.</summary>
    public string RunId { get; } = runId;

    /// <summary>When the update was produced.</summary>
    public DateTimeOffset TimestampUtc { get; } = timestampUtc ?? DateTimeOffset.UtcNow;
}

/// <summary>The run has started.</summary>
public sealed class AgentRunStartedUpdate(string runId) : AgentUpdate(runId);

/// <summary>
///     An intermediate message produced during the run (e.g. a tool call/result round-trip),
///     not the final answer.
/// </summary>
/// <param name="message">The intermediate message.</param>
public sealed class AgentMessageUpdate(string runId, AiMessage message) : AgentUpdate(runId)
{
    /// <summary>The intermediate message.</summary>
    public AiMessage Message { get; } = message;
}

/// <summary>The run finished successfully; <see cref="Message" /> is the agent's final answer.</summary>
/// <param name="message">The agent's final answer.</param>
public sealed class AgentRunCompletedUpdate(string runId, AiMessage message) : AgentUpdate(runId)
{
    /// <summary>The agent's final answer.</summary>
    public AiMessage Message { get; } = message;
}

/// <summary>The run failed.</summary>
/// <param name="error">Description of what went wrong.</param>
public sealed class AgentRunFailedUpdate(string runId, string error) : AgentUpdate(runId)
{
    /// <summary>Description of what went wrong.</summary>
    public string Error { get; } = error;
}