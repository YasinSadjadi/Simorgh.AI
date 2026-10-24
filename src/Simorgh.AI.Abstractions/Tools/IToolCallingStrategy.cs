using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;

namespace Simorgh.AI.Abstractions.Tools;

/// <summary>
///     Controls how an agent performs tool calling: which tools are advertised to the model
///     in each round, and how the calls the model makes are resolved into conversation
///     messages. Any tool-use pattern can be built on this contract — instant loading of
///     every tool, lazy/on-demand loading via meta-tools, code-execution styles where the
///     model composes tool usage itself, or an entirely custom scheme of your own.
///     Per-run state can be kept in <see cref="AgentRunContext.Items" />.
/// </summary>
public interface IToolCallingStrategy
{
    /// <summary>
    ///     Returns the tools to advertise to the model for the given round (0-based).
    ///     <paramref name="availableTools" /> is everything registered; a strategy may
    ///     return all of it, a subset, meta-tools of its own, or nothing.
    /// </summary>
    Task<IReadOnlyList<AiToolDeclaration>> GetToolDeclarationsAsync(
        IReadOnlyList<AiToolDeclaration> availableTools,
        AgentRunContext context,
        int round,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Handles the tool calls the model made in the last round and returns the messages
    ///     to append to the conversation (e.g. a tool message with results, a synthetic
    ///     assistant note, or nothing at all). The default execution path — registry lookup
    ///     plus the middleware tool chain — is provided as <paramref name="invokeNext" />;
    ///     a strategy may call it per tool, batch through it, or bypass it entirely and
    ///     produce results on its own.
    /// </summary>
    Task<IReadOnlyList<AiMessage>> InvokeToolsAsync(
        AgentRunContext context,
        IReadOnlyList<AiToolCallContent> calls,
        Func<AiToolCallContent, CancellationToken, Task<AiToolResultContent>> invokeNext,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     How many tool rounds a single run may take before failing.
    ///     Strategies with their own loop control can raise or remove this limit.
    /// </summary>
    int MaxToolRounds { get; }
}
