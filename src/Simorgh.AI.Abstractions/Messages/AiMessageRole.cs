namespace Simorgh.AI.Abstractions.Messages;

/// <summary>
///     The author of an <see cref="AiMessage" /> in a Simorgh.AI conversation.
/// </summary>
public enum AiMessageRole
{
    /// <summary>Instructions that shape agent behavior.</summary>
    System,

    /// <summary>Input originating from the end user.</summary>
    User,

    /// <summary>Output produced by an agent (assistant).</summary>
    Agent,

    /// <summary>Result of a tool invocation, returned to the agent.</summary>
    Tool
}