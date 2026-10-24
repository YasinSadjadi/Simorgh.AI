namespace Simorgh.AI.Abstractions.Agent;

/// <summary>
///     Thrown when an agent run fails.
/// </summary>
public sealed class AgentRunException(string message, Exception? innerException = null)
    : Exception(message, innerException);