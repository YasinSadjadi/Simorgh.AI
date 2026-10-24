using Simorgh.AI.Abstractions.Agent;
using Simorgh.AI.Abstractions.Contents;
using Simorgh.AI.Abstractions.Messages;
using Simorgh.AI.Abstractions.Tools;

namespace Simorgh.AI.Extensions.Tools;

/// <summary>
///     The classic tool-calling strategy: every registered tool is advertised to the model
///     in every round, and each call the model makes is executed in order through the
///     default execution path and returned as a single tool message.
/// </summary>
public sealed class InstantToolCallingStrategy : IToolCallingStrategy
{
    /// <inheritdoc />
    public int MaxToolRounds => 16;

    /// <inheritdoc />
    public Task<IReadOnlyList<AiToolDeclaration>> GetToolDeclarationsAsync(
        IReadOnlyList<AiToolDeclaration> availableTools,
        AgentRunContext context,
        int round,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(availableTools);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiMessage>> InvokeToolsAsync(
        AgentRunContext context,
        IReadOnlyList<AiToolCallContent> calls,
        Func<AiToolCallContent, CancellationToken, Task<AiToolResultContent>> invokeNext,
        CancellationToken cancellationToken = default)
    {
        var results = new List<AiToolResultContent>(calls.Count);
        foreach (var call in calls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await invokeNext(call, cancellationToken).ConfigureAwait(false));
        }

        return
        [
            new AiMessage(AiMessageRole.Tool, [.. results]),
        ];
    }
}
